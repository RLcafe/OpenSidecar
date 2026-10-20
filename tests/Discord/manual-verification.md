# Discord soundboard verification

Automated checks (`scripts\build.ps1 -Test`) run against an in-process fake of the Discord pipe and a fake token server. They cover frames, the handshake, unknown app ids, Discord not running, sound ordering, play arguments, error messages, token refresh and revocation, encrypted storage, voice channel events, reconnection and the widget's empty, loaded and not-in-voice states. They never open the real Discord pipe or contact discord.com.

With your own Discord app and the Discord desktop app signed in:

1. In the Developer Portal, create an application, add `http://127.0.0.1` under OAuth2 → Redirects, and copy the Client ID and Client Secret.
2. Settings → Discord: paste both and tap Connect Discord. Discord shows an Authorize popup; approve it. Settings shows Connected with your name. Reopen Settings: the secret field is empty and its label says a secret is saved.
3. Decline the popup once: the app details stay saved and the message says it was declined.
4. Edit layout: turn on Soundboard, place it and save. Without a voice channel the buttons are disabled and the card says to join one.
5. Join a voice channel. The channel name appears and the buttons enable without tapping anything. Tap a sound: everyone in the channel hears it, and the card confirms it.
6. Switch to a channel in another server: that server's sounds move to the front. Without Nitro, another server's sound shows the Nitro message.
7. Quit Discord: the buttons disable and the card says to open Discord. Start it again: the widget reconnects within about ten seconds.
8. Restart Widget Screen: the connection restores without a popup. After the token expires (about a week), it refreshes silently.
9. Disconnect in Settings: the widget returns to its setup state and the app details stay saved. Deauthorize the app in Discord (User Settings → Authorized Apps): the widget asks you to connect again.
10. Hide the widget or minimize the app: no reconnect attempts while it's hidden.
