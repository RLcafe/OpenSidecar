using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // One occurrence of a calendar event, in UTC (all-day events keep their dates in Date and EndDate).
    public sealed class CalendarEvent
    {
        public string Title { get; set; }
        public string Location { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public bool AllDay { get; set; }
        public DateTime Date { get; set; }
        public DateTime EndDate { get; set; }
        public int Feed { get; set; }
        public CalendarEvent() { Title = ""; Location = ""; }
    }

    // Reads iCalendar (.ics) text, the read-only feeds Google ("Secret address in iCal format") and Outlook ("Publish
    // a calendar") give out (docs/design/calendar.md). Events with a time zone use the feed's own VTIMEZONE when it
    // has one, then a Windows zone of that name, then a short list of common IANA names; floating times are local.
    // Recurrence covers the usual RRULEs: DAILY, WEEKLY (BYDAY), MONTHLY (BYMONTHDAY, or BYDAY like 2TU and -1FR),
    // YEARLY (BYMONTH, BYDAY), with INTERVAL, COUNT and UNTIL, plus EXDATE and moved or cancelled occurrences
    // (RECURRENCE-ID). Anything else repeats as the plain rule or appears once, never invented.
    public static class IcsCalendar
    {
        private sealed class Property { public string Name, Value; public Dictionary<string, string> Parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
        private sealed class Component { public string Kind; public readonly List<Property> Properties = new List<Property>(); public readonly List<Component> Children = new List<Component>(); public Property Get(string name) { return Properties.FirstOrDefault(p => p.Name == name); } }

        // Long lines are folded with a leading space or tab on the next line (RFC 5545 §3.1).
        private static List<string> Unfold(string text)
        {
            var lines = new List<string>();
            foreach (string raw in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                if ((raw.StartsWith(" ") || raw.StartsWith("\t")) && lines.Count > 0) lines[lines.Count - 1] += raw.Substring(1);
                else if (raw.Length > 0) lines.Add(raw);
            }
            return lines;
        }
        private static Property ParseLine(string line)
        {
            int colon = -1; bool quoted = false;
            for (int i = 0; i < line.Length; i++) { if (line[i] == '"') quoted = !quoted; else if (line[i] == ':' && !quoted) { colon = i; break; } }
            if (colon < 0) return null;
            var parts = line.Substring(0, colon).Split(';');
            var property = new Property { Name = parts[0].ToUpperInvariant(), Value = line.Substring(colon + 1) };
            foreach (string part in parts.Skip(1)) { int eq = part.IndexOf('='); if (eq > 0) property.Parameters[part.Substring(0, eq)] = part.Substring(eq + 1).Trim('"'); }
            return property;
        }
        private static Component Parse(string text)
        {
            var root = new Component { Kind = "ROOT" }; var stack = new Stack<Component>(); stack.Push(root);
            foreach (string line in Unfold(text))
            {
                var property = ParseLine(line); if (property == null) continue;
                if (property.Name == "BEGIN") { var child = new Component { Kind = property.Value.Trim().ToUpperInvariant() }; stack.Peek().Children.Add(child); stack.Push(child); }
                else if (property.Name == "END") { if (stack.Count > 1) stack.Pop(); }
                else stack.Peek().Properties.Add(property);
            }
            return root;
        }
        private static string Unescape(string value)
        {
            var text = new StringBuilder();
            for (int i = 0; i < (value ?? "").Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length) { char next = value[++i]; text.Append(next == 'n' || next == 'N' ? ' ' : next); }
                else text.Append(value[i]);
            }
            return text.ToString().Trim();
        }

        // ----- Times and zones -----

        // A wall time and how to turn it into UTC.
        private sealed class Stamp { public DateTime Local; public bool Utc, DateOnly; public Zone Zone; }
        private abstract class Zone { public abstract TimeSpan Offset(DateTime local); }
        private sealed class WindowsZone : Zone
        {
            public readonly TimeZoneInfo Info; public WindowsZone(TimeZoneInfo info) { Info = info; }
            public override TimeSpan Offset(DateTime local) { var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified); return Info.IsInvalidTime(unspecified) ? Info.GetUtcOffset(unspecified.AddHours(1)) : Info.GetUtcOffset(unspecified); }
        }
        // A VTIMEZONE from the feed: its STANDARD and DAYLIGHT parts, each an offset from a start on a yearly rule.
        private sealed class FeedZone : Zone
        {
            public readonly List<Tuple<DateTime, TimeSpan, Dictionary<string, string>>> Parts = new List<Tuple<DateTime, TimeSpan, Dictionary<string, string>>>();
            public override TimeSpan Offset(DateTime local)
            {
                DateTime best = DateTime.MinValue; TimeSpan offset = Parts.Count > 0 ? Parts[0].Item2 : TimeSpan.Zero;
                foreach (var part in Parts)
                    foreach (int year in new[] { local.Year - 1, local.Year })
                    {
                        DateTime start = part.Item3 == null ? (year == part.Item1.Year ? part.Item1 : DateTime.MinValue) : Yearly(part.Item1, part.Item3, year);
                        if (start != DateTime.MinValue && start >= part.Item1 && start <= local && start > best) { best = start; offset = part.Item2; }
                    }
                return offset;
            }
        }
        // IANA names Google uses, for feeds without a VTIMEZONE.
        private static readonly Dictionary<string, string> Iana = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "UTC", "UTC" }, { "Etc/UTC", "UTC" }, { "GMT", "GMT Standard Time" }, { "Europe/London", "GMT Standard Time" }, { "Europe/Dublin", "GMT Standard Time" },
            { "Europe/Paris", "Romance Standard Time" }, { "Europe/Berlin", "W. Europe Standard Time" }, { "Europe/Amsterdam", "W. Europe Standard Time" }, { "Europe/Madrid", "Romance Standard Time" },
            { "Europe/Rome", "W. Europe Standard Time" }, { "Europe/Istanbul", "Turkey Standard Time" }, { "Europe/Moscow", "Russian Standard Time" }, { "Africa/Cairo", "Egypt Standard Time" },
            { "Asia/Riyadh", "Arab Standard Time" }, { "Asia/Kuwait", "Arab Standard Time" }, { "Asia/Qatar", "Arab Standard Time" }, { "Asia/Bahrain", "Arab Standard Time" }, { "Asia/Dubai", "Arabian Standard Time" },
            { "Asia/Muscat", "Arabian Standard Time" }, { "Asia/Tehran", "Iran Standard Time" }, { "Asia/Karachi", "Pakistan Standard Time" }, { "Asia/Kolkata", "India Standard Time" }, { "Asia/Calcutta", "India Standard Time" },
            { "Asia/Singapore", "Singapore Standard Time" }, { "Asia/Shanghai", "China Standard Time" }, { "Asia/Tokyo", "Tokyo Standard Time" }, { "Asia/Seoul", "Korea Standard Time" },
            { "Australia/Sydney", "AUS Eastern Standard Time" }, { "America/New_York", "Eastern Standard Time" }, { "America/Chicago", "Central Standard Time" }, { "America/Denver", "Mountain Standard Time" },
            { "America/Phoenix", "US Mountain Standard Time" }, { "America/Los_Angeles", "Pacific Standard Time" }, { "America/Toronto", "Eastern Standard Time" }, { "America/Sao_Paulo", "E. South America Standard Time" }
        };
        private static Zone FindZone(string id, Dictionary<string, Zone> feedZones)
        {
            if (String.IsNullOrEmpty(id)) return null;
            Zone zone;
            if (feedZones.TryGetValue(id, out zone)) return zone;
            var windows = WorldClockService.Find(id);
            string mapped;
            if (windows == null && Iana.TryGetValue(id, out mapped)) windows = WorldClockService.Find(mapped);
            return windows == null ? null : new WindowsZone(windows);
        }
        private static Dictionary<string, Zone> FeedZones(Component root)
        {
            var zones = new Dictionary<string, Zone>(StringComparer.OrdinalIgnoreCase);
            foreach (var timezone in root.Children.SelectMany(c => c.Kind == "VCALENDAR" ? c.Children : new List<Component> { c }).Where(c => c.Kind == "VTIMEZONE"))
            {
                var id = timezone.Get("TZID"); if (id == null) continue;
                var zone = new FeedZone();
                foreach (var part in timezone.Children.Where(c => c.Kind == "STANDARD" || c.Kind == "DAYLIGHT"))
                {
                    var start = part.Get("DTSTART"); var to = part.Get("TZOFFSETTO"); if (start == null || to == null) continue;
                    DateTime when; bool utc, dateOnly; TimeSpan offset;
                    if (!ParseTime(start.Value, out when, out utc, out dateOnly) || !ParseOffset(to.Value, out offset)) continue;
                    var rule = part.Get("RRULE");
                    zone.Parts.Add(Tuple.Create(when, offset, rule == null ? null : Rule(rule.Value)));
                }
                if (zone.Parts.Count > 0) zones[id.Value.Trim()] = zone;
            }
            return zones;
        }
        private static bool ParseOffset(string text, out TimeSpan offset)
        {
            offset = TimeSpan.Zero; text = (text ?? "").Trim();
            if (text.Length < 5) return false;
            int sign = text[0] == '-' ? -1 : 1; int hours, minutes;
            if (!int.TryParse(text.Substring(1, 2), out hours) || !int.TryParse(text.Substring(3, 2), out minutes)) return false;
            offset = TimeSpan.FromMinutes(sign * (hours * 60 + minutes)); return true;
        }
        private static bool ParseTime(string text, out DateTime when, out bool utc, out bool dateOnly)
        {
            text = (text ?? "").Trim(); utc = text.EndsWith("Z"); dateOnly = text.Length == 8;
            return DateTime.TryParseExact(utc ? text.TrimEnd('Z') : text, dateOnly ? "yyyyMMdd" : "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out when);
        }
        private static Stamp Time(Property property, Dictionary<string, Zone> zones)
        {
            if (property == null) return null;
            DateTime when; bool utc, dateOnly;
            if (!ParseTime(property.Value.Split(',')[0], out when, out utc, out dateOnly)) return null;
            string tzid; property.Parameters.TryGetValue("TZID", out tzid);
            return new Stamp { Local = when, Utc = utc, DateOnly = dateOnly || (property.Parameters.ContainsKey("VALUE") && property.Parameters["VALUE"] == "DATE"), Zone = FindZone(tzid, zones) };
        }
        private static DateTime ToUtc(DateTime local, Stamp stamp, TimeZoneInfo floating)
        {
            if (stamp.Utc) return DateTime.SpecifyKind(local, DateTimeKind.Utc);
            if (stamp.Zone != null) return DateTime.SpecifyKind(local - stamp.Zone.Offset(local), DateTimeKind.Utc);
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            if (floating.IsInvalidTime(unspecified)) unspecified = unspecified.AddHours(1);
            return TimeZoneInfo.ConvertTimeToUtc(unspecified, floating);
        }

        // ----- Recurrence -----

        private static Dictionary<string, string> Rule(string text)
        {
            var rule = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (text ?? "").Split(';')) { int eq = part.IndexOf('='); if (eq > 0) rule[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim().ToUpperInvariant(); }
            return rule;
        }
        private static readonly string[] Days = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };
        // "2TU" is the second Tuesday, "-1FR" the last Friday, "MO" any Monday (0).
        private static bool Weekday(string token, out DayOfWeek day, out int ordinal)
        {
            day = DayOfWeek.Sunday; ordinal = 0; token = (token ?? "").Trim();
            if (token.Length < 2) return false;
            int index = Array.IndexOf(Days, token.Substring(token.Length - 2)); if (index < 0) return false;
            day = (DayOfWeek)index;
            string number = token.Substring(0, token.Length - 2);
            return number.Length == 0 || int.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ordinal);
        }
        // The days of a month that match "2TU", "-1FR" or "MO".
        private static IEnumerable<DateTime> InMonth(int year, int month, DayOfWeek day, int ordinal)
        {
            var all = Enumerable.Range(1, DateTime.DaysInMonth(year, month)).Select(d => new DateTime(year, month, d)).Where(d => d.DayOfWeek == day).ToList();
            if (ordinal == 0) return all;
            int index = ordinal > 0 ? ordinal - 1 : all.Count + ordinal;
            return index >= 0 && index < all.Count ? new[] { all[index] } : new DateTime[0];
        }
        // A VTIMEZONE part's start in a given year (its yearly rule, usually BYMONTH and BYDAY like 2SU or -1SU).
        private static DateTime Yearly(DateTime start, Dictionary<string, string> rule, int year)
        {
            int month; if (!rule.ContainsKey("BYMONTH") || !int.TryParse(rule["BYMONTH"].Split(',')[0], out month)) month = start.Month;
            DateTime day = new DateTime(year, month, Math.Min(start.Day, DateTime.DaysInMonth(year, month)));
            DayOfWeek weekday; int ordinal;
            if (rule.ContainsKey("BYDAY") && Weekday(rule["BYDAY"].Split(',')[0], out weekday, out ordinal))
            {
                var found = InMonth(year, month, weekday, ordinal == 0 ? 1 : ordinal).FirstOrDefault();
                if (found == default(DateTime)) return DateTime.MinValue;
                day = found;
            }
            return day.Date + start.TimeOfDay;
        }

        // The local start times of a recurring event that begin before 'until' (and within its own COUNT and UNTIL).
        private static IEnumerable<DateTime> Occurrences(DateTime start, Dictionary<string, string> rule, DateTime from, DateTime until, Stamp stamp, TimeZoneInfo floating)
        {
            string freq = rule.ContainsKey("FREQ") ? rule["FREQ"] : "";
            int interval; if (!rule.ContainsKey("INTERVAL") || !int.TryParse(rule["INTERVAL"], out interval) || interval < 1) interval = 1;
            int count = int.MaxValue, parsed;
            if (rule.ContainsKey("COUNT") && int.TryParse(rule["COUNT"], out parsed) && parsed > 0) count = parsed;
            DateTime ruleEnd = DateTime.MaxValue;
            if (rule.ContainsKey("UNTIL"))
            {
                DateTime end; bool utc, dateOnly;
                if (ParseTime(rule["UNTIL"], out end, out utc, out dateOnly))
                    ruleEnd = dateOnly ? end.AddDays(1).AddTicks(-1) : utc && !stamp.DateOnly ? ToLocalOf(end, stamp, floating) : end;
            }
            var byDay = rule.ContainsKey("BYDAY") ? rule["BYDAY"].Split(',') : new string[0];
            var byMonthDay = rule.ContainsKey("BYMONTHDAY") ? rule["BYMONTHDAY"].Split(',').Select(t => { int d; return int.TryParse(t, out d) ? d : 0; }).Where(d => d != 0).ToArray() : new int[0];
            int found = 0, first = 0;
            // Without a COUNT, a long-running repeat starts a period before the window instead of at its first time.
            if (count == int.MaxValue && from > start)
            {
                double span = freq == "DAILY" ? (from - start).TotalDays : freq == "WEEKLY" ? (from - start).TotalDays / 7 : freq == "MONTHLY" ? (from.Year - start.Year) * 12 + from.Month - start.Month : freq == "YEARLY" ? from.Year - start.Year : 0;
                first = Math.Max(0, (int)Math.Floor(span / interval) - 1);
            }
            for (int period = first; period < first + 5000; period++)
            {
                var candidates = new List<DateTime>();
                if (freq == "DAILY") candidates.Add(start.AddDays(period * interval));
                else if (freq == "WEEKLY")
                {
                    // The week (Monday first) that holds the period's start, then the listed weekdays in it.
                    var weekStart = start.Date.AddDays(-(((int)start.DayOfWeek + 6) % 7)).AddDays(7 * period * interval);
                    if (byDay.Length == 0) candidates.Add(start.AddDays(7 * period * interval));
                    else foreach (string token in byDay) { DayOfWeek day; int ordinal; if (Weekday(token, out day, out ordinal)) candidates.Add(weekStart.AddDays(((int)day + 6) % 7) + start.TimeOfDay); }
                }
                else if (freq == "MONTHLY")
                {
                    var month = new DateTime(start.Year, start.Month, 1).AddMonths(period * interval);
                    if (byMonthDay.Length > 0) foreach (int d in byMonthDay) { int days = DateTime.DaysInMonth(month.Year, month.Month), dayNumber = d > 0 ? d : days + d + 1; if (dayNumber >= 1 && dayNumber <= days) candidates.Add(month.AddDays(dayNumber - 1) + start.TimeOfDay); }
                    else if (byDay.Length > 0) foreach (string token in byDay) { DayOfWeek day; int ordinal; if (Weekday(token, out day, out ordinal)) candidates.AddRange(InMonth(month.Year, month.Month, day, ordinal).Select(x => x + start.TimeOfDay)); }
                    else if (start.Day <= DateTime.DaysInMonth(month.Year, month.Month)) candidates.Add(month.AddDays(start.Day - 1) + start.TimeOfDay);
                }
                else if (freq == "YEARLY")
                {
                    int year = start.Year + period * interval;
                    if (rule.ContainsKey("BYMONTH") || byDay.Length > 0) { var at = Yearly(start, rule, year); if (at != DateTime.MinValue) candidates.Add(at); }
                    else if (start.Month != 2 || start.Day != 29 || DateTime.IsLeapYear(year)) candidates.Add(new DateTime(year, start.Month, start.Day) + start.TimeOfDay);
                }
                else { yield return start; yield break; }
                candidates.Sort();
                foreach (var candidate in candidates)
                {
                    if (candidate < start) continue;
                    if (candidate > ruleEnd || found >= count) yield break;
                    found++;
                    if (candidate >= until) yield break;
                    yield return candidate;
                }
            }
        }
        private static DateTime ToLocalOf(DateTime utc, Stamp stamp, TimeZoneInfo floating)
        {
            if (stamp.Utc) return utc;
            if (stamp.Zone != null) return utc + stamp.Zone.Offset(utc + stamp.Zone.Offset(utc));
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), floating);
        }

        // ----- Events -----

        // Every occurrence that overlaps [fromUtc, toUtc), soonest first. 'floating' is the zone for times without one
        // (the PC's own).
        public static List<CalendarEvent> Events(string ics, DateTime fromUtc, DateTime toUtc, TimeZoneInfo floating, int feed = 0)
        {
            var root = Parse(ics);
            var zones = FeedZones(root);
            var vevents = root.Children.SelectMany(c => c.Kind == "VCALENDAR" ? c.Children : new List<Component> { c }).Where(c => c.Kind == "VEVENT").ToList();
            // Moved or cancelled occurrences of a recurring event: UID plus the original start, in UTC.
            var overrides = new Dictionary<string, Component>();
            foreach (var e in vevents.Where(e => e.Get("RECURRENCE-ID") != null && e.Get("UID") != null))
            {
                var id = Time(e.Get("RECURRENCE-ID"), zones); if (id == null) continue;
                overrides[e.Get("UID").Value + "|" + Key(ToUtc(id.Local, id, floating), id.DateOnly, id.Local)] = e;
            }
            var result = new List<CalendarEvent>();
            foreach (var e in vevents.Where(e => e.Get("RECURRENCE-ID") == null))
            {
                if (Cancelled(e)) continue;
                var start = Time(e.Get("DTSTART"), zones); if (start == null) continue;
                TimeSpan length = Length(e, start, zones);
                var excluded = new HashSet<string>();
                foreach (var ex in e.Properties.Where(p => p.Name == "EXDATE"))
                    foreach (string value in ex.Value.Split(','))
                    {
                        var stamp = Time(new Property { Name = "EXDATE", Value = value, Parameters = ex.Parameters }, zones);
                        if (stamp != null) excluded.Add(Key(ToUtc(stamp.Local, stamp, floating), stamp.DateOnly, stamp.Local));
                    }
                var rrule = e.Get("RRULE");
                var until = ToLocalOf(toUtc, start, floating).AddDays(1);
                var starts = rrule == null ? new[] { start.Local } : Occurrences(start.Local, Rule(rrule.Value), ToLocalOf(fromUtc, start, floating).AddDays(-2), until, start, floating);
                string uid = e.Get("UID") == null ? null : e.Get("UID").Value;
                foreach (var local in starts)
                {
                    var startUtc = start.DateOnly ? local : ToUtc(local, start, floating);
                    string key = Key(start.DateOnly ? local : startUtc, start.DateOnly, local);
                    if (excluded.Contains(key)) continue;
                    Component source = e;
                    if (uid != null && overrides.TryGetValue(uid + "|" + key, out source)) { if (Cancelled(source)) continue; }
                    else source = e;
                    var occurrence = Build(source == e ? null : source, e, local, length, start, zones, floating, feed);
                    if (occurrence != null && Overlaps(occurrence, fromUtc, toUtc, floating)) result.Add(occurrence);
                }
            }
            return result.OrderBy(o => o.AllDay ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(o.Date, DateTimeKind.Unspecified), floating) : o.StartUtc).ThenBy(o => o.Title).ToList();
        }
        private static string Key(DateTime utc, bool dateOnly, DateTime local) { return dateOnly ? local.ToString("yyyyMMdd", CultureInfo.InvariantCulture) : utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture); }
        private static bool Cancelled(Component e) { var status = e.Get("STATUS"); return status != null && status.Value.Trim().Equals("CANCELLED", StringComparison.OrdinalIgnoreCase); }
        private static TimeSpan Length(Component e, Stamp start, Dictionary<string, Zone> zones)
        {
            var end = Time(e.Get("DTEND"), zones);
            if (end != null) { var length = end.Local - start.Local; if (end.Utc != start.Utc || end.Zone != start.Zone) length = ToUtc(end.Local, end, TimeZoneInfo.Utc) - ToUtc(start.Local, start, TimeZoneInfo.Utc); return length > TimeSpan.Zero ? length : TimeSpan.Zero; }
            var duration = e.Get("DURATION");
            if (duration != null) { try { return System.Xml.XmlConvert.ToTimeSpan(duration.Value.Trim()); } catch (FormatException) { } }
            return start.DateOnly ? TimeSpan.FromDays(1) : TimeSpan.Zero;
        }
        private static CalendarEvent Build(Component moved, Component series, DateTime local, TimeSpan length, Stamp start, Dictionary<string, Zone> zones, TimeZoneInfo floating, int feed)
        {
            var e = moved ?? series;
            var summary = e.Get("SUMMARY") ?? series.Get("SUMMARY"); var location = e.Get("LOCATION") ?? series.Get("LOCATION");
            var item = new CalendarEvent { Title = summary == null ? "Busy" : Unescape(summary.Value), Location = location == null ? "" : Unescape(location.Value), Feed = feed };
            if (item.Title.Length == 0) item.Title = "Busy";
            if (moved != null)
            {
                var s = Time(moved.Get("DTSTART"), zones); if (s == null) return null;
                length = Length(moved, s, zones); start = s; local = s.Local;
            }
            if (start.DateOnly) { item.AllDay = true; item.Date = local.Date; item.EndDate = local.Date + TimeSpan.FromDays(Math.Max(1, Math.Round(length.TotalDays))); item.StartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(item.Date, DateTimeKind.Unspecified), floating); item.EndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(item.EndDate, DateTimeKind.Unspecified), floating); }
            else { item.StartUtc = ToUtc(local, start, floating); item.EndUtc = item.StartUtc + length; }
            return item;
        }
        private static bool Overlaps(CalendarEvent e, DateTime fromUtc, DateTime toUtc, TimeZoneInfo floating)
        {
            var end = e.EndUtc > e.StartUtc ? e.EndUtc : e.StartUtc.AddTicks(1);
            return e.StartUtc < toUtc && end > fromUtc;
        }
    }
}
