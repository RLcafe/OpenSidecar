# Sticker Sheet widget redesign

Source: the design doc "Widget redesign: dense, characterful widgets", approved by Ali on 1 Nov 2026 ("I like it. Proceed."). He chose **Sticker Sheet** and approved the new inks. Ali explicitly invoked the Impeccable skill for this redesign. The Claude Code widget is out of scope everywhere. The technical record is [decision 021](../decisions/021-sticker-sheet-inks.md), and the world in short is in [DESIGN.md](../../DESIGN.md).

The redesign ships in five phases, each a MINOR release (check `main`'s latest tag before tagging). Steam game artwork, per-app mixer level meters and storage "used this week" history are deferred: they're in no phase yet.

## The world (every widget except Claude Code)

The redesign adds four things only: colour inks, heavier type, denser fact rows and one die-cut sticker per card. These stay the same:

- the grid, buttons, sliders and reading order;
- the `CardSizing` thresholds and minimum and preferred sizes;
- the touch floors (48 and 52);
- the 13-unit caption floor and text up to 140%.

### Inks

Each ink passes 9:1 with the dark text `#11170E` and on every palette surface.

| Ink | Hex | Default owner |
| --- | --- | --- |
| Lime | `#BAF28B` | Sound mixer, Games |
| Glacier | `#8ED8F8` | Quick Launch, Clipboard, and Weather, World clocks and Smart home until their live inks |
| Lavender | `#C5AEFF` | Soundboard, Storage |
| Amber | `#F5C47D` | Warnings only, never a default owner |
| Coral | `#FF9E85` | Focus timer, Calendar |
| Mint | `#7FE6C0` | Spotify, Prayer times |
| Rose | `#FFA6CF` | Now Playing, Macros |
| Lemon | `#F0E36E` | System stats, Notes |

- **Live inks (later phases).** Weather takes its ink from the sky condition, World clocks from each city's day or evening, and Smart home from each light's colour.
- **Changing an ink.** A widget's ink is chosen in Edit layout on both apps. It's stored on the PC and mirrored to the phone.
- **Settings → Theme → Widget colours:** Each its own, or Accent only. Accent only makes every ink the chosen system accent.

### Card anatomy

1. **Label strip.** It replaces the grey uppercase title: a 28-unit pill in the widget's ink with the name in Segoe UI Black 13, letter-spaced about 0.09 em, in dark text `#11170E`. Under 200 units wide, it collapses to a 12-unit ink dot and a short name.
2. **Status sticker** at the top right: one short live fact in an outlined pill (ink border and ink text, 1.5-unit border, 13 bold), or filled (ink fill, dark text) for an active or warning state. It replaces scattered right-aligned captions such as LIVE, EVERY 2 SEC or Up 2d 6h.
3. **Hero sticker:** one die-cut shape per card, a path with a 3-unit light keyline (Text colour) and a solid lift copy offset 4 units down in the Page colour. Never DropShadowEffect, BlurEffect or setShadowLayer. It may tilt up to 3°, but only if it holds at most one short number.
4. **Fact-rail chips:** Raised fill, radius 10, a 13-unit muted caption (uppercase, letter-spaced) over a 17-unit semibold value.
5. **Footline** on Medium and Large: the source and how fresh it is, 13, muted.

### Type

Numerals stay Segoe UI Light. Strips and sticker text use Segoe UI Black, values Semibold and body text Regular. Android uses sans-serif-light, sans-serif-black and sans-serif-medium. Values that update use tabular figures.

### State language

- **Warning:** Amber caution-tape stripes on the affected sticker or row, plus a glyph. Never colour alone.
- **Unavailable or not set up:** a dashed outline, no fill, and the reason in plain words.
- **Offline or stale:** an OFFLINE or STALE stamp in the status sticker, with the last values at 60% opacity.
- **Loading:** an outline with a static "…". No spinners.

### Performance rules

- No new timers: the 1 s heartbeat or data arrival only.
- No continuous animation. Instant press feedback and one-shot 150–350 ms state changes are allowed, all off in Quiet mode.
- Geometries are built once per size class and frozen (`Freeze()` on Windows). On Android, Path and Paint objects are reused and `onDraw` allocates nothing.
- The tree is rebuilt only when the size class changes.
- About 150 elements on Small and Medium cards and 300 on Large.
- Images are decoded at the size they're shown.
- No extra network requests.
- Check: the default layout for 60 s on the previous release against the branch; CPU within 0.5 points, memory within 10%.

## Phases

1. **Foundations** (`feature/sticker-foundations`). See "What phase 1 built" below.
2. **Default five** (`feature/sticker-defaults`):
   - **Focus:** a kitchen-timer dial, the end time, tomato session pips, a Today minutes chip (the sum of finished session lengths) and Next break.
   - **Now Playing:** a sleeve with the disc peeking out, a tint from the cover's average colour (a 1-pixel downscale once per cover), the album and source app from the Windows media session, and Length and Left chips.
   - **Quick Launch:** keycaps with a 4-unit skirt that drop 2 units when pressed, an LED dot for buttons that are on, and on Large a hotkey legend.
   - **Sound mixer:** an ON AIR microphone badge. On Large cards at least 420 tall: vertical channel strips, a master strip with an LED ladder, 88 units per app strip, and the rest behind Apps.
   - **Spotify:** crate tabs; caption chips with song counts, owners and years parsed from the existing replies; on Large, a side panel tinted from the cover.
3. **Readings:**
   - **System stats:** a spec badge for the most strained reading, temperature badges on the rings, min and max from the existing history, and CPU and GPU model names read once.
   - **Weather:** a sky gradient band, a glyph sticker, a 12-hour temperature curve with rain bars, a sunrise–sunset arc, and Humidity, UV and Rain chips. Add `relative_humidity_2m`, `precipitation_probability`, `uv_index_max`, `sunrise` and `sunset` to the same Open-Meteo request.
   - **Storage:** jars, with caution stripes under the threshold.
   - **World clocks:** analog faces, Lemon from 7 AM to 7 PM and Lavender otherwise, labelled day and evening (not sunlight). On Large, 24-hour bands with 9–5 shaded and a now line.
   - **Prayer:** a sun-path arc from Fajr to Isha placed by time, the countdown, and Hijri, Method and Alert chips.
4. **Lists:**
   - **Notes:** a Lemon sticky note with dark text, and the to-do progress.
   - **Clipboard:** receipt slips with the copy time and a type tag.
   - **Macros:** a bead chain; beads fill as steps finish, and a failed bead is striped.
   - **Calendar:** a tear-off date block, a countdown pill, and a day timeline on Large.
5. **The rest:**
   - **Games:** cartridge cells, a store chip, when it was last played, and playtime from Steam only.
   - **Smart home:** bulbs in their reported colour, rocker switches and a thermostat dial.
   - **Soundboard:** sampler pads with a 150 ms flash, and the voice channel status.

A phase is done when:

- `build.ps1 -Test` and `build-android.ps1 -Test` pass;
- Small, Medium, Large and Wide renders of every changed card exist at 1920 × 720, with text at 100% and 140%, in all three palettes;
- failure states are rendered;
- the performance check is within target;
- the phone is checked on the JVM, and on a device or emulator if one is available.

## What phase 1 built

- **Inks.**
  - Windows: `src/app/Inks.cs` (names, colours, defaults, strip names, Widget colours, state words) and `DashboardController.ApplyInks`, which sets `InkLime`…`InkLemon`, `InkText` and `InkOf<id>` as frozen brushes.
  - Android: `core/Inks.java`, plus `Theme.INK_*` and `Theme.ink(name)`.
  - Checks on both apps compare the two through `artifacts\tests\ink-defaults.json`.
- **Label strips.**
  - Windows: `LabelStrip` hosted in the XAML Borders that replaced the title TextBlocks (`FocusStrip`, `MediaStrip`, `DeckHeading`, …). Weather builds its strip in `WeatherCard` (Medium and Large; Small has no heading). Letter spacing comes from `SpacedText`.
  - Android: `Stickers.LabelStrip`, made by `Widget.strip()`.
- **Status stickers.**
  - Windows: `StatusSticker.Wrap` around the existing caption TextBlocks, which keep their names and text. `Filled` is set for Prayer's "now", and `Warns` for Clipboard's PAUSED.
  - Android: `Stickers.Sticker`, a TextView made by `Widget.sticker(warns)`. It ignores a card's `setTextColor`.
- **Shared helpers:** `Stickers.FactChip` / `factChip`, `Unavailable` / `unavailable`, `Stale`, `Caution` (a frozen repeating gradient) / `caution`, `Tilt` (±3°), `Rounded`, `Disc` and `Burst` geometries (cached and frozen), and `StickerShape`.
- **Ink picker.** "Ink for <widget>" sits at the top of Edit layout's list, with seven 48-unit swatches. The widget rows show a dot in their ink. The check sits on the chosen swatch, and Claude Code shows "keeps its own colours".
- **Widget colours** is in Settings → Theme on both apps. The phone sends its change to the PC with `layout.inks`.
- **Renders:** `tests\sticker-renders.ps1` writes `artifacts\sticker-sheet\`: the default layout and two pages with the other widgets in Midnight, Slate and Warm, text at 140%, Accent only, changed inks, narrow cards and the ink picker.
- **Performance check** (1 Nov 2026, 16 logical cores): the default layout, windowed with isolated settings, measured for 60 s after 10 s warm-up. The v0.33.2 release used 0.05% CPU and 123.2 MB private memory; this branch used 0.07% CPU and 122.9 MB. That's within the targets (CPU within 0.5 points, memory within 10%).

## What phase 2 built

Branch `feature/sticker-defaults`. The default five, on both apps unless a line says otherwise:

- **Focus**
  - Large: a kitchen-timer dial (`TimerDial`, `StickerParts.TimerDial`). The time left is a wedge in the ink, sweeping clockwise from a knob at the top, over 60 minute ticks (every fifth longer), with a 3-unit keyline and a solid lift. The countdown and its end time sit in the dial's inner disc (62%).
  - Medium and Large: today's sessions as tomato pips (`PipRow`), filled ones done and dashed ones still to go, out of the day's four and up to eight. Under them, the Today chip (the finished sessions' minutes, already kept as `FocusMinutesToday`) and the Next break chip (the end time while running, otherwise "In 25 min"), when the card has room (`FocusTimer.MinutesText`/`BreakText`, `Format.focusMinutes`/`focusBreak`).
  - The status sticker reads DONE (filled), FOCUSING (filled), PAUSED or "2 TODAY" (`FocusTimer.StateText`, `Format.focusState`).
  - The pips replace the sessions line, which stays as their spoken description.
- **Now Playing**
  - The cover is a sleeve with a 2-unit keyline. While something plays, a record (`DiscPeek`) peeks out of it by 30% of the cover (not on the folded Small card).
  - Nothing playing leaves an empty dashed sleeve.
  - The card's top takes 22% of the cover's average colour, which is decoded once per cover to a single pixel (`MediaService.AverageColour`, `Blend`). Even a white cover keeps the text at 7:1 or better.
  - An album line comes from the Windows media session. The Length and Left chips show when the text column is roomy.
  - The phone link's `media` section adds `album` and `tint` ("#RRGGBB"), so the phone tints the same without decoding the cover again.
- **Quick Launch**
  - Keycaps (`Keycap` style; `Stickers.keycap` on the phone): the cap sits on a 4-unit darker skirt and drops 2 units while pressed.
  - An LED dot in the ink lights for a button that's on.
  - Large tiles show a hotkey's keys as the cap's secondary legend at the top right (`Format.hotkeyLegend` on the phone, from the `kind` the PC already sends).
  - The six-a-page grid is unchanged.
- **Sound mixer**
  - Large (the Full mixer, at least 420 tall) shows channel strips. The master strip comes first (112 units, an LED ladder beside its slider, then mute and the output device). Then one 88-unit strip per app that fits (`CardSizing.MixerStripSlots`), each with a badge in the ink, name, percent, slider, mute and output.
  - Strips use upright sliders that keep the 48 × 48 thumb (`VerticalSlider` style, `TouchSlider` reads y; `TouchSlider.vertical()` on the phone).
  - Apps (now on Large cards too) opens the full list, and Done goes back.
  - The heading shows an ON AIR badge (filled, with the microphone glyph) while the microphone is live, and MUTED while it's muted. A badge with a glyph needs 62 units of room, or it steps aside.
  - Medium and Large cards run the level meter, now LED segments (`LedMeter`; top two segments Amber), on the existing level timer.
  - **Phone:** the phone link doesn't carry output levels, so the phone's mixer has the strips and the badge but no LED meter. Adding one would need polling the PC several times a second.
- **Spotify**
  - Crate tabs (`CrateTab` style): dividers with rounded tops on an ink line, with the chosen one filled in the ink.
  - Each album, playlist or song row adds a caption chip built from the reply already fetched: "11 songs · 2024", "Ali · 24 songs" or a song's year (`SpotifyService.CaptionOf`). The phone gets it as `caption` on each item.
  - The Large side panel takes 30% of the playing song's cover colour.
- **Render flag** `--render-no-media` shows Now Playing's empty state whatever plays on the PC. `tests\sticker-renders.ps1` adds the five cards at Small, Medium, Large and Wide in each palette, at 140%, and with nothing playing.
- **Not rendered:**
  - Spotify's Large design with a library, which needs an account (`SpotifyTests` covers it with fixtures).
  - The mixer with the microphone muted, which would mean muting the real microphone. The badge words are checked.
- **Performance check** (1 Nov 2026): the default layout for 60 s after a 10 s warm-up, windowed with isolated settings.
  - v0.35.1 release: 0.07% CPU, 126.7 MB private memory.
  - This branch: 0.05% CPU, 125.6 MB.

## What phase 3 built

Branch `feature/sticker-readings`. The readings, on both apps unless a line says otherwise. The drawn parts are in `src/widgets/ReadingParts.cs` and `android/.../views/ReadingParts.java`; the rules they follow are in the services and `core/` so both apps check the same numbers.

- **System stats**
  - A peak sticker in the heading names the busiest shown reading of CPU, GPU, RAM and VRAM ("GPU 92%"), filled from 80% (`SystemReadings.PeakId`/`PeakText`, `SystemText.peakId`/`peakText`).
  - CPU and GPU rings and gauges wear their temperature as a badge sticker at the top right ("58°", in the chosen unit).
  - Large tiles add "min 12% · max 87%" under each reading from the 48 readings the card already keeps (`Range`).
  - The CPU and GPU details show short model names ("Ryzen 7 7800X3D", "RTX 4070"; `SystemService.ShortModel`, `SystemText.shortModel`). The CPU name is read once from the registry and sent to the phone as `system.cpuName`.
- **Weather**
  - The reading sits on a sky band, a rounded panel in the sky's ink fading downwards: Lemon for a clear day, Glacier for cloud, rain and snow, Lavender for storms and night (`SkyInk`, `SkyStrength`). Day is between sunrise and sunset in the city, from Open-Meteo's `utc_offset_seconds`.
  - The condition glyph sits on a die-cut disc sticker in the sky's ink, drawn in dark text.
  - Chips: Feels, Humidity and Rain on Medium; Large adds UV ("6 High"), Wind and High / Low as far as they fit (`CardSizing.WeatherChips`, three to six).
  - Large, and Medium from 300 tall, show the next 12 hours as a temperature curve over rain bars (`TemperatureCurve`): the highest and lowest temperatures by their points, a dot at now and a label every three hours. Rain bars show from a 5% chance.
  - Large cards from 380 units of body width show a sunrise–sunset arc (`SunArc`): the day gone in Lemon and the sun at now, or a hollow dot at the end after sunset, with the two times under it.
  - The same single Open-Meteo request now asks for `relative_humidity_2m`, `precipitation_probability`, `uv_index_max`, `sunrise` and `sunset`. A missing extra leaves the rest of the reading intact.
  - Offline keeps the last reading at 60% with OFFLINE in the status sticker and "last update" in the credit. No city, or the service unreachable with nothing to show, puts the reason in the dashed unavailable outline.
- **Storage**
  - Each drive is a lidded jar (`StorageJar`) with a keyline and lift, filled from the bottom by its used space in the Storage ink (`CardSizing.JarLevel`; any use shows at least a sliver).
  - A drive under the low-space threshold fills with Amber caution tape, keeps the warning colour on its free space, and the heading names it. Small's thin bar uses the same tape.
  - Medium puts a small jar beside each drive's name, free space and used of total (the bar is gone). Large gives each drive a big jar labelled with its free percentage on a pill in the card's colour.
  - "Used this week" stays deferred: it needs a history the app doesn't keep.
- **World clocks**
  - Each city gets an analog face (`ClockFace`), Lemon from 7 AM to 7 PM in that city and Lavender otherwise, labelled Day or Evening in the same ink (`WorldClockService.Ink`/`PartOfDay`, `WorldClockText`). It's about the hours people keep, not sunlight.
  - Faces sit beside roomy Small rows, atop Medium columns from a 150-unit body (shorter cards keep the plain columns), on Large tiles and at the left of stacked tiles.
  - Large tiles add a 24-hour band (`DayBand`): midnight to midnight, 9 to 5 shaded in the city's ink, and a line at the city's time now. Stacked tiles get it from 110 units tall.
  - Faces and bands redraw only when a minute turns, as the times do.
- **Prayer**
  - The day's sun path (`PrayerPath`): an arc from Fajr (left) to Isha (right) with each prayer placed by its time (`PrayerService.PathPart`, `PrayerText.pathPart`), sunrise as a short Lemon mark, the day gone in the Prayer ink and the sun at now. Prayers gone are filled, the next one is larger, the rest hollow. After Isha the whole arc is lit and tomorrow's Fajr is the next prayer in the list.
  - Medium shows it under the countdown when there's room. It takes what the text leaves, so larger text shrinks it; without room the progress line stays.
  - Large sets it beside the next prayer and countdown, with Hijri, Method ("Umm al-Qura") and Alert (Off, Chime or Your sound) chips under them from a 301-unit body, then today's times (with sunrise when the list keeps 22 units a row) or tiles across a wide card. The phone link adds `prayer.alert`.
  - No city puts the reason in the dashed unavailable outline. World clocks without zones and Storage without drives do the same.
- **Render flags** `--render-weather-offline` (a real reading, then a failed update, so the card shows it stale) and `--render-weather-unreachable` (the first update fails). They show failure states, never made-up readings. `tests\sticker-renders.ps1` adds the five readings at Small, Medium and Large in each palette, at 140%, with a 20% low-space threshold (so this PC's fuller drives show caution tape), Weather offline and unreachable, and no city set. Hardware sensors aren't installed in the worktree, so CPU temperature shows "Not installed" in every scene.
- **Not rendered:** a system under strain (a filled peak sticker and hot badges need a real load; the 80% rule and badge words are checked), and the clock faces at a fixed hour (they follow the real time; the inks and hands are checked).
- **Performance check** (1 Nov 2026): 60 s after a 10 s warm-up, windowed with isolated settings, on the default layout and on the medium readings page (all five readings shown, Weather fetching for London).
  - Default layout: v0.36.0 release 0.07% CPU and 128.4 MB private memory; this branch 0.08% CPU and 123.0 MB.
  - Readings page: v0.36.0 release 0.01% CPU and 129.8 MB; this branch 0.02% CPU and 131.9 MB.
  - Both are within the targets (CPU within 0.5 points, memory within 10%).
