using System;
using System.Globalization;
using System.Linq;
using Ambysto.Power.Core.Apps;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.KeepAwake;
using Ambysto.Power.Core.Thermal;
using Microsoft.UI.Xaml;

namespace Ambysto.Power.App.Windows;

public sealed partial class PopoverWindow : Window
{
    private readonly Action<ManualSessionSpec> _startManualSession;
    private readonly Action _stopManualSession;
    private readonly Action _openMainWindow;

    public PopoverWindow(
        Action<ManualSessionSpec> startManualSession,
        Action stopManualSession,
        Action openMainWindow)
    {
        _startManualSession = startManualSession;
        _stopManualSession = stopManualSession;
        _openMainWindow = openMainWindow;
        InitializeComponent();
        AppWindow.Resize(new global::Windows.Graphics.SizeInt32(360, 480));
    }

    public void RefreshView(
        BatterySnapshot? battery,
        AppPowerSnapshot? apps,
        ThermalSnapshot? thermal,
        KeepAwakeSnapshot? keepAwake)
    {
        if (battery is null || !battery.Current.HasBattery)
        {
            ChargeHeadlineText.Text = "Mains · No battery";
            HealthAndCyclesText.Text = battery?.NoBatteryMessage ?? "This PC has no battery; running on mains power.";
            DischargeWattsText.Visibility = Visibility.Collapsed;
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
            ChargeHeadlineText.Text = $"{pct} · {battery.Current.State} ({battery.TimeRemainingText})";
            HealthAndCyclesText.Text = $"Health: {healthStr} · Cycles: {cycleStr}";

            if (battery.DischargeWatts is double w)
            {
                DischargeWattsText.Text = string.Create(CultureInfo.InvariantCulture, $"{w:0.0} W on battery");
                DischargeWattsText.Visibility = Visibility.Visible;
            }
            else
            {
                DischargeWattsText.Visibility = Visibility.Collapsed;
            }
        }

        if (apps is { Rows.Count: > 0 })
        {
            TopAppsText.Text = string.Join(
                " · ",
                apps.Rows.Take(3).Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.DisplayName} ({r.SharePercent:0.#} %)")));
        }
        else
        {
            TopAppsText.Text = "No app activity measured yet";
        }

        if (thermal is not null)
        {
            SensorFamilySummary? cpu = thermal.SensorSummaries.FirstOrDefault(s => s.Family == SensorFamily.Cpu);
            string sensorPart = cpu?.DisplayText ?? thermal.SensorsStatusText;
            ThermalLineText.Text = $"{thermal.State} · {sensorPart}";
        }

        if (keepAwake is not null)
        {
            if (keepAwake.ActiveSession is ActiveKeepAwakeSession session)
            {
                KeepAwakeStatusText.Text = $"Active: {session.Title} ({(session.Kind == AssertionKind.SystemAndDisplay ? "PC + display" : "PC only")})";
            }
            else
            {
                KeepAwakeStatusText.Text = "Inactive";
            }

            if (keepAwake.LastEndedNotice is SessionEndedNotice notice)
            {
                EndedNoticeText.Text = notice.DisplayText;
                EndedNoticeText.Visibility = Visibility.Visible;
            }
            else
            {
                EndedNoticeText.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void OnStartIndefiniteClick(object sender, RoutedEventArgs e)
    {
        bool displayOn = KeepDisplayOnCheck.IsChecked ?? true;
        _startManualSession(new ManualSessionSpec("Manual", ManualSessionMode.UntilTurnedOff, displayOn));
    }

    private void OnStart30MinClick(object sender, RoutedEventArgs e)
    {
        bool displayOn = KeepDisplayOnCheck.IsChecked ?? true;
        _startManualSession(new ManualSessionSpec("30 min", ManualSessionMode.ForDuration, displayOn, TimeSpan.FromMinutes(30)));
    }

    private void OnStart1HourClick(object sender, RoutedEventArgs e)
    {
        bool displayOn = KeepDisplayOnCheck.IsChecked ?? true;
        _startManualSession(new ManualSessionSpec("1 hour", ManualSessionMode.ForDuration, displayOn, TimeSpan.FromHours(1)));
    }

    private void OnStart2HoursClick(object sender, RoutedEventArgs e)
    {
        bool displayOn = KeepDisplayOnCheck.IsChecked ?? true;
        _startManualSession(new ManualSessionSpec("2 hours", ManualSessionMode.ForDuration, displayOn, TimeSpan.FromHours(2)));
    }

    private void OnStopManualClick(object sender, RoutedEventArgs e) => _stopManualSession();

    private void OnOpenMainWindowClick(object sender, RoutedEventArgs e) => _openMainWindow();
}
