package com.alialyami.widgetscreen.core;

// Port of the Windows FocusTimer. The clock is injected: Android passes SystemClock.elapsedRealtime,
// which keeps counting while the screen is off, and tests pass a fake clock.
public final class FocusTimer {
    public interface Clock { long millis(); }

    private final Clock clock;
    private long durationMs = 25 * 60000L, accumulatedMs, startedAt = -1;

    public FocusTimer(Clock clock) { this.clock = clock; }

    public boolean running() { return startedAt >= 0; }
    public long durationMs() { return durationMs; }
    public int durationMinutes() { return (int) (durationMs / 60000L); }
    private long elapsed() { return accumulatedMs + (running() ? Math.max(0, clock.millis() - startedAt) : 0); }
    public long remainingAt(long passed) { return passed >= durationMs ? 0 : durationMs - passed; }
    public long remainingMs() { return remainingAt(elapsed()); }
    public double progress() { return 1 - remainingMs() / (double) durationMs; }

    public void toggle() {
        if (running()) { accumulatedMs = elapsed(); startedAt = -1; }
        else { if (remainingMs() == 0) accumulatedMs = 0; startedAt = clock.millis(); }
    }

    public void reset(int minutes) { durationMs = Math.max(1, Math.min(180, minutes)) * 60000L; accumulatedMs = 0; startedAt = -1; }

    public boolean finishIfElapsed() {
        if (!running() || remainingMs() > 0) return false;
        accumulatedMs = elapsed(); startedAt = -1;
        return true;
    }

    // Kept across app restarts: duration, time already counted, and when the current run started.
    public String save() { return durationMs + "," + accumulatedMs + "," + startedAt; }

    public void restore(String saved) {
        if (saved == null) return;
        String[] parts = saved.split(",");
        if (parts.length != 3) return;
        try {
            long duration = Long.parseLong(parts[0]), accumulated = Long.parseLong(parts[1]), started = Long.parseLong(parts[2]);
            if (duration < 60000L || duration > 180 * 60000L || accumulated < 0) return;
            durationMs = duration; accumulatedMs = Math.min(accumulated, duration);
            // A start time in the future means the device restarted since; the run can't be resumed.
            startedAt = started >= 0 && started <= clock.millis() ? started : -1;
        } catch (NumberFormatException ignored) { }
    }
}
