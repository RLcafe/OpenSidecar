using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace WidgetScreen
{
    // A Claude Code session as the card and the phone show it.
    public sealed class ClaudeSession
    {
        public string Id = "", Project = "", ProjectName = "", Title = "", Message = "", Model = "";
        public ClaudeState State;
        public DateTime Since;
        public int Pid, WindowPid;
        public long Window;
        public bool CanFocus;
        public readonly Dictionary<ClaudePeriod, long> Tokens = new Dictionary<ClaudePeriod, long>();
        public string Shown { get { return String.IsNullOrWhiteSpace(Title) ? ProjectName : Title; } }
        public long TokensFor(ClaudePeriod period) { long value; return Tokens.TryGetValue(period, out value) ? value : 0; }
    }

    // Everything the Claude card shows at one moment.
    public sealed class ClaudeView
    {
        public bool Connected, Sample, LogsFailed, LogsRead;
        public List<ClaudeSession> Sessions = new List<ClaudeSession>();
        public ClaudeLimits Limits;
        public readonly Dictionary<ClaudePeriod, List<KeyValuePair<string, long>>> Usage = new Dictionary<ClaudePeriod, List<KeyValuePair<string, long>>>();
        public int NeedsYou { get { return Sessions.Count(s => s.State == ClaudeState.NeedsYou); } }
        // A session has run and answered without rate limits: an API key or a logged-out Claude Code.
        public bool NoPlan { get { return Limits != null && !Limits.Known && Limits.MissingAt.HasValue; } }
        public List<KeyValuePair<string, long>> UsageFor(ClaudePeriod period) { List<KeyValuePair<string, long>> list; return Usage.TryGetValue(period, out list) ? list : new List<KeyValuePair<string, long>>(); }

        // Needs you first (longest waiting first), then Working, Done and Ready, newest first within each.
        public static List<ClaudeSession> Order(IEnumerable<ClaudeSession> sessions)
        {
            return sessions.OrderBy(s => Rank(s.State)).ThenBy(s => s.State == ClaudeState.NeedsYou ? s.Since.Ticks : -s.Since.Ticks).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        }
        public static int Rank(ClaudeState state) { return state == ClaudeState.NeedsYou ? 0 : state == ClaudeState.Working ? 1 : state == ClaudeState.Done ? 2 : 3; }

        // The labelled fixture for renders and checks ("Sample" in the heading). Never shown as live data.
        public static ClaudeView Fixture(DateTime now, bool needsYou)
        {
            var view = new ClaudeView { Connected = true, Sample = true, LogsRead = true };
            Func<string, string, string, ClaudeState, double, string, long, ClaudeSession> make = (id, project, title, state, minutes, model, tokens) => {
                var s = new ClaudeSession { Id = id, Project = "C:\\Code\\" + project, ProjectName = project, Title = title, State = state, Since = now.AddMinutes(-minutes), Model = model, CanFocus = true };
                s.Tokens[ClaudePeriod.FiveHour] = tokens; s.Tokens[ClaudePeriod.Today] = tokens * 2; s.Tokens[ClaudePeriod.Week] = tokens * 9;
                return s;
            };
            var list = new List<ClaudeSession> {
                make("sample-1", "api-server", "Fix the login timeout", needsYou ? ClaudeState.NeedsYou : ClaudeState.Working, 2, "Opus 5.5", 412000),
                make("sample-2", "widget-screen", "Claude card critters", ClaudeState.Working, 14, "Opus 5.5", 1830000),
                make("sample-3", "photo-manager", "", ClaudeState.Done, 6, "Sonnet 5.5", 96400),
                make("sample-4", "notes", "Tidy the README", ClaudeState.Ready, 1, "Haiku 5.5", 0) };
            if (needsYou) list[0].Message = "Claude needs your permission to use Bash";
            view.Sessions = Order(list);
            view.Limits = new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = needsYou ? 84 : 23, ResetsAt = now.AddMinutes(100) }, Week = new ClaudeLimitWindow { Percent = 41, ResetsAt = now.AddDays(3).AddHours(5) }, UpdatedAt = now.AddMinutes(-1) };
            view.Usage[ClaudePeriod.FiveHour] = new List<KeyValuePair<string, long>> { P("widget-screen", 1830000), P("api-server", 412000), P("photo-manager", 96400) };
            view.Usage[ClaudePeriod.Today] = new List<KeyValuePair<string, long>> { P("widget-screen", 3660000), P("api-server", 824000), P("photo-manager", 192800), P("Other", 54000) };
            view.Usage[ClaudePeriod.Week] = new List<KeyValuePair<string, long>> { P("widget-screen", 16470000), P("api-server", 3708000), P("photo-manager", 867600), P("scada-bom", 2210000), P("Other", 412000) };
            foreach (var key in view.Usage.Keys.ToList()) view.Usage[key] = view.Usage[key].OrderByDescending(p => p.Key == "Other" ? -1 : p.Value).ToList();
            return view;
        }
        private static KeyValuePair<string, long> P(string name, long tokens) { return new KeyValuePair<string, long>(name, tokens); }
    }

    // Reads what ClaudeHook.exe writes (sessions and plan limits), the session logs for usage, and, when allowed, the
    // read-only usage check (docs/design/claude-watcher.md). Nothing here writes to Claude Code's folder except the
    // connector, and only when the user taps Connect or Disconnect.
    public sealed class ClaudeService : IDisposable
    {
        public static readonly TimeSpan OnlineEvery = TimeSpan.FromMinutes(5), Stale = TimeSpan.FromMinutes(15);
        public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        public readonly ClaudeConnector Connector;
        public readonly ClaudeUsage Usage;
        private readonly string bridge;
        private readonly Dictionary<string, Cached> files = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private DateTime limitsWrite = DateTime.MinValue;
        private ClaudeLimits fileLimits, onlineLimits;
        private DateTime lastOnline = DateTime.MinValue, onlineBlockedUntil = DateTime.MinValue;
        private HttpClient http;
        private sealed class Cached { public DateTime Written; public long Length; public ClaudeSessionFile Session; }

        public ClaudeService(string bridgeFolder, string configFolder)
        {
            bridge = bridgeFolder;
            Connector = new ClaudeConnector(configFolder, bridgeFolder);
            Usage = new ClaudeUsage(configFolder);
        }

        public string BridgeFolder { get { return bridge; } }
        public string ConfigFolder { get { return Connector.ConfigFolder; } }
        public bool Connected { get; private set; }
        public void CheckConnected() { Connected = Connector.IsConnected(); }
        public List<ClaudeSessionFile> Sessions { get { return files.Values.Select(c => c.Session).Where(s => s != null).ToList(); } }

        // Reads the session files that changed since last time; drops sessions whose Claude Code has gone (when alive
        // is checked) or that have been quiet for 12 hours. Returns whether anything changed.
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
            foreach (var path in files.Keys.Where(p => !present.Contains(p)).ToList()) { files.Remove(path); changed = true; }
            foreach (var pair in files.ToList())
            {
                var s = pair.Value.Session; if (s == null) continue;
                bool forgotten = now - ClaudeBridge.FromEpoch(Math.Max(s.LastEvent, s.Since)) > ClaudeBridge.Forgotten;
                if (forgotten || (checkAlive && !ClaudeProcesses.Alive(s.Pid)))
                {
                    // Closed without SessionEnd (the terminal went away): the file goes too, so it doesn't come back.
                    try { File.Delete(pair.Key); } catch (Exception) { }
                    files.Remove(pair.Key); changed = true;
                }
            }
            return changed;
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

        // The newer of the status line's numbers and the usage check's.
        public ClaudeLimits Limits
        {
            get
            {
                if (onlineLimits == null) return fileLimits;
                if (fileLimits == null || !fileLimits.Known) return onlineLimits;
                return onlineLimits.UpdatedAt > fileLimits.UpdatedAt ? onlineLimits : fileLimits;
            }
        }

        // ----- The read-only usage check (Ali approved, 1 Nov 2026) -----

        // Every 5 minutes while the app runs with the Claude card on a page, whether or not a session is open or Claude
        // Code is running (Ali, 1 Nov 2026). After any problem it stays quiet for an hour.
        public bool OnlineDue(DateTime now)
        {
            return now >= onlineBlockedUntil && now - lastOnline >= OnlineEvery;
        }

        // Reads Claude Code's sign-in token, asks api.anthropic.com for the plan's usage, and forgets the token. It is
        // sent nowhere else, never logged, stored or refreshed (refreshing would rotate Claude Code's own sign-in).
        public async Task<bool> CheckOnlineAsync(DateTime now)
        {
            lastOnline = now;
            try
            {
                string path = Path.Combine(ConfigFolder, ".credentials.json");
                if (!File.Exists(path)) { onlineBlockedUntil = now.AddHours(1); return false; }
                var oauth = ClaudeJson.Obj(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(path, Encoding.UTF8))), "claudeAiOauth");
                double expires = ClaudeJson.Num(oauth, "expiresAt");
                string token = ClaudeJson.Str(oauth, "accessToken");
                if (String.IsNullOrEmpty(token) || (!Double.IsNaN(expires) && ClaudeBridge.FromEpoch(expires) <= now.AddMinutes(1))) { onlineBlockedUntil = now.AddHours(1); return false; }
                if (http == null) http = new HttpClient(new PreferIPv4Handler()) { Timeout = TimeSpan.FromSeconds(12) };
                using (var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    request.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
                    token = null;
                    using (var response = await http.SendAsync(request).ConfigureAwait(false))
                    {
                        if (response.StatusCode != HttpStatusCode.OK) { onlineBlockedUntil = now.AddHours(1); return false; }
                        var parsed = ClaudeBridge.ParseUsageResponse(await response.Content.ReadAsStringAsync().ConfigureAwait(false), DateTime.UtcNow);
                        if (parsed == null) { onlineBlockedUntil = now.AddHours(1); return false; }
                        onlineLimits = parsed;
                        return true;
                    }
                }
            }
            catch (Exception) { onlineBlockedUntil = now.AddHours(1); return false; }
        }

        // ----- What the card shows -----

        public ClaudeView View(DateTime now, List<ClaudeUsageEntry> usage)
        {
            var view = new ClaudeView { Connected = Connected, Limits = Limits, LogsFailed = Usage.Failed, LogsRead = Usage.Scanned };
            var starts = new Dictionary<ClaudePeriod, DateTime>();
            foreach (ClaudePeriod period in Enum.GetValues(typeof(ClaudePeriod))) starts[period] = ClaudeUsage.Start(period, view.Limits, now);
            var sessions = new List<ClaudeSession>();
            foreach (var file in Sessions)
            {
                var s = new ClaudeSession { Id = file.Id, State = file.State, Since = ClaudeBridge.FromEpoch(file.Since), Project = file.Project, ProjectName = ClaudeBridge.ProjectName(file.Project),
                    Title = file.Title, Message = file.Message, Model = file.Model, Pid = file.Pid, WindowPid = file.WindowPid, Window = file.Window, CanFocus = (file.Window != 0 || file.WindowPid != 0) && ClaudeProcesses.Window(file.Window, file.WindowPid) != IntPtr.Zero };
                if (usage != null) foreach (var pair in starts) s.Tokens[pair.Key] = ClaudeUsage.Total(usage, pair.Value, s.Id);
                sessions.Add(s);
            }
            view.Sessions = ClaudeView.Order(sessions);
            if (usage != null) foreach (var pair in starts) view.Usage[pair.Key] = ClaudeUsage.ByProject(usage, pair.Value, 6);
            return view;
        }

        public void Dispose() { if (http != null) http.Dispose(); }
    }
}
