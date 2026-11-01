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
        public int Pid, WindowPid;
        public long Window;
        public bool CanFocus;
        public readonly Dictionary<ClaudePeriod, long> Tokens = new Dictionary<ClaudePeriod, long>();
        public readonly Dictionary<ClaudePeriod, double> Cost = new Dictionary<ClaudePeriod, double>();
        public string Shown { get { return String.IsNullOrWhiteSpace(Title) ? ProjectName : Title; } }
        public long TokensFor(ClaudePeriod period) { long value; return Tokens.TryGetValue(period, out value) ? value : 0; }
        public double CostFor(ClaudePeriod period) { double value; return Cost.TryGetValue(period, out value) ? value : 0; }
    }

    // How the plan meters' numbers stand: the widget's own sign-in and its last check.
    public enum ClaudePlanState { SignedOut, Reading, Current, Failed, Expired }

    // A project in Settings → Claude → Projects: its folder, when it was last used, this week's usage and its tick.
    public sealed class ClaudeProjectInfo
    {
        public string Folder = "", Name = "";
        public DateTime LastUsed;
        public long WeekTokens;
        public double WeekCost;
        public bool Shown;
    }

    // Everything the Claude card shows at one moment.
    public sealed class ClaudeView
    {
        public bool Connected, Sample, LogsFailed, LogsRead;
        public ClaudePlanState Plan;
        public List<ClaudeSession> Sessions = new List<ClaudeSession>();
        public ClaudeLimits Limits;
        public readonly Dictionary<ClaudePeriod, List<ClaudeProjectUsage>> Usage = new Dictionary<ClaudePeriod, List<ClaudeProjectUsage>>();
        public int NeedsYou { get { return Sessions.Count(s => s.State == ClaudeState.NeedsYou); } }
        // A session has run and answered without rate limits: an API key or a logged-out Claude Code.
        public bool NoPlan { get { return Limits != null && !Limits.Known && Limits.MissingAt.HasValue; } }
        public List<ClaudeProjectUsage> UsageFor(ClaudePeriod period) { List<ClaudeProjectUsage> list; return Usage.TryGetValue(period, out list) ? list : new List<ClaudeProjectUsage>(); }

        // Needs you first (longest waiting first), then Working, Done and Ready, newest first within each.
        public static List<ClaudeSession> Order(IEnumerable<ClaudeSession> sessions)
        {
            return sessions.OrderBy(s => Rank(s.State)).ThenBy(s => s.State == ClaudeState.NeedsYou ? s.Since.Ticks : -s.Since.Ticks).ThenBy(s => s.Id, StringComparer.Ordinal).ToList();
        }
        public static int Rank(ClaudeState state) { return state == ClaudeState.NeedsYou ? 0 : state == ClaudeState.Working ? 1 : state == ClaudeState.Done ? 2 : 3; }

        // The labelled fixture for renders and checks ("Sample" in the heading). Never shown as live data.
        public static ClaudeView Fixture(DateTime now, bool needsYou)
        {
            var view = new ClaudeView { Connected = true, Sample = true, LogsRead = true, Plan = ClaudePlanState.Current };
            Func<string, string, string, ClaudeState, double, string, long, ClaudeSession> make = (id, project, title, state, minutes, model, tokens) => {
                var s = new ClaudeSession { Id = id, Project = "C:\\Code\\" + project, ProjectName = project, Title = title, State = state, Since = now.AddMinutes(-minutes), Model = model, CanFocus = true, Where = "Desktop" };
                s.Tokens[ClaudePeriod.FiveHour] = tokens; s.Tokens[ClaudePeriod.Today] = tokens * 2; s.Tokens[ClaudePeriod.Week] = tokens * 9;
                s.Cost[ClaudePeriod.FiveHour] = tokens / 1e6 * 1.1; s.Cost[ClaudePeriod.Today] = tokens / 1e6 * 2.2; s.Cost[ClaudePeriod.Week] = tokens / 1e6 * 9.9;
                return s;
            };
            var list = new List<ClaudeSession> {
                make("sample-1", "api-server", "Fix the login timeout", needsYou ? ClaudeState.NeedsYou : ClaudeState.Working, 2, "Opus 5.5", 412000),
                make("sample-2", "widget-screen", "Claude card critters", ClaudeState.Working, 14, "Opus 5.5", 1830000),
                make("sample-3", "photo-manager", "", ClaudeState.Done, 6, "Sonnet 5.5", 96400),
                make("sample-4", "notes", "Tidy the README", ClaudeState.Ready, 1, "Haiku 5.5", 0) };
            if (needsYou) list[0].Message = "Claude needs your permission to use Bash";
            view.Sessions = Order(list);
            view.Limits = new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = needsYou ? 84 : 23, ResetsAt = now.AddMinutes(100) }, Week = new ClaudeLimitWindow { Percent = 41, ResetsAt = now.AddDays(3).AddHours(5) }, UpdatedAt = now.AddMinutes(-1), Source = "online" };
            view.Limits.Extra.Add(new KeyValuePair<string, ClaudeLimitWindow>("Opus week", new ClaudeLimitWindow { Percent = 18, ResetsAt = now.AddDays(3).AddHours(5) }));
            view.Usage[ClaudePeriod.FiveHour] = new List<ClaudeProjectUsage> { P("widget-screen", 1830000, 2.01), P("api-server", 412000, 0.45), P("photo-manager", 96400, 0.11) };
            view.Usage[ClaudePeriod.Today] = new List<ClaudeProjectUsage> { P("widget-screen", 3660000, 4.03), P("api-server", 824000, 0.91), P("photo-manager", 192800, 0.21), P("Other", 54000, 0.06) };
            view.Usage[ClaudePeriod.Week] = new List<ClaudeProjectUsage> { P("widget-screen", 16470000, 18.1), P("api-server", 3708000, 4.08), P("photo-manager", 867600, 0.95), P("scada-bom", 2210000, 2.43), P("Other", 412000, 0.45) };
            foreach (var key in view.Usage.Keys.ToList()) view.Usage[key] = view.Usage[key].OrderByDescending(p => p.Name == "Other" ? -1 : p.Tokens).ToList();
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
        public static readonly TimeSpan OnlineEvery = TimeSpan.FromMinutes(5), Stale = TimeSpan.FromMinutes(15);
        // A session only telemetry knows about (no hook, not in Claude Code's list) shows while it was this recent.
        public static readonly TimeSpan TelemetryOnly = TimeSpan.FromMinutes(2);
        public readonly ClaudeConnector Connector;
        public readonly ClaudeUsage Usage;
        public readonly ClaudeRegistry Registry;
        public readonly ClaudeAccount Account;
        public readonly ClaudeTelemetry Telemetry;
        private readonly string bridge;
        private readonly Dictionary<string, Cached> files = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ClaudeTelemetryEvent> activity = new Dictionary<string, ClaudeTelemetryEvent>();
        private readonly Dictionary<int, ClaudeProcesses.Owner> owners = new Dictionary<int, ClaudeProcesses.Owner>();
        private readonly object activityGate = new object();
        private DateTime limitsWrite = DateTime.MinValue;
        private ClaudeLimits fileLimits, onlineLimits;
        private DateTime lastOnline = DateTime.MinValue;
        private bool onlineFailed;
        private sealed class Cached { public DateTime Written; public long Length; public ClaudeSessionFile Session; }

        public ClaudeService(string bridgeFolder, string configFolder, string signInPath = null, int telemetryPort = ClaudeTelemetry.DefaultPort, ClaudeAccount account = null)
        {
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
            lock (activityGate)
                foreach (var e in events)
                {
                    if (e.Usage != null) Usage.Add(e.Usage, now);
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

        // ----- The hook files -----

        // Reads the session files that changed since last time; drops sessions whose Claude Code has gone (when alive
        // is checked) or that have been quiet for 12 hours. Claude Code's own list is read alongside. Returns whether
        // anything changed.
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
            changed |= Registry.Read(RegistryAlive ?? (pid => pid > 0 && ClaudeProcesses.Alive(pid)), checkAlive);
            return changed;
        }
        // The checks replace the process test, since their sample processes aren't Claude Code.
        public Func<int, bool> RegistryAlive;

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
                return onlineLimits == null ? ClaudePlanState.Reading : ClaudePlanState.Current;
            }
        }

        // Every 5 minutes while signed in (Ali, 1 Nov 2026), whether or not Claude Code is running.
        public bool OnlineDue(DateTime now) { return Account != null && Account.SignedIn && now - lastOnline >= OnlineEvery; }
        public void CheckSoon() { lastOnline = DateTime.MinValue; }

        public async Task<bool> CheckOnlineAsync(DateTime now)
        {
            lastOnline = now;
            if (Account == null) return false;
            try
            {
                var limits = await Account.UsageAsync(DateTime.UtcNow).ConfigureAwait(false);
                if (limits == null) { onlineFailed = false; return true; }
                onlineLimits = limits; onlineFailed = false;
                return true;
            }
            catch (Exception) { onlineFailed = true; return true; }
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
            return result;
        }
        // A reply's project folder; telemetry's replies come without one, so the session's or the repository's.
        private static Func<ClaudeUsageEntry, string> FolderOf(Dictionary<string, string> folders, Dictionary<string, ClaudeTelemetryEvent> activity)
        {
            return e => {
                if (e.Folder.Length > 0) return e.Folder;
                string folder; if (folders.TryGetValue(e.Session, out folder)) return folder;
                ClaudeTelemetryEvent a; if (activity.TryGetValue(e.Session, out a) && a.Repository.Length > 0) return a.Repository;
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
            foreach (var file in Sessions)
            {
                sessions[file.Id] = new ClaudeSession { Id = file.Id, State = file.State, Since = ClaudeBridge.FromEpoch(file.Since), Project = file.Project,
                    Title = file.Title, Message = file.Message, Model = file.Model, Pid = file.Pid, WindowPid = file.WindowPid, Window = file.Window };
                stateAt[file.Id] = ClaudeBridge.FromEpoch(file.Since);
            }
            foreach (var entry in Registry.Entries)
            {
                ClaudeSession s;
                var listed = ClaudeRegistry.State(entry);
                bool hooked = sessions.TryGetValue(entry.Session, out s);
                if (!hooked) { s = new ClaudeSession { Id = entry.Session, State = ClaudeState.Ready, Since = entry.Started, Project = entry.Folder }; sessions[entry.Session] = s; stateAt[entry.Session] = DateTime.MinValue; }
                if (s.Project.Length == 0) s.Project = entry.Folder;
                if (s.Pid == 0) s.Pid = entry.Pid;
                if (s.Title.Length == 0 && entry.NamedByUser) s.Title = entry.Name;
                s.Where = ClaudeRegistry.Where(entry.Entrypoint);
                if (s.Window == 0 && s.WindowPid == 0 && entry.Pid > 0)
                {
                    ClaudeProcesses.Owner owner;
                    if (!owners.TryGetValue(entry.Pid, out owner)) { owner = ClaudeProcesses.OwnerOf(entry.Pid); owners[entry.Pid] = owner; }
                    s.Window = owner.Window; s.WindowPid = owner.WindowPid;
                }
                // Claude Code's own list wins when it changed after the hook's news.
                if (listed.HasValue && entry.StatusChanged > stateAt[entry.Session].AddSeconds(1))
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
                    s.State = ClaudeState.Working; s.Since = e.At; s.Message = ""; stateAt[e.Session] = e.At;
                }
            }

            var list = new List<ClaudeSession>();
            foreach (var s in sessions.Values)
            {
                string started; if (folders.TryGetValue(s.Id, out started)) s.Project = started;
                s.Project = ClaudeUsage.Key(s.Project);
                if (shown != null && !shown(s.Project)) continue;
                s.ProjectName = ClaudeBridge.ProjectName(s.Project);
                s.CanFocus = (s.Window != 0 || s.WindowPid != 0) && ClaudeProcesses.Window(s.Window, s.WindowPid) != IntPtr.Zero;
                if (usage != null)
                {
                    if (s.Model.Length == 0) { var last = usage.Where(u => u.Session == s.Id && u.Model.Length > 0).OrderByDescending(u => u.At).FirstOrDefault(); if (last != null) s.Model = ModelName(last.Model); }
                    foreach (var pair in starts) { s.Tokens[pair.Key] = ClaudeUsage.Total(usage, pair.Value, s.Id); s.Cost[pair.Key] = ClaudeUsage.Cost(usage, pair.Value, s.Id); }
                }
                list.Add(s);
            }
            view.Sessions = ClaudeView.Order(list);
            if (usage != null) foreach (var pair in starts) view.Usage[pair.Key] = ClaudeUsage.ByProject(usage, pair.Value, 6, folderOf, shown);
            return view;
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
            Action<string, DateTime> seen = (folder, at) => {
                string key = ClaudeUsage.Key(folder);
                if (key.Length == 0) return;
                ClaudeProjectInfo info;
                if (!found.TryGetValue(key, out info)) { info = new ClaudeProjectInfo { Folder = key, Name = ClaudeBridge.ProjectName(key) }; found[key] = info; }
                if (at > info.LastUsed) info.LastUsed = at;
            };
            DateTime week = now.AddDays(-7);
            foreach (var e in Usage.Entries)
            {
                string folder = folderOf(e); seen(folder, e.At);
                string key = ClaudeUsage.Key(folder);
                if (e.At >= week && found.ContainsKey(key)) { found[key].WeekTokens += e.Tokens; found[key].WeekCost += e.Cost; }
            }
            foreach (var pair in Usage.LastSeen) { string folder; if (folders.TryGetValue(pair.Key, out folder)) seen(folder, pair.Value); }
            Func<string, string, string> startedIn = (id, folder) => { string start; return folders.TryGetValue(id, out start) ? start : folder; };
            foreach (var e in Registry.Entries) seen(startedIn(e.Session, e.Folder), e.StatusChanged);
            foreach (var s in Sessions) seen(startedIn(s.Id, s.Project), ClaudeBridge.FromEpoch(Math.Max(s.LastEvent, s.Since)));
            foreach (var a in act.Values) { string folder; seen(folders.TryGetValue(a.Session, out folder) ? folder : a.Repository, a.At); }
            foreach (var info in found.Values) info.Shown = shown == null || shown(info.Folder);
            return found.Values.OrderByDescending(p => p.LastUsed).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public void Dispose() { Telemetry.Dispose(); if (Account != null) Account.Dispose(); }
    }
}
