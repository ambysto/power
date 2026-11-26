using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Ambysto.Kit.Processes;
using Ambysto.Power.App.Tray;
using Ambysto.Power.Core.Alerts;
using Ambysto.Power.Core.Apps;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.History;
using Ambysto.Power.Core.KeepAwake;
using Ambysto.Power.Core.Licensing;
using Ambysto.Power.Core.Suite;
using Ambysto.Power.Core.Thermal;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Ambysto.Power.App.Windows;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly ExecutionStateAssertion _assertion;
    private readonly WindowsBatteryReader _batteryReader;
    private readonly BatteryMonitor _batteryMonitor;
    private readonly ProcessSampler _processSampler;
    private readonly PowerEstimator _powerEstimator;
    private readonly WindowsThermalReader _thermalReader;
    private readonly ThermalEpisodeTracker _thermalTracker;
    private readonly KeepAwakeController _keepAwakeController;
    private readonly HistoryStore _historyStore;
    private readonly EntitlementService _entitlementService;
    // The Pro features come through one seam. The public build has none; a release build adds the private module.
    private readonly IProFeatures _proFeatures = CreateProFeatures();
    private readonly IPowerAlertEvaluator? _alertEvaluator;
    private readonly IKeepAwakeRuleMatcher? _ruleMatcher;
    private readonly LoginItem _loginItem;
    private readonly UpdateChecker _updateChecker;
    private readonly SuiteGlanceWriter _glanceWriter;
    private readonly TrayIconController _trayController;
    private readonly DispatcherQueueTimer _pollTimer;

    private PopoverWindow? _popoverWindow;
    private PowerWindowKind _selectedAppWindow = PowerWindowKind.Last5Minutes;
    private DateTimeOffset _lastAppFlushUtc = DateTimeOffset.UtcNow;
    private bool _deleteConfirmPending;
    private DispatcherQueueTimer? _quitAfterTimer;

    public MainWindow()
    {
        InitializeComponent();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(980, 700));

        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AmbystoPower.ico");
        if (File.Exists(iconPath))
        {
            try
            {
                AppWindow.SetIcon(iconPath);
                AboutLogoImage.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
            }
            catch (Exception)
            {
            }
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string powerDataDir = Path.Combine(localAppData, "Ambysto", "Power");
        string historyDir = Path.Combine(powerDataDir, "history");
        string suiteDir = !string.IsNullOrWhiteSpace(Program.SuiteFolderOption)
            ? Program.SuiteFolderOption
            : Path.Combine(localAppData, "Ambysto", "Suite");

        _historyStore = new HistoryStore(historyDir);
        _entitlementService = new EntitlementService(
            powerDataDir,
            PowerLicenseConfig.Instance,
            devProOverride: Program.DevProFlag,
            purchaseUrl: null);

        DateOnly todayLocal = DateOnly.FromDateTime(DateTime.Now);
        _ = _historyStore.RunLaunchRetention(
            todayLocal,
            _entitlementService.Current.IsPro,
            _entitlementService.Current.HasStoredKeyText);

        DateOnly minDate = _entitlementService.Current.IsPro
            ? DateOnly.MinValue
            : todayLocal.AddDays(-HistoryStore.FreeRetentionDays);

        _assertion = new ExecutionStateAssertion();
        _batteryReader = new WindowsBatteryReader();
        _batteryMonitor = new BatteryMonitor(
            _batteryReader,
            _historyStore.ReadBatterySamples(minDate, todayLocal),
            _historyStore.ReadDailyHealthPoints());

        _processSampler = new ProcessSampler();
        _powerEstimator = new PowerEstimator(_historyStore.ReadAppPowerReadings(minDate, todayLocal));
        _thermalReader = new WindowsThermalReader();
        _thermalTracker = new ThermalEpisodeTracker(_historyStore.ReadThermalEpisodes(minDate, todayLocal));
        _ruleMatcher = _proFeatures.CreateRuleMatcher();
        _keepAwakeController = new KeepAwakeController(_assertion, ruleMatcher: _ruleMatcher);
        _alertEvaluator = _proFeatures.CreateAlertEvaluator();
        _loginItem = new LoginItem(Environment.ProcessPath ?? "AmbystoPower.exe");
        _updateChecker = new UpdateChecker();
        _glanceWriter = new SuiteGlanceWriter(suiteDir);
        _trayController = new TrayIconController();

        Closed += OnMainWindowClosed;

        _pollTimer = DispatcherQueue.CreateTimer();
        _pollTimer.Interval = TimeSpan.FromSeconds(15);
        _pollTimer.Tick += (_, _) => RunPollCycle();

        RunPollCycle();
        _pollTimer.Start();

        if (!string.IsNullOrWhiteSpace(Program.OpenTabOption))
        {
            switch (Program.OpenTabOption.Trim().ToLowerInvariant())
            {
                case "battery":
                    ShowTab(BatteryPanel);
                    break;
                case "apps":
                    ShowTab(AppsPanel);
                    break;
                case "thermal":
                    ShowTab(ThermalPanel);
                    break;
                case "awake":
                case "keep-awake":
                    ShowTab(KeepAwakePanel);
                    break;
                case "settings":
                    ShowTab(SettingsPanel);
                    break;
                case "about":
                    ShowTab(AboutPanel);
                    break;
            }
        }

        if (Program.QuitAfterSecondsOption is int quitSeconds && quitSeconds > 0)
        {
            _quitAfterTimer = DispatcherQueue.CreateTimer();
            _quitAfterTimer.Interval = TimeSpan.FromSeconds(quitSeconds);
            _quitAfterTimer.IsRepeating = false;
            _quitAfterTimer.Tick += (_, _) => Close();
            _quitAfterTimer.Start();
        }
    }

    private void RunPollCycle()
    {
        DateTimeOffset nowLocal = DateTimeOffset.Now;
        DateTimeOffset nowUtc = nowLocal.ToUniversalTime();
        DateOnly todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);
        bool isPro = _entitlementService.Current.IsPro;
        // Alerts and keep-awake rules need the private module too; a key alone does not unlock them in a build without it.
        bool proFeaturesOn = isPro && _proFeatures.Available;

        _assertion.CheckWatchdog(nowUtc);

        (BatterySnapshot batterySnap, BatterySamplePoint? writtenBattery, DailyHealthPoint? writtenDaily) =
            _batteryMonitor.Tick(nowLocal);

        if (writtenBattery is not null)
        {
            _historyStore.AppendBatterySample(writtenBattery, todayLocal, isPro);
        }

        if (writtenDaily is not null)
        {
            _historyStore.AppendDailyHealthPoint(writtenDaily, isPro);
        }

        ProcessSample procSample = _processSampler.Sample();
        _powerEstimator.RecordSample(procSample, nowUtc);
        if (nowUtc - _lastAppFlushUtc >= TimeSpan.FromMinutes(5))
        {
            _lastAppFlushUtc = nowUtc;
            AppPowerReading? flushedAppReading = _powerEstimator.FlushFiveMinuteReading(nowUtc);
            if (flushedAppReading is not null)
            {
                _historyStore.AppendAppPowerReading(flushedAppReading, todayLocal, isPro);
            }
        }

        AppPowerSnapshot appSnap = _powerEstimator.GetSnapshot(
            _selectedAppWindow,
            nowUtc,
            _batteryMonitor.LastUnpluggedAtUtc);

        ThermalReading thermalReading = _thermalReader.ReadThermal();
        string[] topAppNames = appSnap.Rows.Take(3).Select(r => r.DisplayName).ToArray();
        (ThermalSnapshot thermalSnap, ThermalEpisode? closedEpisode) = _thermalTracker.Update(
            thermalReading,
            topAppNames,
            batterySnap.Current.TemperatureCelsius);

        if (closedEpisode is not null)
        {
            _historyStore.AppendThermalEpisode(closedEpisode, todayLocal, isPro);
        }

        List<ProcessRecord> readableProcs = procSample.AllProcesses.Where(p => p.IsReadable).ToList();
        KeepAwakeSnapshot keepAwakeSnap = _keepAwakeController.EvaluateTick(
            nowLocal,
            batterySnap.Current.PowerSource,
            batterySnap.Current.ChargePercent,
            thermalSnap.State,
            readableProcs,
            proFeaturesOn);

        _ = _alertEvaluator?.Evaluate(
            nowUtc,
            proFeaturesOn,
            batterySnap.HealthPercent,
            batterySnap.Current.CycleCount,
            batterySnap.Current.PowerSource,
            thermalSnap.OpenEpisode,
            appSnap.Rows,
            keepAwakeSnap.LastEndedNotice,
            keepAwakeSnap.ActiveSession?.IsCapQuestionVisible ?? false);

        _glanceWriter.WriteGlance(
            batterySnap.Current,
            thermalSnap.State,
            keepAwakeSnap.ActiveSession is not null,
            nowUtc);

        _trayController.Update(
            batterySnap,
            keepAwakeSnap,
            _alertEvaluator?.IsTrayIconMarkedForRefusedPermission ?? false);

        UpdateUi(batterySnap, appSnap, thermalSnap, keepAwakeSnap);
        _popoverWindow?.RefreshView(batterySnap, appSnap, thermalSnap, keepAwakeSnap);
    }

    private void UpdateUi(
        BatterySnapshot battery,
        AppPowerSnapshot apps,
        ThermalSnapshot thermal,
        KeepAwakeSnapshot keepAwake)
    {
        bool isPro = _entitlementService.Current.IsPro;
        TierBadgeText.Text = isPro ? "PRO" : "FREE";
        TrayStatusText.Text = _trayController.StatusBadgeText;

        // Battery tab
        if (!battery.Current.HasBattery)
        {
            BatteryPrimaryHeadline.Text = "Mains · Desktop / No Battery";
            BatteryDetailsLine.Text = battery.NoBatteryMessage ?? "This PC has no battery; running on mains power.";
            BatterySinceUnpluggedLine.Visibility = Visibility.Collapsed;
            BatteryRecordStartedLine.Text = string.Empty;
        }
        else
        {
            string pct = battery.Current.ChargePercent is int p
                ? string.Create(CultureInfo.InvariantCulture, $"{p} %")
                : "—";
            string healthStr = battery.HealthPercent is double hp
                ? string.Create(CultureInfo.InvariantCulture, $"{hp:0.#} %")
                : "not reported";
            string cycleStr = battery.Current.CycleCount is int c
                ? c.ToString(CultureInfo.InvariantCulture)
                : "not reported";
            string condStr = battery.Current.Condition ?? "not reported";
            string tempStr = battery.Current.TemperatureCelsius is double t
                ? string.Create(CultureInfo.InvariantCulture, $"{t:0.#} °C")
                : "not reported";
            BatteryPrimaryHeadline.Text = $"{pct} · {battery.Current.State} · Remaining: {battery.TimeRemainingText}";
            BatteryDetailsLine.Text = $"Health: {healthStr} · Cycles: {cycleStr} · Condition: {condStr} · Temp: {tempStr}";

            if (battery.SinceUnplugged is UnplugSummary unp)
            {
                BatterySinceUnpluggedLine.Text = unp.DisplayText;
                BatterySinceUnpluggedLine.Visibility = Visibility.Visible;
            }
            else
            {
                BatterySinceUnpluggedLine.Visibility = Visibility.Collapsed;
            }

            BatteryRecordStartedLine.Text = battery.RecordStartedText ?? string.Empty;
        }

        BatteryHistorySummaryText.Text = string.Create(
            CultureInfo.InvariantCulture,
            $"Daily health points: {battery.DailyHealthPoints.Count} · 24h samples: {battery.SamplesLast24Hours.Count}");

        // Apps tab
        AppsHeaderNotesText.Text = $"Column: {apps.ColumnHeader} · {apps.GpuNote} · {apps.UnmeasuredNote}";
        if (apps.Rows.Count == 0)
        {
            AppsTableText.Text = "No readable process activity recorded in this window yet.";
        }
        else
        {
            AppsTableText.Text = string.Join(
                Environment.NewLine,
                apps.Rows.Take(15).Select(r => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{r.SharePercent,6:0.0} %   {r.DisplayName} ({r.ProcessCount} proc, {r.CpuSeconds:0.1} s CPU)")));
        }

        // Thermal tab
        ThermalStateHeadlineText.Text = thermal.State.ToString();
        ThermalDetailsText.Text = $"CPU speed limit: {thermal.CpuSpeedLimitText} · Warning level: {thermal.ThermalWarningLevelText}";
        SensorFamiliesText.Text = string.Join(
            Environment.NewLine,
            thermal.SensorSummaries.Select(s => s.DisplayText));
        ThermalEpisodesText.Text = thermal.CompletedEpisodes.Count == 0
            ? "No throttling episodes recorded."
            : string.Join(Environment.NewLine, thermal.CompletedEpisodes.Select(e => e.SummaryText));

        // Keep-Awake tab
        if (keepAwake.ActiveSession is ActiveKeepAwakeSession active)
        {
            KeepAwakeHeadlineText.Text = $"Active: {active.Title} ({(active.Kind == AssertionKind.SystemAndDisplay ? "PC + display" : "PC only")})";
            CapQuestionBanner.Visibility = active.IsCapQuestionVisible ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            KeepAwakeHeadlineText.Text = "Inactive";
            CapQuestionBanner.Visibility = Visibility.Collapsed;
        }

        if (keepAwake.LastEndedNotice is SessionEndedNotice notice)
        {
            KeepAwakeLastNoticeText.Text = notice.DisplayText;
            KeepAwakeLastNoticeText.Visibility = Visibility.Visible;
        }
        else
        {
            KeepAwakeLastNoticeText.Visibility = Visibility.Collapsed;
        }

        RulesPlainEnglishText.Text = keepAwake.Rules.Count == 0
            ? "No rules saved."
            : string.Join(Environment.NewLine, keepAwake.Rules.Select(r => _ruleMatcher?.DescribeRuleInWords(r) ?? r.Title));

        FirstRuleLoginBanner.Visibility = keepAwake.ShouldPromptLoginItemOnFirstRule
            ? Visibility.Visible
            : Visibility.Collapsed;

        SleepRequestsText.Text = keepAwake.SleepRequests.Count == 0
            ? "No active sleep assertions."
            : string.Join(Environment.NewLine, keepAwake.SleepRequests.Select(s => $"{s.AppName} — {s.KindText} ({s.ReasonText})"));

        // Settings & Pro tab
        LicenseStatusText.Text = _entitlementService.Current.StatusMessage ?? "Ambysto Power Free";
        PruneEvaluation retention = _historyStore.EvaluateRetention(
            DateOnly.FromDateTime(DateTime.Now),
            isPro,
            _entitlementService.Current.HasStoredKeyText);

        RetentionNoteText.Text = retention.OlderDataHiddenInFree
            ? $"Older history ({retention.EligibleFileNamesOlderThan7Days.Count} day files) is kept on disk and hidden in Free."
            : isPro
                ? "Pro keeps full history on disk."
                : "Free keeps 7 days of history.";

        ToggleLoginItemButton.Content = _loginItem.IsOn
            ? "Turn Off Start at Login"
            : "Turn On Start at Login";
    }

    private void OnShowBatteryTabClick(object sender, RoutedEventArgs e) => ShowTab(BatteryPanel);
    private void OnShowAppsTabClick(object sender, RoutedEventArgs e) => ShowTab(AppsPanel);
    private void OnShowThermalTabClick(object sender, RoutedEventArgs e) => ShowTab(ThermalPanel);
    private void OnShowKeepAwakeTabClick(object sender, RoutedEventArgs e) => ShowTab(KeepAwakePanel);
    private void OnShowSettingsTabClick(object sender, RoutedEventArgs e)
    {
        _alertEvaluator?.ClearPermissionRefusedMarkOnWindowOpen();
        ShowTab(SettingsPanel);
    }

    private void OnShowAboutTabClick(object sender, RoutedEventArgs e) => ShowTab(AboutPanel);

    private async void OnStarOnGitHubClick(object sender, RoutedEventArgs e)
    {
        _ = await SystemLauncher.OpenHttpsAsync(new Uri("https://github.com/ambysto/power"));
    }

    private async void OnOpenWebsiteClick(object sender, RoutedEventArgs e)
    {
        _ = await SystemLauncher.OpenHttpsAsync(new Uri("https://power.ambysto.com/"));
    }

    private async void OnBuyCoffeeClick(object sender, RoutedEventArgs e)
    {
        _ = await SystemLauncher.OpenHttpsAsync(new Uri("https://buymeacoffee.com/ambysto"));
    }

    private void ShowTab(UIElement activePanel)
    {
        BatteryPanel.Visibility = activePanel == BatteryPanel ? Visibility.Visible : Visibility.Collapsed;
        AppsPanel.Visibility = activePanel == AppsPanel ? Visibility.Visible : Visibility.Collapsed;
        ThermalPanel.Visibility = activePanel == ThermalPanel ? Visibility.Visible : Visibility.Collapsed;
        KeepAwakePanel.Visibility = activePanel == KeepAwakePanel ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = activePanel == SettingsPanel ? Visibility.Visible : Visibility.Collapsed;
        AboutPanel.Visibility = activePanel == AboutPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnAppWindow5MinClick(object sender, RoutedEventArgs e)
    {
        _selectedAppWindow = PowerWindowKind.Last5Minutes;
        RunPollCycle();
    }

    private void OnAppWindow1HourClick(object sender, RoutedEventArgs e)
    {
        _selectedAppWindow = PowerWindowKind.LastHour;
        RunPollCycle();
    }

    private void OnAppWindowSinceUnpluggedClick(object sender, RoutedEventArgs e)
    {
        _selectedAppWindow = PowerWindowKind.SinceUnplugged;
        RunPollCycle();
    }

    private void OnManualIndefiniteClick(object sender, RoutedEventArgs e) =>
        StartManualFromUi(new ManualSessionSpec("Manual", ManualSessionMode.UntilTurnedOff, MainKeepDisplayOnCheck.IsChecked ?? true));

    private void OnManual30MinClick(object sender, RoutedEventArgs e) =>
        StartManualFromUi(new ManualSessionSpec("30 min", ManualSessionMode.ForDuration, MainKeepDisplayOnCheck.IsChecked ?? true, TimeSpan.FromMinutes(30)));

    private void OnManual1HourClick(object sender, RoutedEventArgs e) =>
        StartManualFromUi(new ManualSessionSpec("1 hour", ManualSessionMode.ForDuration, MainKeepDisplayOnCheck.IsChecked ?? true, TimeSpan.FromHours(1)));

    private void OnManual2HoursClick(object sender, RoutedEventArgs e) =>
        StartManualFromUi(new ManualSessionSpec("2 hours", ManualSessionMode.ForDuration, MainKeepDisplayOnCheck.IsChecked ?? true, TimeSpan.FromHours(2)));

    private void OnManual4HoursClick(object sender, RoutedEventArgs e) =>
        StartManualFromUi(new ManualSessionSpec("4 hours", ManualSessionMode.ForDuration, MainKeepDisplayOnCheck.IsChecked ?? true, TimeSpan.FromHours(4)));

    private void OnManualStopClick(object sender, RoutedEventArgs e)
    {
        _keepAwakeController.StopManualSession(DateTimeOffset.Now);
        RunPollCycle();
    }

    private void StartManualFromUi(ManualSessionSpec spec)
    {
        _keepAwakeController.StartManualSession(spec, DateTimeOffset.Now);
        RunPollCycle();
    }

    private void OnCapAnswerYesClick(object sender, RoutedEventArgs e)
    {
        _keepAwakeController.AnswerCapQuestion(keepAwake: true, DateTimeOffset.Now);
        RunPollCycle();
    }

    private void OnCapAnswerNoClick(object sender, RoutedEventArgs e)
    {
        _keepAwakeController.AnswerCapQuestion(keepAwake: false, DateTimeOffset.Now);
        RunPollCycle();
    }

    private void OnSaveAppRuleClick(object sender, RoutedEventArgs e)
    {
        if (!_entitlementService.Current.IsPro)
        {
            RulesPlainEnglishText.Text = "Automatic keep-awake rules require Ambysto Power Pro.";
            return;
        }

        // The rule matcher lives in the private module; a key alone does not unlock rules in a build without it.
        if (!_proFeatures.Available)
        {
            RulesPlainEnglishText.Text = "Automatic keep-awake rules are not part of this build.";
            return;
        }

        string target = string.IsNullOrWhiteSpace(RuleAppInputBox.Text) ? "Code.exe" : RuleAppInputBox.Text.Trim();
        var rule = new KeepAwakeRule(
            Id: "rule-default",
            Title: target,
            Kind: KeepAwakeRuleKind.WhileAppRuns,
            Enabled: true,
            DisplayOn: MainKeepDisplayOnCheck.IsChecked ?? true,
            AppNameOrPath: target,
            IncludeHelpers: RuleHelpersCheck.IsChecked ?? false);

        _keepAwakeController.AddOrUpdateRule(rule, isPro: true);
        RunPollCycle();
    }

    private void OnEnableLoginFromBannerClick(object sender, RoutedEventArgs e)
    {
        _loginItem.TurnOn();
        _keepAwakeController.AcknowledgeFirstRuleLoginPrompt();
        RunPollCycle();
    }

    private void OnDismissLoginBannerClick(object sender, RoutedEventArgs e)
    {
        _keepAwakeController.AcknowledgeFirstRuleLoginPrompt();
        RunPollCycle();
    }

    private void OnActivateLicenseClick(object sender, RoutedEventArgs e)
    {
        _ = _entitlementService.ActivateKey(LicenseKeyInputBox.Text);
        RunPollCycle();
    }

    private void OnDeactivateLicenseClick(object sender, RoutedEventArgs e)
    {
        _entitlementService.DeactivateKey();
        LicenseKeyInputBox.Text = string.Empty;
        RunPollCycle();
    }

    private void OnDeleteOldHistoryClick(object sender, RoutedEventArgs e)
    {
        DateOnly todayLocal = DateOnly.FromDateTime(DateTime.Now);
        PruneEvaluation eval = _historyStore.EvaluateRetention(
            todayLocal,
            _entitlementService.Current.IsPro,
            _entitlementService.Current.HasStoredKeyText);

        if (eval.EligibleFileNamesOlderThan7Days.Count == 0)
        {
            _deleteConfirmPending = false;
            SettingsActionFeedbackText.Text = "No day files older than 7 days to delete.";
            return;
        }

        if (!_deleteConfirmPending)
        {
            _deleteConfirmPending = true;
            string filesList = string.Join(", ", eval.EligibleFileNamesOlderThan7Days);
            SettingsActionFeedbackText.Text = $"Click again to confirm deleting: {filesList}";
            return;
        }

        _deleteConfirmPending = false;
        IReadOnlyList<string> deleted = _historyStore.DeleteHistoryOlderThan7Days(todayLocal);
        SettingsActionFeedbackText.Text = string.Create(CultureInfo.InvariantCulture, $"Deleted {deleted.Count} day file(s) older than 7 days.");
        RunPollCycle();
    }

    private void OnToggleLoginItemClick(object sender, RoutedEventArgs e)
    {
        if (_loginItem.IsOn)
        {
            _loginItem.TurnOff();
        }
        else
        {
            _loginItem.TurnOn();
        }

        RunPollCycle();
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        Version installed = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 1, 1);
        UpdateResult res = await _updateChecker.CheckAsync(installed, CancellationToken.None);
        string msg = res.State switch
        {
            UpdateState.Current => "You are running the latest version.",
            UpdateState.Available => $"Update {res.Version} is available.",
            _ => "Could not check for updates right now."
        };
        SettingsActionFeedbackText.Text = msg;
        UpdateFeedbackText.Text = msg;
    }

    private void OnOpenPopoverClick(object sender, RoutedEventArgs e)
    {
        _keepAwakeController.MarkPopoverOpened();
        if (_popoverWindow is null)
        {
            _popoverWindow = new PopoverWindow(
                StartManualFromUi,
                () =>
                {
                    _keepAwakeController.StopManualSession(DateTimeOffset.Now);
                    RunPollCycle();
                },
                Activate);
            _popoverWindow.Closed += (_, _) => _popoverWindow = null;
        }

        _popoverWindow.Activate();
        RunPollCycle();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        Dispose();
    }

    public void Dispose()
    {
        _quitAfterTimer?.Stop();
        _pollTimer.Stop();
        _keepAwakeController.Shutdown(DateTimeOffset.Now);
        _keepAwakeController.Dispose();
        _assertion.Dispose();
        _thermalReader.Dispose();
        _updateChecker.Dispose();
        _trayController.Dispose();
        _popoverWindow?.Close();
    }

#pragma warning disable CA1859 // the seam is the interface on purpose: the module's type exists in only one of the two builds
    private static IProFeatures CreateProFeatures()
#pragma warning restore CA1859
    {
#if POWER_PRO
        return new Ambysto.Power.Pro.ProModule();
#else
        return NoProFeatures.Instance;
#endif
    }
}
