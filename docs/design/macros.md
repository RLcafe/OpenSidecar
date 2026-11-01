# Macros

User request, 1 Nov 2026 (Windows and the phone). A Macros card is a Stream Deck–style grid of buttons, each running a chain of steps. It is hidden by default; turn it on in Edit layout's Widgets list. With no macros it shows "Add a macro", which opens Settings → Macros.

## The card

The same machinery as Quick Launch (`CardSizing.DeckGrid`, `DeckLabel`, `DeckGlyph`, `Tile`): six buttons a page in whichever grid suits the card, buttons that grow with it, glyph-only cells with the name in the tooltip, tiles on large cells that say "3 steps · asks first", a pager and an Edit button. Its minimum size is Quick Launch's (288 × 204), kept on compact canvases (`WidgetLayout.KeepsSix`). The running macro's button takes the selected look and its spoken name says which step it's on. Every button's tooltip lists its steps.

## Steps (`MacroStep`, `MacroActions`)

| Step | What it does |
| --- | --- |
| Keyboard shortcut | Sent to the app the user was in, through `DeckHotkeys` like a Quick Launch hotkey. |
| Open an app, a file, a folder or a website | `DeckActions.Open`, with the same checks as Quick Launch. |
| Dashboard / media control | Play/pause, previous, next, mute, microphone, volume up/down, Focus, Quiet mode, sound settings (`RunControl`, shared with Quick Launch). |
| Wait | 0.1 to 30 seconds (the editor's slider goes to 10). |
| Run a script | A `.ps1` (through `powershell.exe -NoProfile -ExecutionPolicy Bypass -File`, so it runs instead of opening in Notepad), `.bat`/`.cmd` (through `cmd.exe`) or `.vbs` (through `wscript.exe`), with optional arguments. It starts and the macro goes on without waiting; add a Wait after it if the next step depends on it. |

Up to 60 macros of up to 12 steps (`AppSettings.Macros`, saved in `settings.xml` with the other settings).

**Running.** Steps run in order on the dashboard's thread, awaiting each one. One macro runs at a time; tapping another says the first is still running. A step that fails stops the rest, and the status line names the step: "Stream start stopped at step 3 (Hotkey · Ctrl+Shift+M): Select the app you want to control first…". A macro is checked before it starts, so a missing file or broken hotkey stops it before anything runs.

**Asking first.** A macro asks on the confirm sheet, listing every step in words, when "Ask before running this macro" is on, when it has a script, or when any step would ask on a Quick Launch button (arguments, Delete, Alt+F4, script files opened as files). That keeps the rule "confirm consequential actions" and makes every chain inspectable before it runs.

## Settings → Macros

A new tab after Quick Launch (tabs: Theme, Display, Widgets, Weather, Prayer, Spotify, Quick Launch, Macros, Phone, Discord, Claude). `MacroEditor` builds it in code: the macros on the left with + Add, move up, move down and Remove; on the right the selected macro's name, icon, icon colour and Ask first, its steps (each a row in words with move and remove buttons; tapping one loads it to change), and a step editor (the step's kind, then the field that kind needs: keys, a path with Browse…, an address, a control, a wait slider, or arguments). Add step checks the step and says what's wrong. Save changes checks every macro and selects the first one with a problem.

## The phone

The `macros` section sends each macro's label, glyph, colour, whether it asks first and its steps in words, and which macro is running (and its step). `macro.run` (with `index`, `label` and `confirmed`) runs it on the PC; the phone asks first on its own confirm sheet with the same list, and the PC refuses an unconfirmed run of a macro that asks, or a run whose label no longer matches. Macros are edited on the PC only.

## Checks

`tests/Macros/MacroTests.cs`: checking each kind of step, what asks first (scripts, Alt+F4, Ask first), problems naming the step, wording and the confirm sheet's list, cleaning saved macros; saving and copying; the Quick Launch–like layout rules; running a chain of Quiet mode toggles and waits on the dashboard (in order, shown running, a second tap refused, Ask first showing the steps, Cancel not running, Run running); and building a macro step by step in Settings → Macros, refusing a broken step and a macro without steps. The phone-link checks run a macro from the phone, refuse an unconfirmed one that asks and one that changed. `AndroidTests.java` covers the section and wording. The checks never launch apps, run scripts or send keys.

Not tried: hotkey, launch and script steps for real (they act on the user's desktop), and the phone card on a device.
