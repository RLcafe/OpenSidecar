# Resizable widgets: edge grips, smaller minimums and cards that fold

User request, 31 Oct 2026: "make the widgets resizable even more and use impeccable skill to design it". Designed with the Impeccable skill (Operate mode, a refinement of the existing visual world: same palettes, glyphs, type and touch sizes). Builds on [`any-screen-size.md`](any-screen-size.md) §6 and [`organize-widgets.md`](organize-widgets.md). Same on both apps.

## 1. Eight grips

- Edit layout keeps the four 48 × 48 **corner grips** (16 × 16 accent squares) and adds four **edge grips** at the middle of each side: a 28 × 8 accent bar (8 × 28 on the left and right sides) in the same 48 × 48 hit area, with the same 2-unit page-coloured border. A corner resizes both ways; an edge resizes only its side (`N`, `S`, `W`, `E` in `WidgetLayout.Transform` and `Align`, which already worked side by side).
- An edge grip shows only on a side of at least **168 units** (`LayoutEditor.EdgeGripSide`, `WidgetCanvas.EDGE_GRIP_SIDE`), so it never crowds the corner grips on small cards.
- Windows cursors: up-down and left-right arrows for edges. Automation names: "Resize Focus timer top edge". The phone's touch picks a corner first, then an edge within 24 units of a side's middle.
- Snapping, guides, undo, arrow keys (Shift+arrows still grow from the bottom-right corner) and the phone's nudges are unchanged.

## 2. Smaller minimums

The smallest size of each card is now where it still keeps its main reading and main controls at full touch size. Preferred sizes (used by Arrange) didn't change, so the default layouts and every saved layout look the same.

| Widget | Before | Now |
| --- | --- | --- |
| Focus timer | 296 × 300 | 280 × 192 |
| Now playing | 360 × 310 | 312 × 200 |
| Quick launch | 420 × 190 | 200 × 152, then 288 × 204 for six buttons (v0.16.0) |
| Sound mixer | 380 × 420 | 320 × 224 |
| System stats | 380 × 310 | 260 × 280 |
| Weather | 360 × 220 | 232 × 152 |
| Spotify | 560 × 500 | 360 × 360 |
| Soundboard | 360 × 240 | 240 × 152, then 300 × 240 for six buttons (v0.16.0) |

On compact canvases these still shrink to 78% at most (`WidgetLayout.Compact`), except Quick Launch and the Soundboard, which keep their full minimum so six buttons fit (`WidgetLayout.KeepsSix`).

## 3. Cards that fold

Thresholds live in `CardSizing` (`src/widgets/CardSizing.cs`, `android/.../core/CardSizing.java`, the same numbers). The Windows side applies them in `DashboardController.Cards.cs`; the phone's cards read them when they're built for their size. The rule: a card first rearranges, then gives up secondary rows, and never its main reading or main action. Nothing is clipped, and buttons stay at least 48 units.

- **Focus**, under 300 tall: the four presets fold into one **length button** beside Start ("25m"; a tap steps 25 → 50 → 5 → the chosen fourth length, and it's off while a session runs). Start reads "Start", and the status and today's sessions lines go; the countdown fills what's left (40 at the smallest).
- **Now playing**, under 290 tall: the seek row and the hint row fold away, and the cover shrinks to the title row (44 to 104). Under 400 wide the buttons draw closer (4-unit gaps, a 56-wide play button), always centred clear of the volume button.
- **Sound mixer**: *Full* at 420 tall and up (as before). *Compact* from 300: the microphone becomes a button beside the master slider, the level meter and the APPS heading fold away (the meter stops running), and the app list scrolls. *Master* under 300: the master volume and microphone, with an **Apps · 3** button in the header that swaps the master rows for the app list in the same card ("Done" brings them back). The Windows sound settings button gives its room to the slider on compact cards under 400 wide (Windows only; the phone never had it).
- **Quick launch**: six buttons a page at any size since v0.16.0 (see "Six buttons a page" below); before that the grid went down to one button with paging. Under 342 wide the Edit button goes (Settings → Quick Launch still opens the deck; Windows only, the phone has none), and a paged card under 300 wide keeps its pager over its heading.
- **System stats**, under 380 wide: rows drop the CPU graph and the GPU and RAM bars (248-unit rows instead of 324), so more fit and the numbers stay large. Network rates use 17 instead of 21.
- **Weather**, under 200 tall: the details line goes; the temperature, city, condition and the Open-Meteo credit stay.
- **Spotify**, under 500 tall: a smaller now-playing strip (48 cover), and the status row only while it has something to say.
- **Empty states** ("Connect Spotify", "Connect Discord") give way by the room under the heading: the icon first (under 268), then the sentence (under 156), then the heading (under 100); the button always stays. The phone's Discord empty state has no button, so it keeps the heading.
- **Soundboard**: the channel name needs 360 units, and a paged card under 300 wide keeps the pager over its heading.

## 4. Checks and renders

- Windows: `tests\Layout\OrganizeTests.cs` "Edge grips resize one side only…" (edges, minimums, fold thresholds, reading sizes); `tests\SmokeTests.cs` "Smallest free widgets keep essential content and touch controls in bounds" now also checks that every shown button in the smallest Focus, Now Playing, Mixer, Quick Launch and Weather cards is at least 48 × 48 and inside its card.
- Android: `AndroidTests` "Edge grips resize one side only and cards fold like the PC" (same numbers as Windows).
- Renders at 1920 × 720 of the smallest cards and of a middle size, in and out of Edit layout; `tests\screen-size-renders.ps1` shows the default layouts unchanged.

## Not verified

- Edge grips with a real finger on the touchscreen, and the phone's cards at small sizes on a device or emulator (none was available; the Android views build and their thresholds are checked on the JVM).
- The Spotify card's compact strip with a connected account (no live account in the renders).

## Six buttons a page (user request, 31 Oct 2026, v0.16.0)

The user asked for Quick Launch and the Discord Soundboard to always show six buttons, whatever the card's size, instead of fewer buttons with more pages on small cards and more buttons on big ones. Both apps now use the same rule (`CardSizing.PageGrid`, `pageGrid`):

- A page holds six buttons (`PageButtons`). With fewer than six buttons or sounds, the grid holds just those, so they grow into the card.
- The grid is 3 × 2, 2 × 3, 6 × 1 or 1 × 6: whichever keeps every button at least 48 units and gives cells closest to the usual button shape (124 × 76 for Quick Launch, 156 × 64 for the Soundboard). A grid never leaves a whole row or column empty.
- Buttons grow with the card. A Quick Launch label sits under its glyph in cells 76 tall or more, beside it in shorter cells 112 wide or more, and otherwise the button shows its glyph alone, with the label in its tooltip and spoken name (`CardSizing.DeckLabel`).
- Minimum sizes went up to fit six 48-unit buttons: Quick Launch 288 × 204 and the Soundboard 300 × 240. On compact canvases these two don't shrink (`WidgetLayout.KeepsSix`), so a saved layout with a smaller card grows to the new minimum when it loads.
- The phone's cards use the same grid; its Soundboard rows now stretch to fill the card like the Windows `UniformGrid`.
