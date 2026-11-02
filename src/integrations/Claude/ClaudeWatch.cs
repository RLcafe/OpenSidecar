using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace WidgetScreen
{
    // Package 3 (decision 025): the Claude card reacts the moment a source changes instead of looking every 2 seconds.
    // Windows says when a file in a watched folder is written, made, renamed or removed; the first change after the
    // dashboard last looked calls Changed once, and later ones only add to what is pending until Take() collects it,
    // so a burst of writes asks for one rebuild. Nothing is read here. The 2-second look stays as a fallback every
    // 30 seconds, and the old interval comes back whenever a watcher fails (Broken).
    public sealed class ClaudeWatch : IDisposable
    {
        [Flags] public enum Kinds { None = 0, Sessions = 1, Logs = 2, Telemetry = 4 }
        public static readonly TimeSpan Fallback = TimeSpan.FromSeconds(30);

        private readonly object gate = new object();
        private readonly List<FileSystemWatcher> watchers = new List<FileSystemWatcher>();
        private readonly Action changed;
        private int pending;
        private string watching = "";
        private volatile bool broken;

        // changed runs on a worker thread; the dashboard hands it to its own thread.
        public ClaudeWatch(Action changed) { this.changed = changed; }

        // Watching every folder asked for that exists, and none has failed since.
        public bool Running { get { lock (gate) return watchers.Count > 0 && !broken; } }
        public bool Broken { get { return broken; } }
        public int Folders { get { lock (gate) return watchers.Count; } }

        // Watches these folders (folder, what a change there means, whether its subfolders count). Folders that don't
        // exist are left out; calling again with a different list (one appeared, Connect moved one) starts over.
        public void Watch(IEnumerable<Tuple<string, Kinds, bool>> folders)
        {
            var wanted = (folders ?? Enumerable.Empty<Tuple<string, Kinds, bool>>()).Where(f => !String.IsNullOrEmpty(f.Item1) && Directory.Exists(f.Item1)).ToList();
            string key = String.Join("|", wanted.Select(f => f.Item1.ToLowerInvariant() + ";" + (int)f.Item2 + ";" + f.Item3));
            lock (gate)
            {
                if (key == watching && !broken) return;
                StopLocked();
                watching = key;
                foreach (var f in wanted)
                {
                    try
                    {
                        var w = new FileSystemWatcher(f.Item1) { IncludeSubdirectories = f.Item3, InternalBufferSize = 32768,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
                        var kind = f.Item2;
                        FileSystemEventHandler on = (s, e) => Poke(kind);
                        w.Changed += on; w.Created += on; w.Deleted += on;
                        w.Renamed += (s, e) => Poke(kind);
                        // A full buffer or a folder taken away: some changes were lost, so the fallback interval comes back.
                        w.Error += (s, e) => { broken = true; Poke(Kinds.Sessions | Kinds.Logs); };
                        w.EnableRaisingEvents = true;
                        watchers.Add(w);
                    }
                    catch (Exception) { broken = true; }
                }
            }
        }

        // Something changed (a watcher, or telemetry arriving): calls Changed once until the dashboard takes it.
        public void Poke(Kinds kind)
        {
            int before, after;
            do { before = pending; after = before | (int)kind; }
            while (Interlocked.CompareExchange(ref pending, after, before) != before);
            if (before == 0 && after != 0 && changed != null) { try { changed(); } catch (Exception) { } }
        }

        // What changed since the last look, cleared.
        public Kinds Take() { return (Kinds)Interlocked.Exchange(ref pending, 0); }

        public void Stop() { lock (gate) { StopLocked(); watching = ""; } }
        private void StopLocked()
        {
            foreach (var w in watchers) { try { w.EnableRaisingEvents = false; w.Dispose(); } catch (Exception) { } }
            watchers.Clear();
            broken = false;
        }

        public void Dispose() { Stop(); }
    }
}
