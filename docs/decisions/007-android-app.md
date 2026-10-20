# 007 — Android app

The user asked for an Android version of Widget Screen that builds an installable APK, with the Windows app left unchanged, and set a standing rule: future changes go into both versions.

## Decision

- **Native Android, same repository and versions.** The app lives in `android/` (manifest, `res/`, `src/`, `tests/`) and builds with `scripts\build-android.ps1` into `artifacts\android\WidgetScreen.apk`. Both apps share the `vX.Y.Z` tags; the APK's version name and code come from the same `git describe` as the exe (`versionCode = MAJOR·1000000 + MINOR·10000 + PATCH·100 + commits since the tag`, capped at 99).
- **No Gradle, no libraries.** Like the Windows build with `csc.exe`, the Android build calls the SDK's own tools directly: `aapt2` for resources, `javac` against the platform `android.jar`, `d8`, `aapt add`, `zipalign` and `apksigner`. The code is Java with framework views only (no AndroidX, no Kotlin, no WebView). The build signs with the standard Android debug key (`%USERPROFILE%\.android\debug.keystore`, created if missing), so every build installs over the previous one on the same device.
- **minSdk 26 (Android 8.0), targetSdk 35, compiled against the newest installed platform.** Version-specific calls are guarded (`Build.VERSION.SDK_INT`), and Android Lint runs clean with the manifest's `uses-sdk`.
- **A testable core.** `com.alialyami.widgetscreen.core` holds everything that doesn't need Android: settings and their XML file, the widget canvas math, the focus timer, formats, palettes, Quick Launch validation, the weather and Spotify parsing, and a small JSON reader. `build-android.ps1 -Test` compiles it *without* `android.jar` (so it can't depend on Android) and runs `android\tests\…\AndroidTests.java` on the desktop JVM, printing PASS/FAIL lines like the Windows test exe.
- **The same dashboard.** Views are built in the Windows design units and scaled uniformly by `ScaledHost`, the counterpart of the WPF `Viewbox`. On screens wide enough for the 1920 × 720 design at 0.6 dp per unit or more (tablets held sideways, wide displays), the dashboard keeps its header, footer and free 1856 × 576 canvas; the canvas stretches to fill the screen's shape, while text stays at its design size. Elsewhere (phones) the same cards stack in a scrolling list. Settings → Display can force either.
- **Settings** is an overlay panel with the Windows tabs (Theme, Display, Widgets, Weather, Spotify, Quick Launch), up to 1440 × 672 units and full screen on phones. Dropdowns became segmented buttons, which are easier to hit. Appearance previews live and Cancel restores it.

## Widgets on Android

| Windows | Android |
| --- | --- |
| Header clock, Focus timer, Weather | Same behavior and wording. The focus run survives the app being closed (it's saved with `SystemClock.elapsedRealtime`, which keeps counting while the screen is off). |
| Now Playing (Windows media sessions) | Android media sessions from any app, read and controlled through notification access (`MediaListener`). Until access is allowed, the card says so and offers *Allow access*. |
| Sound mixer | **Sound**: Android has no per-app volume. Media volume is the master slider with mute; the output name under it opens Android's output switcher (Android 14+), the volume panel (10–13) or sound settings. Ringtone, Notifications, Alarms and Calls replace the app sessions. The microphone button and live output levels are dropped (apps can't mute the system microphone, and levels would need the record permission). |
| Quick Launch | App (an installed app, or the phone's default calculator, files, browser and so on), Website and Control. File, Folder and Hotkey actions don't exist on Android. Controls: play/pause, previous, next, mute, volume up/down, focus, Quiet mode, Android sound settings. Buttons keep drawn glyphs, not app icons. |
| System stats | Battery, RAM, Storage, Network and Uptime. Android doesn't let apps read CPU or GPU usage, so those rows became Battery and Storage. |
| Spotify | Same widget, same Web API calls and wording (`SpotifyService` is a port). Sign-in returns through the app link `widgetscreen://spotify-callback`, so that address must be added to the Spotify app next to the Windows one; the same Client ID works for both. Tokens are encrypted with an AES key in the Android Keystore (`spotify-session.dat` in the app's private folder), the Android counterpart of DPAPI. When no Spotify device is active, the widget opens the Spotify app and retries. |
| Discord soundboard | **Dropped.** It plays through the Discord desktop app's local RPC pipe, which the Android Discord app doesn't offer. |
| Second screen, Close | Not needed: the app fills the screen; Android's Home and Back leave it. Settings → Display can hide the status and navigation bars and keep the screen on. |

## Rationale and tradeoffs

- Gradle with the Android Gradle Plugin would download a build system and plugins on first use. Calling the SDK tools keeps the build offline, small (about 120 KB of APK) and in the same spirit as `build.ps1`, at the cost of features Gradle gives for free (resource shrinking, library dependencies, instrumentation tests). If the app ever needs a library, moving to Gradle is a separate decision.
- The debug key is fine for installing on the user's own devices. It can't publish to Google Play; a release key would be a separate decision.
- A loopback listener like the Windows sign-in isn't reliable on Android: while the browser is in front, Android may freeze the app before Spotify redirects. The app link avoids that, at the cost of one extra redirect address in the user's Spotify app.
- Stretching the canvas instead of letterboxing means cards on a 16:10 tablet are taller than on the 1920 × 720 screen. Text and touch targets keep their sizes; only the free space grows.
- Android lists notification access as a sensitive permission. It is only used for media sessions; the listener ignores notifications.
- The JVM tests cover the shared logic only. Views, Android services and live accounts need a device or emulator; see "Unverified" in `docs/design/android-app.md`.
