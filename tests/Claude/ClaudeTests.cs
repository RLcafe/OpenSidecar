using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using WidgetScreen;

// The Claude Code watcher (docs/design/claude-watcher.md): the settings.json merge, the bridge's files, the logs, the
// plan limits, the card's sizes and the phone's words. Everything runs in temporary folders under the test output;
// Claude Code's real folder is never read or written here.
internal static class ClaudeTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        // Inner lists may scroll: what a ScrollViewer has scrolled out of (or only half into) its viewport isn't shown.
        for (var e = System.Windows.Media.VisualTreeHelper.GetParent(element); e != null && e != root; e = System.Windows.Media.VisualTreeHelper.GetParent(e))
        {
            var scroller = e as System.Windows.Controls.ScrollViewer; var item = element as FrameworkElement;
            if (scroller == null || item == null || item.ActualHeight <= 0) continue;
            var box = item.TransformToAncestor(scroller).TransformBounds(new Rect(0, 0, item.ActualWidth, item.ActualHeight));
            if (box.Top < -0.5 || box.Bottom > scroller.ActualHeight + 0.5) return false;
        }
        return true;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-checks", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static readonly DateTime Now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static string Line(string id, string request, string time, string cwd, string session, int input, int output, int creation, int read)
    {
        return "{\"type\":\"assistant\",\"sessionId\":\"" + session + "\",\"cwd\":\"" + cwd.Replace("\\", "\\\\") + "\",\"timestamp\":\"" + time + "\",\"requestId\":\"" + request + "\",\"message\":{\"id\":\"" + id + "\",\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":" + creation + ",\"cache_read_input_tokens\":" + read + "}}}";
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude JSON keeps key order, numbers and formatting", () => {
            string text = "{\n  \"model\": \"opus\",\n  \"permissions\": {\n    \"allow\": [\n      \"Bash(git:*)\"\n    ]\n  },\n  \"cleanupPeriodDays\": 1e2,\n  \"empty\": {},\n  \"list\": [],\n  \"quote\": \"a \\\"b\\\" \\\\ é\"\n}";
            Assert(ClaudeJson.Write(ClaudeJson.Parse(text)) == text, "Round trip changed the text.");
            object ignored;
            Assert(!ClaudeJson.TryParse("{\"a\": 1,}", out ignored) && !ClaudeJson.TryParse("{\"a\" 1}", out ignored) && !ClaudeJson.TryParse("[1, 2", out ignored), "Invalid JSON was accepted.");
            Assert(ClaudeJson.Parse("\uFEFF{\"a\":true}") is JsonObject, "A byte order mark broke parsing.");
        });

        check("Claude Connect merges into settings.json and Disconnect takes exactly that out", () => {
            string config = Fresh(directory, "config-merge"), bridge = Fresh(directory, "bridge-merge");
            string hook = Path.Combine(directory, ClaudeBridge.HookExe);
            var connector = new ClaudeConnector(config, bridge);
            // Existing hooks, an existing status line and other keys.
            string original = "{\n  \"model\": \"opus\",\n  \"hooks\": {\n    \"PostToolUse\": [\n      {\n        \"matcher\": \"Edit\",\n        \"hooks\": [\n          {\n            \"type\": \"command\",\n            \"command\": \"npx prettier --write\"\n          }\n        ]\n      }\n    ]\n  },\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"~/.claude/my-line.sh\",\n    \"padding\": 2\n  },\n  \"enabledPlugins\": {\n    \"a@b\": true\n  }\n}\n";
            File.WriteAllText(connector.SettingsPath, original);
            Assert(!connector.IsConnected(), "Connected before Connect.");
            connector.Connect(hook);
            Assert(connector.IsConnected() && File.Exists(connector.HookPath), "Connect didn't connect or copy the bridge.");
            Assert(File.ReadAllText(connector.SettingsPath + ClaudeConnector.BackupSuffix) == original, "The backup isn't the file as it was.");
            var root = (JsonObject)ClaudeJson.Parse(File.ReadAllText(connector.SettingsPath));
            Assert(root.Select(p => p.Key).SequenceEqual(new[] { "model", "hooks", "statusLine", "enabledPlugins", "env" }), "Keys moved.");
            var env = ClaudeJson.Obj(root, "env");
            Assert(ClaudeJson.Str(env, "CLAUDE_CODE_ENABLE_TELEMETRY") == "1" && ClaudeJson.Str(env, "OTEL_EXPORTER_OTLP_ENDPOINT") == "http://127.0.0.1:" + ClaudeTelemetry.DefaultPort && ClaudeJson.Str(env, "OTEL_EXPORTER_OTLP_PROTOCOL") == "http/json" && !env.Contains("OTEL_LOG_USER_PROMPTS") && !env.Contains("OTEL_LOG_TOOL_DETAILS"), "Telemetry settings missing, or prompts would be logged.");
            Assert(!connector.NeedsUpdate() && connector.TelemetryConflicts().Count == 0, "A fresh connection asks for an update.");
            var hooks = ClaudeJson.Obj(root, "hooks");
            var post = (List<object>)hooks.Get("PostToolUse");
            Assert(post.Count == 2 && ClaudeJson.Str(ClaudeJson.Obj(post[0]), "matcher") == "Edit" && ClaudeJson.Str(ClaudeJson.Obj(((List<object>)ClaudeJson.Obj(post[0]).Get("hooks"))[0]), "command") == "npx prettier --write", "The user's own hook was replaced.");
            foreach (var e in ClaudeConnector.Events) Assert(((List<object>)hooks.Get(e[0])).OfType<JsonObject>().Count(g => ClaudeJson.List(g, "hooks").Any(ClaudeConnector.Ours)) == 1, "Missing hook for " + e[0] + ".");
            var notification = ((List<object>)hooks.Get("Notification")).OfType<JsonObject>().Single();
            Assert(ClaudeJson.Str(notification, "matcher") == "permission_prompt|elicitation_dialog", "Notification hook matcher wrong.");
            string command = ClaudeJson.Str(ClaudeJson.Obj(ClaudeJson.List(notification, "hooks")[0]), "command");
            Assert(command.StartsWith("\"") && command.EndsWith("\" hook") && !command.Contains("\\") && command.Contains(ClaudeBridge.Marker), "Hook command not in Claude Code's form: " + command);
            var line = ClaudeJson.Obj(root, "statusLine");
            Assert(ClaudeConnector.Ours(line) && ClaudeJson.Str(line, "command").EndsWith(" statusline") && ClaudeJson.Num(line, "padding") == 2, "The status line isn't the bridge, or lost its padding.");
            Assert(ClaudeJson.Str(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(ClaudeBridge.PreviousStatusLinePath(bridge)))), "command") == "~/.claude/my-line.sh", "The earlier status line wasn't kept.");
            connector.Connect(hook);
            var again = ClaudeJson.Obj((JsonObject)ClaudeJson.Parse(File.ReadAllText(connector.SettingsPath)), "hooks");
            Assert(((List<object>)again.Get("Stop")).Count == 1 && ((List<object>)again.Get("PostToolUse")).Count == 2, "Connecting twice added hooks twice.");
            Assert(ClaudeJson.Str(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(ClaudeBridge.PreviousStatusLinePath(bridge)))), "command") == "~/.claude/my-line.sh", "Connecting twice lost the earlier status line.");
            connector.Disconnect();
            Assert(File.ReadAllText(connector.SettingsPath) == original && !connector.IsConnected() && !File.Exists(ClaudeBridge.PreviousStatusLinePath(bridge)), "Disconnect didn't put settings.json back as it was.");
        });

        check("Claude Connect works with no settings.json and refuses invalid JSON", () => {
            string config = Fresh(directory, "config-none"), bridge = Fresh(directory, "bridge-none");
            var connector = new ClaudeConnector(Path.Combine(config, "nested"), bridge);
            connector.Connect(Path.Combine(directory, ClaudeBridge.HookExe));
            var root = (JsonObject)ClaudeJson.Parse(File.ReadAllText(connector.SettingsPath));
            Assert(root.Count == 3 && root.Contains("hooks") && root.Contains("statusLine") && root.Contains("env") && !File.Exists(connector.SettingsPath + ClaudeConnector.BackupSuffix), "A new settings.json is wrong.");
            connector.Disconnect();
            Assert(File.ReadAllText(connector.SettingsPath).Trim() == "{}", "Disconnect left something behind: " + File.ReadAllText(connector.SettingsPath));

            string broken = Fresh(directory, "config-broken");
            var invalid = new ClaudeConnector(broken, Fresh(directory, "bridge-broken"));
            File.WriteAllText(invalid.SettingsPath, "{ \"model\": \"opus\", ");
            bool refused = false;
            try { invalid.Connect(Path.Combine(directory, ClaudeBridge.HookExe)); } catch (InvalidOperationException e) { refused = e.Message.Contains("valid JSON"); }
            Assert(refused && File.ReadAllText(invalid.SettingsPath) == "{ \"model\": \"opus\", " && !File.Exists(invalid.SettingsPath + ClaudeConnector.BackupSuffix) && !invalid.IsConnected(), "Invalid settings.json was touched.");
            bool missing = false;
            try { new ClaudeConnector(Fresh(directory, "config-nohook"), Fresh(directory, "bridge-nohook")).Connect(Path.Combine(directory, "nothing.exe")); } catch (InvalidOperationException) { missing = true; }
            Assert(missing, "Connected without a bridge to copy.");
            Assert(ClaudeConnector.DefaultConfigFolder == Path.Combine(directory, "claude-config"), "CLAUDE_CONFIG_DIR isn't honoured.");
        });

        check("Claude hook mode records each session's state in its own file", () => {
            string bridge = Fresh(directory, "bridge-hook");
            Func<ClaudeProcesses.Owner> owner = () => new ClaudeProcesses.Owner { ClaudePid = 4242, WindowPid = 77, Window = 1234 };
            Func<string, string> ev = (name) => "{\"session_id\":\"abc-123\",\"transcript_path\":\"C:/t.jsonl\",\"cwd\":\"C:\\\\Code\\\\api-server\",\"hook_event_name\":\"" + name + "\"";
            string path = ClaudeBridge.SessionPath(bridge, "abc-123");
            ClaudeBridge.Hook(ev("SessionStart") + ",\"source\":\"startup\"}", bridge, owner, Now);
            var s = ClaudeBridge.Read(path);
            Assert(s != null && s.State == ClaudeState.Ready && s.Project == "C:\\Code\\api-server" && s.Pid == 4242 && s.Window == 1234 && s.WindowPid == 77, "SessionStart wasn't recorded.");
            ClaudeBridge.Hook(ev("UserPromptSubmit") + ",\"prompt\":\"hi\"}", bridge, owner, Now.AddSeconds(5));
            Assert(ClaudeBridge.Read(path).State == ClaudeState.Working && ClaudeBridge.Read(path).Since == ClaudeBridge.Epoch(Now.AddSeconds(5)), "Working wasn't recorded.");
            ClaudeBridge.Hook(ev("Notification") + ",\"notification_type\":\"permission_prompt\",\"message\":\"Claude needs your permission to use Bash\"}", bridge, owner, Now.AddSeconds(9));
            s = ClaudeBridge.Read(path);
            Assert(s.State == ClaudeState.NeedsYou && s.Message == "Claude needs your permission to use Bash" && ClaudeText.Ask(s.Message) == "Wants to use Bash", "Needs you wasn't recorded.");
            ClaudeBridge.Hook(ev("PostToolUse") + ",\"tool_name\":\"Bash\"}", bridge, owner, Now.AddSeconds(20));
            Assert(ClaudeBridge.Read(path).State == ClaudeState.Working && ClaudeBridge.Read(path).Message == "", "A granted permission didn't go back to Working.");
            ClaudeBridge.Hook(ev("Stop") + "}", bridge, owner, Now.AddSeconds(30));
            ClaudeBridge.Hook(ev("Notification") + ",\"notification_type\":\"idle_prompt\",\"message\":\"Claude is waiting for your input\"}", bridge, owner, Now.AddSeconds(90));
            Assert(ClaudeBridge.Read(path).State == ClaudeState.Done, "Idle prompts must stay Done.");
            ClaudeBridge.Hook(ev("SessionStart") + ",\"source\":\"compact\"}", bridge, owner, Now.AddSeconds(95));
            Assert(ClaudeBridge.Read(path).State == ClaudeState.Done, "Compacting reset the state.");
            ClaudeBridge.Hook("{\"session_id\":\"other\",\"cwd\":\"D:/x\",\"hook_event_name\":\"UserPromptSubmit\"}", bridge, null, Now);
            Assert(Directory.GetFiles(ClaudeBridge.SessionsFolder(bridge), "*.json").Length == 2 && ClaudeBridge.Read(ClaudeBridge.SessionPath(bridge, "other")).Pid == 0, "Sessions share a file.");
            ClaudeBridge.Hook(ev("SessionEnd") + ",\"reason\":\"exit\"}", bridge, owner, Now.AddSeconds(99));
            Assert(!File.Exists(path) && !Directory.GetFiles(ClaudeBridge.SessionsFolder(bridge), "*.tmp").Any(), "SessionEnd didn't remove the session, or left a temporary file.");
            Assert(ClaudeBridge.SafeName("../../evil") == "evil" && ClaudeBridge.SafeName("") == "session", "Session ids aren't made safe for file names.");
        });

        check("Claude status line keeps limits, the session's name and model, and prints a short line", () => {
            string bridge = Fresh(directory, "bridge-status");
            long five = (long)(Now.AddHours(2) - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            string input = "{\"session_id\":\"s1\",\"session_name\":\"Fix login\",\"cwd\":\"C:/Code/api\",\"workspace\":{\"project_dir\":\"C:/Code/api\"},\"model\":{\"display_name\":\"Opus 5.5\"},\"context_window\":{\"used_percentage\":8.2},\"cost\":{\"total_api_duration_ms\":1200},\"rate_limits\":{\"five_hour\":{\"used_percentage\":23.5,\"resets_at\":" + five + "},\"seven_day\":{\"used_percentage\":41.2,\"resets_at\":\"2026-11-05T09:00:00Z\"}}}";
            string printed = ClaudeBridge.StatusLine(input, bridge, null, Now);
            Assert(printed == "5h 24% · week 41% · ctx 8%" || printed == "5h 23% · week 41% · ctx 8%", "Status line text wrong: " + printed);
            var limits = ClaudeBridge.ReadLimits(bridge);
            Assert(limits.FiveHour.Percent == 23.5 && limits.FiveHour.ResetsAt == Now.AddHours(2) && limits.Week.ResetsAt == new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc) && limits.Source == "statusline" && limits.UpdatedAt == Now, "Limits weren't kept.");
            var s = ClaudeBridge.Read(ClaudeBridge.SessionPath(bridge, "s1"));
            Assert(s.Title == "Fix login" && s.Model == "Opus 5.5" && Math.Round(s.Context) == 8 && s.Project == "C:/Code/api", "The session's details weren't kept.");
            // An API-key session: Claude Code answered, but no limits came.
            string api = Fresh(directory, "bridge-api");
            Assert(ClaudeBridge.StatusLine("{\"session_id\":\"s2\",\"cost\":{\"total_api_duration_ms\":900}}", api, null, Now) == "Claude Code", "A line with nothing known isn't the default.");
            var view = new ClaudeView { Limits = ClaudeBridge.ReadLimits(api), Plan = ClaudePlanState.Current };
            Assert(view.NoPlan && ClaudeText.LimitsMissing(view) == "Plan limits need a Claude Pro or Max login.", "No-plan state not recognised.");
            Assert(ClaudeText.LimitsMissing(new ClaudeView()) == "Sign in to Claude in Settings → Claude to see plan limits." && ClaudeText.LimitsMissing(new ClaudeView { Plan = ClaudePlanState.Expired }) == "Your Claude sign-in has ended. Sign in again in Settings → Claude.", "Plan sign-in messages wrong.");
        });

        check("Claude rate limits and the usage check parse epoch and ISO times", () => {
            var rate = (JsonObject)ClaudeJson.Parse("{\"five_hour\":{\"used_percentage\":12,\"resets_at\":1738425600},\"seven_day\":{\"used_percentage\":120,\"resets_at\":\"2025-02-06T16:00:00+00:00\"}}");
            var limits = ClaudeBridge.ParseRateLimits(rate, Now);
            Assert(limits.FiveHour.ResetsAt == new DateTime(2025, 2, 1, 16, 0, 0, DateTimeKind.Utc) && limits.Week.ResetsAt == new DateTime(2025, 2, 6, 16, 0, 0, DateTimeKind.Utc) && limits.Week.Percent == 100, "Rate limits parsed wrongly.");
            Assert(ClaudeBridge.ParseRateLimits((JsonObject)ClaudeJson.Parse("{\"seven_day\":{\"used_percentage\":5}}"), Now).FiveHour == null, "A missing window was invented.");
            Assert(ClaudeBridge.ParseTime(new JsonNumber("1738425600000")) == new DateTime(2025, 2, 1, 16, 0, 0, DateTimeKind.Utc) && ClaudeBridge.ParseTime("nonsense") == null, "Milliseconds or bad times parsed wrongly.");
            var online = ClaudeBridge.ParseUsageResponse("{\"five_hour\":{\"utilization\":37.0,\"resets_at\":\"2026-11-01T15:59:59.943648+00:00\"},\"seven_day\":{\"utilization\":61,\"resets_at\":\"2026-11-06T03:59:59+00:00\"},\"seven_day_opus\":null,\"extra\":[1]}", Now);
            Assert(online != null && online.Source == "online" && online.FiveHour.Percent == 37 && online.FiveHour.ResetsAt.Value.Hour == 15 && online.Week.Percent == 61, "The usage check's answer parsed wrongly.");
            Assert(ClaudeBridge.ParseUsageResponse("{\"five_hour\":null,\"seven_day\":null}", Now) == null && ClaudeBridge.ParseUsageResponse("<html>", Now) == null, "An empty or broken answer was used.");
            var stale = new ClaudeLimitWindow { Percent = 90, ResetsAt = Now.AddMinutes(-1) };
            Assert(!stale.Current(Now) && ClaudeText.Percent(stale, Now) == 0, "A window past its reset still counts.");
            Assert(ClaudeText.Reset(new ClaudeLimitWindow { Percent = 100, ResetsAt = Now.AddHours(1) }, Now, true).StartsWith("Limit reached · back ") && ClaudeText.Reset(new ClaudeLimitWindow { Percent = 40, ResetsAt = Now.AddDays(3) }, Now, false).StartsWith("resets "), "Reset words wrong.");
            Assert(ClaudeText.Warns(80) && !ClaudeText.Warns(79.9) && SegmentBar.Lit(23) == 2 && SegmentBar.Lit(3) == 1 && SegmentBar.Lit(0) == 0 && SegmentBar.Lit(99) == 9 && SegmentBar.Lit(100) == 10, "Meter rules wrong.");
        });

        check("Claude usage counts each reply once, by project, session and period", () => {
            var seen = new Dictionary<string, ClaudeUsageEntry>();
            string text = String.Join("\n", new[] {
                "{\"type\":\"user\",\"message\":{\"content\":\"usage assistant\"}}",
                Line("m1", "r1", "2026-11-01T11:00:00Z", "C:\\Code\\api-server", "s1", 10, 20, 30, 40),
                Line("m1", "r1", "2026-11-01T11:00:01Z", "C:\\Code\\api-server", "s1", 10, 20, 30, 40),
                Line("m2", "r2", "2026-11-01T06:00:00Z", "C:\\Code\\api-server", "s1", 1000, 0, 0, 0),
                Line("m3", "r3", "2026-10-28T12:00:00Z", "D:/work/widget-screen/", "s2", 0, 500, 0, 0),
                Line("m4", "r4", "2026-10-28T12:05:00Z", "D:/work/widget-screen/android", "s2", 0, 1, 0, 0),
                "not json at all",
                "" });
            var entries = ClaudeUsage.Parse(text, seen, "file");
            Assert(entries.Count == 4 && entries[0].Tokens == 100 && ClaudeBridge.ProjectName(entries[0].Folder) == "api-server" && ClaudeBridge.ProjectName(entries[2].Folder) == "widget-screen" && entries[2].Session == "s2" && ClaudeBridge.ProjectName(entries[3].Folder) == "widget-screen", "Usage lines parsed wrongly, or a folder change became another project.");
            Assert(ClaudeUsage.Parse(Line("m1", "r1", "2026-11-01T11:00:00Z", "C:/x", "s1", 1, 1, 1, 1), seen, "file").Count == 0, "A repeated reply counted twice across reads.");
            var limits = new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = 10, ResetsAt = Now.AddHours(1) } };
            Assert(ClaudeUsage.Start(ClaudePeriod.FiveHour, limits, Now) == Now.AddHours(-4) && ClaudeUsage.Start(ClaudePeriod.FiveHour, null, Now) == Now.AddHours(-5) && ClaudeUsage.Start(ClaudePeriod.Week, null, Now) == Now.AddDays(-7), "Period starts wrong.");
            Assert(ClaudeUsage.Total(entries, ClaudeUsage.Start(ClaudePeriod.FiveHour, limits, Now)) == 100 && ClaudeUsage.Total(entries, Now.AddDays(-7)) == 1601 && ClaudeUsage.Total(entries, Now.AddDays(-7), "s1") == 1100, "Totals wrong.");
            var top = ClaudeUsage.ByProject(entries, Now.AddDays(-7), 6);
            Assert(top.Count == 2 && top[0].Name == "api-server" && top[0].Tokens == 1100 && top[0].Folder == "C:\\Code\\api-server" && top[1].Folder == "D:\\work\\widget-screen", "By-project totals wrong.");
            var many = Enumerable.Range(0, 9).Select(i => new ClaudeUsageEntry { At = Now, Folder = "C:\\p" + i, Tokens = 100 - i, Cost = 1 }).ToList();
            var six = ClaudeUsage.ByProject(many, Now.AddHours(-1), 6);
            Assert(six.Count == 6 && six[5].Name == "Other" && six[5].Tokens == 95 + 94 + 93 + 92 && six[5].Cost == 4, "The rest isn't added up as Other.");
            Assert(ClaudeUsage.Format(950) == "950" && ClaudeUsage.Format(12400) == "12.4k" && ClaudeUsage.Format(1234567) == "1.2M" && ClaudeUsage.Format(3400000000) == "3.4B" && ClaudeUsage.Format(999999) == "999k", "Token counts are formatted wrongly.");

            // Incremental reads: only new lines, and a line still being written waits.
            string config = Fresh(directory, "config-logs");
            string folder = Path.Combine(config, "projects", "C--Code-api-server"); Directory.CreateDirectory(folder);
            string log = Path.Combine(folder, "s1.jsonl");
            File.WriteAllText(log, Line("a", "1", "2026-11-01T11:00:00Z", "C:\\Code\\api-server", "s1", 1, 1, 1, 1) + "\n" + Line("b", "2", "2026-11-01T11:01:00Z", "C:\\Code\\api-server", "s1", 2, 2, 2, 2).Substring(0, 40));
            var usage = new ClaudeUsage(config);
            usage.Scan(DateTime.UtcNow);
            Assert(usage.Entries.Count == 1 && !usage.Failed, "The first scan read a partial line.");
            File.WriteAllText(log, Line("a", "1", "2026-11-01T11:00:00Z", "C:\\Code\\api-server", "s1", 1, 1, 1, 1) + "\n" + Line("b", "2", "2026-11-01T11:01:00Z", "C:\\Code\\api-server", "s1", 2, 2, 2, 2) + "\n");
            usage.Scan(DateTime.UtcNow);
            Assert(usage.Entries.Count == 2 && usage.Entries.Sum(e => e.Tokens) == 12, "The finished line wasn't read once.");
            var none = new ClaudeUsage(Fresh(directory, "config-empty")); none.Scan(DateTime.UtcNow);
            Assert(none.Failed && none.Scanned, "A missing logs folder isn't reported.");
        });

        check("Claude sessions are read back, ordered, and dropped when forgotten", () => {
            string bridge = Fresh(directory, "bridge-read");
            Action<string, ClaudeState, int, int> write = (id, state, minutes, pid) => ClaudeBridge.WriteAtomic(ClaudeBridge.SessionPath(bridge, id), ClaudeJson.Compact(new ClaudeSessionFile { Id = id, State = state, Project = "C:/Code/" + id, Since = ClaudeBridge.Epoch(Now.AddMinutes(-minutes)), LastEvent = ClaudeBridge.Epoch(Now.AddMinutes(-minutes)), Pid = pid }.ToJson()));
            write("ready", ClaudeState.Ready, 1, 0); write("done", ClaudeState.Done, 3, 0); write("work", ClaudeState.Working, 2, 0);
            write("needs-old", ClaudeState.NeedsYou, 9, 0); write("needs-new", ClaudeState.NeedsYou, 1, 0); write("ancient", ClaudeState.Working, 60 * 13, 0);
            // A process that isn't Claude Code: the session's Claude Code has gone.
            write("gone", ClaudeState.Working, 1, Process.GetCurrentProcess().Id);
            var service = new ClaudeService(bridge, Fresh(directory, "config-read"));
            Assert(service.ReadSessions(Now, true), "Nothing was read.");
            var view = service.View(Now, null);
            Assert(view.Sessions.Select(s => s.Id).SequenceEqual(new[] { "needs-old", "needs-new", "work", "done", "ready" }), "Order wrong: " + String.Join(",", view.Sessions.Select(s => s.Id)));
            Assert(!File.Exists(ClaudeBridge.SessionPath(bridge, "ancient")) && !File.Exists(ClaudeBridge.SessionPath(bridge, "gone")), "Forgotten or closed sessions linger.");
            Assert(!service.ReadSessions(Now, false), "Unchanged files were read again.");
            Assert(ClaudeText.Heading(view) == "2 sessions need you" && ClaudeText.StateWords(ClaudeState.NeedsYou, Now.AddMinutes(-2), Now, false) == "Needs you · 2 min" && ClaudeText.StateWords(ClaudeState.Working, Now, Now, false) == "Working" && ClaudeText.StateWords(ClaudeState.Done, Now.AddHours(-3), Now, true) == "Done · 3 h", "State words wrong.");
            Assert(ClaudeBridge.ProjectName("C:\\Code\\api-server\\") == "api-server" && ClaudeBridge.ProjectName("/home/a/b") == "b" && ClaudeBridge.ProjectName("") == "Claude Code", "Project names wrong.");
        });

        check("Claude critters pick the same body and colour for a project", () => {
            Assert(CritterPose.Hash("api-server") == CritterPose.Hash(" API-Server ") && CritterPose.Hash("") == 2166136261u && CritterPose.Hash("a") == 3826002220u, "The critter hash changed.");
            var shapes = new HashSet<int>(); var accents = new HashSet<int>();
            foreach (var name in new[] { "api-server", "widget-screen", "photo-manager", "notes", "scada-bom", "aeq", "otsec", "brainstorming" }) { shapes.Add(CritterPose.Shape(name)); accents.Add(CritterPose.Accent(name)); }
            Assert(shapes.Count == 3 && accents.Count == 3 && shapes.All(s => s >= 0 && s < 3), "Projects don't spread over the bodies and colours.");
            Assert(CritterPose.Hop(ClaudeState.NeedsYou, true, false) == -4 && CritterPose.Hop(ClaudeState.NeedsYou, true, true) == 0 && CritterPose.PupilX(ClaudeState.Working, true, false) == 3 && CritterPose.PupilX(ClaudeState.Working, true, true) == -3, "Quiet mode doesn't keep the critters still.");
            foreach (int shape in new[] { 0, 1, 2 }) { var b = CritterPose.Body(shape); Assert(b[0] >= 10 && b[1] >= 26 && b[2] <= 90 && b[3] <= 94 && CritterPose.MouthY(b) < b[3], "A body leaves its box."); }
        });

        check("Claude picks small, medium and large designs", () => {
            Assert(CardSizing.Claude(260, 180) == CardSizing.Design.Small && CardSizing.Claude(359, 400) == CardSizing.Design.Small && CardSizing.Claude(600, 279) == CardSizing.Design.Small && CardSizing.Claude(472, 310) == CardSizing.Design.Medium && CardSizing.Claude(639, 576) == CardSizing.Design.Medium && CardSizing.Claude(640, 420) == CardSizing.Design.Large, "Claude designs picked at the wrong sizes.");
            Assert(CardSizing.ClaudeStrip(1) == 84 && CardSizing.ClaudeStrip(1.4) == 94 && CardSizing.ClaudeColumns(1808, 3) == 3 && CardSizing.ClaudeColumns(592, 5) == 2 && CardSizing.ClaudeColumns(200, 2) == 1 && CardSizing.ClaudeColumns(900, 0) == 0
                && CardSizing.ClaudeBabyCell(1) == 84 && CardSizing.ClaudeBabyCell(1.4) == 90 && CardSizing.ClaudeBabiesPerRow(400, 1) == 4 && CritterPose.Baby(100) == 40 && CritterPose.Baby(40) == 24, "Claude sizes wrong.");
            // The same numbers as the phone's CardSizing (AndroidTests: "Claude project critters…").
            Assert(CardSizing.ClaudeMediumColumns(424, 5) == 3 && CardSizing.ClaudeMediumColumns(1000, 3) == 3 && CardSizing.ClaudeMediumBabies(130, 1) && !CardSizing.ClaudeMediumBabies(70, 1)
                && CardSizing.ClaudeMediumBabyCount(140, 30, 5) == 3 && CardSizing.ClaudeMediumBabyCount(60, 30, 5) == 0 && CardSizing.ClaudeMediumBabyCount(140, 30, 2) == 2
                && !CardSizing.ClaudeSmallPercent(132, 1.4) && CardSizing.ClaudeSmallPercent(132, 1), "Claude project critters and captions sized wrongly.");
            Assert(!WidgetLayout.Default(new AppSettings()).First(p => p.Id == "Claude").Visible && WidgetLayout.Minimum("Claude") == new Size(260, 180) && WidgetLayout.Title("Claude") == "Claude Code", "Claude isn't a hidden widget with its minimum.");
            Assert(!new AppSettings().ClaudeChime && new AppSettings().ClaudeShowNewProjects && new AppSettings().ClaudeProjects == null, "Claude settings defaults wrong.");
        });

        check("Claude designs keep text and critters inside the card at 140%", () => {
            var fixtureNow = DateTime.UtcNow;
            var views = new Dictionary<string, Func<ClaudeView>> {
                { "sample", () => ClaudeView.Fixture(fixtureNow, false) },
                { "needs", () => { var v = ClaudeView.Fixture(fixtureNow, true); v.Sessions[0].Title = "A very long session title that never ends and keeps going"; v.Sessions[0].ProjectName = "an-extremely-long-project-folder-name-for-checks"; return v; } },
                { "unconnected", () => new ClaudeView { Connected = false, Sample = true } },
                { "signedout", () => { var v = ClaudeView.Fixture(fixtureNow, false); v.Limits = null; v.Plan = ClaudePlanState.SignedOut; return v; } },
                { "expired", () => { var v = ClaudeView.Fixture(fixtureNow, false); v.Plan = ClaudePlanState.Expired; v.Limits.UpdatedAt = fixtureNow.AddHours(-2); return v; } },
                { "empty", () => new ClaudeView { Connected = true, Sample = true, LogsRead = true, Limits = new ClaudeLimits { MissingAt = fixtureNow } } } };
            foreach (var size in new[] { new Size(260, 180), new Size(359, 300), new Size(472, 310), new Size(360, 280), new Size(639, 576), new Size(640, 420), new Size(900, 576), new Size(1856, 576) })
            {
                string path = Path.Combine(directory, "claude-" + size.Width + "x" + size.Height + ".xml");
                var settings = new AppSettings { TextScale = 1.4 };
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Claude"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                SettingsStore.Save(path, settings);
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            controller.UseClaudeSample(false);
                            foreach (var pair in views)
                            {
                                controller.ClaudeCardView.Update(pair.Value());
                                var dashboard = (FrameworkElement)window.FindName("Dashboard");
                                dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                                var card = (FrameworkElement)window.FindName("ClaudeCard");
                                Assert(card.Visibility == Visibility.Visible && window.FindName("ClaudeView") != null && window.FindName("ClaudeCritter0") != null, "Claude built no design at " + size + " (" + pair.Key + ").");
                                Assert(controller.ClaudeCardView.Current == CardSizing.Claude(size.Width, size.Height), "Claude picked the wrong design at " + size + ".");
                                if (pair.Key == "needs") Assert(((System.Windows.Controls.Border)card).BorderThickness.Left == 2, "The card doesn't light up when a session needs you.");
                                if (pair.Key == "sample") Assert(((System.Windows.Controls.Border)card).BorderThickness.Left == 1, "The card stays lit.");
                                foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is System.Windows.Controls.TextBlock || e is ClaudeCritter || e is SegmentBar || e is MeterBar || e is System.Windows.Controls.Button)))
                                {
                                    var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                    Assert(box.Left >= 22.5 && box.Top >= 22.5 && box.Right <= card.ActualWidth - 22.5 && box.Bottom <= card.ActualHeight - 22.5, "Claude (" + pair.Key + ") pushes " + part.GetType().Name + " " + (part is System.Windows.Controls.TextBlock ? "\"" + ((System.Windows.Controls.TextBlock)part).Text + "\" " : "") + "out of its " + size + " card: " + box);
                                }
                            }
                        }
                    }
                    finally { window.Close(); }
                }
            }
        });

        check("ClaudeHook.exe writes the session and runs the earlier status line", () => {
            string folder = Fresh(directory, "hook-exe");
            File.Copy(Path.Combine(directory, ClaudeBridge.HookExe), Path.Combine(folder, ClaudeBridge.HookExe));
            Func<string, string, string> run = (mode, input) => {
                var start = new ProcessStartInfo(Path.Combine(folder, ClaudeBridge.HookExe), mode) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
                using (var process = Process.Start(start))
                {
                    var bytes = new UTF8Encoding(false).GetBytes(input); process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); process.StandardInput.Close();
                    string output = process.StandardOutput.ReadToEnd();
                    Assert(process.WaitForExit(15000) && process.ExitCode == 0, "ClaudeHook.exe " + mode + " didn't exit 0.");
                    return output;
                }
            };
            var watch = Stopwatch.StartNew();
            run("hook", "{\"session_id\":\"exe-1\",\"cwd\":\"C:/Code/api\",\"hook_event_name\":\"UserPromptSubmit\"}");
            var s = ClaudeBridge.Read(ClaudeBridge.SessionPath(folder, "exe-1"));
            Assert(s != null && s.State == ClaudeState.Working, "The hook exe didn't write the session.");
            Assert(run("hook", "not json") == "" && run("nonsense", "") .Contains("Settings → Claude"), "Bad input broke the hook exe.");
            string line = run("statusline", "{\"session_id\":\"exe-1\",\"context_window\":{\"used_percentage\":12}}");
            Assert(line == "ctx 12%\n", "The default status line is wrong: " + line);
            ClaudeBridge.WriteAtomic(ClaudeBridge.PreviousStatusLinePath(folder), "{\"type\":\"command\",\"command\":\"echo previous-line\"}");
            line = run("statusline", "{\"session_id\":\"exe-1\"}");
            Assert(line.Trim() == "previous-line", "The earlier status line wasn't passed on: " + line);
            Assert(watch.Elapsed < TimeSpan.FromSeconds(20), "The bridge is slow.");
        });
    }
}
