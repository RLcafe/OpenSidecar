# 013 — Tapping the dashboard keeps the game focused

The user asked (31 Oct 2026) that clicking or tapping Widget Screen on the second screen should not take the keyboard and foreground away from the game or app they're using on the main screen.

## Decision

- **The dashboard window doesn't activate when it's clicked or tapped** (`src/app/FocusKeeper.cs`). It carries the `WS_EX_NOACTIVATE` extended style and answers `WM_MOUSEACTIVATE` with `MA_NOACTIVATE` and `WM_POINTERACTIVATE` with `PA_NOACTIVATE`. Buttons, sliders, dropdowns and drags still get their mouse, touch and pen input; Windows just leaves the foreground where it was. `WS_EX_APPWINDOW` keeps the taskbar button, which a no-activate window would otherwise lose, and a `WM_STYLECHANGING` hook keeps both bits when WPF rewrites the style (fullscreen, window preview, the tray).
- **Text fields are the exception.** A press on a text box, password box or editable dropdown (the Spotify search box, Settings fields) drops the style and activates the window, because typing needs the keyboard. The window then behaves normally until something else is activated (for example, clicking back into the game), and the style comes back.
- **Showing the window doesn't activate it** while the setting is on (`ShowActivated = false`), so launching at sign-in or coming back from the tray doesn't steal the focus. The phone-driven return from the tray never activates, whatever the setting (`TrayMode.Show(false)`). The fullscreen and window moves (`DisplayService`) pass `SWP_NOACTIVATE`, so a monitor being unplugged or replugged mid-game doesn't pull the focus either.
- **A setting**: Settings → Display → *Keep the focus on the game or app I'm using*, on by default; off gives back the old window that activates on every click.
- **Android: no change.** The phone is a separate device, so tapping it never touches the PC's focus; the phone link already runs every action without activating the PC window, and the one place it did (the automatic return from the tray) is fixed on the Windows side. On the phone itself, the only analogue is split-screen with a game, where an unfocusable window (`FLAG_NOT_FOCUSABLE`) would also send Back and the keyboard to the other app, and can't be checked without a device; it wasn't done.

## Tradeoffs

- **Keyboard shortcuts** (F11, Escape, and Edit layout's arrows and Ctrl+Z) reach the window only once it has the keyboard, which now means after tapping a text field. The on-screen buttons do the same things; turning the setting off brings the shortcuts back for keyboard and mouse users.
- **Typing takes the focus**, by necessity. Searching Spotify mid-game moves the keyboard to the dashboard until you click back into the game.
- **Exclusive-fullscreen games** may still minimize when anything on another screen is touched, depending on the game; the window no longer activates, which is the part Widget Screen controls. Borderless and windowed games keep their focus.
- **Unverified on the real touchscreen.** The automated check proves the style, the activation answers, the text-field switch and the setting, but Windows refuses pointer messages sent by an app, so a real finger (and the WPF touch stack with a window that's never active) still needs the user's try.
