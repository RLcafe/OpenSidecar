# Calendar and agenda

User request, 1 Nov 2026 (Windows and the phone). A Calendar card shows what's next from read-only calendar feeds, with a countdown to the next event. It is hidden by default; turn it on in Edit layout's Widgets list.

## Feeds, not accounts

The card reads iCalendar (.ics) feeds: Google Calendar's "Secret address in iCal format" and an Outlook calendar published as ICS (Settings → Calendar → Shared calendars → Publish), or any other https/webcal .ics address. Up to eight feeds, each with a name. There is no sign-in and no OAuth: signing in to Google or Microsoft would need an app registered with each of them (a client ID, a consent screen and, for Google, verification), which this personal app doesn't have. The feeds are read only; the widget can't change a calendar.

**The addresses are secrets** (anyone with one can read that calendar), so they're kept only in `calendar-feeds.dat` next to `settings.xml`, encrypted with DPAPI for the Windows account like the Spotify and Discord sessions, never in `settings.xml` and never in the repository. Settings shows them shortened to the host and the file name (`calendar.google.com/…/basic.ics`), and the phone never receives them. Removing every feed deletes the file.

## Settings → Calendar

A new tab after Prayer (tabs: Theme, Display, Widgets, Weather, Prayer, Calendar, Spotify, Quick Launch, Macros, Phone, Discord, Claude). To keep twelve tabs on one row the tab headers lost a little padding (16 instead of 24 units on each side). The tab lists the feeds with a Remove button each, and adds one from a name (optional; "Google Calendar", "Outlook" or "Calendar" from the address otherwise) and the address, which must start with https://, http:// or webcal://. Changes are saved with Save changes, like the rest of Settings; the right column says where to find each address.

## Reading

`CalendarService` fetches every feed (20-second timeout, 5 MB at most, through `PreferIPv4Handler`) when the card is first shown, then every 15 minutes while it's shown (30 in Quiet mode), and when the card is tapped. A feed that fails keeps its last good copy, and the heading says "1 FEED FAILED" (the reasons are in its tooltip and in Settings), or "OFFLINE" when none could be read. A 401, 403 or 404 says the address may have been reset.

`IcsCalendar` reads the feeds without a library:

- Folded lines, escaped text (`\,` `\n`), `DTSTART`/`DTEND` or `DURATION`, all-day dates (`VALUE=DATE`), UTC times, `LOCATION`, `STATUS:CANCELLED`.
- **Time zones:** the feed's own `VTIMEZONE` first (Google and Outlook include them, with their daylight-saving rules), then a Windows zone of that name (Outlook uses Windows names), then a short table of common IANA names (Google uses them); times without a zone are the PC's own.
- **Recurrence:** `RRULE` with `FREQ` DAILY, WEEKLY (`BYDAY`), MONTHLY (`BYMONTHDAY`, or `BYDAY` such as `2TU` and `-1FR`) and YEARLY (`BYMONTH`, `BYDAY`), with `INTERVAL`, `COUNT` and `UNTIL`; `EXDATE`; occurrences moved or cancelled with `RECURRENCE-ID`. Repeats are expanded in the event's own time zone, so a 9:00 meeting stays at 9:00 across a daylight-saving change. Long-running repeats without a COUNT start near today instead of at their first date. Rules beyond these repeat as the plain rule or appear once; nothing is invented.

The agenda covers the next two weeks, up to 200 events, soonest first.

## Designs (`CardSizing.Calendar`, the same numbers on the phone)

| Design | When | Shows |
| --- | --- | --- |
| Small | under 340 wide or 220 tall | The next event: its day and time in the accent, its title, and "in 25 m" (or "now · 45 m left"). A short card puts the countdown and start on one line over the title. |
| Medium | otherwise, below Large | The next event, a rule, then as many later events as fit at 30 units a row: "8:10 PM", "Tmrw 9:00 AM", "Thu 2:30 PM", with a dot per feed when there are several. The next-event block grows with the text size (`CalendarNextHeight`). |
| Large | 440 × 380 and up | The next event and its countdown, then the agenda grouped by day ("TODAY", "TOMORROW", "WEDNESDAY, NOV 4") with time, title and place, scrolling inside the card. |

The next event is the first timed event that hasn't ended, or else the first all-day one. The countdown follows the one-second heartbeat while the card is shown; the card only redraws when the minute or the events change. Without feeds the card says where to add them, and a tap opens Settings → Calendar.

## The phone

The `calendar` section carries the number of feeds, whether they've been read, the failed feeds' names and reasons, the PC's UTC offset, and up to 60 events (title, place, start and end as moments, all-day dates, feed number). The phone shows them on the PC's clock and counts down with its own; a tap on its card sends `calendar.refresh`. Feeds are added on the PC only.

## Checks

`tests/Calendar/CalendarTests.cs`: a Google-style feed (the feed's own New York rules on both sides of the November change, weekly Monday/Wednesday with COUNT, a skipped day, a moved day, a folded summary, an escaped comma, an all-day event, a cancelled event, soonest first); an Outlook-style feed (a Windows zone name, last Friday of the month with UNTIL, a daily repeat from 2020, yearly); broken feeds ignored; countdown, time and day wording; feeds kept encrypted, cleaned (ftp and file addresses refused), masked and deleted; a feed served from loopback read, then refused with the last copy kept; and the card at five sizes at 140% text in 12- and 24-hour time with a labelled fixture keeping its text inside the card, plus Settings refusing a bad address, adding and removing feeds. The phone-link checks read the section and confirm no address is in it, and refresh from the phone. `AndroidTests.java` covers parsing and wording.

Not tried: real Google and Outlook feeds (the fixtures follow their formats), and the phone card on a device.
