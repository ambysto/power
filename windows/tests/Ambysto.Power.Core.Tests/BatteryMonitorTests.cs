using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Ambysto.Power.Core.Battery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores")]
[assembly: SuppressMessage("Performance", "CA1861:Avoid constant arrays as arguments")]
[assembly: SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable")]
[assembly: SuppressMessage("Usage", "MSTEST0032:Assertion condition is always true")]
[assembly: SuppressMessage("Usage", "MSTEST0037:Use proper 'Assert' methods")]

namespace Ambysto.Power.Core.Tests;

[TestClass]
public sealed class BatteryMonitorTests
{
    private sealed class FakeBatteryReader : IBatteryReader
    {
        public BatteryReading NextReading { get; set; } = CreateBatteryReading(
            timestampUtc: new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero),
            hasBattery: true,
            powerSource: PowerSourceKind.Mains,
            state: BatteryChargingState.Charged,
            chargePercent: 88);

        public BatteryReading ReadBattery() => NextReading;
    }

    [TestMethod]
    public void ComputeHealthPercentReturnsRatioAndHandlesNullOrNonPositiveValues()
    {
        double? health90 = BatteryMonitor.ComputeHealthPercent(45_000, 50_000);
        Assert.IsNotNull(health90);
        Assert.AreEqual(90.0, health90.Value, 0.001);

        double? health85 = BatteryMonitor.ComputeHealthPercent(42_500, 50_000);
        Assert.IsNotNull(health85);
        Assert.AreEqual(85.0, health85.Value, 0.001);

        Assert.IsNull(BatteryMonitor.ComputeHealthPercent(null, 50_000));
        Assert.IsNull(BatteryMonitor.ComputeHealthPercent(45_000, null));
        Assert.IsNull(BatteryMonitor.ComputeHealthPercent(0, 50_000));
        Assert.IsNull(BatteryMonitor.ComputeHealthPercent(-100, 50_000));
        Assert.IsNull(BatteryMonitor.ComputeHealthPercent(45_000, 0));
        Assert.IsNull(BatteryMonitor.ComputeHealthPercent(45_000, -50_000));
    }

    [TestMethod]
    public void ComputeDischargeWattsReturnsNullOnMainsAndComputesWattsOnBattery()
    {
        // Even with non-zero voltage, current, and rate, mains must return null.
        Assert.IsNull(BatteryMonitor.ComputeDischargeWatts(
            PowerSourceKind.Mains,
            voltageMillivolts: 12_000,
            currentMilliamps: -1_500,
            dischargeRateMilliwatts: -18_000));

        // On battery with current (mA) and voltage (mV): |current| * voltage / 1_000_000.0
        double? fromSignedCurrent = BatteryMonitor.ComputeDischargeWatts(
            PowerSourceKind.Battery,
            voltageMillivolts: 12_000,
            currentMilliamps: -1_500,
            dischargeRateMilliwatts: null);
        Assert.IsNotNull(fromSignedCurrent);
        Assert.AreEqual(18.0, fromSignedCurrent.Value, 0.001);

        double? fromPositiveCurrent = BatteryMonitor.ComputeDischargeWatts(
            PowerSourceKind.Battery,
            voltageMillivolts: 12_000,
            currentMilliamps: 1_250,
            dischargeRateMilliwatts: null);
        Assert.IsNotNull(fromPositiveCurrent);
        Assert.AreEqual(15.0, fromPositiveCurrent.Value, 0.001);

        // On battery with dischargeRateMilliwatts: |rate| / 1000.0
        double? fromMilliwatts = BatteryMonitor.ComputeDischargeWatts(
            PowerSourceKind.Battery,
            voltageMillivolts: null,
            currentMilliamps: null,
            dischargeRateMilliwatts: -15_500);
        Assert.IsNotNull(fromMilliwatts);
        Assert.AreEqual(15.5, fromMilliwatts.Value, 0.001);

        Assert.IsNull(BatteryMonitor.ComputeDischargeWatts(
            PowerSourceKind.Battery,
            voltageMillivolts: null,
            currentMilliamps: null,
            dischargeRateMilliwatts: null));
    }

    [TestMethod]
    public void FilterPlausibleTemperatureKeeps5To120InclusiveAndDropsOutliers()
    {
        Assert.AreEqual(5.0, BatteryMonitor.FilterPlausibleTemperature(5.0));
        Assert.AreEqual(36.5, BatteryMonitor.FilterPlausibleTemperature(36.5));
        Assert.AreEqual(120.0, BatteryMonitor.FilterPlausibleTemperature(120.0));

        Assert.IsNull(BatteryMonitor.FilterPlausibleTemperature(null));
        Assert.IsNull(BatteryMonitor.FilterPlausibleTemperature(4.9));
        Assert.IsNull(BatteryMonitor.FilterPlausibleTemperature(0.0));
        Assert.IsNull(BatteryMonitor.FilterPlausibleTemperature(-10.0));
        Assert.IsNull(BatteryMonitor.FilterPlausibleTemperature(120.1));
        Assert.IsNull(BatteryMonitor.FilterPlausibleTemperature(200.0));
    }

    [TestMethod]
    public void FormatTimeRemainingAndSinceUnpluggedMatchSpecText()
    {
        Assert.AreEqual("—", BatteryMonitor.FormatTimeRemaining(null));
        Assert.AreEqual("—", BatteryMonitor.FormatTimeRemaining(TimeSpan.FromSeconds(-1)));
        Assert.AreEqual("—", BatteryMonitor.FormatTimeRemaining(TimeSpan.Zero));
        Assert.AreEqual("1 h 20 min", BatteryMonitor.FormatTimeRemaining(TimeSpan.FromMinutes(80)));
        Assert.AreEqual("45 min", BatteryMonitor.FormatTimeRemaining(TimeSpan.FromMinutes(45)));

        Assert.AreEqual(
            "since you unplugged: −23 % in 1 h 20 min",
            BatteryMonitor.FormatSinceUnplugged(-23, TimeSpan.FromMinutes(80)));
        Assert.AreEqual(
            "since you unplugged: −23 % in 1 h 20 min",
            BatteryMonitor.FormatSinceUnplugged(23, TimeSpan.FromMinutes(80)));
    }

    [TestMethod]
    public void TickDesktopNoBatteryReturnsOneSentenceAndWritesNoSamplesOrDailyPoints()
    {
        var reader = new FakeBatteryReader
        {
            NextReading = new BatteryReading(
                TimestampUtc: new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero),
                HasBattery: false,
                PowerSource: PowerSourceKind.Mains,
                State: BatteryChargingState.Charged,
                ChargePercent: null,
                TimeRemaining: null,
                CycleCount: null,
                RatedCycleLimit: null,
                DesignCapacityMwh: null,
                FullChargeCapacityMwh: null,
                SystemMaxCapacityPercent: null,
                Condition: null,
                TemperatureCelsius: null,
                VoltageMillivolts: null,
                CurrentMilliamps: null,
                DischargeRateMilliwatts: null)
        };

        var monitor = new BatteryMonitor(reader);
        var (snapshot, writtenSample, writtenDaily) = monitor.Tick(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero));

        Assert.IsFalse(snapshot.Current.HasBattery);
        Assert.AreEqual(PowerSourceKind.Mains, snapshot.Current.PowerSource);
        Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.NoBatteryMessage));
        Assert.IsNull(writtenSample);
        Assert.IsNull(writtenDaily);
        Assert.AreEqual(0, snapshot.DailyHealthPoints.Count);
        Assert.AreEqual(0, snapshot.SamplesLast24Hours.Count);
    }

    [TestMethod]
    public void TickEnforcesFiveMinuteAndSourceChangeSamplingAndDailyHealthPointAndUnplugSummary()
    {
        var t0 = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero);
        var reader = new FakeBatteryReader
        {
            NextReading = CreateBatteryReading(
                timestampUtc: t0,
                hasBattery: true,
                powerSource: PowerSourceKind.Mains,
                state: BatteryChargingState.Charged,
                chargePercent: 88,
                fullChargeMwh: 45_000,
                designMwh: 50_000,
                cycleCount: 120,
                tempCelsius: 32.0,
                voltageMv: 12_500,
                currentMa: 0)
        };

        var monitor = new BatteryMonitor(reader);

        // 1. First tick on Mains writes both a sample and the day's DailyHealthPoint.
        var tick1 = monitor.Tick(t0);
        Assert.IsNotNull(tick1.WrittenSample);
        Assert.AreEqual(88, tick1.WrittenSample.ChargePercent);
        Assert.IsNull(tick1.WrittenSample.DischargeWatts);
        Assert.IsNotNull(tick1.WrittenDailyPoint);
        Assert.AreEqual(new DateOnly(2026, 10, 25), tick1.WrittenDailyPoint.Date);
        Assert.AreEqual(new DateOnly(2026, 10, 25), tick1.Snapshot.RecordStartedDate);
        Assert.AreEqual("since 25 Oct 2026", tick1.Snapshot.RecordStartedText);
        Assert.IsNull(tick1.Snapshot.SinceUnplugged);

        // 2. Charge-only change 1 minute later writes NO sample and NO second daily point.
        var t1Min = t0.AddMinutes(1);
        reader.NextReading = CreateBatteryReading(
            timestampUtc: t1Min,
            hasBattery: true,
            powerSource: PowerSourceKind.Mains,
            state: BatteryChargingState.Charging,
            chargePercent: 90);
        var tick2 = monitor.Tick(t1Min);
        Assert.IsNull(tick2.WrittenSample);
        Assert.IsNull(tick2.WrittenDailyPoint);
        Assert.AreEqual(1, tick2.Snapshot.DailyHealthPoints.Count);

        // 3. 5 minutes after the first sample writes a new sample, still 1 daily health point.
        var t5Min = t0.AddMinutes(5);
        reader.NextReading = CreateBatteryReading(
            timestampUtc: t5Min,
            hasBattery: true,
            powerSource: PowerSourceKind.Mains,
            state: BatteryChargingState.Charged,
            chargePercent: 90);
        var tick3 = monitor.Tick(t5Min);
        Assert.IsNotNull(tick3.WrittenSample);
        Assert.AreEqual(90, tick3.WrittenSample.ChargePercent);
        Assert.IsNull(tick3.WrittenDailyPoint);
        Assert.AreEqual(1, tick3.Snapshot.DailyHealthPoints.Count);

        // 4. Transition from Mains (90%) to Battery (89%) 1 minute later immediately writes a sample and starts SinceUnplugged.
        var tUnplug = t5Min.AddMinutes(1);
        reader.NextReading = CreateBatteryReading(
            timestampUtc: tUnplug,
            hasBattery: true,
            powerSource: PowerSourceKind.Battery,
            state: BatteryChargingState.Discharging,
            chargePercent: 89,
            voltageMv: 12_000,
            currentMa: -1_500);
        var tickUnplug = monitor.Tick(tUnplug);
        Assert.IsNotNull(tickUnplug.WrittenSample);
        Assert.AreEqual(PowerSourceKind.Battery, tickUnplug.WrittenSample.PowerSource);
        Assert.IsNotNull(tickUnplug.WrittenSample.DischargeWatts);
        Assert.AreEqual(18.0, tickUnplug.WrittenSample.DischargeWatts.Value, 0.001);
        Assert.IsNotNull(tickUnplug.Snapshot.SinceUnplugged);
        Assert.AreEqual(90, tickUnplug.Snapshot.SinceUnplugged.StartChargePercent);
        Assert.AreEqual(89, tickUnplug.Snapshot.SinceUnplugged.CurrentChargePercent);

        // 5. 1 h 20 min after unplugging at 67% charge: "since you unplugged: −23 % in 1 h 20 min".
        var tLaterOnBattery = tUnplug.AddMinutes(80);
        reader.NextReading = CreateBatteryReading(
            timestampUtc: tLaterOnBattery,
            hasBattery: true,
            powerSource: PowerSourceKind.Battery,
            state: BatteryChargingState.Discharging,
            chargePercent: 67,
            voltageMv: 12_000,
            currentMa: -1_500);
        var tickLater = monitor.Tick(tLaterOnBattery);
        Assert.IsNotNull(tickLater.Snapshot.SinceUnplugged);
        Assert.AreEqual("since you unplugged: −23 % in 1 h 20 min", tickLater.Snapshot.SinceUnplugged.DisplayText);

        // 6. Transition back to Mains immediately writes a sample and clears SinceUnplugged.
        var tPlugBack = tLaterOnBattery.AddMinutes(1);
        reader.NextReading = CreateBatteryReading(
            timestampUtc: tPlugBack,
            hasBattery: true,
            powerSource: PowerSourceKind.Mains,
            state: BatteryChargingState.Charging,
            chargePercent: 67);
        var tickPlugBack = monitor.Tick(tPlugBack);
        Assert.IsNotNull(tickPlugBack.WrittenSample);
        Assert.AreEqual(PowerSourceKind.Mains, tickPlugBack.WrittenSample.PowerSource);
        Assert.IsNull(tickPlugBack.Snapshot.SinceUnplugged);

        // 7. Next local calendar day writes a second DailyHealthPoint while preserving RecordStartedDate.
        var tNextDay = t0.AddDays(1);
        reader.NextReading = CreateBatteryReading(
            timestampUtc: tNextDay,
            hasBattery: true,
            powerSource: PowerSourceKind.Mains,
            state: BatteryChargingState.Charged,
            chargePercent: 100,
            fullChargeMwh: 44_900,
            designMwh: 50_000,
            cycleCount: 121);
        var tickNextDay = monitor.Tick(tNextDay);
        Assert.IsNotNull(tickNextDay.WrittenDailyPoint);
        Assert.AreEqual(new DateOnly(2026, 10, 26), tickNextDay.WrittenDailyPoint.Date);
        Assert.AreEqual(2, tickNextDay.Snapshot.DailyHealthPoints.Count);
        Assert.AreEqual(new DateOnly(2026, 10, 25), tickNextDay.Snapshot.RecordStartedDate);
        Assert.AreEqual("since 25 Oct 2026", tickNextDay.Snapshot.RecordStartedText);
    }

    [TestMethod]
    [TestCategory("Console")]
    public void WindowsBatteryReaderReadsMainsStatusInConsoleSession()
    {
        var reader = new WindowsBatteryReader();
        BatteryReading reading = reader.ReadBattery();
        Assert.AreEqual(PowerSourceKind.Mains, reading.PowerSource);
    }

    [TestMethod]
    [TestCategory("Admin")]
    public void WindowsBatteryReaderReadsMainsStatusInAdminSession()
    {
        var reader = new WindowsBatteryReader();
        BatteryReading reading = reader.ReadBattery();
        Assert.AreEqual(PowerSourceKind.Mains, reading.PowerSource);
    }

    private static BatteryReading CreateBatteryReading(
        DateTimeOffset timestampUtc,
        bool hasBattery,
        PowerSourceKind powerSource,
        BatteryChargingState state,
        int? chargePercent,
        int? fullChargeMwh = 45_000,
        int? designMwh = 50_000,
        int? cycleCount = 120,
        double? tempCelsius = 31.5,
        int? voltageMv = 12_000,
        int? currentMa = 0)
    {
        return new BatteryReading(
            TimestampUtc: timestampUtc,
            HasBattery: hasBattery,
            PowerSource: powerSource,
            State: state,
            ChargePercent: chargePercent,
            TimeRemaining: powerSource == PowerSourceKind.Battery ? TimeSpan.FromMinutes(120) : null,
            CycleCount: cycleCount,
            RatedCycleLimit: 1000,
            DesignCapacityMwh: designMwh,
            FullChargeCapacityMwh: fullChargeMwh,
            SystemMaxCapacityPercent: null,
            Condition: null,
            TemperatureCelsius: tempCelsius,
            VoltageMillivolts: voltageMv,
            CurrentMilliamps: currentMa,
            DischargeRateMilliwatts: null,
            ModelIdentifier: "Windows Test PC");
    }
}
