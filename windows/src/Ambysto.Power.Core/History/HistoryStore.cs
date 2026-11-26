using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ambysto.Power.Core.Apps;
using Ambysto.Power.Core.Battery;
using Ambysto.Power.Core.Thermal;

namespace Ambysto.Power.Core.History;

public sealed partial class HistoryStore
{
    public const int FreeRetentionDays = 7;
    public const string DailyHealthFileName = "daily-health.jsonl";
    public const string ProHistoryMarkerFileName = "pro-history";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [LibraryImport("kernel32.dll", EntryPoint = "DeleteFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteFileW(string lpFileName);

    public HistoryStore(string historyDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(historyDirectoryPath);
        HistoryDirectoryPath = Path.GetFullPath(historyDirectoryPath);
        Directory.CreateDirectory(HistoryDirectoryPath);
    }

    public string HistoryDirectoryPath { get; }

    public bool HasProHistoryMarker =>
        File.Exists(Path.Combine(HistoryDirectoryPath, ProHistoryMarkerFileName));

    public static bool IsExactDayFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || fileName.Length != 16)
        {
            return false;
        }

        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            return false;
        }

        if (!fileName.EndsWith(".jsonl", StringComparison.Ordinal))
        {
            return false;
        }

        ReadOnlySpan<char> datePart = fileName.AsSpan(0, 10);
        return DateOnly.TryParseExact(
            datePart,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    public PruneEvaluation EvaluateRetention(
        DateOnly todayLocal,
        bool isPro,
        bool hasAnyStoredKeyText)
    {
        List<string> eligible = FindEligibleDayFilesOlderThan7Days(todayLocal);
        bool shouldAutoPrune = !isPro && !hasAnyStoredKeyText && !HasProHistoryMarker;
        bool olderDataHiddenInFree = !isPro && !shouldAutoPrune && eligible.Count > 0;
        return new PruneEvaluation(shouldAutoPrune, olderDataHiddenInFree, eligible);
    }

    public IReadOnlyList<string> RunLaunchRetention(
        DateOnly todayLocal,
        bool isPro,
        bool hasAnyStoredKeyText)
    {
        PruneEvaluation evaluation = EvaluateRetention(todayLocal, isPro, hasAnyStoredKeyText);
        if (!evaluation.ShouldAutoPruneAtLaunch)
        {
            return [];
        }

        return DeleteHistoryOlderThan7Days(todayLocal);
    }

    public IReadOnlyList<string> DeleteHistoryOlderThan7Days(DateOnly todayLocal)
    {
        if (!IsSafeHistoryDirectory())
        {
            return [];
        }

        List<string> eligible = FindEligibleDayFilesOlderThan7Days(todayLocal);
        var deleted = new List<string>(eligible.Count);

        foreach (string fileName in eligible)
        {
            if (!IsExactDayFileName(fileName))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(Path.Combine(HistoryDirectoryPath, fileName));
            if (!IsChildOfHistoryDirectory(fullPath) || !IsSafeRegularFile(fullPath))
            {
                continue;
            }

            if (DeleteFileW(fullPath))
            {
                deleted.Add(fileName);
            }
        }

        return deleted;
    }

    public void AppendBatterySample(BatterySamplePoint sample, DateOnly localDate, bool isPro)
    {
        ArgumentNullException.ThrowIfNull(sample);
        EnsureProMarkerIfPro(isPro);
        AppendDayEnvelope(localDate, new DayRecordEnvelope("battery", sample, null, null));
    }

    public void AppendDailyHealthPoint(DailyHealthPoint point, bool isPro)
    {
        ArgumentNullException.ThrowIfNull(point);
        EnsureProMarkerIfPro(isPro);

        Directory.CreateDirectory(HistoryDirectoryPath);
        string path = Path.Combine(HistoryDirectoryPath, DailyHealthFileName);
        string line = JsonSerializer.Serialize(point, JsonOptions) + Environment.NewLine;
        File.AppendAllText(path, line);
    }

    public void AppendAppPowerReading(AppPowerReading reading, DateOnly localDate, bool isPro)
    {
        ArgumentNullException.ThrowIfNull(reading);
        EnsureProMarkerIfPro(isPro);
        AppendDayEnvelope(localDate, new DayRecordEnvelope("app", null, reading, null));
    }

    public void AppendThermalEpisode(ThermalEpisode episode, DateOnly localDate, bool isPro)
    {
        ArgumentNullException.ThrowIfNull(episode);
        EnsureProMarkerIfPro(isPro);
        AppendDayEnvelope(localDate, new DayRecordEnvelope("thermal", null, null, episode));
    }

    public IReadOnlyList<DailyHealthPoint> ReadDailyHealthPoints()
    {
        string path = Path.Combine(HistoryDirectoryPath, DailyHealthFileName);
        if (!IsSafeRegularFile(path))
        {
            return [];
        }

        var byDate = new SortedDictionary<DateOnly, DailyHealthPoint>();
        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                DailyHealthPoint? point = JsonSerializer.Deserialize<DailyHealthPoint>(line, JsonOptions);
                if (point is not null)
                {
                    byDate[point.Date] = point;
                }
            }
            catch (JsonException)
            {
                // Skip malformed line.
            }
        }

        return [.. byDate.Values];
    }

    public IReadOnlyList<BatterySamplePoint> ReadBatterySamples(DateOnly? minDateLocal, DateOnly todayLocal)
    {
        var results = new List<BatterySamplePoint>();
        foreach (DayRecordEnvelope env in ReadDayEnvelopes(minDateLocal, todayLocal))
        {
            if (string.Equals(env.Kind, "battery", StringComparison.Ordinal) && env.Battery is not null)
            {
                results.Add(env.Battery);
            }
        }

        return results;
    }

    public IReadOnlyList<ThermalEpisode> ReadThermalEpisodes(DateOnly? minDateLocal, DateOnly todayLocal)
    {
        var results = new List<ThermalEpisode>();
        foreach (DayRecordEnvelope env in ReadDayEnvelopes(minDateLocal, todayLocal))
        {
            if (string.Equals(env.Kind, "thermal", StringComparison.Ordinal) && env.Thermal is not null)
            {
                results.Add(env.Thermal);
            }
        }

        return results;
    }

    public IReadOnlyList<AppPowerReading> ReadAppPowerReadings(DateOnly? minDateLocal, DateOnly todayLocal)
    {
        var results = new List<AppPowerReading>();
        foreach (DayRecordEnvelope env in ReadDayEnvelopes(minDateLocal, todayLocal))
        {
            if (string.Equals(env.Kind, "app", StringComparison.Ordinal) && env.App is not null)
            {
                results.Add(env.App);
            }
        }

        return results;
    }

    private List<string> FindEligibleDayFilesOlderThan7Days(DateOnly todayLocal)
    {
        if (!Directory.Exists(HistoryDirectoryPath))
        {
            return [];
        }

        DateOnly cutoff = todayLocal.AddDays(-FreeRetentionDays);
        var eligible = new List<string>();

        foreach (string fullPath in Directory.GetFiles(HistoryDirectoryPath))
        {
            string fileName = Path.GetFileName(fullPath);
            if (!IsExactDayFileName(fileName))
            {
                continue;
            }

            if (!IsSafeRegularFile(fullPath))
            {
                continue;
            }

            DateOnly fileDate = DateOnly.ParseExact(
                fileName.AsSpan(0, 10),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

            if (fileDate < cutoff)
            {
                eligible.Add(fileName);
            }
        }

        eligible.Sort(StringComparer.Ordinal);
        return eligible;
    }

    private void EnsureProMarkerIfPro(bool isPro)
    {
        if (!isPro)
        {
            return;
        }

        Directory.CreateDirectory(HistoryDirectoryPath);
        string markerPath = Path.Combine(HistoryDirectoryPath, ProHistoryMarkerFileName);
        if (!File.Exists(markerPath))
        {
            File.WriteAllText(markerPath, string.Empty);
        }
    }

    private void AppendDayEnvelope(DateOnly localDate, DayRecordEnvelope envelope)
    {
        Directory.CreateDirectory(HistoryDirectoryPath);
        string fileName = localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl";
        string fullPath = Path.Combine(HistoryDirectoryPath, fileName);
        string line = JsonSerializer.Serialize(envelope, JsonOptions) + Environment.NewLine;
        File.AppendAllText(fullPath, line);
    }

    private List<DayRecordEnvelope> ReadDayEnvelopes(DateOnly? minDateLocal, DateOnly todayLocal)
    {
        if (!Directory.Exists(HistoryDirectoryPath))
        {
            return [];
        }

        var files = new List<(DateOnly Date, string FullPath)>();
        foreach (string fullPath in Directory.GetFiles(HistoryDirectoryPath))
        {
            string fileName = Path.GetFileName(fullPath);
            if (!IsExactDayFileName(fileName) || !IsSafeRegularFile(fullPath))
            {
                continue;
            }

            DateOnly fileDate = DateOnly.ParseExact(
                fileName.AsSpan(0, 10),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture);

            if (minDateLocal is DateOnly minDate && fileDate < minDate)
            {
                continue;
            }

            if (fileDate > todayLocal)
            {
                continue;
            }

            files.Add((fileDate, fullPath));
        }

        files.Sort((a, b) => a.Date.CompareTo(b.Date));
        var envelopes = new List<DayRecordEnvelope>();

        foreach ((_, string fullPath) in files)
        {
            foreach (string line in File.ReadLines(fullPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    DayRecordEnvelope? env = JsonSerializer.Deserialize<DayRecordEnvelope>(line, JsonOptions);
                    if (env is not null)
                    {
                        envelopes.Add(env);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed line.
                }
            }
        }

        return envelopes;
    }

    private bool IsSafeHistoryDirectory()
    {
        if (!Directory.Exists(HistoryDirectoryPath))
        {
            return false;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(HistoryDirectoryPath);
            return (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool IsChildOfHistoryDirectory(string fullPath)
    {
        string? parentDir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            return false;
        }

        string normalizedHistory = Path.TrimEndingDirectorySeparator(HistoryDirectoryPath);
        string normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parentDir));
        return string.Equals(normalizedHistory, normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeRegularFile(string fullPath)
    {
        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            FileAttributes attributes = File.GetAttributes(fullPath);
            return (attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record DayRecordEnvelope(
        string Kind,
        BatterySamplePoint? Battery,
        AppPowerReading? App,
        ThermalEpisode? Thermal);
}
