using System;
using System.Collections.Generic;
using Ambysto.Kit.Processes;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.KeepAwake;
using Ambysto.Power.Core.Licensing;
using Ambysto.Power.Core.Thermal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.Core.Tests;

[TestClass]
public sealed partial class KeepAwakeTests
{
    private const string TestSid = "S-1-5-21-1000-2000-3000-1001";

    private sealed class FakeExecutionStateNative : IExecutionStateNative
    {
        public List<uint> Calls { get; } = [];

        public uint SetExecutionState(uint flags)
        {
            Calls.Add(flags);
            return flags;
        }
    }

    private sealed class FakeSleepAssertion : ISleepAssertion
    {
        public bool IsActive { get; private set; }
        public AssertionKind? CurrentKind { get; private set; }
        public string? CurrentName { get; private set; }
        public TimeSpan? CurrentTimeout { get; private set; }
        public int AcquireCount { get; private set; }
        public int ReleaseCount { get; private set; }

        public void Acquire(AssertionKind kind, string assertionName, TimeSpan timeout)
        {
            IsActive = true;
            CurrentKind = kind;
            CurrentName = assertionName;
            CurrentTimeout = timeout;
            AcquireCount++;
        }

        public void Refresh(TimeSpan timeout)
        {
            if (IsActive)
            {
                CurrentTimeout = timeout;
            }
        }

        public void Release()
        {
            if (IsActive)
            {
                IsActive = false;
                CurrentKind = null;
                CurrentName = null;
                CurrentTimeout = null;
                ReleaseCount++;
            }
        }

        public void Dispose() => Release();
    }

    [TestMethod]
    public void ExecutionStateAssertionSetsExpectedFlagsReleasesBeforeSwitchCapsLeaseAndExpiresWatchdog()
    {
        var fakeNative = new FakeExecutionStateNative();
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        using var assertion = new ExecutionStateAssertion(fakeNative, () => now);

        // 1. Acquire SystemOnly -> 0x80000001u, capped at 15 minutes.
        assertion.Acquire(AssertionKind.SystemOnly, "Ambysto Power: Build", TimeSpan.FromHours(2));
        Assert.IsTrue(assertion.IsActive);
        Assert.AreEqual(AssertionKind.SystemOnly, assertion.CurrentKind);
        Assert.AreEqual("Ambysto Power: Build", assertion.CurrentName);
        Assert.AreEqual(ExecutionStateAssertion.MaxLeaseTimeout, assertion.CurrentTimeout);
        Assert.AreEqual(1, fakeNative.Calls.Count);
        Assert.AreEqual(0x80000001u, fakeNative.Calls[0]);

        // 2. Switching kind to SystemAndDisplay releases old kind (0x80000000u) before setting 0x80000003u.
        assertion.Acquire(AssertionKind.SystemAndDisplay, "Ambysto Power: Presentation", TimeSpan.FromMinutes(10));
        Assert.IsTrue(assertion.IsActive);
        Assert.AreEqual(AssertionKind.SystemAndDisplay, assertion.CurrentKind);
        Assert.AreEqual("Ambysto Power: Presentation", assertion.CurrentName);
        Assert.AreEqual(TimeSpan.FromMinutes(10), assertion.CurrentTimeout);
        Assert.AreEqual(3, fakeNative.Calls.Count);
        Assert.AreEqual(0x80000000u, fakeNative.Calls[1]);
        Assert.AreEqual(0x80000003u, fakeNative.Calls[2]);

        // 3. Refresh updates lease and re-asserts execution state.
        assertion.Refresh(TimeSpan.FromMinutes(10));
        Assert.AreEqual(TimeSpan.FromMinutes(10), assertion.CurrentTimeout);
        Assert.AreEqual(4, fakeNative.Calls.Count);
        Assert.AreEqual(0x80000003u, fakeNative.Calls[3]);

        // 4. Watchdog before expiry keeps assertion active.
        assertion.CheckWatchdog(now.AddMinutes(9));
        Assert.IsTrue(assertion.IsActive);

        // 5. Watchdog at/after lease expiry clears execution state (0x80000000u) and sets IsActive = false.
        assertion.CheckWatchdog(now.AddMinutes(10).AddSeconds(1));
        Assert.IsFalse(assertion.IsActive);
        Assert.IsNull(assertion.CurrentKind);
        Assert.AreEqual(0x80000000u, fakeNative.Calls[^1]);

        // Refresh while inactive is a no-op.
        assertion.Refresh(TimeSpan.FromMinutes(5));
        Assert.IsFalse(assertion.IsActive);

        // 6. Re-acquire and verify explicit Release() clears execution state (0x80000000u).
        assertion.Acquire(AssertionKind.SystemOnly, "Ambysto Power: Short", TimeSpan.FromMinutes(5));
        Assert.IsTrue(assertion.IsActive);
        assertion.Release();
        Assert.IsFalse(assertion.IsActive);
        Assert.AreEqual(0x80000000u, fakeNative.Calls[^1]);
    }

    [TestMethod]
    [TestCategory("Console")]
    public void ExecutionStateAssertionLiveOsThreadAcquiresAndReleasesCleanly()
    {
        using var assertion = new ExecutionStateAssertion();
        try
        {
            assertion.Acquire(AssertionKind.SystemOnly, "Ambysto Power: LiveTest", TimeSpan.FromMinutes(1));
            Assert.IsTrue(assertion.IsActive);
            assertion.Acquire(AssertionKind.SystemAndDisplay, "Ambysto Power: LiveTestDisplay", TimeSpan.FromMinutes(1));
            Assert.IsTrue(assertion.IsActive);
        }
        finally
        {
            assertion.Release();
            Assert.IsFalse(assertion.IsActive);
        }
    }

    // Without the private module there is no rule matcher, so no rule ever matches, even for a Pro key; manual sessions
    // and the saved rule list are not affected. This is the behaviour of the public build.
    [TestMethod]
    public void WithoutARuleMatcherNoRuleStartsASessionButManualSessionsStillWork()
    {
        var assertion = new FakeSleepAssertion();
        using var controller = new KeepAwakeController(assertion);
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        ProcessRecord proc = CreateReadableProcess(101, 1_000L, "AppOne.exe", @"C:\Apps\One\AppOne.exe");

        controller.AddOrUpdateRule(
            new KeepAwakeRule("rule-1", "Rule One", KeepAwakeRuleKind.WhileAppRuns, Enabled: true, DisplayOn: false, AppNameOrPath: "AppOne.exe"),
            isPro: true);
        Assert.AreEqual(1, controller.Rules.Count);

        KeepAwakeSnapshot snap = controller.EvaluateTick(now, PowerSourceKind.Mains, batteryChargePercent: 90, ThermalState.Nominal, [proc], isPro: true);
        Assert.IsNull(snap.ActiveSession);
        Assert.IsFalse(assertion.IsActive);

        controller.StartManualSession(new ManualSessionSpec("Manual", ManualSessionMode.UntilTurnedOff, DisplayOn: false), now);
        Assert.IsTrue(assertion.IsActive);
        Assert.IsFalse(NoProFeatures.Instance.Available);
        Assert.IsNull(NoProFeatures.Instance.CreateRuleMatcher());
        Assert.IsNull(NoProFeatures.Instance.CreateAlertEvaluator());
    }

    private static ProcessRecord CreateReadableProcess(
        int pid,
        long startTicks,
        string name,
        string executablePath,
        int? parentPid = null)
    {
        return ProcessRecord.Readable(
            identity: new ProcessIdentity(pid, startTicks),
            parentProcessId: parentPid,
            name: name,
            sessionId: 1,
            ownerSid: TestSid,
            executablePath: executablePath,
            workingDirectory: @"C:\work",
            commandLine: executablePath,
            arguments: [executablePath],
            usage: new ProcessCounters(10_000_000L, 1_000_000_000L, 0, 0));
    }
}
