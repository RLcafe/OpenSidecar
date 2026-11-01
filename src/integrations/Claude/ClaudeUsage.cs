using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // The periods the card counts tokens over (docs/design/claude-watcher.md).
    public enum ClaudePeriod { FiveHour, Today, Week }

    // One assistant reply's tokens and its cost at API prices, from Claude Code's session logs or its telemetry.
    public sealed class ClaudeUsageEntry
    {
        public DateTime At;
        public string Session = "", Folder = "", Model = "", Request = "";
        // The tokens the card shows: fresh input, cache writes and output (Ali, 1 Nov 2026: counting cache reads made
        // the totals hundreds of times what the Claude app shows). Cache reads are kept in CacheRead.
        public long Tokens;
        // The input side of the reply (fresh input, cache writes and cache reads) and how much of it came from the
        // cache, for the card's cache hit rate.
        public long Prompt, CacheRead;
        public double Cost;
        // Telemetry's cost is Claude Code's own; a log line's is worked out from ClaudePrices.
        public bool FromTelemetry;
    }

    // One project's usage over a period: its folder (the key Settings → Claude's project list uses), its name as shown,
    // its tokens and its cost at API prices.
    public sealed class ClaudeProjectUsage
    {
        public string Folder = "", Name = "";
        public long Tokens;
        public double Cost;
    }

    // API prices per million tokens, from the model catalog Claude Code 2.1.309 carries (read 1 Nov 2026). They only
    // label usage "≈ $ at API prices": a Pro or Max plan doesn't pay them. Telemetry's cost_usd wins when it comes.
    public static class ClaudePrices
    {
        private sealed class Tier { public double Input, Output, Write5m, Write1h, Read; public Tier Long; public long LongAbove; }
        private static Tier T(double input, double output, double write5m, double write1h, double read) { return new Tier { Input = input, Output = output, Write5m = write5m, Write1h = write1h, Read = read }; }
        private static readonly Tier Haiku55 = new Tier { Input = 0.1, Output = 0.5, Write5m = 0.125, Write1h = 0.2, Read = 0.01, LongAbove = 100000, Long = T(0.5, 2.5, 0.625, 1, 0.05) };
        // Longest names first, so "claude-opus-5-5" isn't priced as "claude-opus-5".
        private static readonly KeyValuePair<string, Tier>[] Models = new Dictionary<string, Tier> {
            { "claude-3-5-haiku", T(0.8, 4, 1, 1.6, 0.08) }, { "claude-haiku-4-5", T(1, 5, 1.25, 2, 0.1) }, { "claude-haiku-5-5", Haiku55 },
            { "claude-3-5-sonnet", T(3, 15, 3.75, 6, 0.3) }, { "claude-3-7-sonnet", T(3, 15, 3.75, 6, 0.3) }, { "claude-sonnet-4-0", T(3, 15, 3.75, 6, 0.3) },
            { "claude-sonnet-4-5", T(3, 15, 3.75, 6, 0.3) }, { "claude-sonnet-4-6", T(3, 15, 3.75, 6, 0.3) }, { "claude-sonnet-4", T(3, 15, 3.75, 6, 0.3) },
            { "claude-sonnet-5", T(2, 10, 2.5, 4, 0.2) }, { "claude-sonnet-5-5", T(2, 10, 2.5, 4, 0.1) },
            { "claude-opus-4-0", T(15, 75, 18.75, 30, 1.5) }, { "claude-opus-4-1", T(15, 75, 18.75, 30, 1.5) }, { "claude-opus-4", T(15, 75, 18.75, 30, 1.5) },
            { "claude-opus-4-5", T(5, 25, 6.25, 10, 0.5) }, { "claude-opus-4-6", T(5, 25, 6.25, 10, 0.5) }, { "claude-opus-4-7", T(5, 25, 6.25, 10, 0.5) },
            { "claude-opus-4-8", T(5, 25, 6.25, 10, 0.5) }, { "claude-opus-5", T(5, 25, 6.25, 10, 0.5) }, { "claude-opus-5-5", T(4, 20, 5, 8, 0.2) },
            { "claude-fable-5", T(10, 50, 12.5, 20, 1) }, { "claude-fable-5-1", T(10, 50, 12.5, 20, 0.25) },
            { "claude-mythos-5", T(10, 50, 12.5, 20, 1) }, { "claude-mythos-5-1", T(10, 50, 12.5, 20, 0.25) } }
            .OrderByDescending(p => p.Key.Length).ToArray();

        // The reply's cost in dollars, or 0 for a model the table doesn't know (a gateway's own name, say).
        public static double Cost(string model, long input, long output, long write5m, long write1h, long read)
        {
            var tier = Find(model);
            if (tier == null) return 0;
            if (tier.Long != null && input + read + write5m + write1h > tier.LongAbove) tier = tier.Long;
            return (input * tier.Input + output * tier.Output + write5m * tier.Write5m + write1h * tier.Write1h + read * tier.Read) / 1e6;
        }
        public static bool Known(string model) { return Find(model) != null; }
        private static Tier Find(string model)
        {
            if (String.IsNullOrWhiteSpace(model)) return null;
            string name = model.Trim().ToLowerInvariant();
            int at = name.IndexOf("claude-", StringComparison.Ordinal);
            if (at < 0) return null;
            name = name.Substring(at);
            foreach (var pair in Models)
                if (name.StartsWith(pair.Key, StringComparison.Ordinal) && Suffix(name.Substring(pair.Key.Length))) return pair.Value;
            return null;
        }
        // What may follow a catalog name: nothing, a date, "-v1", "-latest", "-fast", "@…", ":…" or "[1m]"; not
        // another version number ("claude-opus-5" isn't "claude-opus-5-1").
        private static bool Suffix(string rest)
        {
            if (rest.Length == 0 || rest[0] == '@' || rest[0] == '[' || rest[0] == ':') return true;
            if (rest[0] != '-' || rest.Length < 2) return false;
            string next = rest.Substring(1);
            if (next.StartsWith("v", StringComparison.Ordinal) || next.StartsWith("latest", StringComparison.Ordinal) || next.StartsWith("fast", StringComparison.Ordinal)) return true;
            int digits = 0; while (digits < next.Length && Char.IsDigit(next[digits])) digits++;
            return digits >= 8;
        }
    }

    // Tokens and cost per project and per session, from Claude Code's logs (projects\<encoded folder>\<session>.jsonl
    // under the Claude folder) and its telemetry (ClaudeTelemetry). Assistant lines carry message.usage; a streamed
    // reply repeats it on several lines, so each reply counts once, by its request id (message.id when there is none),
    // and a reply telemetry already counted isn't counted again from the log (and the other way round). Tokens are the
    // four counts added, the usual way usage tools count. Logs are read incrementally: only bytes added since the last
    // scan, and only files changed within the last week. A scan runs off the UI thread; the card reads a snapshot.
    public sealed class ClaudeUsage
    {
        public static readonly TimeSpan Kept = TimeSpan.FromDays(8);
        private readonly string projects;
        private readonly Dictionary<string, long> offsets = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ClaudeUsageEntry> replies = new Dictionary<string, ClaudeUsageEntry>();
        private readonly Dictionary<string, string> folders = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> lastSeen = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, string> titles = new Dictionary<string, string>();
        private readonly Dictionary<string, string> prompts = new Dictionary<string, string>();
        private List<ClaudeUsageEntry> entries = new List<ClaudeUsageEntry>();
        private readonly object gate = new object();
        public bool Failed { get; private set; }
        public bool Scanned { get; private set; }

        public ClaudeUsage(string configFolder) { projects = Path.Combine(configFolder, "projects"); }

        public List<ClaudeUsageEntry> Entries { get { lock (gate) return entries.ToList(); } }
        // The folder each session started in, as the logs say.
        public Dictionary<string, string> Folders { get { lock (gate) return new Dictionary<string, string>(folders); } }
        // When each log last changed, by session: the project list's "last used" for projects no session runs in now.
        public Dictionary<string, DateTime> LastSeen { get { lock (gate) return new Dictionary<string, DateTime>(lastSeen); } }
        // A title the user gave a session in Claude Code (its custom-title lines), by session.
        public Dictionary<string, string> Titles { get { lock (gate) return new Dictionary<string, string>(titles); } }
        // The first line of each session's first request (ClaudeTitles.FromPrompt), for a thread with no real title.
        public Dictionary<string, string> Prompts { get { lock (gate) return new Dictionary<string, string>(prompts); } }

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
                        string session = Path.GetFileNameWithoutExtension(file.Name);
                        DateTime seen; if (!Helper(file) && (!lastSeen.TryGetValue(session, out seen) || seen < file.LastWriteTimeUtc)) lastSeen[session] = file.LastWriteTimeUtc;
                        long offset; offsets.TryGetValue(file.FullName, out offset);
                        if (file.Length < offset) offset = 0;
                        if (file.Length == offset) continue;
                        offsets[file.FullName] = ReadFrom(file.FullName, offset, oldest);
                    }
                    Prune(oldest);
                    Failed = false;
                }
                catch (Exception) { Failed = true; }
                Scanned = true;
            }
        }

        // A reply from telemetry. Its cost is Claude Code's own, so it replaces a log line's estimate for the same reply.
        public void Add(ClaudeUsageEntry entry, DateTime now)
        {
            if (entry == null || entry.Tokens + entry.CacheRead <= 0) return;
            lock (gate)
            {
                string key = entry.Request.Length > 0 ? entry.Request : "telemetry|" + entry.Session + "|" + entry.At.Ticks + "|" + entry.Tokens;
                ClaudeUsageEntry known;
                if (replies.TryGetValue(key, out known))
                {
                    known.Cost = entry.Cost; known.FromTelemetry = true;
                    if (known.Model.Length == 0) known.Model = entry.Model;
                    if (known.Prompt == 0) { known.Prompt = entry.Prompt; known.CacheRead = entry.CacheRead; }
                    return;
                }
                if (entry.At < now - Kept) return;
                replies[key] = entry; entries.Add(entry);
            }
        }

        private void Prune(DateTime oldest)
        {
            entries.RemoveAll(e => e.At < oldest);
            foreach (var key in replies.Where(p => p.Value.At < oldest).Select(p => p.Key).ToList()) replies.Remove(key);
        }

        // A helper's log (a subagent's, under <session>\subagents\) is part of its session, not a thread of its own; its
        // usage lines carry the session's id, so they still count.
        private static bool Helper(FileInfo file)
        {
            return file.Name.StartsWith("agent-", StringComparison.OrdinalIgnoreCase) || String.Equals(file.Directory.Name, "subagents", StringComparison.OrdinalIgnoreCase);
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
                foreach (var entry in Parse(text, replies, session, folders, titles, prompts)) if (entry.At >= oldest) entries.Add(entry);
                return offset + end + 1;
            }
        }

        // The usage in a piece of a log; replies holds the replies already counted (from either source). A session's
        // project is the folder it started in: its cwd moves when its shell changes folders, which isn't another
        // project (folders remembers it).
        public static List<ClaudeUsageEntry> Parse(string text, Dictionary<string, ClaudeUsageEntry> replies, string session, Dictionary<string, string> folders = null, Dictionary<string, string> titles = null, Dictionary<string, string> prompts = null)
        {
            if (folders == null) folders = new Dictionary<string, string>();
            if (titles == null) titles = new Dictionary<string, string>();
            if (prompts == null) prompts = new Dictionary<string, string>();
            var result = new List<ClaudeUsageEntry>();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                // Most lines are prompts, tool results and attachments; only assistant lines with usage are read, but
                // any line's cwd tells which folder its session started in.
                if (line.Length == 0) continue;
                bool usageLine = line.IndexOf("\"usage\"", StringComparison.Ordinal) >= 0 && line.IndexOf("\"assistant\"", StringComparison.Ordinal) >= 0;
                bool titleLine = !usageLine && line.IndexOf("\"custom-title\"", StringComparison.Ordinal) >= 0;
                if (!usageLine && !titleLine && line.IndexOf("\"cwd\"", StringComparison.Ordinal) < 0) continue;
                object parsed; if (!ClaudeJson.TryParse(line, out parsed)) continue;
                var o = ClaudeJson.Obj(parsed);
                string id = ClaudeJson.Str(o, "sessionId") ?? session ?? "", folder;
                // The title the user typed for the session (the latest one wins); never the prompts themselves.
                if (titleLine && ClaudeJson.Str(o, "type") == "custom-title") { string title = (ClaudeJson.Str(o, "customTitle") ?? "").Trim(); if (title.Length > 0) titles[id] = title; continue; }
                if (!folders.TryGetValue(id, out folder) && !String.IsNullOrEmpty(ClaudeJson.Str(o, "cwd"))) { folder = ClaudeJson.Str(o, "cwd"); folders[id] = folder; }
                // The first request a person made in the session (not a helper's, a note Claude Code added or a
                // compacted thread's summary): its first line names a thread that has no real title. A Remote Control
                // brief can arrive as a note (its <relay><note> envelope), so a note carrying one counts.
                if (!usageLine && !prompts.ContainsKey(id) && ClaudeJson.Str(o, "type") == "user" && !ClaudeJson.Bool(o, "isSidechain") && !ClaudeJson.Bool(o, "isCompactSummary"))
                {
                    string words = Words(ClaudeJson.Obj(o, "message"));
                    if (!ClaudeJson.Bool(o, "isMeta") || ClaudeTitles.Envelope(words))
                    {
                        string asked = ClaudeTitles.FromPrompt(words);
                        if (asked.Length > 0) prompts[id] = asked;
                    }
                }
                if (!usageLine || ClaudeJson.Str(o, "type") != "assistant") continue;
                var message = ClaudeJson.Obj(o, "message"); var usage = ClaudeJson.Obj(message, "usage");
                if (usage == null) continue;
                string request = ClaudeJson.Str(o, "requestId") ?? "", key = request.Length > 0 ? request : (ClaudeJson.Str(message, "id") ?? "");
                if (key.Length > 0 && replies.ContainsKey(key)) continue;
                long input = Count(usage, "input_tokens"), output = Count(usage, "output_tokens"), creation = Count(usage, "cache_creation_input_tokens"), read = Count(usage, "cache_read_input_tokens");
                long tokens = input + output + creation;
                if (tokens + read <= 0) continue;
                var at = ClaudeBridge.ParseTime(ClaudeJson.Str(o, "timestamp"));
                if (!at.HasValue) continue;
                // A cache write is a five-minute one unless the reply says how much was kept for an hour.
                var cache = ClaudeJson.Obj(usage, "cache_creation");
                long hour = Math.Min(creation, Count(cache, "ephemeral_1h_input_tokens"));
                string model = ClaudeJson.Str(message, "model") ?? "";
                var entry = new ClaudeUsageEntry { At = at.Value, Session = id, Folder = folder ?? "", Model = model, Request = request, Tokens = tokens, Prompt = input + creation + read, CacheRead = read, Cost = ClaudePrices.Cost(model, input, output, creation - hour, hour, read) };
                if (key.Length > 0) replies[key] = entry;
                result.Add(entry);
            }
            return result;
        }
        // A message's text: its content when that is a string, else its text blocks (tool results have none).
        private static string Words(JsonObject message)
        {
            if (message == null) return "";
            var text = message.Get("content") as string;
            if (text != null) return text;
            var blocks = ClaudeJson.List(message, "content");
            return blocks == null ? "" : String.Join("\n", blocks.OfType<JsonObject>().Where(b => ClaudeJson.Str(b, "type") == "text").Select(b => ClaudeJson.Str(b, "text") ?? ""));
        }
        private static long Count(JsonObject usage, string key) { double v = ClaudeJson.Num(usage, key); return Double.IsNaN(v) || v < 0 ? 0 : (long)v; }

        // Where a period starts: the last five hours, local midnight, or the last seven days (decision 019). The plan's
        // own windows used to set 5 h and Week, but a week that had just reset hid almost every thread and project.
        public static DateTime Start(ClaudePeriod period, ClaudeLimits limits, DateTime now)
        {
            if (period == ClaudePeriod.Today) return DateTime.SpecifyKind(now.ToLocalTime().Date, DateTimeKind.Local).ToUniversalTime();
            return now - (period == ClaudePeriod.FiveHour ? TimeSpan.FromHours(5) : TimeSpan.FromDays(7));
        }

        public static long Total(IEnumerable<ClaudeUsageEntry> entries, DateTime from, string session = null)
        {
            return entries.Where(e => e.At >= from && (session == null || e.Session == session)).Sum(e => e.Tokens);
        }
        public static double Cost(IEnumerable<ClaudeUsageEntry> entries, DateTime from, string session = null)
        {
            return entries.Where(e => e.At >= from && (session == null || e.Session == session)).Sum(e => e.Cost);
        }

        // A folder as a project key: full path, backslashes, no trailing slash. Case doesn't matter on Windows.
        public static string Key(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) return "";
            string trimmed = folder.Trim().Replace('/', '\\');
            if (trimmed.Length > 3) trimmed = trimmed.TrimEnd('\\');
            return trimmed;
        }

        // The projects with the most tokens since from, biggest first; past top, the rest are added up as "Other".
        // folderOf finds a reply's folder when it came without one (telemetry); shown leaves out projects Settings →
        // Claude unticked.
        public static List<ClaudeProjectUsage> ByProject(IEnumerable<ClaudeUsageEntry> entries, DateTime from, int top, Func<ClaudeUsageEntry, string> folderOf = null, Func<string, bool> shown = null)
        {
            var sums = entries.Where(e => e.At >= from).Select(e => new { Entry = e, Folder = Key(folderOf != null ? folderOf(e) : e.Folder) })
                .Where(x => shown == null || shown(x.Folder))
                .GroupBy(x => x.Folder, StringComparer.OrdinalIgnoreCase)
                .Select(g => new ClaudeProjectUsage { Folder = g.First().Folder, Name = ClaudeBridge.ProjectName(g.First().Folder), Tokens = g.Sum(x => x.Entry.Tokens), Cost = g.Sum(x => x.Entry.Cost) })
                .Where(p => p.Tokens > 0).OrderByDescending(p => p.Tokens).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (top <= 0 || sums.Count <= top) return sums;
            var result = sums.Take(Math.Max(0, top - 1)).ToList();
            var rest = sums.Skip(Math.Max(0, top - 1)).ToList();
            result.Add(new ClaudeProjectUsage { Folder = "", Name = "Other", Tokens = rest.Sum(p => p.Tokens), Cost = rest.Sum(p => p.Cost) });
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

        // "≈ $0.42", "≈ $12", "≈ $1.2k": what the tokens would cost at API prices.
        public static string Money(double dollars)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (Double.IsNaN(dollars) || dollars <= 0) return "≈ $0";
            if (dollars < 10) return "≈ $" + dollars.ToString("0.00", culture);
            if (dollars < 1000) return "≈ $" + Math.Round(dollars).ToString(culture);
            return "≈ $" + Short(dollars / 1e3) + "k";
        }
    }
}
