using System;
using System.Collections.Generic;

namespace Ambysto.Power.Core.History;

public sealed record PruneEvaluation(
    bool ShouldAutoPruneAtLaunch,
    bool OlderDataHiddenInFree,
    IReadOnlyList<string> EligibleFileNamesOlderThan7Days);

public sealed record DischargeSessionSummary(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    int StartChargePercent,
    int EndChargePercent,
    double? AverageWatts);
