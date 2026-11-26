using System;
using System.Collections.Generic;

namespace Ambysto.Power.Core.Thermal;

public enum ThermalState
{
    Nominal = 0,
    Fair = 1,
    Serious = 2,
    Critical = 3
}

public enum SensorFamily
{
    Cpu,
    Gpu,
    Ssd,
    Fans,
    SystemPower,
    Battery
}

public sealed record SensorReading(
    SensorFamily Family,
    string Key,
    double Value,
    string Unit);

public sealed record SensorFamilySummary(
    SensorFamily Family,
    bool HasSensor,
    double? HottestOrPrimaryValue,
    int SensorCount,
    string DisplayText);

public sealed record ThermalEpisode(
    DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc,
    TimeSpan Duration,
    ThermalState WorstState,
    int? LowestSpeedLimitPercent,
    IReadOnlyList<string> TopThreeApps,
    string SummaryText);

public sealed record ThermalReading(
    DateTimeOffset TimestampUtc,
    ThermalState State,
    int? CpuSpeedLimitPercent,
    string? ThermalWarningLevel,
    bool HasVerifiedSensorTable,
    IReadOnlyList<SensorReading> RawSensors);

public sealed record ThermalSnapshot(
    ThermalState State,
    int? CpuSpeedLimitPercent,
    string CpuSpeedLimitText,
    string ThermalWarningLevelText,
    bool HasVerifiedSensors,
    string SensorsStatusText,
    IReadOnlyList<SensorFamilySummary> SensorSummaries,
    ThermalEpisode? OpenEpisode,
    IReadOnlyList<ThermalEpisode> CompletedEpisodes);
