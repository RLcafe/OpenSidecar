# 009 — Tray mode while the phone is the screen

The user asked (21 Oct 2026) for the Windows app to leave the taskbar and sit in the notification area (tray) while the phone is their second screen, automatically and with a button they can toggle.

## Decision

- **Hide to tray** button in the dashboard header (after Second screen). It hides the window and removes it from the taskbar; a Widget Screen icon appears in the notification area. Clicking the icon, or *Show Widget Screen* in its menu, brings the dashboard back; *Close Widget Screen* in the menu quits. The icon exists only while the window is hidden (`src/app/TrayMode.cs`, a WinForms `NotifyIcon`, already part of the framework references).
- **Automatic.** Settings → Phone → *Move to the tray while my phone shows the dashboard* (on by default, applies right away). While a paired phone has read the dashboard in the last 15 seconds, the window moves to the tray once, with a one-time tip. A minute after the phone stops, the window comes back by itself, but only if the phone hid it. If the user brings the window back while the phone is connected, it stays until the phone disconnects and reconnects. Edit layout and an open Settings panel are never hidden automatically.
- **While hidden the PC dashboard doesn't poll**, like when it's minimized; the phone still gets fresh readings because the phone link reads services on demand.
- **Windows only.** The tray is a Windows concept, so the phone app is unchanged.

## Tradeoffs

- "Phone connected" means "a paired phone is reading the dashboard", so a phone with the app closed or the screen off counts as gone after a minute.
- The window hides rather than minimizing, so it can't be reached with Alt+Tab while in the tray; the tray icon is the way back.
