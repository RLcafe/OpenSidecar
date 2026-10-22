# One confirm sheet, first-launch tips, tab order and the tray default (22 Oct 2026)

From the app audit. Four small things that each changed what the user sees.

## The confirm sheet

- Quick Launch buttons that ask first (arguments, scripts, installers, Delete, Alt+F4, or *Ask before running this button*) asked in a Win32 `MessageBox`: unthemed, unscaled by the Viewbox, small buttons, placed wherever Windows chose. They now ask in `ConfirmSheet` (`src/widgets/ConfirmSheet.cs`, `ConfirmOverlay` in the XAML): a card over the dashboard with the label, the button's name, what it does, and 52 px *Cancel* and *Run <name>* buttons. Escape, Cancel or a tap on the scrim answers no. Focus goes to Cancel and returns afterwards.
- The phone had a `Theme_DeviceDefault` `AlertDialog`; it now uses `Dashboard.confirm`, the same sheet in the app's palette, closed by Back, Cancel or a tap outside.
- Page delete keeps its "tap delete again" confirmation: that is one tap in the editor's own list, not a question that needs a sheet.
- `RunDeckActionAsync` awaits the sheet, so a phone's `deck.run` with `confirmed=false` still gets the PC's sheet, as before.

## First-launch tips

- A row in the header, where the page switcher goes once there are pages: Windows says *Swipe sideways to switch pages · Settings → Phone pairs your phone*; the phone says *Swipe sideways to switch pages · Edits here are saved on the PC* (upright list: *Edit layout on the PC or a sideways phone · Settings → PC pairs this phone*). *Got it* sets `HintDismissed` / `hintDismissed` and the row never returns.
- It shows only while there is one page, no edit is open and the header isn't stacked (narrow screens have no room beside the clock).

## Phone before Discord

Settings tabs are now Theme, Display, Widgets, Weather, Spotify, Quick Launch, Phone, Discord. `OpenDiscordSettings` selects index 7. The phone's own Settings are unchanged (Theme, Display, Widgets, PC).

## Tray mode default

`HideToTrayWithPhone` defaults to **off**. Existing settings files keep whatever they had. Decision 009 is updated.
