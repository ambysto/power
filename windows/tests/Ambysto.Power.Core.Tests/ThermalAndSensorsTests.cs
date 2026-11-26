using System;
using System.Collections.Generic;
using System.Linq;
using Ambysto.Power.Core.Thermal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Ambysto.Power.Core.Tests;

[TestClass]
public sealed class ThermalAndSensorsTests
{
    [TestMethod]
    public void EvaluateStateFromKelvinMapsThresholdsAccurately()
    {
        Assert.AreEqual(ThermalState.Nominal, SensorAggregator.EvaluateStateFromKelvin(null));
        Assert.AreEqual(ThermalState.Nominal, SensorAggregator.EvaluateStateFromKelvin(300.0));
        Assert.AreEqual(ThermalState.Nominal, SensorAggregator.EvaluateStateFromKelvin(343.14));

        Assert.AreEqual(ThermalState.Fair, SensorAggregator.EvaluateStateFromKelvin(343.15));
        Assert.AreEqual(ThermalState.Fair, SensorAggregator.EvaluateStateFromKelvin(348.15));
        Assert.AreEqual(ThermalState.Fair, SensorAggregator.EvaluateStateFromKelvin(353.14));

        Assert.AreEqual(ThermalState.Serious, SensorAggregator.EvaluateStateFromKelvin(353.15));
        Assert.AreEqual(ThermalState.Serious, SensorAggregator.EvaluateStateFromKelvin(358.15));
        Assert.AreEqual(ThermalState.Serious, SensorAggregator.EvaluateStateFromKelvin(363.14));

        Assert.AreEqual(ThermalState.Critical, SensorAggregator.EvaluateStateFromKelvin(363.15));
        Assert.AreEqual(ThermalState.Critical, SensorAggregator.EvaluateStateFromKelvin(380.0));
    }

    [TestMethod]
    public void AggregateWithoutVerifiedTableReturnsNoSensorsFoundAndNoNumbersForHardwareFamilies()
    {
        IReadOnlyList<SensorReading> rawReadings =
        [
            new SensorReading(SensorFamily.Cpu, "Tp01", 85.0, "°C"),
            new SensorReading(SensorFamily.Gpu, "Tg01", 75.0, "°C")
        ];

        IReadOnlyList<SensorFamilySummary> summaries = SensorAggregator.Aggregate(
            hasVerifiedTable: false,
            readings: rawReadings,
            batteryTemperatureCelsius: 33.0);

        SensorFamilySummary cpu = summaries.First(s => s.Family == SensorFamily.Cpu);
        Assert.IsFalse(cpu.HasSensor);
        Assert.IsNull(cpu.HottestOrPrimaryValue);
        Assert.AreEqual(0, cpu.SensorCount);
        StringAssert.Contains(cpu.DisplayText, "no sensors found", StringComparison.OrdinalIgnoreCase);

        SensorFamilySummary gpu = summaries.First(s => s.Family == SensorFamily.Gpu);
        Assert.IsFalse(gpu.HasSensor);
        Assert.IsNull(gpu.HottestOrPrimaryValue);
        StringAssert.Contains(gpu.DisplayText, "no sensors found", StringComparison.OrdinalIgnoreCase);

        SensorFamilySummary ssd = summaries.First(s => s.Family == SensorFamily.Ssd);
        Assert.IsFalse(ssd.HasSensor);
        Assert.IsNull(ssd.HottestOrPrimaryValue);
        StringAssert.Contains(ssd.DisplayText, "no sensors found", StringComparison.OrdinalIgnoreCase);

        SensorFamilySummary battery = summaries.First(s => s.Family == SensorFamily.Battery);
        Assert.IsTrue(battery.HasSensor);
        Assert.IsNotNull(battery.HottestOrPrimaryValue);
        Assert.AreEqual(33.0, battery.HottestOrPrimaryValue.Value, 0.001);
    }

    [TestMethod]
    public void AggregateWithVerifiedTableFiltersOutliersPicksHottestWithCountAndNeverNamesACore()
    {
        IReadOnlyList<SensorReading> rawReadings =
        [
            new SensorReading(SensorFamily.Cpu, "CPU Core 0", 72.0, "°C"),
            new SensorReading(SensorFamily.Cpu, "CPU Core 1", 91.0, "°C"),
            new SensorReading(SensorFamily.Cpu, "CPU Core 2", 84.0, "°C"),
            new SensorReading(SensorFamily.Cpu, "CPU Outlier Low", 4.9, "°C"),
            new SensorReading(SensorFamily.Cpu, "CPU Outlier High", 120.1, "°C"),
            new SensorReading(SensorFamily.Gpu, "GPU Die 0", 5.0, "°C"),
            new SensorReading(SensorFamily.Gpu, "GPU Die 1", 120.0, "°C"),
            new SensorReading(SensorFamily.Ssd, "SSD Unplausible", -10.0, "°C")
        ];

        IReadOnlyList<SensorFamilySummary> summaries = SensorAggregator.Aggregate(
            hasVerifiedTable: true,
            readings: rawReadings,
            batteryTemperatureCelsius: 150.0); // Out-of-bounds battery temp should be dropped.

        SensorFamilySummary cpu = summaries.First(s => s.Family == SensorFamily.Cpu);
        Assert.IsTrue(cpu.HasSensor);
        Assert.IsNotNull(cpu.HottestOrPrimaryValue);
        Assert.AreEqual(91.0, cpu.HottestOrPrimaryValue.Value, 0.001);
        Assert.AreEqual(3, cpu.SensorCount);
        Assert.AreEqual("CPU 91 °C, hottest of 3 sensors", cpu.DisplayText);
        Assert.IsFalse(cpu.DisplayText.Contains("Core", StringComparison.OrdinalIgnoreCase));

        SensorFamilySummary gpu = summaries.First(s => s.Family == SensorFamily.Gpu);
        Assert.IsTrue(gpu.HasSensor);
        Assert.IsNotNull(gpu.HottestOrPrimaryValue);
        Assert.AreEqual(120.0, gpu.HottestOrPrimaryValue.Value, 0.001);
        Assert.AreEqual(2, gpu.SensorCount);

        SensorFamilySummary ssd = summaries.First(s => s.Family == SensorFamily.Ssd);
        Assert.IsFalse(ssd.HasSensor);
        Assert.IsNull(ssd.HottestOrPrimaryValue);
        StringAssert.Contains(ssd.DisplayText, "no sensors found", StringComparison.OrdinalIgnoreCase);

        SensorFamilySummary battery = summaries.First(s => s.Family == SensorFamily.Battery);
        Assert.IsFalse(battery.HasSensor);
        Assert.IsNull(battery.HottestOrPrimaryValue);
    }

    [TestMethod]
    public void ThermalEpisodeTrackerStaysClosedOnNominalAndOpensTracksAndClosesEpisodes()
    {
        var tracker = new ThermalEpisodeTracker();
        var t0 = new DateTimeOffset(2026, 10, 25, 11, 0, 0, TimeSpan.Zero);

        // 1. Nominal state and null/100 speed limit -> stays closed.
        var normalTick = tracker.Update(
            new ThermalReading(
                TimestampUtc: t0,
                State: ThermalState.Nominal,
                CpuSpeedLimitPercent: null,
                ThermalWarningLevel: null,
                HasVerifiedSensorTable: false,
                RawSensors: []),
            currentTopApps: ["Browser", "Editor"]);

        Assert.IsNull(tracker.OpenEpisode);
        Assert.IsNull(normalTick.Snapshot.OpenEpisode);
        Assert.IsNull(normalTick.ClosedEpisode);
        Assert.AreEqual("not reported", normalTick.Snapshot.CpuSpeedLimitText);
        Assert.AreEqual("not reported", normalTick.Snapshot.ThermalWarningLevelText);

        // 2. Fair state with 85% speed limit opens an episode.
        var tStart = t0.AddMinutes(2);
        var openTick1 = tracker.Update(
            new ThermalReading(
                TimestampUtc: tStart,
                State: ThermalState.Fair,
                CpuSpeedLimitPercent: 85,
                ThermalWarningLevel: "Moderate",
                HasVerifiedSensorTable: false,
                RawSensors: []),
            currentTopApps: ["Xcode", "Chrome", "Spotlight", "Terminal"]);

        Assert.IsNotNull(openTick1.Snapshot.OpenEpisode);
        Assert.IsNull(openTick1.ClosedEpisode);
        Assert.AreEqual(ThermalState.Fair, openTick1.Snapshot.OpenEpisode.WorstState);
        Assert.AreEqual(85, openTick1.Snapshot.OpenEpisode.LowestSpeedLimitPercent);
        Assert.AreEqual(3, openTick1.Snapshot.OpenEpisode.TopThreeApps.Count);

        // 3. Escalates to Serious and 70% speed limit -> updates WorstState and LowestSpeedLimitPercent.
        var tMid = tStart.AddMinutes(7);
        var openTick2 = tracker.Update(
            new ThermalReading(
                TimestampUtc: tMid,
                State: ThermalState.Serious,
                CpuSpeedLimitPercent: 70,
                ThermalWarningLevel: "Heavy",
                HasVerifiedSensorTable: false,
                RawSensors: []),
            currentTopApps: ["Compiler", "Chrome", "Spotlight"]);

        Assert.IsNotNull(openTick2.Snapshot.OpenEpisode);
        Assert.IsNull(openTick2.ClosedEpisode);
        Assert.AreEqual(ThermalState.Serious, openTick2.Snapshot.OpenEpisode.WorstState);
        Assert.AreEqual(70, openTick2.Snapshot.OpenEpisode.LowestSpeedLimitPercent);

        // 4. State returns to Nominal, but speed limit is still 90% (< 100%) -> episode remains open!
        var tStillThrottled = tStart.AddMinutes(10);
        var openTick3 = tracker.Update(
            new ThermalReading(
                TimestampUtc: tStillThrottled,
                State: ThermalState.Nominal,
                CpuSpeedLimitPercent: 90,
                ThermalWarningLevel: null,
                HasVerifiedSensorTable: false,
                RawSensors: []),
            currentTopApps: ["Compiler", "Chrome", "Spotlight"]);

        Assert.IsNotNull(openTick3.Snapshot.OpenEpisode);
        Assert.IsNull(openTick3.ClosedEpisode);
        Assert.AreEqual(ThermalState.Serious, openTick3.Snapshot.OpenEpisode.WorstState);
        Assert.AreEqual(70, openTick3.Snapshot.OpenEpisode.LowestSpeedLimitPercent);

        // 5. Both State == Nominal and CpuSpeedLimitPercent == 100 -> closes episode and emits ClosedEpisode.
        var tEnd = tStart.AddMinutes(14);
        var closeTick = tracker.Update(
            new ThermalReading(
                TimestampUtc: tEnd,
                State: ThermalState.Nominal,
                CpuSpeedLimitPercent: 100,
                ThermalWarningLevel: null,
                HasVerifiedSensorTable: false,
                RawSensors: []),
            currentTopApps: ["Compiler", "Chrome", "Spotlight"]);

        Assert.IsNull(closeTick.Snapshot.OpenEpisode);
        Assert.IsNotNull(closeTick.ClosedEpisode);
        Assert.AreEqual(TimeSpan.FromMinutes(14), closeTick.ClosedEpisode.Duration);
        Assert.AreEqual(ThermalState.Serious, closeTick.ClosedEpisode.WorstState);
        Assert.AreEqual(70, closeTick.ClosedEpisode.LowestSpeedLimitPercent);
        StringAssert.Contains(closeTick.ClosedEpisode.SummaryText, "throttled", StringComparison.OrdinalIgnoreCase);
        StringAssert.Contains(closeTick.ClosedEpisode.SummaryText, "; on top:", StringComparison.OrdinalIgnoreCase);
        Assert.AreEqual(1, closeTick.Snapshot.CompletedEpisodes.Count);

        var rehydratedTracker = new ThermalEpisodeTracker(closeTick.Snapshot.CompletedEpisodes);
        Assert.AreEqual(1, rehydratedTracker.CompletedEpisodes.Count);
        var verifiedTick = rehydratedTracker.Update(
            new ThermalReading(
                TimestampUtc: tEnd.AddMinutes(1),
                State: ThermalState.Nominal,
                CpuSpeedLimitPercent: 100,
                ThermalWarningLevel: "None",
                HasVerifiedSensorTable: true,
                RawSensors: [new SensorReading(SensorFamily.Fans, "Fan0", 1800, "RPM")]),
            currentTopApps: [],
            batteryTemperatureCelsius: 32.0);
        Assert.IsTrue(verifiedTick.Snapshot.HasVerifiedSensors);
        Assert.AreEqual("verified sensor table", verifiedTick.Snapshot.SensorsStatusText);
        Assert.AreEqual("100 %", verifiedTick.Snapshot.CpuSpeedLimitText);
        Assert.AreEqual("None", verifiedTick.Snapshot.ThermalWarningLevelText);
    }

    [TestMethod]
    [TestCategory("Console")]
    public void WindowsThermalReaderReadsWithoutThrowingInConsoleSession()
    {
        using var reader = new WindowsThermalReader();
        ThermalReading reading = reader.ReadThermal();
        Assert.IsFalse(reading.HasVerifiedSensorTable);
    }
}
