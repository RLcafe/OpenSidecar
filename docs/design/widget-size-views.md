# Small, medium and large widget designs (31 Oct 2026)

User request, 31 Oct 2026: "for all widgets, make them all have a small, medium and large view each having a different design that switches when i resize. as a test start with system stats and weather only. make them look nice with a newer look with better design."

Until now a card had one layout that scaled and folded rows away as it shrank ([`resizable-widgets.md`](resizable-widgets.md)). Now System stats and Weather each have **three designs**, and the card picks one from its size while you resize it in Edit layout, live, on both apps. The other widgets keep their single layout until this pilot is approved. Designed by hand in the existing visual system (palettes, accents, Segoe UI with Light numerals, Raised tiles, 13-unit captions as the floor); the Impeccable skill wasn't used because the request didn't name it.

## Thresholds

`CardSizing.Stats` / `CardSizing.Weather` (Windows) and `CardSizing.stats` / `CardSizing.weather` (phone), the same numbers:

| Card | Small | Medium | Large |
| --- | --- | --- | --- |
| System stats | under 340 wide, or under 240 tall | everything between (the reference 472 × 310) | 400 wide and 420 tall, or more |
| Weather | under 340 wide, or under 220 tall | everything between (the reference 472 × 250) | 400 wide and 340 tall, or more |

Minimum and preferred sizes are unchanged, so default layouts open in the Medium design and saved layouts look the same as before except for the new designs.

## System stats

- **Small, a list.** One row per reading: the name at the left, the number at the right in Light numerals sized to the row (`StatsListValue`, 20–40), and a 4-unit bar under CPU, GPU and RAM. Network shows ↓ and ↑ stacked; uptime is a smaller number.
- **Medium, rings.** CPU, GPU and RAM as rings side by side (`StatsRing`, 56–200 across, the percentage inside), with the name and a detail under each (the GPU's engine, RAM's "15.4 / 31.1 GB"). Network and uptime share a 56-unit Raised strip underneath. With no percentage chosen, Medium falls back to the list.
- **Large, tiles with history.** A Raised tile per reading (CPU, GPU, RAM, Network): the name and detail on top, the number (`StatsTileValue`, 30–64), and the last 48 readings (about 1.5 minutes) as a line over a faint area, with hairlines at the floor and 50%. Network graphs download in the accent and upload as a muted line, scaled to the largest rate shown. Two tiles across, four on cards 960 wide or more (`StatsTileColumns`); uptime moves to the heading ("Up 2d 6h 52m").
- A reading at 90% or more turns the number, ring, bar or graph to the warning colour, as before. The history is kept for CPU, GPU, RAM and both network rates from the readings the card already gets, so no extra polling; switching designs keeps it.

## Weather

- **Small, a glance.** No heading: the temperature (Light, `WeatherTemperature`) with the condition glyph beside it, the city on one line, today's high and low (or the condition), and the Open-Meteo credit. Offline reads "Offline. Tap to try again." in the warning colour.
- **Medium, today.** The heading (OFFLINE shows at its right), a large glyph beside the temperature with the condition and city, then **Feels, Wind and High / Low** as three small Raised tiles (`WeatherService.Facts`, `Weather.facts`). A Medium card at least 300 tall also gets three hours (now, +3 h, +6 h) with a glyph each.
- **Large, the forecast.** The same, larger (temperature up to 128, the condition in the title size), and a Raised strip of up to five hours (now, +3, +6, +9 and +12 h; as many as fit 72 units each, `WeatherHours`), "Now" in the accent.
- Without a city, or before the first reading, the facts' place holds the message ("Tap to choose a city.", "Updating weather…", or the offline text). The whole card stays the refresh tap.
- The forecast now keeps five hours instead of three (`WeatherService.Parse`, `Weather.parse`); the request is unchanged.

## Code

- Windows: `src/widgets/SystemStatsCard.cs` and `src/widgets/WeatherCard.cs` build the design for the card's size into `SystemBody` and `WeatherBody` and keep the last reading, so a rebuild starts full. The drawn parts (`RingGauge`, `MeterBar`, `HistoryGraph`, the weather glyph at any size) are in `src/widgets/CardGraphics.cs`, with brushes set by resource so palettes and accents apply live. `SystemStatsLayout` and the stats `Viewbox` are gone.
- Phone: `SystemWidget.java` and `WeatherWidget.java` build the same three designs when the card is built for its size; `views/RingGauge.java` is new and `views/Sparkline.java` gained the area, guides and second series. `core/StatsLayout.java` is gone.
- Nothing changed in the phone link: the phone already receives every reading and keeps its own history.

## Checks and renders

- Windows: `SmokeTests` "System stats and Weather switch between small, medium and large designs that stay inside their cards" (thresholds, sizes, uptime text, and every text, ring, graph and tile inside the card's padding at four sizes with text at 140% and a long city name); "System stats grow with their widget"; the weather parser keeps five hours and the facts.
- Phone: `AndroidTests` "System stats and Weather pick small, medium and large designs like the PC" and the updated weather and temperature checks.
- `tests\size-design-renders.ps1` renders the Small, Medium, Large and wide scenes with live readings (this PC's stats, Open-Meteo for London) into `artifacts\size-designs\`.

## Not verified

- The phone's new card layouts on a device or emulator (none was available; they build, and their sizes are checked on the JVM).
- Live touch resizing on the real touchscreen.

## Every widget (31 Oct 2026, v0.18.0)

The user asked for the rest the same day. Small is each card's folded layout from [`resizable-widgets.md`](resizable-widgets.md) and Medium is the card as it was; Large is new. Thresholds are in `CardSizing` on both apps.

| Card | Small | Medium | Large |
| --- | --- | --- | --- |
| Focus (`Focus`) | under 300 tall: countdown, length button, Start | the countdown, status, presets and a progress line | from 300 × 460 (the default full column): the progress as a ring (`FocusRing`, 160–420) around the countdown, with the status and today's sessions inside it |
| Now Playing (`Media`) | under 290 tall: cover and song in one row over the buttons | cover beside the song over the seek row | from 360 × 460: a big centred cover (`MediaCover`, 120–360, less with larger text) over the song in 28, then the seek row and buttons |
| Sound mixer (`MixerDesign`) | Master (under 300 tall) | Compact (300–419) | Full (420 and up); 760 wide or more lists the apps in two columns 24 apart (`MixerColumns`) |
| Spotify (`Spotify`) | under 500 tall: the compact card | the full card | from 760 wide: the song moves into a Raised side panel (a third of the card, 260–360) with a cover as wide as the panel over the title, artist and a full-width Like |
| Quick Launch and Soundboard buttons (`Tile`, by cell) | under 100 × 64: the glyph (or the sound's name) alone | the glyph over the name (a play mark over the sound's name) | from 150 × 110: a tile from the top left with the glyph, the name in 17 semibold, and what it does: "App", "Website · www.example.com", "Hotkey · Ctrl+Shift+M" (`DeckActions.Kind`, sent to the phone as `kind`), or "Discord sound", "This server", "From another server" |

- The default layout's Focus column (320 × 576) now opens in the Large ring design; everything else in the default layout keeps its Medium design.
- Windows rearranges the existing XAML parts for each design (`FitFocus`, `FitMedia`, `FitMixer` in `DashboardController.Cards.cs`, `SpotifyWidget.Fit`, `BuildShortcuts`, `SoundboardWidget.Build`); the phone builds each design when the card is built for its size.
- Checks: `SmokeTests` "Every other card picks small, medium and large designs, and the large ones stay inside their cards" (thresholds, deck lines, and the Large Focus, Now Playing and two-column mixer in bounds at 140% text); `SpotifyTests` "Spotify's Large design puts the song in a side panel that stays inside the card" (labelled fixture, `artifacts\tests\spotify-large-fixture.png`); `AndroidTests` "Every other card picks small, medium and large designs like the PC".
- Renders: `tests\size-design-renders.ps1` scenes `cards-small`, `cards-medium`, `cards-large` and `cards-wide` (live audio and media; Spotify and Discord aren't connected in isolated settings, so their cards show the Connect state there).
- Not verified: the phone's new layouts on a device, the Soundboard's tiles with a connected Discord, and Spotify's side panel with a real account (fixture only).