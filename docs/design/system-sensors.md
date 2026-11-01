# System stats with hardware sensors (1 Nov 2026)

How the readings are read, the decisions and the licences are in `docs/decisions/016-hardware-sensors.md`. This note is the behaviour. Windows and the phone draw the same.

## Readings

Nine, each with its own checkbox in Settings → Stats (all on for a new install; existing settings get the four new ones on): CPU usage, **CPU temperature**, GPU usage, **GPU temperature**, **video memory (VRAM)**, RAM, **frame rate (FPS) of the game in front**, network and uptime. Hidden readings aren't read. Turn them all off to hide the card.

| Reading | Shows | Detail | Warning colour |
| --- | --- | --- | --- |
| CPU temperature | "62°C" (or °F with Settings → Weather in Fahrenheit) | "Package" | 85 °C and up |
| GPU temperature | "71°C" | The GPU's name | 85 °C and up |
| VRAM | "52%" | "6.2 / 12 GB" | 90% and up |
| FPS | "144" | The game's process and the source ("RTSS" or "DirectX") | — |

A reading that can't be had shows "—" and says why: in a Small row in short ("Needs admin", "Needs PawnIO", "Not installed", "Repair sensors", "No game", "Not reported"), in Medium and Large details in full ("Needs admin: full-sensors mode", "Needs full-sensors mode or RivaTuner", "Needs the PawnIO driver", "Hardware sensors aren't installed", "Sensor files damaged: run setup again", "No game in front", "Not reported by this PC"), and in the value's tooltip. Dials and graphs of the frame rate scale to the nearest common refresh rate at or above the highest reading (60, 120, 144, 165, 240, 360).

## Styles

Each reading except uptime has a style in Settings → Stats: **Auto**, **Line** (history line over a faint area, with guides), **Bars** (the last 24 readings), **Sparkline** (the line alone), **Ring** or **Gauge** (a 240° arc open at the bottom). Network can be Line, Bars or Sparkline (two series: download, and upload muted). Auto: Medium draws usage as rings, temperatures as gauges and the frame rate as a sparkline; Large draws history lines. A Small row always has its value and a thin bar.

## Designs

The size thresholds are unchanged (`CardSizing.Stats`). With up to nine readings, each design checks it fits and otherwise steps down:

- **Large** (from 400 × 420): a tile per reading (uptime in the heading), tiles at least 150 wide and `StatsTileMinimum` tall (128 at 100% text, 136 at 140%), in the 1–4 columns that make them closest to 1.6 : 1. A dial style draws a big dial with the value inside; the others draw the value over the graph. If no grid fits, Medium.
- **Medium**: a cell per reading in its style, at least 100 wide with room for a 56-unit dial and its name and detail (`StatsDialText`), in the shape that gives the biggest dials, over the network and uptime strip. Graph cells put the name and value on a line over the graph. If no grid fits, Small.
- **Small**: rows at least `StatsListRow` tall (40 at 100%, 44 at 140%) in columns at least 140 wide. When a card can't hold every reading, it lists them in order until it's full and the last row says "N more: make the card bigger" (its tooltip names them).

## Settings → Stats

Left: the readings and their styles. Right, **Hardware sensors**: whether LibreHardwareMonitor is installed and its files match their pins; **Full-sensors mode** (Off / On / On but this copy isn't elevated / the task starts another copy) with Turn on, Turn off and Restart now; **PawnIO driver**, installed or not, with Open pawnio.eu; **Frame rate**, which source applies on this PC. Readings and styles are saved with Save changes; full-sensors mode takes effect when it's switched.

## Reading cadence

Unchanged: every 2 s while the card is shown (5 s in Quiet mode), shared with the phone for a second. Windows' GPU adapter list is kept for a minute; the sensor files are hashed once per run; LibreHardwareMonitor opens only when a reading needs it, and the frame-counting session runs only while the frame rate is shown.

## The phone

The `system` section adds `showCpuTemp`, `showGpuTemp`, `showVram`, `showFps`, `styles` (only the non-Auto ones), `fahrenheit`, `cpuTemp`/`cpuTempState`, `gpuTemp`/`gpuTempState`/`gpuName`, `vramUsedGB`/`vramTotalGB`/`vramState`, `fps`/`fpsState`/`fpsSource`/`fpsApp` and `fullSensors` (whether the PC runs elevated). States are `Working`, `NotInstalled`, `Damaged`, `NeedsFullSensors`, `NeedsPawnIO`, `NoGame` or `Unavailable`. `SystemText.java` words them like the PC and `CardSizing` sizes the designs with the same numbers.

## Checks

`tests/Sensors/SensorTests.cs`: the pins equal `SHA256SUMS.txt`; changed files are Damaged and never loaded; the real 0.9.6 files load from checked bytes and read this PC's GPU (read only, when fetched); without the files CPU temperature says Not installed while GPU temperature and VRAM come from Windows; the CPU temperature isn't read without admin rights; wording, units, styles and Auto; RTSS frame rates from a labelled fixture memory block (and a block without RTSS's signature is ignored); the frame counter counts only Present events of the right process, and starts only where Windows allows; the full-sensors task XML (elevated, no trigger, escaped command) and the shell launch; the card with all nine readings and every style at five sizes at 140% text inside its padding, with and without full readings; Settings → Stats saving readings and styles. The installer checks cover ticked and unticked sensors. `AndroidTests.java` covers the section, wording and sizes.

Not tried: full-sensors mode itself (registering the task, the elevated copy and CPU temperatures through PawnIO need a UAC prompt on the real PC), launching through the shell from an elevated copy, RTSS with a real game, and counting a real game's frames (the counter was checked live against this PC's DXGI events, not a game).
