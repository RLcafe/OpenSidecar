using System;
using System.Diagnostics;

namespace WidgetScreen
{
    public sealed class FocusTimer
    {
        private readonly Stopwatch elapsed = new Stopwatch();
        private TimeSpan duration = TimeSpan.FromMinutes(25);
        public bool Running { get { return elapsed.IsRunning; } }
        public TimeSpan Duration { get { return duration; } }
        public TimeSpan Remaining { get { return RemainingAt(elapsed.Elapsed); } }
        public double Progress { get { return 1 - Remaining.TotalSeconds / duration.TotalSeconds; } }

        public TimeSpan RemainingAt(TimeSpan passed) { return passed >= duration ? TimeSpan.Zero : duration - passed; }
        public void Toggle() { if (Running) elapsed.Stop(); else { if (Remaining == TimeSpan.Zero) elapsed.Reset(); elapsed.Start(); } }
        public void Reset(int minutes) { duration = TimeSpan.FromMinutes(Math.Max(1, Math.Min(180, minutes))); elapsed.Reset(); }
        public bool FinishIfElapsed() { if (!Running || Remaining > TimeSpan.Zero) return false; elapsed.Stop(); return true; }
    }
}
