# LibreHardwareMonitor (hardware sensors)

The System stats card reads CPU and GPU temperatures through [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 (MPL-2.0), the one third-party library Widget Screen uses (user approval, 1 Nov 2026; see `docs/decisions/016-hardware-sensors.md`).

- **Pinned:** version 0.9.6, the official `LibreHardwareMonitor.zip` (the .NET Framework build) from the v0.9.6 GitHub release, 6,632,626 bytes, SHA-256 `086d9f1b…e529c001`. GitHub's own digest for that asset matched on 1 Nov 2026.
- **Files used:** the nine DLLs in `SHA256SUMS.txt` (2,934,528 bytes): the library and what it loads. The rest of the zip (the LibreHardwareMonitor app, its charting and task-scheduler libraries) isn't used.
- **Not in git.** `scripts\get-sensors.ps1` downloads the zip (or takes `-Zip <path>`), checks the zip's SHA-256, extracts the nine files into `bin\` (git-ignored) and checks each file's SHA-256. `scripts\build.ps1` copies them into `artifacts\app\sensors\` with `THIRD-PARTY-NOTICES.txt` and `licenses\`, and `scripts\package.ps1` puts them in the setup exe.
- **Checked again at run time:** `SensorPackage` (in `src\integrations\Sensors`) knows the same hashes and loads a file only when its bytes match, so a damaged or swapped file is never loaded. `tests\Sensors\SensorTests.cs` keeps the two lists equal.
- **Not bundled:** the PawnIO driver (https://pawnio.eu), which LibreHardwareMonitor needs for CPU temperatures. Settings says when it's needed and links to it.

To move to another version: change the URL, version and hashes here, in `scripts\get-sensors.ps1` and in `SensorPackage.cs`, update `THIRD-PARTY-NOTICES.txt`, and record it in the decision record.
