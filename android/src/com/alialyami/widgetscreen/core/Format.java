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

    // "3d 4h 12m"; parts 2 drops the minutes, as SystemStatsCard.Uptime on Windows.
    public static String uptime(long ms, int parts) {
        long minutes = Math.max(0, ms) / 60000;
        String text = (minutes / 1440) + "d " + (minutes / 60 % 24) + "h";
        return parts >= 3 ? text + " " + (minutes % 60) + "m" : text;
    }
    public static String uptime(long ms) {
        long minutes = Math.max(0, ms) / 60000;
        return (minutes / 1440) + "d " + (minutes / 60 % 24) + "h " + (minutes % 60) + "m";
    }

    public static String gigabytes(long bytes) { return String.format(Locale.US, "%.1f", bytes / 1073741824.0); }

    public static String percent(double value) { return Math.round(value) + "%"; }

    // The Sticker Sheet's Focus facts (docs/design/sticker-sheet.md, phase 2), the same as the PC's FocusTimer.
    public static String focusMinutes(int minutes) {
        minutes = Math.max(0, minutes);
        return minutes >= 60 ? (minutes / 60) + " h " + (minutes % 60) + " min" : minutes + " min";
    }
    // Next break: when a running session ends, otherwise how long the next session is.
    public static String focusBreak(boolean running, long remainingMs, long durationMs, LocalDateTime now, boolean use24) {
        if (running && remainingMs > 0) return endsAt(now.plusNanos(remainingMs * 1000000L), use24);
        long left = remainingMs > 0 && remainingMs < durationMs ? remainingMs : durationMs;
        return "In " + Math.max(1, (long) Math.ceil(left / 60000.0)) + " min";
    }
    // "Speakers (Logi Z407)" reads "Z407" on a mixer strip (as on the PC): the part in brackets, or its last word.
    public static String shortDevice(String name) {
        if (name == null || name.trim().isEmpty()) return "";
        int open = name.lastIndexOf('('), close = name.lastIndexOf(')');
        String inner = open >= 0 && close > open ? name.substring(open + 1, close).trim() : "";
        if (inner.length() < 3) inner = name.trim();
        String[] words = inner.split(" +");
        String last = words.length == 0 ? inner : words[words.length - 1];
        return last.length() >= 3 ? last : inner;
    }
    // A hotkey button's keys for its keycap legend, from the PC's "Hotkey  ·  Ctrl+Shift+M"; null for other buttons.
    public static String hotkeyLegend(String kind) {
        if (kind == null || !kind.startsWith("Hotkey")) return null;
        int dot = kind.indexOf('·');
        String keys = dot < 0 ? "" : kind.substring(dot + 1).trim();
        return keys.isEmpty() ? null : keys.toUpperCase(Locale.ROOT);
    }
    public static String focusState(boolean running, boolean finished, boolean paused, int sessionsToday) {
        if (finished) return "DONE";
        if (running) return "FOCUSING";
        if (paused) return "PAUSED";
        return sessionsToday > 0 ? sessionsToday + " TODAY" : "";
    }
}
