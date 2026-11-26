using System;
using System.Collections.Generic;

namespace Ambysto.Power.Core.Apps;

public enum PowerWindowKind
{
    Last5Minutes,
    LastHour,
    SinceUnplugged
}

public sealed record AppPowerRow(
    string GroupKey,
    string DisplayName,
    double CpuSeconds,
    long IdleWakeups,
    long? EnergyNanojoules,
    double RawImpact,
    double SharePercent,
    int ProcessCount);

public sealed record AppPowerReading(
    DateTimeOffset WindowStartUtc,
    DateTimeOffset WindowEndUtc,
    IReadOnlyList<AppPowerRow> Apps,
    int UnreadableProcessCount);

public sealed record AppPowerSnapshot(
    PowerWindowKind Window,
    IReadOnlyList<AppPowerRow> Rows,
    int UnreadableProcessCount,
    string ColumnHeader,
    string GpuNote,
    string UnmeasuredNote);
