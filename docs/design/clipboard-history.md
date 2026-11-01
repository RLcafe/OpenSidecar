# Clipboard history

User request, 1 Nov 2026 (Windows, with the phone as a remote). A Clipboard card keeps the last texts copied on the PC. A tap puts one back on the clipboard and pastes it into the app in front. It is hidden by default; turn it on in Edit layout's Widgets list.

## Watching

- `ClipboardService` registers a message-only window with `AddClipboardFormatListener` and reads the clipboard when Windows sends `WM_CLIPBOARDUPDATE`: no polling and no timer. It only listens while the Clipboard widget is shown on some page (`WidgetLayout.ShownAnywhere`), and never while rendering a picture.
- Only text is kept (`UnicodeText`), up to 20,000 characters each. Copying a text that is already in the history moves it to the top instead of repeating it. The history holds 5 to 50 texts (Settings → Widgets → Clipboard history, default 10).
- **Private copies are skipped.** A copy that carries `ExcludeClipboardContentFromMonitorProcessing` or `Clipboard Viewer Ignore`, or `CanIncludeInClipboardHistory` set to 0 (what password managers such as KeePass, 1Password and Bitwarden set, and what Windows' own clipboard history honours), is never read into the history. If the clipboard can't be read, the copy is skipped too.
- **Pause** (on the card or the phone) stops keeping new copies until Resume; it lasts until the app closes. **Clear** asks first on the confirm sheet.

## Memory or disk

By default the history is only in memory and is gone when the app closes. Settings → Widgets → "Keep the history when Widget Screen closes" saves it to `clipboard-history.dat` next to `settings.xml`, encrypted with DPAPI for the Windows account (like the Spotify and Discord sessions); turning it off deletes the file.

## Pasting

A tap writes the text to the clipboard (retrying while another app holds it) and sends Ctrl+V through `DeckHotkeys`, the same way Quick Launch hotkeys reach "the app you were in": the dashboard never takes focus (decision 013), so the app in front keeps the keyboard and receives the paste. If no app can take it, the text is still on the clipboard and the status line says why it wasn't pasted. Elevated apps and some games refuse simulated keys, as with hotkey buttons.

## Designs (`CardSizing.Clip`, the same numbers on the phone)

| Design | When | Shows |
| --- | --- | --- |
| Small | under 340 wide or 220 tall | One-line rows, and Pause and Clear as icon buttons. |
| Medium | otherwise, below Large | Rows with when each text was copied ("4 min ago"), and Pause and Clear as text buttons. |
| Large | 440 × 400 and up | Two-line previews with when and how long ("120 characters"). |

The list scrolls inside the card; every row is a button at least 48 tall. The heading counts the texts, or says PAUSED in the warning colour.

## The phone

The `clipboard` section sends whether the PC is watching or paused and each text's id, a 200-character one-line preview, its length and when it was copied; full texts never leave the PC. Commands: `clipboard.paste` (by id; it pastes on the PC), `clipboard.pause` (with `paused`) and `clipboard.clear` (the phone asks first).

**Android limit:** since Android 10 an app can only read the clipboard while it is in front and focused, so the phone can't keep a history of its own copies. The phone card is the PC's history, and pastes on the PC.

## Checks

`tests/Clipboard/ClipboardTests.cs`: newest first, moving repeats to the top, trimming, blank and long texts, previews and wording; all three private formats skipped and a plain or allowed copy kept; nothing written to disk unless asked, the kept file encrypted, coming back after a restart and deleted when turned off; pausing; settings saved and clamped; hidden by default; and the card at three sizes at 140% text with a labelled fixture, keeping its 48-unit buttons inside the card, pausing, Cancel and Clear on the confirm sheet, and Settings saving the size. The phone-link checks read previews, pause and resume, refuse pasting a text that isn't there, and clear. The checks never write to the real clipboard; texts are offered to the history directly.

Not tried: a live copy reaching the listener and a live paste into another app (both touch the user's real clipboard), and the phone card on a device.
