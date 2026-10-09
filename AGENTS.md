# Widget Screen — Agent Instructions

## Project brief

Build a personal dashboard for a small **1920 × 720 second monitor** connected to a Windows PC. It should offer useful controls and glanceable information while the user works or plays games.

The dashboard should feel like a cohesive tool, with readable widgets, quick interactions, and low resource usage.

## Folder structure and file placement

Use this layout when adding files. See [README.md](README.md) for the workspace overview.

| Location | Purpose |
| --- | --- |
| `AGENTS.md` | Project requirements and instructions for agents working in this repository. |
| `README.md` | Workspace overview and, once implemented, setup and run instructions. |
| `docs/planning/` | Feature scope, milestones, backlog, and implementation plans. |
| `docs/design/` | Screen layouts, mockups, touch behavior, and visual references. |
| `docs/decisions/` | Agreed technical decisions, including their rationale and tradeoffs. |
| `src/app/` | Application startup, window management, dashboard layout, and settings. |
| `src/widgets/` | Widget interfaces, presentation, and user interactions. |
| `src/integrations/` | Windows API access, device communication, and external service providers. |
| `assets/` | Icons, images, and other assets shipped with the application. |
| `tests/` | Automated checks, test fixtures, and manual verification procedures. |
| `scripts/` | Repeatable development, build, and packaging helpers. |

* Keep widget presentation separate from integrations that retrieve data or execute actions.
* Store design references in `docs/design/` and application assets in `assets/`.
* Add feature-specific subfolders as needed. The source folders are organizational boundaries; they do not require separate build projects or dictate a framework.
* Name technical decision records sequentially, for example `001-ui-framework.md`, and explain the decision, rationale, and tradeoffs.
* Organize tests around the features or behavior they verify.
* Remove a folder's `.gitkeep` when adding real content to that folder.
* Keep this folder map and the layout in `README.md` synchronized when the structure changes.

## Candidate widgets

These are ideas to explore, not an agreed feature list:

* **Sound mixer:** master volume, mute, output selection, and potentially per-application volume.
* **Spotify:** playback controls, track information, and album artwork.
* **System information:** useful CPU, GPU, memory, and temperature readings where supported.
* **Weather:** current conditions and a compact forecast for a user-selected location.
* **Now playing and visualizer:** current media information with an optional audio-reactive display.
* **Stream Deck-style buttons:** configurable shortcuts for applications, websites, media controls, or other selected actions.

Confirm which widgets belong in the first version before implementing them. Spotify and now-playing information may share a widget if that makes the layout clearer. suggest widgets you may think are useful

## Design principles

* Design primarily for **1920 × 720**. Keep the main dashboard usable without scrolling at that resolution.
* should work in full screen on second screen only.
* should prioritize touch interface as screen is touch screen
* Prioritize readability at the monitor's actual size and normal viewing distance. Resolution alone does not establish comfortable text size. have a sizing/scale feature to scale for viewing
* Use consistent spacing, typography, colors, and control behavior across widgets.
* Keep essential information visible at a glance. Avoid clutter and unnecessary animation.
* Make visualizers and decorative motion optional, with reduced-motion support.
* Clearly distinguish loading, unavailable data, disconnected integrations, and working controls.
* Keep layout and widget settings configurable where practical; start with a focused default arrangement.
* Make it low resource intensive
* add customization features to make the widget screen appealing and pretty

## Technical direction

* Choose the implementation approach after discussing the first-version requirements and integration constraints.
* Account for Windows integration needs when comparing a browser dashboard, desktop application, or a dashboard with a local companion service.
* Verify feasibility before promising per-application audio control, system sensors, media control, or audio capture. Browser access alone may not cover these needs.
* Prefer a small, maintainable dependency set and clear separation between widget UI and data providers.
* Keep integrations independent so an unavailable service does not break the whole dashboard.
* Use sample data for prototypes when needed, and clearly label it. Never present simulated readings as live data.
* Avoid unnecessary CPU/GPU use, network requests, and background polling, especially while games are running.
* Do not use webview implementation as it will drain gpu.
* App is windows app.

## Local data and integrations

* Prefer local storage for layout and preferences unless the user requests synchronization.
* Do not commit credentials, tokens, or private account data. Use appropriate credential storage for the chosen platform.
* Make required account connections, permissions, and any paid services clear before adding an integration.
* Shortcut actions must correspond to settings the user can inspect. Confirm destructive or irreversible actions before executing them.

## Working process after review

1. Agree on the initial widgets, visual direction, and how the dashboard will run on the second monitor.
2. Resolve the integration constraints that affect the implementation approach.
3. Build a small, reviewable first version, then add agreed integrations incrementally.
4. Validate the layout at 1920 × 720 and check the interactions that changed.
* Read this file before working in the repository and update it when the user settles project decisions.
* Preserve existing user work. Keep changes focused on the agreed scope.
* Make routine, reversible implementation decisions independently once build work is authorized. Ask only when a missing preference or consequential decision materially affects the result.
* Use checks appropriate to the change; avoid tests that only repeat implementation details.
* For integrations, check relevant failure states such as unavailable devices, expired account access, or missing sensor data.
* Report what changed, what was verified, and any remaining limitations in plain language.

## Settled customization requirements

* Keep the implemented native WPF dashboard; do not introduce a WebView.
* Widgets support live editing on the dashboard: drag each widget independently to move it and stretch any corner to resize it. Use a free canvas within 1920 × 720, with touch-sized corner targets, minimum usable sizes, optional snapping, undo/redo, hide/show, Save, Cancel, and Reset.
* Settings has dedicated **Theme** and **Display** tabs. Theme controls the overall palette, accent, text scale, and widget corners; Display controls the secondary monitor and fullscreen launch preference.
* System stats include CPU, GPU, and RAM utilization, with independent visibility settings for these metrics, network traffic, and uptime. Hidden metrics do not poll; missing GPU support must not block other readings.
* Preferences remain local and older settings files retain compatible defaults.
* Quick Launch is a customizable deck with add/remove/reorder, names and icons, presets, Start menu app selection, app/file/folder/website actions, keyboard shortcuts, and built-in dashboard/media controls. Keep the existing visual style. Use touch-sized paged buttons instead of shrinking a growing deck; preserve old assignments and intentionally empty decks. Edit from the widget or Settings → Quick Launch, with transactional Save/Cancel. Weather has its own Settings tab.
* Do not invoke Impeccable until the user explicitly authorizes it again.
* Spotify has its own widget; the original Windows Now Playing widget remains independent. Spotify shows artwork, current song, Like, transport and seeking controls, with Albums, Playlists and Search inline in the card. Albums and accessible playlists expand to song lists; songs link to Spotify. Account setup lives in a dedicated Settings → Spotify tab. Browser sign-in uses PKCE; the Client ID is saved in preferences independently of encrypted tokens, and connections restore on restart. Seeking is touch friendly; Windows Now Playing also supports seeking where the media app allows it.
* The default Spotify arrangement shows Clock, Focus, Now Playing, Quick Launch, Sound Mixer and Spotify. System Stats and Weather are hidden by default and can be restored in Edit layout. Preserve existing custom arrangements; Settings → Spotify offers an undoable layout preview, and Reset layout uses this new default.

## Decisions still open

* First-version widget selection and priorities.
* Physical monitor dimensions and validation of comfortable text/touch sizing.
* Required account connections, weather location, and shortcut actions.
* Whether controls need to work during fullscreen games, and what interaction method is preferred.
