# Widget Screen

A personal dashboard for a Windows PC's **1920 × 720 secondary touchscreen**: clock, focus timer, Windows Now Playing, Quick Launch deck, sound mixer, Spotify, and optional System stats and Weather. It should stay glanceable and light on resources while the user works or plays games.

**Stack:** native **WPF on .NET Framework 4.8, x64**, C#, built directly with the Framework's `csc.exe`. No `.csproj`, no NuGet packages, no WebView, no browser frontend, no companion server, no installer.

## Read first

The authoritative handoff lives in [`design specfication/`](design%20specfication/). The folder name is misspelled on purpose; don't rename it. **Treat everything in it as read-only.** If a later change makes the spec out of date, write a new baseline or note in `docs/design/` instead of editing it.

| File | Use it for |
| --- | --- |
| `design specfication/as-built-specification-and-design.md` | The main spec: behavior, geometry, palette, widget contracts, Spotify, settings, polling, data format, acceptance criteria (§10) and source map (§11). |
| `design specfication/as-built-reference/ui-reference.md` | The full baseline XAML. It is the normative source for exact margins, strings, tooltips and automation names. |
| `design specfication/as-built-reference/screens.md` + `01…11-*.png` | Native 1920 × 720 renders of every screen and Settings tab. |
| `design specfication/as-built-reference/default-settings.xml` | Effective default `settings.xml`. |
| `design specfication/as-built-reference/implementation-source.zip` | Frozen source snapshot (src, scripts, tests, docs, AGENTS.md, README.md) with hashes in `baseline.json`. |
| `design specfication/as-built-reference/verification.md` | What the 47-check baseline verified, and what it didn't. |
| `design specfication/touchscreen-polish.md` | **Newer than the baseline.** See "Spec precedence". |
| `design specfication/customization-notes.md`, `live-layout-notes.md`, `polish-notes.md` | Historical notes. Column sizing was superseded by live layout editing. |

### Spec precedence

1. `touchscreen-polish.md` (9 Oct 2026, written after the baseline) wins where it conflicts. It changes these things: the Settings panel goes from **1260 × 660 to 1440 × 672** with an explicit footer **Cancel**; inputs, buttons and dropdown rows get a **52 px** minimum height; slider thumbs become **48 × 48**, and sliders capture touch directly; the scrollbar interaction area is **40 px**; SmallSize goes from **14 to 15**; Focus presets show the selected one; microphone and session mute buttons are at least **48 px**; press-and-hold right-click and flicks are disabled.
2. After that comes the as-built spec prose for behavior, then the XAML and reference package for exact values.
3. Older notes and README summaries come last.

The source in `implementation-source.zip` is the baseline **before** the touchscreen polish (for example, `MainWindow.xaml` still declares 1260 × 660). On 9 Oct 2026 the user chose to unpack this snapshot into the project root as the starting point (all 48 files match `baseline.json`). The touchscreen polish was implemented on top of it on 9 Oct 2026 (finger gestures on sliders go through `src/widgets/TouchSlider.cs`), so `src/` now differs from the snapshot and from `ui-reference.md` wherever `touchscreen-polish.md` says so. `AGENTS.md` and `README.md` at the root come from the snapshot; README's link to `docs/design/as-built-specification-and-design.md` points to a file that actually lives in `design specfication/`.

## Project layout

```text
Widget screen/
├── CLAUDE.md
├── design specfication/   Read-only as-built spec and reference package
├── docs/
│   ├── planning/          Scope, milestones, backlog, implementation plans
│   ├── design/            New design notes, layouts, touch behavior, visual references
│   └── decisions/         Numbered technical decision records (001-…, 002-…)
├── src/
│   ├── app/               Startup, window and display, dashboard layout, settings, XAML
│   ├── widgets/           Widget presentation and interactions (Spotify/ subfolder)
│   └── integrations/      Windows APIs, devices, external services (Spotify/ subfolder)
├── assets/                Assets shipped with the app (none today; glyphs come from Segoe MDL2)
├── tests/                 Automated checks and manual procedures (QuickLaunch/, Spotify/)
├── scripts/               build.ps1, run.ps1, export-design-reference.ps1
└── artifacts/             Build output (git-ignored, created by the build)
```

- Keep widget presentation separate from integrations that fetch data or run actions.
- Source folders are organizational only. The build compiles every `src/**/*.cs` into one exe.
- Group tests by the feature or behavior they verify.
- Empty folders hold a `.gitkeep`. Remove it when the folder gets real content.
- Name decision records sequentially (`003-…md`) and cover the decision, rationale and tradeoffs. 001 (Spotify) and 002 (Quick Launch) exist in the source snapshot.
- Never extract `implementation-source.zip` on top of a live `src/`. The recursive build would compile duplicate sources. Extract it to a separate folder, or into an empty project root.

Source responsibilities (spec §11): `Program.cs` handles startup, render mode and error log. `MainWindow.xaml` is the visual tree. `DashboardController.cs` handles wiring, polling, settings transactions and display mode. `WidgetLayout.cs` and `LayoutEditor.cs` own the canvas, bounds and gesture history. `Settings.cs` holds the XML model and migration. Widgets live in `src/widgets/` (FocusTimer, LevelVisualizer, PlaybackScrubber, DeckEditor, Spotify). Integrations live in `src/integrations/` (Audio, System, Gpu, Media, Weather, Display, DeckActions/DeckHotkeys/Launcher, Spotify).

## Build, run, test (Windows, PowerShell, from the project root)

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test      # build + all automated checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1              # add -Windowed for preview
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\render-checks.ps1      # 1920×720 captures to artifacts/polish/
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\export-design-reference.ps1
```

- Output goes to `artifacts/app/WidgetScreen.exe`. If the running app locks it, build with `-OutputDirectory artifacts\live-edit` and run with `-AppDirectory artifacts\live-edit`.
- The tests are a console exe (`artifacts/tests/WidgetScreen.Tests.exe`) that prints `PASS`/`FAIL` lines. Add new checks to `tests/SmokeTests.cs`, `tests/QuickLaunch/DeckTests.cs` or `tests/Spotify/SpotifyTests.cs` with the `Check(name, action)` pattern.
- App flags: `--settings <path>` isolates preferences and the Spotify session; `--windowed`; `--render <png>` with `--render-settings`, `--render-tab N`, `--render-edit`, `--render-edit-demo`.
- Live Windows integrations (audio, media, GPU) need ordinary desktop access. "Unavailable" in a sandbox doesn't mean a device is missing.
- Use isolated `--settings` paths in tests and renders. Never touch the user's real `%LOCALAPPDATA%\WidgetScreen\settings.xml`, Spotify session, playback, likes or volume from automated checks.

## Non-negotiables

- Native WPF only. **No WebView** (it drains the GPU) and no browser dashboard. No third-party packages unless the user agrees.
- It's a Windows app. Fullscreen goes on the **secondary monitor only**. Preview is a window on the primary; there is no primary-screen fullscreen.
- Design for 1920 × 720 logical units inside a uniform `Viewbox`. The dashboard **never scrolls** and never reflows into columns. Settings and inner lists may scroll.
- Put touch first. Keep touch targets at their specified minimums, and don't add decorative animation. Motion such as output levels stays optional, and Quiet mode turns it off.
- Keep resource use low. Use one 1 s dispatcher heartbeat with tick counters. Hidden or minimized widgets and hidden metrics don't poll. Don't add new timers or polling without a reason.
- Keep integrations isolated. One failing service must never break the dashboard. Clearly distinguish loading, unavailable, disconnected and working states.
- **Never show simulated readings as live data.** Fixtures must be labelled.
- Store preferences locally (XML, written atomically). Never commit credentials. Spotify tokens go only in DPAPI-encrypted `spotify-session.dat`, which is git-ignored.
- Quick Launch actions must be inspectable in Settings. Confirm destructive or consequential actions (arguments, scripts and installers, Delete, Alt+F4, or `Confirm=true`).
- Fonts are Segoe UI, Segoe UI Light for the clock, and Segoe MDL2 Assets for glyphs. Don't add images or emoji for icons, or custom fonts.
- Keep the visual system: Midnight, Slate and Warm palettes, Lime, Glacier, Lavender and Amber accents, and the dynamic resources in spec §3.3–3.5.
- Documented quirks (for example, the SpotifyAccount Client ID TextBox not resetting) are intentional baseline behavior. Fixing one is a deliberate divergence, so raise it with the user first.
- **Don't invoke Impeccable** unless the user explicitly authorizes it again. Its web detector doesn't validate WPF anyway.

## Settled decisions (from AGENTS.md)

- Live layout editing on a free 1856 × 576 canvas: drag to move, four 48 × 48 corner grips, minimum sizes, optional 8 px snap, undo/redo (50), hide/show, Reset, Save and Cancel.
- Settings tabs, in order: Theme, Display, Widgets, Weather, Spotify, Quick Launch. Appearance previews live, and Cancel restores it.
- System stats: CPU, GPU, RAM, network and uptime, each with its own toggle. Missing GPU support never blocks the others.
- Quick Launch: a paged deck (6 per page, or 3 when compact) with App, File, Folder, Website, Hotkey and Control actions, presets and Start menu picker. Old assignments and intentionally empty decks are preserved.
- Spotify is its own widget (PKCE, user-supplied Client ID, redirect `http://127.0.0.1:43827/callback`). Windows Now Playing stays independent.
- Default layout shows Clock, Focus, Now Playing, Quick Launch, Sound Mixer and Spotify. System stats and Weather are hidden. Custom layouts are preserved.
- Sound mixer (9 Oct 2026): app sliders come from every output, each app has an output button to see and change its device, and the master output is chosen by tapping Master volume. See `docs/decisions/003-mixer-output-routing.md` and `docs/design/mixer-output-devices.md`.

**Still open:** physical monitor size and comfortable text and touch sizing; whether controls must work during exclusive-fullscreen games. Physical touch, on-screen keyboard, monitor unplug and replug, and live Spotify account actions are still unverified on real hardware.

## Working process

- Read this file and the relevant spec section before changing code. Keep changes focused on the agreed scope and preserve existing user work.
- Make routine, reversible implementation decisions yourself. Ask only when a missing preference materially changes the result.
- Verify against the spec's acceptance criteria (§10): run `build.ps1 -Test`, render at 1920 × 720 for visual changes, and check integration failure states (unavailable device, expired account, missing sensor).
- Report what changed, what was verified, and what remains unverified in plain language.
- When the user settles a decision, record it here (and in `docs/decisions/` if it's a technical choice).
