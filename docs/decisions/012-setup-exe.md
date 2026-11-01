# 012 — Our own setup exe, per-user install

Date: 31 Oct 2026. Status: accepted (user request: "an exe package that automatically places a shortcut in the Startup folder and Start menu and can be uninstalled").

## Decision

Widget Screen ships a setup exe, `WidgetScreen-Setup-vX.Y.Z.exe`, written in C# in `installer/` and built by `scripts\package.ps1` with the same Framework `csc.exe` as the app. The app's two files are embedded as resources. Setup installs for the signed-in user only:

| What | Where |
| --- | --- |
| App | `%LOCALAPPDATA%\Programs\Widget Screen\WidgetScreen.exe` (+ `.config`) |
| Uninstaller | `%LOCALAPPDATA%\Programs\Widget Screen\Uninstall Widget Screen.exe` (a copy of setup) |
| Start menu | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Widget Screen.lnk` |
| Start with Windows | `%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\Widget Screen.lnk` (checkbox, on by default) |
| Installed apps entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\WidgetScreen` |
| Hardware sensors (optional, decision 016) | `%LOCALAPPDATA%\Programs\Widget Screen\sensors\` (LibreHardwareMonitor 0.9.6, its notice and licences; "Install hardware sensors" checkbox, on by default) |
| Settings (untouched) | `%LOCALAPPDATA%\WidgetScreen\` |

## Why

- **No third-party tools.** WiX, Inno Setup and NSIS are installs and packages the project doesn't take; MSIX needs signing and a package identity, and would change where the app keeps its files. A few hundred lines of C# do the whole job and build with what is already on every Windows PC.
- **Per-user, no administrator rights.** The dashboard is a personal app. An all-users install in Program Files meant every update needed an elevated copy and a hand-edited Startup shortcut (the situation the user was in until October). Per-user installs are what VS Code, Discord and Spotify do.
- **One file.** The setup exe both installs and updates (it closes a running installed copy first), and the copy in the install folder is the uninstaller that Installed apps runs.

## Tradeoffs

- The setup exe isn't code-signed, so SmartScreen may say "Windows protected your PC" the first time; *More info → Run anyway*. Signing needs a certificate the project doesn't have.
- Another Windows account on the same PC needs its own install.
- A firewall rule Windows created for an older copy's path doesn't cover the new path, so with the phone link on, Windows asks once more for the installed copy.
- Uninstall can't delete a firewall rule (that needs admin). Windows leaves it harmless once the exe is gone.
- The app has no single-instance guard, so opening the desktop shortcut (which points at `releases\`) while the installed copy runs gives two dashboards. Use one or the other.

## Hardware sensors (1 Nov 2026)

Setup also carries the optional LibreHardwareMonitor files (decision 016) when the build has them, behind a checkbox that says what they are, their licences and that the PawnIO driver isn't included. They're the only third-party files Widget Screen ships; uninstalling removes them, and the uninstaller also removes full-sensors mode's task. Setup still needs no administrator rights; only removing that task asks.

## Android

Nothing changes there: an APK is already Android's installer, and Android's own Uninstall removes the app and its private folder.
