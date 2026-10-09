# Quick Launch verification

Automated checks in `DeckTests.cs` cover old settings migration, empty and large collections, order and metadata persistence, type-specific validation, missing resources, confirmation selection, hotkey parsing, add/remove/reorder/save/cancel, failed-save recovery, paging and 115% text/touch geometry. They do not launch user apps, send keys or modify playback.

Render with `tests/render-checks.ps1 -AppDirectory artifacts/deck -Only dashboard,shortcut-settings,deck-pages,weather-settings`. Captures use isolated settings at 1920 × 720. Inspect the editor and default/paged card for clipping and consistent styling.

On the actual touchscreen:

1. Tap Quick Launch → Edit. Add Calculator through its preset. Add a second application through Start menu filtering, and a third through Browse. Choose a new icon and name, reorder with the arrows, save, and restart. Verify the order and assignments persist.
2. Add more than six buttons and page forward/back. Resize Quick Launch to minimum height: it should use three buttons per page, without shrinking touch controls. Remove all buttons, save, and confirm the empty state can reopen the editor. Cancel a removal and confirm no change.
3. Add a harmless file, a folder and a website. Check each opens its assigned target. Rename the file or disconnect its drive; tapping it should explain the error, and saving an unrelated setting should still work.
4. With a disposable text editor document active, configure `Ctrl+A`. Return to the editor, then tap the deck button: focus should return there and select its text. Test a harmless global shortcut such as `Win+Shift+S`. Cancel the screenshot without saving. Try an elevated app and confirm failure is clear when Windows blocks input. Verify on the real secondary monitor while a game is running; do not assume exclusive fullscreen compatibility.
5. Enable confirmation on a harmless website action. Cancel and verify nothing opens. Confirm and verify it opens once. Configure a harmless executable with arguments and verify the exact target/arguments appear in confirmation. Check Alt+F4/Delete confirmation only with disposable content; no destructive action is needed for testing.
6. Check playback and volume actions with user approval, including when Sound Mixer is hidden. Disconnect audio or stop the media app; the dashboard should remain usable and report unavailable integration state.
7. Try long names, 115% text and every palette. Scroll the button list and form using touch. Check Settings Save/Cancel remain reachable with all action types and expanded arguments.
