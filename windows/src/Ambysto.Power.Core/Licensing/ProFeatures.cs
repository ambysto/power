using System;
using System.Collections.Generic;
using Ambysto.Kit.Processes;
using Ambysto.Power.Core.Alerts;
using Ambysto.Power.Core.Apps;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.History;
using Ambysto.Power.Core.KeepAwake;
using Ambysto.Power.Core.Thermal;

namespace Ambysto.Power.Core.Licensing;

/// <summary>Decides which background alerts a refresh raises (docs/spec/05-free-pro.md, Pro). The real one is in the private module.</summary>
public interface IPowerAlertEvaluator
{
    /// <summary>The alerts the user switched on.</summary>
    PowerAlertSettings Settings { get; }

    /// <summary>True when the system refused to show notifications and the tray icon carries the mark.</summary>
    bool IsTrayIconMarkedForRefusedPermission { get; }

    /// <summary>Switches one alert on or off; the first switch-on asks for the notification permission.</summary>
    void SetAlertEnabled(PowerAlertKind kind, bool enabled);

    /// <summary>Clears the tray mark when the window opens.</summary>
    void ClearPermissionRefusedMarkOnWindowOpen();

    /// <summary>The alerts raised by this refresh; none when <paramref name="isPro"/> is false.</summary>
    IReadOnlyList<TriggeredPowerAlert> Evaluate(
        DateTimeOffset nowUtc,
        bool isPro,
        double? batteryHealthPercent,
        int? cycleCount,
        PowerSourceKind powerSource,
        ThermalEpisode? openEpisode,
        IReadOnlyList<AppPowerRow> currentAppShares,
        SessionEndedNotice? newGuardRailEndNotice = null,
        bool capQuestionJustBecameDue = false);
}

/// <summary>Decides whether a keep-awake rule holds right now, and says it in words (Pro). The real one is in the private module.</summary>
public interface IKeepAwakeRuleMatcher
{
    /// <summary>True when <paramref name="rule"/> is enabled and its condition (a running app or task, or a time window) holds.</summary>
    bool IsRuleMatching(KeepAwakeRule rule, IReadOnlyList<ProcessRecord> readableProcesses, DateTimeOffset nowLocal);

    /// <summary>The rule as one sentence.</summary>
    string DescribeRuleInWords(KeepAwakeRule rule);
}

/// <summary>The three Pro features, behind one seam: background alerts, keep-awake rules and export. The public build has
/// <see cref="NoProFeatures"/>; a release build adds the private module, which implements this. The licence check, the key box and
/// the controls a key unlocks stay in the public code and decide only whether the locked state shows.</summary>
public interface IProFeatures
{
    /// <summary>False when this build holds no Pro features.</summary>
    bool Available { get; }

    /// <summary>A background-alert evaluator; null when unavailable. <paramref name="notificationPermissionRequester"/> asks the system for permission.</summary>
    IPowerAlertEvaluator? CreateAlertEvaluator(Func<bool>? notificationPermissionRequester = null);

    /// <summary>The keep-awake rule matcher; null when unavailable.</summary>
    IKeepAwakeRuleMatcher? CreateRuleMatcher();

    /// <summary>The battery report as Markdown; null when unavailable.</summary>
    string? FormatBatteryReportMarkdown(
        string model,
        DateOnly? recordStartedDate,
        BatteryReading currentBattery,
        IReadOnlyList<DailyHealthPoint> dailyPoints,
        IReadOnlyList<DischargeSessionSummary> lastSevenDischargeSessions);

    /// <summary>The history as JSON; null when unavailable.</summary>
    string? FormatHistoryJson(
        IReadOnlyList<BatterySamplePoint> batterySamples,
        IReadOnlyList<DailyHealthPoint> dailyPoints,
        IReadOnlyList<ThermalEpisode> episodes,
        IReadOnlyList<AppPowerReading> appReadings);

    /// <summary>The history as CSV; null when unavailable.</summary>
    string? FormatHistoryCsv(
        IReadOnlyList<BatterySamplePoint> batterySamples,
        IReadOnlyList<DailyHealthPoint> dailyPoints,
        IReadOnlyList<ThermalEpisode> episodes,
        IReadOnlyList<AppPowerReading> appReadings);
}

/// <summary>The public build: no Pro feature is available, whatever the key says.</summary>
public sealed class NoProFeatures : IProFeatures
{
    /// <summary>The one instance.</summary>
    public static NoProFeatures Instance { get; } = new();

    private NoProFeatures()
    {
    }

    /// <inheritdoc />
    public bool Available => false;

    /// <inheritdoc />
    public IPowerAlertEvaluator? CreateAlertEvaluator(Func<bool>? notificationPermissionRequester = null) => null;

    /// <inheritdoc />
    public IKeepAwakeRuleMatcher? CreateRuleMatcher() => null;

    /// <inheritdoc />
    public string? FormatBatteryReportMarkdown(string model, DateOnly? recordStartedDate, BatteryReading currentBattery, IReadOnlyList<DailyHealthPoint> dailyPoints, IReadOnlyList<DischargeSessionSummary> lastSevenDischargeSessions) => null;

    /// <inheritdoc />
    public string? FormatHistoryJson(IReadOnlyList<BatterySamplePoint> batterySamples, IReadOnlyList<DailyHealthPoint> dailyPoints, IReadOnlyList<ThermalEpisode> episodes, IReadOnlyList<AppPowerReading> appReadings) => null;

    /// <inheritdoc />
    public string? FormatHistoryCsv(IReadOnlyList<BatterySamplePoint> batterySamples, IReadOnlyList<DailyHealthPoint> dailyPoints, IReadOnlyList<ThermalEpisode> episodes, IReadOnlyList<AppPowerReading> appReadings) => null;
}
