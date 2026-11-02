using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WidgetScreen
{
    // A Claude Code session as the card and the phone show it.
    public sealed class ClaudeSession
    {
        public string Id = "", Project = "", ProjectName = "", Title = "", Message = "", Model = "", Where = "";
        public ClaudeState State;
        public DateTime Since;
        // Running: Claude Code has the session open now. An ended thread (from the Claude app's list or the logs) keeps
        // its title, last activity and usage, and shows asleep.
        public bool Running;
        public DateTime LastActive;
        public int Pid, WindowPid;
        public long Window;
        public bool CanFocus;
        public readonly Dictionary<ClaudePeriod, long> Tokens = new Dictionary<ClaudePeriod, long>();
        public readonly Dictionary<ClaudePeriod, double> Cost = new Dictionary<ClaudePeriod, double>();
        // The v2 card (decision 017): its car and what the car does, its tokens a minute right now, its model mix
        // (Opus, Sonnet, Haiku shares) and lines added and removed per period.
        public bool Compacting;
        // Archived in the Claude app (its isArchived flag), or resolved: its latest reply completed the job and
        // nothing newer has happened (decision 020).
        public bool Archived, Resolved;
        // Decision 023: why a Stuck session stopped (StopFailure's error, or "quota_disabled") and its details; how
        // full its context window is (0–100, NaN when unknown); subagents running and open tasks; tool calls that
        // failed or auto mode denied, and the last failure.
        public string Reason = "", ReasonText = "", LastError = "";
        public double Context = Double.NaN;
        public int Workers, Tasks, Failed, Denied;
        public PitState Pit = PitState.Idle;
        public PitCar Car = PitCar.Sports;
        public double Rate;
        public readonly Dictionary<ClaudePeriod, double[]> Mix = new Dictionary<ClaudePeriod, double[]>();
        // The models by name with their share of the tokens, per period (the Models column).
        public readonly Dictionary<ClaudePeriod, List<KeyValuePair<string, double>>> Models = new Dictionary<ClaudePeriod, List<KeyValuePair<string, double>>>();
        public List<KeyValuePair<string, double>> ModelsFor(ClaudePeriod period) { List<KeyValuePair<string, double>> value; return Models.TryGetValue(period, out value) ? value : new List<KeyValuePair<string, double>>(); }
        public readonly Dictionary<ClaudePeriod, long[]> Lines = new Dictionary<ClaudePeriod, long[]>();
        // Claude's replies in each period (one per API request): a number Ali can check against the thread itself.
        public readonly Dictionary<ClaudePeriod, int> Replies = new Dictionary<ClaudePeriod, int>();
        public int RepliesFor(ClaudePeriod period) { int value; return Replies.TryGetValue(period, out value) ? value : 0; }
        // Tokens read from the prompt cache in each period: its own ledger column, apart from Tokens (Ali, 1 Nov 2026).
        public readonly Dictionary<ClaudePeriod, long> CacheReads = new Dictionary<ClaudePeriod, long>();
        public long CacheReadsFor(ClaudePeriod period) { long value; return CacheReads.TryGetValue(period, out value) ? value : 0; }
        public string Shown { get { return String.IsNullOrWhiteSpace(Title) ? ProjectName : Title; } }
        public long TokensFor(ClaudePeriod period) { long value; return Tokens.TryGetValue(period, out value) ? value : 0; }
        public double CostFor(ClaudePeriod period) { double value; return Cost.TryGetValue(period, out value) ? value : 0; }
        public double[] MixFor(ClaudePeriod period) { double[] value; return Mix.TryGetValue(period, out value) ? value : new double[3]; }
        public long[] LinesFor(ClaudePeriod period) { long[] value; return Lines.TryGetValue(period, out value) ? value : new long[2]; }
    }

    // How the plan meters' numbers stand: the widget's own sign-in and its last check. NoMeters: signed in, but the
    // plan has no usage meters (the usage check answered 403; decision 023).
    public enum ClaudePlanState { SignedOut, Reading, Current, Failed, Expired, NoMeters }

    // A project in Settings → Claude → Projects: its folder, when it was last used, this week's usage and its tick.
    public sealed class ClaudeProjectInfo
    {
        public string Folder = "", Name = "";
        public DateTime LastUsed;
        public long WeekTokens;
        public double WeekCost;
        public int Threads;
        public bool Shown;
    }

    // A project on the card: its big critter and the threads (baby critters) active in the period, most urgent first,
    // then newest. Its face is the most urgent running thread's; with none running it sleeps. Collapsed folds the
    // threads into it, but a thread that needs you still turns it Amber.
    public sealed class ClaudeProjectGroup
    {
        public string Folder = "", Name = "";
        public List<ClaudeSession> Threads = new List<ClaudeSession>();
        public long Tokens;
        public double Cost;
        // Its threads' model mix, weighted by their tokens, and their lines added and removed.
        public double[] Mix = new double[3];
        public List<KeyValuePair<string, double>> Models = new List<KeyValuePair<string, double>>();
        public long Added, Removed, CacheReads;
        public int Replies;
        // Threads Settings → Claude hides (archived, or idle when asked); they still count in the totals above.
        public int Hidden;
        public bool Collapsed;
        public int Running { get { return Threads.Count(t => t.Running); } }
        public bool NeedsYou { get { return Threads.Any(t => t.Running && t.State == ClaudeState.NeedsYou); } }
        public bool Asleep { get { return !Threads.Any(t => t.Running); } }
        public ClaudeState State { get { var first = Threads.Where(t => t.Running).OrderBy(t => ClaudeView.Rank(t.State)).FirstOrDefault(); return first == null ? ClaudeState.Ready : first.State; } }
        // When a project with no thread in the period was last used (it still gets a quiet garage).
        public DateTime Seen;
        public DateTime LastActive { get { return Threads.Count == 0 ? Seen : Threads.Max(t => t.LastActive); } }
        public bool Working { get { return Threads.Any(t => t.Running && t.State == ClaudeState.Working); } }
        // Its garage's state: needs you (the beacon), busy (light under the shutter), all ended (dimmed) or quiet.
        public PitState Garage { get { return NeedsYou ? PitState.Need : Working ? PitState.Work : Asleep ? PitState.End : Threads.Any(t => t.Running && t.State == ClaudeState.Done) ? PitState.Done : PitState.Idle; } }
    }

    // Everything the Claude card shows at one moment.
    public sealed class ClaudeView
    {
        public bool Connected, Sample, LogsFailed, LogsRead;
        public ClaudePlanState Plan;
        // The running sessions (the critters Small shows and the pill counts), and every thread of the last week,
        // running or ended, that the projects group.
        public List<ClaudeSession> Sessions = new List<ClaudeSession>();
        public List<ClaudeSession> Threads = new List<ClaudeSession>();
        public ClaudeLimits Limits;
        public readonly Dictionary<ClaudePeriod, List<ClaudeProjectUsage>> Usage = new Dictionary<ClaudePeriod, List<ClaudeProjectUsage>>();
        public readonly Dictionary<ClaudePeriod, DateTime> Starts = new Dictionary<ClaudePeriod, DateTime>();
        public readonly HashSet<string> Collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Every shown project this PC knows Claude Code was used in (by folder: its name and last use), so a project with
        // no thread in the period still gets a quiet garage (decision 019).
        public readonly Dictionary<string, KeyValuePair<string, DateTime>> Known = new Dictionary<string, KeyValuePair<string, DateTime>>(StringComparer.OrdinalIgnoreCase);
        // The pit wall (decision 017): tokens a minute over the last hour (oldest first) and now, the rev counter's
        // scale, the 5-hour lanes by project and today's facts. Worked out when the view is made.
        public double[] Burn = new double[ClaudePace.BurnMinutes];
        public double Rate, Scale = ClaudePace.MinScale;
        public List<ClaudeLane> Lanes = new List<ClaudeLane>();
        public ClaudeFacts Facts = new ClaudeFacts();
        public DateTime MadeAt = DateTime.UtcNow;
        // "Telemetry off: port 43832 in use" when the receiver couldn't listen where settings.json sends it (decision 023).
        public string TelemetryWarning = "";
        public int Stuck { get { return Sessions.Count(s => s.State == ClaudeState.Stuck); } }
        public int NeedsYou { get { return Sessions.Count(s => s.State == ClaudeState.NeedsYou); } }
        public int Working { get { return Sessions.Count(s => s.State == ClaudeState.Working); } }
        // Settings → Claude: show archived threads (off by default) and hide idle ones (off by default; decision 020).
        public bool ShowArchived, HideIdle;
        public bool Shows(ClaudeSession t) { return !ClaudePace.Hidden(t.Pit, ShowArchived, HideIdle); }

        // The projects with a thread active in the period (or running), a project that needs you first, then ones with
        // something running, then the most recently active.
        public List<ClaudeProjectGroup> Groups(ClaudePeriod period)
        {
            DateTime start; if (!Starts.TryGetValue(period, out start)) start = DateTime.MinValue;
            return Threads.Where(t => t.Running || t.LastActive >= start)
                .GroupBy(t => t.Project, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ClaudeProjectGroup {
                    Folder = g.Key, Name = g.First().ProjectName, Collapsed = Collapsed.Contains(g.Key),
                    Threads = g.Where(Shows).OrderBy(t => ClaudePace.Parked(t.Pit) ? 5 : t.Running ? Rank(t.State) : 4).ThenByDescending(t => t.LastActive).ThenBy(t => t.Id, StringComparer.Ordinal).ToList(),
                    Hidden = g.Count(t => !Shows(t)), Seen = g.Max(t => t.LastActive),
                    Tokens = g.Sum(t => t.TokensFor(period)), Cost = g.Sum(t => t.CostFor(period)), Mix = GroupMix(g, period), Models = GroupModels(g, period),
                    Added = g.Sum(t => t.LinesFor(period)[0]), Removed = g.Sum(t => t.LinesFor(period)[1]), Replies = g.Sum(t => t.RepliesFor(period)), CacheReads = g.Sum(t => t.CacheReadsFor(period)) })
                .OrderBy(g => g.NeedsYou ? 0 : g.Running > 0 ? 1 : 2).ThenByDescending(g => g.LastActive).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList()
                .Concat(Known.Where(k => !Threads.Any(t => (t.Running || t.LastActive >= start) && String.Equals(t.Project, k.Key, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(k => k.Value.Value)
                    .Select(k => new ClaudeProjectGroup { Folder = k.Key, Name = k.Value.Key, Seen = k.Value.Value, Collapsed = Collapsed.Contains(k.Key) })).ToList();
        }
        // The threads' models weighted by their tokens.
        private static List<KeyValuePair<string, double>> GroupModels(IEnumerable<ClaudeSession> threads, ClaudePeriod period)
        {
            var sums = new Dictionary<string, double>();
            foreach (var t in threads) { long tokens = t.TokensFor(period); foreach (var m in t.ModelsFor(period)) { double v; sums.TryGetValue(m.Key, out v); sums[m.Key] = v + m.Value * tokens; } }
            double total = sums.Values.Sum();
            return total <= 0 ? new List<KeyValuePair<string, double>>() : sums.Select(p => new KeyValuePair<string, double>(p.Key, p.Value / total)).OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).ToList();
        }
        private static double[] GroupMix(IEnumerable<ClaudeSession> threads, ClaudePeriod period)
        {
            var sums = new double[3];
            foreach (var t in threads) { var mix = t.MixFor(period); long tokens = t.TokensFor(period); for (int i = 0; i < 3; i++) sums[i] += mix[i] * tokens; }
            double total = sums.Sum();
            return total <= 0 ? new double[3] : sums.Select(v => v / total).ToArray();
        }
        // A session has run and answered without rate limits: an API key or a logged-out Claude Code.
        public bool NoPlan { get { return Limits != null && !Limits.Known && Limits.MissingAt.HasValue; } }
        public List<ClaudeProjectUsage> UsageFor(ClaudePeriod period) { List<ClaudeProjectUsage> list; return Usage.TryGetValue(period, out list) ? list : new List<ClaudeProjectUsage>(); }

        // Needs you first (longest waiting first), then Stuck, Working, Done and Ready, newest first within each.
        public static List<ClaudeSession> Order(IEnumerable<ClaudeSession> sessions)
        {
            return sessions.OrderBy(s => Rank(s.State)).ThenBy(s => s.State == ClaudeState.NeedsYou ? s.Since.Ticks : -s.Since.Ticks).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        }
        public static int Rank(ClaudeState state) { return state == ClaudeState.NeedsYou ? 0 : state == ClaudeState.Stuck ? 1 : state == ClaudeState.Working ? 2 : state == ClaudeState.Done ? 3 : 4; }

        // The labelled fixture for renders and checks ("Sample" in the heading). Never shown as live data.
        public static ClaudeView Fixture(DateTime now, bool needsYou)
        {
            var view = new ClaudeView { Connected = true, Sample = true, LogsRead = true, Plan = ClaudePlanState.Current };
            foreach (ClaudePeriod period in Enum.GetValues(typeof(ClaudePeriod))) view.Starts[period] = ClaudeUsage.Start(period, null, now);
            // A running thread when state is given; an ended one (asleep) otherwise, last active minutes ago.
            Func<string, string, string, ClaudeState?, double, string, long, ClaudeSession> make = (id, project, title, state, minutes, model, tokens) => {
                var s = new ClaudeSession { Id = id, Project = "C:\\Code\\" + project, ProjectName = project, Title = title, State = state ?? ClaudeState.Ready, Running = state.HasValue,
                    Since = now.AddMinutes(-minutes), LastActive = now.AddMinutes(-minutes), Model = model, CanFocus = state.HasValue, Where = state.HasValue ? "Desktop" : "" };
                bool fiveHour = minutes <= 300, today = minutes <= 600;
                s.Tokens[ClaudePeriod.FiveHour] = fiveHour ? tokens : 0; s.Tokens[ClaudePeriod.Today] = today ? tokens * 2 : 0; s.Tokens[ClaudePeriod.Week] = tokens * 9;
                s.Cost[ClaudePeriod.FiveHour] = fiveHour ? tokens / 1e6 * 1.1 : 0; s.Cost[ClaudePeriod.Today] = today ? tokens / 1e6 * 2.2 : 0; s.Cost[ClaudePeriod.Week] = tokens / 1e6 * 9.9;
                return s;
            };
            var threads = new List<ClaudeSession> {
                make("sample-1", "api-server", "Fix the login timeout", needsYou ? ClaudeState.NeedsYou : ClaudeState.Working, 2, "Opus 5.5", 412000),
                make("sample-5", "api-server", "Rate limit middleware", null, 95, "Opus 5.5", 268000),
                make("sample-6", "api-server", "Bump dependencies", null, 1500, "Sonnet 5.5", 41000),
                make("sample-2", "widget-screen", "Claude card critters", ClaudeState.Working, 14, "Opus 5.5", 1830000),
                make("sample-7", "widget-screen", "Phone polling interval", ClaudeState.Done, 12, "Opus 5.5", 640000),
                make("sample-8", "widget-screen", "Fix the Arrange button", null, 180, "Opus 5.5", 905000),
                make("sample-9", "widget-screen", "Release notes for v0.29", null, 420, "Sonnet 5.5", 122000),
                make("sample-10", "widget-screen", "", null, 2900, "Opus 5.5", 77000),
                make("sample-3", "photo-manager", "Face grouping", ClaudeState.Done, 6, "Sonnet 5.5", 96400),
                make("sample-11", "photo-manager", "Thumbnail cache", null, 260, "Sonnet 5.5", 58000) };
            if (needsYou) threads[0].Message = "Claude needs your permission to use Bash";
            threads[7].Archived = true; threads[5].Resolved = true;
            // The pit wall's sample readings: each thread's mix, lines and live burn, and what its car does.
            var mixes = new Dictionary<string, double[]> { { "Opus 5.5", new[] { 1.0, 0, 0 } }, { "Sonnet 5.5", new[] { 0, 1.0, 0 } } };
            var lines = new Dictionary<string, long[]> { { "sample-1", new long[] { 412, 96 } }, { "sample-2", new long[] { 880, 120 } }, { "sample-7", new long[] { 590, 180 } }, { "sample-8", new long[] { 38, 0 } }, { "sample-3", new long[] { 120, 34 } }, { "sample-5", new long[] { 50, 30 } } };
            var rates = new Dictionary<string, double> { { "sample-1", needsYou ? 0 : 9000 }, { "sample-2", 41000 } };
            foreach (var t in threads)
            {
                foreach (ClaudePeriod period in Enum.GetValues(typeof(ClaudePeriod)))
                {
                    t.Mix[period] = mixes[t.Model];
                    t.Models[period] = t.TokensFor(period) > 0 ? new List<KeyValuePair<string, double>> { new KeyValuePair<string, double>(t.Model, t.Id == "sample-2" ? 0.7 : 1) } : new List<KeyValuePair<string, double>>();
                    if (t.Id == "sample-2" && t.TokensFor(period) > 0) t.Models[period].Add(new KeyValuePair<string, double>("Sonnet 5.5", 0.3));
                    long[] l; t.Lines[period] = t.TokensFor(period) > 0 && lines.TryGetValue(t.Id, out l) ? l : new long[2];
                }
                double rate; rates.TryGetValue(t.Id, out rate); t.Rate = rate;
                foreach (ClaudePeriod period in Enum.GetValues(typeof(ClaudePeriod))) { t.Replies[period] = (int)(t.TokensFor(period) / 9000); t.CacheReads[period] = t.TokensFor(period) * 12; }
                t.Car = ClaudePace.Car(t.MixFor(ClaudePeriod.Week), t.Model);
            }
            threads.First(t => t.Id == "sample-3").Compacting = true;
            // Context rings (decision 023): one thread near compaction, one past 60%.
            threads.First(t => t.Id == "sample-2").Context = 82; threads.First(t => t.Id == "sample-7").Context = 64;
            foreach (var t in threads) t.Pit = ClaudePace.State(t, now, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue);
            view.Threads = threads;
            view.Sessions = Order(threads.Where(t => t.Running));
            view.Limits = new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = needsYou ? 84 : 23, ResetsAt = now.AddMinutes(100) }, Week = new ClaudeLimitWindow { Percent = 41, ResetsAt = now.AddDays(3).AddHours(5) }, UpdatedAt = now.AddMinutes(-1), Source = "online" };
            view.Limits.Extra.Add(new KeyValuePair<string, ClaudeLimitWindow>("Opus week", new ClaudeLimitWindow { Percent = 18, ResetsAt = now.AddDays(3).AddHours(5) }));
            view.Usage[ClaudePeriod.FiveHour] = new List<ClaudeProjectUsage> { P("widget-screen", 1830000, 2.01), P("api-server", 412000, 0.45), P("photo-manager", 96400, 0.11) };
            view.Usage[ClaudePeriod.Today] = new List<ClaudeProjectUsage> { P("widget-screen", 3660000, 4.03), P("api-server", 824000, 0.91), P("photo-manager", 192800, 0.21), P("Other", 54000, 0.06) };
            view.Usage[ClaudePeriod.Week] = new List<ClaudeProjectUsage> { P("widget-screen", 16470000, 18.1), P("api-server", 3708000, 4.08), P("photo-manager", 867600, 0.95), P("scada-bom", 2210000, 2.43), P("Other", 412000, 0.45) };
            foreach (var key in view.Usage.Keys.ToList()) view.Usage[key] = view.Usage[key].OrderByDescending(p => p.Name == "Other" ? -1 : p.Tokens).ToList();
            // An hour of burn rising to now, and a lane per project for the last five hours.
            for (int i = 0; i < ClaudePace.BurnMinutes; i++) view.Burn[i] = Math.Max(2000, 18000 + 14000 * Math.Sin(i / 7.0) + i * 300 + (i > 48 ? (i - 48) * 2600 : 0));
            view.Rate = 50000; view.Scale = ClaudePace.Scale(view.Burn.Max());
            var profiles = new Dictionary<string, int[]> {
                { "widget-screen", new[] { 0, 0, 1, 2, 3, 2, 1, 0, 0, 1, 3, 4, 4, 3, 2, 3, 5, 6, 7, 6, 5, 6, 8, 9, 8, 7, 8, 9, 10, 9 } },
                { "api-server", new[] { 2, 3, 4, 5, 4, 3, 2, 1, 1, 2, 3, 3, 2, 1, 0, 0, 0, 1, 2, 2, 1, 0, 0, 0, 0, 1, 0, 0, 3, 4 } },
                { "photo-manager", new[] { 5, 6, 4, 3, 2, 1, 0, 0, 0, 0, 0, 0, 1, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2, 3, 1, 0, 2 } } };
            foreach (var pair in profiles) view.Lanes.Add(new ClaudeLane { Folder = "C:\\Code\\" + pair.Key, Name = pair.Key, Bins = pair.Value.Select(v => v * 61000.0).ToArray() });
            view.Facts = new ClaudeFacts { Tokens = 4676800, Cost = 11.4, Replies = 519, Prompt = 4400000, CacheRead = 4004000, Tools = 312, Asked = 41, Denied = 7, Added = 2100, Removed = 460, LinesThreads = 4, Telemetry = true };
            return view;
        }
        private static ClaudeProjectUsage P(string name, long tokens, double cost) { return new ClaudeProjectUsage { Folder = name == "Other" ? "" : "C:\\Code\\" + name, Name = name, Tokens = tokens, Cost = cost }; }
    }

    // Reads every source the card has (docs/design/claude-watcher.md, decisions 014 and 015) and merges them by session,
    // newest news winning: the hook files ClaudeHook.exe writes, Claude Code's own session list, its telemetry, its
    // logs for usage, the status line's plan limits, and the plan check with the widget's own Claude sign-in. Nothing
    // here writes to Claude Code's folder except the connector, and only when the user taps Connect or Disconnect.
    public sealed class ClaudeService : IDisposable
    {
        public static readonly TimeSpan Stale = TimeSpan.FromMinutes(15);
        // A session only telemetry knows about (no hook, not in Claude Code's list) shows while it was this recent.
        public static readonly TimeSpan TelemetryOnly = TimeSpan.FromMinutes(2);
        public readonly ClaudeConnector Connector;
        public readonly ClaudeUsage Usage;
        public readonly ClaudeRegistry Registry;
        public readonly ClaudeAccount Account;
        public readonly ClaudeTelemetry Telemetry;
        public readonly ClaudeAppThreads AppThreads;
        private readonly string bridge;
        private readonly Dictionary<string, Cached> files = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ClaudeTelemetryEvent> activity = new Dictionary<string, ClaudeTelemetryEvent>();
        // The window found for each running Claude Code process, looked up again after OwnerKept and forgotten when the
        // process leaves Claude Code's list, so a reused process id never finds an old window (review fix 11).
        private readonly Dictionary<int, KeyValuePair<DateTime, ClaudeProcesses.Owner>> owners = new Dictionary<int, KeyValuePair<DateTime, ClaudeProcesses.Owner>>();
        public static readonly TimeSpan OwnerKept = TimeSpan.FromMinutes(10);
        public int CachedOwners { get { return owners.Count; } }
        private readonly object activityGate = new object();
        private DateTime limitsWrite = DateTime.MinValue;
        private ClaudeLimits fileLimits, onlineLimits;
        private DateTime lastOnline = DateTime.MinValue;
        private bool onlineFailed;
        // The plan check's interval (Settings → Claude) and, after Anthropic turned a check away, the longer wait.
        private TimeSpan onlineEvery = TimeSpan.FromSeconds(ClaudeIntervals.PlanDefault), backoff = TimeSpan.Zero;
        private sealed class Cached { public DateTime Written; public long Length; public ClaudeSessionFile Session; }

        public ClaudeService(string bridgeFolder, string configFolder, string signInPath = null, int telemetryPort = ClaudeTelemetry.DefaultPort, ClaudeAccount account = null, ClaudeAppThreads appThreads = null)
        {
            AppThreads = appThreads ?? new ClaudeAppThreads();
            bridge = bridgeFolder;
            Connector = new ClaudeConnector(configFolder, bridgeFolder, telemetryPort);
            Usage = new ClaudeUsage(configFolder);
            Registry = new ClaudeRegistry(configFolder);
            Account = account ?? (signInPath == null ? null : new ClaudeAccount(signInPath));
            Telemetry = new ClaudeTelemetry(telemetryPort, Receive);
        }

        public string BridgeFolder { get { return bridge; } }
        public string ConfigFolder { get { return Connector.ConfigFolder; } }
        public bool Connected { get; private set; }
        public void CheckConnected() { Connected = Connector.IsConnected(); }
        public List<ClaudeSessionFile> Sessions { get { return files.Values.Select(c => c.Session).Where(s => s != null).ToList(); } }
        // Set when telemetry brought something new; the dashboard clears it after refreshing the card.
        public volatile bool TelemetryChanged;

        // ----- Telemetry (ClaudeTelemetry calls this on its own thread) -----

        public void Receive(List<ClaudeTelemetryEvent> events)
        {
            DateTime now = DateTime.UtcNow;
            // Usage first and outside activityGate: Usage.Add may wait for a scan, and the dashboard reads activity
            // (review fix 6).
            foreach (var e in events) if (e.Usage != null) Usage.Add(e.Usage, now);
            lock (activityGate)
                foreach (var e in events)
                {
                    if (Track(e, now)) continue;
                    ClaudeTelemetryEvent known;
                    if (!activity.TryGetValue(e.Session, out known) || known.At <= e.At)
                    {
                        // Keep what the newer event lacks (an "active" metric carries no event name worth showing).
                        if (known != null) { if (e.Repository.Length == 0) e.Repository = known.Repository; if (e.Service.Length == 0) e.Service = known.Service; if (e.Entrypoint.Length == 0) e.Entrypoint = known.Entrypoint; }
                        activity[e.Session] = e;
                    }
                }
            TelemetryChanged = true;
        }
        private Dictionary<string, ClaudeTelemetryEvent> Activity { get { lock (activityGate) return new Dictionary<string, ClaudeTelemetryEvent>(activity); } }

        // The v2 card's telemetry (decision 017): tool decisions and results, lines changed, and when each session last
        // asked for a tool, finished one, made an API request or hit an API error. Kept a week. Returns true for a lines
        // event, which says nothing about the session's state.
        private readonly List<ClaudeToolEntry> tools = new List<ClaudeToolEntry>();
        private readonly List<ClaudeLinesEntry> lines = new List<ClaudeLinesEntry>();
        private readonly Dictionary<string, double> seriesLast = new Dictionary<string, double>();
        private readonly Dictionary<string, DateTime[]> moments = new Dictionary<string, DateTime[]>();
        private const int Decided = 0, Finished = 1, Failed = 2, Requested = 3;
        private bool Track(ClaudeTelemetryEvent e, DateTime now)
        {
            if (e.At < now - ClaudeUsage.Kept) return e.Name == "lines";
            DateTime[] times;
            if (!moments.TryGetValue(e.Session, out times)) moments[e.Session] = times = new DateTime[4];
            Action<int> mark = i => { if (e.At > times[i]) times[i] = e.At; };
            switch (e.Name)
            {
                case "tool_decision": mark(Decided); tools.Add(new ClaudeToolEntry { At = e.At, Session = e.Session, Asked = e.Asked, Denied = e.Denied }); break;
                case "tool_result": mark(Finished); tools.Add(new ClaudeToolEntry { At = e.At, Session = e.Session, Result = true }); break;
                case "api_error": mark(Failed); break;
                case "api_request": mark(Requested); break;
                case "lines":
                    {
                        double value = e.Added + e.Removed, delta = value;
                        if (e.Cumulative)
                        {
                            // A cumulative series counts from its start: only what it grew by since the last export is new.
                            double last; delta = seriesLast.TryGetValue(e.Series, out last) && value >= last ? value - last : value;
                            seriesLast[e.Series] = value;
                        }
                        if (delta > 0) lines.Add(new ClaudeLinesEntry { At = e.At, Session = e.Session, Added = e.Added > 0 ? (long)Math.Round(delta) : 0, Removed = e.Removed > 0 ? (long)Math.Round(delta) : 0 });
                        return true;
                    }
            }
            if (tools.Count > 20000 || lines.Count > 20000) { DateTime oldest = now - ClaudeUsage.Kept; tools.RemoveAll(t => t.At < oldest); lines.RemoveAll(l => l.At < oldest); }
            return false;
        }
        // For the checks: what telemetry kept.
        public List<ClaudeToolEntry> ToolEntries { get { lock (activityGate) return tools.ToList(); } }
        public List<ClaudeLinesEntry> LinesEntries { get { lock (activityGate) return lines.ToList(); } }

        // ----- The hook files -----

        // Reads the session files that changed since last time. A session is alive until its own SessionEnd or its
        // process is gone (decision 023): a file goes when alive is checked and its process has exited (or the id now
        // belongs to a later process). A file with no known process, or one /clear ended, goes after 12 quiet hours.
        // Claude Code's own list is read alongside. Returns whether anything changed.
        public bool ReadSessions(DateTime now, bool checkAlive)
        {
            bool changed = false;
            string folder = ClaudeBridge.SessionsFolder(bridge);
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(folder))
                    foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.json"))
                    {
                        present.Add(file.FullName);
                        Cached cached;
                        if (files.TryGetValue(file.FullName, out cached) && cached.Written == file.LastWriteTimeUtc && cached.Length == file.Length) continue;
                        files[file.FullName] = new Cached { Written = file.LastWriteTimeUtc, Length = file.Length, Session = ClaudeBridge.Read(file.FullName) };
                        changed = true;
                    }
            }
            catch (Exception) { }
            foreach (var path in files.Keys.Where(p => !present.Contains(p)).ToList()) { Gone(files[path].Session); files.Remove(path); changed = true; }
            foreach (var pair in files.ToList())
            {
                var s = pair.Value.Session; if (s == null) continue;
                bool forgotten = (s.Pid <= 0 || s.Ended) && now - ClaudeBridge.FromEpoch(Math.Max(s.LastEvent, s.Since)) > ClaudeBridge.Forgotten;
                if (forgotten || (checkAlive && s.Pid > 0 && !(SessionAlive ?? (Func<ClaudeSessionFile, bool>)DefaultAlive)(s)))
                {
                    // Closed without SessionEnd (the terminal went away): the file goes too, so it doesn't come back.
                    try { File.Delete(pair.Key); } catch (Exception) { }
                    Gone(s); files.Remove(pair.Key); changed = true;
                }
            }
            Func<ClaudeRegistryEntry, bool> listed;
            if (RegistryAlive != null) { var test = RegistryAlive; listed = e => test(e.Pid); }
            else listed = e => e.Pid > 0 && ClaudeProcesses.Alive(e.Pid, e.Started);
            changed |= Registry.Read(listed, checkAlive);
            changed |= AppThreads.Read(now);
            return changed;
        }
        // Sessions whose hook file went (SessionEnd, or their process left): their logs no longer make them running.
        private readonly HashSet<string> gone = new HashSet<string>(StringComparer.Ordinal);
        private void Gone(ClaudeSessionFile s) { if (s == null) return; if (gone.Count > 1000) gone.Clear(); gone.Add(s.Id); }
        // A hook file's process: running, and started before the session was first seen (Since only moves later).
        private static bool DefaultAlive(ClaudeSessionFile s) { return ClaudeProcesses.Alive(s.Pid, ClaudeBridge.FromEpoch(s.Since)); }
        // The checks replace the process tests, since their sample process ids are made up.
        public Func<int, bool> RegistryAlive;
        public Func<ClaudeSessionFile, bool> SessionAlive;

        // ----- Telemetry's port (decision 023) -----

        // Listens where settings.json sends telemetry (the port Connect wrote), or on the default port.
        public bool StartTelemetry()
        {
            int port = Connector.ConfiguredPort();
            return Telemetry.Start(port > 0 ? port : Connector.TelemetryPort);
        }
        // Connect binds first, then writes the port it got into settings.json: the default, or the next free one of
        // ClaudeTelemetry.PortsTried. With every one taken it writes the default and the card says telemetry is off.
        public void Connect(string hookSource)
        {
            bool bound = Telemetry.StartFirstFree(Connector.TelemetryPort);
            Connector.Connect(hookSource, bound ? Telemetry.Port : Connector.TelemetryPort);
        }
        // "Telemetry off: port 43832 in use", while connected and the receiver can't listen where telemetry is sent.
        public string TelemetryWarning()
        {
            if (!Connected || Telemetry.Running || Telemetry.Problem == null) return "";
            int port = Connector.ConfiguredPort();
            return port > 0 ? "Telemetry off: port " + port + " in use" : "";
        }

        // The plan limits Claude Code's status line last wrote. Returns whether they changed.
        public bool ReadLimits()
        {
            try
            {
                string path = ClaudeBridge.LimitsPath(bridge);
                DateTime written = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
                if (written == limitsWrite) return false;
                limitsWrite = written; fileLimits = written == DateTime.MinValue ? null : ClaudeBridge.ReadLimits(bridge);
                return true;
            }
            catch (Exception) { return false; }
        }

        // The newer of the status line's numbers and the plan check's.
        public ClaudeLimits Limits
        {
            get
            {
                if (onlineLimits == null) return fileLimits;
                if (fileLimits == null || !fileLimits.Known) return onlineLimits;
                return onlineLimits.UpdatedAt > fileLimits.UpdatedAt ? onlineLimits : fileLimits;
            }
        }
        // For the checks: the plan check's numbers as if just read.
        public void UseOnlineLimits(ClaudeLimits limits) { onlineLimits = limits; onlineFailed = false; }

        // ----- The plan check with the widget's own sign-in (decision 015) -----

        public ClaudePlanState Plan
        {
            get
            {
                if (Account == null || Account.Expired) return Account != null ? ClaudePlanState.Expired : ClaudePlanState.SignedOut;
                if (!Account.SignedIn) return ClaudePlanState.SignedOut;
                if (onlineFailed) return ClaudePlanState.Failed;
                if (Account.NoMeters) return ClaudePlanState.NoMeters;
                return onlineLimits == null ? ClaudePlanState.Reading : ClaudePlanState.Current;
            }
        }

        // Every PlanEvery while signed in (300 s unless Settings → Claude says otherwise), whether or not Claude Code is
        // running; longer after Anthropic turned a check away (ClaudeIntervals.Backoff).
        public TimeSpan PlanEvery { get { return onlineEvery; } set { onlineEvery = value < TimeSpan.FromSeconds(ClaudeIntervals.PlanMinimum) ? TimeSpan.FromSeconds(ClaudeIntervals.PlanMinimum) : value; } }
        public TimeSpan NextWait { get { return backoff > TimeSpan.Zero ? backoff : onlineEvery; } }
        public bool BackingOff { get { return backoff > TimeSpan.Zero; } }
        public bool OnlineDue(DateTime now) { return Account != null && Account.SignedIn && now - lastOnline >= NextWait; }
        public void CheckSoon() { lastOnline = DateTime.MinValue; }

        public async Task<bool> CheckOnlineAsync(DateTime now)
        {
            lastOnline = now;
            if (Account == null) return false;
            try
            {
                var limits = await Account.UsageAsync(DateTime.UtcNow).ConfigureAwait(false);
                backoff = TimeSpan.Zero;
                // No numbers: the sign-in ended or the plan has no meters; the old online numbers no longer stand.
                if (limits == null) { onlineFailed = false; if (Account.NoMeters) onlineLimits = null; return true; }
                onlineLimits = limits; onlineFailed = false;
                return true;
            }
            catch (Exception e) { Refused(e); return true; }
        }
        // A failed check: Anthropic turning it away (429, or a 5xx) doubles the wait up to 15 minutes; anything else
        // (no internet, an unreadable answer) tries again at the chosen interval.
        public void Refused(Exception e)
        {
            onlineFailed = true;
            var refused = e as ClaudeHttpException;
            backoff = refused != null && refused.Throttled ? ClaudeIntervals.Backoff(onlineEvery, backoff) : TimeSpan.Zero;
        }

        // ----- What the card shows -----

        // The folder each session belongs to: the one it started in. The log's first cwd says that best; the hook's and
        // Claude Code's list's cwd follow the shell when it changes folders (into a worktree, say), so they only stand
        // in until the log has a line.
        private Dictionary<string, string> SessionFolders()
        {
            var result = new Dictionary<string, string>();
            foreach (var e in Registry.Entries) if (e.Folder.Length > 0) result[e.Session] = e.Folder;
            foreach (var s in Sessions) if (s.Project.Length > 0) result[s.Id] = s.Project;
            foreach (var pair in Usage.Folders) if (!String.IsNullOrEmpty(pair.Value)) result[pair.Key] = pair.Value;
            // The Claude app records the folder a Code-tab thread started in, whatever its shell did since.
            foreach (var t in AppThreads.Threads) if (t.Folder.Length > 0) result[t.Session] = t.Folder;
            return result;
        }

        // A thread's project: the folder it started in, with a worktree (…\.claude\worktrees\x or …\.worktrees\x)
        // counted under the folder it was made from.
        public static string ProjectOf(string folder)
        {
            string key = ClaudeUsage.Key(folder);
            foreach (var marker in new[] { "\\.claude\\worktrees\\", "\\.worktrees\\" })
            {
                int at = key.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (at > 0) return key.Substring(0, at);
            }
            return key;
        }
        // A reply's project folder; telemetry's replies come without one, so the session's or the repository's.
        private static Func<ClaudeUsageEntry, string> FolderOf(Dictionary<string, string> folders, Dictionary<string, ClaudeTelemetryEvent> activity)
        {
            return e => {
                string folder; if (folders.TryGetValue(e.Session, out folder)) return ProjectOf(folder);
                if (e.Folder.Length > 0) return ProjectOf(e.Folder);
                ClaudeTelemetryEvent a; if (activity.TryGetValue(e.Session, out a) && a.Repository.Length > 0) return ProjectOf(a.Repository);
                return "";
            };
        }

        // Whether Settings → Claude shows a project (by folder): its own tick, else Show new projects automatically.
        public static Func<string, bool> Filter(IEnumerable<ClaudeProjectChoice> choices, bool showNew)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in choices ?? Enumerable.Empty<ClaudeProjectChoice>()) if (c != null && c.Folder != null) map[ClaudeUsage.Key(c.Folder)] = c.Shown;
            return folder => { bool shown; return map.TryGetValue(ClaudeUsage.Key(folder), out shown) ? shown : showNew; };
        }

        // How much later than the Claude app's record the logs may run before a completed thread counts as active again.
        public static readonly TimeSpan ResolvedSlack = TimeSpan.FromMinutes(2);
        public ClaudeView View(DateTime now, List<ClaudeUsageEntry> usage, Func<string, bool> shown = null)
        {
            var view = new ClaudeView { Connected = Connected, Limits = Limits, LogsFailed = Usage.Failed, LogsRead = Usage.Scanned, Plan = Plan };
            var starts = new Dictionary<ClaudePeriod, DateTime>();
            foreach (ClaudePeriod period in Enum.GetValues(typeof(ClaudePeriod))) starts[period] = ClaudeUsage.Start(period, view.Limits, now);
            var act = Activity;
            var folders = SessionFolders();
            var folderOf = FolderOf(folders, act);

            var sessions = new Dictionary<string, ClaudeSession>();
            var stateAt = new Dictionary<string, DateTime>();
            // A name the user gave a running session in Claude Code (its rename).
            var named = new Dictionary<string, string>();
            // Sessions /clear ended (decision 023): kept as ended threads while their process goes on.
            var ended = new Dictionary<string, DateTime>();
            foreach (var file in Sessions)
            {
                sessions[file.Id] = new ClaudeSession { Id = file.Id, State = file.State, Since = ClaudeBridge.FromEpoch(file.Since), Project = file.Project,
                    Title = file.Title, Message = file.Message, Model = ModelName(file.Model), Pid = file.Pid, WindowPid = file.WindowPid, Window = file.Window,
                    Compacting = file.Compacting && now - ClaudeBridge.FromEpoch(Math.Max(file.LastEvent, file.Since)) < ClaudePace.CompactForgotten,
                    Reason = file.State == ClaudeState.Stuck ? file.Error : "", ReasonText = file.State == ClaudeState.Stuck ? file.ErrorText : "", Context = file.Context,
                    Workers = file.Workers.Count, Tasks = file.OpenTasks, Failed = file.Failed, Denied = file.Denied, LastError = file.LastError };
                stateAt[file.Id] = ClaudeBridge.FromEpoch(file.Since);
                if (file.Ended) ended[file.Id] = ClaudeBridge.FromEpoch(Math.Max(file.LastEvent, file.Since));
            }
            var listedPids = new HashSet<int>(Registry.Entries.Select(e => e.Pid));
            foreach (var pid in owners.Keys.Where(p => !listedPids.Contains(p)).ToList()) owners.Remove(pid);
            foreach (var entry in Registry.Entries)
            {
                ClaudeSession s;
                var listed = ClaudeRegistry.State(entry);
                bool hooked = sessions.TryGetValue(entry.Session, out s);
                if (!hooked) { s = new ClaudeSession { Id = entry.Session, State = ClaudeState.Ready, Since = entry.Started, Project = entry.Folder }; sessions[entry.Session] = s; stateAt[entry.Session] = DateTime.MinValue; }
                if (s.Project.Length == 0) s.Project = entry.Folder;
                if (s.Pid == 0) s.Pid = entry.Pid;
                if (entry.NamedByUser && !String.IsNullOrWhiteSpace(entry.Name)) named[entry.Session] = entry.Name;
                s.Where = ClaudeRegistry.Where(entry.Entrypoint);
                if (s.Window == 0 && s.WindowPid == 0 && entry.Pid > 0)
                {
                    KeyValuePair<DateTime, ClaudeProcesses.Owner> cached;
                    if (!owners.TryGetValue(entry.Pid, out cached) || now - cached.Key > OwnerKept) { cached = new KeyValuePair<DateTime, ClaudeProcesses.Owner>(now, ClaudeProcesses.OwnerOf(entry.Pid)); owners[entry.Pid] = cached; }
                    s.Window = cached.Value.Window; s.WindowPid = cached.Value.WindowPid;
                }
                // Claude Code's own list wins when it changed after the hook's news, except that its "idle" doesn't
                // undo Stuck: only a new turn (busy or waiting) does (decision 023).
                bool keepStuck = hooked && s.State == ClaudeState.Stuck && listed == ClaudeState.Done;
                if (listed.HasValue && !keepStuck && entry.StatusChanged > stateAt[entry.Session].AddSeconds(1))
                {
                    var next = listed.Value;
                    // Idle is Done after a turn, Ready before one.
                    if (next == ClaudeState.Done && (hooked ? s.State == ClaudeState.Ready : usage == null || !usage.Any(u => u.Session == entry.Session))) next = ClaudeState.Ready;
                    if (next != s.State || !hooked) s.Since = entry.StatusChanged;
                    s.State = next;
                    s.Message = next == ClaudeState.NeedsYou ? ClaudeRegistry.Waiting(entry.WaitingFor) : "";
                    stateAt[entry.Session] = entry.StatusChanged;
                }
            }
            foreach (var pair in act)
            {
                var e = pair.Value;
                ClaudeSession s;
                if (!sessions.TryGetValue(e.Session, out s))
                {
                    if (now - e.At > TelemetryOnly || e.Name == "active") continue;
                    string folder; folders.TryGetValue(e.Session, out folder);
                    s = new ClaudeSession { Id = e.Session, State = ClaudeState.Working, Since = e.At, Project = folder ?? e.Repository };
                    sessions[e.Session] = s; stateAt[e.Session] = e.At;
                }
                if (s.Where.Length == 0) s.Where = ClaudeRegistry.Where(e.Entrypoint);
                // A prompt, a tool or a request after the last news means it's working again.
                bool working = e.Name == "user_prompt" || e.Name == "tool_decision" || e.Name == "tool_result" || e.Name == "api_request";
                DateTime at; stateAt.TryGetValue(e.Session, out at);
                if (working && e.At > at.AddSeconds(1) && s.State != ClaudeState.Working)
                {
                    s.State = ClaudeState.Working; s.Since = e.At; s.Message = ""; s.Reason = ""; s.ReasonText = ""; stateAt[e.Session] = e.At;
                }
            }

            foreach (var s in sessions.Values)
            {
                DateTime endedAt;
                s.Running = !ended.TryGetValue(s.Id, out endedAt);
                if (!s.Running) { s.State = ClaudeState.Ready; s.Since = endedAt; s.LastActive = endedAt; s.Compacting = false; s.Reason = ""; s.ReasonText = ""; }
            }
            // The ended threads of the last week: the Claude app's list (archived ones marked, decision 020) and the
            // logs, which know every way Claude Code runs.
            var lastSeen = Usage.LastSeen; var titles = Usage.Titles; var prompts = Usage.Prompts;
            var app = new Dictionary<string, ClaudeAppThread>();
            foreach (var t in AppThreads.Threads) { ClaudeAppThread known; if (!app.TryGetValue(t.Session, out known) || known.LastActive < t.LastActive) app[t.Session] = t; }
            DateTime oldest = starts[ClaudePeriod.Week];
            var perSession = usage == null ? new Dictionary<string, List<ClaudeUsageEntry>>() : usage.GroupBy(u => u.Session).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var id in app.Keys.Concat(lastSeen.Keys).Distinct().ToList())
            {
                if (sessions.ContainsKey(id)) continue;
                ClaudeAppThread a; app.TryGetValue(id, out a);
                DateTime seen; lastSeen.TryGetValue(id, out seen);
                DateTime last = a != null && a.LastActive > seen ? a.LastActive : seen;
                if (last < oldest) continue;
                // Archived threads are kept, marked, and hidden by the card unless Settings → Claude shows them.
                var thread = new ClaudeSession { Id = id, State = ClaudeState.Ready, Since = last, LastActive = last, Archived = a != null && a.Archived };
                // A session only the logs know, with a reply in the last two minutes, is running (decision 023): one
                // started before Connect by a Claude Code that keeps no session list. One whose hook file went is not.
                // The reply's own time counts, not the log file's (Claude Code touches a log long after a turn).
                List<ClaudeUsageEntry> replies; DateTime replied = DateTime.MinValue;
                if (seen > DateTime.MinValue && perSession.TryGetValue(id, out replies) && replies.Count > 0) replied = replies.Max(u => u.At);
                if (replied > DateTime.MinValue && now - replied <= TelemetryOnly && replied <= now + TimeSpan.FromMinutes(1) && !gone.Contains(id) && (a == null || !a.Archived))
                { thread.Running = true; thread.State = ClaudeState.Working; thread.Since = replied; }
                sessions[id] = thread;
            }

            var list = new List<ClaudeSession>();
            foreach (var s in sessions.Values)
            {
                string started; if (folders.TryGetValue(s.Id, out started)) s.Project = started;
                s.Project = ProjectOf(s.Project);
                if (shown != null && !shown(s.Project)) continue;
                s.ProjectName = ClaudeBridge.ProjectName(s.Project);
                ClaudeAppThread a; app.TryGetValue(s.Id, out a);
                // The name (decision 018): one the user typed (a rename in Claude Code's list or its log), else one
                // generated (Claude Code's status line, the Claude app's), else the first line of the first request.
                // Placeholders such as "Remote Control session (from a project)" count as none; the card falls back
                // to a label from the id.
                string rename, typed, asked; named.TryGetValue(s.Id, out rename); titles.TryGetValue(s.Id, out typed); prompts.TryGetValue(s.Id, out asked);
                s.Title = ClaudeTitles.Pick(rename, typed, s.Title, a == null ? null : a.Title, asked);
                // Where it runs, beside the name: a Remote Control thread says so, running or ended.
                if (a != null && a.Remote) s.Where = "Remote Control";
                else if (!s.Running && s.Where.Length == 0 && a != null) s.Where = "Desktop";
                DateTime seen; lastSeen.TryGetValue(s.Id, out seen);
                // Resolved only while the logs hold no reply newer than the app's record (a new turn un-resolves it). The
                // log file's own time doesn't tell: Claude Code touches it about an hour after a turn (review, 1 Nov 2026).
                List<ClaudeUsageEntry> replied; DateTime lastReply = DateTime.MinValue;
                if (perSession.TryGetValue(s.Id, out replied) && replied.Count > 0) lastReply = replied.Max(u => u.At);
                s.Resolved = a != null && a.Completed && !a.Archived && lastReply <= a.LastActive + ResolvedSlack;
                foreach (var at in new[] { s.Since, seen, a == null ? DateTime.MinValue : a.LastActive }) if (at > s.LastActive) s.LastActive = at;
                s.CanFocus = s.Running && (s.Window != 0 || s.WindowPid != 0) && ClaudeProcesses.Window(s.Window, s.WindowPid) != IntPtr.Zero;
                List<ClaudeUsageEntry> mine;
                if (usage != null && perSession.TryGetValue(s.Id, out mine))
                {
                    if (s.Model.Length == 0) { var last = mine.Where(u => u.Model.Length > 0).OrderByDescending(u => u.At).FirstOrDefault(); if (last != null) s.Model = ModelName(last.Model); }
                    if (mine.Count > 0) { var newest = mine.Max(u => u.At); if (newest > s.LastActive) s.LastActive = newest; }
                    foreach (var pair in starts) { s.Tokens[pair.Key] = ClaudeUsage.Total(mine, pair.Value); s.Cost[pair.Key] = ClaudeUsage.Cost(mine, pair.Value); s.Replies[pair.Key] = mine.Count(u => u.At >= pair.Value); s.CacheReads[pair.Key] = mine.Where(u => u.At >= pair.Value).Sum(u => u.CacheRead); }
                }
                else if (usage != null) foreach (var pair in starts) { s.Tokens[pair.Key] = 0; s.Cost[pair.Key] = 0; s.Replies[pair.Key] = 0; s.CacheReads[pair.Key] = 0; }
                list.Add(s);
            }
            view.Threads = list;
            // Every shown project with any known use: the Claude app's threads (any age) and the logs of the last week.
            Action<string, DateTime> remember = (folder, at) => {
                string key = ProjectOf(folder);
                if (key.Length == 0 || (shown != null && !shown(key))) return;
                KeyValuePair<string, DateTime> was;
                if (!view.Known.TryGetValue(key, out was) || was.Value < at) view.Known[key] = new KeyValuePair<string, DateTime>(ClaudeBridge.ProjectName(key), at);
            };
            foreach (var t in app.Values) remember(t.Folder, t.LastActive);
            foreach (var pair in lastSeen) { string folder; if (folders.TryGetValue(pair.Key, out folder)) remember(folder, pair.Value); }
            foreach (var t in list) remember(t.Project, t.LastActive);
            view.Sessions = ClaudeView.Order(list.Where(s => s.Running));
            view.TelemetryWarning = TelemetryWarning();
            foreach (var pair in starts) view.Starts[pair.Key] = pair.Value;
            if (usage != null) foreach (var pair in starts) view.Usage[pair.Key] = ClaudeUsage.ByProject(usage, pair.Value, 6, folderOf, shown);
            PitWall(view, now, usage, perSession, folderOf, shown);
            return view;
        }

        // The v2 card's readings (decision 017): each thread's car, burn, mix and lines; the hour's burn and the rev
        // counter's scale; a lane per project for the last five hours; and today's facts. Only shown projects count.
        private void PitWall(ClaudeView view, DateTime now, List<ClaudeUsageEntry> usage, Dictionary<string, List<ClaudeUsageEntry>> perSession, Func<ClaudeUsageEntry, string> folderOf, Func<string, bool> shown)
        {
            List<ClaudeToolEntry> toolList; List<ClaudeLinesEntry> lineList; Dictionary<string, DateTime[]> times;
            lock (activityGate) { toolList = tools.ToList(); lineList = lines.ToList(); times = moments.ToDictionary(p => p.Key, p => (DateTime[])p.Value.Clone()); }
            var ids = new HashSet<string>(view.Threads.Select(t => t.Id));
            var mine = usage == null ? new List<ClaudeUsageEntry>() : usage.Where(e => shown == null || shown(ClaudeUsage.Key(folderOf(e)))).ToList();
            var minute = TimeSpan.FromMinutes(1);
            view.Burn = ClaudePace.Bins(mine, now, minute, ClaudePace.BurnMinutes);
            view.Rate = ClaudePace.Rate(mine, now);
            view.Scale = ClaudePace.Scale(Math.Max(view.Burn.Max(), view.Rate));
            // Tools and lines from the logs (review fix 2); telemetry only says which tool calls asked you.
            var logTools = Usage.Tools; var logLines = Usage.Lines;
            var linesBySession = logLines.Where(l => ids.Contains(l.Session)).GroupBy(l => l.Session).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var t in view.Threads)
            {
                List<ClaudeUsageEntry> entries; if (!perSession.TryGetValue(t.Id, out entries)) entries = new List<ClaudeUsageEntry>();
                List<ClaudeLinesEntry> changed; linesBySession.TryGetValue(t.Id, out changed);
                foreach (var pair in view.Starts)
                {
                    var from = pair.Value;
                    t.Mix[pair.Key] = ClaudePace.Mix(entries.Where(e => e.At >= from));
                    t.Models[pair.Key] = ClaudePace.Models(entries.Where(e => e.At >= from));
                    t.Lines[pair.Key] = changed == null ? new long[2] : new[] { changed.Where(l => l.At >= from).Sum(l => l.Added), changed.Where(l => l.At >= from).Sum(l => l.Removed) };
                }
                t.Rate = t.Running ? ClaudePace.Rate(entries, now) : 0;
                t.Car = ClaudePace.Car(t.MixFor(ClaudePeriod.Week), t.Model);
                DateTime[] m; if (!times.TryGetValue(t.Id, out m)) m = new DateTime[4];
                t.Pit = ClaudePace.State(t, now, m[Decided], m[Finished], m[Failed], m[Requested]);
            }
            // A lane for every project used in the last five hours (up to six). Ali asked for projects only, no thread
            // lanes under them (1 Nov 2026).
            DateTime fiveHours = now - ClaudePace.FiveHours;
            var bin = TimeSpan.FromMinutes(10);
            var projects = mine.Where(e => e.At >= fiveHours).GroupBy(e => ClaudeUsage.Key(folderOf(e)), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0).OrderByDescending(g => g.Sum(e => e.Tokens)).Take(ClaudePace.MostLanes).ToList();
            view.Lanes = projects.Select(g => new ClaudeLane { Folder = g.Key, Name = ClaudeBridge.ProjectName(g.Key), Bins = ClaudePace.Bins(g, now, bin, ClaudePace.LaneBins) }).ToList();
            // Today's facts.
            DateTime today = view.Starts[ClaudePeriod.Today];
            var todays = mine.Where(e => e.At >= today).ToList();
            var todayDecisions = toolList.Where(t => t.At >= today && ids.Contains(t.Session) && !t.Result).ToList();
            int todayCalls = logTools.Count(t => t.At >= today && ids.Contains(t.Session));
            var todayLines = logLines.Where(l => l.At >= today && ids.Contains(l.Session)).ToList();
            view.Facts = new ClaudeFacts {
                Tokens = todays.Sum(e => e.Tokens), Cost = todays.Sum(e => e.Cost), Replies = todays.Count, Prompt = todays.Sum(e => e.Prompt), CacheRead = todays.Sum(e => e.CacheRead),
                Tools = todayCalls, Asked = todayDecisions.Count(t => t.Asked), Denied = todayDecisions.Count(t => t.Denied),
                Added = todayLines.Sum(l => l.Added), Removed = todayLines.Sum(l => l.Removed), LinesThreads = todayLines.Select(l => l.Session).Distinct().Count(),
                Telemetry = Telemetry.LastReceived > DateTime.MinValue || toolList.Count > 0 || lineList.Count > 0 };
            view.Facts.AskedPartial = view.Facts.Telemetry && todayDecisions.Count < todayCalls;
            view.MadeAt = now;
        }

        // "claude-opus-5-5-20261001" reads "Opus 5.5".
        public static string ModelName(string model)
        {
            if (String.IsNullOrWhiteSpace(model)) return "";
            string name = model.Trim().ToLowerInvariant();
            int at = name.IndexOf("claude-", StringComparison.Ordinal);
            if (at < 0) return model.Trim();
            var parts = name.Substring(at + 7).Split('-', '[', '@');
            if (parts.Length == 0 || parts[0].Length == 0 || Char.IsDigit(parts[0][0])) return model.Trim();
            var version = parts.Skip(1).TakeWhile(p => p.Length > 0 && p.Length <= 2 && p.All(Char.IsDigit)).ToList();
            return Char.ToUpperInvariant(parts[0][0]) + parts[0].Substring(1) + (version.Count > 0 ? " " + String.Join(".", version) : "");
        }

        // Every project the sources know, newest activity first, with this week's usage, for Settings → Claude.
        public List<ClaudeProjectInfo> Projects(DateTime now, Func<string, bool> shown)
        {
            var act = Activity;
            var folders = SessionFolders();
            var folderOf = FolderOf(folders, act);
            var found = new Dictionary<string, ClaudeProjectInfo>(StringComparer.OrdinalIgnoreCase);
            var threads = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            Action<string, DateTime> seen = (folder, at) => {
                string key = ProjectOf(folder);
                if (key.Length == 0) return;
                ClaudeProjectInfo info;
                if (!found.TryGetValue(key, out info)) { info = new ClaudeProjectInfo { Folder = key, Name = ClaudeBridge.ProjectName(key) }; found[key] = info; }
                if (at > info.LastUsed) info.LastUsed = at;
            };
            DateTime week = now.AddDays(-7);
            foreach (var e in Usage.Entries)
            {
                string folder = folderOf(e); seen(folder, e.At);
                string key = ProjectOf(folder);
                if (e.At >= week && found.ContainsKey(key)) { found[key].WeekTokens += e.Tokens; found[key].WeekCost += e.Cost; }
            }
            foreach (var pair in Usage.LastSeen) { string folder; if (folders.TryGetValue(pair.Key, out folder)) seen(folder, pair.Value); }
            Func<string, string, string> startedIn = (id, folder) => { string start; return folders.TryGetValue(id, out start) ? start : folder; };
            foreach (var e in Registry.Entries) seen(startedIn(e.Session, e.Folder), e.StatusChanged);
            foreach (var s in Sessions) seen(startedIn(s.Id, s.Project), ClaudeBridge.FromEpoch(Math.Max(s.LastEvent, s.Since)));
            foreach (var a in act.Values) { string folder; seen(folders.TryGetValue(a.Session, out folder) ? folder : a.Repository, a.At); }
            foreach (var t in AppThreads.Threads) seen(startedIn(t.Session, t.Folder), t.LastActive);
            // Each project's threads this week, as the card counts them.
            Action<string, string, DateTime> thread = (id, folder, at) => {
                string key = ProjectOf(folder); if (key.Length == 0 || at < week || !found.ContainsKey(key)) return;
                HashSet<string> ids; if (!threads.TryGetValue(key, out ids)) threads[key] = ids = new HashSet<string>();
                ids.Add(id);
            };
            foreach (var pair in Usage.LastSeen) { string folder; if (folders.TryGetValue(pair.Key, out folder)) thread(pair.Key, folder, pair.Value); }
            foreach (var t in AppThreads.Threads) thread(t.Session, startedIn(t.Session, t.Folder), t.LastActive);
            foreach (var e in Registry.Entries) thread(e.Session, startedIn(e.Session, e.Folder), now);
            foreach (var s in Sessions) thread(s.Id, startedIn(s.Id, s.Project), now);
            foreach (var info in found.Values) { HashSet<string> ids; info.Threads = threads.TryGetValue(info.Folder, out ids) ? ids.Count : 0; info.Shown = shown == null || shown(info.Folder); }
            return found.Values.OrderByDescending(p => p.LastUsed).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void Dispose() { Telemetry.Dispose(); if (Account != null) Account.Dispose(); }
    }
}
