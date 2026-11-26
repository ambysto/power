using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Ambysto.Power.Core.Thermal;

public sealed class ThermalEpisodeTracker
{
    private readonly List<ThermalEpisode> _completedEpisodes = [];

    public ThermalEpisodeTracker(IEnumerable<ThermalEpisode>? initialEpisodes = null)
    {
        if (initialEpisodes is not null)
        {
            _completedEpisodes.AddRange(initialEpisodes);
        }
    }

    public ThermalEpisode? OpenEpisode { get; private set; }
    public IReadOnlyList<ThermalEpisode> CompletedEpisodes => _completedEpisodes;

    public (ThermalSnapshot Snapshot, ThermalEpisode? ClosedEpisode) Update(
        ThermalReading reading,
        IReadOnlyList<string> currentTopApps,
        double? batteryTemperatureCelsius = null)
    {
        ArgumentNullException.ThrowIfNull(reading);
        currentTopApps ??= [];

        string[] topThree = currentTopApps
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Take(3)
            .ToArray();

        bool isThrottled = reading.State != ThermalState.Nominal
            || (reading.CpuSpeedLimitPercent is int speedLimit && speedLimit < 100);

        ThermalEpisode? closedEpisode = null;

        if (isThrottled)
        {
            if (OpenEpisode is null)
            {
                TimeSpan initialDuration = TimeSpan.Zero;
                OpenEpisode = new ThermalEpisode(
                    StartUtc: reading.TimestampUtc,
                    EndUtc: null,
                    Duration: initialDuration,
                    WorstState: reading.State,
                    LowestSpeedLimitPercent: reading.CpuSpeedLimitPercent,
                    TopThreeApps: topThree,
                    SummaryText: FormatSummary(reading.TimestampUtc, null, initialDuration, topThree));
            }
            else
            {
                ThermalState worstState = reading.State > OpenEpisode.WorstState
                    ? reading.State
                    : OpenEpisode.WorstState;

                int? lowestLimit = OpenEpisode.LowestSpeedLimitPercent;
                if (reading.CpuSpeedLimitPercent is int curLimit)
                {
                    lowestLimit = lowestLimit is int prevLimit
                        ? Math.Min(prevLimit, curLimit)
                        : curLimit;
                }

                IReadOnlyList<string> apps = topThree.Length > 0 ? topThree : OpenEpisode.TopThreeApps;
                TimeSpan duration = reading.TimestampUtc >= OpenEpisode.StartUtc
                    ? reading.TimestampUtc - OpenEpisode.StartUtc
                    : TimeSpan.Zero;

                OpenEpisode = new ThermalEpisode(
                    StartUtc: OpenEpisode.StartUtc,
                    EndUtc: null,
                    Duration: duration,
                    WorstState: worstState,
                    LowestSpeedLimitPercent: lowestLimit,
                    TopThreeApps: apps,
                    SummaryText: FormatSummary(OpenEpisode.StartUtc, null, duration, apps));
            }
        }
        else if (OpenEpisode is not null)
        {
            TimeSpan duration = reading.TimestampUtc >= OpenEpisode.StartUtc
                ? reading.TimestampUtc - OpenEpisode.StartUtc
                : OpenEpisode.Duration;
            IReadOnlyList<string> apps = OpenEpisode.TopThreeApps.Count > 0 ? OpenEpisode.TopThreeApps : topThree;

            closedEpisode = new ThermalEpisode(
                StartUtc: OpenEpisode.StartUtc,
                EndUtc: reading.TimestampUtc,
                Duration: duration,
                WorstState: OpenEpisode.WorstState,
                LowestSpeedLimitPercent: OpenEpisode.LowestSpeedLimitPercent,
                TopThreeApps: apps,
                SummaryText: FormatSummary(OpenEpisode.StartUtc, reading.TimestampUtc, duration, apps));

            _completedEpisodes.Add(closedEpisode);
            OpenEpisode = null;
        }

        IReadOnlyList<SensorFamilySummary> summaries = SensorAggregator.Aggregate(
            reading.HasVerifiedSensorTable,
            reading.RawSensors,
            batteryTemperatureCelsius);

        string speedText = reading.CpuSpeedLimitPercent is int limitPct
            ? string.Create(CultureInfo.InvariantCulture, $"{limitPct} %")
            : "not reported";

        string warningText = !string.IsNullOrWhiteSpace(reading.ThermalWarningLevel)
            ? reading.ThermalWarningLevel
            : "not reported";

        string sensorsStatus = reading.HasVerifiedSensorTable
            ? "verified sensor table"
            : "no sensors found";

        var snapshot = new ThermalSnapshot(
            State: reading.State,
            CpuSpeedLimitPercent: reading.CpuSpeedLimitPercent,
            CpuSpeedLimitText: speedText,
            ThermalWarningLevelText: warningText,
            HasVerifiedSensors: reading.HasVerifiedSensorTable,
            SensorsStatusText: sensorsStatus,
            SensorSummaries: summaries,
            OpenEpisode: OpenEpisode,
            CompletedEpisodes: _completedEpisodes.ToArray());

        return (snapshot, closedEpisode);
    }

    private static string FormatSummary(
        DateTimeOffset startUtc,
        DateTimeOffset? endUtc,
        TimeSpan duration,
        IReadOnlyList<string> topApps)
    {
        int mins = (int)Math.Max(1, Math.Round(duration.TotalMinutes));
        string startStr = startUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
        string rangeStr = endUtc is DateTimeOffset end
            ? $"{startStr}\u2013{end.ToString("HH:mm", CultureInfo.InvariantCulture)}"
            : $"{startStr}\u2013now";
        string appsStr = topApps.Count > 0 ? string.Join(", ", topApps) : "none";

        return string.Create(
            CultureInfo.InvariantCulture,
            $"throttled {mins} min, {rangeStr}; on top: {appsStr}");
    }
}
