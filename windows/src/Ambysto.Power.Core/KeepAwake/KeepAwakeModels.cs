using System;
using System.Collections.Generic;

namespace Ambysto.Power.Core.KeepAwake;

public enum AssertionKind
{
    SystemOnly,
    SystemAndDisplay
}

public enum ManualSessionMode
{
    UntilTurnedOff,
    ForDuration,
    UntilTime
}

public enum KeepAwakeRuleKind
{
    WhileAppRuns,
    WhileTaskRuns,
    TimeWindow
}

public enum SessionEndReason
{
    UserClickedEnd,
    ReplacedByNewSession,
    DurationExpired,
    RuleStoppedMatching,
    BatteryFloorReached,
    CriticalThermalReached,
    CapReachedUnanswered,
    CapPromptDeclined,
    AppQuitting
}

public sealed record ManualSessionSpec(
    string Title,
    ManualSessionMode Mode,
    bool DisplayOn,
    TimeSpan? Duration = null,
    DateTimeOffset? UntilUtc = null);

public sealed record KeepAwakeRule(
    string Id,
    string Title,
    KeepAwakeRuleKind Kind,
    bool Enabled,
    bool DisplayOn,
    string? AppNameOrPath = null,
    bool IncludeHelpers = false,
    Ambysto.Kit.Processes.ProcessIdentity? TargetTaskIdentity = null,
    string? TargetTaskNameOrPath = null,
    IReadOnlySet<DayOfWeek>? Weekdays = null,
    TimeOnly? WindowStartLocal = null,
    TimeOnly? WindowEndLocal = null,
    int BatteryFloorPercent = KeepAwakeController.DefaultBatteryFloorPercent,
    TimeSpan CapDuration = default);

public sealed record ActiveKeepAwakeSession(
    string SessionId,
    string Title,
    bool IsManual,
    string? RuleId,
    AssertionKind Kind,
    string AssertionName,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CapDeadlineUtc,
    DateTimeOffset? ManualDeadlineUtc,
    int BatteryFloorPercent,
    bool IsCapQuestionVisible);

public sealed record SessionEndedNotice(
    SessionEndReason Reason,
    DateTimeOffset EndedAtLocal,
    string DisplayText);

public sealed record SleepRequestEntry(
    string AppName,
    string KindText,
    DateTimeOffset? SinceUtc,
    string ReasonText,
    bool IsOwnAssertion);

public sealed record KeepAwakeSnapshot(
    ActiveKeepAwakeSession? ActiveSession,
    SessionEndedNotice? LastEndedNotice,
    bool IsTrayIconMarked,
    bool ShouldPromptLoginItemOnFirstRule,
    IReadOnlyList<KeepAwakeRule> Rules,
    IReadOnlyList<SleepRequestEntry> SleepRequests);
