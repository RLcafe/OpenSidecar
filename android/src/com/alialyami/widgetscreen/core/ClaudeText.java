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
    // The flag at the heading's right: "SAMPLE", and "AS OF 10:42" when the meters are old or the last check failed.
    public static String flag(PcState.Claude claude, double now, boolean use24) {
        List<String> parts = new ArrayList<String>();
        if (claude.sample) parts.add("SAMPLE");
        if (old(claude, now)) parts.add("AS OF " + clock(claude.updatedAt, use24).toUpperCase(Locale.ROOT));
        return join(parts);
    }
    public static final String SIGN_IN = "Sign in to Claude in Settings → Claude on the PC to see plan limits.", SIGN_IN_ENDED = "The PC's Claude sign-in has ended. Sign in again in Settings → Claude on the PC.";
    // The meters' empty states: the PC's own Claude sign-in is what fills them (decision 015).
    public static String limitsMissing(PcState.Claude claude) {
        if ("expired".equals(claude.plan)) return SIGN_IN_ENDED;
        if ("reading".equals(claude.plan)) return "Reading your plan's limits…";
        if ("failed".equals(claude.plan)) return "Couldn't read your plan's limits. Trying again soon.";
        if ("current".equals(claude.plan)) return claude.noPlan ? "Plan limits need a Claude Pro or Max login." : "Plan limits show after Claude Code's next reply.";
        return SIGN_IN;
    }
    // The meters show numbers that may have moved since: older than 15 minutes, or the last check didn't go through.
    public static boolean old(PcState.Claude claude, double now) {
        return claude.limitsKnown() && claude.updatedAt > 0 && (now - claude.updatedAt > STALE || "failed".equals(claude.plan) || "expired".equals(claude.plan));
    }
    // "as of 10:42 · checked online" under the Large panel; "Plan limits as of 10:42" when a check failed.
    public static String source(PcState.Claude claude, boolean use24) {
        if (!claude.limitsKnown() || claude.updatedAt <= 0) return "";
        if ("expired".equals(claude.plan)) return "as of " + clock(claude.updatedAt, use24) + " · sign in again on the PC";
        if ("failed".equals(claude.plan)) return "Plan limits as of " + clock(claude.updatedAt, use24);
        return "as of " + clock(claude.updatedAt, use24) + " · " + ("online".equals(claude.source) ? "checked online" : "from Claude Code");
    }
    // "≈ $0.42", "≈ $12", "≈ $1.2k": what the tokens would cost at API prices.
    public static String money(double dollars) {
        if (Double.isNaN(dollars) || dollars <= 0) return "≈ $0";
        if (dollars < 10) return String.format(Locale.US, "≈ $%.2f", dollars);
        if (dollars < 1000) return "≈ $" + Math.round(dollars);
        return "≈ $" + shortNumber(dollars / 1e3) + "k";
    }
    public static String threads(int count) { return count == 1 ? "1 thread" : count + " threads"; }
    public static String cars(int count) { return count == 1 ? "1 car" : count + " cars"; }
    // Under a project's name: "3 threads · 2 running · Claude app", or when its threads ended.
    public static String groupDetail(PcState.Claude claude, PcState.ClaudeGroup group, double now) {
        List<String> parts = new ArrayList<String>(); parts.add(threads(group.threads.size()));
        int running = 0; String where = null; boolean mixed = false; double last = 0;
        for (String id : group.threads) {
            PcState.ClaudeThread t = claude.thread(id); if (t == null) continue;
            last = Math.max(last, t.lastActive);
            if (!t.running) continue;
            running++;
            if (!t.where.isEmpty()) { if (where == null) where = t.where; else if (!where.equals(t.where)) mixed = true; }
        }
        if (running > 0) parts.add(running + " running");
        if (where != null && !mixed) parts.add("Desktop".equals(where) ? "Claude app" : where);
        else if (running == 0 && last > 0) parts.add("ended " + duration(now - last) + " ago");
        return join(parts);
    }
    // "3:40 pm" today, otherwise the day ("Thu").
    public static String ago(double at, double now, boolean use24) {
        java.time.ZonedDateTime local = Instant.ofEpochMilli((long) (at * 1000)).atZone(ZoneId.systemDefault()), today = Instant.ofEpochMilli((long) (now * 1000)).atZone(ZoneId.systemDefault());
        return local.toLocalDate().equals(today.toLocalDate()) ? clock(at, use24) : local.format(DateTimeFormatter.ofPattern("EEE", Locale.US));
    }
    // A baby's label: the title the Claude app (or the user) gave the thread; never its prompts.
    public static String threadTitle(PcState.ClaudeThread thread, double now, boolean use24) {
        return thread.title != null && !thread.title.trim().isEmpty() ? thread.title.trim() : "Untitled · " + ago(thread.lastActive, now, use24);
    }
    // The heading's live summary: "1 needs you · 2 running · 6 threads today".
    public static String summary(PcState.Claude claude, int period, boolean threads) {
        if (!claude.connected && claude.threads.isEmpty()) return "Not connected";
        List<String> parts = new ArrayList<String>();
        if (claude.needsYou() > 0) parts.add(claude.needsYou() + " needs you");
        if (claude.working() > 0) parts.add(claude.working() + " running");
        if (threads) {
            int count = 0; for (PcState.ClaudeGroup g : claude.groups[period]) count += g.threads.size();
            parts.add(threads(count) + (period == 0 ? " in 5 h" : period == 1 ? " today" : " this week"));
        }
        if (parts.isEmpty()) parts.add(claude.sessions.isEmpty() ? "Nothing running" : "All quiet");
        return join(parts);
    }
    // "resets 4:18 pm · in 1 h 38 m".
    public static String fiveHourReset(PcState.ClaudeWindow window, double now, boolean use24) {
        if (window == null || window.resetsAt <= 0) return "";
        if (!current(window, now)) return "reset";
        if (window.pct >= 100) return reset(window, now, use24);
        double left = window.resetsAt - now;
        String in = left >= 3600 ? (int) (left / 3600) + " h " + (int) ((left % 3600) / 60) + " m" : Math.max(1, (int) Math.ceil(left / 60)) + " m";
        return "resets " + when(window.resetsAt, now, use24) + " · in " + in;
    }
    // "resets Wed 9:00 am".
    public static String weekReset(PcState.ClaudeWindow window, boolean use24) {
        if (window == null || window.resetsAt <= 0) return "";
        java.time.ZonedDateTime local = Instant.ofEpochMilli((long) (window.resetsAt * 1000)).atZone(ZoneId.systemDefault());
        return "resets " + local.format(DateTimeFormatter.ofPattern("EEE", Locale.US)) + " " + clock(window.resetsAt, use24);
    }
    // The narrower weekly windows in one line: "Opus only 18% · Sonnet 9%".
    public static String extras(PcState.Claude claude, double now) {
        List<String> parts = new ArrayList<String>();
        for (int i = 0; i < Math.min(2, claude.extra.size()); i++) { PcState.ClaudeWindow w = claude.extra.get(i); parts.add(w.name.replace(" week", "") + (i == 0 ? " only " : " ") + Math.round(percent(w, now)) + "%"); }
        return join(parts);
    }
    public static String lap(int lap) { return lap < 0 ? "WEEK" : "WEEK · LAP " + (lap + 1) + " OF 7"; }
    public static String[] lapDays(PcState.ClaudeWindow week, double now) {
        String[] days = new String[7];
        double start = week != null && week.resetsAt > 0 ? week.resetsAt - ClaudePit.WEEK : now - 6 * 86400;
        for (int i = 0; i < 7; i++) days[i] = Instant.ofEpochMilli((long) ((start + i * 86400) * 1000)).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("EEE", Locale.US));
        return days;
    }
    // A thread's chip: what its car is doing.
    public static String chip(String pit) {
        switch (pit == null ? "" : pit) {
            case "need": return "Needs you"; case "work": return "Working"; case "rev": return "Tool run"; case "pit": return "Compacting";
            case "stuck": return "Retrying"; case "done": return "Done"; case "end": return "Ended"; default: return "Ready";
        }
    }
    // Where a thread runs and for how long in its state ("Desktop · 6 min"), or when it ended ("ended 1 h ago").
    public static String where(PcState.ClaudeThread thread, double now) {
        if (!thread.running) return "ended " + duration(now - thread.lastActive) + " ago";
        String age = duration(now - thread.since);
        return thread.where.isEmpty() ? age : thread.where + " · " + age;
    }
    public static String added(long lines) { return "+" + tokens(lines); }
    public static String removed(long lines) { return "−" + tokens(lines); }
    // The ledger's dollars without the "≈" (the column says it): "7.10", "12", "1.2k".
    public static String dollars(double value) { String money = money(value); return money.substring(money.indexOf('$') + 1); }
    public static String mix(double[] mix) {
        String[] names = { "Opus", "Sonnet", "Haiku" };
        List<String> parts = new ArrayList<String>();
        for (int i = 0; i < 3; i++) if (mix[i] > 0.005) parts.add(names[i] + " " + Math.round(mix[i] * 100) + "%");
        return parts.isEmpty() ? "No replies in this period" : join(parts);
    }
    public static String share(double share) { return Double.isNaN(share) ? "—" : Math.round(share * 100) + "%"; }
    private static String join(List<String> parts) {
        StringBuilder text = new StringBuilder();
        for (String part : parts) { if (text.length() > 0) text.append(" · "); text.append(part); }
        return text.toString();
    }
    public static String noThreads(int period) { return "No Claude Code threads " + (period == 0 ? "in the last 5 hours." : period == 1 ? "today." : "this week."); }
    // A project critter's face: the most urgent running thread's (needs, working, done, ready); asleep with none running.
    public static int rank(String state) { return "needs".equals(state) ? 0 : "working".equals(state) ? 1 : "done".equals(state) ? 2 : 3; }
    public static String groupState(PcState.Claude claude, PcState.ClaudeGroup group) {
        String best = "ready"; int rank = 9;
        for (String id : group.threads) { PcState.ClaudeThread t = claude.thread(id); if (t != null && t.running && rank(t.state) < rank) { rank = rank(t.state); best = t.state; } }
        return best;
    }
    public static boolean groupNeeds(PcState.Claude claude, PcState.ClaudeGroup group) { return "needs".equals(groupState(claude, group)); }
    // The phone asks the PC for the claude section on the PC card's interval, never more often than every 2 s.
    public static int pollEvery(PcState.Claude claude) { return Math.max(2, claude == null ? 2 : claude.every); }
    public static int period(String name) { return "FiveHour".equals(name) ? 0 : "Week".equals(name) ? 2 : 1; }

    // A project row's or session's usage: "1.8M · ≈ $2.01".
    public static String usage(long tokens, double cost) { return tokens(tokens) + " · " + money(cost); }

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
