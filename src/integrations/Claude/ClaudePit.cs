using System;
using System.Collections.Generic;
using System.Linq;

namespace WidgetScreen
{
    // What a thread's car does on the v2 card (docs/design/claude-watcher-v2.html, decisions 017 and 019): Idle (engine
    // off: nothing asked yet, or your turn for over 30 minutes), Work (wheels turning), Rev (a long tool run: holding on
    // the brakes), Need (a question or permission waiting on you: hazards and a beacon), Pit (compacting: up on the
    // jack), Stuck (rate-limited or an API error: wheelspin), Done ("your turn": Claude finished and waits for your next
    // message, brake lights on) and End (under a cover).
    // Archived (decision 020): a thread archived in the Claude app, parked under its cover and dimmed the most.
    // Resolved: the Claude app says its latest reply completed the job (Ali, 1 Nov 2026); shown like Archived.
    public enum PitState { Idle, Work, Rev, Need, Pit, Stuck, Done, End, Archived, Resolved }

    // The car classes, by the model a thread mostly used: a grand tourer for Opus (and bigger), a sports car for
    // Sonnet, a hot hatch for Haiku.
    public enum PitCar { GrandTourer, Sports, Hatch }

    // One project's lane on the 5-hour timeline: tokens in each 10-minute bin, oldest first.
    public sealed class ClaudeLane
    {
        // A project's lane, or one of its threads' (Session set, Name its title) under it.
        public string Folder = "", Name = "", Session = "";
        public double[] Bins = new double[ClaudePace.LaneBins];
    }

    // The four facts beside the timeline, for today: tokens and their ≈ $, how much input came from the cache, tool
    // calls (with how many asked you and how many you denied), and lines changed (by how many threads).
    public sealed class ClaudeFacts
    {
        public long Tokens, Prompt, CacheRead;
        public double Cost;
        public int Tools, Asked, Denied, LinesThreads, Replies;
        public long Added, Removed;
        // Whether telemetry has said anything (it alone knows which tool calls asked you), and whether it saw fewer
        // tool calls than the logs did today, so "asked" is a partial count (review fix 2). Tools and lines come from
        // the logs.
        public bool Telemetry, AskedPartial;
        public double CacheHit { get { return Prompt <= 0 ? Double.NaN : (double)CacheRead / Prompt; } }
    }

    // A tool decision or result, or lines changed, from telemetry: kept a week for the facts and each thread's lines.
    public sealed class ClaudeToolEntry { public DateTime At; public string Session = ""; public bool Result, Asked, Denied; }
    public sealed class ClaudeLinesEntry { public DateTime At; public string Session = ""; public long Added, Removed; }

    // The pit wall's arithmetic, the same as ClaudePit.java on the phone: the plan's pace, the week's laps, the rev
    // counter's scale, the timeline's bins and which car and behaviour a thread gets.
    public static class ClaudePace
    {
        public static readonly TimeSpan FiveHours = TimeSpan.FromHours(5), Week = TimeSpan.FromDays(7);
        public const int BurnMinutes = 60, LaneBins = 30, Laps = 7;
        public const double MinScale = 60000, Redline = 0.75;
        // How long a tool may run before the car revs on the spot, and how long an API error keeps it spinning.
        public static readonly TimeSpan LongTool = TimeSpan.FromSeconds(15), ErrorShows = TimeSpan.FromSeconds(60);
        // A compaction no hook ended within this long is over.
        public static readonly TimeSpan CompactForgotten = TimeSpan.FromMinutes(10);
        // Your turn for this long is just idle (decision 019).
        public static readonly TimeSpan TurnIdle = TimeSpan.FromMinutes(30);
        public const int MostLanes = 6;

        // How much of a window has passed (0–1): the pace tick on the 5-hour gauge and the week's laps. NaN when the
        // window's reset time isn't known.
        public static double Elapsed(ClaudeLimitWindow window, TimeSpan length, DateTime now)
        {
            if (window == null || !window.ResetsAt.HasValue || !window.Current(now)) return Double.NaN;
            double done = 1 - (window.ResetsAt.Value - now).TotalSeconds / length.TotalSeconds;
            return Math.Max(0, Math.Min(1, done));
        }
        // Where the window ends at this rate ("on pace for 92%"): NaN in its first twentieth, when it says nothing yet.
        public static double Projection(double percent, double elapsed)
        {
            if (Double.IsNaN(elapsed) || elapsed < 0.05) return Double.NaN;
            return Math.Min(999, percent / elapsed);
        }
        // The projection turns Amber near the limit, like the fuel lamp.
        public static bool Warns(double projection) { return !Double.IsNaN(projection) && projection >= 90; }
        public static bool Fuel(double fiveHourPercent) { return fiveHourPercent >= 90; }
        // A lap's fill (0–1): the share of that day's seventh of the window that the percentage covers.
        public static double Lap(double percent, int lap) { return Math.Max(0, Math.Min(1, (percent / 100 - lap / 7.0) * 7)); }
        // The lap the week is on (0–6), from how much of it has passed.
        public static int LapNow(double elapsed) { return Double.IsNaN(elapsed) ? -1 : Math.Min(Laps - 1, (int)Math.Floor(elapsed * Laps)); }

        // The rev counter's full scale: 60k tokens a minute, or the next round step above the hour's busiest minute.
        public static double Scale(double peak)
        {
            if (Double.IsNaN(peak) || peak <= MinScale) return MinScale;
            double power = Math.Pow(10, Math.Floor(Math.Log10(peak)));
            foreach (double step in new[] { 1, 1.5, 2, 3, 4, 6, 8, 10 }) if (step * power >= peak) return step * power;
            return 10 * power;
        }
        // The share of the scale a thread burns, which sets its wheels' speed (4–24 frames a second).
        public static double Share(double rate, double scale) { return scale <= 0 || Double.IsNaN(rate) ? 0 : Math.Max(0, Math.Min(1, rate / scale)); }
        public static double WheelFps(double share) { return 4 + 20 * Math.Max(0, Math.Min(1, share)); }
        public static bool Heavy(double share) { return share >= 0.5; }
        public static bool Hot(double share) { return share >= Redline; }

        // A model's family for the mix bar and the car: 0 Opus (and the bigger Fable and Mythos), 1 Sonnet, 2 Haiku,
        // -1 for a name that isn't a Claude model.
        public static int Family(string model)
        {
            string name = (model ?? "").ToLowerInvariant();
            if (name.Contains("haiku")) return 2;
            if (name.Contains("sonnet")) return 1;
            if (name.Contains("opus") || name.Contains("fable") || name.Contains("mythos")) return 0;
            return -1;
        }
        // Token shares per family (0–1, adding up to 1, or all 0 when nothing was used).
        public static double[] Mix(IEnumerable<ClaudeUsageEntry> entries)
        {
            var sums = new double[3];
            foreach (var e in entries) { int f = Family(e.Model); if (f >= 0) sums[f] += e.Tokens; }
            double total = sums.Sum();
            return total <= 0 ? new double[3] : sums.Select(v => v / total).ToArray();
        }
        // The models replies used, by display name ("Opus 4.8"), with their share of the tokens, biggest first.
        public static List<KeyValuePair<string, double>> Models(IEnumerable<ClaudeUsageEntry> entries)
        {
            var sums = entries.Where(e => e.Model.Length > 0).GroupBy(e => ClaudeService.ModelName(e.Model)).Select(g => new KeyValuePair<string, double>(g.Key, g.Sum(e => (double)e.Tokens))).ToList();
            double total = sums.Sum(p => p.Value);
            return total <= 0 ? new List<KeyValuePair<string, double>>() : sums.Select(p => new KeyValuePair<string, double>(p.Key, p.Value / total)).OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        }
        // The Models column (Ali, 1 Nov 2026): one model by name ("Opus 4.8"); a mix as its top two with shares
        // ("Opus 70% · Sonnet 30%"). Each part is (words, model).
        public static List<KeyValuePair<string, string>> ModelLabel(List<KeyValuePair<string, double>> models)
        {
            var parts = new List<KeyValuePair<string, string>>();
            if (models == null || models.Count == 0) return parts;
            if (models[0].Value >= 0.95) { parts.Add(new KeyValuePair<string, string>(models[0].Key, models[0].Key)); return parts; }
            foreach (var m in models.Take(2)) parts.Add(new KeyValuePair<string, string>(Family(m.Key) >= 0 ? FamilyName(m.Key) + " " + Math.Round(m.Value * 100) + "%" : m.Key + " " + Math.Round(m.Value * 100) + "%", m.Key));
            return parts;
        }
        public static string FamilyName(string model) { int f = Family(model); return f == 0 ? (model ?? "").Split(' ')[0] : f == 1 ? "Sonnet" : f == 2 ? "Haiku" : model; }
        // The car follows the model the thread used most, else the model it names, else a sports car.
        public static PitCar Car(double[] mix, string model)
        {
            if (mix != null && mix.Sum() > 0) { int best = 0; for (int i = 1; i < 3; i++) if (mix[i] > mix[best]) best = i; return (PitCar)best; }
            int family = Family(model);
            return family < 0 ? PitCar.Sports : (PitCar)family;
        }

        // What a thread's car does. Running threads follow their state; a working one revs during a long tool run and
        // spins after an API error (until a later request goes through).
        public static PitState State(ClaudeSession thread, DateTime now, DateTime decision, DateTime result, DateTime error, DateTime request)
        {
            if (!thread.Running) return thread.Archived ? PitState.Archived : thread.Resolved ? PitState.Resolved : PitState.End;
            // Live state wins over Resolved: working, needing you, compacting or a fresh "your turn" shows as that. An
            // open thread only counts as resolved once it has sat idle for TurnIdle (decision 020, review fix 1).
            bool settled = now - thread.Since >= TurnIdle;
            switch (thread.State)
            {
                case ClaudeState.NeedsYou: return PitState.Need;
                // Stuck stays until the next turn starts (decision 023), however long ago the error was.
                case ClaudeState.Stuck: return PitState.Stuck;
                case ClaudeState.Done: return thread.Compacting ? PitState.Pit : !settled ? PitState.Done : thread.Resolved ? PitState.Resolved : PitState.Idle;
                case ClaudeState.Ready: return thread.Compacting ? PitState.Pit : settled && thread.Resolved ? PitState.Resolved : PitState.Idle;
            }
            if (thread.Compacting) return PitState.Pit;
            if (error > request && now - error < ErrorShows) return PitState.Stuck;
            if (decision > result && now - decision >= LongTool) return PitState.Rev;
            return PitState.Work;
        }
        public static string StateName(PitState state) { return state.ToString().ToLowerInvariant(); }
        // The car's context ring (decision 023): 0 none (under 60% or unknown), 1 from 60%, 2 from 80% (compaction
        // soon), 3 from 95%.
        public static int ContextLevel(double percent) { return Double.IsNaN(percent) || percent < 60 ? 0 : percent < 80 ? 1 : percent < 95 ? 2 : 3; }
        // Whether Settings → Claude hides a thread in this state (its tokens still count in its project's totals).
        public static bool Hidden(PitState state, bool showArchived, bool hideIdle) { return (Parked(state) && !showArchived) || (state == PitState.Idle && hideIdle); }
        // Archived or resolved: under the cover, dimmed the most, last in its project.
        public static bool Parked(PitState state) { return state == PitState.Archived || state == PitState.Resolved; }
        public static PitState ParseState(string name) { PitState state; return Enum.TryParse(name ?? "", true, out state) ? state : PitState.Idle; }
        public static string CarName(PitCar car) { return car == PitCar.GrandTourer ? "gt" : car == PitCar.Hatch ? "hatch" : "sport"; }

        // Tokens in each of the last count bins of the given length, oldest first, ending now.
        public static double[] Bins(IEnumerable<ClaudeUsageEntry> entries, DateTime now, TimeSpan bin, int count)
        {
            var bins = new double[count];
            DateTime start = now - TimeSpan.FromTicks(bin.Ticks * count);
            foreach (var e in entries)
            {
                if (e.At < start || e.At > now) continue;
                int i = (int)((e.At - start).Ticks / bin.Ticks);
                if (i >= count) i = count - 1;
                bins[i] += e.Tokens;
            }
            return bins;
        }
        // Tokens a minute over the last two minutes.
        public static double Rate(IEnumerable<ClaudeUsageEntry> entries, DateTime now)
        {
            return entries.Where(e => e.At > now.AddMinutes(-2) && e.At <= now).Sum(e => (double)e.Tokens) / 2;
        }
    }
}
