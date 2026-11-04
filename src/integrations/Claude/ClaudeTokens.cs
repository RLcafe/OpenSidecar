using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // Tokens per minute for the Claude card (decision 027), from Claude Code's own session logs
    // (projects\<folder>\<session>.jsonl and their helpers' logs, read-only). Each reply counts once (by request id, as
    // a reply is written over several lines) with its fresh input, cache writes and output; cache reads don't count
    // (Ali, 1 Nov 2026: they made the totals hundreds of times what the Claude app shows). Only logs written in the
    // last day are looked at, a log seen for the first time is read from its tail, and later scans read only what was
    // added. The same pass notes which sessions' latest entry is an API error, for the Blocked count.
    public sealed class ClaudeTokens
    {
        // The history the tile draws, a bar a minute, and the window the rate is averaged over.
        public const int Minutes = 30, RateMinutes = 5;
        public static readonly TimeSpan Looked = TimeSpan.FromHours(24);
        // The tail read from a log seen for the first time: more for one written in the last half hour, whose replies
        // count, than for an older one, whose last entry is all that matters. One scan reads at most ScanBudget.
        public const int RecentTail = 8 << 20, OldTail = 64 << 10;
        public long ScanBudget = 64L << 20;

        private readonly string projects;
        private readonly object gate = new object();
        private readonly Dictionary<string, long> offsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        // Replies counted in the last half hour, by request id, with when they came.
        private readonly Dictionary<string, Reply> replies = new Dictionary<string, Reply>();
        private readonly HashSet<string> errored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private sealed class Reply { public DateTime At; public long Tokens; }
        private volatile Snapshot published = new Snapshot();
        private sealed class Snapshot { public double[] History = new double[Minutes]; public HashSet<string> Errored = new HashSet<string>(StringComparer.OrdinalIgnoreCase); public bool Found, Scanned; }

        public ClaudeTokens(string configFolder) { projects = Path.Combine(configFolder, "projects"); }

        // Whether Claude Code's logs folder is there (false: Claude Code hasn't run on this PC, or not as this user).
        public bool Found { get { return published.Found; } }
        public bool Scanned { get { return published.Scanned; } }
        // Tokens in each of the last 30 minutes, oldest first, as of the last scan.
        public double[] History { get { return published.History.ToArray(); } }
        // Sessions whose latest entry is an API error.
        public HashSet<string> Errored { get { return new HashSet<string>(published.Errored, StringComparer.OrdinalIgnoreCase); } }
        public double PerMinute { get { return Rate(published.History); } }

        // The average of the last five whole minutes and the one in progress, per minute.
        public static double Rate(double[] history)
        {
            if (history == null || history.Length == 0) return 0;
            int take = Math.Min(RateMinutes, history.Length);
            return history.Skip(history.Length - take).Sum() / take;
        }

        // Runs off the UI thread.
        public void Scan(DateTime now)
        {
            lock (gate)
            {
                bool found = Directory.Exists(projects);
                try
                {
                    if (found)
                    {
                        long budget = ScanBudget;
                        var files = new DirectoryInfo(projects).EnumerateFiles("*.jsonl", SearchOption.AllDirectories).Where(f => now - f.LastWriteTimeUtc <= Looked).OrderByDescending(f => f.LastWriteTimeUtc).ToList();
                        var present = new HashSet<string>(files.Select(f => f.FullName), StringComparer.OrdinalIgnoreCase);
                        foreach (var path in offsets.Keys.Where(p => !present.Contains(p)).ToList()) offsets.Remove(path);
                        foreach (var file in files)
                        {
                            if (budget <= 0) break;
                            long offset;
                            if (!offsets.TryGetValue(file.FullName, out offset) || file.Length < offset)
                                offset = Math.Max(0, file.Length - (now - file.LastWriteTimeUtc <= TimeSpan.FromMinutes(Minutes) ? RecentTail : OldTail));
                            if (offset >= file.Length) { offsets[file.FullName] = file.Length; continue; }
                            budget -= Read(file.FullName, offset, file.Length, now);
                        }
                    }
                    foreach (var key in replies.Where(p => now - p.Value.At > TimeSpan.FromMinutes(Minutes + 1)).Select(p => p.Key).ToList()) replies.Remove(key);
                }
                catch (Exception) { }
                published = new Snapshot { History = Bins(replies.Values.Select(r => new KeyValuePair<DateTime, long>(r.At, r.Tokens)), now), Errored = new HashSet<string>(errored, StringComparer.OrdinalIgnoreCase), Found = found, Scanned = true };
            }
        }

        // Reads a log from offset to its last whole line; returns the bytes read.
        private long Read(string path, long offset, long length, DateTime now)
        {
            byte[] bytes;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    bytes = new byte[(int)Math.Min(length - offset, Int32.MaxValue / 2)];
                    int read = 0; while (read < bytes.Length) { int n = stream.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                    if (read < bytes.Length) Array.Resize(ref bytes, read);
                }
            }
            catch (Exception) { return 0; }
            int last = Array.LastIndexOf(bytes, (byte)'\n');
            if (last < 0) { offsets[path] = offset; return bytes.Length; }
            // A tail starts mid-line: what comes before its first line break is skipped.
            int first = offset > 0 && !offsets.ContainsKey(path) ? Array.IndexOf(bytes, (byte)'\n') + 1 : 0;
            offsets[path] = offset + last + 1;
            if (last + 1 > first)
            {
                // A helper's log (a subagent's, in its session's subagents folder) adds tokens but says nothing about
                // whether its session is blocked.
                string session = Helper(path) ? null : Path.GetFileNameWithoutExtension(path);
                Parse(Encoding.UTF8.GetString(bytes, first, last + 1 - first), session, now);
            }
            return bytes.Length;
        }
        private static bool Helper(string path)
        {
            string folder = Path.GetFileName(Path.GetDirectoryName(path) ?? "") ?? "";
            return folder.Equals("subagents", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).StartsWith("agent-", StringComparison.OrdinalIgnoreCase);
        }

        private void Parse(string text, string session, DateTime now)
        {
            foreach (var line in Lines(text))
            {
                if (session != null && line.Type != null) { if (line.Error) errored.Add(session); else errored.Remove(session); }
                if (line.Tokens <= 0 || now - line.At > TimeSpan.FromMinutes(Minutes + 1) || line.Key.Length == 0 || replies.ContainsKey(line.Key)) continue;
                replies[line.Key] = new Reply { At = line.At, Tokens = line.Tokens };
            }
        }

        // What one log line says: whether it's a turn of the conversation (a user or assistant line; Type is null for
        // the rest), whether it's an API error, and a reply's tokens, key and time.
        public sealed class Line { public string Type, Key = ""; public bool Error; public long Tokens; public DateTime At; }
        public static List<Line> Lines(string text)
        {
            var result = new List<Line>();
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || (line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0 && line.IndexOf("\"user\"", StringComparison.Ordinal) < 0)) continue;
                object parsed; if (!ClaudeJson.TryParse(line, out parsed)) continue;
                var o = ClaudeJson.Obj(parsed);
                string type = ClaudeJson.Str(o, "type");
                if (type != "assistant" && type != "user") continue;
                // Claude Code's own notes (isMeta) and helpers' lines in the main log don't change where the turn stands.
                bool turn = !ClaudeJson.Bool(o, "isMeta") && !ClaudeJson.Bool(o, "isSidechain");
                var entry = new Line { Type = turn ? type : null, Error = type == "assistant" && ClaudeJson.Bool(o, "isApiErrorMessage") };
                if (type == "assistant")
                {
                    var message = ClaudeJson.Obj(o, "message"); var usage = ClaudeJson.Obj(message, "usage");
                    var at = ClaudeTime.Parse(ClaudeJson.Str(o, "timestamp"));
                    if (usage != null && at.HasValue)
                    {
                        entry.Tokens = Count(usage, "input_tokens") + Count(usage, "output_tokens") + Count(usage, "cache_creation_input_tokens");
                        entry.At = at.Value;
                        string request = ClaudeJson.Str(o, "requestId") ?? "";
                        entry.Key = request.Length > 0 ? request : (ClaudeJson.Str(message, "id") ?? "");
                    }
                }
                result.Add(entry);
            }
            return result;
        }
        private static long Count(JsonObject usage, string key) { double v = ClaudeJson.Num(usage, key); return Double.IsNaN(v) || v < 0 ? 0 : (long)v; }

        // Tokens per minute over the last 30 minutes, oldest first; the last bar is the minute in progress.
        public static double[] Bins(IEnumerable<KeyValuePair<DateTime, long>> replies, DateTime now)
        {
            var bins = new double[Minutes];
            foreach (var r in replies)
            {
                double ago = (now - r.Key).TotalMinutes;
                if (ago < 0) ago = 0;
                int index = Minutes - 1 - (int)Math.Floor(ago);
                if (index >= 0 && index < Minutes) bins[index] += r.Value;
            }
            return bins;
        }

        // "940", "12.4k", "1.2M".
        public static string Format(double tokens)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (Double.IsNaN(tokens) || tokens < 0) return "—";
            if (tokens < 1000) return Math.Round(tokens).ToString(culture);
            if (tokens < 1e6) return (tokens < 1e4 ? (Math.Floor(tokens / 100) / 10).ToString("0.0", culture) : Math.Round(tokens / 1000).ToString(culture)) + "k";
            return (tokens < 1e7 ? (Math.Floor(tokens / 1e5) / 10).ToString("0.0", culture) : Math.Round(tokens / 1e6).ToString(culture)) + "M";
        }
    }
}
