package com.alialyami.widgetscreen.core;

import java.time.Instant;
import java.time.ZoneId;
import java.time.ZonedDateTime;
import java.time.format.DateTimeFormatter;
import java.util.Locale;

// What the Claude Code card says (decisions 027 and 028, docs/design/claude-card.md), the same words as the Windows
// ClaudeText and ClaudePace: the plan tiles' percentages, notes and pace, and tokens per minute. Times are Unix seconds.
public final class ClaudeText {
    private ClaudeText() { }

    // A plan window's use now: 0 once its reset has passed, NaN without one.
    public static double percent(PcState.ClaudeWindow w, double now) {
        if (w == null || Double.isNaN(w.pct)) return Double.NaN;
        if (w.resetsAt > 0 && w.resetsAt <= now) return 0;
        return Math.max(0, Math.min(100, w.pct));
    }
    private static boolean hasLimits(PcState.Claude c) { return c != null && c.updatedAt > 0; }
    public static String percentText(PcState.Claude c, boolean week, double now) {
        if (!hasLimits(c)) return c != null && "reading".equals(c.plan) ? "…" : "—";
        double pct = percent(week ? c.week : c.fiveHour, now);
        return Double.isNaN(pct) ? "—" : Math.round(pct) + "%";
    }
    public static String planNote(PcState.Claude c, boolean week, double now, boolean use24, ZoneId zone) {
        String plan = c == null ? "signedOut" : c.plan;
        switch (plan) {
            case "signedOut": return "Sign in on the PC: Settings → Claude";
            case "expired": return "Sign in again on the PC";
            case "noMeters": return "No limits on this plan";
            case "reading": return "Checking…";
        }
        if (!hasLimits(c)) return "Couldn't check";
        if ("failed".equals(plan)) return "As of " + clock(c.updatedAt, use24, zone);
        PcState.ClaudeWindow w = week ? c.week : c.fiveHour;
        if (w == null) return week ? "No weekly limit" : "No 5-hour limit";
        if (w.resetsAt <= 0) return "";
        if (w.resetsAt <= now) return "Reset";
        return "Resets " + when(w.resetsAt, now, use24, zone);
    }
    public static boolean stale(PcState.Claude c) { return c != null && ("failed".equals(c.plan) || "expired".equals(c.plan)); }

    // The instruments' arithmetic (decision 028), the same as ClaudePace on the PC: how much of a window has passed
    // (the gauge's pace tick and the week's laps), where it ends at this rate, the lap the week is on, and the tokens
    // gauge's scale. Times are Unix seconds.
    public static final double FIVE_HOURS = 5 * 3600, WEEK = 7 * 86400, REDLINE = 0.75, MIN_SCALE = 10000;
    public static final int LAPS = 7;
    public static double elapsed(PcState.ClaudeWindow w, double length, double now) {
        if (w == null || w.resetsAt <= 0 || w.resetsAt <= now) return Double.NaN;
        return Math.max(0, Math.min(1, 1 - (w.resetsAt - now) / length));
    }
    public static double projection(double percent, double elapsed) {
        if (Double.isNaN(percent) || Double.isNaN(elapsed) || elapsed < 0.05) return Double.NaN;
        return Math.min(999, percent / elapsed);
    }
    public static boolean warns(double projection) { return !Double.isNaN(projection) && projection >= 90; }
    public static double lap(double percent, int lap) { return Double.isNaN(percent) ? 0 : Math.max(0, Math.min(1, (percent / 100 - lap / 7.0) * 7)); }
    public static int lapNow(double elapsed) { return Double.isNaN(elapsed) ? -1 : Math.min(LAPS - 1, (int) Math.floor(elapsed * LAPS)); }
    public static double scale(double peak) {
        if (Double.isNaN(peak) || peak <= MIN_SCALE) return MIN_SCALE;
        double power = Math.pow(10, Math.floor(Math.log10(peak)));
        for (double step : new double[] { 1, 1.5, 2, 3, 4, 6, 8, 10 }) if (step * power >= peak) return step * power;
        return 10 * power;
    }
    public static double peak(double[] minutes) { double most = 0; if (minutes != null) for (double v : minutes) most = Math.max(most, v); return most; }
    // "on pace for 92%", or nothing early in a window; "LAP 5 OF 7".
    public static String pace(double projection) { return Double.isNaN(projection) ? "" : "on pace for " + Math.round(projection) + "%"; }
    public static String lapText(int lap) { return lap < 0 ? "" : "LAP " + (lap + 1) + " OF 7"; }
    // The day names under the laps, from the window's start (the week before the reset), or the week to today.
    public static String[] lapDays(PcState.ClaudeWindow week, double now, ZoneId zone) {
        String[] days = new String[LAPS];
        double start = week != null && week.resetsAt > 0 ? week.resetsAt - WEEK : now - 6 * 86400;
        for (int i = 0; i < LAPS; i++)
            days[i] = Instant.ofEpochMilli((long) ((start + i * 86400) * 1000)).atZone(zone).format(DateTimeFormatter.ofPattern("EEE", Locale.US));
        return days;
    }

    // Tokens per minute: the average of the last five minutes (the one in progress included), as on the PC.
    public static final int RATE_MINUTES = 5;
    public static double rate(double[] minutes) {
        if (minutes == null || minutes.length == 0) return 0;
        int take = Math.min(RATE_MINUTES, minutes.length); double sum = 0;
        for (int i = minutes.length - take; i < minutes.length; i++) sum += minutes[i];
        return sum / take;
    }
    public static String tokensValue(PcState.Claude c) { return c == null || !c.logsRead ? "…" : !c.logs ? "—" : tokens(rate(c.minutes)); }
    public static String tokensNote(PcState.Claude c) { return c == null || !c.logsRead ? "Reading Claude Code's logs…" : !c.logs ? "No Claude Code logs on the PC" : "Last " + RATE_MINUTES + " minutes"; }
    // "940", "9.4k", "12k", "1.2M" (ClaudeTokens.Format on the PC).
    public static String tokens(double n) {
        if (Double.isNaN(n) || n < 0) return "—";
        if (n < 1000) return String.valueOf(Math.round(n));
        if (n < 1e6) return (n < 1e4 ? String.format(Locale.US, "%.1f", Math.floor(n / 100) / 10) : String.valueOf(Math.round(n / 1000))) + "k";
        return (n < 1e7 ? String.format(Locale.US, "%.1f", Math.floor(n / 1e5) / 10) : String.valueOf(Math.round(n / 1e6))) + "M";
    }

    // "3:40 PM" today, "Thu 9 AM" another day (24-hour: "15:40", "Thu 09:00").
    public static String when(double at, double now, boolean use24, ZoneId zone) {
        ZonedDateTime local = Instant.ofEpochMilli((long) (at * 1000)).atZone(zone), today = Instant.ofEpochMilli((long) (now * 1000)).atZone(zone);
        String day = local.toLocalDate().equals(today.toLocalDate()) ? "" : local.format(DateTimeFormatter.ofPattern("EEE", Locale.US)) + " ";
        return day + clock(at, use24, zone);
    }
    public static String clock(double at, boolean use24, ZoneId zone) {
        ZonedDateTime local = Instant.ofEpochMilli((long) (at * 1000)).atZone(zone);
        return local.format(DateTimeFormatter.ofPattern(use24 ? "HH:mm" : local.getMinute() == 0 ? "h a" : "h:mm a", Locale.US));
    }
}
