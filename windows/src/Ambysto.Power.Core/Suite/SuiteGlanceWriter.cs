using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.Thermal;

namespace Ambysto.Power.Core.Suite;

public sealed record SuiteGlanceMetric(string Id, string Label, string Value, string Unit, string Level);

public sealed record SuiteGlanceRecord(
    string AppId,
    DateTimeOffset UpdatedAtUtc,
    string Level,
    IReadOnlyList<SuiteGlanceMetric> Metrics);

public sealed class SuiteGlanceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _glancesDirectory;

    public SuiteGlanceWriter(string suiteFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suiteFolderPath);
        _glancesDirectory = Path.Combine(suiteFolderPath, "glances");
        GlanceFilePath = Path.Combine(_glancesDirectory, "power.json");
    }

    public string GlanceFilePath { get; }

    public static SuiteGlanceRecord BuildRecord(
        BatteryReading battery,
        ThermalState thermalState,
        bool keepAwakeActive,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(battery);

        string overallLevel = thermalState switch
        {
            ThermalState.Critical => "critical",
            ThermalState.Serious => "warn",
            _ => "ok"
        };

        string chargeValue = battery.HasBattery && battery.ChargePercent is int pct
            ? pct.ToString(CultureInfo.InvariantCulture)
            : "Mains";
        string chargeUnit = battery.HasBattery && battery.ChargePercent is not null ? "%" : string.Empty;

        var metrics = new List<SuiteGlanceMetric>
        {
            new(
                Id: "battery",
                Label: "Battery",
                Value: chargeValue,
                Unit: chargeUnit,
                Level: "ok"),
            new(
                Id: "thermal",
                Label: "Thermal",
                Value: thermalState.ToString().ToLowerInvariant(),
                Unit: string.Empty,
                Level: overallLevel),
            new(
                Id: "keep_awake",
                Label: "Keep-Awake",
                Value: keepAwakeActive ? "on" : "off",
                Unit: string.Empty,
                Level: "ok")
        };

        return new SuiteGlanceRecord(
            AppId: "power",
            UpdatedAtUtc: nowUtc,
            Level: overallLevel,
            Metrics: metrics);
    }

    public void WriteGlance(
        BatteryReading battery,
        ThermalState thermalState,
        bool keepAwakeActive,
        DateTimeOffset nowUtc)
    {
        SuiteGlanceRecord record = BuildRecord(battery, thermalState, keepAwakeActive, nowUtc);
        Directory.CreateDirectory(_glancesDirectory);
        string json = JsonSerializer.Serialize(record, JsonOptions);
        File.WriteAllText(GlanceFilePath, json);
    }
}
