# 003 — Play songs inside their album or playlist

Decision date: 2026-10-09

## Decision

When a song is tapped inside an album or playlist in the Spotify widget, start that collection at the song with `PUT /me/player/play` and the body `{"context_uri": "<album or playlist URI>", "offset": {"position": N}}`. Spotify then continues through the rest of the album or playlist. The user asked for this after tapping a playlist song opened its link, which played the song from its original album.

This is a deliberate divergence from the as-built spec §5.1 ("Tap song or episode → external link, not a direct play request"). The baseline behavior remains in these cases:

- Search results and episodes still open their `open.spotify.com` link, because they have no collection to continue through.
- Connections without the playback permission (`user-modify-playback-state`) still open the link, as do accounts the API rejects for playback.

## Details

- `N` is the song's zero-based position in the collection, including items the widget hides (local files, unavailable tracks), so it matches Spotify's own positions. It comes from the page offset plus the raw index in the API response.
- If Spotify reports no active device (player 404), the widget opens the Spotify app with `spotify:` and retries the same request every 2 seconds, up to 6 times. If it still fails, the status asks the user to play anything in Spotify once and tap the song again.
- No new scopes, device picker or polling were added. While the request (and any retries) runs, the transport buttons are disabled, like other playback commands.

## Tradeoffs

Playing directly requires Spotify Premium and an available Spotify Connect device; the link fallback did not. Retrying after opening the app may still fail if the app opens without becoming the active device, because the widget has no device-listing permission (`user-read-playback-state`). Adding that permission would let the widget target a specific device, but it would require every existing connection to reconnect, so it was left out.

Real playback with the user's account has not been verified by automated checks. Tests use HTTP fixtures and never open links or start playback.
