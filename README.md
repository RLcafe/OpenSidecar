# Widget Screen

A personal Windows dashboard for a 1920 × 720 secondary touchscreen, with useful controls and information for working and gaming.

Read [AGENTS.md](AGENTS.md) for the project requirements and working instructions. The first version uses native WPF on .NET Framework 4.8, without a WebView or third-party packages.

For a detailed reconstruction handoff, read the [as-built specification and design](docs/design/as-built-specification-and-design.md). It records the 9 October 2026 source baseline, including exact geometry, visual tokens, widget behavior, settings, integrations, eleven native screen references and a hashed source snapshot.

## Build and run

On Windows 10/11 x64 with .NET Framework 4.8 installed, run from this directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

You can also open `artifacts/app/WidgetScreen.exe` directly. No administrator privileges or separate .NET SDK are required. A restricted sandbox may prevent access to Windows audio and media sessions; run the app normally on your desktop to use those integrations.

If the running app locks the normal executable, build into a separate folder with `scripts/build.ps1 -Test -OutputDirectory artifacts/live-edit`. Close the old app before opening `artifacts/live-edit/WidgetScreen.exe`; both builds use your same preferences. `scripts/run.ps1 -AppDirectory artifacts/live-edit` launches that build, and `tests/render-checks.ps1 -AppDirectory artifacts/live-edit` verifies it.

- The app opens fullscreen on an available secondary monitor. With no secondary monitor, it opens a window on the primary display.
- Press **F11** to toggle the secondary screen view or **Escape** to return to a window. Use `scripts/run.ps1 -Windowed` to start in preview mode.
- **Settings → Theme** offers Midnight, Slate, and Warm palettes, four accents, square-to-rounded widget corners, text size (90–115%), clock format, and output levels. Appearance previews immediately; closing Settings discards unsaved changes.
- **Settings → Display** selects the secondary monitor and whether to open fullscreen at launch. The toolbar and F11 still switch views immediately.
- **Edit layout** on the dashboard starts live editing. Drag anywhere on a widget to move it; stretch any of its four corners to resize it independently. Corner targets are 48 × 48 design pixels. Widgets stay inside the screen and retain minimum usable sizes; compact widgets adapt their content.
- The edit toolbar offers **Undo**, **Redo**, **Reset layout**, optional **Snap** to an 8 px grid, and **Show widget**. Select hidden widgets from the dropdown to restore them. Position and dimensions appear in the footer; overlaps are indicated, and moved widgets come to the front. **Save layout** keeps changes locally; **Cancel** or Escape restores the last saved layout.
- Keyboard editing: arrows move, Shift+arrows resize from the bottom-right, Ctrl+arrows move by 1 px, Ctrl+Z undoes, and Ctrl+Y/Ctrl+Shift+Z redoes. Widget controls resume when editing ends. Output levels pause while editing.
- **Settings → Widgets** opens the dashboard editor and adds/removes CPU, GPU, RAM, network, and uptime readings independently. Save other settings before opening the editor. Turning all stats off hides System Pulse; hidden widgets stop their integration polling. Older column-size preferences initialize the free layout when no custom layout is saved.
- **Quick Launch → Edit** or **Settings → Quick Launch** customizes the button deck. **Settings → Weather** contains city selection.
- **Quiet mode** pauses output levels and reduces polling. Touch or mouse can operate controls; keyboard focus stays inside Settings while it is open.
- Preferences are saved in `%LOCALAPPDATA%\WidgetScreen\settings.xml`. Weather is off until a city is selected; city search and weather requests use Open-Meteo.

## Quick Launch deck

Tap **Edit** on Quick Launch, then select a button or **+ Add**. Choose its name, icon and action, or start with a preset such as Calculator, Notes, Screenshot or Play / pause. Use the arrows below the button list to reorder; **Remove** deletes the selected button. **Save changes** keeps the whole deck locally, while the Settings close button cancels edits. You can remove every button and use **Add your first button** later. Existing assignments survive the upgrade.

Actions include:

- **App:** select a Start menu app, filter the list by name, browse to an `.exe` or `.lnk`, or enter a registered app link such as `spotify:`. The picker indexes Start menu shortcuts on demand; apps without a shortcut can be assigned through Browse or their registered link. Optional arguments are passed directly to the app and require confirmation when run.
- **File / folder / website:** browse to a local target or enter a full `https://` or `http://` address. Files open with their Windows association. Missing files or disconnected drives report an error when tapped.
- **Keyboard shortcut:** type a combination such as `Ctrl+Shift+S`, `Win+D`, `Alt+F4`, or `F5`. Quick Launch returns focus to the last active external app before sending keys, and refuses to send if focus cannot be restored. Windows reserves `Ctrl+Alt+Delete`. Elevated apps and some games can block simulated input; exclusive fullscreen behavior needs hardware testing.
- **Dashboard / media control:** play/pause, previous/next track, sound or microphone mute, volume steps, focus timer, Quiet mode, and Windows sound settings. Media controls use Windows Now Playing independently of the Spotify widget.

The widget retains its original colors and icon-button style. Six buttons appear per page, or three in a compact widget, with 48 px minimum touch targets. Extra buttons use previous/next arrows. No additional polling timer is introduced. Enable **Ask before running** for actions needing review; launch arguments, common script/installer files, Delete shortcuts and Alt+F4 always require confirmation. Assignments are visible in Settings and button tooltips. An unchanged assignment to a temporarily unavailable app does not block saving other settings.

The normal `artifacts/app/WidgetScreen.exe` build includes this editor. See [Quick Launch verification](tests/QuickLaunch/manual-verification.md).

## Spotify

**Spotify** is a separate widget. **Now Playing** keeps its Windows media controls independently. The Spotify card shows the current song, artist, artwork, Like / Liked, transport buttons and a draggable seek bar. Albums, Playlists and Search browse inline; albums and accessible playlists expand to their songs. Song links open Spotify. No popup or WebView is used.

The default layout shows Clock, Focus, Now Playing, Quick Launch, Sound Mixer and Spotify, with System Stats and Weather hidden. Existing custom layouts are preserved. To switch, open **Settings > Spotify > Arrange dashboard for Spotify + Now Playing**, review the preview, and **Save layout**. Cancel/Undo remain available. Hidden widgets can be restored in Edit layout; Reset uses the new default.

1. Create a **Web API** app in the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard). The app owner needs Premium for Development Mode; add the sign-in account to its user allowlist.
2. Register this exact redirect URI: http://127.0.0.1:43827/callback.
3. Open **Settings > Spotify**, paste the app's **Client ID**, and choose **Connect Spotify**. The ID is saved before your browser opens. No client secret is needed. Complete browser approval and return to Settings for the saved connection status. Cancel sign-in, closing Settings, or the three-minute timeout stops an unfinished attempt.
4. Start playback in Spotify on an active device. Spotify playback/seek controls require **Premium** and user-modify-playback-state; existing connections need **one reconnect** to grant the added permission. Ordinary restarts restore saved access automatically. Windows Now Playing seeking depends on the media app's capabilities.

Account permissions cover current playback, saved music, private/collaborative playlists, likes and playback control. The application does not stream or capture Spotify audio. Client ID preferences and encrypted login tokens are stored separately in %LOCALAPPDATA%/WidgetScreen/settings.xml and %LOCALAPPDATA%/WidgetScreen/spotify-session.dat. Moving or rebuilding the executable does not change these paths. A custom --settings path deliberately isolates account storage in that settings folder.

**Save Client ID**, **Connect** and **Disconnect** apply immediately, independently of the general Settings Save/Cancel buttons. Disconnect removes local tokens but retains the ID. Revoke the grant in Spotify's account settings if required. A temporary refresh/network failure keeps the saved login; revoked/expired access requests reconnection. A damaged/unreadable credential file reports recovery guidance without deleting it.

Playback polls at most every ten seconds, or thirty in Quiet mode; hidden/minimized widgets stop new polls. The progress display advances locally each second. Browsing loads ten entries on demand. Spotify limits playlist contents to playlists you own or collaborate on; an inaccessible playlist still has an **Open Spotify** button. Rate limits pause requests. Podcasts/local items cannot be liked as songs. Tests use isolated fixtures and never change real playback or liked songs; live authorization, physical touch and account controls still need verification with your account. See [integration decision](docs/decisions/001-spotify-integration.md) and [manual checks](tests/Spotify/manual-verification.md).

## Verification

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\render-checks.ps1
```

The smoke checks cover XAML templates, settings persistence and migration, theme preview/cancel, cross-tab saving, live layout transactions, all four resize corners, snapping, undo/redo, hide/show, restart persistence, failed-save recovery, minimum widget sizes, metric visibility, GPU aggregation, timer behavior, shortcuts, weather parsing, and read-only system/audio/media access. The rendering script writes actual 1920 × 720 WPF captures to `artifacts/polish/`, including 115% text, all Settings tabs, themes, resized widgets, hidden stats, and live editing, using isolated preferences. It does not change your personal settings or playback. Native integration checks require ordinary desktop access; sandbox captures may correctly show unavailable GPU, media, or audio-session data. Physical touch gestures remain a manual hardware check.

## Workspace layout

```text
Widget Screen/
├── AGENTS.md             Project requirements and agent instructions
├── README.md             Workspace guide
├── BRANCHES.md           Unmerged branches, worktree and merge process, version rules
├── CHANGELOG.md          Released versions
├── docs/
│   ├── planning/         Scope, milestones, feature notes, and backlog
│   ├── design/           Layouts, mockups, touch behavior, and visual references
│   └── decisions/        Agreed technical choices and their rationale
├── src/
│   ├── app/              Application startup, window, layout, and settings
│   ├── widgets/          Widget interfaces, presentation, and interactions
│   └── integrations/     Windows APIs, devices, and external service providers
├── assets/               Assets used by the application, such as icons and images
├── tests/                Automated checks, fixtures, and manual test procedures
├── scripts/              Repeatable development, build, and packaging helpers
├── artifacts/            Build output (git-ignored)
└── .worktrees/           One git worktree per unmerged branch (git-ignored)
```

## Branches and versions

New work happens on a `feature/<name>` or `fix/<name>` branch, checked out as a worktree in `.worktrees\<name>` inside this folder, never next to it. Each branch builds and tests in its own worktree, so its test build is `.worktrees\<name>\artifacts\app\WidgetScreen.exe`. [BRANCHES.md](BRANCHES.md) lists the active branches and their test builds, and explains what to do after a merge. Released versions are tagged `vX.Y.Z` on `main` and described in [CHANGELOG.md](CHANGELOG.md).

## Organization conventions

- Keep planning documents and design references in `docs/`; put assets shipped with the application in `assets/`.
- Keep widget presentation separate from the integrations that provide data or execute actions.
- Add feature-specific subfolders when the feature is introduced. The source folders above are organizational placeholders, not separate build projects.
- Record consequential technical decisions in `docs/decisions/` using names such as `001-ui-framework.md`, with the decision, rationale, and tradeoffs.
- Keep test files organized around the behavior they verify as the implementation develops.
- Empty folders contain `.gitkeep` so Git can retain the structure. Remove each placeholder when that folder gains real content.

## Current status

Implemented: clock, focus timer, configurable launch buttons, master/application volume and mute, default microphone mute, Windows media session information and transport controls, Spotify current playback/likes/library/search with account setup, configurable CPU/GPU/RAM/network/uptime readings, optional current weather, saved widget sizing, and theme customization.

GPU utilization uses Windows GPU Engine performance counters: process usage is combined per physical engine and the busiest engine across adapters is displayed. It needs a compatible driver and two samples; unsupported counters show **Unavailable** and retry once a minute. System sampling runs off the UI thread every two seconds (five in Quiet mode). See [Microsoft's GPU utilization explanation](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/) and [PDH array counter API](https://learn.microsoft.com/en-us/windows/win32/api/pdh/nf-pdh-pdhgetformattedcounterarrayw).

The output display shows live peak-level history, not a frequency spectrum. Media features depend on what the active app exposes to Windows. Temperature sensors, in-app output-device switching, and a frequency-spectrum visualizer are not included yet; the mixer opens Windows sound settings for device selection. Physical touch and behavior while a game owns exclusive fullscreen still need hardware verification.
