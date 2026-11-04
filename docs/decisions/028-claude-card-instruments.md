# 028: The Claude Code card's instruments come back

On 4 Nov 2026, after v0.43.0, Ali asked to "bring back the design of the dashboards … to how it looked before, with the 5 hour being like a dial gauge and the weekly being like a race showing the days and the pace to reach it, and the tokens per minute also the same as before." The four-tile card of decision 027 stays; only the first three tiles' insides change, on both apps. The design is in [docs/design/claude-card.md](../design/claude-card.md).

## Decisions

- **The old instruments, in the new tiles.** The needle gauge, the week's laps and the burn line from the old card's pit wall (decision 017, `PitGraphics.cs` and `PitViews.java`) are back as `ClaudeGauge`, `ClaudeLaps` and `ClaudeBurn` (`src/widgets/ClaudeGauges.cs`, `android/.../views/ClaudeGauges.java`). They are drawn in the card's ink rather than the accent, since the card now wears an ink like every other, and they take the theme's colours as resource references on Windows so a theme or ink change redraws them.
- **5-HOUR:** the gauge with a pace tick and the projection arc, the percentage, the reset and "on pace for N%". **WEEKLY:** "LAP N OF 7", the percentage with the pace and the reset beside it, and the seven laps named by the window's days. **TOKENS / MIN:** the gauge with its redline over the half hour's burn line.
- **No new data.** The pace is how much of the window has passed, from its reset time and length (5 hours, 7 days); the projection is the use divided by the pace, shown from the window's first twentieth; the lap is the day of the window. All of it comes from the percentages and reset times the sign-in already reads (`ClaudePace` on the PC, the same functions in the phone's `ClaudeText`), so the phone link is unchanged and nothing else is fetched. The old "full in 1 h 40" line needed the telemetry receiver and stays gone.
- **The tokens gauge's scale** is 10k a minute or the next round step above the half hour's busiest minute (the old floor was 60k, which suited the old count with cache reads; decision 027 leaves cache reads out).
- **Fitting the tiles.** A gauge sits beside its reading in a wide tile and over it otherwise; the reading shrinks to keep its first line and other lines step aside (`CardSizing.ClaudeFit`), and the laps drop their day names in short tiles. The card's checks keep everything inside its own tile at every tested size and at text 100% and 140%.

## Tradeoffs

- The smallest tiles (the 360 × 280 minimum card) show a small gauge with just its reading, and laps without day names; the lines come back as the card grows.
- The projection says nothing in a window's first twentieth (15 minutes of the 5 hours, about 8 hours of the week), as before, since early numbers swing too much.
