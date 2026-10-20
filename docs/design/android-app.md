# Android app

Widget Screen for Android phones and tablets: the same dashboard, palettes and widgets as the Windows app, adapted where Android works differently. Decision record: `docs/decisions/007-android-app.md`.

## Build and install

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-android.ps1 -Test   # APK + core checks
```

- Needs a JDK (17 or newer) and the Android SDK with a platform and build-tools (`%LOCALAPPDATA%\Android\Sdk`, or `ANDROID_HOME`). Nothing is downloaded.
- Output: `artifacts\android\WidgetScreen.apk`. Release copies get it in `releases\vX.Y.Z\WidgetScreen.apk` (see `scripts\release.ps1`).
- Install: copy the APK to the device and open it (Android asks once to allow installs from that app), or with USB debugging on, `adb install -r artifacts\android\WidgetScreen.apk`. Newer builds install over older ones.
- Android Lint: `lint.bat --sources android\src --resources android\res --classpath artifacts\android-obj\classes --libraries <platform android.jar> android`.

## First run

1. **Now Playing**: tap *Allow access* on the card (or Settings → Widgets) and turn on Widget Screen under notification access. Android needs this for any app to see or control other apps' media.
2. **Spotify**: in Settings → Spotify, use the same Spotify developer app as on Windows. Add `widgetscreen://spotify-callback` to its redirect addresses (next to `http://127.0.0.1:43827/callback`), paste the Client ID and tap *Connect Spotify*. Spotify sends the browser back to the app when you approve.
3. **Weather and System stats** start hidden, as on Windows. Turn them on in Settings → Widgets or in Edit layout.

## Layout

- **Dashboard** (tablets held sideways and other wide screens, or chosen in Settings → Display): the 1920 × 720 design scaled to fit, with the header clock, Edit layout, Quiet mode and Settings. The widget canvas stretches to the screen's shape. Edit layout works as on Windows: drag a card to move it, drag a corner grip to resize, Snap, Undo/Redo, Reset layout, Widgets (show or hide), Cancel and Save layout. Back cancels.
- **List** (phones, or chosen in Settings → Display): the clock and buttons on top, then the shown widgets stacked in the dashboard's reading order (left to right). Sliders take the finger directly, so drag elsewhere to scroll. Widgets are shown or hidden in Settings → Widgets.

## What differs from Windows

See the table in decision 007. In short: Sound controls Android's volume streams instead of per-app sessions; Quick Launch has App, Website and Control actions; System stats shows Battery and Storage instead of CPU and GPU; Spotify signs in through an app link; the Discord soundboard, microphone button, output levels and Second screen are not on Android.

## Unverified

The APK was built and its shared logic tested on the desktop JVM, and Android Lint found no errors. It has not yet run on a device or emulator, so these are unverified: the rendered layout at real screen sizes, touch on sliders and in Edit layout, notification access and media control in real apps, volume changes under Do Not Disturb, the output switcher, the Spotify sign-in round trip and playback, and the on-screen keyboard over Settings.
