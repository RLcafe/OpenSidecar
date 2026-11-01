package com.alialyami.widgetscreen.core;

import java.time.LocalDateTime;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.time.temporal.ChronoUnit;
import java.util.Locale;

// The World clocks wording, the same as WorldClockService.cs on Windows (docs/design/world-clocks.md). The PC sends each
// city's offset from UTC and its own; the phone works the times out from its clock between readings.
public final class WorldClockText {
    private static final DateTimeFormatter DATE = DateTimeFormatter.ofPattern("MMM d", Locale.US);

    public static LocalDateTime at(long utcMillis, int offsetMinutes) {
        return LocalDateTime.ofEpochSecond(Math.floorDiv(utcMillis, 1000L) + offsetMinutes * 60L, 0, ZoneOffset.UTC);
    }

    // The city's day against the PC's day: 0 today, 1 tomorrow, -1 yesterday.
    public static int dayOffset(long utcMillis, int offsetMinutes, int pcOffsetMinutes) {
        return (int) ChronoUnit.DAYS.between(at(utcMillis, pcOffsetMinutes).toLocalDate(), at(utcMillis, offsetMinutes).toLocalDate());
    }

    public static String day(int offset) {
        return offset == 0 ? "Today" : offset == 1 ? "Tomorrow" : offset == -1 ? "Yesterday" : (offset > 0 ? "+" : "−") + Math.abs(offset) + " days";
    }

    // "Same time", "+3 h", "−5:30 h": the city against the PC's clock.
    public static String difference(int minutes) {
        if (minutes == 0) return "Same time";
        int size = Math.abs(minutes);
        return (minutes > 0 ? "+" : "−") + (size / 60) + (size % 60 == 0 ? "" : String.format(Locale.US, ":%02d", size % 60)) + " h";
    }

    // "Tomorrow, Nov 2" on a large tile.
    public static String date(LocalDateTime time, int dayOffset) { return day(dayOffset) + ", " + DATE.format(time); }
}
