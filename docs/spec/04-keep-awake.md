# 04 — Keep awake, safely

Platform-neutral. Keep-awake is the **only** thing Power changes on the machine, and it is reversible by construction.

## Sessions and rules

| Kind | Tier | Ends |
|---|---|---|
| **Manual session**: until turned off, for a duration, or until a time | Free | by the user, by its deadline, or by a guard rail |
| **Rule: while an app runs** | Pro | when the app quits |
| **Rule: while a task runs** | Pro | when the task exits |
| **Rule: time window** (weekdays, start, end) | Pro | at the end of the window |

- Every session and rule has its own **display-on** setting: on keeps the display awake too (which also covers system sleep); off keeps only the system awake.
- **One session at a time.** The manual session wins over rules; among rules the first that matches, in the user's order. Starting a session while another is active is offered as "end and start".
- **App rule:** matches the main application only. "Also its helpers" is a switch, **off by default**: a helper such as a sync daemon never exits and would keep the Mac awake for good. With the switch on, any process whose program lives inside that application counts.
- **Task rule:** a process picked from the live list (ends when that exact process, identified by process id + start time, exits; a zombie counts as exited), or a program name or path matched against the user's processes, worded in the sheet as "any process named `xcodebuild`".
- **Time window:** local time; may cross midnight.
- The rule sheet shows **what matches right now**, and the saved rule is written out in words: "Keep the Mac and the display awake while Xcode is running (helpers not counted); stops at 20 % on battery, if the Mac gets critically hot, and asks you after 12 h".

## The sleep assertion

A sleep assertion is a request to the system not to sleep.

- Exactly **one assertion of one kind** exists at a time: the **display** kind when display-on is set, the **system** kind otherwise. Changing kind releases the old one first.
- It is named "Ambysto Power: `<session title>`".
- It is released when the session ends, when a guard rail ends it, and when Power quits. If Power crashes, the system drops it at once.
- **System-side timeout as a safety net:** the assertion is created with a timeout of the smaller of the time left before the cap and **15 minutes**, with the action "release", and the timeout is refreshed on every tick. A hung Power loses its assertion within 15 minutes.

macOS note: `IOPMAssertionCreateWithName` with `kIOPMAssertionTypePreventUserIdleDisplaySleep` or `kIOPMAssertionTypePreventUserIdleSystemSleep`; `IOPMAssertionSetProperty` with `kIOPMAssertionTimeoutKey` and the release timeout action; `IOPMAssertionRelease`. These calls live in one source file.

Windows note: `SetThreadExecutionState` (`ES_CONTINUOUS | ES_SYSTEM_REQUIRED` and `ES_DISPLAY_REQUIRED` when display-on is set; `ES_CONTINUOUS` to release) in one choke-point file (`ExecutionStateAssertion.cs`), pinned to a dedicated OS thread with a 15-minute watchdog lease (`min(remainingUntilCap, 15 minutes)`). Power never spawns `powercfg` and never modifies power plans or lid-close policy.

## Guard rails

On every session and rule, in every tier. Evaluated on every tick (30 s with the window closed, 5 s open) and **immediately** on a power-source or thermal change. Timed sessions end on their deadline, not on the next tick.

| Guard rail | Default | Ends the session when | Restarts only when |
|---|---|---|---|
| Battery floor | **20 %** | on battery and charge ≤ floor (never while on mains) | the source is mains, or charge is above **floor + 5** |
| Thermal release | critical | the thermal state reaches **critical** (03) | the state is nominal or fair |
| Cap | **12 h** | the cap is reached without an answer | see below |

## The cap question

**10 minutes before the cap** the user is asked "Still working? Keep awake for another 12 h?" in the popover and the window (Pro adds a notification).

- **Yes** extends by the cap length. **No** ends the session. **No answer ends it at the cap.**
- Rules get the same question. An unanswered rule rests until a matching gap of at least **60 s**, then may start again.

## The user is always told

When a session ends for a reason other than the user's own click, **in every tier**:

- a line in the popover and on the Keep Awake tab, such as "ended at 20 % battery, 14:02";
- the menu-bar icon is marked until the popover opens.

Pro adds a notification (05). The line is never the notification alone.

## Who is keeping my Mac awake

A read-only, system-wide list of sleep assertions, grouped by process (Free). Columns: app, kind in plain words, since when, the reason text. Kinds: "keeps the Mac awake", "keeps the display awake", "the user is active", "a network task". Power's own row is marked. No action is offered on other apps' assertions. The list is read every 5 s while its tab is open and never in the background.

macOS note: `IOPMCopyAssertionsByProcess`, keyed by pid; each entry has `AssertType`, `AssertName` and timestamps. Types seen: `PreventUserIdleSystemSleep`, `PreventUserIdleDisplaySleep`, `PreventSystemSleep`, `NoIdleSleepAssertion`, `UserIsActive`, `SystemIsActive`.

Windows note: `powercfg /requests` lists the same when run elevated; because Power runs as a standard user and never spawns `powercfg`, the list on Windows shows Power's own active assertion row and notes that reading other processes' kernel power requests requires administrator rights.

## Login item

Rules run only while Power runs. When the user saves the **first** rule, Power asks once "Open Ambysto Power at login?" The default button is **No**; Power registers itself only on Yes. Settings shows and changes the state, including "approve it in System Settings → Login Items" when the system needs approval. Nothing is asked at onboarding and nothing else is registered.

macOS note: `SMAppService.mainApp`; states `enabled`, `requiresApproval`, `notRegistered`, `notFound`.

Windows note: the same question, with a per-user startup entry (`"Ambysto Power"` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` in `LoginItem.cs`).

## What it never does

No lid-close (clamshell) override. No change to Energy Saver, Low Power Mode, charging, or any sleep setting. No root, no helper. No signals to other processes.
