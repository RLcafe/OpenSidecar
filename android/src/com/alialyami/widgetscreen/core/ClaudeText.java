package com.alialyami.widgetscreen.core;

import java.time.Instant;
import java.time.ZoneId;
import java.time.format.DateTimeFormatter;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

// The Claude card's words (docs/design/claude-watcher.md), the same as ClaudeText in the Windows ClaudeCard.cs.
// Times are Unix seconds.
public final class ClaudeText {
    public static final String NOT_CONNECTED = "Connect Claude Code in Settings → Claude on the PC.", NO_SESSIONS = "No Claude Code sessions running.", NO_LOGS = "Can't read Claude Code's logs.";
    public static final String[] PERIOD_NAMES = { "5 h", "Today", "Week" };
    // Older than this, the meters say when they were read (15 minutes).
    public static final double STALE = 15 * 60;

    public static String state(String state) {
        return "needs".equals(state) ? "Needs you" : "working".equals(state) ? "Working" : "done".equals(state) ? "Done" : "Ready";
    }
    // "now", "2 min", "3 h".
    public static String duration(double seconds) {
        if (seconds < 60) return "now";
        if (seconds < 3600) return (int) (seconds / 60) + " min";
        return (int) (seconds / 3600) + " h";
    }
    // "Needs you · 2 min" always says how long; the others only when asked (the Large rows).
    public static String stateWords(String state, double since, double now, boolean withTime) {
        return "needs".equals(state) || (withTime && !"ready".equals(state) && state != null && !state.isEmpty()) ? state(state) + " · " + duration(now - since) : state(state);
    }
    // "3:40 pm" within a day, otherwise the day ("Thu").
    public static String when(double resetsAt, double now, boolean use24) {
        java.time.ZonedDateTime local = Instant.ofEpochMilli((long) (resetsAt * 1000)).atZone(ZoneId.systemDefault());
        if (resetsAt - now < 24 * 3600) return local.format(DateTimeFormatter.ofPattern(use24 ? "HH:mm" : "h:mm a", Locale.US)).toLowerCase(Locale.ROOT);
        return local.format(DateTimeFormatter.ofPattern("EEE", Locale.US));
    }
    public static String clock(double time, boolean use24) {
        return Instant.ofEpochMilli((long) (time * 1000)).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern(use24 ? "HH:mm" : "h:mm a", Locale.US)).toLowerCase(Locale.ROOT);
    }
    // A window whose reset time has passed starts again at nothing.
    public static boolean current(PcState.ClaudeWindow window, double now) { return window != null && (window.resetsAt <= 0 || window.resetsAt > now); }
    public static double percent(PcState.ClaudeWindow window, double now) { return current(window, now) ? Math.max(0, Math.min(100, window.pct)) : 0; }
    // Under the meter: "resets 3:40 pm", or "Limit reached · back 3:40 pm" at 100%.
    public static String reset(PcState.ClaudeWindow window, double now, boolean use24) {
        if (window == null) return "";
        if (!current(window, now)) return "reset";
        String when = window.resetsAt > 0 ? when(window.resetsAt, now, use24) : "";
        if (window.pct >= 100) return when.isEmpty() ? "Limit reached" : "Limit reached · back " + when;
        return when.isEmpty() ? "" : "resets " + when;
    }
    // A meter turns Amber from 80%.
    public static boolean warns(double percent) { return percent >= 80; }
    // Segments lit in a ten-segment meter: any use lights the first, and a full one only at 100%.
    public static int lit(double percent) { if (Double.isNaN(percent) || percent <= 0) return 0; return Math.max(1, Math.min(10, (int) Math.floor(percent / 10 + 0.0001))); }

    // The hook's message, shorter: "Claude needs your permission to use Bash" reads "Wants to use Bash".
    public static String ask(String message) {
        String permission = "Claude needs your permission to use ";
        if (message == null || message.trim().isEmpty()) return "";
        message = message.trim();
        return message.toLowerCase(Locale.ROOT).startsWith(permission.toLowerCase(Locale.ROOT)) && message.length() > permission.length() ? "Wants to use " + message.substring(permission.length()) : message;
    }
    // The heading: "CLAUDE CODE", "api-server needs you" or "2 sessions need you".
    public static String heading(PcState.Claude claude) {
        List<PcState.ClaudeSession> needs = new ArrayList<PcState.ClaudeSession>();
        for (PcState.ClaudeSession s : claude.sessions) if ("needs".equals(s.state)) needs.add(s);
        return needs.isEmpty() ? "CLAUDE CODE" : needs.size() == 1 ? needs.get(0).project + " needs you" : needs.size() + " sessions need you";
    }
    // The flag at the heading's right: "SAMPLE · 3 RUNNING", and "AS OF 10:42" when the meters are old (Medium).
    public static String flag(PcState.Claude claude, double now, boolean use24, boolean withAge) {
        List<String> parts = new ArrayList<String>();
        if (claude.sample) parts.add("SAMPLE");
        if (!claude.sessions.isEmpty()) parts.add(claude.sessions.size() + " RUNNING");
        if (withAge && claude.limitsKnown() && claude.updatedAt > 0 && now - claude.updatedAt > STALE) parts.add("AS OF " + clock(claude.updatedAt, use24).toUpperCase(Locale.ROOT));
        StringBuilder text = new StringBuilder();
        for (String part : parts) { if (text.length() > 0) text.append(" · "); text.append(part); }
        return text.toString();
    }
    public static String limitsMissing(PcState.Claude claude) {
        return claude.noPlan ? "Plan limits need a Claude Pro or Max login." : "Plan limits show after Claude Code's next reply.";
    }
    // "as of 10:42 · from Claude Code" under the Large panel.
    public static String source(PcState.Claude claude, boolean use24) {
        if (!claude.limitsKnown() || claude.updatedAt <= 0) return "";
        return "as of " + clock(claude.updatedAt, use24) + " · " + ("online".equals(claude.source) ? "checked online" : "from Claude Code");
    }

    // 950, 12.4k, 1.2M, 3.4B.
    public static String tokens(long tokens) {
        if (tokens < 1000) return String.valueOf(Math.max(0, tokens));
        if (tokens < 1000000) return shortNumber(tokens / 1e3) + "k";
        if (tokens < 1000000000) return shortNumber(tokens / 1e6) + "M";
        return shortNumber(tokens / 1e9) + "B";
    }
    private static String shortNumber(double value) {
        if (value >= 100) return String.valueOf((long) Math.floor(value));
        double tenth = Math.floor(value * 10) / 10;
        return tenth == Math.floor(tenth) ? String.valueOf((long) tenth) : String.format(Locale.US, "%.1f", tenth);
    }
}
