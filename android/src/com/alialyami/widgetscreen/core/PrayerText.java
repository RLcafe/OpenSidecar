package com.alialyami.widgetscreen.core;

import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.Locale;

// Prayer times wording, the same as PrayerService.cs on Windows (docs/design/prayer-times.md). The PC works the times
// out and sends them as moments; the phone shows them in the PC's time zone and counts down with its own clock.
public final class PrayerText {
    public static final String[] NAMES = { "Fajr", "Dhuhr", "Asr", "Maghrib", "Isha" };
    // A prayer that began in the last 20 minutes is shown as now.
    public static final long NOW_FOR_MS = 20 * 60000L;
    private static final DateTimeFormatter TIME_12 = DateTimeFormatter.ofPattern("h:mm a", Locale.US);
    private static final DateTimeFormatter TIME_24 = DateTimeFormatter.ofPattern("HH:mm", Locale.US);

    public static String name(int index) { return index >= 0 && index < NAMES.length ? NAMES[index] : ""; }

    // "in 1 h 12 m", "in 12 m", "in under a minute".
    public static String countdown(long leftMs) {
        if (leftMs < 60000) return "in under a minute";
        long minutes = (leftMs + 59999) / 60000;
        return minutes >= 60 ? "in " + minutes / 60 + " h " + minutes % 60 + " m" : "in " + minutes + " m";
    }

    // A moment shown on the PC's clock.
    public static String time(double epochMs, int pcOffsetMinutes, boolean use24) {
        if (epochMs <= 0) return "—";
        LocalDateTime at = WorldClockText.at((long) epochMs, pcOffsetMinutes);
        return (use24 ? TIME_24 : TIME_12).format(at).toUpperCase(Locale.US);
    }

    public static double progress(long now, double previousAt, double nextAt) {
        double span = Math.max(60000, nextAt - previousAt);
        return Math.max(0, Math.min(1, (now - previousAt) / span));
    }

    // Sticker Sheet phase 3, the same rules as PrayerService on the PC. Where a moment sits on the day's sun path: 0 at
    // Fajr, 1 at Isha, outside that range before or after them (NaN without both).
    public static double pathPart(double at, double fajr, double isha) { return fajr <= 0 || isha <= fajr ? Double.NaN : (at - fajr) / (isha - fajr); }
    // A method's name for a chip: "Umm al-Qura, Makkah" is "Umm al-Qura".
    public static String methodShort(String name) { if (name == null) return ""; int comma = name.indexOf(','); return comma > 0 ? name.substring(0, comma).trim() : name; }

    public static boolean justStarted(long now, double previousAt) { return previousAt > 0 && now - previousAt >= 0 && now - previousAt < NOW_FOR_MS; }
}
