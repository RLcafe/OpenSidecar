using System;
using System.Diagnostics;

namespace WidgetScreen
{
    public sealed class FocusTimer
    {
        private readonly Stopwatch elapsed = new Stopwatch();
        private readonly Func<DateTime> now;
        private TimeSpan duration = TimeSpan.FromMinutes(25);
        private DateTime? finishedAt;
        // How long the card shows a finished session before it quietly goes back to ready.
        public static readonly TimeSpan FinishShownFor = TimeSpan.FromSeconds(60);

        public FocusTimer() : this(() => DateTime.UtcNow) { }
        public FocusTimer(Func<DateTime> now) { this.now = now; }

        public bool Running { get { return elapsed.IsRunning; } }
        public TimeSpan Duration { get { return duration; } }
        public TimeSpan Remaining { get { return RemainingAt(elapsed.Elapsed); } }
        public double Progress { get { return 1 - Remaining.TotalSeconds / duration.TotalSeconds; } }
        // Stopped part-way with time left.
        public bool Paused { get { return !Running && elapsed.Elapsed > TimeSpan.Zero && Remaining > TimeSpan.Zero; } }
        // The session ended within the last minute and nobody dismissed it: the card says so.
        public bool Finished { get { return finishedAt.HasValue && now() - finishedAt.Value < FinishShownFor; } }

        public TimeSpan RemainingAt(TimeSpan passed) { return passed >= duration ? TimeSpan.Zero : duration - passed; }
        public void Toggle() { finishedAt = null; if (Running) elapsed.Stop(); else { if (Remaining == TimeSpan.Zero) elapsed.Reset(); elapsed.Start(); } }
        public void Reset(int minutes) { Reset(TimeSpan.FromMinutes(Math.Max(1, Math.Min(180, minutes)))); }
        public void Reset(TimeSpan length) { finishedAt = null; duration = length > TimeSpan.Zero ? length : TimeSpan.FromMinutes(1); elapsed.Reset(); }
        public void Dismiss() { finishedAt = null; }
        public bool FinishIfElapsed() { if (!Running || Remaining > TimeSpan.Zero) return false; elapsed.Stop(); finishedAt = now(); return true; }

        // "3 sessions today · 1 h 40 m", or nothing before the first session. The phone formats it the same way.
        public static string SessionsText(int sessions, int minutes)
        {
            if (sessions <= 0) return "";
            string time = minutes >= 60 ? (minutes / 60) + " h " + (minutes % 60) + " m" : minutes + " m";
            return sessions + (sessions == 1 ? " session today  ·  " : " sessions today  ·  ") + time;
        }

        // The Sticker Sheet's Focus facts (docs/design/sticker-sheet.md, phase 2); the phone's core/Format has the same.
        // Today: the finished sessions' minutes. "0 min" before the first.
        public static string MinutesText(int minutes)
        {
            minutes = Math.Max(0, minutes);
            return minutes >= 60 ? (minutes / 60) + " h " + (minutes % 60) + " min" : minutes + " min";
        }
        // Next break: when a running session ends, otherwise how long the next session is.
        public static string BreakText(bool running, TimeSpan remaining, TimeSpan duration, DateTime now, bool use24)
        {
            if (running && remaining > TimeSpan.Zero) return (now + remaining).ToString(use24 ? "HH:mm" : "h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            var left = remaining > TimeSpan.Zero && remaining < duration ? remaining : duration;
            return "In " + Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + " min";
        }
        // The status sticker: Done, Focusing or Paused, otherwise today's count ("2 TODAY"), or nothing.
        public static string StateText(bool running, bool finished, bool paused, int sessionsToday)
        {
            if (finished) return "DONE";
            if (running) return "FOCUSING";
            if (paused) return "PAUSED";
            return sessionsToday > 0 ? sessionsToday + " TODAY" : "";
        }
    }
}
