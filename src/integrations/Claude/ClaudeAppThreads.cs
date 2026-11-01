using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // One thread in the Claude desktop app's Code tab, as its sidebar lists it: the Claude Code session behind it, the
    // folder it started in, the title the app shows, and when it was made and last active.
    public sealed class ClaudeAppThread
    {
        public string Session = "", Folder = "", Title = "";
        // Started from the Claude app's Remote Control (a phone or claude.ai driving this PC): shown as where it runs.
        public bool Remote;
        public bool Archived;
        // The app's summary of the latest reply says "completed" (postTurnSummary.status_category, and it summarizes
        // lastAssistantUuid): Ali counts that as resolved (1 Nov 2026; decision 020). Only the category is read.
        public bool Completed;
        public DateTime Created, LastActive;
    }

    // The Claude desktop app keeps a small record per Code-tab thread (claude-code-sessions\<org>\<account>\local_*.json
    // under its data folder: cliSessionId, originCwd, title, isArchived, createdAt, lastActivityAt). It's where the
    // sidebar's titles come from; Claude Code's own files only carry a title the user typed. It isn't a documented
    // interface, so it is read defensively, only the fields above, and never written. Changed files only are parsed.
    public sealed class ClaudeAppThreads
    {
        // The checks point this at a throwaway folder (a ';'-separated list), so the real app data is never read.
        public const string RootsVariable = "WIDGETSCREEN_CLAUDE_APP_DATA";
        public static readonly TimeSpan FindRootsEvery = TimeSpan.FromMinutes(1);
        private readonly Func<IEnumerable<string>> findRoots;
        private List<string> roots = new List<string>();
        private DateTime rootsFound = DateTime.MinValue;
        private readonly Dictionary<string, Cached> files = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private sealed class Cached { public DateTime Written; public long Length; public ClaudeAppThread Thread; }

        public ClaudeAppThreads(Func<IEnumerable<string>> roots = null) { findRoots = roots ?? DefaultRoots; }

        // %APPDATA%\Claude, %LOCALAPPDATA%\Claude and the Store build's %LOCALAPPDATA%\Claude-Data\<profile>, each with
        // its claude-code-sessions folder.
        public static IEnumerable<string> DefaultRoots()
        {
            string set = Environment.GetEnvironmentVariable(RootsVariable);
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

        public List<ClaudeAppThread> Threads { get { return files.Values.Select(c => c.Thread).Where(t => t != null).ToList(); } }

        // Re-reads changed records; returns whether anything changed.
        public bool Read(DateTime now)
        {
            if (now - rootsFound >= FindRootsEvery) { rootsFound = now; try { roots = findRoots().ToList(); } catch (Exception) { roots = new List<string>(); } }
            bool changed = false;
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                try
                {
                    foreach (var file in new DirectoryInfo(root).EnumerateFiles("local_*.json", SearchOption.AllDirectories))
                    {
                        present.Add(file.FullName);
                        Cached cached;
                        if (files.TryGetValue(file.FullName, out cached) && cached.Written == file.LastWriteTimeUtc && cached.Length == file.Length) continue;
                        string text = null;
                        try { using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var reader = new StreamReader(stream, Encoding.UTF8)) text = reader.ReadToEnd(); }
                        catch (Exception) { continue; }
                        files[file.FullName] = new Cached { Written = file.LastWriteTimeUtc, Length = file.Length, Thread = Parse(text) };
                        changed = true;
                    }
                }
                catch (Exception) { }
            }
            foreach (var path in files.Keys.Where(p => !present.Contains(p)).ToList()) { files.Remove(path); changed = true; }
            return changed;
        }

        // One record, or null when it names no Claude Code session.
        public static ClaudeAppThread Parse(string text)
        {
            object parsed; if (!ClaudeJson.TryParse(text ?? "", out parsed)) return null;
            var o = ClaudeJson.Obj(parsed);
            string session = ClaudeJson.Str(o, "cliSessionId");
            if (String.IsNullOrEmpty(session)) return null;
            string folder = ClaudeJson.Str(o, "originCwd");
            if (String.IsNullOrEmpty(folder)) folder = ClaudeJson.Str(o, "cwd") ?? "";
            double created = ClaudeJson.Num(o, "createdAt"), active = ClaudeJson.Num(o, "lastActivityAt");
            var thread = new ClaudeAppThread { Session = session, Folder = folder, Title = (ClaudeJson.Str(o, "title") ?? "").Trim(), Archived = ClaudeJson.Bool(o, "isArchived") };
            var summary = ClaudeJson.Obj(o, "postTurnSummary");
            string about = ClaudeJson.Str(summary, "summarizes_uuid"), last = ClaudeJson.Str(o, "lastAssistantUuid");
            thread.Completed = String.Equals(ClaudeJson.Str(summary, "status_category"), "completed", StringComparison.OrdinalIgnoreCase)
                && (String.IsNullOrEmpty(about) || String.IsNullOrEmpty(last) || about == last);
            thread.Remote = ClaudeJson.Obj(o, "remoteControlSpawn") != null || thread.Title.StartsWith("Remote Control", StringComparison.OrdinalIgnoreCase);
            thread.Created = Double.IsNaN(created) ? DateTime.MinValue : ClaudeBridge.FromEpoch(created);
            thread.LastActive = Double.IsNaN(active) ? thread.Created : ClaudeBridge.FromEpoch(active);
            return thread;
        }
    }

    // The two refresh numbers in Settings → Claude (Ali, 1 Nov 2026): the card's, at least 1 s, and the plan check's,
    // at least 10 s. A plan check that Anthropic turns away (429 or a 5xx) waits twice as long each time, up to 15
    // minutes, and goes back to the chosen interval after a success.
    public static class ClaudeIntervals
    {
        public const int CardDefault = 2, CardMinimum = 1, PlanDefault = 300, PlanMinimum = 10, Maximum = 86400;
        public const int PlanWarnBelow = 60;
        public static readonly TimeSpan BackoffLimit = TimeSpan.FromMinutes(15);
        public static int Card(int seconds) { return Math.Max(CardMinimum, Math.Min(Maximum, seconds)); }
        public static int Plan(int seconds) { return Math.Max(PlanMinimum, Math.Min(Maximum, seconds)); }
        // The next wait after a turned-away check: double the last one (or the chosen interval), at most 15 minutes,
        // but never shorter than the chosen interval.
        public static TimeSpan Backoff(TimeSpan chosen, TimeSpan last)
        {
            var doubled = TimeSpan.FromTicks(Math.Max(chosen.Ticks, last.Ticks) * 2);
            return doubled > BackoffLimit ? (chosen > BackoffLimit ? chosen : BackoffLimit) : doubled;
        }
        // A typed number: digits only, clamped; anything else keeps the fallback.
        public static int Parse(string text, int fallback, Func<int, int> clamp)
        {
            long value;
            if (!Int64.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out value)) return clamp(fallback);
            return clamp((int)Math.Min(Int32.MaxValue, value));
        }
    }
}
