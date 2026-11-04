using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WidgetScreen
{
    // One plan window: how much of it is used (0–100) and when it resets.
    public sealed class ClaudeLimitWindow
    {
        public double Percent;
        public DateTime? ResetsAt;
    }

    // The plan's five-hour and weekly limits, from the widget's own sign-in (ClaudeAccount.UsageAsync).
    public sealed class ClaudeLimits
    {
        public ClaudeLimitWindow FiveHour, Week;
        public DateTime UpdatedAt;
        public bool Known { get { return FiveHour != null || Week != null; } }
    }

    // Where the plan tiles stand: not signed in, the first check on its way, a current reading, a check that didn't go
    // through (the last reading stays), a sign-in Anthropic ended, or a plan with no usage meters (a 403).
    public enum ClaudePlanState { SignedOut, Reading, Current, Failed, Expired, NoMeters }

    public static class ClaudeTime
    {
        public static long Epoch(DateTime time) { return (long)Math.Round((time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds); }
        public static DateTime FromEpoch(double ms) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms); }

        // Unix seconds (or milliseconds, if the number is that large), or an ISO-8601 time.
        public static DateTime? Parse(object value)
        {
            double number = Double.NaN;
            if (value is JsonNumber) number = ((JsonNumber)value).Value;
            else if (value is string && !Double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) number = Double.NaN;
            if (!Double.IsNaN(number) && number > 0) return FromEpoch(number > 1e11 ? number : number * 1000);
            var text = value as string; DateTime time;
            if (!String.IsNullOrWhiteSpace(text) && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out time)) return DateTime.SpecifyKind(time, DateTimeKind.Utc);
            return null;
        }
    }

    public static class ClaudePlan
    {
        // The read-only usage check's answer (GET api.anthropic.com/api/oauth/usage, undocumented): five_hour and
        // seven_day, each utilization 0–100 and resets_at as ISO-8601; either may be null. The newer "limits" list
        // ({kind, percent, resets_at}) fills a window the older fields left out. Null when nothing is usable.
        public static ClaudeLimits ParseUsage(string json, DateTime now)
        {
            object parsed; if (!ClaudeJson.TryParse(json, out parsed)) return null;
            var o = ClaudeJson.Obj(parsed); if (o == null) return null;
            var limits = new ClaudeLimits { FiveHour = Window(ClaudeJson.Obj(o, "five_hour"), "utilization"), Week = Window(ClaudeJson.Obj(o, "seven_day"), "utilization"), UpdatedAt = now };
            foreach (var item in (ClaudeJson.List(o, "limits") ?? new List<object>()).OfType<JsonObject>())
            {
                string kind = (ClaudeJson.Str(item, "kind") ?? "").ToLowerInvariant();
                var window = Window(item, "percent");
                if (window == null) continue;
                if (kind == "weekly_all") { if (limits.Week == null) limits.Week = window; }
                else if (kind.Contains("five_hour") || kind.Contains("session") || kind.Contains("5h")) { if (limits.FiveHour == null) limits.FiveHour = window; }
            }
            return limits.Known ? limits : null;
        }

        private static ClaudeLimitWindow Window(JsonObject o, string percentKey)
        {
            if (o == null) return null;
            double pct = ClaudeJson.Num(o, percentKey);
            if (Double.IsNaN(pct)) return null;
            return new ClaudeLimitWindow { Percent = Math.Max(0, Math.Min(100, pct)), ResetsAt = ClaudeTime.Parse(o.Get("resets_at")) };
        }

        // A window whose reset time has passed starts again at nothing until the next check says otherwise.
        public static double Percent(ClaudeLimitWindow window, DateTime now)
        {
            if (window == null) return Double.NaN;
            return window.ResetsAt.HasValue && window.ResetsAt.Value <= now ? 0 : window.Percent;
        }
    }
}
