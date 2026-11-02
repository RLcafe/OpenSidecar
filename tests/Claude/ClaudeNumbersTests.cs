using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using WidgetScreen;

// Package 2 of the watcher plan, "Honest numbers": Claude Code's own cost and clocks from the status line, a spend
// limit, unknown models shown as unpriced rather than $0, telemetry's tool runs, API errors and counters, the car
// revving past its tool's usual time, the 5-hour ETA, the chunked first scan, helpers' tokens apart, and the side
// panel's chosen facts. Fixtures are shaped like Claude Code's statusline and monitoring docs. Everything runs in
// throwaway folders; Claude Code's real folder is never read or written.
internal static class ClaudeNumbersTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-numbers", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-6; }
    private static string Nanos(DateTime time) { return (ClaudeBridge.Epoch(time) * 1000000L).ToString(CultureInfo.InvariantCulture); }
    private static string Seconds(DateTime time) { return (ClaudeBridge.Epoch(time) / 1000).ToString(CultureInfo.InvariantCulture); }

    // OTLP JSON pieces.
    private static string S(string key, string value) { return "{\"key\":\"" + key + "\",\"value\":{\"stringValue\":\"" + value + "\"}}"; }
    private static string I(string key, long value) { return "{\"key\":\"" + key + "\",\"value\":{\"intValue\":\"" + value.ToString(CultureInfo.InvariantCulture) + "\"}}"; }
    private static string D(string key, double value) { return "{\"key\":\"" + key + "\",\"value\":{\"doubleValue\":" + value.ToString("R", CultureInfo.InvariantCulture) + "}}"; }
    private static string Record(string name, string session, DateTime at, params string[] attributes)
    {
        var all = new List<string> { S("event.name", name), S("session.id", session) }; all.AddRange(attributes);
        return "{\"timeUnixNano\":\"" + Nanos(at) + "\",\"body\":{\"stringValue\":\"claude_code." + name + "\"},\"attributes\":[" + String.Join(",", all) + "]}";
    }
    private static string Logs(params string[] records)
    {
        return "{\"resourceLogs\":[{\"resource\":{\"attributes\":[" + S("service.name", "claude-code") + "]},\"scopeLogs\":[{\"scope\":{\"name\":\"com.anthropic.claude_code.events\"},\"logRecords\":[" + String.Join(",", records) + "]}]}]}";
    }
    private static string Point(string session, DateTime at, double value, params string[] attributes)
    {
        var all = new List<string> { S("session.id", session) }; all.AddRange(attributes);
        return "{\"attributes\":[" + String.Join(",", all) + "],\"startTimeUnixNano\":\"" + Nanos(at.AddHours(-1)) + "\",\"timeUnixNano\":\"" + Nanos(at) + "\",\"asDouble\":" + value.ToString("R", CultureInfo.InvariantCulture) + "}";
    }
    private static string Sum(string name, int temporality, params string[] points)
    {
        return "{\"name\":\"claude_code." + name + "\",\"sum\":{\"aggregationTemporality\":" + temporality + ",\"isMonotonic\":true,\"dataPoints\":[" + String.Join(",", points) + "]}}";
    }
    private static string Metrics(params string[] sums)
    {
        return "{\"resourceMetrics\":[{\"resource\":{\"attributes\":[" + S("service.name", "claude-code") + "]},\"scopeMetrics\":[{\"metrics\":[" + String.Join(",", sums) + "]}]}]}";
    }
    private static string LogLine(string request, DateTime at, string session, string model, int input, string extra = "")
    {
        return "{\"type\":\"assistant\",\"sessionId\":\"" + session + "\",\"cwd\":\"C:\\\\Code\\\\api\",\"timestamp\":\"" + at.ToString("o", CultureInfo.InvariantCulture) + "\",\"requestId\":\"" + request + "\"" + extra
            + ",\"message\":{\"id\":\"msg-" + request + "\",\"model\":\"" + model + "\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":100,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";
    }
    // A running session in the bridge folder (this test exe's process keeps it alive; seen now, so the process isn't
    // younger than the session).
    private static void Running(string bridge, string id, DateTime at)
    {
        var file = new ClaudeSessionFile { Id = id, State = ClaudeState.Working, Project = "C:/Code/api", Since = ClaudeBridge.Epoch(at), LastEvent = ClaudeBridge.Epoch(at), Pid = Process.GetCurrentProcess().Id };
        ClaudeBridge.WriteAtomic(ClaudeBridge.SessionPath(bridge, id), ClaudeJson.Compact(file.ToJson()));
    }
    private static ClaudeService Service(string directory, string name, params string[] running)
    {
        string bridge = Fresh(directory, name + "-bridge");
        DateTime real = DateTime.UtcNow;
        foreach (var id in running) Running(bridge, id, real);
        var service = new ClaudeService(bridge, Fresh(directory, name + "-config"), appThreads: new ClaudeAppThreads(() => new string[0]));
        service.ReadSessions(real, true);
        return service;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude status line keeps Claude Code's cost, clocks, lines, setup and a spend limit, and only rewrites on news", () => {
            string bridge = Fresh(directory, "status");
            string input = "{\"session_id\":\"n1\",\"cwd\":\"C:/Code/api\",\"model\":{\"id\":\"claude-opus-5-5\",\"display_name\":\"Opus 5.5\"},"
                + "\"cost\":{\"total_cost_usd\":1.234,\"total_duration_ms\":120000,\"total_api_duration_ms\":60000,\"total_lines_added\":40,\"total_lines_removed\":7},"
                + "\"context_window\":{\"used_percentage\":20,\"context_window_size\":1000000},\"exceeds_200k_tokens\":true,\"output_style\":{\"name\":\"Explanatory\"},"
                + "\"agent\":{\"name\":\"reviewer\"},\"worktree\":{\"name\":\"fix-login\",\"branch\":\"claude/fix-login\",\"path\":\"C:/Code/api/.worktrees/fix-login\"},"
                + "\"rate_limits\":{\"five_hour\":{\"used_percentage\":30,\"resets_at\":" + Seconds(Now.AddHours(2)) + "},\"seven_day\":{\"used_percentage\":50,\"resets_at\":" + Seconds(Now.AddDays(3)) + "},"
                + "\"spend_limit\":{\"used_percentage\":85.5,\"resets_at\":" + Seconds(Now.AddDays(10)) + ",\"used_usd\":42.75,\"limit_usd\":50,\"period\":\"month\"}}}";
            ClaudeBridge.StatusLine(input, bridge, null, Now);
            string path = ClaudeBridge.SessionPath(bridge, "n1");
            var s = ClaudeBridge.Read(path);
            Assert(Near(s.CostUsd, 1.234) && s.DurationMs == 120000 && s.ApiMs == 60000 && s.LinesAdded == 40 && s.LinesRemoved == 7 && s.ContextSize == 1000000 && s.Over200k, "Claude Code's cost, clocks or lines weren't kept.");
            Assert(s.Style == "Explanatory" && s.Agent == "reviewer" && s.Worktree == "fix-login" && s.WorktreeBranch == "claude/fix-login", "The session's setup wasn't kept.");
            var round = ClaudeSessionFile.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(ClaudeJson.Compact(s.ToJson()))));
            Assert(Near(round.CostUsd, 1.234) && round.ApiMs == 60000 && round.Over200k && round.WorktreeBranch == "claude/fix-login" && Double.IsNaN(new ClaudeSessionFile().CostUsd), "The session file doesn't round-trip the new fields.");
            var limits = ClaudeBridge.ReadLimits(bridge);
            Assert(limits.Spend != null && limits.Spend.Percent == 85.5 && limits.Spend.ResetsAt == Now.AddDays(10) && limits.Spend.UsedUsd == 42.75 && limits.Spend.LimitUsd == 50 && limits.Spend.Period == "month", "The spend limit wasn't kept.");
            var chips = ClaudeText.PlanChips(limits, Now, true);
            Assert(chips.Count == 1 && chips[0].Key.StartsWith("Spend limit 86%") && chips[0].Key.EndsWith(" month") && chips[0].Value, "The spend limit chip is wrong: " + String.Join(" | ", chips.Select(c => c.Key)));
            // 30 s later only the clocks moved (under a minute): the file isn't rewritten.
            string later = input.Replace("\"total_duration_ms\":120000", "\"total_duration_ms\":150000");
            ClaudeBridge.StatusLine(later, bridge, null, Now.AddSeconds(30));
            Assert(ClaudeBridge.Read(path).LastEvent == ClaudeBridge.Epoch(Now) && ClaudeBridge.Read(path).DurationMs == 120000, "A refresh with only the clocks moved rewrote the session.");
            // A cent more: written, with the clocks.
            ClaudeBridge.StatusLine(later.Replace("1.234", "1.25"), bridge, null, Now.AddSeconds(60));
            var after = ClaudeBridge.Read(path);
            Assert(Near(after.CostUsd, 1.25) && after.DurationMs == 150000 && after.LastEvent == ClaudeBridge.Epoch(Now.AddSeconds(60)), "A cost change wasn't written with the clocks.");
            // Nothing about a spend limit: no chip, and the file keeps none.
            string plain = Fresh(directory, "status-plain");
            ClaudeBridge.StatusLine("{\"session_id\":\"n2\",\"rate_limits\":{\"five_hour\":{\"used_percentage\":5,\"resets_at\":" + Seconds(Now.AddHours(1)) + "}}}", plain, null, Now);
            Assert(ClaudeBridge.ReadLimits(plain).Spend == null && ClaudeText.PlanChips(ClaudeBridge.ReadLimits(plain), Now, true).Count == 0, "A spend limit was invented.");
        });

        check("Claude Connect makes the status line refresh every 30 s, keeps the user's own interval, and old connections offer Update", () => {
            string config = Fresh(directory, "connect-config"), bridge = Fresh(directory, "connect-bridge");
            string hook = Path.Combine(directory, ClaudeBridge.HookExe);
            var connector = new ClaudeConnector(config, bridge);
            connector.Connect(hook);
            Func<JsonObject> root = () => ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(connector.SettingsPath)));
            Assert(ClaudeJson.Num(ClaudeJson.Obj(root(), "statusLine"), "refreshInterval") == ClaudeConnector.StatusRefresh && ClaudeConnector.StatusRefresh == 30 && !connector.NeedsUpdate(), "Connect didn't set the 30 s refresh.");
            // A connection from before package 2: no refreshInterval.
            var old = root(); ClaudeJson.Obj(old, "statusLine").Remove("refreshInterval");
            File.WriteAllText(connector.SettingsPath, ClaudeJson.Write(old));
            Assert(connector.NeedsUpdate(), "An old connection without the refresh doesn't offer Update connection.");
            connector.Connect(hook);
            Assert(!connector.NeedsUpdate() && ClaudeJson.Num(ClaudeJson.Obj(root(), "statusLine"), "refreshInterval") == 30, "Update connection didn't add the refresh.");
            connector.Disconnect();
            // The user's own status line refreshes every 5 s: Connect keeps that, Disconnect puts theirs back as it was.
            string mine = "{\n  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"~/.claude/line.sh\",\n    \"refreshInterval\": 5\n  }\n}\n";
            File.WriteAllText(connector.SettingsPath, mine);
            connector.Connect(hook);
            var line = ClaudeJson.Obj(root(), "statusLine");
            Assert(ClaudeConnector.Ours(line) && ClaudeJson.Num(line, "refreshInterval") == 5 && !connector.NeedsUpdate(), "The user's own refresh wasn't kept.");
            connector.Disconnect();
            Assert(File.ReadAllText(connector.SettingsPath) == mine, "Disconnect didn't put the user's status line back.");
        });

        check("Claude prices: an unknown model is unpriced (not $0), Claude Code's own cost wins, and the status line fills the gap", () => {
            var seen = new Dictionary<string, ClaudeUsageEntry>();
            var parsed = ClaudeUsage.Parse(LogLine("q1", Now, "p1", "claude-mystery-9", 1000) + "\n" + LogLine("q2", Now, "p1", "claude-opus-5-5", 1000) + "\n", seen, "p1");
            Assert(parsed.Count == 2 && !parsed[0].Priced && parsed[0].Cost == 0 && parsed[1].Priced && parsed[1].Cost > 0 && parsed[1].CostSource == "prices", "Unknown and known models aren't told apart.");
            // Telemetry: micro-dollars win over cost_usd; with neither and an unknown model, it is unpriced.
            var events = ClaudeOtlp.ParseLogs(Logs(
                Record("api_request", "p1", Now, S("model", "claude-opus-5-5"), I("input_tokens", 100), I("output_tokens", 10), I("cost_usd_micros", 2500000), D("cost_usd", 9), S("request_id", "t1")),
                Record("api_request", "p1", Now, S("model", "claude-mystery-9"), I("input_tokens", 100), I("output_tokens", 10), S("request_id", "t2")),
                Record("api_request", "p1", Now, S("model", "claude-opus-5-5"), I("input_tokens", 100), I("output_tokens", 10), D("cost_usd", 0.75), S("request_id", "t3"))));
            Assert(events.Count == 3 && Near(events[0].Usage.Cost, 2.5) && events[0].Usage.FromTelemetry && events[0].Usage.CostSource == "claude", "Claude Code's micro-dollars didn't win.");
            Assert(!events[1].Usage.Priced && !events[1].Usage.FromTelemetry && events[1].Usage.Cost == 0 && Near(events[2].Usage.Cost, 0.75) && events[2].Usage.Priced, "Telemetry's unpriced or cost_usd reply is wrong.");
            // The status line says the session cost $3: $1 is priced, so the other $2 is shared by size (300 and 100).
            var usage = new List<ClaudeUsageEntry> {
                new ClaudeUsageEntry { Session = "s1", At = Now, Tokens = 100, Cost = 1, Priced = true },
                new ClaudeUsageEntry { Session = "s1", At = Now, Tokens = 200, CacheRead = 100, Model = "claude-mystery-9", Priced = false },
                new ClaudeUsageEntry { Session = "s1", At = Now, Tokens = 100, Model = "claude-mystery-9", Priced = false },
                new ClaudeUsageEntry { Session = "s2", At = Now, Tokens = 100, Model = "claude-mystery-9", Priced = false } };
            var filled = ClaudeService.Fill(usage, new Dictionary<string, double> { { "s1", 3 } });
            Assert(filled.Count == 4 && Near(filled[1].Cost, 1.5) && Near(filled[2].Cost, 0.5) && filled[1].Priced && filled[1].CostSource == "statusline" && !filled[3].Priced, "The status line's total wasn't shared out: " + String.Join(",", filled.Select(f => f.Cost)));
            Assert(!usage[1].Priced && usage[1].Cost == 0, "Fill changed the scan's own entries.");
            Assert(ReferenceEquals(ClaudeService.Fill(usage, new Dictionary<string, double>()), usage), "Fill copied with nothing to fill.");
            // The card: "~" before the API value, and a tip naming the model.
            var models = new List<string> { "claude-mystery-9" };
            Assert(ClaudeText.Unpriced(models) == "~" && ClaudeText.Unpriced(new List<string>()) == "" && ClaudeText.UnpricedTip(models).StartsWith("Price unknown for claude-mystery-9"), "Unpriced words wrong.");
            var view = new ClaudeView(); view.Unpriced[ClaudePeriod.Today] = models; view.CostSource[ClaudePeriod.Today] = "mixed";
            view.FactsBy[ClaudePeriod.Today] = new ClaudeFacts { Tokens = 1200, Cost = 2.5, Replies = 3 };
            var fact = ClaudeText.Fact(ClaudeFactKinds.Tokens, view, ClaudePeriod.Today, true, true);
            Assert(fact[1].Contains("~$") && fact[2].Contains("Price unknown for claude-mystery-9") && fact[2].Contains("Costs from Claude Code where it sent them"), "The API value doesn't say what it leaves out: " + fact[1]);
            Assert(!ClaudeText.Fact(ClaudeFactKinds.Tokens, view, ClaudePeriod.Today, true, false)[1].Contains("$"), "The API value showed while hidden.");
        });

        check("Claude telemetry reads tool runs, API errors, helpers, modes and the counters (one fixture each)", () => {
            var events = ClaudeOtlp.ParseLogs(Logs(
                Record("tool_decision", "e1", Now, S("tool_name", "Bash"), S("tool_use_id", "toolu_1"), S("decision", "accept"), S("source", "config")),
                Record("tool_result", "e1", Now, S("tool_name", "Bash"), S("tool_use_id", "toolu_1"), S("success", "false"), D("duration_ms", 1500), S("error_type", "timeout")),
                Record("api_error", "e1", Now, S("model", "claude-opus-5-5"), S("error", "Overloaded: the full message is never kept"), I("status_code", 529), I("attempt", 3), I("duration_ms", 900)),
                Record("api_request", "e1", Now, S("model", "claude-haiku-4-5"), I("input_tokens", 10), I("output_tokens", 1), S("agent.name", "Explore"), S("query_source", "subagent"), S("speed", "fast"), S("effort", "high"), S("skill.name", "docs"), S("mcp_server.name", "github"), S("request_id", "h1")),
                Record("subagent_completed", "e1", Now, S("agent_type", "Explore"), I("total_tool_uses", 12), I("duration_ms", 30000)),
                Record("permission_mode_changed", "e1", Now, S("from_mode", "default"), S("to_mode", "plan"))));
            Assert(events.Count == 6, "Not every event was read: " + events.Count);
            Assert(events[0].Tool == "Bash" && events[0].ToolUse == "toolu_1" && !events[0].Denied && !events[0].Failed, "tool_decision parsed wrongly.");
            Assert(events[1].Failed && events[1].DurationMs == 1500 && events[1].ErrorType == "timeout" && events[1].ToolUse == "toolu_1", "tool_result parsed wrongly.");
            Assert(events[2].Status == 529 && events[2].Attempt == 3 && events[2].Usage == null && !events.Any(e => e.ErrorType.Contains("Overloaded")), "api_error parsed wrongly (or kept the message).");
            Assert(events[3].Usage.Sidechain && events[3].Agent == "Explore" && events[3].QuerySource == "subagent" && events[3].Speed == "fast" && events[3].Effort == "high" && events[3].Skill == "docs" && events[3].Mcp == "github", "api_request's details parsed wrongly.");
            Assert(events[4].Agent == "Explore" && events[4].ToolUses == 12 && events[4].DurationMs == 30000, "subagent_completed parsed wrongly.");
            Assert(events[5].Mode == "plan", "permission_mode_changed parsed wrongly.");
            var metrics = ClaudeOtlp.ParseMetrics(Metrics(
                Sum("token.usage", 1, Point("e1", Now, 1000, S("type", "input"), S("model", "claude-opus-5-5"), S("query_source", "main"))),
                Sum("cost.usage", 1, Point("e1", Now, 0.5, S("query_source", "subagent"))),
                Sum("active_time.total", 1, Point("e1", Now, 120, S("type", "user")), Point("e1", Now, 300, S("type", "cli"))),
                Sum("session.count", 1, Point("e1", Now, 1, S("start_type", "resume"))),
                Sum("commit.count", 2, Point("e1", Now, 4)),
                Sum("pull_request.count", 1, Point("e1", Now, 1)),
                Sum("code_edit_tool.decision", 1, Point("e1", Now, 3, S("decision", "accept"), S("tool_name", "Edit")), Point("e1", Now, 1, S("decision", "reject"), S("tool_name", "Write")))));
            var counted = metrics.Where(m => m.Name == "metric").ToList();
            Func<string, string, ClaudeTelemetryEvent> find = (metric, type) => counted.SingleOrDefault(m => m.Metric == metric && m.Type == type);
            Assert(counted.Count == 9 && metrics.Count(m => m.Name == "active") == 1, "The counters weren't all read: " + counted.Count);
            Assert(find("token.usage", "input").Value == 1000 && find("token.usage", "input").QuerySource == "main" && find("cost.usage", "subagent").Value == 0.5 && find("active_time.total", "user").Value == 120 && find("active_time.total", "cli").Value == 300, "Counter values or splits wrong.");
            Assert(find("session.count", "resume") != null && find("commit.count", "").Cumulative && !find("pull_request.count", "").Cumulative && find("code_edit_tool.decision", "accept").Value == 3 && find("code_edit_tool.decision", "reject").Value == 1, "Counter kinds wrong.");
            Assert(find("code_edit_tool.decision", "accept").Series != find("code_edit_tool.decision", "reject").Series, "Two series share a name.");
        });

        check("Claude cars rev past their tool's usual time, a later request ends a call that never finished, and a retry shows", () => {
            var rev = ClaudePace.RevAfter(new double[0]) == ClaudePace.LongTool && ClaudePace.RevAfter(new[] { 1000.0, 3000, 2000 }) == TimeSpan.FromSeconds(4)
                && ClaudePace.RevAfter(new[] { 100.0 }) == TimeSpan.FromSeconds(3) && ClaudePace.RevAfter(new[] { 100000.0 }) == TimeSpan.FromSeconds(60)
                && ClaudePace.RevAfter(new[] { 1000.0, 3000, Double.NaN }) == TimeSpan.FromSeconds(4) && ClaudePace.RevAfter(null) == ClaudePace.LongTool;
            Assert(rev, "RevAfter is wrong.");
            var service = Service(directory, "rev", "rv", "rt");
            DateTime real = DateTime.UtcNow;
            // Five Bash runs of 2 s: Bash revs after 4 s.
            var records = Enumerable.Range(1, 5).Select(i => Record("tool_result", "rv", real.AddMinutes(-1), S("tool_name", "Bash"), S("tool_use_id", "done" + i), S("success", "true"), D("duration_ms", 2000))).ToList();
            records.Add(Record("tool_decision", "rv", real.AddSeconds(-2), S("tool_name", "Bash"), S("tool_use_id", "now1"), S("decision", "accept"), S("source", "config")));
            service.Receive(ClaudeOtlp.ParseLogs(Logs(records.ToArray())));
            Func<string, ClaudeSession> thread = id => service.View(real, null).Threads.Single(t => t.Id == id);
            Assert(thread("rv").Revving == false && thread("rv").Pit == PitState.Work, "A call within its usual time revs.");
            service.Receive(ClaudeOtlp.ParseLogs(Logs(Record("tool_decision", "rv", real.AddSeconds(-10), S("tool_name", "Bash"), S("tool_use_id", "long1"), S("decision", "accept"), S("source", "user_permanent")))));
            Assert(thread("rv").Revving == true && thread("rv").Pit == PitState.Rev, "A call past twice its usual time doesn't rev.");
            // Its result arrives: the call is no longer open.
            service.Receive(ClaudeOtlp.ParseLogs(Logs(Record("tool_result", "rv", real.AddSeconds(-1), S("tool_name", "Bash"), S("tool_use_id", "long1"), S("success", "true"), D("duration_ms", 9000)))));
            Assert(thread("rv").Revving == false, "A finished call still revs.");
            service.Receive(ClaudeOtlp.ParseLogs(Logs(Record("tool_decision", "rv", real.AddSeconds(-30), S("tool_name", "Grep"), S("tool_use_id", "lost1"), S("decision", "accept"), S("source", "config")))));
            Assert(thread("rv").Revving == true, "A long call of a tool with no history doesn't rev after 15 s.");
            // Its result never came, but a request went out after it: it has finished.
            service.Receive(ClaudeOtlp.ParseLogs(Logs(Record("api_request", "rv", real.AddSeconds(-1), S("model", "claude-opus-5-5"), I("input_tokens", 10), I("output_tokens", 1), S("request_id", "after-lost")))));
            Assert(thread("rv").Revving == false && thread("rv").Pit == PitState.Work, "A request after a call didn't end it.");
            // An API error on a working thread: Stuck on the car, with the retry in its tip.
            service.Receive(ClaudeOtlp.ParseLogs(Logs(Record("api_error", "rt", real.AddSeconds(-5), I("status_code", 529), I("attempt", 3)),
                Record("permission_mode_changed", "rt", real.AddSeconds(-50), S("from_mode", "default"), S("to_mode", "acceptEdits")))));
            var rt = thread("rt");
            Assert(rt.Pit == PitState.Stuck && rt.ErrorStatus == 529 && rt.ErrorAttempt == 3 && rt.Mode == "acceptEdits" && rt.Revving == null, "The API error or mode wasn't read onto the thread.");
            string tip = ClaudeText.ThreadTip(rt, ClaudePeriod.Today, real, true, false);
            Assert(tip.Contains("Retrying after an API error · HTTP 529 · attempt 3") && tip.Contains("Mode acceptEdits"), "The tip doesn't say it is retrying: " + tip);
            Assert(ClaudeText.Retrying(new ClaudeSession()) == "", "A thread with no error says it's retrying.");
        });

        check("Claude side-panel facts come from telemetry, the logs and the hooks, and helpers' tokens count apart", () => {
            var service = Service(directory, "facts", "fx");
            DateTime real = DateTime.UtcNow, at = real.AddMinutes(-1);
            var records = new List<string>();
            for (int i = 0; i < 3; i++) records.Add(Record("tool_result", "fx", at, S("tool_name", "Read"), S("tool_use_id", "r" + i), S("success", "true"), D("duration_ms", 100)));
            records.Add(Record("tool_result", "fx", at, S("tool_name", "Bash"), S("tool_use_id", "b1"), S("success", "true"), D("duration_ms", 4000)));
            records.Add(Record("tool_result", "fx", at, S("tool_name", "Bash"), S("tool_use_id", "b2"), S("success", "false"), D("duration_ms", 6000), S("error_type", "exit_code")));
            records.Add(Record("api_error", "fx", at, I("status_code", 529), I("attempt", 1)));
            records.Add(Record("api_error", "fx", real.AddHours(-2), I("status_code", 500), I("attempt", 1)));
            records.Add(Record("subagent_completed", "fx", at, S("agent_type", "Explore"), I("total_tool_uses", 4)));
            records.Add(Record("api_request", "fx", real.AddSeconds(-1), S("model", "claude-opus-5-5"), I("input_tokens", 1), S("request_id", "fx-last")));
            service.Receive(ClaudeOtlp.ParseLogs(Logs(records.ToArray())));
            service.Receive(ClaudeOtlp.ParseMetrics(Metrics(
                Sum("code_edit_tool.decision", 1, Point("fx", at, 3, S("decision", "accept")), Point("fx", at, 1, S("decision", "reject"))),
                Sum("commit.count", 2, Point("fx", at, 2)),
                Sum("pull_request.count", 1, Point("fx", at, 1)),
                Sum("active_time.total", 1, Point("fx", at, 120, S("type", "user")), Point("fx", at, 300, S("type", "cli"))))));
            // The cumulative commit counter says 2 again, then 5: only the 3 new ones are added.
            service.Receive(ClaudeOtlp.ParseMetrics(Metrics(Sum("commit.count", 2, Point("fx", at, 2)))));
            service.Receive(ClaudeOtlp.ParseMetrics(Metrics(Sum("commit.count", 2, Point("fx", at, 5)))));
            var usage = new List<ClaudeUsageEntry> {
                new ClaudeUsageEntry { Session = "fx", At = at, Folder = "C:\\Code\\api", Tokens = 600, Prompt = 1000, CacheRead = 400, Cost = 1, Request = "own" },
                new ClaudeUsageEntry { Session = "fx", At = at, Folder = "C:\\Code\\api", Tokens = 400, Cost = 1, Request = "helper", Sidechain = true } };
            var view = service.View(real, usage);
            var f = view.FactsFor(ClaudePeriod.FiveHour);
            Assert(f.SlowTool == "Bash" && f.SlowMs == 5000 && f.Runs == 5 && f.RunFailures == 1 && !f.FailuresFromHooks && Near(f.FailureRate, 0.2), "Tool runs wrong: " + f.SlowTool + " " + f.Runs + "/" + f.RunFailures);
            Assert(f.Errors == 1 && Near(f.HelperShare, 0.4) && f.Helpers == 1, "Errors or helpers wrong: " + f.Errors + " " + f.HelperShare);
            Assert(f.EditsAccepted == 3 && f.EditsRejected == 1 && f.Commits == 5 && f.PullRequests == 1 && f.ActiveUser == 120 && f.ActiveCli == 300 && f.Metrics, "Counters wrong: commits " + f.Commits);
            Assert(Near(f.CacheHit, 0.4) && view.Facts == view.FactsFor(ClaudePeriod.Today), "Cache hits or today's facts wrong.");
            var slow = ClaudeText.Fact(ClaudeFactKinds.Slowest, view, ClaudePeriod.FiveHour, true, false);
            var fail = ClaudeText.Fact(ClaudeFactKinds.Failures, view, ClaudePeriod.FiveHour, true, false);
            var helpers = ClaudeText.Fact(ClaudeFactKinds.Helpers, view, ClaudePeriod.FiveHour, true, false);
            Assert(slow[0] == "5 s" && slow[1] == "SLOWEST · BASH" && fail[0] == "20%" && fail[1] == "1 OF 5 FAILED" && helpers[0] == "40%" && helpers[2].Contains("1 finished"), "Fact words wrong: " + slow[0] + " " + fail[1] + " " + helpers[0]);
            Assert(ClaudeText.Fact(ClaudeFactKinds.Commits, view, ClaudePeriod.FiveHour, true, false)[1] == "COMMITS · 1 PRS" && ClaudeText.Fact(ClaudeFactKinds.Active, view, ClaudePeriod.FiveHour, true, false)[0] == "5 min", "Counter fact words wrong.");
            Assert(ClaudeText.Fact(ClaudeFactKinds.Errors, new ClaudeView(), ClaudePeriod.Today, true, false)[0] == "—" && ClaudeText.Fact(ClaudeFactKinds.Edits, new ClaudeView(), ClaudePeriod.Today, true, false)[2] == ClaudeText.NeedsTelemetry, "Facts without telemetry don't say so.");
            var fx = view.Threads.Single(x => x.Id == "fx");
            Assert(fx.HelperTokensFor(ClaudePeriod.FiveHour) == 400 && ClaudeText.ThreadTip(fx, ClaudePeriod.FiveHour, real, true, true).Contains("Own 600 · helpers 400 tokens"), "Helpers' tokens aren't apart on the thread.");
            // No telemetry runs: the hooks' failure counts over the logs' tool calls.
            var hooks = new ClaudeService(Fresh(directory, "facts-hooks-bridge"), Fresh(directory, "facts-hooks-config"), appThreads: new ClaudeAppThreads(() => new string[0]));
            Assert(hooks.View(real, null).FactsFor(ClaudePeriod.FiveHour).Runs == 0 && !hooks.View(real, null).FactsFor(ClaudePeriod.FiveHour).FailuresFromHooks, "Runs were invented without telemetry or logs.");
        });

        check("Claude logs keep helpers' replies apart (isSidechain, agentId or a subagents log)", () => {
            var seen = new Dictionary<string, ClaudeUsageEntry>();
            string text = LogLine("a1", Now, "sc", "claude-opus-5-5", 10) + "\n" + LogLine("a2", Now, "sc", "claude-opus-5-5", 10, ",\"isSidechain\":true") + "\n" + LogLine("a3", Now, "sc", "claude-opus-5-5", 10, ",\"agentId\":\"a4f2\"") + "\n";
            var entries = ClaudeUsage.Parse(text, seen, "sc");
            Assert(entries.Count == 3 && !entries[0].Sidechain && entries[1].Sidechain && entries[2].Sidechain, "isSidechain or agentId wasn't read.");
            Assert(ClaudeUsage.Parse(LogLine("h1", Now, "sc", "claude-opus-5-5", 10), seen, "sc", helper: true).Single().Sidechain, "A subagent's log isn't a helper's.");
            // The same reply seen again in a helper's log: still counted once, now as a helper's.
            Assert(ClaudeUsage.Parse(LogLine("a1", Now, "sc", "claude-opus-5-5", 10), seen, "sc", helper: true).Count == 0 && seen["a1"].Sidechain, "A repeated reply counted twice, or didn't learn it was a helper's.");
        });

        check("Claude's 5-hour ETA: when the tank fills at the last half hour's pace, only before the reset", () => {
            Assert(ClaudePace.Eta(40, 1000, 10) == 150 && Double.IsNaN(ClaudePace.Eta(0, 1000, 10)) && Double.IsNaN(ClaudePace.Eta(100, 1000, 10)) && Double.IsNaN(ClaudePace.Eta(40, 1000, 0)) && Double.IsNaN(ClaudePace.Eta(40, 0, 10)) && Double.IsNaN(ClaudePace.Eta(Double.NaN, 1000, 10)), "Eta maths wrong.");
            Assert(ClaudeText.Span(TimeSpan.FromMinutes(150)) == "2 h 30" && ClaudeText.Span(TimeSpan.FromMinutes(120)) == "2 h" && ClaudeText.Span(TimeSpan.FromMinutes(25)) == "25 min" && ClaudeText.Span(TimeSpan.FromSeconds(40)) == "40 s", "Span words wrong.");
            Assert(ClaudeText.Eta(150) == "full in 2 h 30 at this pace" && ClaudeText.Eta(Double.NaN) == "" && ClaudeText.PaceLine(Double.NaN, 150).Contains("full in 2 h 30"), "ETA words wrong.");
            DateTime real = DateTime.UtcNow;
            // 40% used; 700 tokens earlier in the window and 300 over the last half hour (10 a minute): 150 minutes.
            var usage = new List<ClaudeUsageEntry> { new ClaudeUsageEntry { Session = "x", At = real.AddHours(-1), Tokens = 700 } };
            for (int i = 0; i < 3; i++) usage.Add(new ClaudeUsageEntry { Session = "x", At = real.AddMinutes(-5 - i * 5), Tokens = 100 });
            var service = Service(directory, "eta");
            service.UseOnlineLimits(new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = 40, ResetsAt = real.AddHours(3) }, UpdatedAt = real, Source = "online" });
            Assert(Math.Abs(service.View(real, usage).LimitEtaMinutes - 150) < 0.01, "The ETA isn't worked out from the window.");
            // It resets in 2 hours, before the tank would fill: no ETA.
            service.UseOnlineLimits(new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = 40, ResetsAt = real.AddHours(2) }, UpdatedAt = real, Source = "online" });
            Assert(Double.IsNaN(service.View(real, usage).LimitEtaMinutes), "An ETA after the reset was shown.");
        });

        check("Claude's first scan reads big logs from the tail in chunks over several scans and stops at the week's edge", () => {
            string config = Fresh(directory, "chunks");
            string folder = Path.Combine(config, "projects", "C--Code-api"); Directory.CreateDirectory(folder);
            string log = Path.Combine(folder, "big.jsonl");
            DateTime real = DateTime.UtcNow, oldest = real - ClaudeUsage.Kept;
            // About 20 MB over 20 days, oldest first: a little under half of it is inside the week kept.
            string pad = new string('x', 400);
            int count = 0, inside = 0;
            using (var writer = new StreamWriter(log, false, new UTF8Encoding(false)))
            {
                writer.Write("{\"type\":\"user\",\"sessionId\":\"big\",\"cwd\":\"C:\\\\Code\\\\api\",\"timestamp\":\"" + real.AddDays(-20).ToString("o", CultureInfo.InvariantCulture) + "\",\"message\":{\"role\":\"user\",\"content\":\"Read the big log\"}}\n");
                const int total = 40000;
                for (int i = 0; i < total; i++)
                {
                    DateTime at = real.AddDays(-20).AddSeconds(20.0 * 24 * 3600 * i / total);
                    if (at >= oldest) inside++;
                    writer.Write(LogLine("big-" + i, at, "big", "claude-opus-5-5", 1, ",\"pad\":\"" + pad + "\"") + "\n");
                    count++;
                }
            }
            long size = new FileInfo(log).Length;
            Assert(size > 18L * 1024 * 1024, "The fixture is too small: " + size);
            var usage = new ClaudeUsage(config) { ScanBudget = 3L * ClaudeUsage.ChunkBytes };
            usage.Scan(real);
            int first = usage.Entries.Count, scans = 1;
            Assert(usage.Behind && first > 0 && first < inside && usage.Entries.Min(e => e.At) > real.AddDays(-4), "The first scan didn't start from the newest lines, or read it all at once.");
            Assert(usage.Folders["big"] == "C:\\Code\\api" && usage.Prompts.ContainsKey("big"), "The head wasn't read for the folder and first request.");
            while (usage.Behind && scans < 50) { usage.Scan(real); scans++; }
            var entries = usage.Entries;
            Assert(!usage.Behind && entries.Count == inside && entries.All(e => e.At >= oldest) && entries.Select(e => e.Request).Distinct().Count() == inside, "Chunks lost or repeated lines: " + entries.Count + " of " + inside);
            // It stopped at the week's edge rather than reading the 20 days.
            long budgetRead = (long)scans * usage.ScanBudget;
            Assert(scans > 2 && budgetRead < size + 2L * ClaudeUsage.ChunkBytes, "Took " + scans + " scans for " + size + " bytes.");
            // New lines after that are read from the end, once.
            File.AppendAllText(log, LogLine("big-new", real, "big", "claude-opus-5-5", 1) + "\n");
            usage.Scan(real);
            Assert(usage.Entries.Count == inside + 1 && !usage.Behind, "A line added after the chunks wasn't read once.");
        });

        check("Claude side-panel fact choices are cleaned and kept in settings", () => {
            Assert(ClaudeFactKinds.Clean(null).SequenceEqual(ClaudeFactKinds.Default), "No choice isn't the default.");
            Assert(ClaudeFactKinds.Clean(new[] { "slowest", "bogus", "slowest", "tokens", "edits", "commits", "active" }).SequenceEqual(new[] { "slowest", "tokens", "edits", "commits" }), "Choices weren't cleaned.");
            Assert(ClaudeFactKinds.Clean(new[] { "errors" }).SequenceEqual(new[] { "errors", "tokens", "cache", "tools" }), "Missing choices weren't filled from the default.");
            Assert(ClaudeFactKinds.All.All(k => ClaudeFactKinds.Name(k).Length > 0) && ClaudeFactKinds.All.Distinct().Count() == 11, "A fact has no name.");
            string path = Path.Combine(Fresh(directory, "facts-settings"), "settings.xml");
            SettingsStore.Save(path, new AppSettings { ClaudeFacts = new[] { "failures", "helpers", "errors", "active" } });
            string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.ClaudeFacts.SequenceEqual(new[] { "failures", "helpers", "errors", "active" }) && loaded.Copy().ClaudeFacts.SequenceEqual(loaded.ClaudeFacts) && !ReferenceEquals(loaded.Copy().ClaudeFacts, loaded.ClaudeFacts), "Fact choices weren't kept or copied.");
            File.WriteAllText(path, File.ReadAllText(path).Replace("<Fact>helpers</Fact>", "<Fact>nonsense</Fact>"));
            Assert(SettingsStore.Load(path, out warning).ClaudeFacts.SequenceEqual(new[] { "failures", "errors", "active", "tokens" }), "A broken choice survived loading.");
        });
    }
}
