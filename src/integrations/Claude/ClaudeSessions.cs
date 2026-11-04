using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // How the Claude card counts a thread (decision 027): finished its turn (Mint), waiting on you (Amber: a question,
    // a permission dialog, or work the Claude app marks ready for review), blocked (Coral: the Claude app says so, or
    // the last reply was an API error), and the extra count, working right now (Glacier).
    public enum ClaudeStatus { Done, NeedsYou, Blocked, Working }

    public sealed class ClaudeCounts
    {
        public int Done, NeedsYou, Blocked, Working;
        public int Total { get { return Done + NeedsYou + Blocked + Working; } }
        public int Of(ClaudeStatus status) { return status == ClaudeStatus.Done ? Done : status == ClaudeStatus.NeedsYou ? NeedsYou : status == ClaudeStatus.Blocked ? Blocked : Working; }
        public void Add(ClaudeStatus status) { if (status == ClaudeStatus.Done) Done++; else if (status == ClaudeStatus.NeedsYou) NeedsYou++; else if (status == ClaudeStatus.Blocked) Blocked++; else Working++; }
        public bool Same(ClaudeCounts other) { return other != null && other.Done == Done && other.NeedsYou == NeedsYou && other.Blocked == Blocked && other.Working == Working; }
    }

    // One running Claude Code session, from Claude Code's own list (sessions\<pid>.json in its folder: pid, sessionId,
    // status busy/idle/waiting, startedAt). Claude Code writes one for every way it runs: a terminal, an editor and the
    // Claude desktop app's Code tab.
    public sealed class ClaudeLiveSession
    {
        public string Session = "", Status = "";
        public int Pid;
        public DateTime Started;
    }

    // One thread in the Claude desktop app (claude-code-sessions\…\local_*.json under its data folder): the session
    // behind it, whether it's archived, when it was last active, and the app's own summary of its latest reply
    // (postTurnSummary.status_category: "completed", "review_ready" or "blocked").
    public sealed class ClaudeAppThread
    {
        public string Session = "", Category = "";
        public bool Archived;
        public DateTime LastActive;
    }

    // The counts under the mascot. Both sources are read-only, undocumented files, so they are read defensively and
    // only files that changed are parsed again. Nothing is written anywhere.
    public sealed class ClaudeSessions
    {
        // The checks point this at a throwaway folder (a ';'-separated list), so the real app data is never read.
        public const string AppRootsVariable = "WIDGETSCREEN_CLAUDE_APP_DATA";
        // A Claude app thread that isn't running counts for a day after its last activity, unless it's archived.
        public static readonly TimeSpan Recent = TimeSpan.FromHours(24);
        // A process that started more than a minute after its session did has reused the session's process id.
        public static readonly TimeSpan ReuseSlack = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan FindRootsEvery = TimeSpan.FromMinutes(1);

        private readonly string sessionsFolder;
        private readonly Func<IEnumerable<string>> findRoots;
        private List<string> roots = new List<string>();
        private DateTime rootsFound = DateTime.MinValue;
        private readonly Dictionary<string, Cached<ClaudeLiveSession>> live = new Dictionary<string, Cached<ClaudeLiveSession>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Cached<ClaudeAppThread>> threads = new Dictionary<string, Cached<ClaudeAppThread>>(StringComparer.OrdinalIgnoreCase);
        private sealed class Cached<T> { public DateTime Written; public long Length; public T Value; }

        public ClaudeSessions(string configFolder, Func<IEnumerable<string>> appRoots = null)
        {
            sessionsFolder = Path.Combine(configFolder, "sessions");
            findRoots = appRoots ?? DefaultAppRoots;
        }

        public ClaudeCounts Counts { get; private set; }

        // Re-reads changed files and counts again; alive decides whether a session's process still runs. Returns
        // whether the counts changed.
        public bool Read(DateTime now, ICollection<string> errored, Func<ClaudeLiveSession, bool> alive = null)
        {
            if (now - rootsFound >= FindRootsEvery) { rootsFound = now; try { roots = findRoots().ToList(); } catch (Exception) { roots = new List<string>(); } }
            Refresh(live, new[] { sessionsFolder }, "*.json", SearchOption.TopDirectoryOnly, ParseLive);
            Refresh(threads, roots, "local_*.json", SearchOption.AllDirectories, ParseThread);
            var running = live.Values.Select(c => c.Value).Where(s => s != null && (alive ?? Alive)(s)).ToList();
            var next = Count(running, threads.Values.Select(c => c.Value).Where(t => t != null), errored, now);
            bool changed = !next.Same(Counts);
            Counts = next;
            return changed;
        }

        // Each running session once, then each recent Claude app thread that isn't running. A running session that is
        // busy works and one that waits needs you; an idle one has finished its turn, unless the app's summary of that
        // turn says it's waiting for review or blocked, or the last reply was an API error.
        public static ClaudeCounts Count(IEnumerable<ClaudeLiveSession> running, IEnumerable<ClaudeAppThread> appThreads, ICollection<string> errored, DateTime now)
        {
            var counts = new ClaudeCounts();
            var byId = new Dictionary<string, ClaudeAppThread>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in appThreads) if (t.Session.Length > 0 && (!byId.ContainsKey(t.Session) || byId[t.Session].LastActive < t.LastActive)) byId[t.Session] = t;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in running)
            {
                if (s.Session.Length == 0 || !seen.Add(s.Session)) continue;
                ClaudeAppThread thread; byId.TryGetValue(s.Session, out thread);
                if (s.Status == "busy") counts.Add(ClaudeStatus.Working);
                else if (s.Status == "waiting") counts.Add(ClaudeStatus.NeedsYou);
                else if (errored != null && errored.Contains(s.Session)) counts.Add(ClaudeStatus.Blocked);
                else counts.Add(Finished(thread));
            }
            foreach (var t in byId.Values)
            {
                if (seen.Contains(t.Session) || t.Archived || now - t.LastActive > Recent) continue;
                counts.Add(errored != null && errored.Contains(t.Session) ? ClaudeStatus.Blocked : Finished(t));
            }
            return counts;
        }
        private static ClaudeStatus Finished(ClaudeAppThread thread)
        {
            string category = thread == null ? "" : thread.Category;
            return category == "blocked" ? ClaudeStatus.Blocked : category == "review_ready" ? ClaudeStatus.NeedsYou : ClaudeStatus.Done;
        }

        // ----- Files -----

        private static void Refresh<T>(Dictionary<string, Cached<T>> cache, IEnumerable<string> folders, string pattern, SearchOption depth, Func<string, T> parse)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    foreach (var file in new DirectoryInfo(folder).EnumerateFiles(pattern, depth))
                    {
                        present.Add(file.FullName);
                        Cached<T> cached;
                        if (cache.TryGetValue(file.FullName, out cached) && cached.Written == file.LastWriteTimeUtc && cached.Length == file.Length) continue;
                        string text;
                        try { using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var reader = new StreamReader(stream, Encoding.UTF8)) text = reader.ReadToEnd(); }
                        catch (Exception) { continue; }
                        cache[file.FullName] = new Cached<T> { Written = file.LastWriteTimeUtc, Length = file.Length, Value = parse(text) };
                    }
                }
                catch (Exception) { }
            }
            foreach (var path in cache.Keys.Where(p => !present.Contains(p)).ToList()) cache.Remove(path);
        }

        // One file of Claude Code's session list, or null when it names no session or is a spare process.
        public static ClaudeLiveSession ParseLive(string text)
        {
            object parsed; if (!ClaudeJson.TryParse(text ?? "", out parsed)) return null;
            var o = ClaudeJson.Obj(parsed);
            string session = ClaudeJson.Str(o, "sessionId");
            if (String.IsNullOrEmpty(session) || ClaudeJson.Bool(o, "spare")) return null;
            double pid = ClaudeJson.Num(o, "pid"), started = ClaudeJson.Num(o, "startedAt");
            return new ClaudeLiveSession { Session = session, Status = ClaudeJson.Str(o, "status") ?? "", Pid = Double.IsNaN(pid) ? 0 : (int)pid, Started = Double.IsNaN(started) ? DateTime.MinValue : ClaudeTime.FromEpoch(started) };
        }

        // One Claude app thread record, or null when it names no Claude Code session. The summary's category counts only
        // when it describes the thread's latest reply.
        public static ClaudeAppThread ParseThread(string text)
        {
            object parsed; if (!ClaudeJson.TryParse(text ?? "", out parsed)) return null;
            var o = ClaudeJson.Obj(parsed);
            string session = ClaudeJson.Str(o, "cliSessionId");
            if (String.IsNullOrEmpty(session)) return null;
            double created = ClaudeJson.Num(o, "createdAt"), active = ClaudeJson.Num(o, "lastActivityAt");
            var summary = ClaudeJson.Obj(o, "postTurnSummary");
            string about = ClaudeJson.Str(summary, "summarizes_uuid"), last = ClaudeJson.Str(o, "lastAssistantUuid");
            bool current = String.IsNullOrEmpty(about) || String.IsNullOrEmpty(last) || about == last;
            return new ClaudeAppThread {
                Session = session, Archived = ClaudeJson.Bool(o, "isArchived"),
                Category = current ? (ClaudeJson.Str(summary, "status_category") ?? "").Trim().ToLowerInvariant().Replace(' ', '_') : "",
                LastActive = !Double.IsNaN(active) ? ClaudeTime.FromEpoch(active) : !Double.IsNaN(created) ? ClaudeTime.FromEpoch(created) : DateTime.MinValue };
        }

        // Whether the session's process still runs: any process with its id that didn't start well after the session.
        // A process this app may not ask about counts as running.
        public static bool Alive(ClaudeLiveSession session)
        {
            if (session.Pid <= 0) return true;
            try
            {
                using (var process = Process.GetProcessById(session.Pid))
                {
                    if (process.HasExited) return false;
                    if (session.Started > DateTime.MinValue)
                    {
                        try { if (process.StartTime.ToUniversalTime() > session.Started + ReuseSlack) return false; }
                        catch (Exception) { }
                    }
                    return true;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (Exception) { return true; }
        }

        // %APPDATA%\Claude, %LOCALAPPDATA%\Claude and the Store build's %LOCALAPPDATA%\Claude-Data\<profile>, each with
        // its claude-code-sessions folder.
        public static IEnumerable<string> DefaultAppRoots()
        {
            string set = Environment.GetEnvironmentVariable(AppRootsVariable);
            if (set != null) return set.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Trim()).Where(Directory.Exists).ToList();
            var found = new List<string>();
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var app in new[] { Path.Combine(roaming, "Claude"), Path.Combine(local, "Claude") }) found.Add(Path.Combine(app, "claude-code-sessions"));
            try
            {
                string data = Path.Combine(local, "Claude-Data");
                if (Directory.Exists(data)) foreach (var profile in Directory.EnumerateDirectories(data)) found.Add(Path.Combine(profile, "claude-code-sessions"));
            }
            catch (Exception) { }
            return found.Where(Directory.Exists).ToList();
        }
    }
}
