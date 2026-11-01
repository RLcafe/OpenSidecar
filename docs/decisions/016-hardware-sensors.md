# 016 — Hardware sensors: LibreHardwareMonitor, full-sensors mode and frame rates

Date: 1 Nov 2026. Status: accepted. User requests: "upgrade the system stats widget with LibreHardwareMonitor stats … GPU load, VRAM, temperature and in-game FPS, CPU temperature. more graphical representation types and customization. have LibreHardwareMonitor integrated with the install package", and "make sure there is a check box in the installation process telling the user extra packages are installed". The way admin-only sensors are reached ("optional full-sensors mode"), the download of LibreHardwareMonitor 0.9.6, the MPL-2.0 notice and leaving PawnIO out were the user's decisions, relayed on 1 Nov 2026. Windows only; the phone shows the same readings and states over the phone link.

## Decision

### Where each reading comes from

| Reading | Source | Needs |
| --- | --- | --- |
| CPU, GPU usage, RAM, network, uptime | Unchanged (`SystemService`, `GpuService`) | Nothing |
| GPU temperature | Windows' graphics kernel (`D3DKMTQueryAdapterInfo`, adapter performance data, what Task Manager shows; `GpuTelemetry`), else LibreHardwareMonitor | Nothing (a WDDM 2.4+ driver) |
| Video memory | Dedicated memory size from the graphics kernel and the "GPU Adapter Memory → Dedicated Usage" counter for the main adapter, else LibreHardwareMonitor | Nothing |
| CPU temperature | LibreHardwareMonitor (`HardwareSensors`) | The sensor files, full-sensors mode (administrator rights) and the PawnIO driver |
| Frame rate of the game in front | RivaTuner Statistics Server's shared memory (`RtssReader`) when it runs, else DXGI and Direct3D 9 Present events from a real-time ETW session (`PresentCounter`) | RTSS running (any game), or the right to read performance events (administrators, or members of Windows' Performance Log Users group, DirectX games) |

The main GPU is the adapter with the most dedicated memory (a gaming PC's card, not the processor's graphics). The game in front is the foreground window's process; the dashboard never takes the focus (decision 013), so that's the game. Every reading that can't be had says why in the card and on the phone ("Needs admin: full-sensors mode", "Needs the PawnIO driver", "Hardware sensors aren't installed", "No game in front", "Not reported by this PC") and never shows a made-up number.

### LibreHardwareMonitor, pinned and checked

- **Version 0.9.6**, the official `LibreHardwareMonitor.zip` (.NET Framework build) from the v0.9.6 GitHub release: 6,632,626 bytes, SHA-256 `086d9f1b5a99e643edc2cfaaac16051685b551e4c5ac0b32a57c58c0e529c001`, the same as GitHub's own digest for that asset. Only nine DLLs from it are used (2,934,528 bytes): the library and what it loads.
- The DLLs aren't in git. `scripts\get-sensors.ps1` downloads the zip (or takes `-Zip`), checks it, extracts the nine files to `third-party\LibreHardwareMonitor\bin` and checks each one against `SHA256SUMS.txt`. `build.ps1` checks them again when it copies them to `sensors\` next to the exe; `release.ps1` hands the checked files to the tag's build checkout, so a release never downloads anything.
- **At run time** `SensorPackage` knows the same nine SHA-256 hashes, reads each file into memory, and loads it with `Assembly.Load(bytes)` only when the bytes match, so the bytes checked are the bytes run, and a damaged or swapped file is never loaded (the card then says to run setup again). The library is reached only by reflection: the app builds, starts and works without it.
- It opens lazily on the System stats reading's background thread, only for what Windows doesn't already give, and closes when nothing needs it. If it throws, it's closed for the rest of the session and the other readings carry on.

### Licences

| Component | Version | Licence |
| --- | --- | --- |
| LibreHardwareMonitorLib | 0.9.6 | MPL-2.0 |
| BlackSharp.Core, DiskInfoToolkit, RAMSPDToolkit-NDD | 1.0.7, 1.1.2, 1.4.2 | MPL-2.0 |
| HidSharp | 2.6.4 | Apache-2.0 |
| System.Memory, System.Buffers, System.Numerics.Vectors, System.Runtime.CompilerServices.Unsafe | 4.0.5, 4.0.5, 4.1.6, 6.0.3 | MIT (.NET Foundation) |

The files are distributed unmodified; `sensors\THIRD-PARTY-NOTICES.txt` names each one, its licence and where its source code is, and `sensors\licenses\` holds the full MPL-2.0, Apache-2.0 and MIT texts. Setup installs both with the files. MPL-2.0 is file-level copyleft: it applies to those files, not to Widget Screen's own code, which only loads them.

### PawnIO isn't bundled

LibreHardwareMonitor reads CPU registers through the PawnIO driver. Widget Screen and its setup never install a driver. Settings → Stats says whether PawnIO is installed and links to https://pawnio.eu.

### Full-sensors mode (the app stays asInvoker)

- Settings → Stats → **Turn on full-sensors mode** asks on the confirm sheet, then registers one Task Scheduler task, "Widget Screen full sensors", through `schtasks /Create /XML` run with `runas` (one UAC prompt). The task runs this exe with `--full-sensors` at the highest rights the user has; it has **no trigger**, no time limit and isn't stopped on battery.
- With the mode on, any normal start of Widget Screen (Start menu, Startup shortcut, a desktop shortcut) runs the task (`schtasks /Run`, no prompt) and closes, so the copy that stays is the elevated one. It hands over only when the task starts this same exe; otherwise Settings says the task points at another copy. "Restart now" does the hand-over at once.
- **Turn off** deletes the task (one UAC prompt) and, if the app is elevated, restarts it without administrator rights through the desktop shell. The uninstaller deletes the task too (a UAC prompt, only when the task exists; never in the automated checks).
- **Launching from an elevated app.** Quick Launch, macros (including scripts), Games and the PawnIO link start programs through `ProcessLauncher`: when the app is elevated, it asks the desktop's own shell (Explorer, which runs without administrator rights) to start them, through the desktop folder view's `IShellDispatch2.ShellExecute`, so a game or app started from the dashboard doesn't inherit the rights. Tradeoff: if that shell route fails (Explorer isn't running, or a policy blocks it), the program starts elevated rather than not at all. Hotkeys and the clipboard paste work as before (an elevated app may send input to any window). The setting itself is stored in `settings.xml` (`FullSensors`) and saved when it's switched, not with Save changes.

### Frame counting without the mode

Windows lets administrators and members of the Performance Log Users group start ETW sessions. The development PC's account is in that group, so frame counting worked there without elevation; the app therefore tries ETW whenever the frame rate is shown, and asks again a minute after Windows refuses. The DXGI provider's `Present_Start` (42) and Direct3D 9's `Present_Start` (1) are counted per process; Vulkan and OpenGL games need RTSS. The session ("WidgetScreen-FrameRate") stops when the frame rate isn't shown and when the app closes; one left by a crash is replaced.

### Graph styles and customization

Settings → Stats (a new tab after Widgets) shows each of the nine readings with a checkbox and a style: Auto, Line, Bars, Sparkline, Ring or Gauge (`AppSettings.StatStyles`, "Cpu=Ring;Fps=Bars"; network has no dial, uptime no graph). Auto keeps the existing look: rings on Medium for usage, gauges for temperatures, a sparkline for the frame rate, and history lines on Large. Temperatures follow Settings → Weather's Celsius or Fahrenheit and show the unit.

## Why

- Windows' own GPU telemetry covers GPU temperature and VRAM with no extra files and no rights, so most PCs get them without the library or full-sensors mode; LibreHardwareMonitor is needed for CPU temperatures, which Windows doesn't expose.
- Loading by reflection from checked bytes keeps the one third-party library optional and tamper-evident, without NuGet or a project file.
- A task the user approves once is the documented way to start an app elevated without a UAC prompt each time; the app's manifest stays asInvoker, so without the mode nothing changes.

## Tradeoffs

- In full-sensors mode the dashboard runs with administrator rights. Its phone link, Spotify, Home Assistant and Claude connections then run elevated too; they talk only to the same local or vendor hosts as before.
- An app started through the shell can't be watched by Widget Screen (it never was).
- The shell route and the task hand-over can only be tried on a real PC with UAC; the checks cover the task definition, the hand-over decision and a non-elevated shell launch, not the elevated run.
- RTSS's shared-memory layout is RivaTuner's; a future RTSS that changes it would read as "not running" rather than wrong numbers (the signature and version are checked).

## Android

The phone has no hardware sensors of the PC's and no installer. It gets every reading, its state and the styles in the `system` section and draws the same designs (`SystemText`, `SystemWidget`, `ArcGauge`, `HistoryBars`). Full-sensors mode and PawnIO are set up on the PC.
