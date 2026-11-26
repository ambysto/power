using System;

namespace Ambysto.Power.Core.Alerts;

public enum PowerAlertKind
{
    BatteryHealthLow,
    CycleMilestone,
    ThrottlingDuration,
    AppHighOnBattery,
    SessionEndedByGuardRail,
    CapQuestionDue
}

public sealed record PowerAlertSettings(
    bool HealthAlertEnabled = false,
    double HealthThresholdPercent = 80.0,
    bool CycleMilestoneAlertEnabled = false,
    bool ThrottlingAlertEnabled = false,
    TimeSpan ThrottlingThreshold = default,
    bool AppHighOnBatteryAlertEnabled = false,
    double AppHighShareThresholdPercent = 50.0,
    TimeSpan AppHighDurationThreshold = default,
    bool SessionEndedByGuardRailAlertEnabled = false,
    bool CapQuestionAlertEnabled = false);

public sealed record TriggeredPowerAlert(
    PowerAlertKind Kind,
    string SubjectKey,
    string Title,
    string Body,
    DateTimeOffset TriggeredAtUtc);
