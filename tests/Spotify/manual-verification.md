# Spotify account verification

Automated tests run through scripts/build.ps1 -Test. Captures under artifacts/tests/spotify-*-fixture.png use labeled sample data. Tests cover sign-in callbacks, encrypted persistence across a fresh process, saved Client ID across settings saves and restarts, transient refresh failures, library paging/search, inline UI, seek boundaries and permissions, one request per scrub gesture, token expiry and rate limits. Tests never authorize a real account or change personal playback or likes.

With your configured Spotify Web API app:

1. Register http://127.0.0.1:43827/callback and allowlist your account. Confirm Premium requirements.
2. Open Settings > Spotify, save the Client ID, close and reopen the app. The ID must remain. Connect and approve in your browser; success must appear only after encrypted tokens are saved. Return to Settings, which stays open and shows Connected.
3. Close and reopen the executable from another working directory or a new build folder. The account must restore without prompting. Play a song after the access token has expired to verify silent refresh.
4. Decline or cancel a reconnect attempt. The previous account and saved ID must remain. Close Settings while signing in and reconnect to confirm the callback port is released.
5. Choose Arrange dashboard for Spotify + Now Playing. Verify System Stats and Weather are hidden; Clock, Focus, Quick Launch and Mixer stay available. Cancel restores the old layout; Save and restart retain the new arrangement. Existing custom layouts must not be overwritten on startup.
6. Start a song on an active Spotify device. Check artwork, title, artist, elapsed time, pause/play/previous/next and Like. A rejected Like must not show a success state. External changes to likes should appear within a minute.
7. Drag or tap the Spotify seek bar with mouse and physical touch. Preview time should follow your finger, and playback changes once on release. Try keyboard arrows/Home/End, cancellation, song changes during a drag, paused playback and dragging to the end. End must not skip to another song. Reconnect once for the new playback permission; missing Premium, restricted items and no active device must have useful feedback.
8. Browse Albums, Playlists and Search inside the card, with Next/Previous, Enter, empty results, punctuation and long titles. Open an album or owned/collaborative playlist to browse songs. Other playlists may deny contents; Open Spotify must still work. No action should open a popup or force account setup. Disconnected tabs should show guidance.
9. Verify Windows Now Playing independently with Spotify both connected and disconnected. Check its transport and seek behavior with supported and unsupported media apps.
10. Disconnect network and restore it; the login must remain saved. Disconnect in Settings and restart; tokens stay cleared, ID stays saved. Revoke the grant in Spotify to check reconnection guidance.
11. Hide Spotify or minimize the app and confirm no new polling. Quiet mode polls at most every 30 seconds. Verify theme changes, 115% text, 560 x 500 minimum widget size, 1920 x 720 fullscreen, and physical touchscreen targets.

The app controls an existing Spotify device; it does not stream music or choose a playback device. Official Spotify links may open in a browser depending on Windows configuration. Custom --settings paths intentionally isolate credentials in that settings folder.
