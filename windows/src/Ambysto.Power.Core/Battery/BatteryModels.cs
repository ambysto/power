using System;
using System.Collections.Generic;

namespace Ambysto.Power.Core.Battery;

public enum PowerSourceKind { Mains, Battery }
public enum BatteryChargingState { Charged, Charging, Discharging }

public sealed record BatteryReading(
    DateTimeOffset TimestampUtc,
    bool HasBattery,
    PowerSourceKind PowerSource,
    BatteryChargingState State,
    int? ChargePercent,
    TimeSpan? TimeRemaining,
    int? CycleCount,
    int? RatedCycleLimit,
    int? DesignCapacityMwh,
    int? FullChargeCapacityMwh,
    int? SystemMaxCapacityPercent,
    string? Condition,
    double? TemperatureCelsius,
    int? VoltageMillivolts,
    int? CurrentMilliamps,
    int? DischargeRateMilliwatts,
    string ModelIdentifier = "Windows PC");

public sealed record BatterySamplePoint(
    DateTimeOffset TimestampUtc,
    int ChargePercent,
    PowerSourceKind PowerSource,
    BatteryChargingState State,
    int? FullChargeCapacityMwh,
    int? CycleCount,
    double? TemperatureCelsius,
    double? DischargeWatts);

public sealed record DailyHealthPoint(
    DateOnly Date,
    int FullChargeCapacityMwh,
    int CycleCount,
    int DesignCapacityMwh,
    string Model);

public sealed record UnplugSummary(
    DateTimeOffset UnpluggedAtUtc,
    int StartChargePercent,
    int CurrentChargePercent,
    int DeltaChargePercent,
    TimeSpan Elapsed,
    string DisplayText);

public sealed record BatterySnapshot(
    BatteryReading Current,
    double? HealthPercent,
    double? DischargeWatts,
    string TimeRemainingText,
    string NoBatteryMessage,
    DateOnly? RecordStartedDate,
    string? RecordStartedText,
    UnplugSummary? SinceUnplugged,
    IReadOnlyList<BatterySamplePoint> SamplesLast24Hours,
    IReadOnlyList<BatterySamplePoint> SamplesLast7Days,
    IReadOnlyList<DailyHealthPoint> DailyHealthPoints);
