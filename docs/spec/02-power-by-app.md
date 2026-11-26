# 02 — Power by app

Platform-neutral. Power shows which apps use power as an **estimate**, never as a measurement of watts.

## Grouping

One row per app. A process belongs to the first rule that matches:

1. the **outermost application bundle** that contains its program (an app and all its helpers are one row);
2. otherwise the program named by its first command-line argument;
3. otherwise its process name.

Only the current user's processes are shown in detail. System processes and other users' processes are **counted, never listed**: "n not measured" appears under the table. Power appears in the list like any app, with its own numbers, so the user can check the app that measures.

## Windows of time

| Window | Covers |
|---|---|
| last 5 minutes | the most recent 5-minute reading |
| last hour | the last 12 readings |
| since unplugged | the readings since the sample taken at the last move from mains to battery (01) |

Readings are 5 minutes long and are built from the process samples taken in them (every 30 s with the window closed; every 2–5 s while it is open).

## Inputs: per-interval deltas

For each process, between two samples of the **same identity** (process id + start time, see Ambysto Kit process spec):

| Input | Meaning |
|---|---|
| CPU time | seconds of CPU used in the interval |
| idle wakeups | times the process woke an idle processor |
| energy | energy the system attributes to the process, where it reports one |

An app's inputs for a window are the **sums of these deltas** over its processes and the window's samples. A process's first sample has no deltas; a counter that went down (a reused id, a reset) gives no value for that interval rather than a wrong one.

macOS note: the inputs come from Ambysto Kit's process sampler (Kit 0.3, `UsageDeltas`): CPU time (`ri_user_time` + `ri_system_time`, converted through the Mach clock), `ri_pkg_idle_wkups`, and energy from `ri_energy_nj` (`RUSAGE_INFO_V6`). **Not `ri_billed_energy`:** the Kit measured that a process's own busy loop moves `ri_billed_energy` by 0 and `ri_energy_nj` by billions of nanojoules, so billed energy would hide the app that is working (Kit spec `process.md`, energy spike of 2026-10-12 and 2026-10-18). Energy is absent when either sample lacks it. Power uses **only deltas and shares of it, never a unit**.

## The estimate

One rule, in one place. A change to the rule or the constant is a spec change.

| Case | impact |
|---|---|
| energy is non-zero for at least one process in the window (Apple Silicon) | `impact = energy delta` |
| otherwise (no process reports energy) | `impact = cpuSeconds + idleWakeups × 0.0005` |

In the second case each idle wakeup is charged as half a millisecond of CPU, a stand-in for the cost of waking the package. The constant is **0.0005 seconds**.

## What is shown

- Per app: **share of measured apps** (the app's impact ÷ the sum over all measured apps, in percent; shares add to 100), with the CPU time and the idle wakeups beside it.
- The column header says **"estimate"**.
- Under the table: **"GPU not measured"** (no public interface attributes GPU use to a process; the UI says so instead of hiding it) and "n not measured".
- The number is not Activity Monitor's Energy Impact, which is a private formula; the two lists are expected to agree on the top apps, not on values.

## Storage

Readings are kept in memory for the last hour and written as `apps` history lines: kept 7 days in Free, for good in Pro (05).

Windows note: `Ambysto.Kit.Process` (`0.3.0`, `ProcessReader` + `ProcessSampler`) reads per-process CPU time (`GetProcessTimes`) over the monotonic unbiased interrupt-time clock (excluding sleep). Readable processes are grouped by rolling child processes up to their highest readable ancestor in the same application directory (`Program Files\<App>`, `Program Files (x86)\<App>`, `%LOCALAPPDATA%\Programs\<App>`, `%LOCALAPPDATA%\<Vendor>\<App>`, `Windows\SystemApps\<Pkg>`), falling back to the first command-line argument program name and then the process name. There is no public wakeup or per-process energy counter for a standard user on Windows (`IdleWakeups` and `EnergyNanojoules` are `null`), so the estimate falls back to CPU time (`impact = cpuSeconds`).
