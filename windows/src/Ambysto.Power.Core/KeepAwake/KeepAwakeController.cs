using System;
using System.Collections.Generic;
using System.Globalization;
using Ambysto.Kit.Processes;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.Licensing;
using Ambysto.Power.Core.Thermal;

namespace Ambysto.Power.Core.KeepAwake;

public sealed class KeepAwakeController : IDisposable
{
    public const int DefaultBatteryFloorPercent = 20;
    public const int BatteryRestartHysteresisPercent = 5;
    public static readonly TimeSpan DefaultCapDuration = TimeSpan.FromHours(12);
    public static readonly TimeSpan CapQuestionLeadTime = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan UnansweredRuleRestGap = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan MaxAssertionTimeout = TimeSpan.FromMinutes(15);

    private readonly ISleepAssertion _assertion;
    private readonly List<KeepAwakeRule> _rules = [];
    private readonly Dictionary<string, int> _batteryBlockedRuleIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset?> _capRestingRules = new(StringComparer.Ordinal);

    private bool _hasEverSavedRule;
    private bool _shouldPromptLoginItemOnFirstRule;
    private bool _thermalBlocked;
    private bool _disposed;

    // The rule matcher is a Pro feature in the private module; without one no rule ever matches.
    private readonly IKeepAwakeRuleMatcher? _ruleMatcher;

    public KeepAwakeController(
        ISleepAssertion assertion,
        bool hasEverSavedRule = false,
        IKeepAwakeRuleMatcher? ruleMatcher = null)
    {
        ArgumentNullException.ThrowIfNull(assertion);
        _assertion = assertion;
        _hasEverSavedRule = hasEverSavedRule;
        _ruleMatcher = ruleMatcher;
    }

    public ActiveKeepAwakeSession? ActiveSession { get; private set; }
    public SessionEndedNotice? LastEndedNotice { get; private set; }
    public bool IsTrayIconMarked { get; private set; }
    public bool ShouldPromptLoginItemOnFirstRule => _shouldPromptLoginItemOnFirstRule;
    public IReadOnlyList<KeepAwakeRule> Rules => _rules;

    public void StartManualSession(ManualSessionSpec spec, DateTimeOffset nowLocal)
    {
        ArgumentNullException.ThrowIfNull(spec);

        DateTimeOffset startedUtc = nowLocal.ToUniversalTime();
        DateTimeOffset? manualDeadlineUtc = spec.Mode switch
        {
            ManualSessionMode.UntilTurnedOff => null,
            ManualSessionMode.ForDuration => startedUtc.Add(spec.Duration ?? TimeSpan.FromHours(1)),
            ManualSessionMode.UntilTime => spec.UntilUtc ?? startedUtc.AddHours(1),
            _ => null
        };

        DateTimeOffset capDeadlineUtc = startedUtc.Add(DefaultCapDuration);
        AssertionKind kind = spec.DisplayOn ? AssertionKind.SystemAndDisplay : AssertionKind.SystemOnly;
        string title = string.IsNullOrWhiteSpace(spec.Title) ? "Manual" : spec.Title.Trim();
        string assertionName = $"Ambysto Power: {title}";

        ActiveSession = new ActiveKeepAwakeSession(
            SessionId: Guid.NewGuid().ToString("N"),
            Title: title,
            IsManual: true,
            RuleId: null,
            Kind: kind,
            AssertionName: assertionName,
            StartedAtUtc: startedUtc,
            CapDeadlineUtc: capDeadlineUtc,
            ManualDeadlineUtc: manualDeadlineUtc,
            BatteryFloorPercent: DefaultBatteryFloorPercent,
            IsCapQuestionVisible: false);

        _assertion.Acquire(kind, assertionName, ComputeLeaseTimeout(ActiveSession, startedUtc));
    }

    public void StopManualSession(DateTimeOffset nowLocal)
    {
        if (ActiveSession is { IsManual: true })
        {
            EndActiveSession(SessionEndReason.UserClickedEnd, nowLocal, markTray: false, recordNotice: false, floorPercent: null);
        }
    }

    public void AddOrUpdateRule(KeepAwakeRule rule, bool isPro)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (!isPro)
        {
            return;
        }

        int existingIndex = _rules.FindIndex(r => string.Equals(r.Id, rule.Id, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            _rules[existingIndex] = rule;
        }
        else
        {
            _rules.Add(rule);
        }

        if (!_hasEverSavedRule)
        {
            _hasEverSavedRule = true;
            _shouldPromptLoginItemOnFirstRule = true;
        }
    }

    public void RemoveRule(string ruleId)
    {
        _rules.RemoveAll(r => string.Equals(r.Id, ruleId, StringComparison.Ordinal));
        _batteryBlockedRuleIds.Remove(ruleId);
        _capRestingRules.Remove(ruleId);
    }

    public void AcknowledgeFirstRuleLoginPrompt()
    {
        _shouldPromptLoginItemOnFirstRule = false;
    }

    public void AnswerCapQuestion(bool keepAwake, DateTimeOffset nowLocal)
    {
        if (ActiveSession is null)
        {
            return;
        }

        DateTimeOffset nowUtc = nowLocal.ToUniversalTime();
        if (keepAwake)
        {
            TimeSpan extension = DefaultCapDuration;
            if (ActiveSession.RuleId is string ruleId)
            {
                KeepAwakeRule? rule = _rules.Find(r => string.Equals(r.Id, ruleId, StringComparison.Ordinal));
                if (rule is not null && rule.CapDuration > TimeSpan.Zero)
                {
                    extension = rule.CapDuration;
                }
            }

            DateTimeOffset baseTime = ActiveSession.CapDeadlineUtc > nowUtc ? ActiveSession.CapDeadlineUtc : nowUtc;
            ActiveSession = ActiveSession with
            {
                CapDeadlineUtc = baseTime.Add(extension),
                IsCapQuestionVisible = false
            };
            _assertion.Refresh(ComputeLeaseTimeout(ActiveSession, nowUtc));
        }
        else
        {
            if (ActiveSession.RuleId is string ruleId)
            {
                _capRestingRules[ruleId] = null;
            }

            EndActiveSession(SessionEndReason.CapPromptDeclined, nowLocal, markTray: false, recordNotice: true, floorPercent: null);
        }
    }

    public void MarkPopoverOpened()
    {
        IsTrayIconMarked = false;
    }

    public KeepAwakeSnapshot EvaluateTick(
        DateTimeOffset nowLocal,
        PowerSourceKind powerSource,
        int? batteryChargePercent,
        ThermalState thermalState,
        IReadOnlyList<ProcessRecord> readableProcesses,
        bool isPro,
        IReadOnlyList<SleepRequestEntry>? externalSleepRequests = null)
    {
        DateTimeOffset nowUtc = nowLocal.ToUniversalTime();
        readableProcesses ??= [];

        // 1. Update recovery hysteresis for thermal and battery blocks.
        if (_thermalBlocked && thermalState is ThermalState.Nominal or ThermalState.Fair)
        {
            _thermalBlocked = false;
        }

        if (powerSource == PowerSourceKind.Mains)
        {
            _batteryBlockedRuleIds.Clear();
        }
        else if (powerSource == PowerSourceKind.Battery && batteryChargePercent is int charge)
        {
            var unblocked = new List<string>();
            foreach (KeyValuePair<string, int> kvp in _batteryBlockedRuleIds)
            {
                if (charge > kvp.Value + BatteryRestartHysteresisPercent)
                {
                    unblocked.Add(kvp.Key);
                }
            }

            foreach (string id in unblocked)
            {
                _batteryBlockedRuleIds.Remove(id);
            }
        }

        // 2. Update 60-second non-matching rest gap for rules that hit the unanswered 12h cap.
        UpdateCapRestingRules(readableProcesses, nowLocal, nowUtc);

        // 3. Evaluate guard rails and deadlines on any active session.
        if (thermalState == ThermalState.Critical)
        {
            _thermalBlocked = true;
            if (ActiveSession is not null)
            {
                EndActiveSession(
                    SessionEndReason.CriticalThermalReached,
                    nowLocal,
                    markTray: true,
                    recordNotice: true,
                    floorPercent: null);
            }
        }

        if (ActiveSession is not null
            && powerSource == PowerSourceKind.Battery
            && batteryChargePercent is int currentBattery
            && currentBattery <= ActiveSession.BatteryFloorPercent)
        {
            int floor = ActiveSession.BatteryFloorPercent;
            if (ActiveSession.RuleId is string ruleId)
            {
                _batteryBlockedRuleIds[ruleId] = floor;
            }

            EndActiveSession(
                SessionEndReason.BatteryFloorReached,
                nowLocal,
                markTray: true,
                recordNotice: true,
                floorPercent: floor);
        }

        if (ActiveSession is { IsManual: true, ManualDeadlineUtc: DateTimeOffset manualDeadline }
            && nowUtc >= manualDeadline)
        {
            EndActiveSession(
                SessionEndReason.DurationExpired,
                nowLocal,
                markTray: true,
                recordNotice: true,
                floorPercent: null);
        }

        if (ActiveSession is not null && nowUtc >= ActiveSession.CapDeadlineUtc)
        {
            if (ActiveSession.RuleId is string cappedRuleId)
            {
                _capRestingRules[cappedRuleId] = null;
            }

            EndActiveSession(
                SessionEndReason.CapReachedUnanswered,
                nowLocal,
                markTray: true,
                recordNotice: true,
                floorPercent: null);
        }

        // 4. Re-evaluate rule matching if no manual session is active.
        if (ActiveSession is not { IsManual: true })
        {
            KeepAwakeRule? winningRule = FindFirstEligibleMatchingRule(
                nowLocal,
                powerSource,
                batteryChargePercent,
                readableProcesses,
                isPro);

            if (ActiveSession is { IsManual: false, RuleId: string activeRuleId })
            {
                if (winningRule is null)
                {
                    EndActiveSession(
                        SessionEndReason.RuleStoppedMatching,
                        nowLocal,
                        markTray: true,
                        recordNotice: true,
                        floorPercent: null);
                }
                else if (!string.Equals(winningRule.Id, activeRuleId, StringComparison.Ordinal))
                {
                    StartRuleSession(winningRule, nowUtc);
                }
            }
            else if (ActiveSession is null && winningRule is not null)
            {
                StartRuleSession(winningRule, nowUtc);
            }
        }

        // 5. Update 10-minute cap question visibility and refresh assertion lease.
        if (ActiveSession is not null)
        {
            bool showCapPrompt = nowUtc >= ActiveSession.CapDeadlineUtc.Subtract(CapQuestionLeadTime)
                && nowUtc < ActiveSession.CapDeadlineUtc;
            if (ActiveSession.IsCapQuestionVisible != showCapPrompt)
            {
                ActiveSession = ActiveSession with { IsCapQuestionVisible = showCapPrompt };
            }

            TimeSpan lease = ComputeLeaseTimeout(ActiveSession, nowUtc);
            if (!_assertion.IsActive || _assertion.CurrentKind != ActiveSession.Kind || _assertion.CurrentName != ActiveSession.AssertionName)
            {
                _assertion.Acquire(ActiveSession.Kind, ActiveSession.AssertionName, lease);
            }
            else
            {
                _assertion.Refresh(lease);
            }
        }

        // 6. Build sleep request list.
        var sleepRequests = new List<SleepRequestEntry>();
        if (ActiveSession is not null)
        {
            sleepRequests.Add(new SleepRequestEntry(
                AppName: "Ambysto Power",
                KindText: ActiveSession.Kind == AssertionKind.SystemAndDisplay ? "system + display" : "system",
                SinceUtc: ActiveSession.StartedAtUtc,
                ReasonText: ActiveSession.AssertionName,
                IsOwnAssertion: true));
        }

        if (externalSleepRequests is not null)
        {
            foreach (SleepRequestEntry ext in externalSleepRequests)
            {
                sleepRequests.Add(ext);
            }
        }

        return new KeepAwakeSnapshot(
            ActiveSession: ActiveSession,
            LastEndedNotice: LastEndedNotice,
            IsTrayIconMarked: IsTrayIconMarked,
            ShouldPromptLoginItemOnFirstRule: _shouldPromptLoginItemOnFirstRule,
            Rules: _rules.ToArray(),
            SleepRequests: sleepRequests);
    }

    public void Shutdown(DateTimeOffset nowLocal)
    {
        if (ActiveSession is not null)
        {
            EndActiveSession(
                SessionEndReason.AppQuitting,
                nowLocal,
                markTray: false,
                recordNotice: false,
                floorPercent: null);
        }
        else
        {
            _assertion.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _assertion.Release();
        _disposed = true;
    }

    private void UpdateCapRestingRules(
        IReadOnlyList<ProcessRecord> readableProcesses,
        DateTimeOffset nowLocal,
        DateTimeOffset nowUtc)
    {
        if (_capRestingRules.Count == 0)
        {
            return;
        }

        var ruleIds = new List<string>(_capRestingRules.Keys);
        foreach (string ruleId in ruleIds)
        {
            KeepAwakeRule? rule = _rules.Find(r => string.Equals(r.Id, ruleId, StringComparison.Ordinal));
            if (rule is null)
            {
                _capRestingRules.Remove(ruleId);
                continue;
            }

            bool isMatching = _ruleMatcher?.IsRuleMatching(rule, readableProcesses, nowLocal) ?? false;
            DateTimeOffset? nonMatchingSince = _capRestingRules[ruleId];

            if (!isMatching)
            {
                if (nonMatchingSince is null)
                {
                    _capRestingRules[ruleId] = nowUtc;
                }
                else if (nowUtc - nonMatchingSince.Value >= UnansweredRuleRestGap)
                {
                    _capRestingRules.Remove(ruleId);
                }
            }
            else
            {
                if (nonMatchingSince is DateTimeOffset gapStart && nowUtc - gapStart >= UnansweredRuleRestGap)
                {
                    _capRestingRules.Remove(ruleId);
                }
                else
                {
                    _capRestingRules[ruleId] = null;
                }
            }
        }
    }

    private KeepAwakeRule? FindFirstEligibleMatchingRule(
        DateTimeOffset nowLocal,
        PowerSourceKind powerSource,
        int? batteryChargePercent,
        IReadOnlyList<ProcessRecord> readableProcesses,
        bool isPro)
    {
        if (!isPro || _ruleMatcher is null || _thermalBlocked || _rules.Count == 0)
        {
            return null;
        }

        for (int i = 0; i < _rules.Count; i++)
        {
            KeepAwakeRule rule = _rules[i];
            if (!rule.Enabled)
            {
                continue;
            }

            if (_capRestingRules.ContainsKey(rule.Id) || _batteryBlockedRuleIds.ContainsKey(rule.Id))
            {
                continue;
            }

            int floor = rule.BatteryFloorPercent > 0 ? rule.BatteryFloorPercent : DefaultBatteryFloorPercent;
            if (powerSource == PowerSourceKind.Battery
                && batteryChargePercent is int charge
                && charge <= floor)
            {
                _batteryBlockedRuleIds[rule.Id] = floor;
                continue;
            }

            if (_ruleMatcher.IsRuleMatching(rule, readableProcesses, nowLocal))
            {
                return rule;
            }
        }

        return null;
    }

    private void StartRuleSession(KeepAwakeRule rule, DateTimeOffset nowUtc)
    {
        int floor = rule.BatteryFloorPercent > 0 ? rule.BatteryFloorPercent : DefaultBatteryFloorPercent;
        TimeSpan cap = rule.CapDuration > TimeSpan.Zero ? rule.CapDuration : DefaultCapDuration;
        DateTimeOffset capDeadlineUtc = nowUtc.Add(cap);
        AssertionKind kind = rule.DisplayOn ? AssertionKind.SystemAndDisplay : AssertionKind.SystemOnly;
        string title = string.IsNullOrWhiteSpace(rule.Title) ? "Rule" : rule.Title.Trim();
        string assertionName = $"Ambysto Power: {title}";

        ActiveSession = new ActiveKeepAwakeSession(
            SessionId: Guid.NewGuid().ToString("N"),
            Title: title,
            IsManual: false,
            RuleId: rule.Id,
            Kind: kind,
            AssertionName: assertionName,
            StartedAtUtc: nowUtc,
            CapDeadlineUtc: capDeadlineUtc,
            ManualDeadlineUtc: null,
            BatteryFloorPercent: floor,
            IsCapQuestionVisible: false);

        _assertion.Acquire(kind, assertionName, ComputeLeaseTimeout(ActiveSession, nowUtc));
    }

    private void EndActiveSession(
        SessionEndReason reason,
        DateTimeOffset nowLocal,
        bool markTray,
        bool recordNotice,
        int? floorPercent)
    {
        ActiveSession = null;
        _assertion.Release();

        if (markTray)
        {
            IsTrayIconMarked = true;
        }

        if (recordNotice)
        {
            string timeText = nowLocal.ToString("HH:mm", CultureInfo.InvariantCulture);
            string displayText = reason switch
            {
                SessionEndReason.BatteryFloorReached =>
                    string.Create(CultureInfo.InvariantCulture, $"ended at {floorPercent ?? DefaultBatteryFloorPercent} % battery, {timeText}"),
                SessionEndReason.CriticalThermalReached =>
                    $"ended due to critical thermal state, {timeText}",
                SessionEndReason.CapReachedUnanswered =>
                    $"ended at 12 h cap, {timeText}",
                SessionEndReason.DurationExpired =>
                    $"ended when timer finished, {timeText}",
                SessionEndReason.RuleStoppedMatching =>
                    $"ended when rule finished, {timeText}",
                SessionEndReason.CapPromptDeclined =>
                    $"ended at 12 h prompt, {timeText}",
                _ =>
                    $"ended ({reason}), {timeText}"
            };

            LastEndedNotice = new SessionEndedNotice(reason, nowLocal, displayText);
        }
    }

    private static TimeSpan ComputeLeaseTimeout(ActiveKeepAwakeSession session, DateTimeOffset nowUtc)
    {
        TimeSpan remaining = session.CapDeadlineUtc - nowUtc;
        if (session.ManualDeadlineUtc is DateTimeOffset manualDeadline)
        {
            TimeSpan manualRemaining = manualDeadline - nowUtc;
            if (manualRemaining < remaining)
            {
                remaining = manualRemaining;
            }
        }

        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(1);
        }

        return remaining < MaxAssertionTimeout ? remaining : MaxAssertionTimeout;
    }
}
