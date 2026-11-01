package com.alialyami.widgetscreen.core;

// The pit wall's arithmetic (docs/design/claude-watcher-v2.html; decision 017), the same as ClaudePace in the Windows
// ClaudePit.cs: the plan's pace, the week's laps and the rev counter. The PC decides each thread's car and what it does;
// the phone draws them. Times are Unix seconds.
public final class ClaudePit {
    public static final double FIVE_HOURS = 5 * 3600, WEEK = 7 * 86400, MIN_SCALE = 60000, REDLINE = 0.75;
    public static final int BURN_MINUTES = 60, LANE_BINS = 30;

    // How much of a window has passed (0–1); NaN when its reset time isn't known or has passed.
    public static double elapsed(PcState.ClaudeWindow window, double length, double now) {
        if (window == null || window.resetsAt <= 0 || window.resetsAt <= now) return Double.NaN;
        return Math.max(0, Math.min(1, 1 - (window.resetsAt - now) / length));
    }
    // Where the window ends at this rate; NaN in its first twentieth.
    public static double projection(double percent, double elapsed) {
        if (Double.isNaN(elapsed) || elapsed < 0.05) return Double.NaN;
        return Math.min(999, percent / elapsed);
    }
    public static boolean warns(double projection) { return !Double.isNaN(projection) && projection >= 90; }
    public static boolean fuel(double fiveHourPercent) { return fiveHourPercent >= 90; }
    public static double lap(double percent, int lap) { return Math.max(0, Math.min(1, (percent / 100 - lap / 7.0) * 7)); }
    public static int lapNow(double elapsed) { return Double.isNaN(elapsed) ? -1 : Math.min(6, (int) Math.floor(elapsed * 7)); }
    public static double share(double rate, double scale) { return scale <= 0 || Double.isNaN(rate) ? 0 : Math.max(0, Math.min(1, rate / scale)); }
    public static double wheelFps(double share) { return 4 + 20 * Math.max(0, Math.min(1, share)); }
    public static boolean heavy(double share) { return share >= 0.5; }
    public static boolean hot(double share) { return share >= REDLINE; }
    public static String pace(double projection) { return Double.isNaN(projection) ? "" : "on pace for " + Math.round(projection) + "%"; }
}
