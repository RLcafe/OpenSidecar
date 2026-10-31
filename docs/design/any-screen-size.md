# Any screen size, mirroring, pages, the list editor and cards that size themselves

User requests, 21 Oct 2026:

- "The 1920x720 is the PC secondary screen, but update the code to make it more universal and not locked to this res. The Android device can be a tablet or phone, so think of a way to accommodate both. Same design language but in a way that works for phone."
- "I want the phone to mirror the widget layout on PC" and "Create the ability to have multiple pages to show a different layout."
- "I like how you made editing the layout happen in the phone app, with a list and check mark. Do the same on the Windows app." and "Make the quick launch dynamic in sizing similar to what you did to the sound board. Actually make all widgets size dynamically."

Decision record: [`docs/decisions/010-any-screen-size.md`](../decisions/010-any-screen-size.md). Renders: `.worktrees\any-screen\artifacts\screen-sizes\` from `tests\screen-size-renders.ps1`.

## 1. The screen fit

Both apps use the same rule (`src/app/ScreenFit.cs`, `android/.../core/DashboardFit.java`).

- The dashboard is **designed at the 1920 × 720 scale**: on the owner's screen one design unit is one pixel.
- **Scale** = the largest at which the reference still fits: `min(long side / 1920, short side / 720)`. For a sideways screen that is `min(width / 1920, height / 720)`; an upright screen uses the reference turned upright (720 × 1920).
- **Logical size** = screen size ÷ scale, so one side is 1920 or 720 and the other grows. Extra room becomes canvas, not bigger text.
- **Compact rule** (kept from the phone app, now on Windows too): below 0.75 device units per design unit (Windows pixels, Android dp), the scale may grow up to 0.75 as long as the canvas keeps 80% of the reference canvas each way. Windows measures in pixels, so the owner's 1920 × 720 screen gets exactly today's dashboard at any Windows display scaling.
- **Chrome**: 32-unit side margins, 20 top, 16 bottom, a 72-unit header and a 36-unit footer. Narrow dashboards stack the header (136 units: clock, then the buttons or edit toolbar on a second row). Windows stacks below a logical width of 1800 (its edit toolbar is wide), Android below 1400.
- **Widget canvas** = logical size minus chrome, rounded down to the 8-unit snap grid. The reference canvas is 1856 × 576.

| Screen | Scale | Logical | Canvas |
| --- | --- | --- | --- |
| 1920 × 720 (owner) | 1 | 1920 × 720 | 1856 × 576 |
| 2560 × 720 | 1 | 2560 × 720 | 2496 × 576 |
| 1920 × 1080 | 1 | 1920 × 1080 | 1856 × 936 |
| 3840 × 2160 | 2 | 1920 × 1080 | 1856 × 936 |
| 1280 × 800 | 0.75 | 1706 × 1066 | 1640 × 856 (stacked) |
| 1024 × 600 | 0.661 | 1548 × 907 | 1480 × 696 (stacked) |
| Phone sideways, 2400 × 1080 at 2.625 | 1.55 px | 1548 × 696 | 1480 × 552 |
| Tablet upright, 1600 × 2560 at 2 | 1.5 px | 1066 × 1706 | 1000 × 1496 (stacked) |

On Windows the root grid takes the logical size and the `Viewbox` still scales it uniformly, so **the PC dashboard never scrolls**. The size comes from the second screen in fullscreen and also in the preview window (so the preview shows what the second screen will), from the preview window when no second screen is connected, and from `--render-size WxH` in renders. Settings shrinks to fit narrow logical sizes (at most 1440 × 672).

## 2. Layouts per canvas shape

- Every layout is saved for one canvas size. The 1856 × 576 layout stays in `AppSettings.WidgetLayout`, where it always was, so old settings files load unchanged and older versions still read it. Other sizes go in `CanvasLayouts`. On the phone, `Layout.Focus` keys stay and others are `Layout.1480x552.Focus`.
- **Minimum sizes** shrink with compact canvases (to 78% at most: 80% less the snap-grid rounding), so a stretched layout keeps its proportions as the old stretched phone canvas did.
- **Choosing the layout for a canvas**: the one saved for its size; otherwise the saved layout of the closest aspect ratio that stretches to it without squeezing a shown widget under its minimum; otherwise the page's shown widgets arranged afresh.
- **Arranging** (`WidgetLayout.Arrange`, the same in both apps): the shown widgets, in reading order, go into columns of stacked cards or rows of cards side by side. Every split is tried, and the one that fits and keeps each card closest to its preferred size wins (growing counts half as much as shrinking). With nothing saved, a 1920 × 720 screen still gets exactly the old default, and other shapes get the default five widgets arranged this way (columns on wide screens, three columns or two rows on 16:9 and 16:10, rows on upright screens).
- Edit layout, snap, undo, Reset, Save and Cancel work on the current canvas and save to its entry. A screen change during an edit discards it, with a message.

## 3. Pages

- A dashboard has **up to eight named pages**. Each page has its own layouts per canvas shape, and its own shown widgets. A widget can be on several pages. Widgets on pages not shown are hidden, so they don't poll.
- **Shown widgets belong to the page, not the shape**: saving a layout for one shape carries its shown and hidden widgets to the page's other shapes. A page shows the same widgets on every screen.
- The first page stays in the old fields (`PageName`, `WidgetLayout`, `CanvasLayouts`). The others are in `MorePages`, and `CurrentPage` is the page shown. Old settings become one page named Main.
- **Switching**: the header shows the page switcher (previous, the page's name and "2 of 3", next) when there's more than one page, and a quick sideways swipe of 160 units on the dashboard switches too. Sliders, lists and text boxes keep their own drags. Pages don't switch during Edit layout.
- **The current page is shared**: the phone shows the page the PC shows, and switching on either switches both. Sharing was simple to do, since the PC already owns the layout, and it keeps one dashboard in two places.
- **Managing pages** happens in Edit layout's list (below), the same on both apps: rename the page shown, open, move up or down, delete (tap twice, the last page can't go), and Add page (the default widgets). Page actions save the layout changes first and keep the editor open on the page shown.

## 4. The phone mirrors the PC

- The PC's layouts are the source of truth. `GET /state` always carries a `layoutVersion`, and the phone asks for the `layout` part only when that version changes. The part holds the page shown, the page names, snap, whether the PC is editing, the PC's canvas, the PC screen's own layout, and the current page's saved layouts (`DashboardController.Pages.cs`, `PcState.Layout`). Nothing else on the link changed. The new commands use the same paired, token-protected `/command`.
- The phone stretches the PC layout to its own canvas with the same closest-shape rule; the PC screen's layout counts as a source even when the PC hasn't saved that size.
- **Upright phones** keep the scrolling list, ordered by the PC screen's layout in reading order and showing only the cards shown on the PC.
- **Upright tablets** (smallest width at least 600 dp) now get the dashboard on a tall canvas. Settings → Display keeps Automatic, Dashboard and List, and Keep the app sideways.
- **Edit layout on the phone edits the PC's layout** through the link (`layout.save` for the phone's canvas size on the PC's page). It needs the PC online and not editing itself. Settings → Widgets on the phone also saves to the PC. The last layout the PC sent is kept in the phone's settings, so the dashboard keeps its arrangement while the PC is away.
- `page.show`, `layout.save`, `page.add`, `page.rename`, `page.move` and `page.delete` check the page's name, so a page that changed on the PC in the meantime is refused, not overwritten.

## 5. The list editor on Windows

Edit layout's toolbar keeps Undo, Redo, Reset layout and Snap. The widget dropdown and Show widget box give way to a **Widgets** button that opens the phone's list: a 52-unit check mark row per widget (checking shows and selects it, and the selected widget's row is highlighted), then the pages. Tapping outside, Escape or the button closes it. Dragging, the 48 × 48 corner grips, arrow keys, Save and Cancel are unchanged.

## 6. Cards that size their content

The shared rules are in `src/widgets/CardSizing.cs` and `android/.../core/CardSizing.java`, with the same numbers.

- **Quick Launch** works like the Soundboard: six buttons a page at any card size since v0.16.0 (3 × 2, 2 × 3, 6 × 1 or 1 × 6, see `resizable-widgets.md`), with paging beyond that. Fewer than six buttons use the smallest grid that holds them, so the buttons grow into the card. Glyphs grow with the buttons (24 to 44). The reference card still shows 3 × 2.
- **Focus**: the countdown follows the card's width and free height (42 to 160; 84 on the reference column).
- **Now Playing**: the cover grows with free height and width (104 to 220).
- **Weather**: the temperature follows the card (40 to 96; 54 on the reference card).
- **Spotify**: rows side by side once the card fits two 340-unit rows (up to three).
- **Sound mixer** and **System stats** already filled their cards (a scrolling list of app rows, and the stats Viewbox with 1–3 columns).
- Text never goes below the 13-unit caption, and buttons keep 48 units (52 in the editor's list). Big readings use Ideal text formatting on Windows, because Display formatting dropped the large countdown when the dashboard was scaled below 0.75.

## Not verified

- Real screens other than 1920 × 720, Windows display scaling other than 100%, a portrait second monitor and monitor hot-plug with a different size were only checked through renders and checks.
- The Android views (tall tablet dashboard, page switcher, swipe, the list's Pages section, card sizing) build and their core logic is checked on the JVM, but no emulator or device was available, so they haven't been seen running.
- Touch swipes on the PC are wired to WPF touch events but weren't tried on a touchscreen.


## Readable text on small screens (22 Oct 2026, from the app audit)

- **Text size** can go to **140%** on both apps (it stopped at 115%). Above 115% some cards show less at once; the Settings note says so.
- **Windows display scaling counts on small screens.** `DashboardController.Screen.FitScreen` passes the window's DPI scale as `density` to `ScreenFit.For`, but only when the screen is smaller than the 1920 × 720 reference (`full < 1`); a screen at least the reference size, like the owner's, is never changed by scaling. With `density > 1` the compact floor rises to `density × 0.75` device pixels per design unit, and the canvas may give way down to `max(0.6, 0.8 / density)` of the reference (`ScreenFit.For(width, height, density, stackBelow, minCanvas)`). A 1024 × 600 panel at 150% scaling, for example, goes from 0.66 to about 0.87 pixels per design unit.
- **The phone keeps its rule**: `DashboardFit` already uses the dp density for the floor and keeps the 80% canvas, so phone layouts (saved per canvas size) don't move.
