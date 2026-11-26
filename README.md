# Ambysto Power

Windows shows little about your battery over time, and hides which app drains it or keeps the PC awake. Power shows battery
health with a history, which apps use the most power, the thermal state, and keeps your PC awake on schedules you set. It
measures, and never forces: it never changes your power plan, never ends a process and never starts one. This repository holds
the Windows app (Windows 11 24H2 or later, x64); the behaviour is described in [`docs/spec/`](docs/spec/) without reference to
any operating system.

- **Battery:** health with a history, cycle count, and the discharge rate while on battery.
- **Power by app:** which apps use the most power, as an estimate from CPU time, with thermal state and throttling episodes.
- **Keep-awake:** sessions built on the system's own sleep request, released on any guard rail.

## Free and Pro

Power is open source and free to use. Free covers the battery, power by app and thermal views, manual keep-awake sessions, the
list of what keeps the PC awake, guard rails, daily health points and the last 7 days of history. Pro is keep-awake rules and
history beyond 7 days today; a licence key unlocks them and you enter it in About. Background alerts and export are in the
specification and their code is in the private module, but the Windows app has no control for either yet. The full table is in
[`docs/spec/05-free-pro.md`](docs/spec/05-free-pro.md). Pro is paid only if you are happy to pay: "We only charge when you are
happy to pay."

Where the Pro code lives:

- Keep-awake rule matching, background alerts and export are implemented in a separate private module that is not in this
  repository. A build from this repository has no rule matching, so a rule cannot take effect.
- History beyond 7 days is a retention check in this repository, switched on by the licence key check alone. The key is
  verified offline. Like any check in open source code, it can be edited out of your own build; what a licence
  gets you is the signed build, updates and support, and it is paid on trust.

A signed build with everything comes through the normal download and licence route on https://power.ambysto.com/ .

## Build

This builds Power without the Pro module. You need Windows 11 and the .NET 10 SDK, with Visual Studio or the `dotnet`
command line.

```sh
dotnet build windows/Ambysto.Power.slnx -c Release
dotnet test windows/Ambysto.Power.slnx -c Release --filter "TestCategory!=Console&TestCategory!=Admin&TestCategory!=ProDebug"
```

The Console and Admin test suites need an interactive session and elevation, so the command above leaves them out.

The app depends on two packages of the Ambysto Kit (`Ambysto.Kit.License`, `Ambysto.Kit.Process`) by exact version, from
nuget.org. The Kit source is at https://github.com/ambysto/ambysto-kit .

## Safety rules

- Keep-awake goes through one system sleep request only (`ExecutionStateAssertion.cs`). Power never limits charging, never
  changes a power plan or any sleep setting, never ends a process and never starts one.
- Power by app is an estimate; the GPU is not attributed to apps.
- History is stored on this PC only, with no telemetry.

## Contributing

Issues and pull requests are welcome. A change to safety behaviour (what Power may change on the PC) must update the specs in
`docs/spec/` in the same pull request. `scripts/windows-choke-points.sh` lists the APIs the code may not use (a tripwire).

## Licence

Apache License 2.0, see [LICENSE](LICENSE) and [NOTICE](NOTICE). For the "Ambysto" name and logo, see [TRADEMARK.md](TRADEMARK.md).
