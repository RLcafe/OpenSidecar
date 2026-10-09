# Header clock and scalable System stats

User request, 9 Oct 2026. This diverges from the as-built spec (§3.2, §3.6, §4.1 and §4.6) and from `touchscreen-polish.md` where they describe the Clock widget, the header brand and the System Pulse rows.

## Clock in the header

- The Clock widget is gone. Time and date sit at the top left of the header in every state, including Edit layout, where the editor toolbar sits to their right.
- The header brand ("Widget Screen", "YOUR SPACE. AT A GLANCE." and the "DESK DASHBOARD" pill) was removed to make room.
- Layout: time in Segoe UI Light at `HeaderClockSize` (52 × min(1.06, TextScale)), AM/PM in ClockMuted at BodySize (hidden with the 24-hour clock), a 2 px ControlBorder divider, then the weekday as an Accent label over the date ("October 9") at TitleSize.
- `WidgetLayout.Ids` no longer contains `Clock`. A saved `Clock` placement is dropped when settings load. If Focus sat directly below a visible Clock in the same column (the old default), Focus grows upward into the freed space, unless another visible widget is in the way. Every other custom placement is unchanged.
- The new default gives Focus the full column: (0, 0, 320, 576).
- `ClockWidth` and `ClockHeight` stay in `settings.xml`. They only feed the legacy column formulas, where `ClockWidth` now weights the Focus column.

## System stats

- Each metric is one fixed-height design row (40 high, at least 324 wide, 6 × 3 margins) in a `UniformGrid` inside a `Viewbox`. The card heading row stays at normal size.
- `SystemStatsLayout` (src/widgets) picks 1, 2 or 3 columns, whichever lets the rows draw largest in the card. It then widens the rows so the scaled grid fills the card's width. Readings grow when the widget grows, and bars and the CPU graph use the extra width.
- Rows: CPU (value and graph), GPU (value, bar and engine detail), RAM (percentage, bar and used/total GB), NET (↓ and ↑ rates) and UPTIME. Names are 16 SemiBold Muted, percentages 30, rates and uptime 21, and details 14, all in design units before scaling.
- CPU, GPU and RAM values switch to the Warning brush at 90% or more.
- At the 380 × 310 minimum, all five rows fit at about 0.98 scale.
