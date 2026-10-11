# Changelog

Released versions of Widget Screen, newest first. Each version is an annotated `vX.Y.Z` tag on `main`. Branches that haven't been merged yet are in [BRANCHES.md](BRANCHES.md), along with the version bump rules.

## Unreleased

Nothing yet.

## 0.2.3 (11 Oct 2026)

Merges `feature/app-identity`.

- **App identity**: the exe has a logo (Focus in Lime beside Now Playing, Quick Launch and the mixer on a Midnight tile), shown in Explorer, on the window and in the taskbar. Its properties show the app name "Widget Screen", the author "Ali Alyami" and the version, which the build now takes from the newest tag.
- **Release copies**: `scripts\release.ps1` builds a tagged version from a clean checkout of its tag, runs every check and puts a self-contained copy in `releases\vX.Y.Z\` (git-ignored). The desktop shortcut opens the release copy instead of the `artifacts\app` build. See "Releases" in `BRANCHES.md`.

## 0.2.2 (10 Oct 2026)

Merges `feature/search-play`.

- **Spotify search plays songs**: tapping a search result now plays the song, like tapping a song in a playlist or album, instead of opening it in Spotify. Spotify's own autoplay decides what follows. If no Spotify device is active, the widget opens Spotify and retries. Connections without the playback permission still open the link (decision 003, updated).

## 0.2.1 (10 Oct 2026)

Merges `feature/ui-polish`, a polish, distill and clarify pass over the dashboard and Settings (`docs/design/ui-polish.md`).

- **Quieter dashboard**: labels that never change are gone (LIVE, EVERY 2 SEC, "Playing on this PC", the footer F11 hint and the tagline). The status line clears itself after about ten seconds.
- **Clearer states**: Quiet mode is a toggle that lights up when on, Focus shows when the session ends, and the microphone button states whether it's on or muted.
- **Spotify, not connected**: one "Connect Spotify" empty state with a Set up Spotify button replaces the empty tabs, list and Like strip. The card's header button is now "Account".
- **Polish**: a bigger Focus time in tall cards, the mixer's output name aligned left, a shared selected look for Spotify tabs, and a 13 px caption style for the smallest text.
- **Settings**: plainer headings and shorter copy, Weather in one column with the results list hidden until there are results, and Spotify setup as three numbered steps.
- **Removed** the "Arrange dashboard for Spotify + Now Playing" button. Reset layout does the same.

## 0.2.0 (10 Oct 2026)

Merges `feature/mixer-output-devices` and `feature/header-clock-stats`.

- **Sound mixer outputs**: every app's volume slider shows, whatever output it plays on. Each app has an output button to see and move it to another device, and tapping Master volume picks the default output (decision 005, `docs/design/mixer-output-devices.md`).
- **Header clock**: the time and date always show at the top left of the header, including in Edit layout. The Clock widget and the header brand are gone. Old layouts drop the Clock, and Focus grows into its space when it sat directly below it (`docs/design/header-clock-and-stats.md`).
- **Default layout**: Focus fills the left column, followed by Now Playing, Quick Launch, Sound Mixer and Spotify. Custom layouts are kept.
- **System stats scale with their widget**, using one to three columns that fill the card.
- Branch and version tracking: `BRANCHES.md`, this changelog, and worktrees under the git-ignored `.worktrees\` folder.

## 0.1.0 (9 Oct 2026)

First working version, built on the as-built WPF baseline from `design specfication/`.

- **Touchscreen polish** (`touchscreen-polish.md`): the Settings panel is 1440 × 672 with a footer Cancel, inputs and buttons are at least 52 px tall, slider thumbs are 48 × 48 and capture touch directly, the scrollbar area is 40 px, small text is 15, Focus presets show the selected one, and press-and-hold and flicks are off.
- **Spotify library widget**: tabs are Albums (recently played), Playlists and Search with cover thumbnails, plus a bottom strip with the cover, song, artist and Like. The Playing tab and playback controls are gone (decision 004).
- **Play in collection**: tapping a song inside an album or playlist plays it there, so playback continues through the collection (decision 003).
- **Fixed** Spotify sign-in timing out on networks with unrouted IPv6. Web requests now prefer IPv4 when a host has both.
- **Fixed** a crash on startup when Spotify was already connected.
