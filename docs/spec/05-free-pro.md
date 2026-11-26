# 05 — Free and Pro

Model: like WinRAR. Free is fully usable forever; Pro adds conveniences and never shows an upsell. Product id: `power`.

| Feature | Free | Pro |
|---|---|---|
| Battery, Power by app, Thermal: live views (01–03) | ✓ | ✓ |
| Manual keep-awake with display-on (04) | ✓ | ✓ |
| "Who is keeping my Mac awake" list (04) | ✓ | ✓ |
| Guard rails and "the user is always told" (04) | ✓ | ✓ |
| Daily health points, for the life of the install | ✓ | ✓ |
| Samples, episodes and app readings, last 7 days | ✓ | ✓ |
| Samples, episodes and app readings beyond 7 days | | ✓ |
| Keep-awake rules, with their guard-rail settings (04) | | ✓ |
| Export (battery report, CSV/JSON) | | ✓ |
| Alerts (notifications) | | ✓ |

Launch phase: nothing is charged; early users are gifted a Pro key, unconditionally and for life.

## Alerts (Pro)

Alerts only inform; they never change anything.

| Alert | Fires when | Default |
|---|---|---|
| Health | health (01) falls under the threshold | **80 %** |
| Cycle milestone | the cycle count reaches a milestone, once each | **500** and **1,000** |
| Throttling | an open episode (03) lasts longer than the threshold | **10 minutes** |
| App high on battery | one app is above the share of the estimate (02) on battery for the duration | **50 %** for **10 minutes** |
| Session ended by a guard rail | a keep-awake session ended by the battery floor or the thermal release (04) | every time |
| Cap question | the cap question is due (04) | every time |

- **Cooldown:** 30 minutes per subject (the battery, an episode, an app) for the first four. The last two have no cooldown.
- **Switched off by default**; each alert is on only when the user turns it on.
- **Permission.** Notification permission is requested **only when the user switches an alert on**, never at launch and never on activating Pro. If it is refused, the menu-bar icon is marked until the window is opened.

Windows note: the same rules with toast notifications.

## Export (Pro)

The user chooses where the file is saved. Nothing is sent anywhere. Content is **app names and numbers only**: never command lines, paths or environment.

- **Battery report** (Markdown; the Mac counterpart of Windows' battery report): model, battery since, cycles, design and full-charge capacity, health, the daily points as a table, and the last 7 discharge sessions with watts.
- **History** as JSON or CSV: battery samples, daily points, episodes, app readings.

## History and retention

Stored as daily JSON Lines files `yyyy-MM-dd.jsonl` (typed lines: `battery`, `apps`, `episode`), plus `daily-health.jsonl` (01), on this Mac only. No telemetry. Folder 0700, files 0600.

macOS note: the folder is `~/Library/Application Support/Ambysto Power/History`.

Windows note: the per-user application data folder (`%LOCALAPPDATA%\Ambysto\Power\History`), with the same file names (`yyyy-MM-dd.jsonl`, `daily-health.jsonl`, `pro-history`). Deletion of expired day files uses `DeleteFileW` in `HistoryStore.cs` only, restricted to non-reparse regular files matching `^\d{4}-\d{2}-\d{2}\.jsonl$`. License keys are verified offline via `Ambysto.Kit.License` `0.3.0` (`product` id `power`).

**Pro-history marker.** An empty file `pro-history` is written the first time a line is appended while the edition is Pro.

**Retention.** Day files older than 7 days are deleted at launch **only when all three hold**:

1. the edition is Free;
2. **no license key is stored at all**: a stored key whose check failed, for any reason, means no prune in that launch;
3. **no `pro-history` marker exists**.

Otherwise Free **hides** everything older than 7 days and nothing is deleted; the window says older data exists. Settings offers "Delete history older than 7 days" behind a confirmation that lists the files. Pro never prunes.

This is the **only deletion Power ever performs**. It matches only regular files named exactly `yyyy-MM-dd.jsonl` inside that folder. `daily-health.jsonl` and the marker never match and are never deleted.

## Purchase reminder

Shown only when a purchase page exists (none in the launch phase: **never** while there is none), at most once per launch, **never during an active keep-awake session**, dismissed with one click.

## License

Pro is unlocked only by an Ambysto license key that verifies offline and lists `power` (Ambysto Kit license spec). A key for another product is refused with a clear message and the app stays Free. Release builds have no other way to Pro. Settings and About show "Registered to <licensee>".
