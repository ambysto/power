# 01 — Battery health and history

Platform-neutral. Power only reads the battery; it never changes how the Mac charges.

## What is shown now

| Field | Meaning |
|---|---|
| charge | percent of the current full charge |
| power source | battery or mains |
| state | charging, charged, or discharging |
| time remaining | as the system estimates it; "—" while unknown or unlimited |
| cycle count | full charge cycles so far, shown next to the maker's rating (1,000 cycles for current MacBooks) |
| design capacity | what the battery held when new |
| full-charge capacity | what it holds fully charged today |
| maximum capacity (system) | the percentage the system itself shows, **where the platform publishes it** |
| **health** | full-charge capacity ÷ design capacity |
| condition | the system's own word ("Good", "Fair", "Poor"), where published; otherwise not shown |
| temperature, voltage, current | as reported |
| **discharge rate** | watts = \|current\| × voltage; shown only while on battery |

Health is always Power's own ratio. If the system publishes a maximum-capacity percentage or a condition, both are shown beside it; if not, they are left out, never guessed.

macOS note: the fields come from the power-sources list (`IOPSCopyPowerSourcesInfo`, `IOPSGetProvidingPowerSourceType`, `IOPSGetTimeRemainingEstimate`) and from the `AppleSmartBattery` registry entry, read only, never set. The condition is the power-sources key `kIOPSBatteryHealthKey` (`BatteryHealth`; "Good" on an Apple Silicon MacBook, 2026-10-18), absent or empty when the system does not publish one. Keys: `CycleCount`, `DesignCapacity`, `AppleRawMaxCapacity` (full charge; fall back to `NominalChargeCapacity`, **never** `MaxCapacity`, which is a percentage on Apple Silicon and would read as about 2 % health), `AppleRawCurrentCapacity`, `Temperature`, `Voltage`, `InstantAmperage` (fall back to `Amperage`). The serial number is never read.

macOS note: units are fixed by captured fixtures: `Temperature` is hundredths of a degree Celsius, `Voltage` is millivolts, `InstantAmperage` is milliamps and negative while discharging, so watts = \|mA\| × mV ÷ 1,000,000.

Windows note: `GetSystemPowerStatus` for charge, source and time remaining; `GUID_DEVCLASS_BATTERY` (`IOCTL_BATTERY_QUERY_TAG`, `IOCTL_BATTERY_QUERY_INFORMATION`, `IOCTL_BATTERY_QUERY_STATUS`) or the battery classes in WMI (`BatteryStaticData` design capacity, `BatteryFullChargedCapacity`, `BatteryCycleCount`) for capacities, cycles, voltage and discharge rate; Windows does not publish a separate system maximum-capacity percentage or condition word, so both are omitted (`null`). `powercfg /batteryreport` is the reference the battery report (05) mirrors.

## No battery

**No battery means the list of power sources is empty.** The module says so in one sentence and shows the providing power source only. Every other module works. The presence of a battery entry in the system is never the test: desktop Macs have an all-zero entry.

macOS note: an iMac has an `AppleSmartBattery` registry entry with `CycleCount`, `MaxCapacity` and `CurrentCapacity` all 0 and `IOPSCopyPowerSourcesInfo` returns 0 sources; the source list decides.

Windows note: `Win32_Battery` returns no instance on a desktop PC.

## Sampling and history

Power keeps its own history; the system keeps none.

- **Live view:** refreshed every 30 s with the window closed, every 5 s while it is visible.
- **Sample:** one every 5 minutes while Power runs, plus one on every change between battery and mains. A change of charge alone writes nothing.
- **Sample content:** time, charge, source, state, full-charge capacity, cycle count, temperature, watts.
- **Power-source change:** compare the source before and after; only a real change writes a sample and resets the unplug window.

macOS note: the change callback is `IOPSCreateLimitedPowerNotification` on the main run loop; the general `IOPSNotificationCreateRunLoopSource` (fires on every charge change) only refreshes the card while the window is visible.

## Daily health point

One line per local day, written with the day's first battery sample: date, full-charge capacity, cycle count, design capacity, model. It is kept for the life of the install in **every tier** and is never pruned (05). The window says when the record started ("since 11 Oct 2026").

## Since unplugged

Charge at the last sample taken on mains minus the charge now, over the elapsed time: "since you unplugged: −23 % in 1 h 20 min". The window starts at the last source change to battery and ends when the Mac is on mains again.

## Charts

Charge level over 24 hours and 7 days; full-charge capacity and cycle count over the life of the install (from the daily points); the unplug summary above. Samples older than 7 days exist only in Pro (05).
