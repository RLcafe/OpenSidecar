# 001 — Spotify widget integration

Decision date: 2026-10-09

## Decision

Keep Windows Now Playing independent and add a dedicated Spotify widget. Account setup lives in Settings; albums, playlists, their available songs and search render inside the widget. Results link to Spotify. Add transport and seeking through the Web API, without streaming, WebView, device management, playlist editing or audio capture. Windows media seeking uses the session's native capability flag.

The default arrangement hides System Stats and Weather and keeps Clock, Focus, Now Playing, Quick Launch and Sound Mixer alongside Spotify. Preserve custom layouts, appending Spotify hidden if necessary; an undoable Settings action previews the new default.

Use Spotify Web API with Authorization Code + PKCE, an external browser, a random state, and a loopback callback at `http://127.0.0.1:43827/callback`. The user supplies their own Developer app Client ID; no client secret is shipped. The callback listener binds only to IPv4 loopback, accepts the expected callback and state, and shuts down on cancellation or timeout. Authorization/refresh tokens are encrypted with Windows DPAPI for the current user and kept separately from settings.

## API surface

| Behavior | API | Permission |
| --- | --- | --- |
| Current playback | `GET /me/player/currently-playing` | `user-read-currently-playing` |
| Saved state | `GET /me/library/contains?uris=...` | `user-library-read` for tracks |
| Like/unlike song | `PUT/DELETE /me/library?uris=...` | `user-library-modify` for tracks |
| Saved albums | `GET /me/albums` | `user-library-read` |
| User playlists | `GET /me/playlists` | `playlist-read-private`, `playlist-read-collaborative` |
| Album tracks | GET /albums/{id}/tracks | User authorization |
| Playlist items | GET /playlists/{id}/items | Owned/collaborative playlist access |
| Seek | PUT /me/player/seek?position_ms=... | user-modify-playback-state, Premium |
| Play/pause/previous/next | PUT /me/player/play, PUT /me/player/pause, POST /me/player/previous, POST /me/player/next | user-modify-playback-state, Premium |
| Find songs | `GET /search?type=track&limit=10&q=...` | User authorization |

Generic library endpoints handle Spotify URIs. Older `/me/tracks` write endpoints were removed for new Development Mode applications; do not build against them. Search respects the current maximum page size of ten. Browse lists are paginated instead of loading whole libraries.

## Rationale and tradeoffs

This uses built-in .NET Framework HTTP, JSON, cryptography, and WPF functionality with no new packages. Native UI retains theme resources and touch targets. Spotify has a 560 x 500 minimum size to fit inline browsing and 48-pixel touch controls.

The user must configure a Spotify app and complete interactive sign-in. Spotify's Development Mode requires the owner to have Premium and users to be allowlisted; API quotas and permissions can reject operations. The integration handles unavailable playback, unsupported/local items, expired authorization, 403, and 429 explicitly. Token refresh is serialized and disconnect invalidates in-flight token updates. Refresh tokens can expire, requiring renewed sign-in.

Read playback no faster than ten seconds (thirty in Quiet mode), skip new polls while hidden/minimized, and cache artwork by URL. Saved state is queried when the song changes and roughly once per minute so likes changed in Spotify can be reflected. Rate limits share one backoff across widget and browsing requests. Progress extrapolates locally once per second from playback samples. Scrubbing previews locally and commits once on release, with keyboard support. The service checks that the same item is current, honors seek restrictions, and clamps to duration minus one millisecond to avoid skipping at the end. An active Premium playback device is required. Existing sessions without the new scope remain connected for reading/likes and get reconnect guidance for playback.

A fixed loopback port makes setup copyable but may already be in use. Close the competing process or another Widget Screen instance before retrying. The widget opens official HTTPS Spotify links, which may open the browser depending on Spotify's Windows/link preferences. Account setup and real library mutation need user-account testing; automated checks use isolated HTTP fixtures and do not change personal playback or liked songs.

The callback must exchange the authorization code and persist encrypted tokens before it tells the browser that Spotify is connected. A successful exchange updates Settings and refreshes the visible Spotify widget; failures remain visible in Settings and return an error response to the browser. Client ID is saved in ordinary preferences before browser launch and retained through disconnects or failed sign-in. Only explicit invalid-grant/unauthorized responses clear expired credentials; transient token-server errors retain the login. Startup and refresh reuse encrypted tokens from the settings directory, independent of executable location. The manually compiled executable declares .NET Framework 4.8 targeting, and configuration enables operating-system TLS defaults and strong cryptography to avoid legacy HTTPS defaults. See [Microsoft TLS guidance](https://learn.microsoft.com/en-us/dotnet/framework/network-programming/tls). Loopback regression tests exercise the complete callback/token/save sequence without opening a browser or using a real Spotify account.

## Sources checked

- [PKCE flow](https://developer.spotify.com/documentation/web-api/tutorials/code-pkce-flow)
- [Redirect URI requirements](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri)
- [Current playback](https://developer.spotify.com/documentation/web-api/reference/get-the-users-currently-playing-track)
- [Save library items](https://developer.spotify.com/documentation/web-api/reference/save-library-items)
- [Check saved items](https://developer.spotify.com/documentation/web-api/reference/check-library-contains)
- [2026 API migration](https://developer.spotify.com/documentation/web-api/references/changes/february-2026)
- [Development Mode requirements](https://developer.spotify.com/documentation/web-api/concepts/quota-modes)
- [July 2026 quota updates](https://developer.spotify.com/blog/2026-07-23-web-api-quota-updates)
- [Refresh token expiration](https://developer.spotify.com/blog/2026-06-18-refresh-token-expiration)

- [Seek playback](https://developer.spotify.com/documentation/web-api/reference/seek-to-position-in-currently-playing-track)
- [Playlist items and ownership limits](https://developer.spotify.com/documentation/web-api/reference/get-playlists-items)
- [Windows playback-position capability](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionplaybackcontrols.isplaybackpositionenabled)
