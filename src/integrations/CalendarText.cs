using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WidgetScreen
{
    // The Calendar card's wording (docs/design/calendar.md), the same on the phone (CalendarText.java).
    public static class CalendarText
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        // The event to count down to: the first timed event that hasn't ended, or else the first all-day one.
        public static CalendarEvent Next(IList<CalendarEvent> events, DateTime utcNow)
        {
            return events.FirstOrDefault(e => !e.AllDay && End(e) > utcNow) ?? events.FirstOrDefault(e => End(e) > utcNow);
        }
        private static DateTime End(CalendarEvent e) { return e.EndUtc > e.StartUtc ? e.EndUtc : e.StartUtc.AddMinutes(1); }

        // "in 1 h 30 m", "in under a minute", "now · 45 m left", or "today" for an all-day event under way.
        public static string Countdown(CalendarEvent e, DateTime utcNow)
        {
            if (utcNow < e.StartUtc) return PrayerService.Countdown(e.StartUtc - utcNow);
            if (e.AllDay) return "today";
            var left = End(e) - utcNow;
            int minutes = (int)Math.Ceiling(left.TotalMinutes);
            return "now  ·  " + (minutes >= 60 ? minutes / 60 + " h " + minutes % 60 + " m" : minutes + " m") + " left";
        }

        // "2:30 PM – 3:30 PM", "All day", or "All day · until Nov 5" across several days.
        public static string When(CalendarEvent e, TimeZoneInfo local, bool use24)
        {
            if (e.AllDay) return (e.EndDate - e.Date).TotalDays > 1 ? "All day  ·  until " + e.EndDate.AddDays(-1).ToString("MMM d", Invariant) : "All day";
            string format = use24 ? "HH:mm" : "h:mm tt";
            string start = TimeZoneInfo.ConvertTimeFromUtc(e.StartUtc, local).ToString(format, Invariant);
            return e.EndUtc > e.StartUtc ? start + " – " + TimeZoneInfo.ConvertTimeFromUtc(e.EndUtc, local).ToString(format, Invariant) : start;
        }
        public static string Start(CalendarEvent e, TimeZoneInfo local, bool use24) { return e.AllDay ? "All day" : TimeZoneInfo.ConvertTimeFromUtc(e.StartUtc, local).ToString(use24 ? "HH:mm" : "h:mm tt", Invariant); }

        // "Today", "Tomorrow", or "Thursday, Nov 5" for the day a moment falls on in the PC's zone.
        public static string Day(DateTime utc, TimeZoneInfo local, DateTime utcNow) { return Day(TimeZoneInfo.ConvertTimeFromUtc(utc, local).Date, TimeZoneInfo.ConvertTimeFromUtc(utcNow, local).Date); }
        public static string Day(CalendarEvent e, TimeZoneInfo local, DateTime utcNow)
        {
            var today = TimeZoneInfo.ConvertTimeFromUtc(utcNow, local).Date;
            if (e.AllDay) return Day(e.Date < today && e.EndDate > today ? today : e.Date, today);
            var start = TimeZoneInfo.ConvertTimeFromUtc(e.StartUtc, local).Date;
            return Day(start < today ? today : start, today);
        }
        private static string Day(DateTime date, DateTime today)
        {
            int days = (date - today).Days;
            return days == 0 ? "Today" : days == 1 ? "Tomorrow" : date.ToString("dddd, MMM d", Invariant);
        }
    }
}
