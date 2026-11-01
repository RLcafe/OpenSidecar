using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // The periods the card counts tokens over (docs/design/claude-watcher.md).
    public enum ClaudePeriod { FiveHour, Today, Week }

    // One assistant reply's tokens in Claude Code's session logs.
    public sealed class ClaudeUsageEntry
    {
        public DateTime At;
        public string Session = "", Project = "";
        public long Tokens;
    }

    // Tokens per project and per session from Claude Code's logs: projects\<encoded folder>\<session id>.jsonl under
    // the Claude folder. Assistant lines carry message.usage; a streamed reply repeats it on several lines, so each reply
    // counts once (message.id + requestId). Tokens are the four counts added, the usual way usage tools count. Files
    // are read incrementally: only bytes added since the last scan, and only files changed within the last week.
    // A scan runs off the UI thread; the card reads a snapshot.
    public sealed class ClaudeUsage
    {
        public static readonly TimeSpan Kept = TimeSpan.FromDays(8);
        private readonly string projects;
        private readonly Dictionary<string, long> offsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> seen = new HashSet<string>();
        private List<ClaudeUsageEntry> entries = new List<ClaudeUsageEntry>();
        private readonly object gate = new object();
        public bool Failed { get; private set; }
        public bool Scanned { get; private set; }

        public ClaudeUsage(string configFolder) { projects = Path.Combine(configFolder, "projects"); }

        public List<ClaudeUsageEntry> Entries { get { lock (gate) return entries.ToList(); } }

        public void Scan(DateTime now)
        {
            lock (gate)
            {
                try
                {
                    if (!Directory.Exists(projects)) { Failed = true; Scanned = true; return; }
                    DateTime oldest = now - Kept;
                    foreach (var file in new DirectoryInfo(projects).EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
                    {
                        if (file.LastWriteTimeUtc < oldest) continue;
                        long offset; offsets.TryGetValue(file.FullName, out offset);
                        if (file.Length < offset) offset = 0;
                        if (file.Length == offset) continue;
                        offsets[file.FullName] = ReadFrom(file.FullName, offset, oldest);
                    }
                    entries.RemoveAll(e => e.At < oldest);
                    Failed = false;
                }
                catch (Exception) { Failed = true; }
                Scanned = true;
            }
        }

        // Reads whole lines from offset on; a line still being written waits for the next scan.
        private long ReadFrom(string path, long offset, DateTime oldest)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                stream.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[stream.Length - offset];
                int read = 0; while (read < buffer.Length) { int n = stream.Read(buffer, read, buffer.Length - read); if (n <= 0) break; read += n; }
                int end = Array.LastIndexOf(buffer, (byte)'\n', Math.Max(0, read - 1));
                if (end < 0) return offset;
                string text = Encoding.UTF8.GetString(buffer, 0, end + 1);
                string session = Path.GetFileNameWithoutExtension(path);
                foreach (var entry in Parse(text, seen, session)) if (entry.At >= oldest) entries.Add(entry);
                return offset + end + 1;
            }
        }

        // The usage in a piece of a log; seen holds the replies already counted.
        public static List<ClaudeUsageEntry> Parse(string text, HashSet<string> seen, string session)
        {
            var result = new List<ClaudeUsageEntry>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                // Most lines are prompts, tool results and attachments; only assistant lines with usage are read.
                if (line.Length == 0 || line.IndexOf("\"usage\"", StringComparison.Ordinal) < 0 || line.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) continue;
                object parsed; if (!ClaudeJson.TryParse(line, out parsed)) continue;
                var o = ClaudeJson.Obj(parsed);
                if (ClaudeJson.Str(o, "type") != "assistant") continue;
                var message = ClaudeJson.Obj(o, "message"); var usage = ClaudeJson.Obj(message, "usage");
                if (usage == null) continue;
                string key = (ClaudeJson.Str(message, "id") ?? "") + "|" + (ClaudeJson.Str(o, "requestId") ?? "");
                if (key != "|" && !seen.Add(key)) continue;
                long tokens = Count(usage, "input_tokens") + Count(usage, "output_tokens") + Count(usage, "cache_creation_input_tokens") + Count(usage, "cache_read_input_tokens");
                if (tokens <= 0) continue;
                var at = ClaudeBridge.ParseTime(ClaudeJson.Str(o, "timestamp"));
                if (!at.HasValue) continue;
                result.Add(new ClaudeUsageEntry { At = at.Value, Session = ClaudeJson.Str(o, "sessionId") ?? session ?? "", Project = ClaudeBridge.ProjectName(ClaudeJson.Str(o, "cwd")), Tokens = tokens });
            }
            return result;
        }
        private static long Count(JsonObject usage, string key) { double v = ClaudeJson.Num(usage, key); return Double.IsNaN(v) || v < 0 ? 0 : (long)v; }

        // Where a period starts: the plan's own windows when Claude Code has said when they reset, otherwise the last
        // five hours or seven days; Today is local midnight.
        public static DateTime Start(ClaudePeriod period, ClaudeLimits limits, DateTime now)
        {
            if (period == ClaudePeriod.Today) return DateTime.SpecifyKind(now.ToLocalTime().Date, DateTimeKind.Local).ToUniversalTime();
            var window = limits == null ? null : period == ClaudePeriod.FiveHour ? limits.FiveHour : limits.Week;
            var length = period == ClaudePeriod.FiveHour ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7);
            if (window != null && window.ResetsAt.HasValue && window.ResetsAt.Value > now && window.ResetsAt.Value - now <= length) return window.ResetsAt.Value - length;
            return now - length;
        }

        public static long Total(IEnumerable<ClaudeUsageEntry> entries, DateTime from, string session = null)
        {
            return entries.Where(e => e.At >= from && (session == null || e.Session == session)).Sum(e => e.Tokens);
        }

        // The projects with the most tokens since from, biggest first; past top, the rest are added up as "Other".
        public static List<KeyValuePair<string, long>> ByProject(IEnumerable<ClaudeUsageEntry> entries, DateTime from, int top)
        {
            var sums = entries.Where(e => e.At >= from).GroupBy(e => e.Project, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, long>(g.First().Project, g.Sum(e => e.Tokens))).Where(p => p.Value > 0)
                .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase).ToList();
            if (sums.Count <= top) return sums;
            var result = sums.Take(Math.Max(0, top - 1)).ToList();
            result.Add(new KeyValuePair<string, long>("Other", sums.Skip(Math.Max(0, top - 1)).Sum(p => p.Value)));
            return result;
        }

        // 950, 12.4k, 1.2M, 3.4B.
        public static string Format(long tokens)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (tokens < 1000) return Math.Max(0, tokens).ToString(culture);
            if (tokens < 1000000) return Short(tokens / 1e3) + "k";
            if (tokens < 1000000000) return Short(tokens / 1e6) + "M";
            return Short(tokens / 1e9) + "B";
        }
        private static string Short(double value) { return value >= 100 ? Math.Floor(value).ToString(System.Globalization.CultureInfo.InvariantCulture) : (Math.Floor(value * 10) / 10).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
