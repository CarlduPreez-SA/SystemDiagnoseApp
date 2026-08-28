# SystemDiagnoseApp

A small Windows desktop app that diagnoses why a PC is slow to open programs, and
offers guided, one-at-a-time fixes. Built for troubleshooting a family member's
Windows 10 machine in person from a USB stick.

## What it checks

| Area | What it looks for |
|------|-------------------|
| Disk type & health | HDD vs SSD for the Windows drive, SMART failure prediction, Windows disk status |
| Free disk space | System drive nearly full |
| Startup programs | Everything that launches at sign-in (Run keys, Startup folders, scheduled autostarts) |
| Memory (RAM) | Installed amount, current pressure, paging |
| Top processes | What is actually using CPU / RAM right now |
| Windows Update | Background update activity, machine months behind |
| Restart needed | Pending-reboot flags |
| Antivirus | Multiple AV products, stale definitions, scan in progress |
| Devices & drivers | Devices with driver errors (esp. storage/chipset) |
| Windows errors | Critical/error events in the last 7 days, grouped |
| System files | Offers `sfc` / `DISM` repair |
| Power plan | "Power saver" throttling |
| Visual effects | Animations vs "best performance" |
| Fast startup | Hybrid-shutdown state |
| Bloatware | Installed-program inventory, flags common junk |
| System summary | Hardware / OS overview for the report |

## Guided fixes (each asks for confirmation, and is logged)

- Disable individual startup items
- Clear temporary files
- `sfc /scannow` and `DISM /Online /Cleanup-Image /RestoreHealth`
- Switch power plan to Balanced / High performance
- Set visual effects to "best performance"
- Turn off fast startup

Every applied change is written to `SystemDiagnose-actions-<PC>.log` on the Desktop,
and included in the exported HTML report.

## Requirements

- Windows 10 or 11, 64-bit
- To **build**: .NET 10 SDK
- To **run** the published exe: nothing — it is self-contained
- The app requests Administrator rights on launch (needed for most checks and all fixes)

## Build & run (development)

```bash
dotnet build -c Release
dotnet run --project src/SystemDiagnoseApp
```

Run from an elevated terminal so the UAC-required checks work while developing.

## Publish a single portable exe for the USB stick

```bash
dotnet publish src/SystemDiagnoseApp -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Output:

```
src/SystemDiagnoseApp/bin/Release/net10.0-windows/win-x64/publish/SystemDiagnoseApp.exe
```

Copy that one file to the USB stick. On the target PC, double-click it and approve
the UAC prompt. SmartScreen may warn about an unknown publisher — choose
*More info → Run anyway*.

## Usage on the target PC

1. Run the exe, approve UAC.
2. Click **Run diagnostics** and wait (about 1–2 minutes; the event-log check is the slowest).
3. Work top-down through anything marked **CRITICAL** or **WARNING**.
4. Apply fixes with their buttons — read each confirmation dialog first.
5. Click **Save report to Desktop** to keep an HTML record (includes the action log).
6. Restart the PC if prompted, then re-run to confirm things improved.
