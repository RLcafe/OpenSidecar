# Prayer times

User request, 1 Nov 2026 (Windows and the phone). A Prayer times card shows today's five prayers for one city, the next one with a countdown, and can play an alert on the PC at each prayer. It is hidden by default; turn it on in Edit layout's Widgets list.

## Settings → Prayer

A new tab after Weather (tabs are now Theme, Display, Widgets, Weather, Prayer, Spotify, Quick Launch, Phone, Discord; code opens tabs by their header, `DashboardController.SelectSettingsTab`, and `--render-tab` takes a header too).

- **City.** Search uses the same Open-Meteo geocoding as the weather city (`WeatherService.SearchAsync`), and only the latitude and longitude are kept (`PrayerLocation`, `PrayerLatitude`, `PrayerLongitude`). With no city of its own the card follows the weather city; Use the weather city goes back to that.
- **Calculation method** (`PrayerMethod`): Muslim World League, Umm al-Qura (Makkah), ISNA, Egypt, Karachi, Gulf region, Kuwait, Qatar, Singapore, Diyanet (Turkey), Tehran and Jafari, with their Fajr and Isha angles (or Isha a fixed time after Maghrib). The default follows the PC's region (Saudi Arabia gets Umm al-Qura, the US and Canada ISNA, and so on; elsewhere Muslim World League).
- **Asr** (`PrayerHanafi`): Standard (Shafi'i, Maliki, Hanbali; shadow equal to the object) or Hanafi (twice).
- **Alert** (`PrayerAlert`, off by default) and **sound** (`PrayerSound`): an MP3, WAV, WMA, M4A or AAC file the user picks, played with WPF's `MediaPlayer`; with none, a two-note chime generated in code (`PrayerAlert.Chime`). No recording ships with the app. Play tests the sound from Settings.

## Calculation

`PrayerService.Calculate` is the standard astronomical method of PrayTimes.org (public domain): the sun's declination and equation of time for the day, Dhuhr at solar noon, Fajr and Isha when the sun is the method's angle below the horizon, sunrise and sunset at 0.833°, Asr when an object's shadow is one (or two) times its length plus its noon shadow. Each time is refined once with the sun's position at that time. Where the sun never gets far enough below the horizon (northern summers), Fajr and Isha use the angle-based rule (no further than angle/60 of the night from sunrise and sunset), as most published timetables do. Umm al-Qura's Isha is 90 minutes after Maghrib, 120 in Ramadan (by .NET's `UmAlQuraCalendar`). Times are worked out in UTC for the PC's calendar day and shown in the PC's time zone, rounded to the minute. Nothing is fetched.

Checked against published Aladhan timetables: Riyadh on 1 Nov 2026 (Umm al-Qura) and London on 15 Jun 2026 (Muslim World League, Hanafi) match within two minutes for every time. Mosques sometimes add their own minutes, so Settings says the times can differ by a minute or two.

## The card (`CardSizing.Prayer`, the same numbers on the phone)

| Design | When | Shows |
| --- | --- | --- |
| Small | under 340 wide or 220 tall | The next prayer: its name, its time as a large reading and "in 1 h 12 m". Very short cards put the countdown beside the name. |
| Medium | otherwise, below Large | The next prayer with a progress line since the last one, beside today's five times; the next is in the accent, past ones muted. |
| Large | 440 × 360 and up | The next prayer with progress, the Hijri date (Umm al-Qura) and the method, over the five times and sunrise as rows, or as six tiles across when the body is 860 wide. |

The heading shows the city, or "ASR NOW" in the accent for 20 minutes after a prayer begins (and while the alert plays). The countdown follows the one-second heartbeat while the card is shown; each day's times are worked out once and kept. Without a city the card says so and a tap opens Settings → Prayer.

## The alert

The heartbeat checks every second, also while the dashboard is minimized or in the tray: when a prayer began within the last minute and hasn't sounded yet, the status line says "It's time for Asr." and, if the alert is on and Quiet mode is off, the sound plays on the PC. A Stop button shows on the card while it plays. Changing the city or method never sounds a prayer that has already begun.

## The phone

The phone's `prayer` section carries today's five times and sunrise as moments (epoch milliseconds), the next and current prayer, the PC's UTC offset, the city, method, Hijri date and whether the alert is playing. The phone shows the times on the PC's clock and counts down with its own; its card offers Stop while the PC plays the alert (`prayer.stop`). The alert plays on the PC only, and the city, method and alert are set on the PC.

## Checks

`tests/Prayer/PrayerTests.cs`: both reference timetables, Umm al-Qura's Ramadan Isha, Tehran's Maghrib, every method giving five ordered times in Riyadh, Oslo, Sydney and Reykjavik at midsummer, the next prayer after Asr and across midnight, countdown and Hijri wording, settings defaults, save and repair, following the weather city, the chime being a short WAV, the hidden default, design numbers, every design at 140% text in 12- and 24-hour time keeping its text inside the card with no time cut short, and Settings → Prayer showing and saving the choices. The phone-link checks read the `prayer` section and stop the alert from the phone. `AndroidTests.java` covers the same numbers and wording and parses the real section.

Not tried: the alert sounding at a real prayer time (the timing is the same heartbeat as the countdown, but no check waits for a prayer), a real audio file, and the phone card on a device.
