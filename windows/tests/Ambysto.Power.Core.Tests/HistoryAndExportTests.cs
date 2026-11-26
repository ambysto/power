using System;
using System.Collections.Generic;
using System.IO;
using Ambysto.Power.Core.Apps;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.History;
using Ambysto.Power.Core.Thermal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.Core.Tests;

[TestClass]
public sealed class HistoryAndExportTests
{
    [TestMethod]
    public void IsExactDayFileNameAcceptsOnlyValidCalendarDayJsonlNames()
    {
        Assert.IsTrue(HistoryStore.IsExactDayFileName("2026-10-25.jsonl"));
        Assert.IsTrue(HistoryStore.IsExactDayFileName("2024-02-29.jsonl"));

        Assert.IsFalse(HistoryStore.IsExactDayFileName("daily-health.jsonl"));
        Assert.IsFalse(HistoryStore.IsExactDayFileName("pro-history"));
        Assert.IsFalse(HistoryStore.IsExactDayFileName("license.key"));
        Assert.IsFalse(HistoryStore.IsExactDayFileName("../2026-10-01.jsonl"));
        Assert.IsFalse(HistoryStore.IsExactDayFileName(@"..\2026-10-01.jsonl"));
        Assert.IsFalse(HistoryStore.IsExactDayFileName("2026-13-45.jsonl"));
        Assert.IsFalse(HistoryStore.IsExactDayFileName("2026-02-29.jsonl"));
        Assert.IsFalse(HistoryStore.IsExactDayFileName("2026-10-25.json"));
        Assert.IsFalse(HistoryStore.IsExactDayFileName(string.Empty));
    }

    [TestMethod]
    public void EvaluateRetentionAndRunLaunchRetentionPruneOnlyWhenFreeWithNoStoredKeyAndNoProHistoryMarker()
    {
        string tempDir = CreateTempDirectory();
        try
        {
            var store = new HistoryStore(tempDir);
            var today = new DateOnly(2026, 10, 25);
            var oldDate1 = new DateOnly(2026, 10, 10); // 15 days old (> 7 days)
            var oldDate2 = new DateOnly(2026, 10, 15); // 10 days old (> 7 days)
            var recentDate = new DateOnly(2026, 10, 22); // 3 days old (within 7 days)

            BatterySamplePoint sample = new(
                TimestampUtc: new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero),
                ChargePercent: 85,
                PowerSource: PowerSourceKind.Battery,
                State: BatteryChargingState.Discharging,
                FullChargeCapacityMwh: 45_000,
                CycleCount: 120,
                TemperatureCelsius: 32.0,
                DischargeWatts: 14.5);

            store.AppendBatterySample(sample, oldDate1, isPro: false);
            store.AppendBatterySample(sample, oldDate2, isPro: false);
            store.AppendBatterySample(sample, recentDate, isPro: false);
            store.AppendDailyHealthPoint(
                new DailyHealthPoint(oldDate1, 45_000, 120, 50_000, "Windows PC"),
                isPro: false);

            Assert.IsFalse(store.HasProHistoryMarker);

            // Case 1: Free + hasAnyStoredKeyText == true (e.g. failed/invalid stored key) -> NEVER prunes, sets OlderDataHiddenInFree = true.
            PruneEvaluation evalWithStoredKey = store.EvaluateRetention(today, isPro: false, hasAnyStoredKeyText: true);
            Assert.IsFalse(evalWithStoredKey.ShouldAutoPruneAtLaunch);
            Assert.IsTrue(evalWithStoredKey.OlderDataHiddenInFree);
            Assert.AreEqual(2, evalWithStoredKey.EligibleFileNamesOlderThan7Days.Count);

            IReadOnlyList<string> deletedWithKey = store.RunLaunchRetention(today, isPro: false, hasAnyStoredKeyText: true);
            Assert.AreEqual(0, deletedWithKey.Count);
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "2026-10-10.jsonl")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "2026-10-15.jsonl")));

            // Case 2: Free + no stored key + no pro-history marker -> prunes files older than 7 days, keeps recent and daily-health.jsonl!
            PruneEvaluation evalFreeClean = store.EvaluateRetention(today, isPro: false, hasAnyStoredKeyText: false);
            Assert.IsTrue(evalFreeClean.ShouldAutoPruneAtLaunch);
            Assert.IsFalse(evalFreeClean.OlderDataHiddenInFree);
            Assert.AreEqual(2, evalFreeClean.EligibleFileNamesOlderThan7Days.Count);

            IReadOnlyList<string> deletedClean = store.RunLaunchRetention(today, isPro: false, hasAnyStoredKeyText: false);
            Assert.AreEqual(2, deletedClean.Count);
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "2026-10-10.jsonl")));
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "2026-10-15.jsonl")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "2026-10-22.jsonl")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, HistoryStore.DailyHealthFileName)));
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    [TestMethod]
    public void ProHistoryMarkerPreventsLaunchPruneInFreeAndDailyHealthIsNeverDeleted()
    {
        string tempDir = CreateTempDirectory();
        try
        {
            var store = new HistoryStore(tempDir);
            var today = new DateOnly(2026, 10, 25);
            var oldDate = new DateOnly(2026, 10, 1);

            BatterySamplePoint sample = new(
                TimestampUtc: new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero),
                ChargePercent: 90,
                PowerSource: PowerSourceKind.Mains,
                State: BatteryChargingState.Charged,
                FullChargeCapacityMwh: 45_000,
                CycleCount: 100,
                TemperatureCelsius: 30.0,
                DischargeWatts: null);

            // Appending while isPro == true writes the pro-history marker.
            store.AppendBatterySample(sample, oldDate, isPro: true);
            store.AppendBatterySample(sample, today, isPro: true);
            store.AppendDailyHealthPoint(
                new DailyHealthPoint(oldDate, 45_000, 100, 50_000, "Windows PC"),
                isPro: true);
            store.AppendAppPowerReading(
                new AppPowerReading(
                    sample.TimestampUtc.AddMinutes(-5),
                    sample.TimestampUtc,
                    [new AppPowerRow("code", "VS Code", 12.0, 100, null, 12.05, 100.0, 2)],
                    UnreadableProcessCount: 3),
                today,
                isPro: true);
            store.AppendThermalEpisode(
                new ThermalEpisode(
                    sample.TimestampUtc.AddMinutes(-10),
                    sample.TimestampUtc,
                    TimeSpan.FromMinutes(10),
                    ThermalState.Fair,
                    85,
                    ["VS Code"],
                    "throttled 10 min"),
                today,
                isPro: true);

            Assert.AreEqual(1, store.ReadBatterySamples(today, today).Count);
            Assert.AreEqual(1, store.ReadAppPowerReadings(today, today).Count);
            Assert.AreEqual(1, store.ReadThermalEpisodes(today, today).Count);

            Assert.IsTrue(store.HasProHistoryMarker);
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, HistoryStore.ProHistoryMarkerFileName)));

            // Even when launched in Free with no stored key text, pro-history prevents auto-pruning and hides older data.
            PruneEvaluation eval = store.EvaluateRetention(today, isPro: false, hasAnyStoredKeyText: false);
            Assert.IsFalse(eval.ShouldAutoPruneAtLaunch);
            Assert.IsTrue(eval.OlderDataHiddenInFree);

            IReadOnlyList<string> autoDeleted = store.RunLaunchRetention(today, isPro: false, hasAnyStoredKeyText: false);
            Assert.AreEqual(0, autoDeleted.Count);
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, "2026-10-01.jsonl")));

            // Explicit manual DeleteHistoryOlderThan7Days deletes 2026-10-01.jsonl, but NEVER deletes daily-health.jsonl or pro-history!
            IReadOnlyList<string> manualDeleted = store.DeleteHistoryOlderThan7Days(today);
            Assert.AreEqual(1, manualDeleted.Count);
            Assert.AreEqual("2026-10-01.jsonl", manualDeleted[0]);
            Assert.IsFalse(File.Exists(Path.Combine(tempDir, "2026-10-01.jsonl")));
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, HistoryStore.DailyHealthFileName)));
            Assert.IsTrue(File.Exists(Path.Combine(tempDir, HistoryStore.ProHistoryMarkerFileName)));

            IReadOnlyList<DailyHealthPoint> dailyPoints = store.ReadDailyHealthPoints();
            Assert.AreEqual(1, dailyPoints.Count);
            Assert.AreEqual(oldDate, dailyPoints[0].Date);
        }
        finally
        {
            CleanupTempDirectory(tempDir);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "AmbystoPowerHistoryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
