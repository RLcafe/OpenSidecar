# 010 — Any screen size, mirrored layouts and pages

The user asked (21 Oct 2026) for the dashboard to stop being locked to the 1920 × 720 secondary screen and to work on phones and tablets in both orientations; for the phone to mirror the PC's layout; for layout pages; for the phone's list-and-check-mark editor on Windows; and for every widget to size its content to its card. Design details: [`docs/design/any-screen-size.md`](../design/any-screen-size.md).

## Decision

1. **Same scale, screen-shaped canvas.** Both apps lay the dashboard out at the 1920 × 720 design scale on a logical size shaped like the screen: scale = `min(long side / 1920, short side / 720)`, with the existing compact rule (up to 0.75 device units per design unit, canvas at least 80% of the reference). Windows still uses one `Viewbox` and never scrolls; it measures in pixels so the owner's screen is unchanged at any display scaling. The preview window follows the second screen's shape when one is connected.
2. **Layouts per canvas shape**, keyed by canvas size rounded down to 8. The 1856 × 576 layout keeps its old settings field. A new shape takes the closest-aspect saved layout that stretches without squeezing a shown widget under its minimum, otherwise the page's widgets are arranged afresh by one packing rule shared by both apps (columns of stacks or rows of cards, closest to each card's preferred size).
3. **Pages**: up to eight named pages, each with its own layouts and shown widgets. Shown widgets are per page, the same on every shape. The current page is **shared** between the PC and the phone.
4. **The phone mirrors the PC.** The PC's layouts are the source of truth and travel over the existing paired link (a `layout` part of `/state`, sent only when `layoutVersion` changes, plus `page.*` and `layout.save` commands). The phone stretches them to its canvas, lists the PC's shown cards in reading order when upright, and **saves its own edits to the PC** for its canvas size. Upright tablets get the dashboard.
5. **Windows Edit layout uses the phone's list**: check mark rows for widgets and a Pages section, replacing the widget dropdown and Show widget box.
6. **Cards size their content** with shared `CardSizing` rules: Quick Launch fits its card like the Soundboard (and grows its buttons when they all fit), Focus, Now Playing and Weather grow their main reading, and Spotify adds columns. The reference sizes are unchanged.

## Rationale

- Keeping the design scale, rather than scaling text up on big screens, keeps the touch and text sizes the user already tuned on the real screen, and gives other screens more room instead of bigger buttons.
- Saving per shape means a layout made for the second screen is never squeezed onto a phone, while a phone held sideways still starts from the PC's arrangement.
- Making the PC the owner of every layout (including the phone's shapes) means one place to back up and no merge rules. Sharing the current page follows from that and makes the phone a true second screen.
- Keeping page 1 and the reference layout in the old settings fields lets an older build open the same `settings.xml` and still show the user's layout.

## Tradeoffs

- Arrangements for new shapes are generated, not hand-made. They fit and don't overlap, but the user may want to adjust them once per shape. Edit layout saves that per shape.
- With visibility per page, hiding a widget on the phone hides it on the PC too (same page). Different widgets for different screens need different pages.
- The phone can't edit offline: its edits are saved on the PC. It still shows the last layout the PC sent.
- A screen-size change during an edit discards the edit rather than trying to map it to the new canvas.
- Page actions in the editor save pending layout changes first, so there is no per-page undo across page switches.
- Android views were built and their logic checked on the JVM, but not run on an emulator or device in this change.
