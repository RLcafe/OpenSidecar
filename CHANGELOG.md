# Changelog

Released versions of Widget Screen, newest first. Each version is an annotated `vX.Y.Z` tag on `main`. Branches that haven't been merged yet are in [BRANCHES.md](BRANCHES.md), along with the version bump rules.

## Unreleased

Nothing yet.

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
