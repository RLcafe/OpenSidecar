# Notes and to-do

User request, 1 Nov 2026 (Windows and the phone). A Notes card holds a scratchpad and a to-do list. The PC owns them; the phone edits them through the link and mirrors the PC. The card is hidden by default; turn it on in Edit layout's Widgets list.

## Where the notes live

`notes.xml` next to `settings.xml` (`NotesStore`), written atomically the same way (a temporary file, then `File.Replace`). It isn't encrypted: it's ordinary text the user typed, like the settings. A file that can't be read is left untouched until the next change, and the status line says so. Limits: 16,000 characters of text (so a phone's request stays under the link's 64 KB) and 200 to-dos of up to 200 characters each; a to-do is one line (line breaks become spaces). Every change bumps a version number that the phone sees.

## Saving

- The scratchpad saves a second after the last key (on the existing one-second heartbeat), when it loses focus, and when the dashboard closes. Typing in it activates the window so keys reach it (decision 013), like any text box on the dashboard.
- Adding, ticking, deleting and Clear done save at once. Clear done asks first on the confirm sheet ("Clear 2 done items?"); a single delete doesn't.
- A save that fails puts the notes back as they were and says so on the status line, so nothing pretends to be saved.

## Designs (`CardSizing.Notes`, the same numbers on the phone)

| Design | When | Shows |
| --- | --- | --- |
| Small | under 340 wide or 220 tall | Two tabs, Notes and To-do (with the open count), over the scratchpad or the list. |
| Medium | otherwise, below Large | The same tabs with more room. |
| Large | 700 × 300 and up | The scratchpad and the list side by side, under SCRATCHPAD and TO-DO labels; the list column is 46% of the card (300–520). |

The list scrolls inside the card: each to-do is a 48-unit check box row with a delete button (done items dimmed and struck through), then a box and Add button (Enter adds too), then Clear done while anything is done. The heading counts what's left ("3 TO DO").

## The phone

The `notes` section carries the version, the text and each to-do's id, text and done flag. Commands: `notes.text`, `notes.add`, `notes.toggle` (with `done`), `notes.delete`, `notes.clearDone` and `notes.move` (to a position; the PC has it, the phone card doesn't offer reordering yet). The phone sends its typing 1.2 seconds after it stops or when the box loses focus, and keeps showing its own text until the PC has it, so a reading in between never overwrites what was just typed. If the PC's scratchpad is being typed in at that moment, the PC refuses the phone's text and the phone tries again a few seconds later. Last write wins otherwise; there is no merging of simultaneous edits.

## Checks

`tests/Notes/NotesTests.cs`: the store's add, tick, move, delete, Clear done, limits, line cleaning, atomic save, reload and keeping a damaged file; the hidden default and design numbers; and the card at three sizes at 140% text with a labelled fixture, keeping its controls inside the card and at least 48 tall, adding, ticking, deleting and clearing through its own buttons (Cancel on the confirm sheet keeps the items), showing a change made elsewhere, and saving typing when the dashboard closes. The phone-link checks change the text, add, tick and delete from the phone and refuse bad requests. `AndroidTests.java` parses the section and covers the wording and the one-line rule.

Not tried on a phone or emulator: the phone card builds, and its parsing and wording are checked.
