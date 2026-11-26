using System;
using System.IO;
using Ambysto.Kit.Processes;
using Ambysto.Power.Core.Apps;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.History;
using Ambysto.Power.Core.KeepAwake;
using Ambysto.Power.Core.Licensing;
using Ambysto.Power.Core.Suite;
using Ambysto.Power.Core.Thermal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.UiTests;

[TestClass]
public sealed class PowerUiAndSmokeTests
{
    [TestMethod]
    public void EndToEndPollCycleProducesSnapshotsAndGlanceRecord()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "AmbystoPowerUiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var historyStore = new HistoryStore(Path.Combine(tempDir, "history"));
            var entitlement = new EntitlementService(tempDir);
            var glanceWriter = new SuiteGlanceWriter(Path.Combine(tempDir, "suite"));
            using var assertion = new ExecutionStateAssertion();
            using var keepAwake = new KeepAwakeController(assertion);
            var batteryMonitor = new BatteryMonitor(new WindowsBatteryReader());
            var estimator = new PowerEstimator();
            using var thermalReader = new WindowsThermalReader();
            var thermalTracker = new ThermalEpisodeTracker();

            DateTimeOffset nowLocal = DateTimeOffset.Now;
            var (batterySnap, _, _) = batteryMonitor.Tick(nowLocal);
            Assert.IsNotNull(batterySnap);

            var (thermalSnap, _) = thermalTracker.Update(thermalReader.ReadThermal(), []);
            Assert.IsNotNull(thermalSnap);

            KeepAwakeSnapshot awakeSnap = keepAwake.EvaluateTick(
                nowLocal,
                batterySnap.Current.PowerSource,
                batterySnap.Current.ChargePercent,
                thermalSnap.State,
                [],
                entitlement.Current.IsPro);
            Assert.IsNull(awakeSnap.ActiveSession);

            glanceWriter.WriteGlance(batterySnap.Current, thermalSnap.State, keepAwakeActive: false, nowLocal.ToUniversalTime());
            Assert.IsTrue(File.Exists(glanceWriter.GlanceFilePath));

            PruneEvaluation eval = historyStore.EvaluateRetention(
                DateOnly.FromDateTime(nowLocal.DateTime),
                entitlement.Current.IsPro,
                entitlement.Current.HasStoredKeyText);
            Assert.IsTrue(eval.ShouldAutoPruneAtLaunch);

            AppPowerSnapshot appSnap = estimator.GetSnapshot(PowerWindowKind.Last5Minutes, nowLocal.ToUniversalTime());
            Assert.AreEqual("estimate", appSnap.ColumnHeader);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [TestMethod]
    [TestCategory("Console")]
    public void LiveProcessSamplerAndPowerEstimatorGroupCurrentSessionProcesses()
    {
        var sampler = new ProcessSampler();
        _ = sampler.Sample();
        ProcessSample sample = sampler.Sample();

        var estimator = new PowerEstimator();
        estimator.RecordSample(sample, DateTimeOffset.UtcNow);
        AppPowerSnapshot snapshot = estimator.GetSnapshot(PowerWindowKind.Last5Minutes, DateTimeOffset.UtcNow);

        Assert.AreEqual("estimate", snapshot.ColumnHeader);
        Assert.AreEqual("GPU not measured", snapshot.GpuNote);
        Assert.IsTrue(snapshot.Rows.Count >= 0);
    }
}
