# Setup exe (31 Oct 2026)

The decision and its tradeoffs are in `docs/decisions/012-setup-exe.md`. This note is the behaviour.

## Installing

Double-click `WidgetScreen-Setup-vX.Y.Z.exe` (in `releases\vX.Y.Z\`). One window, in the Midnight palette with the Lime accent and 52 px buttons:

- **Install Widget Screen** when it isn't installed, **Update Widget Screen** (naming both versions) when it is.
- *Start Widget Screen when I sign in to Windows*: on for a new install; an update keeps the current choice.
- *Open Widget Screen when setup finishes*: on. It doesn't open a second copy if any Widget Screen is already running.
- A warning names any other Startup-folder shortcut that also opens `WidgetScreen.exe`, so the app wouldn't open twice. Setup doesn't delete it.
- If the installed copy is running, setup says so and closes it before copying: it asks the window to close (the app saves on the way out), and ends it after a few seconds if it's hidden in the tray.

## Uninstalling

Settings → Apps → Installed apps → Widget Screen → Uninstall runs `Uninstall Widget Screen.exe /uninstall`. The window offers *Also delete my settings, Spotify and Discord sign-ins and paired phones* (off). Uninstall closes the installed copy, removes both shortcuts, the entry and the install folder (a hidden command removes the folder a moment after the uninstaller exits, since it can't delete itself while running).

## Switches

| Switch | Effect |
| --- | --- |
| `/S` | No window. Exit code 0 on success, 1 on failure (reason appended to `%TEMP%\WidgetScreen-Setup.log`). |
| `--no-startup` | Install without the Startup shortcut (removes it on an update). |
| `--launch` | Silent install, then open the app. |
| `/uninstall` | Uninstall instead of install. |
| `--remove-settings` | With `/uninstall`, also delete `%LOCALAPPDATA%\WidgetScreen`. |
| `--root <folder>` | Sandbox: every path and the registry key move under that folder. For the automated checks only. |

## Building and checking

`scripts\package.ps1 -Test` packs `artifacts\app` into `artifacts\setup\WidgetScreen-Setup-<version>.exe` and runs `tests\Installer\installer-checks.ps1`, which installs, updates, uninstalls (keeping and then deleting settings) and uninstalls again with nothing installed, all under a temporary `--root`. `scripts\release.ps1` does the same for each tag and puts the setup in the release folder.

Not checked automatically: the real Start menu, Startup folder and Installed apps (by design, so the checks never touch them), SmartScreen, and closing a real running copy.
