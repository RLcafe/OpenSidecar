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
    // The plan reports no 5-hour window (only weekly ones), as Anthropic's usage answer can.
    public static final String NO_FIVE_HOUR = "No 5-hour limit on your plan right now";
    public static final String NOT_CONNECTED = "Connect Claude Code in Settings → Claude on the PC.", NO_SESSIONS = "No Claude Code sessions running.", NO_LOGS = "Can't read Claude Code's logs.";
    public static final String[] PERIOD_NAMES = { "5 h", "Today", "Week" };
    // Older than this, the meters say when they were read (15 minutes).
    public static final double STALE = 15 * 60;

    public static String state(String state) {
        return "needs".equals(state) ? "Needs you" : "working".equals(state) ? "Working" : "done".equals(state) ? "Your turn" : "stuck".equals(state) ? "Stuck" : "Ready";
    }
    // Why a thread is stuck (decision 023): the PC's words ("Rate limited, resets 14:05"), else the error's own.
    public static String stuckReason(PcState.ClaudeThread thread) {
        if (thread == null) return "";
        if (!thread.reasonText.isEmpty()) return thread.reasonText;
        switch (thread.reason) {
            case "rate_limit": return "Rate limited";
            case "overloaded": return "Overloaded, try again";
            case "authentication_failed": case "oauth_org_not_allowed": return "Sign in again";
            case "cloud_credential_error": return "Cloud credentials failed";
            case "account_on_hold": return "Account on hold";
            case "billing_error": return "Billing problem";
            case "invalid_request": return "Request refused";
            case "model_not_found": return "Model not available";
            case "server_error": return "Server error, try again";
            case "max_output_tokens": return "Reply too long";
            case "quota_disabled": return "Stopped waiting for the usage limit";
            default: return "API error";
        }
    }
    public static boolean stuck(PcState.ClaudeThread thread) { return thread != null && thread.running && "stuck".equals(thread.state); }
    // The car's context ring (decision 023), the same as the PC's: 0 none (under 60% or unknown), 1 from 60%, 2 from
    // 80% (compaction soon), 3 from 95%.
    public static int contextLevel(double percent) { return Double.isNaN(percent) || percent < 60 ? 0 : percent < 80 ? 1 : percent < 95 ? 2 : 3; }
    public static String contextTip(double percent) {
        int level = contextLevel(percent);
        if (level == 0) return "";
        String used = "Context " + Math.round(percent) + "% used";
        return level == 1 ? used : level == 2 ? used + ", compaction soon" : used + ", compaction next";
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
    public static String flag(PcState.Claude claude, double now, boolean use24) { return flag(claude, now, use24, false); }
    // Large always says how fresh the plan numbers are, like the PC (review fix 10).
    public static String flag(PcState.Claude claude, double now, boolean use24, boolean fresh) {
        List<String> parts = new ArrayList<String>();
        if (claude.sample) parts.add("SAMPLE");
        if (old(claude, now)) parts.add("AS OF " + clock(claude.updatedAt, use24).toUpperCase(Locale.ROOT));
        else if (fresh && claude.limitsKnown()) parts.add("LIVE · AS OF " + clock(claude.updatedAt, use24).toUpperCase(Locale.ROOT));
        return join(parts);
    }
    public static final String SIGN_IN = "Sign in to Claude in Settings → Claude on the PC to see plan limits.", SIGN_IN_ENDED = "The PC's Claude sign-in has ended. Sign in again in Settings → Claude on the PC.";
    public static final String NO_METERS = "Your plan has no usage meters.";
    // The meters' empty states: the PC's own Claude sign-in is what fills them (decision 015).
    public static String limitsMissing(PcState.Claude claude) {
        if ("expired".equals(claude.plan)) return SIGN_IN_ENDED;
        if ("noMeters".equals(claude.plan)) return NO_METERS;
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
    public static String groupDetail(PcState.Claude claude, PcState.ClaudeGroup group, double now) { return groupDetail(claude, group, now, false); }
    // Compact (Medium) leaves out where it runs, so "+N hidden" still fits (review fix 3).
    public static String groupDetail(PcState.Claude claude, PcState.ClaudeGroup group, double now, boolean compact) {
        // A project with no thread in the period: when it was last used.
        // Threads Settings → Claude hides on the PC (archived, or idle when asked) are counted (decision 020).
        if (group.threads.isEmpty()) return (group.hidden > 0 ? group.hidden + " hidden · " : "") + (group.lastActive > 0 ? "Quiet · last used " + Instant.ofEpochMilli((long) (group.lastActive * 1000)).atZone(ZoneId.systemDefault()).format(DateTimeFormatter.ofPattern("MMM d", Locale.US)) : "Quiet");
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
        if (where != null && !mixed) { if (!compact) parts.add("Desktop".equals(where) ? "Claude app" : where); }
        else if (running == 0 && last > 0) parts.add("ended " + duration(now - last) + " ago");
        if (group.hidden > 0) parts.add("+" + group.hidden + " hidden");
        return join(parts);
    }
    // "3:40 pm" today, otherwise the day ("Thu").
    public static String ago(double at, double now, boolean use24) {
        java.time.ZonedDateTime local = Instant.ofEpochMilli((long) (at * 1000)).atZone(ZoneId.systemDefault()), today = Instant.ofEpochMilli((long) (now * 1000)).atZone(ZoneId.systemDefault());
        return local.toLocalDate().equals(today.toLocalDate()) ? clock(at, use24) : local.format(DateTimeFormatter.ofPattern("EEE", Locale.US));
    }
    // A thread's name (decision 018): the one the PC picked (a rename, a generated title or the first line of its first
    // request), else a label from its id. Placeholders such as "Remote Control session (from a project)" never show.
    public static String threadTitle(PcState.ClaudeThread thread, double now, boolean use24) {
        return !placeholder(thread.title) ? thread.title.trim() : idLabel(thread.id);
    }
    private static final java.util.regex.Pattern GENERIC = java.util.regex.Pattern.compile("^(remote control|new|untitled|claude code|claude|desktop|vs code|terminal|session|thread)( (session|chat|thread|conversation))?( ?\\(.*\\))?$");
    // An empty title, or one that names the kind of thread rather than the thread (the same rule as the PC's).
    public static boolean placeholder(String title) {
        if (title == null || title.trim().isEmpty()) return true;
        return GENERIC.matcher(title.trim().toLowerCase(Locale.ROOT).replaceAll("\\s+", " ")).matches();
    }
    // The last resort: "Thread 1b2187".
    public static String idLabel(String id) {
        StringBuilder letters = new StringBuilder();
        for (char c : (id == null ? "" : id).toCharArray()) if (Character.isLetterOrDigit(c) && letters.length() < 6) letters.append(Character.toLowerCase(c));
        return letters.length() == 0 ? "Thread" : "Thread " + letters;
    }
    // The heading's live summary: "1 needs you · 2 running · 6 threads today".
    public static String summary(PcState.Claude claude, int period, boolean threads) {
        if (!claude.connected && claude.threads.isEmpty()) return "Not connected";
        List<String> parts = new ArrayList<String>();
        if (claude.needsYou() > 0) parts.add(claude.needsYou() + " needs you");
        if (claude.stuck() > 0) parts.add(claude.stuck() + " stuck");
        int turn = 0; for (PcState.ClaudeThread t : claude.threads) if (t.running && "done".equals(t.pit)) turn++;
        if (turn > 0) parts.add(turn + " your turn");
        if (claude.working() > 0) parts.add(claude.working() + " running");
        if (threads) {
            int count = 0; for (PcState.ClaudeGroup g : claude.groups[period]) count += g.threads.size();
            parts.add(threads(count) + (period == 0 ? " in 5 h" : period == 1 ? " today" : " this week"));
        }
        if (parts.isEmpty()) parts.add(claude.sessions.isEmpty() ? "Nothing running" : "All quiet");
        // Medium and Small keep the two most urgent parts, like the PC (review fix 3).
        if (!threads && parts.size() > 2) parts = new ArrayList<String>(parts.subList(0, 2));
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
    // Archived or resolved: under the cover, dimmed the most, last in its project (decision 020).
    public static boolean parked(String pit) { return "archived".equals(pit) || "resolved".equals(pit); }
    // The tools fact (review fix 2), like the PC's: tool calls from the logs, "asked" from telemetry, partial when
    // telemetry saw fewer of them.
    public static String toolsKey(PcState.ClaudeFacts f) { return !f.telemetry ? "TOOLS" : "TOOLS · " + f.asked + " ASKED" + (f.askedPartial ? " · PARTIAL" : ""); }
    // A thread's chip: what its car is doing.
    public static String chip(String pit) {
        switch (pit == null ? "" : pit) {
            case "need": return "Needs you"; case "work": return "Working"; case "rev": return "Tool run"; case "pit": return "Compacting";
            case "stuck": return "Retrying"; case "done": return "Your turn"; case "end": return "Ended"; case "archived": return "Archived"; case "resolved": return "Resolved"; default: return "Idle";
        }
    }
    // A thread's chip word, like the PC's: a lasting Stuck says so; a working car spinning after an API error is retrying.
    public static String chip(PcState.ClaudeThread thread) {
        return thread != null && "stuck".equals(thread.pit) && "stuck".equals(thread.state) ? "Stuck" : chip(thread == null ? "" : thread.pit);
    }
    // Where a thread runs and for how long in its state ("Desktop · 6 min"), or when it ended ("ended 1 h ago").
    public static String where(PcState.ClaudeThread thread, double now) {
        if (!thread.running) return (thread.where.isEmpty() ? "" : thread.where + " · ") + "ended " + duration(now - thread.lastActive) + " ago";
        String age = duration(now - thread.since);
        return thread.where.isEmpty() ? age : thread.where + " · " + age;
    }
    // The Models column, the same as the PC's: one model by name ("Opus 4.8"); a mix as its top two with shares
    // ("Opus 70% · Sonnet 30%"). Each part is { words, model }.
    public static List<String[]> modelLabel(List<PcState.ClaudeModel> models) {
        List<String[]> parts = new ArrayList<String[]>();
        if (models == null || models.isEmpty()) return parts;
        if (models.get(0).share >= 0.95) { parts.add(new String[] { models.get(0).name, models.get(0).name }); return parts; }
        for (int i = 0; i < Math.min(2, models.size()); i++) { PcState.ClaudeModel m = models.get(i); parts.add(new String[] { familyName(m.name) + " " + Math.round(m.share * 100) + "%", m.name }); }
        return parts;
    }
    // 0 Opus (and the bigger Fable and Mythos), 1 Sonnet, 2 Haiku, -1 another name.
    public static int family(String model) {
        String name = model == null ? "" : model.toLowerCase(Locale.ROOT);
        return name.contains("haiku") ? 2 : name.contains("sonnet") ? 1 : name.contains("opus") || name.contains("fable") || name.contains("mythos") ? 0 : -1;
    }
    private static String familyName(String model) { int f = family(model); return f == 0 ? model.split(" ")[0] : f == 1 ? "Sonnet" : f == 2 ? "Haiku" : model; }
    public static String replies(int count) { return count == 1 ? "1 reply" : count + " replies"; }
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
    public static int rank(String state) { return "needs".equals(state) ? 0 : "stuck".equals(state) ? 1 : "working".equals(state) ? 2 : "done".equals(state) ? 3 : 4; }
    public static String groupState(PcState.Claude claude, PcState.ClaudeGroup group) {
        String best = "ready"; int rank = 9;
        for (String id : group.threads) { PcState.ClaudeThread t = claude.thread(id); if (t != null && t.running && rank(t.state) < rank) { rank = rank(t.state); best = t.state; } }
        return best;
    }
    public static boolean groupNeeds(PcState.Claude claude, PcState.ClaudeGroup group) { return "needs".equals(groupState(claude, group)); }
    // The phone asks the PC for the claude section on the PC card's interval, never more often than every 2 s.
    public static int pollEvery(PcState.Claude claude) { return Math.max(2, claude == null ? 2 : claude.every); }
    public static int period(String name) { return "FiveHour".equals(name) ? 0 : "Week".equals(name) ? 2 : 1; }

    // ----- Package 2 (decision 024), the same words as the PC's ClaudeText -----

    // The side panel's facts: four of these, picked in Settings → Claude on the PC.
    public static final String TOKENS = "tokens", CACHE = "cache", TOOLS = "tools", LINES = "lines", SLOWEST = "slowest", FAILURES = "failures",
        ERRORS = "errors", HELPERS = "helpers", EDITS = "edits", COMMITS = "commits", ACTIVE = "active";
    public static final String[] ALL_FACTS = { TOKENS, CACHE, TOOLS, LINES, SLOWEST, FAILURES, ERRORS, HELPERS, EDITS, COMMITS, ACTIVE };
    public static final String[] DEFAULT_FACTS = { TOKENS, CACHE, TOOLS, LINES };
    // Four known kinds, no repeats, defaults filling any gap (ClaudeFactKinds.Clean).
    public static String[] cleanFacts(String[] kinds) {
        List<String> result = new ArrayList<String>();
        java.util.List<String> all = java.util.Arrays.asList(ALL_FACTS);
        for (String k : kinds == null ? new String[0] : kinds) if (result.size() < 4 && all.contains(k) && !result.contains(k)) result.add(k);
        for (String k : DEFAULT_FACTS) if (result.size() < 4 && !result.contains(k)) result.add(k);
        for (String k : ALL_FACTS) if (result.size() < 4 && !result.contains(k)) result.add(k);
        return result.toArray(new String[0]);
    }
    public static final String NEEDS_TELEMETRY = "Comes from Claude Code's telemetry, which hasn't sent any yet (Settings → Claude → Update connection on the PC).";
    public static final String TOKENS_TIP = "Fresh input, cache writes and output. Cache reads aren't counted here (see Cache hits).";
    public static final String API_VALUE_TIP = "What the same use would cost on Anthropic's API at list prices, cache reads included. It isn't what your Claude plan charges: the plan meters show your plan's use.";
    public static String periodName(int period) { return period >= 0 && period < 3 ? PERIOD_NAMES[period] : "Today"; }
    // "1 h 40", "1 h", "25 min", "40 s".
    public static String span(double seconds) {
        if (Double.isNaN(seconds) || seconds < 60) return Math.max(0, (int) (Double.isNaN(seconds) ? 0 : seconds)) + " s";
        if (seconds < 3600) return (int) (seconds / 60) + " min";
        int minutes = (int) ((seconds % 3600) / 60);
        return (int) (seconds / 3600) + " h" + (minutes > 0 ? String.format(Locale.US, " %02d", minutes) : "");
    }
    // The 5-hour tank's time to the limit: "full in 1 h 40 at this pace" (nothing when it lasts to the reset).
    public static String eta(double minutes) { return Double.isNaN(minutes) ? "" : "full in " + span(minutes * 60) + " at this pace"; }
    // The tank's pace line: the projection and, when it fills before the reset, when.
    public static String paceLine(double projection, double etaMinutes) {
        String pace = ClaudePit.pace(projection), eta = eta(etaMinutes);
        return pace.isEmpty() ? eta : eta.isEmpty() ? pace : pace + " · " + eta;
    }
    // "~" before an API value that leaves out replies from a model with no known price, and what it means.
    public static String unpriced(List<String> models) { return models == null || models.isEmpty() ? "" : "~"; }
    public static String unpricedTip(List<String> models) {
        if (models == null || models.isEmpty()) return "";
        List<String> named = new ArrayList<String>(models.subList(0, Math.min(3, models.size())));
        String names = String.join(", ", named) + (models.size() > 3 ? " and " + (models.size() - 3) + " more" : "");
        return "Price unknown for " + names + ": those replies count $0 here unless Claude Code's status line gave the session's cost.";
    }
    // The API value's explanation: what it is, where its numbers came from, and Claude Code's own count beside it.
    public static String apiValue(PcState.Claude claude, int period) {
        List<String> lines = new ArrayList<String>(); lines.add(API_VALUE_TIP);
        String source = claude.costSource[Math.max(0, Math.min(2, period))];
        lines.add("claude".equals(source) ? "Costs from Claude Code's telemetry." : "statusline".equals(source) ? "Costs from Claude Code's status line, shared out by reply size." : "mixed".equals(source) ? "Costs from Claude Code where it sent them, else from list prices." : "Costs from list prices.");
        if (!Double.isNaN(claude.claudeCodeCost)) lines.add("Running sessions: Claude Code counts $" + dollars(claude.claudeCodeCost) + " in all; Widget Screen counts $" + dollars(claude.estimateCost) + " for them this week.");
        String unknown = unpricedTip(claude.unpriced[Math.max(0, Math.min(2, period))]);
        if (!unknown.isEmpty()) lines.add(unknown);
        return String.join("\n", lines);
    }
    // An API error telemetry saw on a working thread: "Retrying after an API error · HTTP 529 · attempt 3".
    public static String retrying(PcState.ClaudeThread thread) {
        if (thread == null || !thread.apiError) return "";
        return "Retrying after an API error" + (thread.errorStatus > 0 ? " · HTTP " + thread.errorStatus : "") + (thread.errorAttempt > 0 ? " · attempt " + thread.errorAttempt : "");
    }
    // A working car spinning after an API error (not a lasting Stuck): the row says it is retrying.
    public static boolean retryingNow(PcState.ClaudeThread thread) { return thread != null && thread.running && "stuck".equals(thread.pit) && !"stuck".equals(thread.state) && !retrying(thread).isEmpty(); }
    public static String percentOf(double share) { return share(share); }
    // A side-panel fact for the period: its value, the words under it and what it means (ClaudeText.Fact on the PC).
    public static String[] fact(String kind, PcState.Claude claude, int period, boolean counted, boolean showApi) {
        PcState.ClaudeFacts f = claude.factsFor(period);
        String when = periodName(period).toUpperCase(Locale.ROOT), lower = periodName(period).toLowerCase(Locale.ROOT);
        switch (kind == null ? "" : kind) {
            case CACHE:
                return new String[] { share(f.cacheHit()), "CACHE HITS", tokens(f.cacheRead) + " tokens read from the cache " + lower + " (not counted in the token totals)" };
            case TOOLS:
                return new String[] { counted ? String.valueOf(f.tools) : "—", toolsKey(f), toolsTip(f) };
            case LINES:
                boolean any = f.added > 0 || f.removed > 0;
                return new String[] { any ? added(f.added) : "—", any ? removed(f.removed) + " · " + threads(f.linesThreads).toUpperCase(Locale.ROOT) : "LINES", "Lines added and removed " + lower + ", from Claude Code's logs" };
            case SLOWEST:
                return new String[] { Double.isNaN(f.slowMs) ? "—" : span(f.slowMs / 1000), !f.slowTool.isEmpty() ? "SLOWEST · " + f.slowTool.toUpperCase(Locale.ROOT) : "SLOWEST TOOL", f.telemetry ? "The tool with the longest average run " + lower + ", from Claude Code's telemetry." : NEEDS_TELEMETRY };
            case FAILURES:
                return new String[] { share(f.failureRate()), f.runs > 0 ? f.runFailures + " OF " + f.runs + " FAILED" : "TOOL FAILURES",
                    f.failuresFromHooks ? "Tool calls that failed: the sessions' failure counts from Claude Code's hooks over the logs' tool calls " + lower + "." : "Tool runs that failed " + lower + ", from Claude Code's telemetry." };
            case ERRORS:
                return new String[] { f.telemetry ? String.valueOf(f.errors) : "—", "API ERRORS · LAST HOUR", f.telemetry ? "Requests to Claude that failed in the last hour (Claude Code retries most by itself)." : NEEDS_TELEMETRY };
            case HELPERS:
                return new String[] { share(f.helperShare), "HELPERS · " + when, "The subagents' share of the tokens " + lower + (f.helpers > 0 ? "; " + f.helpers + " finished" : "") + "." };
            case EDITS:
                return new String[] { f.metrics ? String.valueOf(f.editsAccepted) : "—", f.metrics ? "EDITS OK · " + f.editsRejected + " REJECTED" : "EDITS", f.metrics ? "Edits, writes and notebook edits you accepted and rejected " + lower + "." : NEEDS_TELEMETRY };
            case COMMITS:
                return new String[] { f.metrics ? String.valueOf(f.commits) : "—", f.metrics ? "COMMITS · " + f.pullRequests + " PRS" : "COMMITS", f.metrics ? "Git commits and pull requests Claude Code made " + lower + "." : NEEDS_TELEMETRY };
            case ACTIVE:
                return new String[] { f.metrics ? span(f.activeCli) : "—", f.metrics ? "CLAUDE · YOU " + span(f.activeUser).toUpperCase(Locale.ROOT) : "ACTIVE TIME", f.metrics ? "Time Claude worked (tools and replies) and time you were active (typing and reading) " + lower + ", from Claude Code's telemetry." : NEEDS_TELEMETRY };
            default:
                String unknown = unpriced(claude.unpriced[Math.max(0, Math.min(2, period))]);
                return new String[] { counted ? tokens(f.tokens) : "—", !counted ? when : showApi ? when + " · " + unknown + "$" + dollars(f.cost) + " API VALUE" : when + " · " + f.replies + " REPLIES", showApi ? apiValue(claude, period) : TOKENS_TIP };
        }
    }
    // A fact that warns: API errors in the last hour, or a fifth or more of the tool runs failing.
    public static boolean factWarns(String kind, PcState.Claude claude, int period) {
        PcState.ClaudeFacts f = claude.factsFor(period);
        return (ERRORS.equals(kind) && f.errors > 0) || (FAILURES.equals(kind) && !Double.isNaN(f.failureRate()) && f.failureRate() >= 0.2);
    }
    public static String toolsTip(PcState.ClaudeFacts f) {
        String calls = "Tool calls, from Claude Code's logs.";
        if (!f.telemetry) return calls + " Which ones asked you comes from Claude Code's telemetry (Settings → Claude → Update connection on the PC).";
        return calls + " " + f.asked + " asked you and " + f.denied + " were denied" + (f.askedPartial ? ", from the sessions that sent telemetry (not every session does, so this is partial)." : ".");
    }
    // The plan's chips: the narrower weekly windows the week tile has no room for (it names two), extra usage and a
    // gateway's spend limit, each { words, "warn" or "" } (warning from 80%, as the meters do).
    public static final int MOST_PLAN_CHIPS = 6;
    public static List<String[]> planChips(PcState.Claude claude, double now, boolean use24) {
        List<String[]> chips = new ArrayList<String[]>();
        if (claude == null) return chips;
        for (int i = 2; i < Math.min(6, claude.extra.size()); i++) {
            PcState.ClaudeWindow w = claude.extra.get(i);
            double pct = percent(w, now);
            chips.add(new String[] { w.name + " " + Math.round(pct) + "%" + (w.resetsAt > 0 && current(w, now) ? " · resets " + when(w.resetsAt, now, use24) : ""), warns(pct) ? "warn" : "" });
        }
        PcState.ClaudeExtraUsage extra = claude.extraUsage;
        if (extra != null) {
            String amounts = !Double.isNaN(extra.used) && !Double.isNaN(extra.limit) ? " · " + amount(extra.used) + " of " + amount(extra.limit) : "";
            chips.add(new String[] { !extra.enabled ? "Extra usage off" : "Extra usage " + (Double.isNaN(extra.pct) ? "on" : Math.round(extra.pct) + "%") + amounts, extra.enabled && !Double.isNaN(extra.pct) && warns(extra.pct) ? "warn" : "" });
        }
        PcState.ClaudeSpend spend = claude.spend;
        if (spend != null) {
            String money = !Double.isNaN(spend.usedUsd) && !Double.isNaN(spend.limitUsd) ? " · $" + dollars(spend.usedUsd) + " of $" + dollars(spend.limitUsd) : "";
            chips.add(new String[] { "Spend limit " + Math.round(spend.pct) + "%" + money + (spend.period.isEmpty() ? "" : " " + spend.period), warns(spend.pct) ? "warn" : "" });
        }
        return chips.size() > MOST_PLAN_CHIPS ? new ArrayList<String[]>(chips.subList(0, MOST_PLAN_CHIPS)) : chips;
    }
    // "12.4", "50": an amount as the PC writes it ("0.##").
    private static String amount(double value) {
        String text = String.format(Locale.US, "%.2f", value);
        return text.contains(".") ? text.replaceAll("0+$", "").replaceAll("\\.$", "") : text;
    }
    // A thread's own tokens and its helpers', for the period: "Own 1.2M · helpers 300k tokens today".
    public static String helpersLine(PcState.ClaudeThread thread, int period) {
        long helpers = thread.helperTokens[period];
        if (helpers <= 0) return "";
        return "Own " + tokens(Math.max(0, thread.tokens[period] - helpers)) + " · helpers " + tokens(helpers) + " tokens " + periodName(period).toLowerCase(Locale.ROOT);
    }
    // A running thread's setup: "Mode plan · Claude Code counts $4.20".
    public static String setup(PcState.ClaudeThread thread, boolean showApi) {
        List<String> parts = new ArrayList<String>();
        if (thread.running && !thread.mode.isEmpty()) parts.add("Mode " + thread.mode);
        if (showApi && !Double.isNaN(thread.costUsd)) parts.add("Claude Code counts $" + dollars(thread.costUsd));
        return join(parts);
    }

    // ----- Package 3 (decision 025) -----

    // "2 helpers working · tasks 3 / 5", or "".
    public static String crew(PcState.ClaudeThread thread) {
        List<String> parts = new ArrayList<String>();
        if (thread.workers > 0) parts.add(thread.workers == 1 ? "1 helper working" : thread.workers + " helpers working");
        if (thread.tasksTotal > 0) parts.add("tasks " + tasks(thread));
        return join(parts);
    }
    // The car's task sticker: "3 / 5".
    public static String tasks(PcState.ClaudeThread thread) { return thread.tasksDone + " / " + thread.tasksTotal; }
    // Why Claude Code ended a thread it kept.
    public static String endedBy(String reason) {
        return "prompt_input_exit".equals(reason) ? "Closed in Claude Code" : "logout".equals(reason) ? "Ended when Claude Code logged out" : "clear".equals(reason) ? "Cleared with /clear" : "";
    }
    // A thread's details, read aloud and shown when its row is held: the PC's tooltip, in the same order.
    public static String details(PcState.ClaudeThread thread, PcState.Claude claude, int period, double now, boolean use24, boolean counted, boolean showApi) {
        List<String> lines = new ArrayList<String>();
        lines.add(threadTitle(thread, now, use24) + " · " + thread.project);
        lines.add(thread.running ? stateWords(thread.state, thread.since, now, true) + ("needs".equals(thread.state) && !ask(thread.message).isEmpty() ? " · " + ask(thread.message) : "") : "Ended · last active " + ago(thread.lastActive, now, use24));
        if (stuck(thread)) lines.add(stuckReason(thread));
        if (retryingNow(thread)) lines.add(retrying(thread));
        String context = thread.running ? contextTip(thread.context) : "";
        if (!context.isEmpty()) lines.add(context);
        String crew = thread.running ? crew(thread) : "";
        if (!crew.isEmpty()) lines.add(crew);
        String ended = thread.running ? "" : endedBy(thread.endReason);
        if (!ended.isEmpty()) lines.add(ended);
        String helpers = counted ? helpersLine(thread, period) : "";
        if (!helpers.isEmpty()) lines.add(helpers);
        String setup = setup(thread, showApi);
        if (!setup.isEmpty()) lines.add(setup);
        List<String> detail = new ArrayList<String>();
        if (counted) detail.add(tokens(thread.tokens[period]) + " tokens · " + replies(thread.replies[period]) + (showApi ? " · " + unpriced(claude.unpriced[period]) + money(thread.cost[period]) + " API value" : "") + " " + periodName(period).toLowerCase(Locale.ROOT));
        if (!thread.model.isEmpty()) detail.add(thread.model);
        if (!thread.where.isEmpty()) detail.add(thread.where);
        if (!detail.isEmpty()) lines.add(join(detail));
        return String.join("\n", lines);
    }
    // A Claude alert as a notification: its title ("Claude needs you · api-server") and words ("Fix the login: Wants to use Bash").
    public static String alertTitle(PcState.ClaudeAlert alert) {
        return ("need".equals(alert.kind) ? "Claude needs you" : "stuck".equals(alert.kind) ? "Claude is stuck" : "Claude finished") + (alert.project.isEmpty() ? "" : " · " + alert.project);
    }
    public static String alertText(PcState.ClaudeAlert alert) {
        String what = "need".equals(alert.kind) ? ask(alert.text) : alert.text == null ? "" : alert.text.trim();
        if (what.isEmpty()) what = "need".equals(alert.kind) ? "Waiting for you" : "stuck".equals(alert.kind) ? "Stopped on an error" : "Done";
        String title = placeholder(alert.title) ? idLabel(alert.session) : alert.title.trim();
        return title + ": " + what;
    }
    // The alerts to raise now, oldest first: those numbered past the last one seen. The first answer after the app
    // starts only sets the mark (nothing from before it pops up); a PC that started again counts from 1 again.
    public static List<PcState.ClaudeAlert> freshAlerts(List<PcState.ClaudeAlert> alerts, long seq, long seen) {
        List<PcState.ClaudeAlert> fresh = new ArrayList<PcState.ClaudeAlert>();
        if (seen < 0 || alerts == null) return fresh;
        long from = seq < seen ? 0 : seen;
        for (PcState.ClaudeAlert a : alerts) if (a.seq > from) fresh.add(a);
        return fresh;
    }

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
