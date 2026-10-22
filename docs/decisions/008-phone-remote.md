# 008 — The Android app is a remote for the PC

After trying the first Android build (decision 007, a standalone phone dashboard), the user said they wanted the widget app on their phone to control their PC, and agreed (21 Oct 2026) to the one change that needs: the Windows app accepting connections from the phone. This replaces the standalone behavior in decision 007; its build, packaging and UI-kit decisions still stand.

## Decision

- **A phone link in the Windows app.** Settings → Phone (a new tab after Discord) has *Let my phone control this PC* (off by default, applies right away), *Pair a phone*, *Forget paired phones*, the PC's addresses and the pairing code. When on, `RemoteServer` listens for HTTP/1.1 on TCP 43830 (one request per connection, 16 KB headers, 64 KB bodies) and answers UDP discovery on 43831. It is part of the app, not a separate server program. Only private, link-local and loopback addresses may connect (`DashboardController.IsLocal`).
- **Pairing.** *Pair a phone* shows a six-digit code for five minutes; five wrong tries close it. A paired phone gets a random 256-bit token; the PC keeps only its SHA-256 hash, DPAPI-encrypted in `remote-devices.dat` next to `settings.xml` (`RemotePairing`). The phone keeps the token encrypted with an Android Keystore key in `pc-link.dat`. Every request but `/hello` and `/pair` needs the token.
- **Requests** (all answered on the PC's UI thread, against the same services the dashboard uses, in `DashboardController.Remote.cs`):
  - `GET /state?parts=…` returns only the sections the phone asks for: `focus`, `media`, `audio`, `system`, `deck`, `weather`, `spotify`, `discord`, plus the PC name, Quiet mode and the 24-hour clock. The phone asks for the cards it shows, every 2 s (5 s in the PC's Quiet mode), so a hidden card isn't read on the PC either, and nothing is read while no phone is connected.
  - `GET /art` returns the current Now Playing cover as JPEG.
  - `POST /command` runs media, mixer (master volume, mute, default output, app volume, mute and output, microphone), focus, Quiet mode, Quick Launch and soundboard actions. Quick Launch buttons are identified by index and label, so a deck changed on the PC can't run the wrong button, and buttons the PC confirms are confirmed on the phone instead of in a dialog on the PC.
  - `POST /spotify` proxies the Spotify widget's calls (current song, browse, tracks, liked, like, play) to the PC's Spotify connection, and can open Spotify on the PC when no device is active. The phone needs no Spotify setup.
- **The phone app** keeps the dashboard, palettes, Edit layout and list layout from decision 007, but every card now mirrors the PC: Focus timer, Now Playing (Windows media), Quick Launch, the Sound mixer with per-app volumes and outputs, System stats (CPU with history, GPU, RAM, network, uptime), Weather (the PC's city, fetched from Open-Meteo by the phone), Spotify (through the PC) and the Discord soundboard. Its Settings are Theme, Display, Widgets and PC (find PCs on the Wi-Fi, or type the address, then enter the code). Quick Launch buttons, the weather city, Spotify, Discord and System stats readings are set up on the PC.
- The Android-only features of decision 007 are gone: Android volume streams, notification-access Now Playing, battery and storage stats, launching phone apps, and the phone's own Spotify sign-in (`widgetscreen://spotify-callback` is no longer used).

## Rationale and tradeoffs

- **Plain HTTP on the local network.** There is no certificate to install, and the phone and PC need nothing but the app. The traffic isn't encrypted, so someone on the same Wi-Fi could read the dashboard state or copy a token from a request. Pairing, the local-address check and *Forget paired phones* limit this; Settings → Phone says to use a Wi-Fi you trust. Encrypting the link would be a later decision.
- **Polling, not push.** Polling every 2 s matches the Windows dashboard's own refresh cadence and keeps the server one-request-per-connection. Taps refresh right after the command.
- **Windows Firewall** asks once to allow Widget Screen on private networks when the link starts. If the user declines, the phone can't connect; Settings on both sides says so.
- **Not over the internet.** Reaching the PC from outside the home network would need port forwarding or a relay service; that's out of scope.
- **Rule break, agreed.** CLAUDE.md said "no companion server". The user approved this exception for the phone link; it is off by default and lives inside the app.
- The Windows checks (`tests/Remote/RemoteTests.cs`) pair, read state, run commands and discover the PC over loopback with an isolated settings folder, and save a real `/state` answer to `artifacts\tests\remote-state-sample.json`; the Android checks parse that file, so both sides agree on the format. A real phone on real Wi-Fi, the firewall prompt and UDP broadcast across a router are still unverified.

## Hardening (22 Oct 2026, from the app audit)

- **Web pages are refused.** A page in the user's browser could reach the link through DNS rebinding. `RemoteServer.RefuseBrowser` answers 403 to any request with an `Origin`, `Sec-Fetch-Mode` or `Sec-Fetch-Site` header, or whose `Host` isn't an IP address or `localhost`. The phone app sends a bare address as `Host` and none of those headers.
- **At most 32 connections** are served at once; extra ones are closed, not queued, so the phone's next request still gets a turn.
- **Per-phone forget and last seen.** `RemoteDevice.LastSeenUtc` follows each authorized request and is saved at most every five minutes; Settings → Phone lists each phone with a Forget button, and *Forget all phones* stays.
- **The phone only talks to home addresses** (`PcLink.isHomeAddress`: private and link-local IPv4, loopback, `.local` names), so a typo can't send the bearer token to the internet. Discovery sends to each interface's directed broadcast first and 255.255.255.255 last, because routers and many phones drop the limited broadcast. Unforced polling backs off 5 s, 15 s, then a minute while the PC is away (`PcLink.retryDelayMs`).
- **Cleartext stays app-wide on Android.** A network security config can limit cleartext to named domains, not to IP ranges, and the PC is reached by address; so `usesCleartextTraffic` stays on and the address check above does the limiting.
- **Still plain HTTP.** Encrypting the link (a self-signed certificate pinned at pairing) remains a future decision; it is real work on both sides and not needed on a trusted home Wi-Fi.
