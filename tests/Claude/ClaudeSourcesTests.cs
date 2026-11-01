using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WidgetScreen;

// The Claude watcher's sources since decision 015: Claude Code's own session list, its OpenTelemetry export on
// loopback, the widget's own Claude sign-in and plan check, the env merge, prices, dedupe and the project filter.
// Everything runs in temporary folders with fixtures; Claude Code's real folder, sign-in and Anthropic's servers are
// never touched (the sign-in and usage requests go to a fake HTTP handler).
internal static class ClaudeSourcesTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static readonly DateTime Now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-checks", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static long Ms(DateTime time) { return ClaudeBridge.Epoch(time); }
    private static string Nanos(DateTime time) { return (ClaudeBridge.Epoch(time) * 1000000L).ToString(System.Globalization.CultureInfo.InvariantCulture); }
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, string, HttpResponseMessage> Send;
        public readonly List<string> Hosts = new List<string>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Hosts.Add(request.RequestUri.Host);
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            return Send(request, body);
        }
    }
    private static HttpResponseMessage Reply(HttpStatusCode status, string body) { return new HttpResponseMessage(status) { Content = new StringContent(body ?? "") }; }

    // Claude Code's OTLP/HTTP JSON for one api_request event and one user_prompt, as its exporter sends them.
    public static string LogsFixture(string session, string request, DateTime at)
    {
        return "{\"resourceLogs\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"claude-code\"}},{\"key\":\"vcs.repository.name\",\"value\":{\"stringValue\":\"widget-screen\"}}]},"
            + "\"scopeLogs\":[{\"scope\":{\"name\":\"com.anthropic.claude_code.events\"},\"logRecords\":["
            + "{\"timeUnixNano\":\"" + Nanos(at.AddSeconds(-5)) + "\",\"body\":{\"stringValue\":\"claude_code.user_prompt\"},\"attributes\":[{\"key\":\"event.name\",\"value\":{\"stringValue\":\"user_prompt\"}},{\"key\":\"session.id\",\"value\":{\"stringValue\":\"" + session + "\"}},{\"key\":\"prompt_length\",\"value\":{\"intValue\":\"12\"}}]},"
            + "{\"timeUnixNano\":\"" + Nanos(at) + "\",\"body\":{\"stringValue\":\"claude_code.api_request\"},\"attributes\":[{\"key\":\"event.name\",\"value\":{\"stringValue\":\"api_request\"}},{\"key\":\"session.id\",\"value\":{\"stringValue\":\"" + session + "\"}},"
            + "{\"key\":\"model\",\"value\":{\"stringValue\":\"claude-opus-5-5\"}},{\"key\":\"input_tokens\",\"value\":{\"intValue\":\"1000\"}},{\"key\":\"output_tokens\",\"value\":{\"intValue\":\"200\"}},"
            + "{\"key\":\"cache_read_tokens\",\"value\":{\"intValue\":\"5000\"}},{\"key\":\"cache_creation_tokens\",\"value\":{\"intValue\":\"300\"}},{\"key\":\"cost_usd\",\"value\":{\"doubleValue\":0.0123}},"
            + "{\"key\":\"request_id\",\"value\":{\"stringValue\":\"" + request + "\"}}]}]}]}]}";
    }
    public static string MetricsFixture(string session, DateTime at)
    {
        return "{\"resourceMetrics\":[{\"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"claude-code\"}}]},\"scopeMetrics\":[{\"metrics\":["
            + "{\"name\":\"claude_code.token.usage\",\"unit\":\"tokens\",\"sum\":{\"aggregationTemporality\":1,\"isMonotonic\":true,\"dataPoints\":["
            + "{\"attributes\":[{\"key\":\"session.id\",\"value\":{\"stringValue\":\"" + session + "\"}},{\"key\":\"type\",\"value\":{\"stringValue\":\"input\"}}],\"timeUnixNano\":\"" + Nanos(at) + "\",\"asDouble\":1000},"
            + "{\"attributes\":[{\"key\":\"session.id\",\"value\":{\"stringValue\":\"" + session + "\"}},{\"key\":\"type\",\"value\":{\"stringValue\":\"output\"}}],\"timeUnixNano\":\"" + Nanos(at.AddSeconds(-30)) + "\",\"asDouble\":200}]}},"
            + "{\"name\":\"other.metric\",\"sum\":{\"dataPoints\":[{\"attributes\":[{\"key\":\"session.id\",\"value\":{\"stringValue\":\"not-claude\"}}]}]}}]}]}]}";
    }
    private static string LogLine(string request, DateTime at, string cwd, string session, int input)
    {
        return "{\"type\":\"assistant\",\"sessionId\":\"" + session + "\",\"cwd\":\"" + cwd.Replace("\\", "\\\\") + "\",\"timestamp\":\"" + at.ToString("o") + "\",\"requestId\":\"" + request + "\",\"message\":{\"id\":\"msg-" + request + "\",\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":200,\"cache_creation_input_tokens\":300,\"cache_read_input_tokens\":5000}}}";
    }
    private static string Registry(string session, int pid, string cwd, string status, DateTime changed, string name = "folder-1", string source = "derived", string waiting = null)
    {
        return "{\"pid\":" + pid + ",\"sessionId\":\"" + session + "\",\"cwd\":\"" + cwd.Replace("\\", "\\\\") + "\",\"startedAt\":" + Ms(changed.AddMinutes(-30)) + ",\"kind\":\"interactive\",\"entrypoint\":\"claude-desktop\",\"name\":\"" + name + "\",\"nameSource\":\"" + source + "\",\"status\":\"" + status + "\"" + (waiting == null ? "" : ",\"waitingFor\":\"" + waiting + "\"") + ",\"updatedAt\":" + Ms(changed) + ",\"statusUpdatedAt\":" + Ms(changed) + "}";
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude pit wall telemetry: tool decisions, lines in both temporalities, cache counts and the facts", () => {
            Func<string, string, string, string> record = (name, session, extra) => "{\"timeUnixNano\":\"" + Nanos(Now.AddMinutes(-1)) + "\",\"attributes\":[{\"key\":\"event.name\",\"value\":{\"stringValue\":\"" + name + "\"}},{\"key\":\"session.id\",\"value\":{\"stringValue\":\"" + session + "\"}}" + extra + "]}";
            Func<string, string, string> attr = (key, value) => ",{\"key\":\"" + key + "\",\"value\":{\"stringValue\":\"" + value + "\"}}";
            string logs = "{\"resourceLogs\":[{\"scopeLogs\":[{\"logRecords\":[" + record("tool_decision", "p1", attr("decision", "accept") + attr("source", "user_temporary")) + ","
                + record("tool_decision", "p1", attr("decision", "reject") + attr("source", "user_reject")) + "," + record("tool_decision", "p1", attr("decision", "accept") + attr("source", "config")) + ","
                + record("tool_result", "p1", attr("success", "true")) + "," + record("api_error", "p1", attr("status_code", "429")) + "]}]}]}";
            var events = ClaudeOtlp.ParseLogs(logs);
            Assert(events.Count == 5 && events[0].Asked && !events[0].Denied && events[1].Asked && events[1].Denied && !events[2].Asked && events[4].Name == "api_error", "Tool decisions parsed wrongly.");
            var usage = ClaudeOtlp.ParseLogs(LogsFixture("p1", "req_p", Now))[1].Usage;
            Assert(usage.Prompt == 6300 && usage.CacheRead == 5000, "A reply's cache counts parsed wrongly.");
            var parsed = ClaudeUsage.Parse(LogLine("req_l", Now, "C:\\Code\\pit", "p1", 700) + "\n", new Dictionary<string, ClaudeUsageEntry>(), "p1");
            Assert(parsed.Count == 1 && parsed[0].Prompt == 6000 && parsed[0].CacheRead == 5000, "A log line's cache counts parsed wrongly.");
            // lines_of_code.count: a delta series adds up; a cumulative one counts only what it grew by.
            Func<int, string, double, string> lines = (temporality, type, value) => "{\"resourceMetrics\":[{\"scopeMetrics\":[{\"metrics\":[{\"name\":\"claude_code.lines_of_code.count\",\"sum\":{\"aggregationTemporality\":" + temporality
                + ",\"dataPoints\":[{\"attributes\":[{\"key\":\"session.id\",\"value\":{\"stringValue\":\"p1\"}},{\"key\":\"type\",\"value\":{\"stringValue\":\"" + type + "\"}}],\"startTimeUnixNano\":\"" + Nanos(Now.AddHours(-1)) + "\",\"timeUnixNano\":\"" + Nanos(Now.AddMinutes(-1)) + "\",\"asInt\":\"" + value + "\"}]}}]}]}]}";
            var metric = ClaudeOtlp.ParseMetrics(lines(2, "added", 40));
            Assert(metric.Count == 2 && metric.Any(e => e.Name == "active") && metric.Any(e => e.Name == "lines" && e.Added == 40 && e.Cumulative), "Lines metrics parsed wrongly.");
            using (var service = new ClaudeService(Fresh(directory, "bridge-pit"), Fresh(directory, "config-pit")))
            {
                service.Receive(events);
                service.Receive(ClaudeOtlp.ParseMetrics(lines(2, "added", 40)));
                service.Receive(ClaudeOtlp.ParseMetrics(lines(2, "added", 55)));
                service.Receive(ClaudeOtlp.ParseMetrics(lines(1, "removed", 7)));
                service.Receive(ClaudeOtlp.ParseMetrics(lines(1, "removed", 3)));
                var kept = service.LinesEntries;
                Assert(kept.Sum(l => l.Added) == 55 && kept.Sum(l => l.Removed) == 10, "Lines added up wrongly: +" + kept.Sum(l => l.Added) + " −" + kept.Sum(l => l.Removed));
                Assert(service.ToolEntries.Count(t => t.Result) == 1 && service.ToolEntries.Count(t => !t.Result && t.Asked) == 2 && service.ToolEntries.Count(t => t.Denied) == 1, "Tool calls kept wrongly.");
                service.Receive(ClaudeOtlp.ParseLogs(LogsFixture("p1", "req_p", Now.AddMinutes(-1))));
                var view = service.View(Now, service.Usage.Entries);
                var thread = view.Threads.FirstOrDefault(t => t.Id == "p1");
                Assert(thread != null && thread.LinesFor(ClaudePeriod.Today)[0] == 55 && thread.LinesFor(ClaudePeriod.Today)[1] == 10 && thread.MixFor(ClaudePeriod.Today)[0] == 1 && thread.Car == PitCar.GrandTourer, "The thread's lines, mix or car are wrong.");
                var f = view.Facts;
                Assert(f.Telemetry && f.Tools == 1 && f.Asked == 2 && f.Denied == 1 && f.Added == 55 && f.Removed == 10 && f.LinesThreads == 1 && f.Prompt == 6300 && f.CacheRead == 5000 && Math.Abs(f.CacheHit - 5000.0 / 6300) < 1e-9, "Today's facts wrong.");
                Assert(view.Burn.Length == 60 && view.Burn.Sum() == 6500 && view.Rate == 3250 && view.Scale == ClaudePace.MinScale && view.Lanes.Count == 1 && view.Lanes[0].Bins.Sum() == 6500, "Burn or lanes wrong.");
            }
        });

        check("Claude's PreCompact hook puts the car on the jack until the compaction ends", () => {
            string bridge = Fresh(directory, "bridge-compact");
            Action<string, string> hook = (name, extra) => ClaudeBridge.Hook("{\"session_id\":\"c1\",\"cwd\":\"C:/Code/api\",\"hook_event_name\":\"" + name + "\"" + extra + "}", bridge, null, Now);
            hook("UserPromptSubmit", "");
            hook("PreCompact", ",\"trigger\":\"auto\"");
            var s = ClaudeBridge.Read(ClaudeBridge.SessionPath(bridge, "c1"));
            Assert(s.Compacting && s.State == ClaudeState.Working, "PreCompact didn't mark the session compacting.");
            hook("SessionStart", ",\"source\":\"compact\"");
            s = ClaudeBridge.Read(ClaudeBridge.SessionPath(bridge, "c1"));
            Assert(!s.Compacting && s.State == ClaudeState.Working, "The compaction didn't end, or the session stopped working.");
            Assert(ClaudeConnector.Events.Any(e => e[0] == "PreCompact"), "Connect doesn't add the PreCompact hook.");
        });

        check("Claude prices follow Claude Code's catalog, and unknown models cost nothing", () => {
            Assert(Math.Abs(ClaudePrices.Cost("claude-opus-5-5", 1000000, 0, 0, 0, 0) - 4) < 1e-9 && Math.Abs(ClaudePrices.Cost("claude-opus-5-5-20261001", 0, 1000000, 0, 0, 0) - 20) < 1e-9, "Opus 5.5 priced wrongly.");
            Assert(Math.Abs(ClaudePrices.Cost("claude-opus-5", 1000000, 0, 0, 0, 1000000) - 5.5) < 1e-9 && Math.Abs(ClaudePrices.Cost("claude-opus-5-5[1m]", 0, 0, 1000000, 1000000, 0) - 13) < 1e-9, "Opus 5 or cache writes priced wrongly.");
            Assert(Math.Abs(ClaudePrices.Cost("us.anthropic.claude-sonnet-4-5-20250929-v1:0", 1000000, 0, 0, 0, 0) - 3) < 1e-9 && ClaudePrices.Cost("claude-opus-5-1", 1000000, 0, 0, 0, 0) == 0 && ClaudePrices.Cost("gpt-x", 5, 5, 5, 5, 5) == 0, "Model names matched wrongly.");
            Assert(Math.Abs(ClaudePrices.Cost("claude-haiku-5-5", 200000, 0, 0, 0, 0) - 0.1) < 1e-9 && Math.Abs(ClaudePrices.Cost("claude-haiku-5-5", 50000, 0, 0, 0, 0) - 0.005) < 1e-9, "Haiku 5.5's long-prompt price wasn't used.");
            Assert(ClaudeUsage.Money(0.4219) == "≈ $0.42" && ClaudeUsage.Money(12.6) == "≈ $13" && ClaudeUsage.Money(1234) == "≈ $1.2k" && ClaudeUsage.Money(0) == "≈ $0", "Cost words wrong.");
            Assert(ClaudeService.ModelName("claude-opus-5-5-20261001") == "Opus 5.5" && ClaudeService.ModelName("claude-sonnet-5") == "Sonnet 5" && ClaudeService.ModelName("claude-haiku-4-5-20251001") == "Haiku 4.5" && ClaudeService.ModelName("custom") == "custom", "Model names shown wrongly.");
        });

        check("Claude OTLP logs and metrics parse into events and usage", () => {
            var events = ClaudeOtlp.ParseLogs(LogsFixture("s-otel", "req_1", Now));
            Assert(events.Count == 2 && events[0].Name == "user_prompt" && events[0].Session == "s-otel" && events[0].At == Now.AddSeconds(-5) && events[0].Usage == null && events[0].Service == "claude-code" && events[0].Repository == "widget-screen", "The prompt event parsed wrongly.");
            var usage = events[1].Usage;
            Assert(events[1].Name == "api_request" && usage != null && usage.Tokens == 6500 && Math.Abs(usage.Cost - 0.0123) < 1e-9 && usage.Request == "req_1" && usage.Model == "claude-opus-5-5" && usage.FromTelemetry && usage.At == Now, "The api_request event's usage parsed wrongly.");
            var metrics = ClaudeOtlp.ParseMetrics(MetricsFixture("s-otel", Now));
            Assert(metrics.Count == 1 && metrics[0].Name == "active" && metrics[0].Session == "s-otel" && metrics[0].At == Now, "Metrics didn't mark the session active once, at its newest point.");
            Assert(ClaudeOtlp.ParseLogs("not json").Count == 0 && ClaudeOtlp.ParseLogs("{\"resourceLogs\":[{\"scopeLogs\":[{\"logRecords\":[{\"body\":{\"stringValue\":\"claude_code.api_request\"}}]}]}]}").Count == 0 && ClaudeOtlp.ParseMetrics("[]").Count == 0, "Broken or session-less telemetry was used.");
        });

        check("Claude telemetry listens on loopback only and accepts only OTLP posts", () => {
            var received = new List<ClaudeTelemetryEvent>();
            using (var telemetry = new ClaudeTelemetry(0, list => { lock (received) received.AddRange(list); }))
            {
                Assert(telemetry.Start() && telemetry.Port > 0, "The receiver didn't start.");
                Func<string, string, string, string> send = (method, path, body) => {
                    using (var client = new TcpClient()) {
                        client.Connect(IPAddress.Loopback, telemetry.Port);
                        var stream = client.GetStream();
                        byte[] data = Encoding.UTF8.GetBytes(body ?? "");
                        byte[] head = Encoding.ASCII.GetBytes(method + " " + path + " HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: application/json\r\nContent-Length: " + data.Length + "\r\n\r\n");
                        stream.Write(head, 0, head.Length); stream.Write(data, 0, data.Length);
                        using (var reader = new StreamReader(stream)) return reader.ReadLine() ?? "";
                    }
                };
                Assert(send("POST", "/v1/logs", LogsFixture("s-wire", "req_w", Now)).Contains(" 200 "), "A logs export wasn't accepted.");
                Assert(send("POST", "/v1/metrics", MetricsFixture("s-wire", Now)).Contains(" 200 "), "A metrics export wasn't accepted.");
                Assert(send("GET", "/v1/logs", "").Contains(" 405 ") && send("POST", "/state", "{}").Contains(" 404 ") && send("POST", "/v1/traces", "{}").Contains(" 404 "), "Other requests weren't refused.");
                using (var client = new TcpClient())
                {
                    client.Connect(IPAddress.Loopback, telemetry.Port);
                    var stream = client.GetStream();
                    byte[] head = Encoding.ASCII.GetBytes("POST /v1/logs HTTP/1.1\r\nContent-Type: application/json\r\nContent-Length: " + (ClaudeTelemetry.MaxBody + 1) + "\r\n\r\n");
                    stream.Write(head, 0, head.Length);
                    using (var reader = new StreamReader(stream)) Assert((reader.ReadLine() ?? "").Contains(" 413 "), "An oversized body wasn't refused.");
                }
                for (int i = 0; i < 50 && received.Count < 3; i++) Thread.Sleep(20);
                lock (received) Assert(received.Count == 3 && received.Any(e => e.Usage != null && e.Usage.Request == "req_w"), "Received telemetry didn't reach the dashboard.");
                // Bound to 127.0.0.1: another address on this PC can't reach it.
                var other = Dns.GetHostAddresses(Dns.GetHostName()).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                if (other != null)
                {
                    bool refused = false;
                    using (var client = new TcpClient()) { try { client.Connect(other, telemetry.Port); } catch (SocketException) { refused = true; } }
                    Assert(refused, "The receiver answered on " + other + ".");
                }
            }
            Assert(ClaudeTelemetry.Loopback(new IPEndPoint(IPAddress.Loopback, 1)) && ClaudeTelemetry.Loopback(new IPEndPoint(IPAddress.IPv6Loopback, 1)) && ClaudeTelemetry.Loopback(new IPEndPoint(IPAddress.Parse("::ffff:127.0.0.1"), 1)), "Loopback addresses were refused.");
            Assert(!ClaudeTelemetry.Loopback(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 1)) && !ClaudeTelemetry.Loopback(new IPEndPoint(IPAddress.Parse("::ffff:10.0.0.2"), 1)) && !ClaudeTelemetry.Loopback(null), "A network address counted as this PC.");
            Assert(ClaudeConnector.Telemetry(ClaudeTelemetry.DefaultPort).Any(t => t[0] == "OTEL_EXPORTER_OTLP_ENDPOINT" && t[1] == "http://127.0.0.1:43832") && ClaudeTelemetry.DefaultPort != 43830 && ClaudeTelemetry.DefaultPort != 43831, "Telemetry isn't on its own loopback port.");
        });

        check("Claude counts a reply once whether telemetry or the log saw it first", () => {
            string config = Fresh(directory, "config-dedupe");
            string folder = Path.Combine(config, "projects", "C--Code-api"); Directory.CreateDirectory(folder);
            var usage = new ClaudeUsage(config);
            // Telemetry first, then the log line for the same reply.
            foreach (var e in ClaudeOtlp.ParseLogs(LogsFixture("s1", "req_a", Now.AddMinutes(-10)))) if (e.Usage != null) usage.Add(e.Usage, Now);
            File.WriteAllText(Path.Combine(folder, "s1.jsonl"), LogLine("req_a", Now.AddMinutes(-10), "C:\\Code\\api", "s1", 1000) + "\n" + LogLine("req_b", Now.AddMinutes(-5), "C:\\Code\\api", "s1", 1000) + "\n");
            usage.Scan(Now);
            var entries = usage.Entries;
            Assert(entries.Count == 2 && entries.Sum(e => e.Tokens) == 13000, "A reply was counted twice or lost: " + entries.Count);
            Assert(Math.Abs(entries.Single(e => e.Request == "req_a").Cost - 0.0123) < 1e-9 && entries.Single(e => e.Request == "req_b").Cost > 0, "Telemetry's cost wasn't kept, or the log's estimate is missing.");
            // The log first, then telemetry for the same reply: telemetry's cost replaces the estimate.
            foreach (var e in ClaudeOtlp.ParseLogs(LogsFixture("s1", "req_b", Now.AddMinutes(-5)))) if (e.Usage != null) usage.Add(e.Usage, Now);
            entries = usage.Entries;
            Assert(entries.Count == 2 && Math.Abs(entries.Single(e => e.Request == "req_b").Cost - 0.0123) < 1e-9 && entries.Single(e => e.Request == "req_b").FromTelemetry, "A late telemetry reply was counted again.");
            Assert(usage.Folders["s1"] == "C:\\Code\\api", "The session's folder wasn't learned from its log.");
        });

        check("Claude Code's session list finds sessions without hooks and merges by newest news", () => {
            string config = Fresh(directory, "config-registry"), bridge = Fresh(directory, "bridge-registry");
            string sessions = Path.Combine(config, "sessions"); Directory.CreateDirectory(sessions);
            File.WriteAllText(Path.Combine(sessions, "101.json"), Registry("only-listed", 101, "C:\\Code\\desk", "busy", Now.AddMinutes(-1), "Fix it", "user"));
            File.WriteAllText(Path.Combine(sessions, "102.json"), Registry("hooked", 102, "C:\\Code\\api", "busy", Now.AddMinutes(-3)));
            File.WriteAllText(Path.Combine(sessions, "103.json"), Registry("idle-after", 103, "C:\\Code\\api", "idle", Now.AddMinutes(-1)));
            File.WriteAllText(Path.Combine(sessions, "104.json"), Registry("waiting", 104, "C:\\Code\\notes", "waiting", Now.AddMinutes(-2), waiting: "input needed"));
            File.WriteAllText(Path.Combine(sessions, "105.json"), Registry("gone", 105, "C:\\Code\\old", "busy", Now.AddMinutes(-2)));
            File.WriteAllText(Path.Combine(sessions, "106.json"), "{\"pid\":106,\"spare\":true}");
            File.WriteAllText(Path.Combine(sessions, "107.json"), "{ broken");
            Action<string, ClaudeState, DateTime> hook = (id, state, at) => ClaudeBridge.WriteAtomic(ClaudeBridge.SessionPath(bridge, id), ClaudeJson.Compact(new ClaudeSessionFile { Id = id, State = state, Project = "C:\\Code\\api", Since = Ms(at), LastEvent = Ms(at) }.ToJson()));
            hook("hooked", ClaudeState.NeedsYou, Now.AddMinutes(-2));   // a permission after the list said busy
            hook("idle-after", ClaudeState.Working, Now.AddMinutes(-4)); // the list went idle after the hook said working
            var service = new ClaudeService(bridge, config) { RegistryAlive = pid => pid != 105 };
            service.ReadSessions(Now, false);
            var view = service.View(Now, new List<ClaudeUsageEntry>());
            Func<string, ClaudeSession> get = id => view.Sessions.FirstOrDefault(s => s.Id == id);
            Assert(get("only-listed") != null && get("only-listed").State == ClaudeState.Working && get("only-listed").Title == "Fix it" && get("only-listed").ProjectName == "desk" && get("only-listed").Where == "Desktop", "A session only Claude Code's list knows wasn't shown as working.");
            Assert(get("hooked").State == ClaudeState.NeedsYou, "A newer permission hook lost to an older list entry.");
            Assert(get("idle-after").State == ClaudeState.Done, "A newer idle entry didn't finish the session.");
            Assert(get("waiting").State == ClaudeState.NeedsYou && get("waiting").Message == "Has a question for you", "A waiting session didn't need you.");
            Assert(get("gone") == null && view.Sessions.Count == 4, "A closed or spare session showed.");
            var parsed = ClaudeRegistry.Parse(Registry("x", 9, "?", "idle", Now));
            Assert(parsed.Folder == "" && !parsed.NamedByUser && ClaudeRegistry.Parse("{\"pid\":3}") == null, "Registry entries parsed wrongly.");
            // A hook for a permission dialog (the desktop app's Code tab sends no Notification for it).
            ClaudeBridge.Hook("{\"session_id\":\"perm\",\"cwd\":\"C:/Code/api\",\"hook_event_name\":\"PermissionRequest\",\"tool_name\":\"Bash\"}", bridge, null, Now);
            var permission = ClaudeBridge.Read(ClaudeBridge.SessionPath(bridge, "perm"));
            Assert(permission.State == ClaudeState.NeedsYou && ClaudeText.Ask(permission.Message) == "Wants to use Bash", "PermissionRequest didn't mean Needs you.");
        });

        check("Claude keeps a session under the folder it started in after it moves into a worktree", () => {
            string config = Fresh(directory, "config-moved"), bridge = Fresh(directory, "bridge-moved");
            string sessions = Path.Combine(config, "sessions"); Directory.CreateDirectory(sessions);
            string logs = Path.Combine(config, "projects", "C--Code-app"); Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(sessions, "401.json"), Registry("moved", 401, "C:\\Code\\app\\.worktrees\\fix", "busy", Now.AddMinutes(-1)));
            ClaudeBridge.WriteAtomic(ClaudeBridge.SessionPath(bridge, "moved"), ClaudeJson.Compact(new ClaudeSessionFile { Id = "moved", State = ClaudeState.Working, Project = "C:\\Code\\app\\.worktrees\\fix", Since = Ms(Now.AddMinutes(-2)), LastEvent = Ms(Now.AddMinutes(-2)) }.ToJson()));
            File.WriteAllText(Path.Combine(logs, "moved.jsonl"), LogLine("m1", Now.AddMinutes(-30), "C:\\Code\\app", "moved", 100) + "\n" + LogLine("m2", Now.AddMinutes(-3), "C:\\Code\\app\\.worktrees\\fix", "moved", 100) + "\n");
            var service = new ClaudeService(bridge, config) { RegistryAlive = pid => true };
            service.ReadSessions(Now, false); service.Usage.Scan(DateTime.UtcNow);
            var view = service.View(Now, service.Usage.Entries);
            Assert(view.Sessions.Single().ProjectName == "app" && view.UsageFor(ClaudePeriod.Week).Single().Name == "app", "The session and its usage aren't both under the folder it started in.");
            Assert(service.Projects(Now, null).Select(p => p.Name).SequenceEqual(new[] { "app" }), "The worktree it moved into became a project of its own.");
        });

        check("Claude telemetry marks sessions working and counts their usage by project", () => {
            string config = Fresh(directory, "config-otel-view"), bridge = Fresh(directory, "bridge-otel-view");
            string sessions = Path.Combine(config, "sessions"); Directory.CreateDirectory(sessions);
            // Telemetry is kept for a week from the real clock, so this check uses it.
            var now = DateTime.UtcNow;
            File.WriteAllText(Path.Combine(sessions, "201.json"), Registry("t1", 201, "C:\\Code\\widget-screen", "idle", now.AddMinutes(-3)));
            var service = new ClaudeService(bridge, config) { RegistryAlive = pid => true };
            service.Receive(ClaudeOtlp.ParseLogs(LogsFixture("t1", "req_t", now.AddMinutes(-1))));
            service.Receive(ClaudeOtlp.ParseLogs(LogsFixture("t-new", "req_n", now)));
            service.ReadSessions(now, false);
            var view = service.View(now, service.Usage.Entries);
            var t1 = view.Sessions.Single(s => s.Id == "t1");
            Assert(t1.State == ClaudeState.Working && t1.TokensFor(ClaudePeriod.Week) == 6500 && Math.Abs(t1.CostFor(ClaudePeriod.Week) - 0.0123) < 1e-9 && t1.Model == "Opus 5.5", "Telemetry after an idle entry didn't make the session work, or its usage is missing.");
            var week = view.UsageFor(ClaudePeriod.Week);
            Assert(week.Any(p => p.Name == "widget-screen" && p.Folder == "C:\\Code\\widget-screen" && p.Tokens == 6500), "Telemetry usage wasn't put under the session's folder.");
            Assert(week.Any(p => p.Folder == "widget-screen"), "A session with no folder didn't fall back to its repository name.");
        });

        check("Claude project filter hides projects from sessions and usage, and survives a save", () => {
            var choices = new[] { new ClaudeProjectChoice { Folder = "C:/Code/api/", Shown = false }, new ClaudeProjectChoice { Folder = "C:\\Code\\desk", Shown = true } };
            var filter = ClaudeService.Filter(choices, true);
            Assert(!filter("C:\\Code\\api") && !filter("c:\\code\\API") && filter("C:\\Code\\desk") && filter("D:\\new"), "Ticks or Show new projects worked wrongly.");
            var strict = ClaudeService.Filter(choices, false);
            Assert(!strict("D:\\new") && strict("C:\\Code\\desk"), "New projects showed with Show new projects off.");
            string config = Fresh(directory, "config-filter"), bridge = Fresh(directory, "bridge-filter");
            string sessions = Path.Combine(config, "sessions"); Directory.CreateDirectory(sessions);
            File.WriteAllText(Path.Combine(sessions, "301.json"), Registry("a", 301, "C:\\Code\\api", "busy", Now.AddMinutes(-1)));
            File.WriteAllText(Path.Combine(sessions, "302.json"), Registry("d", 302, "C:\\Code\\desk", "busy", Now.AddMinutes(-1)));
            var service = new ClaudeService(bridge, config) { RegistryAlive = pid => true };
            service.ReadSessions(Now, false);
            var usage = new List<ClaudeUsageEntry> { new ClaudeUsageEntry { At = Now.AddMinutes(-2), Session = "a", Folder = "C:\\Code\\api", Tokens = 900, Cost = 1 }, new ClaudeUsageEntry { At = Now.AddMinutes(-2), Session = "d", Folder = "C:\\Code\\desk", Tokens = 100, Cost = 0.1 } };
            service.UseOnlineLimits(new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = 40 }, UpdatedAt = Now, Source = "online" });
            var view = service.View(Now, usage, filter);
            Assert(view.Sessions.Select(s => s.Id).SequenceEqual(new[] { "d" }) && view.UsageFor(ClaudePeriod.Week).Select(p => p.Name).SequenceEqual(new[] { "desk" }), "An unticked project still shows.");
            Assert(view.Limits.FiveHour.Percent == 40, "The filter touched the plan meters.");
            var projects = service.Projects(Now, filter);
            Assert(projects.Count == 2 && projects.Single(p => p.Name == "api").Shown == false && projects.Single(p => p.Name == "desk").Shown, "The project list doesn't show both projects with their ticks.");
            string path = Path.Combine(directory, "claude-projects-settings.xml");
            var settings = new AppSettings { ClaudeShowNewProjects = false, ClaudeProjects = choices };
            SettingsStore.Save(path, settings);
            string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(!loaded.ClaudeShowNewProjects && loaded.ClaudeProjects.Length == 2 && loaded.ClaudeProjects[0].Folder == "C:/Code/api/" && !loaded.ClaudeProjects[0].Shown && loaded.ClaudeProjects[1].Shown, "Project choices didn't survive a save.");
            var copy = loaded.Copy(); copy.ClaudeProjects[0].Shown = true;
            Assert(!loaded.ClaudeProjects[0].Shown, "Copy shares the project choices.");
        });

        check("Claude Connect adds telemetry, leaves the user's own telemetry alone, and updates old connections", () => {
            string config = Fresh(directory, "config-env"), bridge = Fresh(directory, "bridge-env");
            string hook = Path.Combine(directory, ClaudeBridge.HookExe);
            var connector = new ClaudeConnector(config, bridge);
            // The user already exports telemetry elsewhere.
            string original = "{\n  \"env\": {\n    \"OTEL_EXPORTER_OTLP_ENDPOINT\": \"http://collector:4318\",\n    \"FOO\": \"bar\"\n  }\n}\n";
            File.WriteAllText(connector.SettingsPath, original);
            connector.Connect(hook);
            var env = ClaudeJson.Obj(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(connector.SettingsPath))), "env");
            Assert(ClaudeJson.Str(env, "OTEL_EXPORTER_OTLP_ENDPOINT") == "http://collector:4318" && !env.Contains("CLAUDE_CODE_ENABLE_TELEMETRY") && env.Count == 2, "The user's telemetry settings were overwritten.");
            Assert(connector.TelemetryConflicts().SequenceEqual(new[] { "OTEL_EXPORTER_OTLP_ENDPOINT" }) && !connector.NeedsUpdate(), "The conflict isn't reported.");
            connector.Disconnect();
            Assert(File.ReadAllText(connector.SettingsPath) == original, "Disconnect changed the user's env.");
            // A value the user set that equals Widget Screen's stays theirs: Disconnect leaves it.
            File.WriteAllText(connector.SettingsPath, "{\n  \"env\": {\n    \"CLAUDE_CODE_ENABLE_TELEMETRY\": \"1\"\n  }\n}\n");
            connector.Connect(hook);
            Assert(connector.TelemetryConflicts().Count == 0 && !connector.NeedsUpdate(), "A matching value counted as a conflict.");
            connector.Disconnect();
            Assert(File.ReadAllText(connector.SettingsPath) == "{\n  \"env\": {\n    \"CLAUDE_CODE_ENABLE_TELEMETRY\": \"1\"\n  }\n}\n", "Disconnect took out the user's own value.");
            // A connection from v0.23–v0.25: hooks and status line only, no PermissionRequest and no telemetry.
            string old = Fresh(directory, "config-old");
            var older = new ClaudeConnector(old, Fresh(directory, "bridge-old"));
            string command = older.Command("hook").Replace("\"", "\\\"");
            File.WriteAllText(older.SettingsPath, "{\n  \"hooks\": {\n    \"Stop\": [\n      {\n        \"hooks\": [\n          {\n            \"type\": \"command\",\n            \"command\": \"" + command + "\"\n          }\n        ]\n      }\n    ]\n  }\n}\n");
            Assert(older.IsConnected() && older.NeedsUpdate(), "An old connection doesn't ask for an update.");
            older.Connect(hook);
            Assert(!older.NeedsUpdate() && ((List<object>)ClaudeJson.Obj(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(older.SettingsPath))), "hooks").Get("Stop")).Count == 1, "Updating left the old entries or still asks.");
        });

        check("Claude sign-in asks only for the profile scope and keeps its tokens encrypted", () => {
            string link = ClaudeAccount.AuthorizeLink(54321, "state-1", "verifier-1");
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
            Assert(link.StartsWith("https://claude.com/cai/oauth/authorize?") && query["client_id"] == ClaudeAccount.ClientId && query["scope"] == "user:profile" && query["redirect_uri"] == "http://localhost:54321/callback"
                && query["code_challenge_method"] == "S256" && query["code_challenge"] == ClaudeAccount.Challenge("verifier-1") && query["state"] == "state-1" && query["response_type"] == "code", "The sign-in link is wrong: " + link);
            string code, error;
            Assert(ClaudeAccount.ReadCallback("GET /callback?code=abc%23state-1&state=state-1 HTTP/1.1", "state-1", out code, out error) && code == "abc" && error == null, "The callback code wasn't read.");
            Assert(!ClaudeAccount.ReadCallback("GET /callback?code=abc&state=other HTTP/1.1", "state-1", out code, out error) && !ClaudeAccount.ReadCallback("GET /favicon.ico HTTP/1.1", "state-1", out code, out error), "A wrong callback was accepted.");
            Assert(ClaudeAccount.ReadCallback("GET /callback?error=access_denied&state=s HTTP/1.1", "s", out code, out error) && error == "access_denied", "A declined sign-in wasn't read.");
            string path = Path.Combine(Fresh(directory, "signin"), "claude-session.dat");
            new ClaudeSignIn { AccessToken = "test-access", RefreshToken = "test-refresh", Scope = "user:profile", ExpiresUtc = Now }.Save(path);
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("test-refresh"), "The sign-in is stored in the clear.");
            var loaded = ClaudeSignIn.Load(path);
            Assert(loaded.AccessToken == "test-access" && loaded.RefreshToken == "test-refresh" && loaded.ExpiresUtc == Now && !loaded.Expired, "The sign-in didn't round-trip.");
            var token = ClaudeAccount.Parse("{\"access_token\":\"a2\",\"expires_in\":3600,\"scope\":\"user:profile\"}", "keep");
            Assert(token.AccessToken == "a2" && token.RefreshToken == "keep" && token.ExpiresUtc > DateTime.UtcNow.AddMinutes(59), "A token answer without a new refresh token lost the old one.");
        });

        check("Claude plan check refreshes its own token, reads the usage and ends a revoked sign-in", () => {
            string folder = Fresh(directory, "plan-check"), path = Path.Combine(folder, "claude-session.dat");
            new ClaudeSignIn { AccessToken = "old-access", RefreshToken = "refresh-1", ExpiresUtc = DateTime.UtcNow.AddHours(1) }.Save(path);
            int usageCalls = 0; string refreshBody = null;
            var handler = new Handler { Send = (request, body) => {
                if (request.RequestUri.AbsoluteUri == ClaudeAccount.TokenUrl) { refreshBody = body; return Reply(HttpStatusCode.OK, "{\"access_token\":\"new-access\",\"refresh_token\":\"refresh-2\",\"expires_in\":3600}"); }
                usageCalls++;
                Assert(request.Headers.GetValues("anthropic-beta").Single() == ClaudeAccount.Beta, "The usage check lacks its beta header.");
                if (request.Headers.Authorization.Parameter == "old-access") return Reply(HttpStatusCode.Unauthorized, "{}");
                return Reply(HttpStatusCode.OK, "{\"five_hour\":{\"utilization\":37,\"resets_at\":\"2026-11-01T15:00:00Z\"},\"seven_day\":{\"utilization\":61,\"resets_at\":\"2026-11-06T04:00:00Z\"},\"seven_day_opus\":{\"utilization\":12,\"resets_at\":null},\"seven_day_sonnet\":null,\"model_scoped\":[{\"display_name\":\"Fable\",\"utilization\":5,\"resets_at\":\"2026-11-06T04:00:00Z\"}]}");
            } };
            using (var account = new ClaudeAccount(path, handler, url => { }))
            {
                var limits = account.UsageAsync(Now).GetAwaiter().GetResult();
                Assert(limits != null && limits.FiveHour.Percent == 37 && limits.Week.Percent == 61 && limits.Source == "online" && usageCalls == 2, "The usage wasn't read after one refresh.");
                Assert(limits.Extra.Count == 2 && limits.Extra[0].Key == "Opus week" && limits.Extra[0].Value.Percent == 12 && limits.Extra[1].Key == "Fable week", "Extra windows parsed wrongly.");
                var refresh = ClaudeJson.Obj(ClaudeJson.Parse(refreshBody));
                Assert(ClaudeJson.Str(refresh, "grant_type") == "refresh_token" && ClaudeJson.Str(refresh, "refresh_token") == "refresh-1" && ClaudeJson.Str(refresh, "client_id") == ClaudeAccount.ClientId && ClaudeJson.Str(refresh, "scope") == "user:profile", "The refresh request is wrong.");
                Assert(ClaudeSignIn.Load(path).RefreshToken == "refresh-2", "The renewed sign-in wasn't saved.");
                Assert(handler.Hosts.All(h => h == "platform.claude.com" || h == "api.anthropic.com"), "A token went to another host: " + String.Join(",", handler.Hosts));
            }
            // A revoked sign-in: the refresh is refused, so the sign-in ends and Settings asks to sign in again.
            new ClaudeSignIn { AccessToken = "", RefreshToken = "revoked", ExpiresUtc = DateTime.MinValue }.Save(path);
            var refused = new Handler { Send = (request, body) => Reply(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}") };
            using (var account = new ClaudeAccount(path, refused, url => { }))
            {
                Assert(account.UsageAsync(Now).GetAwaiter().GetResult() == null && account.Expired && !account.SignedIn && ClaudeSignIn.Load(path).Expired, "A revoked sign-in didn't end.");
                account.SignOut();
                Assert(!File.Exists(path) && !account.Expired && !account.SignedIn, "Sign out didn't forget the sign-in.");
            }
            // A network problem keeps the numbers and says so.
            new ClaudeSignIn { AccessToken = "ok", RefreshToken = "r", ExpiresUtc = DateTime.UtcNow.AddHours(1) }.Save(path);
            var offline = new Handler { Send = (request, body) => { throw new HttpRequestException("offline"); } };
            var service = new ClaudeService(Fresh(directory, "bridge-plan"), Fresh(directory, "config-plan"), account: new ClaudeAccount(path, offline, url => { }));
            service.UseOnlineLimits(new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = 20 }, UpdatedAt = Now.AddMinutes(-30), Source = "online" });
            Assert(service.CheckOnlineAsync(Now).GetAwaiter().GetResult() && service.Plan == ClaudePlanState.Failed && service.Limits.FiveHour.Percent == 20, "A failed check lost the numbers or didn't say so.");
            var view = new ClaudeView { Limits = service.Limits, Plan = service.Plan };
            Assert(ClaudeText.Source(view, true).StartsWith("Plan limits as of ") && ClaudeText.Old(view, Now), "A failed check isn't shown as old numbers.");
            Assert(!service.OnlineDue(Now.AddMinutes(4)) && service.OnlineDue(Now.AddMinutes(5)), "The plan check isn't every 5 minutes.");
            service.Dispose();
        });
    }
}
