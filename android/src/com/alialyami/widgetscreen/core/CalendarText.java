package com.alialyami.widgetscreen.core;

import java.time.LocalDate;
import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.time.temporal.ChronoUnit;
import java.util.List;
import java.util.Locale;

// The Calendar card's wording, the same as CalendarText.cs on Windows (docs/design/calendar.md). Times are moments from
// the PC, shown on the PC's clock (its UTC offset), so the phone agrees with the dashboard.
public final class CalendarText {
    private static final DateTimeFormatter TIME_12 = DateTimeFormatter.ofPattern("h:mm a", Locale.US), TIME_24 = DateTimeFormatter.ofPattern("HH:mm", Locale.US);
    private static final DateTimeFormatter DAY = DateTimeFormatter.ofPattern("EEEE, MMM d", Locale.US), SHORT = DateTimeFormatter.ofPattern("MMM d", Locale.US);

    private static double end(PcState.CalendarItem e) { return e.end > e.start ? e.end : e.start + 60000; }

    // The event to count down to: the first timed event that hasn't ended, or else the first all-day one.
    public static PcState.CalendarItem next(List<PcState.CalendarItem> events, long now) {
        for (PcState.CalendarItem e : events) if (!e.allDay && end(e) > now) return e;
        for (PcState.CalendarItem e : events) if (end(e) > now) return e;
        return null;
    }

    public static String countdown(PcState.CalendarItem e, long now) {
        if (now < e.start) return PrayerText.countdown((long) (e.start - now));
        if (e.allDay) return "today";
        long minutes = (long) Math.ceil((end(e) - now) / 60000.0);
        return "now  ·  " + (minutes >= 60 ? minutes / 60 + " h " + minutes % 60 + " m" : minutes + " m") + " left";
    }

    private static String clock(double ms, int offset, boolean use24) { return (use24 ? TIME_24 : TIME_12).format(WorldClockText.at((long) ms, offset)).toUpperCase(Locale.US); }

    public static String when(PcState.CalendarItem e, int offset, boolean use24) {
        if (e.allDay) {
            LocalDate start = date(e.date), finish = date(e.endDate);
            return start != null && finish != null && ChronoUnit.DAYS.between(start, finish) > 1 ? "All day  ·  until " + SHORT.format(finish.minusDays(1)) : "All day";
        }
        return e.end > e.start ? clock(e.start, offset, use24) + " – " + clock(e.end, offset, use24) : clock(e.start, offset, use24);
    }
    public static String start(PcState.CalendarItem e, int offset, boolean use24) { return e.allDay ? "All day" : clock(e.start, offset, use24); }

    private static LocalDate date(String text) { try { return text == null || text.isEmpty() ? null : LocalDate.parse(text); } catch (Exception ex) { return null; } }

    // "Today", "Tomorrow" or "Thursday, Nov 5", on the PC's calendar.
    public static String day(PcState.CalendarItem e, int offset, long now) {
        LocalDate today = WorldClockText.at(now, offset).toLocalDate();
        LocalDate on = e.allDay && date(e.date) != null ? date(e.date) : WorldClockText.at((long) e.start, offset).toLocalDate();
        if (on.isBefore(today)) on = today;
        long days = ChronoUnit.DAYS.between(today, on);
        return days == 0 ? "Today" : days == 1 ? "Tomorrow" : DAY.format(on);
    }

    // The heading's flag: nothing, "1 FEED FAILED", "2 FEEDS FAILED", or "OFFLINE" when none could be read.
    public static String flag(PcState.Calendar c) {
        int failed = c.failed.size();
        return failed == 0 ? "" : failed >= c.feeds ? "OFFLINE" : failed == 1 ? "1 FEED FAILED" : failed + " FEEDS FAILED";
    }
}
