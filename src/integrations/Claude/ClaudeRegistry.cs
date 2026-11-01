using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // One entry in Claude Code's own list of running sessions (sessions\<pid>.json under the Claude folder).
    public sealed class ClaudeRegistryEntry
    {
        public string Session = "", Folder = "", Name = "", Status = "", WaitingFor = "", Entrypoint = "", Kind = "";
        public bool NamedByUser;
        public int Pid;
        public DateTime Started, StatusChanged;
    }

    // Claude Code keeps a small file per running session in sessions\<pid>.json under its folder (pid, sessionId, cwd,
    // name, status busy/idle/waiting with waitingFor, statusUpdatedAt, entrypoint), for every way it runs: the CLI, an
    // editor, and the Claude desktop app's Code tab. It isn't a documented interface, so it is read defensively and
    // never written: it finds sessions that started before Connect or never fire a hook, and tells Working from idle.
    // A file whose process has gone (or is another program now) is ignored; Claude Code cleans those up itself.
    public sealed class ClaudeRegistry
    {
        private readonly string folder;
        private readonly Dictionary<string, Cached> files = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        private sealed class Cached { public DateTime Written; public long Length; public ClaudeRegistryEntry Entry; }

        public ClaudeRegistry(string configFolder) { folder = Path.Combine(configFolder, "sessions"); }

        // Re-reads changed files; alive decides whether an entry's process still runs (ClaudeProcesses.Alive normally),
        // asked again for every entry when recheck is set or a file changed. Returns whether anything changed.
        public bool Read(Func<int, bool> alive, bool recheck = true)
        {
            bool changed = false;
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (Directory.Exists(folder))
                    foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.json"))
                    {
                        present.Add(file.FullName);
                        Cached cached;
                        if (files.TryGetValue(file.FullName, out cached) && cached.Written == file.LastWriteTimeUtc && cached.Length == file.Length) continue;
                        string text = null;
                        try { using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) using (var reader = new StreamReader(stream, Encoding.UTF8)) text = reader.ReadToEnd(); }
                        catch (Exception) { continue; }
                        files[file.FullName] = new Cached { Written = file.LastWriteTimeUtc, Length = file.Length, Entry = Parse(text) };
                        changed = true;
                    }
            }
            catch (Exception) { }
            foreach (var path in files.Keys.Where(p => !present.Contains(p)).ToList()) { files.Remove(path); changed = true; }
            if (!changed && !recheck && live != null) return false;
            var before = live;
            live = files.Values.Select(c => c.Entry).Where(e => e != null && (alive == null || alive(e.Pid))).ToList();
            if (before == null || before.Count != live.Count || before.Zip(live, (a, b) => a != b).Any(x => x)) changed = true;
            return changed;
        }
        private List<ClaudeRegistryEntry> live;

        // The running sessions, as last read.
        public List<ClaudeRegistryEntry> Entries { get { return live == null ? new List<ClaudeRegistryEntry>() : live.ToList(); } }

        // One file's entry, or null when it names no session or is a spare process waiting to be used.
        public static ClaudeRegistryEntry Parse(string text)
        {
            object parsed; if (!ClaudeJson.TryParse(text ?? "", out parsed)) return null;
            var o = ClaudeJson.Obj(parsed);
            string session = ClaudeJson.Str(o, "sessionId");
            if (String.IsNullOrEmpty(session) || ClaudeJson.Bool(o, "spare")) return null;
            double pid = ClaudeJson.Num(o, "pid");
            string source = ClaudeJson.Str(o, "nameSource") ?? "";
            var entry = new ClaudeRegistryEntry {
                Session = session, Folder = ClaudeJson.Str(o, "cwd") ?? "", Name = ClaudeJson.Str(o, "name") ?? "", Status = ClaudeJson.Str(o, "status") ?? "",
                WaitingFor = ClaudeJson.Str(o, "waitingFor") ?? "", Entrypoint = ClaudeJson.Str(o, "entrypoint") ?? "", Kind = ClaudeJson.Str(o, "kind") ?? "",
                // Claude Code itself only shows a name the user gave (or a peer set); a derived one is "folder-97".
                NamedByUser = source == "user" || source == "peer",
                Pid = Double.IsNaN(pid) ? 0 : (int)pid };
            if (entry.Folder == "?") entry.Folder = "";
            double started = ClaudeJson.Num(o, "startedAt"), status = ClaudeJson.Num(o, "statusUpdatedAt"), updated = ClaudeJson.Num(o, "updatedAt");
            entry.Started = Double.IsNaN(started) ? DateTime.MinValue : ClaudeBridge.FromEpoch(started);
            entry.StatusChanged = !Double.IsNaN(status) ? ClaudeBridge.FromEpoch(status) : !Double.IsNaN(updated) ? ClaudeBridge.FromEpoch(updated) : entry.Started;
            return entry;
        }

        // "busy" works, "waiting" (a question or a permission dialog) needs you, "idle" is finished or not started.
        public static ClaudeState? State(ClaudeRegistryEntry entry)
        {
            if (entry == null) return null;
            if (entry.Status == "busy") return ClaudeState.Working;
            if (entry.Status == "waiting") return ClaudeState.NeedsYou;
            if (entry.Status == "idle") return ClaudeState.Done;
            return null;
        }

        // Where the session runs, for the Large rows: "Desktop", "VS Code", "Terminal".
        public static string Where(string entrypoint)
        {
            switch (entrypoint ?? "")
            {
                case "claude-desktop": return "Desktop";
                case "claude-vscode": return "VS Code";
                case "cli": return "Terminal";
                default: return "";
            }
        }

        // What a waiting session waits for, in the card's words.
        public static string Waiting(string waitingFor)
        {
            switch (waitingFor ?? "")
            {
                case "input needed": return "Has a question for you";
                case "sandbox request": return "Wants to leave the sandbox";
                case "worker request": return "A helper needs your answer";
                default: return "Waiting for you";
            }
        }
    }
}
