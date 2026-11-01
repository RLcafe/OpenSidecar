# World clocks

User request, 1 Nov 2026 (Windows and the phone). A World clocks card shows two or three time zones side by side, each with a city name, its local time, its day and how many hours it is from the PC. It is hidden by default, so the default layout doesn't change; turn it on in Edit layout's Widgets list.

## Choosing the cities

Settings → Widgets → World clocks has three rows, each a time zone list and a city name. The list is Windows' own time zones (`TimeZoneInfo.GetSystemTimeZones`), with None first to leave a row empty. Picking a zone fills the name with its first city ("(UTC+09:00) Osaka, Sapporo, Tokyo" gives "Osaka", "Eastern Time (US & Canada)" gives "Eastern Time"), unless a name of your own is typed there; names are up to 24 characters. The choice is saved as `AppSettings.WorldClocks` (`WorldClock { Zone, Label }`), cleaned on load by `WorldClockService.Clean`: up to three, zones Windows doesn't know are dropped, and an empty name becomes the first city. With no cities the card says so, and a tap on it opens this section.

## Time

Nothing is fetched: times come from Windows' time zone rules (`TimeZoneInfo.ConvertTimeFromUtc`), so daylight saving follows what Windows Update keeps current. The card follows the existing one-second heartbeat only while it's shown, and its text changes only when a minute turns. Times follow the 24-hour clock setting. The day is "Today", "Tomorrow" or "Yesterday" against the PC's date, and the hours are "+3 h", "−5:30 h" or "Same time" against the PC's zone.

## Designs (`CardSizing.Clocks`, the same numbers on the phone)

| Design | When | Shows |
| --- | --- | --- |
| Small | under 340 wide or 200 tall | One row per city: the name, with "Today · +6 h" under it from 46 units a row, and the time at the right. Rows are at least 32 units; cities that don't fit are counted in the heading ("+1 MORE"). |
| Medium | otherwise, below Large | The cities side by side: name, a large time with AM/PM, the day and the hours. The time is as large as the column allows (`ClockColumnTime`). |
| Large | 440 × 300 and up | A tile per city with a larger time, the date ("Tomorrow, Nov 2") and "+6 h from this PC". Tiles sit side by side while each is 170 wide; otherwise they stack, with the time at the right of the name and the date and hours on one line. |

## The phone

The phone's `clocks` section carries each city's name and its offset from UTC now, and the PC's own offset (`pcOffset`). The phone works the time out from its own clock and those offsets, so the card keeps moving between readings, and the day and hours are against the PC (the phone mirrors the PC's dashboard). An offset that changes at a daylight-saving switch arrives with the next reading. Cities are chosen on the PC only, like the weather city.

## Checks

`tests/Clocks/ClockTests.cs` covers default names, day and hour wording, times against a fixed moment in Riyadh's zone, London's daylight saving, cleaning and saving the choice, the hidden default placement, the design numbers, Settings showing the saved cities, and every design at 140% text (12- and 24-hour) keeping its text inside the card with no time cut short. The phone's checks (`AndroidTests.java`) cover the same numbers and wording and parse the real `clocks` section from the Windows checks' sample.

Not tried on a phone or emulator; the phone card builds and its numbers are checked like the PC's.
