using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace WidgetScreen
{
    // One dashboard per settings file (v0.41.1). Two copies on the same settings stacked on the same screen: the one
    // underneath couldn't bind the Claude telemetry port or the phone link, and both wrote the same files. A second
    // start now asks the running copy to show itself (out of the tray, or restored) and closes. Renders and copies
    // with their own --settings never meet: the name comes from the settings path.
    public sealed class SingleInstance : IDisposable
    {
        private readonly Mutex mutex;
        private readonly EventWaitHandle show;
        private RegisteredWaitHandle wait;

        private SingleInstance(Mutex mutex, EventWaitHandle show) { this.mutex = mutex; this.show = show; }

        // "WidgetScreen.<16 hex>" from the settings file's full path, case-insensitive like Windows paths.
        public static string Key(string settingsPath)
        {
            string full;
            try { full = Path.GetFullPath(settingsPath ?? ""); } catch (Exception) { full = settingsPath ?? ""; }
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(full.TrimEnd('\\', '/').ToLowerInvariant()));
                return "WidgetScreen." + String.Concat(hash.Take(8).Select(b => b.ToString("x2")));
            }
        }

        // The claim for this settings file, or null when another copy holds it (that copy has been asked to show).
        // A copy that is closing to restart itself (full-sensors mode on or off) lets go within a few seconds, so a
        // start waits that long before giving way.
        public static SingleInstance Claim(string settingsPath) { return Claim(settingsPath, TimeSpan.FromSeconds(8)); }

        public static SingleInstance Claim(string settingsPath, TimeSpan patience)
        {
            string key = Key(settingsPath);
            var until = DateTime.UtcNow + patience;
            bool asked = false;
            while (true)
            {
                Mutex mutex = null;
                try
                {
                    bool created;
                    mutex = new Mutex(true, @"Local\" + key, out created);
                    bool owned = created;
                    if (!owned)
                    {
                        if (!asked) { Signal(key); asked = true; }
                        var left = until - DateTime.UtcNow;
                        try { owned = mutex.WaitOne(left > TimeSpan.Zero ? left : TimeSpan.Zero); }
                        catch (AbandonedMutexException) { owned = true; }
                    }
                    if (owned) return new SingleInstance(mutex, OpenShow(key));
                    mutex.Dispose();
                    return null;
                }
                // An elevated copy (full-sensors mode) holds it and this one may not open it: wait for it to close.
                catch (UnauthorizedAccessException)
                {
                    if (mutex != null) mutex.Dispose();
                    if (!asked) { Signal(key); asked = true; }
                    if (DateTime.UtcNow >= until) return null;
                    Thread.Sleep(250);
                }
            }
        }

        private static EventWaitHandle OpenShow(string key)
        {
            try { return new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\" + key + ".Show"); }
            catch (Exception) { return null; }
        }

        // Runs onShow (on a pool thread) each time a later start asks this copy to show itself.
        public void OnShow(Action onShow)
        {
            if (show == null || onShow == null) return;
            wait = ThreadPool.RegisterWaitForSingleObject(show, (state, timedOut) => onShow(), null, Timeout.Infinite, false);
        }

        private static void Signal(string key)
        {
            try { using (var show = EventWaitHandle.OpenExisting(@"Local\" + key + ".Show")) show.Set(); }
            catch (Exception) { }
        }

        public void Dispose()
        {
            if (wait != null) { wait.Unregister(null); wait = null; }
            if (show != null) show.Dispose();
            try { mutex.ReleaseMutex(); } catch (ApplicationException) { }
            mutex.Dispose();
        }
    }
}
