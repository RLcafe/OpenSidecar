# Widget Screen

A personal dashboard for a Windows PC's secondary touchscreen (the owner's is **1920 × 720**; other sizes work too): clock, focus timer, Windows Now Playing, Quick Launch deck, sound mixer, Spotify, and optional System stats and Weather. It should stay glanceable and light on resources while the user works or plays games.

**Stack:** native **WPF on .NET Framework 4.8, x64**, C#, built directly with the Framework's `csc.exe`. No `.csproj`, no NuGet packages, no WebView, no browser frontend, no companion server, no installer.

**Android version** (since 20 Oct 2026): a native Android app in `android/` (Java, framework views only, minSdk 26) built straight from the Android SDK tools by `scripts\build-android.ps1` into an installable `WidgetScreen.apk`. No Gradle, no AndroidX or other libraries, no WebView. Since 21 Oct 2026 it is a **remote for the PC**: it mirrors the PC's dashboard and controls the PC over home Wi-Fi through the phone link in the Windows app (Settings → Phone). See `docs/decisions/007-android-app.md`, `docs/decisions/008-phone-remote.md` and `docs/design/android-app.md`.

## Both platforms (standing user rule, 20 Oct 2026)

**Every change goes into both the Windows app (`src/`) and the Android app (`android/`).** Because the phone mirrors the PC, a new or changed widget usually needs three parts: the Windows widget, what the phone link sends or accepts for it (`src/app/DashboardController.Remote.cs`, `android/.../core/PcState.java`), and the phone's card. Plan, build and test both (`build.ps1 -Test` and `build-android.ps1 -Test`) in the same branch, and record the change once in `CHANGELOG.md`. When something can't exist on one platform (a Windows-only API, an Android restriction), say so in the reply and in the decision record or design note, and give that platform the closest equivalent if there is one. Shared Android logic belongs in `android/src/com/alialyami/widgetscreen/core` (no `android.*` imports), with checks in `android/tests`.

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
├── BRANCHES.md            Unmerged branches, worktree and merge process, version rules
├── CHANGELOG.md           Released versions (vX.Y.Z tags on main)
├── design specfication/   Read-only as-built spec and reference package
├── docs/
│   ├── planning/          Scope, milestones, backlog, implementation plans
│   ├── design/            New design notes, layouts, touch behavior, visual references
│   └── decisions/         Numbered technical decision records (001-…, 002-…)
├── android/               Android app: AndroidManifest.xml, res/, src/ (core/ is platform-free), tests/ (JVM checks)
├── src/
│   ├── app/               Startup, window and display, dashboard layout, settings, XAML
│   ├── widgets/           Widget presentation and interactions (Spotify/ subfolder)
│   └── integrations/      Windows APIs, devices, external services (Spotify/ subfolder)
├── assets/                WidgetScreen.ico, the exe and window logo (drawn by scripts\make-icon.ps1)
├── tests/                 Automated checks and manual procedures (QuickLaunch/, Spotify/)
├── scripts/               build.ps1, build-android.ps1, run.ps1, release.ps1, make-icon.ps1, export-design-reference.ps1
├── artifacts/             Build output (git-ignored, created by the build)
├── releases/              Self-contained release copies, releases\vX.Y.Z (git-ignored)
└── .worktrees/            One git worktree per branch (git-ignored)
```

- Keep widget presentation separate from integrations that fetch data or run actions.
- Source folders are organizational only. The build compiles every `src/**/*.cs` into one exe.
- Group tests by the feature or behavior they verify.
- Empty folders hold a `.gitkeep`. Remove it when the folder gets real content.
- Name decision records sequentially (`003-…md`) and cover the decision, rationale and tradeoffs. 001 (Spotify) and 002 (Quick Launch) exist in the source snapshot.
- Never extract `implementation-source.zip` on top of a live `src/`. The recursive build would compile duplicate sources. Extract it to a separate folder, or into an empty project root.

Source responsibilities (spec §11): `Program.cs` handles startup, render mode and error log. `MainWindow.xaml` is the visual tree. `DashboardController.cs` handles wiring, polling, settings transactions and display mode. `WidgetLayout.cs` and `LayoutEditor.cs` own the canvas, bounds and gesture history. `Settings.cs` holds the XML model and migration. Widgets live in `src/widgets/` (FocusTimer, LevelVisualizer, PlaybackScrubber, DeckEditor, Spotify, Discord). Integrations live in `src/integrations/` (Audio, System, Gpu, Media, Weather, Display, DeckActions/DeckHotkeys/Launcher, Spotify, Discord).

## Build, run, test (Windows, PowerShell, from the project root)

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test      # build + all automated checks
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1              # add -Windowed for preview
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\render-checks.ps1      # 1920×720 captures to artifacts/polish/
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\scripts\export-design-reference.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-android.ps1 -Test  # Android APK + core checks on the JVM
```

- The Android build needs a JDK and the Android SDK (`%LOCALAPPDATA%\Android\Sdk` or `ANDROID_HOME`) and downloads nothing. The APK lands in `artifacts\android\WidgetScreen.apk`, signed with the standard debug key so newer builds install over older ones. Its checks print `PASS`/`FAIL` lines from `android/tests/.../AndroidTests.java`; add new ones there with the `check(name, action)` pattern. Views and Android services need a device or emulator; say so when they weren't run.

- The desktop "Widget Screen" shortcut opens the latest release in `releases\vX.Y.Z\`, made by `scripts\release.ps1 -UpdateShortcut` after tagging (see "Releases" in `BRANCHES.md`). Day-to-day builds stay in `artifacts\`.
- Output goes to `artifacts/app/WidgetScreen.exe`. If the running app locks it, build with `-OutputDirectory artifacts\live-edit` and run with `-AppDirectory artifacts\live-edit`.
- The tests are a console exe (`artifacts/tests/WidgetScreen.Tests.exe`) that prints `PASS`/`FAIL` lines. Add new checks to `tests/SmokeTests.cs`, `tests/QuickLaunch/DeckTests.cs`, `tests/Spotify/SpotifyTests.cs`, `tests/Discord/DiscordTests.cs` or `tests/Remote/RemoteTests.cs` with the `Check(name, action)` pattern. The phone-link checks listen on loopback with free ports and isolated settings, and save a real `/state` answer to `artifacts\tests\remote-state-sample.json`, which the Android checks parse; run `build.ps1 -Test` before `build-android.ps1 -Test`.
- App flags: `--settings <path>` isolates preferences and the Spotify session; `--windowed`; `--render <png>` with `--render-size WxH` (the screen's pixel size, 1920x720 by default), `--render-settings`, `--render-tab N`, `--render-edit`, `--render-edit-demo`, `--render-edit-panel`. `tests\screen-size-renders.ps1` renders five screen sizes into `artifacts\screen-sizes\`.
- Live Windows integrations (audio, media, GPU) need ordinary desktop access. "Unavailable" in a sandbox doesn't mean a device is missing.
- Use isolated `--settings` paths in tests and renders. Never touch the user's real `%LOCALAPPDATA%\WidgetScreen\settings.xml`, Spotify session, playback, likes or volume from automated checks.

## Non-negotiables

- Native WPF only on Windows, native Android framework views on Android. **No WebView** (it drains the GPU) and no browser dashboard. No third-party packages (NuGet, Gradle plugins, AndroidX) unless the user agrees.
- No companion server program. The one exception (user approval, 21 Oct 2026) is the phone link inside the Windows app: off by default, local network only, paired with a code. Don't widen it (internet access, more ports, unauthenticated requests) without asking.
- It's a Windows app. Fullscreen goes on the **secondary monitor only**. Preview is a window on the primary; there is no primary-screen fullscreen.
- Designed at the 1920 × 720 scale, laid out on a canvas shaped like the screen (`ScreenFit`, `DashboardFit`), inside a uniform `Viewbox`. The PC dashboard still **never scrolls**. Settings and inner lists may scroll. On Android, `ScaledHost` plays the `Viewbox`; the one exception is the upright phone list layout, which scrolls a single column of the same cards.
- Put touch first. Keep touch targets at their specified minimums, and don't add decorative animation. Motion such as output levels stays optional, and Quiet mode turns it off.
- Keep resource use low. Use one 1 s dispatcher heartbeat with tick counters. Hidden or minimized widgets and hidden metrics don't poll. Don't add new timers or polling without a reason.
- Keep integrations isolated. One failing service must never break the dashboard. Clearly distinguish loading, unavailable, disconnected and working states.
- **Never show simulated readings as live data.** Fixtures must be labelled.
- Store preferences locally (XML, written atomically). Never commit credentials. Spotify tokens go only in DPAPI-encrypted `spotify-session.dat`, and Discord app details and tokens only in DPAPI-encrypted `discord-session.dat`. Both are git-ignored. Paired phones are kept as token hashes in DPAPI-encrypted `remote-devices.dat` next to `settings.xml`. On Android, settings are `settings.xml` (Java properties XML) and the PC pairing is `pc-link.dat`, encrypted with an Android Keystore key, both in the app's private folder and excluded from backups.
- Quick Launch actions must be inspectable in Settings. Confirm destructive or consequential actions (arguments, scripts and installers, Delete, Alt+F4, or `Confirm=true`).
- Fonts are Segoe UI, Segoe UI Light for the clock, and Segoe MDL2 Assets for glyphs. Don't add images or emoji for icons, or custom fonts. Android uses the system sans-serif (light for the clock) and line glyphs drawn in `android/.../views/Glyphs.java`; add a glyph there rather than an image.
- Keep the visual system: Midnight, Slate and Warm palettes, Lime, Glacier, Lavender and Amber accents, and the dynamic resources in spec §3.3–3.5.
- Documented quirks (for example, the SpotifyAccount Client ID TextBox not resetting) are intentional baseline behavior. Fixing one is a deliberate divergence, so raise it with the user first.
- **Don't invoke Impeccable** unless the user explicitly authorizes it again. Its web detector doesn't validate WPF anyway.

## Settled decisions (from AGENTS.md)

- Live layout editing on a free canvas (1856 × 576 on the reference screen, shaped like the screen elsewhere): drag to move, four 48 × 48 corner grips, minimum sizes, optional 8 px snap, undo/redo (50), hide/show, Reset, Save and Cancel. Widgets are shown and hidden, and pages managed, in the editor's Widgets list with check marks, the same on both apps.
- Settings tabs, in order: Theme, Display, Widgets, Weather, Spotify, Quick Launch, Phone, Discord (Phone moved before Discord in the audit, 22 Oct 2026). Appearance previews live, and Cancel restores it.
- System stats: CPU, GPU, RAM, network and uptime, each with its own toggle. Missing GPU support never blocks the others.
- Quick Launch: a paged deck (as many buttons as the card holds, 6 on the reference card, 3 when compact; `CardSizing`) with App, File, Folder, Website, Hotkey and Control actions, presets and Start menu picker. Old assignments and intentionally empty decks are preserved.
- Spotify is its own widget (PKCE, user-supplied Client ID, redirect `http://127.0.0.1:43827/callback`). Windows Now Playing stays independent.
- Tapping a song inside a Spotify album or playlist plays it in that collection so playback continues (user request, 9 Oct 2026; diverges from spec §5.1, see `docs/decisions/003-…`). Search results play the song on its own (user request, 10 Oct 2026). Episodes, and connections without the playback permission, still open the Spotify link.
- The Spotify widget has no Playing tab or playback controls: tabs are Albums (recently played), Playlists and Search, with cover thumbnails, plus a bottom strip with cover, song, artist and Like (user request, 9 Oct 2026; see `docs/decisions/004-…`).
- Web requests skip IPv6 for hosts that also have IPv4 (`PreferIPv4Handler`), because the user's network has unrouted IPv6 that made Spotify sign-in time out.
- Sound mixer (9 Oct 2026): app sliders come from every output, each app has an output button to see and change its device, and the master output is chosen by tapping Master volume. See `docs/decisions/005-mixer-output-routing.md` and `docs/design/mixer-output-devices.md`.
- Time and date always show at the top left of the header, including in Edit layout. There is no Clock widget, and the header brand was removed (user request, 9 Oct 2026; see `docs/design/header-clock-and-stats.md`). Old layouts drop the Clock placement, and Focus grows into its space when it sat directly below it.
- System stats scale with their widget: the rows sit in a Viewbox, and `SystemStatsLayout` picks 1–3 columns and widens rows to fill the card (user request, 9 Oct 2026).
- Default layout shows Focus (full column), Now Playing, Quick Launch, Sound Mixer and Spotify. System stats and Weather are hidden. Custom layouts are preserved.
- Exe identity (user request, 11 Oct 2026): the exe shows the app name "Widget Screen", author "Ali Alyami" and the logo in `assets\WidgetScreen.ico` (Focus in Lime beside Now Playing, Quick Launch and the mixer on a Midnight tile). `scripts\make-icon.ps1` (run with `-STA`) redraws it. The attributes live in `src\app\AssemblyInfo.cs`, and `build.ps1` stamps the version from the newest `vX.Y.Z` tag, so releases need no manual version bump. In-app glyphs still come from Segoe MDL2.
- UI polish (user request, 10 Oct 2026): dashboard text only speaks when something is true or wrong, and the Spotify card shows one empty state until connected. The Arrange-for-Spotify button was removed because Reset layout does the same. See `docs/design/ui-polish.md`.
- Tray mode (user request, 21 Oct 2026): a Hide to tray button in the header, and Settings → Phone → Move to the tray while my phone shows the dashboard (off by default since the audit, 22 Oct 2026). The window leaves the taskbar and a notification-area icon brings it back; it returns by itself a minute after the phone stops, unless the user hid it. Windows only. See `docs/decisions/009-tray-mode.md`.
- Android app as a phone remote (user request, 20–21 Oct 2026): the phone shows the PC's dashboard and every card controls the PC (Focus, Now Playing, Quick Launch, Sound mixer with app outputs and microphone, System stats, Weather, Spotify through the PC's connection, Discord soundboard). Any screen held sideways, and an upright tablet, gets the dashboard on a canvas shaped like the screen (on phones the canvas may shrink to 80% so text stays larger); upright phones get the cards in a scrolling list. Since decision 010 the phone mirrors the PC's layout and pages. Settings → Display can force either, or keep the app sideways (user request, 21 Oct 2026). The phone's own Settings are Theme, Display, Widgets and PC; everything else is set up on the PC. The Windows side is Settings → Phone: HTTP on TCP 43830 and UDP discovery on 43831, local addresses only, six-digit pairing code, polled every 2 s (5 s in Quiet mode) for the visible cards only. See `docs/decisions/008-phone-remote.md`.
- Any screen size, mirroring, pages, list editor and cards that size themselves (user requests, 21 Oct 2026): the dashboard is designed at the 1920 × 720 scale and laid out on a canvas shaped like the screen, with layouts saved per canvas shape (the 1856 × 576 one stays in `WidgetLayout`). Up to eight layout pages, each with its own shown widgets; the page shown is shared with the phone; a header page switcher and a sideways swipe change it. The PC owns every layout and the phone mirrors it over the link, saving its own edits to the PC. Windows Edit layout uses the phone's list with check marks. Every card sizes its content to its card (`CardSizing`). See `docs/decisions/010-any-screen-size.md` and `docs/design/any-screen-size.md`.
- Audio on its own thread (audit, 22 Oct 2026): all MMDevice COM work runs on `AudioWorker`'s background thread and the dashboard awaits it; Windows device notifications trigger an immediate mixer read; the dashboard and a phone share each audio, system and media reading for a second; apps are listed playing first and System sounds last; the level meter sits under the master slider and collapses when paused. See `docs/decisions/011-audio-thread-and-shared-readings.md`.
- Focus finish, custom length and sessions (audit, 22 Oct 2026): a finished session tints the card, chimes (Settings → Theme, silent in Quiet mode) and clears on a tap or after a minute; a fourth "…" preset picks a 5–180 minute length that stays as a preset; the card counts today's sessions; paused dims the time. Same on the phone through the `focus` section. See `docs/design/focus-finish.md`.
- One confirm sheet and first-launch tips (audit, 22 Oct 2026): anything that asks "are you sure" uses `ConfirmSheet` (Windows) or `Dashboard.confirm` (Android), a themed sheet with 52 px buttons; never a Win32 message box or a system dialog. A one-time tips row sits in the header's page-switcher spot until Got it. See `docs/design/confirm-sheet.md`.
- Discord soundboard (user request, 20 Oct 2026): a Soundboard widget, hidden by default, plays sounds as the user in their current voice channel through the Discord desktop app's local RPC pipe, using the user's own Discord app (Client ID and Secret in Settings → Discord, encrypted in `discord-session.dat`). The Discord tab is the last one, after Phone. No bot and no user token. See `docs/decisions/006-discord-soundboard.md`.

**Still open:** comfortable text and touch sizing on other people's screens (the layout adapts, the design scale stays 1920 × 720); whether controls must work during exclusive-fullscreen games. Physical touch, on-screen keyboard, monitor unplug and replug, and live Spotify account actions are still unverified on real hardware.

## Branches, worktrees and versions

[`BRANCHES.md`](BRANCHES.md) has the full process. The short version:

- **Every new branch or worktree lives inside this folder**, at `.worktrees\<name>` (git-ignored). Never create folders in `C:\Users\PCD\Documents\AI Apps\Claud` (user rule, 10 Oct 2026). Create one with `git worktree add ".worktrees\<name>" -b feature/<name> main`, or `fix/<name>` for a bug fix.
- When you create a branch, add it to the table in `BRANCHES.md` with its purpose, base commit, status, checkout and test build paths (`.worktrees\<name>\artifacts\app\WidgetScreen.exe` and `.worktrees\<name>\artifacts\android\WidgetScreen.apk`). Keep that row current.
- After a merge: run `build.ps1 -Test` and `build-android.ps1 -Test` on `main`, move the change into `CHANGELOG.md` under a new version, remove the branch's row, commit, and add an annotated `vX.Y.Z` tag. Then `git worktree remove` the worktree, delete any `artifacts\<name>` build, `git branch -d` the branch, and `git worktree prune`. Finish with `scripts\release.ps1 -UpdateShortcut` so the desktop shortcut runs the new version.
- Versions are semver. MAJOR stays 0 until the user calls it 1.0. MINOR is for a new widget, feature or visible behavior change. PATCH is for fixes and polish. Docs-only changes go under Unreleased. `v0.1.0` is the first tag.

## Working process

- Read this file and the relevant spec section before changing code. Keep changes focused on the agreed scope and preserve existing user work.
- Make routine, reversible implementation decisions yourself. Ask only when a missing preference materially changes the result.
- Verify against the spec's acceptance criteria (§10): run `build.ps1 -Test` and `build-android.ps1 -Test`, render at 1920 × 720 for visual changes (and other sizes with `tests\screen-size-renders.ps1` when layout changes), and check integration failure states (unavailable device, expired account, missing sensor).
- Report what changed, what was verified, and what remains unverified in plain language.
- When the user settles a decision, record it here (and in `docs/decisions/` if it's a technical choice).
