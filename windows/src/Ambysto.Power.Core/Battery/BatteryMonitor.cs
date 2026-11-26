using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Ambysto.Power.Core.Battery;

public sealed class BatteryMonitor
{
    public static readonly TimeSpan SampleInterval = TimeSpan.FromMinutes(5);
    public const double MinPlausibleTemperatureCelsius = 5.0;
    public const double MaxPlausibleTemperatureCelsius = 120.0;

    private readonly IBatteryReader _reader;
    private readonly List<BatterySamplePoint> _samples = [];
    private readonly SortedDictionary<DateOnly, DailyHealthPoint> _dailyPointsByDate = [];

    private DateTimeOffset? _lastSampleWrittenAtUtc;
    private PowerSourceKind? _lastPowerSource;
    private int? _lastKnownChargePercent;
    private DateTimeOffset? _unpluggedAtUtc;
    private int? _unpluggedStartChargePercent;

    public BatteryMonitor(
        IBatteryReader reader,
        IEnumerable<BatterySamplePoint>? initialSamples = null,
        IEnumerable<DailyHealthPoint>? initialDailyPoints = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _reader = reader;

        if (initialSamples is not null)
        {
            foreach (BatterySamplePoint sample in initialSamples)
            {
                _samples.Add(sample);
                if (_lastSampleWrittenAtUtc is null || sample.TimestampUtc > _lastSampleWrittenAtUtc.Value)
                {
                    _lastSampleWrittenAtUtc = sample.TimestampUtc;
                }
            }
        }

        if (initialDailyPoints is not null)
        {
            foreach (DailyHealthPoint pt in initialDailyPoints)
            {
                _dailyPointsByDate[pt.Date] = pt;
            }
        }
    }

    public BatterySnapshot? LatestSnapshot { get; private set; }
    public DateTimeOffset? LastUnpluggedAtUtc => _unpluggedAtUtc;

    public static double? ComputeHealthPercent(int? fullChargeCapacityMwh, int? designCapacityMwh)
    {
        if (fullChargeCapacityMwh is not > 0 || designCapacityMwh is not > 0)
        {
            return null;
        }

        return ((double)fullChargeCapacityMwh.Value / designCapacityMwh.Value) * 100.0;
    }

    public static double? ComputeDischargeWatts(
        PowerSourceKind powerSource,
        int? voltageMillivolts,
        int? currentMilliamps,
        int? dischargeRateMilliwatts = null)
    {
        if (powerSource != PowerSourceKind.Battery)
        {
            return null;
        }

        if (voltageMillivolts is > 0 && currentMilliamps is int current && current != 0)
        {
            return (Math.Abs((double)current) * voltageMillivolts.Value) / 1_000_000.0;
        }

        if (dischargeRateMilliwatts is int rate && rate != 0)
        {
            return Math.Abs((double)rate) / 1_000.0;
        }

        return null;
    }

    public static double? FilterPlausibleTemperature(double? celsius)
    {
        if (celsius is double value
            && value >= MinPlausibleTemperatureCelsius
            && value <= MaxPlausibleTemperatureCelsius)
        {
            return value;
        }

        return null;
    }

    public static string FormatTimeRemaining(TimeSpan? remaining)
    {
        if (remaining is not TimeSpan ts || ts <= TimeSpan.Zero)
        {
            return "—";
        }

        int totalMinutes = (int)Math.Round(ts.TotalMinutes);
        if (totalMinutes <= 0)
        {
            return "—";
        }

        int hours = totalMinutes / 60;
        int minutes = totalMinutes % 60;

        if (hours > 0 && minutes > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{hours} h {minutes} min");
        }

        if (hours > 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{hours} h");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{minutes} min");
    }

    public static string FormatSinceUnplugged(int deltaPercent, TimeSpan elapsed)
    {
        int absDelta = Math.Abs(deltaPercent);
        string timeText = FormatTimeRemaining(elapsed);
        if (timeText == "—")
        {
            timeText = "0 min";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"since you unplugged: \u2212{absDelta} % in {timeText}");
    }

    public (BatterySnapshot Snapshot, BatterySamplePoint? WrittenSample, DailyHealthPoint? WrittenDailyPoint) Tick(
        DateTimeOffset nowLocal)
    {
        DateTimeOffset nowUtc = nowLocal.ToUniversalTime();
        BatteryReading raw = _reader.ReadBattery();
        double? plausibleTemp = FilterPlausibleTemperature(raw.TemperatureCelsius);
        BatteryReading current = raw with { TemperatureCelsius = plausibleTemp };

        if (!current.HasBattery)
        {
            var noBatterySnapshot = new BatterySnapshot(
                Current: current,
                HealthPercent: null,
                DischargeWatts: null,
                TimeRemainingText: "—",
                NoBatteryMessage: "This PC has no battery; running on mains power.",
                RecordStartedDate: null,
                RecordStartedText: string.Empty,
                SinceUnplugged: null,
                SamplesLast24Hours: [],
                SamplesLast7Days: [],
                DailyHealthPoints: []);

            LatestSnapshot = noBatterySnapshot;
            return (noBatterySnapshot, null, null);
        }

        bool powerSourceChanged = _lastPowerSource is PowerSourceKind previousSource
            && previousSource != current.PowerSource;

        if (current.PowerSource == PowerSourceKind.Battery)
        {
            if (_lastPowerSource != PowerSourceKind.Battery)
            {
                _unpluggedAtUtc = nowUtc;
                _unpluggedStartChargePercent = _lastKnownChargePercent ?? current.ChargePercent ?? 100;
            }
        }
        else
        {
            _unpluggedAtUtc = null;
            _unpluggedStartChargePercent = null;
        }

        _lastPowerSource = current.PowerSource;
        if (current.ChargePercent is int currentCharge)
        {
            _lastKnownChargePercent = currentCharge;
        }

        double? dischargeWatts = ComputeDischargeWatts(
            current.PowerSource,
            current.VoltageMillivolts,
            current.CurrentMilliamps,
            current.DischargeRateMilliwatts);

        bool shouldWriteSample = _lastSampleWrittenAtUtc is null
            || powerSourceChanged
            || (nowUtc - _lastSampleWrittenAtUtc.Value) >= SampleInterval;

        BatterySamplePoint? writtenSample = null;
        if (shouldWriteSample)
        {
            writtenSample = new BatterySamplePoint(
                TimestampUtc: nowUtc,
                ChargePercent: current.ChargePercent ?? 0,
                PowerSource: current.PowerSource,
                State: current.State,
                FullChargeCapacityMwh: current.FullChargeCapacityMwh,
                CycleCount: current.CycleCount,
                TemperatureCelsius: current.TemperatureCelsius,
                DischargeWatts: dischargeWatts);

            _samples.Add(writtenSample);
            _lastSampleWrittenAtUtc = nowUtc;
        }

        DateTimeOffset cutoff7d = nowUtc.AddDays(-7);
        _samples.RemoveAll(s => s.TimestampUtc < cutoff7d);

        DateTimeOffset cutoff24h = nowUtc.AddHours(-24);
        BatterySamplePoint[] samples24h = _samples.Where(s => s.TimestampUtc >= cutoff24h).ToArray();

        DateOnly todayLocal = DateOnly.FromDateTime(nowLocal.DateTime);
        DailyHealthPoint? writtenDaily = null;
        if (!_dailyPointsByDate.ContainsKey(todayLocal))
        {
            writtenDaily = new DailyHealthPoint(
                Date: todayLocal,
                FullChargeCapacityMwh: current.FullChargeCapacityMwh ?? 0,
                CycleCount: current.CycleCount ?? 0,
                DesignCapacityMwh: current.DesignCapacityMwh ?? 0,
                Model: current.ModelIdentifier);
            _dailyPointsByDate[todayLocal] = writtenDaily;
        }

        double? healthPercent = ComputeHealthPercent(current.FullChargeCapacityMwh, current.DesignCapacityMwh);
        string remainingText = FormatTimeRemaining(current.TimeRemaining);

        UnplugSummary? sinceUnplugged = null;
        if (current.PowerSource == PowerSourceKind.Battery && _unpluggedAtUtc is DateTimeOffset unpUtc)
        {
            int startPct = _unpluggedStartChargePercent ?? current.ChargePercent ?? 0;
            int curPct = current.ChargePercent ?? startPct;
            TimeSpan elapsed = nowUtc >= unpUtc ? nowUtc - unpUtc : TimeSpan.Zero;
            int delta = curPct - startPct;

            sinceUnplugged = new UnplugSummary(
                UnpluggedAtUtc: unpUtc,
                StartChargePercent: startPct,
                CurrentChargePercent: curPct,
                DeltaChargePercent: delta,
                Elapsed: elapsed,
                DisplayText: FormatSinceUnplugged(delta, elapsed));
        }

        DateOnly? recordStarted = _dailyPointsByDate.Count > 0
            ? _dailyPointsByDate.Keys.First()
            : null;

        string? recordStartedText = recordStarted is DateOnly start
            ? string.Create(CultureInfo.InvariantCulture, $"since {start.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}")
            : null;

        var snapshot = new BatterySnapshot(
            Current: current,
            HealthPercent: healthPercent,
            DischargeWatts: dischargeWatts,
            TimeRemainingText: remainingText,
            NoBatteryMessage: string.Empty,
            RecordStartedDate: recordStarted,
            RecordStartedText: recordStartedText,
            SinceUnplugged: sinceUnplugged,
            SamplesLast24Hours: samples24h,
            SamplesLast7Days: _samples.ToArray(),
            DailyHealthPoints: [.. _dailyPointsByDate.Values]);

        LatestSnapshot = snapshot;
        return (snapshot, writtenSample, writtenDaily);
    }
}
