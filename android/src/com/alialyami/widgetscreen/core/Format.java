package com.alialyami.widgetscreen.core;

import java.time.LocalDateTime;
import java.time.format.DateTimeFormatter;
import java.util.Locale;

// Text formats shared with the Windows app (invariant culture there, US English here).
public final class Format {
    private static final DateTimeFormatter TIME_12 = DateTimeFormatter.ofPattern("h:mm", Locale.US);
    private static final DateTimeFormatter TIME_24 = DateTimeFormatter.ofPattern("HH:mm", Locale.US);
    private static final DateTimeFormatter PERIOD = DateTimeFormatter.ofPattern("a", Locale.US);
    private static final DateTimeFormatter DAY = DateTimeFormatter.ofPattern("EEEE", Locale.US);
    private static final DateTimeFormatter DATE = DateTimeFormatter.ofPattern("MMMM d", Locale.US);
    private static final DateTimeFormatter ENDS_12 = DateTimeFormatter.ofPattern("h:mm a", Locale.US);

    public static String clockTime(LocalDateTime now, boolean use24) { return (use24 ? TIME_24 : TIME_12).format(now); }
    public static String clockPeriod(LocalDateTime now) { return PERIOD.format(now).toUpperCase(Locale.US); }
    public static String clockDay(LocalDateTime now) { return DAY.format(now).toUpperCase(Locale.US); }
    public static String clockDate(LocalDateTime now) { return DATE.format(now); }
    public static String endsAt(LocalDateTime when, boolean use24) { return use24 ? TIME_24.format(when) : ENDS_12.format(when).toUpperCase(Locale.US); }

    // Focus time rounds up to the next whole second, so "25:00" shows until a full second has passed.
    public static String focus(long remainingMs) {
        long seconds = (remainingMs + 999) / 1000;
        return String.format(Locale.US, "%02d:%02d", seconds / 60, seconds % 60);
    }

    public static String focusStatus(long remainingMs, boolean running, double progress, String ends) {
        return remainingMs == 0 ? "Done. Take a break." : running ? "Ends at " + ends : progress > 0 ? "Paused" : "";
    }

    // "3 sessions today · 1 h 40 m", or nothing before the first session (FocusTimer.SessionsText on Windows).
    public static String sessions(int sessions, int minutes) {
        if (sessions <= 0) return "";
        String time = minutes >= 60 ? (minutes / 60) + " h " + (minutes % 60) + " m" : minutes + " m";
        return sessions + (sessions == 1 ? " session today  ·  " : " sessions today  ·  ") + time;
    }

    public static String focusButton(long remainingMs, boolean running, double progress) {
        return running ? "Pause" : progress > 0 && remainingMs > 0 ? "Resume" : "Start focus";
    }

    public static String mediaTime(long ms) {
        long seconds = Math.max(0, ms) / 1000;
        return (seconds / 60) + ":" + String.format(Locale.US, "%02d", seconds % 60);
    }

    public static String rate(double bytesPerSecond) {
        if (bytesPerSecond >= 1048576) return String.format(Locale.US, "%.1f MB/s", bytesPerSecond / 1048576);
        return String.format(Locale.US, "%.0f KB/s", bytesPerSecond / 1024);
    }

    public static String uptime(long ms) {
        long minutes = Math.max(0, ms) / 60000;
        return (minutes / 1440) + "d " + (minutes / 60 % 24) + "h " + (minutes % 60) + "m";
    }

    public static String gigabytes(long bytes) { return String.format(Locale.US, "%.1f", bytes / 1073741824.0); }

    public static String percent(double value) { return Math.round(value) + "%"; }
}
