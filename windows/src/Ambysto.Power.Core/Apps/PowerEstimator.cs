using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ambysto.Kit.Processes;

namespace Ambysto.Power.Core.Apps;

public sealed class PowerEstimator
{
    public const double WakeupCostSeconds = 0.0005;
    public const int MaxHourlyReadings = 12;

    private readonly List<AppPowerReading> _readings = [];
    private readonly Dictionary<string, MutableGroup> _currentGroups = new(StringComparer.Ordinal);
    private DateTimeOffset? _currentWindowStartUtc;
    private int _lastUnreadableCount;

    public PowerEstimator(IEnumerable<AppPowerReading>? initialReadings = null)
    {
        if (initialReadings is not null)
        {
            foreach (AppPowerReading reading in initialReadings)
            {
                _readings.Add(reading);
            }

            while (_readings.Count > MaxHourlyReadings)
            {
                _readings.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<AppPowerReading> Readings => _readings;

    public static double ComputeProcessImpact(
        double cpuSeconds,
        long idleWakeups,
        long? energyNanojoules,
        bool windowUsesEnergy)
    {
        if (windowUsesEnergy)
        {
            return energyNanojoules is > 0 ? energyNanojoules.Value : 0.0;
        }

        double safeCpu = Math.Max(0.0, cpuSeconds);
        long safeWakeups = Math.Max(0L, idleWakeups);
        return safeCpu + (safeWakeups * WakeupCostSeconds);
    }

    public static IReadOnlyList<AppPowerRow> ComputeShares(
        IReadOnlyList<(string GroupKey, string DisplayName, double CpuSeconds, long IdleWakeups, long? EnergyNanojoules, int ProcessCount)> aggregated)
    {
        if (aggregated is null || aggregated.Count == 0)
        {
            return [];
        }

        bool windowUsesEnergy = false;
        foreach (var item in aggregated)
        {
            if (item.EnergyNanojoules is > 0)
            {
                windowUsesEnergy = true;
                break;
            }
        }

        var impacts = new double[aggregated.Count];
        double totalImpact = 0.0;

        for (int i = 0; i < aggregated.Count; i++)
        {
            var item = aggregated[i];
            double impact = ComputeProcessImpact(
                item.CpuSeconds,
                item.IdleWakeups,
                item.EnergyNanojoules,
                windowUsesEnergy);
            impacts[i] = impact;
            totalImpact += impact;
        }

        var rows = new List<AppPowerRow>(aggregated.Count);
        for (int i = 0; i < aggregated.Count; i++)
        {
            var item = aggregated[i];
            double impact = impacts[i];
            double share = totalImpact > 0.0 ? (impact / totalImpact) * 100.0 : 0.0;
            rows.Add(new AppPowerRow(
                GroupKey: item.GroupKey,
                DisplayName: item.DisplayName,
                CpuSeconds: item.CpuSeconds,
                IdleWakeups: item.IdleWakeups,
                EnergyNanojoules: item.EnergyNanojoules,
                RawImpact: impact,
                SharePercent: share,
                ProcessCount: Math.Max(1, item.ProcessCount)));
        }

        rows.Sort((a, b) =>
        {
            int cmp = b.SharePercent.CompareTo(a.SharePercent);
            return cmp != 0 ? cmp : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        return rows;
    }

    public void RecordSample(
        ProcessSample sample,
        DateTimeOffset nowUtc,
        Func<string, string?>? fileDescriptionLookup = null)
    {
        ArgumentNullException.ThrowIfNull(sample);

        _currentWindowStartUtc ??= nowUtc;
        _lastUnreadableCount = sample.UnreadableProcessCount;

        var byPid = new Dictionary<int, ProcessRecord>();
        foreach (ProcessRecord proc in sample.AllProcesses)
        {
            if (proc.IsReadable)
            {
                byPid[proc.Identity.ProcessId] = proc;
            }
        }

        foreach (SampledProcess sampled in sample.Processes)
        {
            if (!sampled.Process.IsReadable)
            {
                continue;
            }

            string groupKey = AppPowerGrouper.ResolveGroupKeyAndName(
                sampled.Process,
                byPid,
                fileDescriptionLookup,
                out string displayName);

            long deltaCpuNs = sampled.Deltas?.CpuTimeNanoseconds ?? 0L;
            double cpuSeconds = Math.Max(0.0, deltaCpuNs / 1_000_000_000.0);
            if (!_currentGroups.TryGetValue(groupKey, out MutableGroup? group))
            {
                group = new MutableGroup(groupKey, displayName);
                _currentGroups[groupKey] = group;
            }

            group.CpuSeconds += cpuSeconds;
            group.Pids.Add(sampled.Process.Identity.ProcessId);
        }
    }

    public AppPowerReading? FlushFiveMinuteReading(DateTimeOffset nowUtc)
    {
        if (_currentGroups.Count == 0)
        {
            return null;
        }

        var tuples = _currentGroups.Values
            .Select(g => (g.GroupKey, g.DisplayName, g.CpuSeconds, g.IdleWakeups, g.EnergyNanojoules, Math.Max(1, g.Pids.Count)))
            .ToArray();

        IReadOnlyList<AppPowerRow> rows = ComputeShares(tuples);
        DateTimeOffset startUtc = _currentWindowStartUtc ?? nowUtc.AddMinutes(-5);
        var reading = new AppPowerReading(
            WindowStartUtc: startUtc,
            WindowEndUtc: nowUtc,
            Apps: rows,
            UnreadableProcessCount: _lastUnreadableCount);

        _readings.Add(reading);
        while (_readings.Count > MaxHourlyReadings)
        {
            _readings.RemoveAt(0);
        }

        _currentGroups.Clear();
        _currentWindowStartUtc = null;
        return reading;
    }

    public AppPowerSnapshot GetSnapshot(
        PowerWindowKind window,
        DateTimeOffset nowUtc,
        DateTimeOffset? sinceUnpluggedUtc = null)
    {
        IReadOnlyList<AppPowerRow> rows;
        int unreadableCount = _lastUnreadableCount;

        switch (window)
        {
            case PowerWindowKind.Last5Minutes:
            {
                if (_currentGroups.Count > 0)
                {
                    var tuples = _currentGroups.Values
                        .Select(g => (g.GroupKey, g.DisplayName, g.CpuSeconds, g.IdleWakeups, g.EnergyNanojoules, Math.Max(1, g.Pids.Count)))
                        .ToArray();
                    rows = ComputeShares(tuples);
                }
                else if (_readings.Count > 0)
                {
                    AppPowerReading last = _readings[^1];
                    rows = last.Apps;
                    unreadableCount = last.UnreadableProcessCount;
                }
                else
                {
                    rows = [];
                }

                break;
            }

            case PowerWindowKind.LastHour:
            {
                rows = AggregateReadings(_readings, includeCurrentBuffer: _readings.Count == 0, out int maxUnreadable);
                if (maxUnreadable > 0)
                {
                    unreadableCount = maxUnreadable;
                }

                break;
            }

            case PowerWindowKind.SinceUnplugged:
            {
                DateTimeOffset cutoff = sinceUnpluggedUtc ?? DateTimeOffset.MinValue;
                List<AppPowerReading> filtered = _readings
                    .Where(r => r.WindowEndUtc > cutoff)
                    .ToList();
                bool includeCurrent = _currentGroups.Count > 0
                    && (_currentWindowStartUtc ?? nowUtc) >= cutoff;
                rows = AggregateReadings(filtered, includeCurrentBuffer: includeCurrent, out int maxUnreadable);
                if (maxUnreadable > 0)
                {
                    unreadableCount = maxUnreadable;
                }

                break;
            }

            default:
                rows = [];
                break;
        }

        return new AppPowerSnapshot(
            Window: window,
            Rows: rows,
            UnreadableProcessCount: unreadableCount,
            ColumnHeader: "estimate",
            GpuNote: "GPU not measured",
            UnmeasuredNote: string.Create(CultureInfo.InvariantCulture, $"{unreadableCount} not measured"));
    }

    private IReadOnlyList<AppPowerRow> AggregateReadings(
        IReadOnlyList<AppPowerReading> readings,
        bool includeCurrentBuffer,
        out int maxUnreadable)
    {
        maxUnreadable = 0;
        var merged = new Dictionary<string, (string DisplayName, double CpuSeconds, long IdleWakeups, long? EnergyNanojoules, int MaxProcs)>(StringComparer.Ordinal);

        foreach (AppPowerReading reading in readings)
        {
            if (reading.UnreadableProcessCount > maxUnreadable)
            {
                maxUnreadable = reading.UnreadableProcessCount;
            }

            foreach (AppPowerRow row in reading.Apps)
            {
                if (!merged.TryGetValue(row.GroupKey, out var existing))
                {
                    merged[row.GroupKey] = (row.DisplayName, row.CpuSeconds, row.IdleWakeups, row.EnergyNanojoules, row.ProcessCount);
                }
                else
                {
                    long? sumEnergy = existing.EnergyNanojoules is not null || row.EnergyNanojoules is not null
                        ? (existing.EnergyNanojoules ?? 0L) + (row.EnergyNanojoules ?? 0L)
                        : null;
                    merged[row.GroupKey] = (
                        existing.DisplayName,
                        existing.CpuSeconds + row.CpuSeconds,
                        existing.IdleWakeups + row.IdleWakeups,
                        sumEnergy,
                        Math.Max(existing.MaxProcs, row.ProcessCount));
                }
            }
        }

        if (includeCurrentBuffer && _currentGroups.Count > 0)
        {
            if (_lastUnreadableCount > maxUnreadable)
            {
                maxUnreadable = _lastUnreadableCount;
            }

            foreach (MutableGroup g in _currentGroups.Values)
            {
                int procCount = Math.Max(1, g.Pids.Count);
                if (!merged.TryGetValue(g.GroupKey, out var existing))
                {
                    merged[g.GroupKey] = (g.DisplayName, g.CpuSeconds, g.IdleWakeups, g.EnergyNanojoules, procCount);
                }
                else
                {
                    merged[g.GroupKey] = (
                        existing.DisplayName,
                        existing.CpuSeconds + g.CpuSeconds,
                        existing.IdleWakeups + g.IdleWakeups,
                        existing.EnergyNanojoules,
                        Math.Max(existing.MaxProcs, procCount));
                }
            }
        }

        var tuples = merged
            .Select(kvp => (
                kvp.Key,
                kvp.Value.DisplayName,
                kvp.Value.CpuSeconds,
                kvp.Value.IdleWakeups,
                kvp.Value.EnergyNanojoules,
                kvp.Value.MaxProcs))
            .ToArray();

        return ComputeShares(tuples);
    }

    private sealed class MutableGroup(string groupKey, string displayName)
    {
        public string GroupKey { get; } = groupKey;
        public string DisplayName { get; } = displayName;
        public double CpuSeconds { get; set; }
        public long IdleWakeups { get; set; }
        public long? EnergyNanojoules { get; set; }
        public HashSet<int> Pids { get; } = [];
    }
}
