# 021 — Sticker Sheet: widget inks, label strips and status stickers

Ali approved the "Widget redesign: dense, characterful widgets" doc on 1 Nov 2026 ("I like it. Proceed.") and chose the **Sticker Sheet** direction with four new inks. The redesign ships in five phases, one MINOR release each. This record covers phase 1, the foundations every card shares (branch `feature/sticker-foundations`). The design note is [docs/design/sticker-sheet.md](../design/sticker-sheet.md), and the world it describes is in [DESIGN.md](../../DESIGN.md). The Claude Code card is out of scope everywhere; it keeps its own design (decision 017).

## Decision

- **Eight inks.** A widget wears one of eight ink colours. Lime, Glacier, Lavender and Amber are the existing accents. Coral `#FF9E85`, Mint `#7FE6C0`, Rose `#FFA6CF` and Lemon `#F0E36E` are widget inks only. Every ink passes 9:1 with the dark text `#11170E`. The names match on both apps: the Windows resources are `InkLime`…`InkLemon` (plus `InkText` and `InkOf<widget id>`), and Android has `Theme.INK_*` with `Theme.ink(name)`.
- **Default owners.**
  - Lime: Sound mixer and Games.
  - Glacier: Quick Launch, Clipboard, Weather, World clocks and Smart home. The last three take their colour from live data in phase 3 and 5.
  - Lavender: Soundboard and Storage.
  - Coral: Focus and Calendar.
  - Mint: Spotify and Prayer.
  - Rose: Now Playing and Macros.
  - Lemon: System stats and Notes.
  - Amber means a warning, so it's never a default owner and Edit layout doesn't offer it.
- **Per-widget ink** is chosen in Edit layout's list on both apps ("Ink for …", seven swatches at the top, 48 units each). It previews at once. Save layout keeps it and Cancel drops it. Inks belong to the widget, not to a page or screen shape. The PC keeps only changed inks, in `AppSettings.WidgetInks` ("Focus=Lemon;Media=Mint").
- **Settings → Theme → Widget colours:** Each its own (default) or Accent only (`AppSettings.WidgetColours`, "Each" or "Accent"). Accent only maps every ink to the chosen system accent, which is today's look plus the new chrome.
- **Card anatomy in phase 1:**
  - The **label strip** replaces the grey uppercase title on every card but Claude Code. It's a 28-unit pill in the widget's ink with the name in Segoe UI Black 13 (Android sans-serif-black), letter-spaced 0.09 em, in dark text. The text follows the text size up to 140% and stays inside the pill. Under 200 units wide, it folds to a 12-unit ink dot and a short name.
  - The **status sticker** replaces the right-hand caption on cards that had one. These are Now Playing's source, the mixer, System stats (status and uptime), Storage, Prayer, Notes, Clipboard, Calendar, Smart home, World clocks and Weather's condition. It's an outlined pill in the ink: 1.5-unit border, 13 bold (the caption floor, not the brief's 12.5). It's filled for an active state, such as a prayer starting now.
  - The card bodies are unchanged in this phase.
- **State language:**
  - A **warning** is an Amber sticker with caution stripes and a warning glyph, never colour alone.
  - **Unavailable or not set up** is a dashed outline with no fill.
  - **Offline or stale** (OFFLINE, STALE, PC OFFLINE) is a stamp: a 2-unit outline in the muted colour around the words. The last values dim to 60% (`Stickers.Stale`).
  - **Loading** is a static outline with "…". There are no spinners.
  - The state comes from the caption's words (`Inks.StateOf` and `Inks.stateOf`, the same rules on both apps). Cards whose caption only speaks up for problems (mixer, stats, storage, calendar, smart home) are warnings. Clipboard's PAUSED is a warning too.
- **Shared parts for the later phases:**
  - fact-rail chips (Raised, radius 10, a 13-unit muted letter-spaced caption over a 17-unit semibold value);
  - die-cut sticker shapes (`StickerShape`): a 3-unit keyline in the Text colour over a solid lift 4 units lower in the Page colour, tilting 3° at most;
  - Windows: `src/widgets/Stickers.cs` beside `CardGraphics.cs`. Android: `views/Stickers.java`, with the platform-free rules in `core/Inks.java`.
- **Phone link.** The layout section carries `inks` (each widget's chosen ink) and `colours` ("Each" or "Accent"), so they count in its version hash. `layout.save` accepts an optional `inks` object, and a new `layout.inks` command action sets inks or Widget colours alone. The phone uses it from its Theme settings. It's the same port and endpoint, the same authentication and no new polling. The phone repaints when a mirrored layout brings new inks.

## Rationale

- **Colour as identity:** with nine or more widgets across pages, an ink per widget makes a card findable at a glance. The strip carries the colour, so the bodies can stay calm.
- **Each ink sits on dark surfaces with dark text,** so contrast is fixed and every palette works with no per-theme variants.
- **Amber stays the warning colour.** Warnings use stripes and a glyph as well, so they never rely on hue.
- **Accent only keeps an escape hatch** for anyone who preferred the single-accent look.
- **The sticker reads state from its caption's words.** The cards keep their existing text, names and tests, and both apps get the same looks from the same strings.

## Tradeoffs and constraints

- **Performance.** There are no new timers and no animation. Brushes and geometries are frozen and cached on Windows. On Android, paints, paths and shaders are made once and `onDraw` allocates nothing. There are no shadows or blurs: a check fails if any element on the dashboard has an `Effect`.
- **WPF has no letter spacing on `TextBlock`.** `SpacedText` lays out each character once with `FormattedText` and draws it, ending in "…" when it doesn't fit.
- **On narrow cards** the strip keeps its words first. A status sticker with under 40 units of room (64 for a warning, which carries stripes and a glyph) steps aside rather than poke out of the card, on both apps. The 240-unit Calendar and World clocks checks hit this case.
- **Small Weather has no heading,** as before, so it has no strip.
- **The sticker text is 13 bold, not 12.5,** so the 13-unit caption floor holds.
- **Weather, World clocks and Smart home** use Glacier until their live inks arrive (phases 3 and 5).
- **Not verified on hardware:** the phone views (strips, stickers, the ink picker and the Theme choice) were checked on the JVM and by building the APK, not on a device or emulator. Physical touch on the PC's touchscreen is still unverified, as before.

## Phase 2: the default five

The insides of Focus, Now Playing, Quick Launch, the mixer and Spotify, on both apps (branch `feature/sticker-defaults`). The details are in "What phase 2 built" in `docs/design/sticker-sheet.md`. The choices behind them:

- **Data the apps already had is preferred.**
  - Focus's Today chip reuses `FocusMinutesToday`.
  - Spotify's captions come from fields in replies already fetched (`total_tracks`, `release_date`, `owner`, `tracks.total`). There are no new requests.
  - The album comes from the same Windows media session read.
- **The cover's colour is decoded once, to one pixel,** when the cover changes (the decoder averages as it scales). The PC sends it to the phone as `tint`, which is cheaper than the phone decoding its own copy.
- **The mixer's channel strips replace the app list on Large cards** (the existing Full mode, at least 420 tall). Apps opens the full list, as it already did on Master cards, so every app stays reachable. The strip count is `CardSizing.MixerStripSlots`, the same on both apps.
- **Upright sliders keep the 48 × 48 thumb and touch capture.** They're a second slider style on Windows (the touch helper reads y when vertical) and a `vertical()` option on the phone's `TouchSlider`.
- **The level meter becomes LED segments.** It runs on Medium cards too, on the same level timer (still off in Quiet mode, while hidden, and with Show live output levels off).
- **The phone has no level meter or ladder.** The link doesn't carry output levels, and sending them would mean polling several times a second. The Large phone card shows the strips without the ladder.
- **ON AIR is a status sticker with a glyph.** A glyph sticker needs 62 units of room, or it steps aside, so small cards keep just the microphone button.
- **`--render-no-media`** renders Now Playing's empty state on a PC that's playing something. It shows an absence, not a made-up reading, so it needs no label.

## Phase 3: the readings

System stats, Weather, Storage, World clocks and Prayer, on both apps (branch `feature/sticker-readings`). The details are in "What phase 3 built" in `docs/design/sticker-sheet.md`. The choices behind them:

- **No new requests or timers.** Weather's extras (humidity, rain chance, UV, sunrise, sunset) ride on the one Open-Meteo request the card already makes. Min and max come from the history System stats already keeps. The CPU's name is read once from the registry. Clock faces and bands redraw when a minute turns, on the existing heartbeat.
- **Shared rules live beside the data,** not in the drawing: `SystemReadings`, `WeatherService`, `WorldClockService`, `PrayerService` and `CardSizing` on Windows, and `SystemText`, `Weather`, `WorldClockText`, `PrayerText` and `CardSizing` in the phone's `core/`. The checks on both apps assert the same values, because the JVM checks can't build views.
- **Weather takes its ink from the sky** for its band and glyph sticker (Lemon, Glacier or Lavender), while the label strip keeps the widget's ink, which the user can change in Edit layout.
- **Day and Evening on World clocks follow the clock, not the sun** (7 AM to 7 PM), as the brief asked: they answer "is it a reasonable hour to call?", which sunlight doesn't.
- **The Prayer sun path is placed by time between Fajr and Isha,** so its spacing shows how the day is shared out; the times themselves stay in the list or tiles.
- **Storage jars replace the rings and bars** on Medium and Large. Small keeps its thin bar, which fills with caution tape like the jars when a drive is low.
- **Room decides what shows.** Medium World clocks need a 150-unit body for faces, Large Prayer a 301-unit body for chips, Medium Prayer 44 units under the countdown for the sun path, and Weather's sun arc 380 units of body width. Below those, the card keeps its earlier layout rather than crowd or clip, at 100% and 140% text.
- **Not set up is a dashed outline** (Weather and Prayer without a city, World clocks without zones, Storage without drives, Weather unreachable with nothing to show), following the state language from phase 1.
- **"Used this week" on Storage stays deferred.** It would need the app to keep a history of drive sizes, which it doesn't.
- **Not verified on hardware:** the phone views were checked on the JVM and by building the APK, not on a device or emulator.
