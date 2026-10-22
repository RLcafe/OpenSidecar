# Weather: units, forecast, glyph and the tap (22 Oct 2026)

From the app audit. The spec's Weather contract (current conditions for one city, refreshed every 15 minutes, Open-Meteo) stays; this note is what was added on top, on both apps.

## Units

- `AppSettings.WeatherUnits` is `metric` (Celsius, km/h) or `imperial` (Fahrenheit, mph). A missing or odd value becomes the region's default (`RegionInfo.CurrentRegion.IsMetric`), so existing settings files pick the right one on first load. Settings → Weather has the choice.
- Open-Meteo converts (`temperature_unit=fahrenheit&wind_speed_unit=mph`), so the apps only label. The big temperature shows `°` alone; the details line names the wind unit.
- The phone receives `imperial` in the `weather` section and fetches with the same parameters.

## Forecast

- The request adds `daily=temperature_2m_max,temperature_2m_min` and `hourly=temperature_2m,weather_code&forecast_days=2`.
- Details read **"Feels 25°  ·  12 km/h  ·  ↑ 28° ↓ 15°"** (`WeatherService.Details`, `Weather.details`).
- The next hours are **now, +3 h and +6 h**, picked from the hour at or after the current one (two forecast days, so late evening still has +6 h). They take a 60-unit row under the reading when the card is at least 300 units tall (`WeatherHoursRow`, `hoursRow`); shorter cards keep the reading only. Hour labels follow the clock (`14:00` or `2 PM`); the first says **Now**.
- Both parts are optional in the parser: a missing or odd forecast leaves current conditions intact.

## Glyph

- `WeatherService.Family(code)` maps a WMO code to **sun, partly, cloud, fog, rain, snow or storm**; `WeatherService.Glyph(family)` is the path on a 24-unit grid, drawn by a `Path` beside the condition; `Glyphs.java` draws the same shapes on the phone. No images, as the rules say.

## The tap

- The Refresh button is gone. Tapping the card updates the weather now (or, with no city, opens Settings → Weather on the PC; the phone says where to choose one). The "updated 14:05" credit is the signal that it worked; while updating it says "updating…".
