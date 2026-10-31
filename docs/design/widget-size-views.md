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
