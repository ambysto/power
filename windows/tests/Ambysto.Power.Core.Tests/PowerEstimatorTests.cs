using System;
using System.Collections.Generic;
using System.Linq;
using Ambysto.Kit.Processes;
using Ambysto.Power.Core.Apps;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.Core.Tests;

[TestClass]
public sealed class PowerEstimatorTests
{
    private const string TestSid = "S-1-5-21-1000-2000-3000-1001";

    [TestMethod]
    public void WakeupCostSecondsConstantMatchesSpec()
    {
        Assert.AreEqual(0.0005, PowerEstimator.WakeupCostSeconds, 1e-9);
    }

    [TestMethod]
    public void ComputeProcessImpactUsesEnergyWhenWindowUsesEnergyElseCpuPlusWakeups()
    {
        double energyImpact = PowerEstimator.ComputeProcessImpact(
            cpuSeconds: 12.0,
            idleWakeups: 2_000,
            energyNanojoules: 750_000,
            windowUsesEnergy: true);
        Assert.AreEqual(750_000.0, energyImpact, 0.001);

        double zeroEnergyInEnergyWindow = PowerEstimator.ComputeProcessImpact(
            cpuSeconds: 12.0,
            idleWakeups: 2_000,
            energyNanojoules: null,
            windowUsesEnergy: true);
        Assert.AreEqual(0.0, zeroEnergyInEnergyWindow, 0.001);

        // cpuSeconds (2.0) + 1000 * 0.0005 (0.5) = 2.5
        double cpuFallbackImpact = PowerEstimator.ComputeProcessImpact(
            cpuSeconds: 2.0,
            idleWakeups: 1_000,
            energyNanojoules: null,
            windowUsesEnergy: false);
        Assert.AreEqual(2.5, cpuFallbackImpact, 0.0001);
    }

    [TestMethod]
    public void ComputeSharesSumsTo100SortedDescendingAndHandlesZeroAndEnergyWindows()
    {
        var cpuAndWakeupRows = PowerEstimator.ComputeShares(
        [
            ("app-b", "Browser", 1.0, 2_000L, null, 3), // impact = 1.0 + 1.0 = 2.0 (25%)
            ("app-a", "Compiler", 5.0, 2_000L, null, 2) // impact = 5.0 + 1.0 = 6.0 (75%)
        ]);

        Assert.AreEqual(2, cpuAndWakeupRows.Count);
        Assert.AreEqual("Compiler", cpuAndWakeupRows[0].DisplayName);
        Assert.AreEqual("Browser", cpuAndWakeupRows[1].DisplayName);
        Assert.AreEqual(75.0, cpuAndWakeupRows[0].SharePercent, 0.01);
        Assert.AreEqual(25.0, cpuAndWakeupRows[1].SharePercent, 0.01);
        Assert.AreEqual(100.0, cpuAndWakeupRows.Sum(r => r.SharePercent), 0.01);

        // When any row reports EnergyNanojoules > 0, window uses energy instead of CPU fallback.
        var energyRows = PowerEstimator.ComputeShares(
        [
            ("app-cpu", "HighCpuZeroEnergy", 100.0, 10_000L, 100L, 1),
            ("app-energy", "HighEnergy", 1.0, 0L, 300L, 1)
        ]);
        Assert.AreEqual("HighEnergy", energyRows[0].DisplayName);
        Assert.AreEqual(75.0, energyRows[0].SharePercent, 0.01);
        Assert.AreEqual(25.0, energyRows[1].SharePercent, 0.01);

        // When all impacts are 0, shares are 0.0.
        var zeroRows = PowerEstimator.ComputeShares(
        [
            ("idle-1", "IdleOne", 0.0, 0L, null, 1),
            ("idle-2", "IdleTwo", 0.0, 0L, null, 1)
        ]);
        Assert.AreEqual(0.0, zeroRows[0].SharePercent, 0.001);
        Assert.AreEqual(0.0, zeroRows[1].SharePercent, 0.001);
    }

    [TestMethod]
    public void ResolveGroupKeyAndNameRollsChildUpToParentInSameAppFolderAndFallsBackToArgv0ThenName()
    {
        ProcessRecord parentApp = CreateReadableProcess(
            pid: 100,
            parentPid: null,
            name: "AcmeApp.exe",
            executablePath: @"C:\Program Files\Acme\App\AcmeApp.exe",
            commandLine: @"""C:\Program Files\Acme\App\AcmeApp.exe""");
        ProcessRecord childWorker = CreateReadableProcess(
            pid: 200,
            parentPid: 100,
            name: "worker.exe",
            executablePath: @"C:\Program Files\Acme\App\bin\worker.exe",
            commandLine: @"""C:\Program Files\Acme\App\bin\worker.exe"" --type=renderer");

        var byPid = new Dictionary<int, ProcessRecord>
        {
            [100] = parentApp,
            [200] = childWorker
        };

        string parentKey = AppPowerGrouper.ResolveGroupKeyAndName(
            parentApp,
            byPid,
            path => path.EndsWith("AcmeApp.exe", StringComparison.OrdinalIgnoreCase) ? "Acme Studio" : null,
            out string parentDisplay);
        string childKey = AppPowerGrouper.ResolveGroupKeyAndName(
            childWorker,
            byPid,
            path => path.EndsWith("AcmeApp.exe", StringComparison.OrdinalIgnoreCase) ? "Acme Studio" : null,
            out string childDisplay);

        Assert.AreEqual(parentKey, childKey);
        Assert.AreEqual("Acme Studio", parentDisplay);
        Assert.AreEqual("Acme Studio", childDisplay);

        // Fallback to first command-line argument when ExecutablePath is null.
        ProcessRecord argvProcess = ProcessRecord.Readable(
            identity: new ProcessIdentity(300, 1_000),
            parentProcessId: null,
            name: "python.exe",
            sessionId: 1,
            ownerSid: TestSid,
            executablePath: null,
            workingDirectory: @"C:\work",
            commandLine: @"C:\tools\mytool.exe --serve",
            arguments: [@"C:\tools\mytool.exe", "--serve"],
            usage: new ProcessCounters(1024, 1_000, 0, 0));

        string argvKey = AppPowerGrouper.ResolveGroupKeyAndName(
            argvProcess,
            new Dictionary<int, ProcessRecord> { [300] = argvProcess },
            null,
            out string argvDisplay);
        Assert.IsFalse(string.IsNullOrWhiteSpace(argvKey));
        StringAssert.Contains(argvDisplay, "mytool", StringComparison.OrdinalIgnoreCase);

        // Fallback to process Name when ExecutablePath and CommandLine are null.
        ProcessRecord nameOnlyProcess = ProcessRecord.Readable(
            identity: new ProcessIdentity(400, 1_000),
            parentProcessId: null,
            name: " standalone-daemon.exe ",
            sessionId: 1,
            ownerSid: TestSid,
            executablePath: null,
            workingDirectory: null,
            commandLine: null,
            arguments: null,
            usage: new ProcessCounters(1024, 1_000, 0, 0));

        string nameKey = AppPowerGrouper.ResolveGroupKeyAndName(
            nameOnlyProcess,
            new Dictionary<int, ProcessRecord> { [400] = nameOnlyProcess },
            null,
            out string nameDisplay);
        Assert.IsFalse(string.IsNullOrWhiteSpace(nameKey));
        StringAssert.Contains(nameDisplay, "standalone-daemon", StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void RecordSampleAndGetSnapshotIncludeOwnProcessAndCoverAllThreeTimeWindows()
    {
        var estimator = new PowerEstimator();
        var t0 = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);

        ProcessRecord ownProcess = CreateReadableProcess(
            pid: 500,
            parentPid: null,
            name: "AmbystoPower.exe",
            executablePath: @"C:\Users\alice\AppData\Local\Programs\Ambysto Power\AmbystoPower.exe",
            commandLine: @"""C:\Users\alice\AppData\Local\Programs\Ambysto Power\AmbystoPower.exe""");
        ProcessRecord editorProcess = CreateReadableProcess(
            pid: 600,
            parentPid: null,
            name: "Code.exe",
            executablePath: @"C:\Users\alice\AppData\Local\Programs\Microsoft VS Code\Code.exe",
            commandLine: @"""C:\Users\alice\AppData\Local\Programs\Microsoft VS Code\Code.exe""");

        var sample1 = new ProcessSample(
            processes:
            [
                new SampledProcess(
                    process: ownProcess,
                    deltas: new UsageDeltas(
                        intervalNanoseconds: 30_000_000_000L,
                        cpuTimeNanoseconds: 1_000_000_000L,
                        bytesRead: 0,
                        bytesWritten: 0),
                    cpuPercent: 3.3,
                    bytesReadPerSecond: 0,
                    bytesWrittenPerSecond: 0),
                new SampledProcess(
                    process: editorProcess,
                    deltas: new UsageDeltas(
                        intervalNanoseconds: 30_000_000_000L,
                        cpuTimeNanoseconds: 3_000_000_000L,
                        bytesRead: 0,
                        bytesWritten: 0),
                    cpuPercent: 10.0,
                    bytesReadPerSecond: 0,
                    bytesWrittenPerSecond: 0)
            ],
            allProcesses: [ownProcess, editorProcess],
            unreadableProcessCount: 7,
            timestampNanoseconds: 30_000_000_000L);

        estimator.RecordSample(
            sample1,
            t0.AddMinutes(1),
            path => path.Contains("AmbystoPower", StringComparison.OrdinalIgnoreCase) ? "Ambysto Power" : "VS Code");

        AppPowerSnapshot last5 = estimator.GetSnapshot(PowerWindowKind.Last5Minutes, t0.AddMinutes(1));
        Assert.AreEqual("estimate", last5.ColumnHeader);
        Assert.AreEqual("GPU not measured", last5.GpuNote);
        Assert.AreEqual("7 not measured", last5.UnmeasuredNote);
        Assert.AreEqual(7, last5.UnreadableProcessCount);
        Assert.AreEqual(2, last5.Rows.Count);
        Assert.IsTrue(last5.Rows.Any(r => r.DisplayName == "Ambysto Power"));
        Assert.AreEqual("VS Code", last5.Rows[0].DisplayName);
        Assert.AreEqual(75.0, last5.Rows[0].SharePercent, 0.01);
        Assert.AreEqual(25.0, last5.Rows[1].SharePercent, 0.01);

        // Flush 13 five-minute readings and verify LastHour caps at MaxHourlyReadings (12) and SinceUnplugged filters by timestamp.
        AppPowerReading? flushedFirst = estimator.FlushFiveMinuteReading(t0.AddMinutes(5));
        Assert.IsNotNull(flushedFirst);

        for (int i = 2; i <= 13; i++)
        {
            estimator.RecordSample(
                sample1,
                t0.AddMinutes((i * 5) - 1),
                path => path.Contains("AmbystoPower", StringComparison.OrdinalIgnoreCase) ? "Ambysto Power" : "VS Code");
            _ = estimator.FlushFiveMinuteReading(t0.AddMinutes(i * 5));
        }

        Assert.AreEqual(PowerEstimator.MaxHourlyReadings, estimator.Readings.Count);

        AppPowerSnapshot lastHour = estimator.GetSnapshot(PowerWindowKind.LastHour, t0.AddMinutes(65));
        Assert.AreEqual(2, lastHour.Rows.Count);
        Assert.AreEqual(12.0, lastHour.Rows.First(r => r.DisplayName == "Ambysto Power").CpuSeconds, 0.01);

        DateTimeOffset unpluggedAt = t0.AddMinutes(50);
        AppPowerSnapshot sinceUnplugged = estimator.GetSnapshot(
            PowerWindowKind.SinceUnplugged,
            t0.AddMinutes(65),
            sinceUnpluggedUtc: unpluggedAt);
        Assert.IsNull(estimator.FlushFiveMinuteReading(t0.AddMinutes(65)));
        AppPowerSnapshot last5FromFlushed = estimator.GetSnapshot(PowerWindowKind.Last5Minutes, t0.AddMinutes(65));
        Assert.AreEqual(2, last5FromFlushed.Rows.Count);
        var emptyEstimator = new PowerEstimator();
        Assert.AreEqual(0, emptyEstimator.GetSnapshot(PowerWindowKind.Last5Minutes, t0).Rows.Count);
        Assert.AreEqual(0, emptyEstimator.GetSnapshot(PowerWindowKind.LastHour, t0).Rows.Count);
        emptyEstimator.RecordSample(sample1, t0.AddMinutes(55));
        Assert.AreEqual(2, emptyEstimator.GetSnapshot(PowerWindowKind.LastHour, t0.AddMinutes(56)).Rows.Count);
        Assert.AreEqual(2, emptyEstimator.GetSnapshot(PowerWindowKind.SinceUnplugged, t0.AddMinutes(56), unpluggedAt).Rows.Count);
        var rehydrated = new PowerEstimator(estimator.Readings);
        Assert.AreEqual(PowerEstimator.MaxHourlyReadings, rehydrated.Readings.Count);
        Assert.AreEqual(2, sinceUnplugged.Rows.Count);
        Assert.AreEqual(3.0, sinceUnplugged.Rows.First(r => r.DisplayName == "Ambysto Power").CpuSeconds, 0.01);
    }

    private static ProcessRecord CreateReadableProcess(
        int pid,
        int? parentPid,
        string name,
        string executablePath,
        string commandLine)
    {
        return ProcessRecord.Readable(
            identity: new ProcessIdentity(pid, 100_000L),
            parentProcessId: parentPid,
            name: name,
            sessionId: 1,
            ownerSid: TestSid,
            executablePath: executablePath,
            workingDirectory: @"C:\work",
            commandLine: commandLine,
            arguments: [executablePath],
            usage: new ProcessCounters(10_000_000L, 1_000_000_000L, 0, 0));
    }
}
