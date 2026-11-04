package com.alialyami.widgetscreen.core;

import java.time.Instant;
import java.time.ZoneId;
import java.time.ZonedDateTime;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// What the Claude Code card says (decision 027, docs/design/claude-card.md), the same words as the Windows ClaudeText:
// the plan tiles' percentages and notes, tokens per minute, the session counts and the mascot's mood. Times are Unix
// seconds.
public final class ClaudeText {
    private ClaudeText() { }

    // The counts, in the order the card lists them: needs you (Amber), blocked (Coral), done (Mint), working (Glacier).
    public static final int NEEDS_YOU = 0, BLOCKED = 1, DONE = 2, WORKING = 3;
    public static final int[] ORDER = { NEEDS_YOU, BLOCKED, DONE, WORKING };
    public static int count(PcState.Claude c, int kind) {
        if (c == null) return 0;
        switch (kind) { case NEEDS_YOU: return c.needsYou; case BLOCKED: return c.blocked; case DONE: return c.done; default: return c.working; }
    }
    public static int total(PcState.Claude c) { return c == null ? 0 : c.done + c.needsYou + c.blocked + c.working; }
    public static String words(int kind, int n) {
        switch (kind) { case NEEDS_YOU: return n == 1 ? "needs you" : "need you"; case BLOCKED: return "blocked"; case WORKING: return "working"; default: return "done"; }
    }
    public static int color(int kind) {
        return kind == NEEDS_YOU ? Theme.WARNING : kind == BLOCKED ? Theme.INK_CORAL : kind == WORKING ? Theme.INK_GLACIER : Theme.INK_MINT;
    }
    public static String summary(PcState.Claude c) {
        if (total(c) == 0) return "No Claude Code sessions";
        List<String> parts = new ArrayList<String>();
        for (int kind : ORDER) { int n = count(c, kind); if (n > 0) parts.add(n + " " + words(kind, n)); }
        return String.join(", ", parts);
    }

    // The mascot's face: the most urgent count wins; nothing open, it sleeps.
    public static final int SLEEPING = 0, MOOD_DONE = 1, MOOD_WORKING = 2, MOOD_BLOCKED = 3, MOOD_NEEDS_YOU = 4;
    public static int mood(PcState.Claude c) {
        if (c == null) return SLEEPING;
        return c.needsYou > 0 ? MOOD_NEEDS_YOU : c.blocked > 0 ? MOOD_BLOCKED : c.working > 0 ? MOOD_WORKING : c.done > 0 ? MOOD_DONE : SLEEPING;
    }
    // The light's colour (0: the theme's muted text, asleep).
    public static int moodColor(int mood) {
        return mood == MOOD_NEEDS_YOU ? Theme.WARNING : mood == MOOD_BLOCKED ? Theme.INK_CORAL : mood == MOOD_WORKING ? Theme.INK_GLACIER : mood == MOOD_DONE ? Theme.INK_MINT : 0;
    }

    // The mascot, an original 16 × 16 pixel robot, the same rows as the Windows ClaudeMascot: '#' body (the card's
    // ink), 'v' visor, 'L' the antenna light. The eyes (three pixels wide, four tall) sit in the visor at columns 4
    // and 9 from row 4, and the chest panel (rows 12 and 13) lights up with the antenna.
    public static final String[] SPRITE = {
        "......LLLL......",
        ".......##.......",
        "..############..",
        ".##############.",
        ".##vvvvvvvvvv##.",
        "###vvvvvvvvvv###",
        "###vvvvvvvvvv###",
        ".##vvvvvvvvvv##.",
        ".##############.",
        "..############..",
        ".....######.....",
        "..############..",
        ".#.####vv####.#.",
        ".#.####vv####.#.",
        "...##########...",
        "...###....###..." };
    public static final int[] EYE_COLUMNS = { 4, 9 };
    public static final int EYE_ROW = 4, SIZE = 16;
    public static String[] eye(int mood) {
        switch (mood) {
            case MOOD_NEEDS_YOU: return new String[] { "eee", "e.e", "eee", "..." };
            case MOOD_BLOCKED: return new String[] { "e.e", ".e.", "e.e", "..." };
            case MOOD_WORKING: return new String[] { "...", ".ee", ".ee", "..." };
            case MOOD_DONE: return new String[] { "...", ".e.", "e.e", "..." };
            default: return new String[] { "...", "...", "eee", "..." };
        }
    }
    // What a pixel is: '#' body, 'v' visor, 'L' light (the antenna, the eyes and the chest panel), '.' nothing.
    public static char pixel(int mood, int row, int col) {
        char c = SPRITE[row].charAt(col);
        if (c != 'v') return c;
        if (row >= 12) return 'L';
        String[] e = eye(mood);
        for (int at : EYE_COLUMNS)
            if (col >= at && col < at + 3 && row >= EYE_ROW && row < EYE_ROW + 4 && e[row - EYE_ROW].charAt(col - at) == 'e') return 'L';
        return 'v';
    }

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
