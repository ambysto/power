# 03 — Thermal state, throttling and sensors

Platform-neutral. Everything here is read-only: Power never controls a fan or a clock.

## State

The system's thermal level, and its changes: **nominal, fair, serious, critical**. It is read on every tick and on the system's change notification.

macOS note: `ProcessInfo.thermalState` and `thermalStateDidChangeNotification` (main run loop).

## Speed limit and warning level

Two figures from the power-management layer, shown **only when the platform reports one**:

- the **CPU speed limit** (percent of full speed);
- the **thermal warning level**.

"Nothing recorded" is not an error: it means the machine has not been throttled. Apple Silicon may never report a speed limit, so the limit is shown as a number only when one is reported and otherwise as "not reported". Episodes (below) never depend on it.

macOS note: `IOPMCopyCPUPowerStatus` (`CPU_Speed_Limit`, `CPU_Available_CPUs`, `CPU_Scheduler_Limit`) and `IOPMGetThermalWarningLevel`; both return `kIOReturnNotFound` (0xe00002f0) when nothing was recorded. `pmset -g therm` reads the same source.

## Episodes

An **episode** is a period in which the state is above nominal **or** a reported speed limit is under 100 %. It opens when either becomes true and closes when both are back to normal.

| Recorded | Meaning |
|---|---|
| start, end, duration | local times and length |
| worst state | the highest state reached |
| lowest limit | the lowest reported speed limit, if any was reported |
| top three apps | the three apps with the highest estimate (02) during the episode |

Shown as a card: "throttled 14 min, 11:02–11:16; on top: Xcode, Chrome, Spotlight". Episodes are history lines (`episode`): kept 7 days in Free, for good in Pro (05).

## Sensors

| Family | Source | Unit |
|---|---|---|
| CPU | per-model table | °C |
| GPU | per-model table | °C |
| SSD | per-model table | °C |
| fans | per-model table: current speed, minimum, maximum, count | rpm |
| system power | per-model table | W |
| battery | the battery reading (01) | °C |

Rules:

- **Read-only.** The write path and fan control never exist in Power.
- **Per-model key table.** Only the keys in this model's table are read; the machine's keys are never scanned during normal use. A table is committed only after every key was compared with a reference sensor tool on that model (±3 °C at the same moment).
- **Hottest-sensor rule.** For each family the card shows the **hottest sensor** with the count: "CPU 91 °C, hottest of 32 sensors". It never names a core.
- **Plausibility.** A temperature outside **5–120 °C** is dropped.
- **"No sensors found".** A model without a verified table, a failed open, or a failed read shows the words "no sensors found" and **no number**. A guessed number is never shown.
- **Cadence.** Every tick while the Thermal tab or the popover is visible; every 30 s otherwise, for the episode record.
- **Models in v1.** Apple Silicon only (the M1 iMac and the M3 Pro MacBook Pro are verified). Intel shows "no sensors found" until a table is verified.

macOS note: sensors are read through the sensor controller's user client (`AppleSMC`, `IOServiceOpen`, `IOConnectCallStructMethod` selector 2) with only three commands: read (5), key by index (8, used only by the capture tool) and key info (9). The write command never appears. The key-data block is 80 bytes and its layout is pinned by a test; values are decoded **by the reported type** (`flt `, `sp78`, `fpe2`, `ui8 `, `ui16`, `ui32`; any other type gives no value). Keys are not documented by Apple and differ per model and macOS version; this is the one deliberate exception to "documented interfaces only". Examples on an M1 iMac: `Tp…`, `Tc…`, `Te…` (CPU), `Tg…` (GPU), `TH0x` (SSD), `F0Ac` with `F0Mn`, `F0Mx` and `FNum` (fans), `PSTR` (system power). Tables are keyed by the `hw.model` identifier.

Windows note: thermal zones through PDH `\Thermal Zone Information(*)\Temperature` (`< 343.15 K (70 °C)` → `nominal`, `70–80 °C` → `fair`, `80–90 °C` → `serious`, `≥ 90 °C` → `critical`; `nominal` when 0 instances are reported) or WMI `MSAcpi_ThermalZoneTemperature`; `PowerDeterminePlatformRoleEx` for the platform role (desktop or mobile). There is no public throttling flag or verified per-model sensor table on Windows in v1, so the speed limit shows `"not reported"` and hardware sensors show `"no sensors found"` unless supplied by a debug fixture.
