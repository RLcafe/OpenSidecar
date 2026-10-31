# Organizing widgets: snapping and Arrange

User request, 31 Oct 2026: make widgets easy to organize, with an action that arranges them neatly and snapping to other widgets while moving or resizing. Both apps, the same rules (`WidgetLayout.cs` and `core/WidgetLayout.java`).

## Snapping to other widgets

With **Snap** on (the default, as before), a drag no longer only rounds to the 8-unit grid. Each edge that moves looks for a target within **12 canvas units** (`AlignDistance`, `ALIGN_DISTANCE`):

- A left or top edge: the canvas edge, another shown widget's left or top edge, or one gap (16 units, the same gap the default arrangement uses) after its right or bottom edge.
- A right or bottom edge: the canvas edge, another widget's right or bottom edge, or one gap before its left or top edge.
- While moving (not resizing), the card's centre: another widget's centre.

The nearest target wins, per axis. An axis with no target in reach follows the 8-unit grid as before, so free placement still lands on the grid. Resizing never goes under a card's minimum size, even when a target is closer. Only shown widgets count, and hidden ones never pull.

While a finger or the mouse holds a card, every line the card now sits on is drawn as a 2-unit accent **guide** above the cards: shared edges and centres as a line across both cards, and a one-gap fit as a line in the middle of the gap. Canvas edges don't get a guide. Letting go clears them. Snap off moves freely with no guides, as before. Arrow keys (Windows) and the Move and Size nudges (phone) are unchanged: they move by exact steps and don't snap to widgets.

## Arrange

An **Arrange** button in the Edit layout toolbar, next to Reset layout. It lines up the shown widgets neatly, filling the canvas without gaps or overlaps, and keeps the order they are in now:

- Their order is read column by column on a wide canvas (left to right, each column top to bottom), or row by row on an upright one. A widget whose centre lies inside the first widget's span of the current column joins that column (`ArrangeOrder`, `arrangeOrder`).
- That order goes through the same arrangement as new screen shapes (`Arrange`, decision 010): columns of stacked cards or rows of cards side by side, picking the split that keeps every card closest to its preferred size.
- Hidden widgets keep their saved places. Stacking layers reset, since nothing overlaps any more. If the shown widgets can't all fit at their minimum sizes, they stay on the canvas and the status line says they overlap.
- It is one undo step, and nothing is saved until Save layout. When the widgets are already arranged that way, it says so and changes nothing.

Reset layout is unchanged: it goes back to the default widgets and places. Arrange keeps your widgets and only tidies them.

## Phone

The phone's Edit layout has the same Snap behaviour, guides (drawn by `WidgetCanvas.dispatchDraw`) and Arrange button. The phone edits the PC's layout on the PC's canvas size and saves it to the PC as before, so no change to the phone link was needed. The Android checks cover the shared snapping and arranging rules; the toolbar and guides on a real phone were not checked, since no device or emulator was attached.
