# 006 — Discord soundboard

The user asked to trigger Discord's soundboard sounds, in the voice channel they're in, from the dashboard (20 Oct 2026).

## Decision

- **Through the Discord desktop app's local RPC pipe.** `DiscordRpc` talks to `\\.\pipe\discord-ipc-0…9`: a handshake with the user's own application id, then JSON commands with a nonce. Sounds play from the user's own account, in their current voice channel, exactly as if they tapped them in Discord.
- **The user's own Discord application.** Like Spotify, the user creates an app in the Discord Developer Portal and pastes its Client ID and Client Secret into Settings → Discord. `AUTHORIZE` makes Discord show its own approval popup; the code is exchanged at `https://discord.com/api/oauth2/token` for the `rpc`, `rpc.voice.read` and `rpc.voice.write` scopes. Without a secret the app must be a Public Client, and the exchange uses PKCE. The app details and tokens are stored only in DPAPI-encrypted `discord-session.dat` next to `settings.xml` (git-ignored), never in `settings.xml`.
- **Commands.** `AUTHENTICATE` with the saved token (refreshed once when expired or rejected), `SUBSCRIBE VOICE_CHANNEL_SELECT`, `GET_SELECTED_VOICE_CHANNEL`, `GET_SOUNDBOARD_SOUNDS` and `PLAY_SOUNDBOARD_SOUND { guild_id, sound_id }`.
- **Widget.** A new `Soundboard` widget, hidden by default and turned on in Edit layout (minimum 360 × 240). It pages as many 148 × 56 sound buttons as fit: the current server's sounds first, then Discord's built-in sounds, then other servers' sounds. Until Discord is connected it shows one setup state. Voice channel changes and Discord closing arrive as pipe events, so it never polls; it only retries the connection every 10 s (30 s after an error) while visible.
- **Settings tab.** "Discord" is added after Quick Launch, so the existing tab order and indexes stay the same.

## Rationale and tradeoffs

- **Why not the HTTP API.** `POST /channels/{id}/send-soundboard-sound` is documented, but it plays the sound from whoever calls it, and that caller has to be connected to the voice channel. In practice that means a bot joined to the channel, and the sound would come from the bot, not from the user. Calling it with the user's own login token would be a self-bot, which Discord's Terms of Service forbid.
- **Why RPC is acceptable.** RPC is Discord's official local control interface (Stream Deck and similar tools use it). Every command is gated by OAuth scopes the user approves in Discord's own popup, and no user token is read or stored.
- **Undocumented commands.** `GET_SOUNDBOARD_SOUNDS` and `PLAY_SOUNDBOARD_SOUND` are implemented in the Discord desktop app's RPC server (they require `rpc` and `rpc.voice.write`) but aren't in Discord's public RPC documentation. A future Discord update could rename or remove them. Then the widget shows Discord's error, and the rest of the dashboard is unaffected.
- **Personal apps only.** Discord limits the `rpc` scopes of unapproved apps to the app's owner and up to 50 testers. That fits a personal dashboard: the app has to belong to the Discord account that uses it.
- **Nitro.** Discord only lets Nitro users play other servers' sounds. Those sounds are listed last, and a refusal is explained.
- **No emoji.** Sounds show their names only. The dashboard doesn't use emoji as icons.
