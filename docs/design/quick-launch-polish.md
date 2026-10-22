# Quick Launch polish (22 Oct 2026, from the app audit)

Four small things on top of the deck in decision 002, on both apps.

- **Colour per button.** `Shortcut.Color` is empty (the theme's accent) or one of Lime, Glacier, Lavender and Amber; anything else is cleared on load. Settings → Quick Launch has an *Icon color* dropdown beside *Icon*. The glyph takes the colour on the PC (`DeckActions.AccentHex`) and on the phone (`Theme.accentColor`); the button chrome is unchanged.
- **Control buttons show their state.** Mute, Microphone, Focus and Quiet mode buttons (`DeckActions.IsStateful`) take the same selected look as the Focus presets while the thing they toggle is on (`DashboardController.DeckStateOn`, refreshed with the mixer reading, the focus tick and Quiet mode). The deck section of `/state` carries `on`, so the phone shows it too; their automation names end in ", on".
- **Drag to reorder.** In Settings → Quick Launch the list reorders by dragging a row up or down, mouse or finger (`DeckEditor.AttachReorder`); the list captures the pointer and rows swap as the pointer crosses them, so a drag never counts as a tap. The up and down buttons stay for keyboard use. Buttons are edited only on the PC, so the phone has nothing here.
- **A hotkey names its target.** `DeckHotkeys.TargetName` is the process name of the app a hotkey would go to; a hotkey button's tooltip reads "Win+Shift+S → chrome" when it opens (`DeckActions.DescribeWithTarget`), and the phone's confirmation and description carry the same text from `/state`.
