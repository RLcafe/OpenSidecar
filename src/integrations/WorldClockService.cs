using System;
using System.Collections.Generic;
using System.Linq;

namespace WidgetScreen
{
    // A time zone on the World clocks card (docs/design/world-clocks.md): a Windows time zone ID and the city name
    // the card shows for it.
    public sealed class WorldClock
    {
        public string Zone { get; set; }
        public string Label { get; set; }
        public WorldClock Copy() { return (WorldClock)MemberwiseClone(); }
    }

    // One clock at a moment: the zone's local time, its offset from UTC, and how it differs from the PC's day and time.
    public sealed class WorldClockReading
    {
        public string Label { get; set; }
        public DateTime Time { get; set; }
        public int OffsetMinutes { get; set; }
        public int DayOffset { get; set; }
        public int DifferenceMinutes { get; set; }
    }

    // World clocks from Windows' own time zones (TimeZoneInfo), so nothing is fetched and daylight saving follows
    // the zone rules Windows Update keeps current.
    public static class WorldClockService
    {
        public const int MaxClocks = 3, MaxLabel = 24;

        public static TimeZoneInfo Find(string id)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { return null; } catch (InvalidTimeZoneException) { return null; } catch (System.Security.SecurityException) { return null; }
        }

        // The first city of the zone's display name: "(UTC+09:00) Osaka, Sapporo, Tokyo" is "Osaka", "(UTC-05:00)
        // Eastern Time (US & Canada)" is "Eastern Time", and UTC is "UTC".
        public static string DefaultLabel(TimeZoneInfo zone)
        {
            if (zone == null) return "";
            if (zone.Id == "UTC" || zone.Id.StartsWith("UTC", StringComparison.Ordinal) && zone.BaseUtcOffset == TimeSpan.Zero && zone.Id.Length <= 6) return "UTC";
            string name = zone.DisplayName ?? zone.Id;
            int close = name.IndexOf(") ", StringComparison.Ordinal);
            if (name.StartsWith("(", StringComparison.Ordinal) && close > 0) name = name.Substring(close + 2);
            int comma = name.IndexOf(',');
            if (comma > 0) name = name.Substring(0, comma);
            int bracket = name.IndexOf(" (", StringComparison.Ordinal);
            if (bracket > 0) name = name.Substring(0, bracket);
            return Trim(name.Trim());
        }
        private static string Trim(string label) { return label.Length > MaxLabel ? label.Substring(0, MaxLabel).TrimEnd() : label; }

        // Up to three clocks with zones Windows knows; an empty name becomes the zone's first city.
        public static WorldClock[] Clean(IEnumerable<WorldClock> clocks)
        {
            var result = new List<WorldClock>();
            if (clocks == null) return result.ToArray();
            foreach (var clock in clocks)
            {
                if (clock == null || result.Count >= MaxClocks) continue;
                var zone = Find(clock.Zone);
                if (zone == null) continue;
                string label = (clock.Label ?? "").Trim();
                result.Add(new WorldClock { Zone = zone.Id, Label = label.Length == 0 ? DefaultLabel(zone) : Trim(label) });
            }
            return result.ToArray();
        }

        // Each clock's time now, and its day and hours against the PC's own time zone.
        public static List<WorldClockReading> Read(IEnumerable<WorldClock> clocks, DateTime utcNow, TimeZoneInfo local)
        {
            utcNow = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
            var here = TimeZoneInfo.ConvertTimeFromUtc(utcNow, local);
            int hereOffset = (int)Math.Round(local.GetUtcOffset(utcNow).TotalMinutes);
            var result = new List<WorldClockReading>();
            foreach (var clock in clocks ?? new WorldClock[0])
            {
                var zone = Find(clock.Zone);
                if (zone == null) continue;
                var time = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
                int offset = (int)Math.Round(zone.GetUtcOffset(utcNow).TotalMinutes);
                result.Add(new WorldClockReading { Label = clock.Label, Time = time, OffsetMinutes = offset, DayOffset = (time.Date - here.Date).Days, DifferenceMinutes = offset - hereOffset });
            }
            return result;
        }

        private static readonly System.Globalization.CultureInfo Invariant = System.Globalization.CultureInfo.InvariantCulture;
        public static string Time(DateTime time, bool use24) { return time.ToString(use24 ? "HH:mm" : "h:mm", Invariant); }
        public static string Period(DateTime time) { return time.ToString("tt", Invariant); }
        // "Today", "Tomorrow", "Yesterday" (or "+2 days" across the date line).
        public static string Day(int offset)
        {
            return offset == 0 ? "Today" : offset == 1 ? "Tomorrow" : offset == -1 ? "Yesterday" : (offset > 0 ? "+" : "−") + Math.Abs(offset) + " days";
        }
        // "Same time", "+3 h", "−5:30 h": the zone against the PC's clock.
        public static string Difference(int minutes)
        {
            if (minutes == 0) return "Same time";
            int size = Math.Abs(minutes);
            return (minutes > 0 ? "+" : "−") + (size / 60) + (size % 60 == 0 ? "" : ":" + (size % 60).ToString("00", Invariant)) + " h";
        }
        // "Tomorrow, Nov 2" on a large tile.
        public static string Date(WorldClockReading reading) { return Day(reading.DayOffset) + ", " + reading.Time.ToString("MMM d", Invariant); }

        // Sticker Sheet phase 3 (docs/design/sticker-sheet.md); the phone's WorldClockText has the same rules.
        // A clock's face is Lemon from 7 AM to 7 PM in its city and Lavender otherwise, labelled Day or Evening: the
        // hours people are about, not sunlight.
        public const int DayStarts = 7, DayEnds = 19, WorkStarts = 9, WorkEnds = 17;
        public static bool Daytime(DateTime time) { return time.Hour >= DayStarts && time.Hour < DayEnds; }
        public static string Ink(DateTime time) { return Daytime(time) ? "Lemon" : "Lavender"; }
        public static string PartOfDay(DateTime time) { return Daytime(time) ? "Day" : "Evening"; }
        // The hands in degrees clockwise from 12: the hour hand moves with the minutes.
        public static double HourAngle(DateTime time) { return (time.Hour % 12 + time.Minute / 60.0) * 30; }
        public static double MinuteAngle(DateTime time) { return time.Minute * 6; }
        // How far through its day a city is, 0 at midnight to 1.
        public static double DayFraction(DateTime time) { return (time.Hour * 60 + time.Minute) / 1440.0; }
    }
}
