using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Ambysto.Power.Core.Thermal;

public static class SensorAggregator
{
    public const double MinPlausibleCelsius = 5.0;
    public const double MaxPlausibleCelsius = 120.0;

    private static readonly SensorFamily[] HardwareFamilies =
    [
        SensorFamily.Cpu,
        SensorFamily.Gpu,
        SensorFamily.Ssd,
        SensorFamily.Fans,
        SensorFamily.SystemPower
    ];

    public static ThermalState EvaluateStateFromKelvin(double? maxKelvin)
    {
        if (maxKelvin is not double k || double.IsNaN(k) || k < 343.15)
        {
            return ThermalState.Nominal;
        }

        if (k < 353.15)
        {
            return ThermalState.Fair;
        }

        if (k < 363.15)
        {
            return ThermalState.Serious;
        }

        return ThermalState.Critical;
    }

    public static IReadOnlyList<SensorFamilySummary> Aggregate(
        bool hasVerifiedTable,
        IReadOnlyList<SensorReading> readings,
        double? batteryTemperatureCelsius = null)
    {
        readings ??= [];
        var summaries = new List<SensorFamilySummary>(6);

        foreach (SensorFamily family in HardwareFamilies)
        {
            string label = GetFamilyLabel(family);
            if (!hasVerifiedTable)
            {
                summaries.Add(new SensorFamilySummary(
                    Family: family,
                    HasSensor: false,
                    HottestOrPrimaryValue: null,
                    SensorCount: 0,
                    DisplayText: $"{label}: no sensors found"));
                continue;
            }

            List<SensorReading> valid = readings
                .Where(r => r.Family == family && IsPlausibleValue(family, r.Value))
                .ToList();

            if (valid.Count == 0)
            {
                summaries.Add(new SensorFamilySummary(
                    Family: family,
                    HasSensor: false,
                    HottestOrPrimaryValue: null,
                    SensorCount: 0,
                    DisplayText: $"{label}: no sensors found"));
                continue;
            }

            double primary = valid.Max(r => r.Value);
            int count = valid.Count;
            string displayText = family is SensorFamily.Cpu or SensorFamily.Gpu or SensorFamily.Ssd
                ? count > 1
                    ? string.Create(CultureInfo.InvariantCulture, $"{label} {primary:0.##} °C, hottest of {count} sensors")
                    : string.Create(CultureInfo.InvariantCulture, $"{label} {primary:0.##} °C")
                : string.Create(CultureInfo.InvariantCulture, $"{label} {primary:0.##} ({count})");

            summaries.Add(new SensorFamilySummary(
                Family: family,
                HasSensor: true,
                HottestOrPrimaryValue: primary,
                SensorCount: count,
                DisplayText: displayText));
        }

        if (batteryTemperatureCelsius is double batTemp
            && batTemp >= MinPlausibleCelsius
            && batTemp <= MaxPlausibleCelsius)
        {
            summaries.Add(new SensorFamilySummary(
                Family: SensorFamily.Battery,
                HasSensor: true,
                HottestOrPrimaryValue: batTemp,
                SensorCount: 1,
                DisplayText: string.Create(CultureInfo.InvariantCulture, $"Battery {batTemp:0.##} °C")));
        }
        else
        {
            summaries.Add(new SensorFamilySummary(
                Family: SensorFamily.Battery,
                HasSensor: false,
                HottestOrPrimaryValue: null,
                SensorCount: 0,
                DisplayText: "Battery: no sensors found"));
        }

        return summaries;
    }

    private static bool IsPlausibleValue(SensorFamily family, double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return false;
        }

        if (family is SensorFamily.Cpu or SensorFamily.Gpu or SensorFamily.Ssd or SensorFamily.Battery)
        {
            return value >= MinPlausibleCelsius && value <= MaxPlausibleCelsius;
        }

        return value >= 0.0;
    }

    private static string GetFamilyLabel(SensorFamily family) =>
        family switch
        {
            SensorFamily.Cpu => "CPU",
            SensorFamily.Gpu => "GPU",
            SensorFamily.Ssd => "SSD",
            SensorFamily.Fans => "Fans",
            SensorFamily.SystemPower => "System Power",
            SensorFamily.Battery => "Battery",
            _ => family.ToString()
        };
}
