# 004 — Spotify widget becomes a library browser with a now-playing strip

Decision date: 2026-10-09

## Decision

At the user's request, the Spotify widget drops its Playing tab and playback controls. It now has:

- Three tabs: **Albums**, **Playlists** and **Search**. Albums is selected by default and loads when the widget starts.
- A cover thumbnail on every album, playlist and song row that has artwork. Songs inside an album have no per-song artwork in the API, so those rows show the fallback glyph.
- A strip at the bottom with the current song's cover, title, artist and the Like button.
- One status line under the strip.

**Albums** shows recently played albums: distinct albums from `GET /me/player/recently-played?limit=50`, most recent first, ten per page. This needs the new `user-read-recently-played` scope. Connections made before this change keep working and show saved albums (`GET /me/albums`) with a hint to reconnect.

Removed: the Playing tab, Open in Spotify (current song), the seek bar with its times, and previous, play/pause and next. The Windows Now Playing widget is unchanged and keeps its own controls. Tapping a song in an album or playlist still plays it in that collection (decision 003).

This diverges from the as-built spec §5.1–5.2 (four tabs, the Now view, transport and seek, and "No thumbnail per result").

## Details

- Covers are loaded only from HTTPS hosts ending in `.scdn.co` or `.spotifycdn.com`. Playlist images also come from `*.spotifycdn.com`. Row covers decode at 104 px into an in-memory cache that is cleared once it reaches 120 entries. Nothing is written to disk.
- The current-playback poll keeps its existing 10 s interval (30 s in Quiet mode), so the strip and Like state stay current. No new timers were added.
- The paging row is hidden when a list has only one page.
- `SpotifyService.PlaybackAsync` and `SeekAsync` remain in the service and its tests, but the widget no longer calls them.

## Tradeoffs

Spotify's recently-played history only covers the last 50 songs, so Albums can list at most that many distinct albums. Showing recent albums requires one reconnect for existing users. Without transport controls in the widget, pausing or skipping happens in Spotify, through the Windows Now Playing widget, or through Quick Launch media controls.
