# Android app: a remote for the PC

Widget Screen for Android shows the PC's dashboard on a phone or tablet and controls the PC over home Wi-Fi: the same cards, palettes and layout as the Windows app. Decisions: `docs/decisions/007-android-app.md` (how it's built) and `docs/decisions/008-phone-remote.md` (the phone link).

## Build and install

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Test           # Windows, saves a real link answer for the next step
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\build-android.ps1 -Test   # APK + core checks
```

- Needs a JDK (17 or newer) and the Android SDK with a platform and build-tools (`%LOCALAPPDATA%\Android\Sdk`, or `ANDROID_HOME`). Nothing is downloaded.
- Output: `artifacts\android\WidgetScreen.apk`. Release copies get it in `releases\vX.Y.Z\WidgetScreen.apk` (see `scripts\release.ps1`).
- Install: copy the APK to the phone and open it (Android asks once to allow installs from that app), or with USB debugging on, `adb install -r artifacts\android\WidgetScreen.apk`. Newer builds install over older ones.

## Connecting the phone

1. On the PC: Widget Screen → Settings → Phone → turn on *Let my phone control this PC*. If Windows asks whether Widget Screen may use private networks, allow it.
2. Tap *Pair a phone*. A six-digit code and the PC's address appear for five minutes.
3. On the phone: the app opens Settings → PC on first launch. Tap *Find PCs on this Wi-Fi* and pick the PC (or type its address), enter the code and tap *Pair*.

The phone remembers the PC. *Forget this PC* on the phone or *Forget paired phones* on the PC undoes it.

## What the phone shows

Every card mirrors the PC and its buttons run on the PC: Focus timer, Now Playing, Quick Launch (confirmations appear on the phone), the Sound mixer (master volume and output, each app's volume, mute and output, the microphone), System stats, Weather, Spotify (using the PC's Spotify connection) and the Discord soundboard. Quiet mode in the header toggles the PC's Quiet mode. The clock is the phone's own time, in the PC's 12 or 24-hour format.

Set up on the PC, not the phone: Quick Launch buttons, the weather city, Spotify, Discord and which System stats readings show. The phone's own Settings: Theme, Display (keep the screen on, hide system bars, dashboard or list layout), Widgets (show or hide cards, Edit layout) and PC.

## Layout

- **Dashboard** (tablets held sideways and other wide screens, or chosen in Settings → Display): the 1920 × 720 design scaled to fit, with the header clock, Edit layout, Quiet mode and Settings. Edit layout works as on Windows. Back cancels.
- **List** (phones, or chosen in Settings → Display): the shown cards stacked in the dashboard's reading order. Sliders take the finger directly, so drag elsewhere to scroll.

## When something's wrong

- The status line says when the PC can't be reached; cards show *PC offline* and come back on their own.
- *This phone isn't paired anymore* means the PC forgot it (or its pairings couldn't be restored): pair again.
- Not found by *Find PCs*: some routers block broadcasts between devices; type the address shown on the PC instead.

## Unverified

Tested: both apps' checks (the Windows link over loopback, including a live read of this PC's media, audio and system state; the phone parsing that real answer) and Android Lint. Not yet run on a real phone: the rendered screens and touch, pairing and discovery over real Wi-Fi, the Windows Firewall prompt, and live Spotify and Discord use from the phone.
