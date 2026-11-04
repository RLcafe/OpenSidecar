using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WidgetScreen;

// The Claude Code card (decision 027, docs/design/claude-card.md): the plan check and sign-in, the session counts, tokens
// per minute, the removal of the old connection, and the four tiles' layout. Everything runs in temporary folders under
// the test output; Claude Code's real folder, the Claude app's data and Anthropic are never reached.
internal static class ClaudeTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static readonly DateTime Now = new DateTime(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-checks", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
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
    private static string Reply(string request, string time, int input, int output, int creation, int read)
    {
        return "{\"type\":\"assistant\",\"timestamp\":\"" + time + "\",\"requestId\":\"" + request + "\",\"message\":{\"id\":\"m-" + request + "\",\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":" + input + ",\"output_tokens\":" + output + ",\"cache_creation_input_tokens\":" + creation + ",\"cache_read_input_tokens\":" + read + "}}}";
    }
    private static string Iso(DateTime time) { return time.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        return true;
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

        check("Claude plan usage parses both windows, the limits list, and epoch or ISO times", () => {
            var online = ClaudePlan.ParseUsage("{\"five_hour\":{\"utilization\":37.0,\"resets_at\":\"2026-11-04T15:59:59.943648+00:00\"},\"seven_day\":{\"utilization\":161,\"resets_at\":\"2026-11-06T03:59:59+00:00\"},\"seven_day_opus\":null}", Now);
            Assert(online != null && online.FiveHour.Percent == 37 && online.FiveHour.ResetsAt.Value.Hour == 15 && online.Week.Percent == 100 && online.UpdatedAt == Now, "The usage answer parsed wrongly.");
            var listed = ClaudePlan.ParseUsage("{\"five_hour\":null,\"limits\":[{\"kind\":\"weekly_all\",\"percent\":22,\"resets_at\":\"2026-11-06T04:00:00Z\"},{\"kind\":\"session_five_hour\",\"percent\":9,\"resets_at\":null}]}", Now);
            Assert(listed.Week.Percent == 22 && listed.FiveHour.Percent == 9 && !listed.FiveHour.ResetsAt.HasValue, "The limits list doesn't fill the windows the older fields left out.");
            Assert(ClaudePlan.ParseUsage("{\"five_hour\":null,\"seven_day\":null}", Now) == null && ClaudePlan.ParseUsage("<html>", Now) == null, "An empty or broken answer was used.");
            Assert(ClaudeTime.Parse(new JsonNumber("1738425600000")) == new DateTime(2025, 2, 1, 16, 0, 0, DateTimeKind.Utc) && ClaudeTime.Parse(new JsonNumber("1738425600")) == new DateTime(2025, 2, 1, 16, 0, 0, DateTimeKind.Utc) && ClaudeTime.Parse("nonsense") == null, "Times parsed wrongly.");
            Assert(ClaudePlan.Percent(new ClaudeLimitWindow { Percent = 90, ResetsAt = Now.AddMinutes(-1) }, Now) == 0 && ClaudePlan.Percent(new ClaudeLimitWindow { Percent = 90, ResetsAt = Now.AddMinutes(1) }, Now) == 90 && Double.IsNaN(ClaudePlan.Percent(null, Now)), "A window past its reset still counts.");
        });

        check("Claude sign-in asks only for the profile scope and keeps its tokens encrypted", () => {
            string link = ClaudeAccount.AuthorizeLink(54321, "state-1", "verifier-1");
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
            Assert(link.StartsWith("https://claude.com/cai/oauth/authorize?") && query["client_id"] == ClaudeAccount.ClientId && query["scope"] == "user:profile" && query["redirect_uri"] == "http://localhost:54321/callback"
                && query["code_challenge_method"] == "S256" && query["code_challenge"] == ClaudeAccount.Challenge("verifier-1") && query["state"] == "state-1" && query["response_type"] == "code", "The sign-in link is wrong: " + link);
            string code, error;
            Assert(ClaudeAccount.ReadCallback("GET /callback?code=abc%23state-1&state=state-1 HTTP/1.1", "state-1", out code, out error) && code == "abc" && error == null, "The callback code wasn't read.");
            Assert(!ClaudeAccount.ReadCallback("GET /callback?code=abc&state=other HTTP/1.1", "state-1", out code, out error) && !ClaudeAccount.ReadCallback("GET /favicon.ico HTTP/1.1", "state-1", out code, out error), "A wrong callback was accepted.");
            string path = Path.Combine(Fresh(directory, "signin"), "claude-session.dat");
            new ClaudeSignIn { AccessToken = "test-access", RefreshToken = "test-refresh", Scope = "user:profile", ExpiresUtc = Now }.Save(path);
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("test-refresh"), "The sign-in is stored in the clear.");
            var loaded = ClaudeSignIn.Load(path);
            Assert(loaded.AccessToken == "test-access" && loaded.RefreshToken == "test-refresh" && loaded.ExpiresUtc == Now && !loaded.Expired, "The sign-in didn't round-trip.");
            var token = ClaudeAccount.Parse("{\"access_token\":\"a2\",\"expires_in\":3600,\"scope\":\"user:profile\"}", "keep");
            Assert(token.AccessToken == "a2" && token.RefreshToken == "keep" && token.ExpiresUtc > DateTime.UtcNow.AddMinutes(59), "A token answer without a new refresh token lost the old one.");
        });

        check("Claude plan check refreshes its own token, ends a revoked sign-in, and a 403 means no limits", () => {
            string folder = Fresh(directory, "plan-check"), path = Path.Combine(folder, "claude-session.dat");
            new ClaudeSignIn { AccessToken = "old-access", RefreshToken = "refresh-1", ExpiresUtc = DateTime.UtcNow.AddHours(1) }.Save(path);
            int usageCalls = 0; string refreshBody = null;
            var handler = new Handler { Send = (request, body) => {
                if (request.RequestUri.AbsoluteUri == ClaudeAccount.TokenUrl) { refreshBody = body; return Reply(HttpStatusCode.OK, "{\"access_token\":\"new-access\",\"refresh_token\":\"refresh-2\",\"expires_in\":3600}"); }
                usageCalls++;
                Assert(request.Headers.GetValues("anthropic-beta").Single() == ClaudeAccount.Beta, "The usage check lacks its beta header.");
                if (request.Headers.Authorization.Parameter == "old-access") return Reply(HttpStatusCode.Unauthorized, "{}");
                return Reply(HttpStatusCode.OK, "{\"five_hour\":{\"utilization\":37,\"resets_at\":\"2026-11-04T15:00:00Z\"},\"seven_day\":{\"utilization\":61,\"resets_at\":\"2026-11-06T04:00:00Z\"}}");
            } };
            using (var service = new ClaudeService(Fresh(directory, "plan-config"), path, Fresh(directory, "plan-bridge"), new ClaudeAccount(path, handler, url => { }), () => new string[0]))
            {
                Assert(service.Plan == ClaudePlanState.Reading && service.PlanDue(Now), "A fresh sign-in isn't checked at once.");
                Assert(service.CheckPlanAsync(Now).GetAwaiter().GetResult() && service.Plan == ClaudePlanState.Current && service.Limits.FiveHour.Percent == 37 && service.Limits.Week.Percent == 61 && usageCalls == 2, "The usage wasn't read after one refresh.");
                var refresh = ClaudeJson.Obj(ClaudeJson.Parse(refreshBody));
                Assert(ClaudeJson.Str(refresh, "grant_type") == "refresh_token" && ClaudeJson.Str(refresh, "refresh_token") == "refresh-1" && ClaudeJson.Str(refresh, "scope") == "user:profile" && ClaudeSignIn.Load(path).RefreshToken == "refresh-2", "The refresh request is wrong or wasn't saved.");
                Assert(handler.Hosts.All(h => h == "platform.claude.com" || h == "api.anthropic.com"), "A token went to another host: " + String.Join(",", handler.Hosts));
                Assert(!service.PlanDue(Now.AddMinutes(4)) && service.PlanDue(Now.AddMinutes(5)), "The plan check isn't every 5 minutes.");
            }
            new ClaudeSignIn { AccessToken = "", RefreshToken = "revoked", ExpiresUtc = DateTime.MinValue }.Save(path);
            var refused = new Handler { Send = (request, body) => Reply(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}") };
            using (var service = new ClaudeService(Fresh(directory, "plan-config"), path, Fresh(directory, "plan-bridge"), new ClaudeAccount(path, refused, url => { }), () => new string[0]))
            {
                service.CheckPlanAsync(Now).GetAwaiter().GetResult();
                Assert(service.Plan == ClaudePlanState.Expired && ClaudeSignIn.Load(path).Expired, "A revoked sign-in didn't end.");
                service.Account.SignOut(); service.SignedOut();
                Assert(!File.Exists(path) && service.Plan == ClaudePlanState.SignedOut, "Sign out didn't forget the sign-in.");
            }
            new ClaudeSignIn { AccessToken = "access", RefreshToken = "refresh", ExpiresUtc = DateTime.UtcNow.AddHours(1) }.Save(path);
            var forbidden = new Handler { Send = (request, body) => Reply(HttpStatusCode.Forbidden, "{\"error\":{\"type\":\"permission_error\"}}") };
            using (var service = new ClaudeService(Fresh(directory, "plan-config"), path, Fresh(directory, "plan-bridge"), new ClaudeAccount(path, forbidden, url => { }), () => new string[0]))
            {
                service.CheckPlanAsync(Now).GetAwaiter().GetResult();
                Assert(service.Plan == ClaudePlanState.NoMeters && service.Account.SignedIn && !ClaudeSignIn.Load(path).Expired, "A 403 ended the sign-in or isn't the no-limits state.");
                var reading = new ClaudeReading { Plan = service.Plan, Now = Now };
                Assert(ClaudeText.Percent(reading, false) == "—" && ClaudeText.PlanNote(reading, false, false) == "No limits on this plan", "The tile doesn't say the plan has no limits.");
            }
        });

        check("Claude plan check backs off when Anthropic turns it away, keeps the last numbers, and recovers", () => {
            Assert(ClaudeService.Backoff(TimeSpan.FromMinutes(5)) == TimeSpan.FromMinutes(10) && ClaudeService.Backoff(TimeSpan.FromMinutes(10)) == TimeSpan.FromMinutes(15) && ClaudeService.Backoff(TimeSpan.FromMinutes(15)) == TimeSpan.FromMinutes(15), "Backoff steps wrong.");
            string path = Path.Combine(Fresh(directory, "backoff"), "claude-session.dat");
            new ClaudeSignIn { AccessToken = "ok", RefreshToken = "r", ExpiresUtc = DateTime.UtcNow.AddHours(1) }.Save(path);
            var status = HttpStatusCode.OK;
            var handler = new Handler { Send = (request, body) => status == HttpStatusCode.OK ? Reply(status, "{\"five_hour\":{\"utilization\":10,\"resets_at\":\"2026-11-04T15:00:00Z\"},\"seven_day\":{\"utilization\":20,\"resets_at\":\"2026-11-06T04:00:00Z\"}}") : Reply(status, "{}") };
            using (var service = new ClaudeService(Fresh(directory, "backoff-config"), path, Fresh(directory, "backoff-bridge"), new ClaudeAccount(path, handler, url => { }), () => new string[0]))
            {
                service.CheckPlanAsync(Now).GetAwaiter().GetResult();
                status = (HttpStatusCode)429; service.CheckPlanAsync(Now).GetAwaiter().GetResult();
                Assert(service.Plan == ClaudePlanState.Failed && service.BackingOff && service.NextWait == TimeSpan.FromMinutes(10) && !service.PlanDue(Now.AddMinutes(9)) && service.PlanDue(Now.AddMinutes(10)), "A 429 didn't double the wait.");
                Assert(service.Limits.FiveHour.Percent == 10, "A failed check lost the numbers.");
                var reading = new ClaudeReading { Plan = service.Plan, Limits = service.Limits, Now = Now };
                Assert(ClaudeText.Percent(reading, false) == "10%" && ClaudeText.PlanNote(reading, false, true).StartsWith("As of "), "A failed check doesn't say when the numbers are from.");
                status = HttpStatusCode.ServiceUnavailable; service.CheckPlanAsync(Now).GetAwaiter().GetResult(); service.CheckPlanAsync(Now).GetAwaiter().GetResult();
                Assert(service.NextWait == TimeSpan.FromMinutes(15), "The backoff went past 15 minutes or didn't grow.");
                status = HttpStatusCode.OK; service.CheckPlanAsync(Now).GetAwaiter().GetResult();
                Assert(service.Plan == ClaudePlanState.Current && !service.BackingOff && service.NextWait == ClaudeService.PlanEvery, "A success didn't go back to every 5 minutes.");
            }
            var offline = new Handler { Send = (request, body) => { throw new HttpRequestException("offline"); } };
            using (var service = new ClaudeService(Fresh(directory, "backoff-config"), path, Fresh(directory, "backoff-bridge"), new ClaudeAccount(path, offline, url => { }), () => new string[0]))
            {
                service.CheckPlanAsync(Now).GetAwaiter().GetResult();
                Assert(service.Plan == ClaudePlanState.Failed && !service.BackingOff, "A network failure backed off or wasn't a failure.");
            }
        });

        check("Claude counts sessions: working, needs you, blocked and done, from Claude Code's list and the Claude app", () => {
            var live = ClaudeSessions.ParseLive("{\"pid\":4242,\"sessionId\":\"s1\",\"cwd\":\"C:\\\\Code\\\\a\",\"status\":\"busy\",\"startedAt\":1793537932958}");
            Assert(live.Session == "s1" && live.Pid == 4242 && live.Status == "busy" && live.Started.Year == 2026, "A session list entry was read wrongly.");
            Assert(ClaudeSessions.ParseLive("{\"pid\":1,\"sessionId\":\"x\",\"spare\":true}") == null && ClaudeSessions.ParseLive("nope") == null, "A spare process or broken file counted.");
            var thread = ClaudeSessions.ParseThread("{\"cliSessionId\":\"t1\",\"isArchived\":false,\"lastActivityAt\":1793537990000,\"lastAssistantUuid\":\"u2\",\"postTurnSummary\":{\"status_category\":\"review_ready\",\"summarizes_uuid\":\"u2\"}}");
            Assert(thread.Session == "t1" && thread.Category == "review_ready" && !thread.Archived, "A Claude app thread was read wrongly.");
            Assert(ClaudeSessions.ParseThread("{\"cliSessionId\":\"t2\",\"lastAssistantUuid\":\"u3\",\"postTurnSummary\":{\"status_category\":\"blocked\",\"summarizes_uuid\":\"u2\"}}").Category == "", "A summary of an older reply counted.");
            Assert(ClaudeSessions.ParseThread("{\"sessionId\":\"local_x\"}") == null, "A record without a Claude Code session was kept.");
            Func<string, string, ClaudeLiveSession> running = (id, state) => new ClaudeLiveSession { Session = id, Status = state };
            Func<string, string, bool, double, ClaudeAppThread> app = (id, category, archived, hoursAgo) => new ClaudeAppThread { Session = id, Category = category, Archived = archived, LastActive = Now.AddHours(-hoursAgo) };
            var counts = ClaudeSessions.Count(
                new[] { running("busy", "busy"), running("ask", "waiting"), running("idle", "idle"), running("idle-review", "idle"), running("idle-error", "idle"), running("busy", "busy") },
                new[] { app("idle-review", "review_ready", false, 1), app("done-app", "completed", false, 2), app("blocked-app", "blocked", false, 3), app("old", "completed", false, 30), app("archived", "blocked", true, 1), app("busy", "blocked", false, 1), app("plain", "", false, 5) },
                new HashSet<string> { "idle-error" }, Now);
            Assert(counts.Working == 1 && counts.NeedsYou == 2 && counts.Blocked == 2 && counts.Done == 3 && counts.Total == 8, "Counts wrong: " + counts.Done + " done, " + counts.NeedsYou + " need you, " + counts.Blocked + " blocked, " + counts.Working + " working.");
            // From files: a dead process doesn't count, a live one does; a thread the app archived doesn't.
            string config = Fresh(directory, "sessions-config"), appData = Fresh(directory, "sessions-app");
            Directory.CreateDirectory(Path.Combine(config, "sessions")); Directory.CreateDirectory(Path.Combine(appData, "org", "acct"));
            int me = Process.GetCurrentProcess().Id;
            File.WriteAllText(Path.Combine(config, "sessions", me + ".json"), "{\"pid\":" + me + ",\"sessionId\":\"mine\",\"status\":\"waiting\"}");
            File.WriteAllText(Path.Combine(config, "sessions", "1.json"), "{\"pid\":2147483000,\"sessionId\":\"gone\",\"status\":\"busy\"}");
            File.WriteAllText(Path.Combine(appData, "org", "acct", "local_a.json"), "{\"cliSessionId\":\"app-1\",\"lastActivityAt\":" + ClaudeTime.Epoch(DateTime.UtcNow.AddMinutes(-5)) + ",\"postTurnSummary\":{\"status_category\":\"completed\"}}");
            File.WriteAllText(Path.Combine(appData, "org", "acct", "local_b.json"), "{\"cliSessionId\":\"app-2\",\"isArchived\":true,\"lastActivityAt\":" + ClaudeTime.Epoch(DateTime.UtcNow.AddMinutes(-5)) + "}");
            var sessions = new ClaudeSessions(config, () => new[] { appData });
            Assert(sessions.Read(DateTime.UtcNow, new HashSet<string>()) && sessions.Counts.NeedsYou == 1 && sessions.Counts.Working == 0 && sessions.Counts.Done == 1 && sessions.Counts.Total == 2, "The session list and app records weren't counted right.");
            Assert(!sessions.Read(DateTime.UtcNow, new HashSet<string>()), "Reading again without changes reported a change.");
            File.WriteAllText(Path.Combine(config, "sessions", me + ".json"), "{\"pid\":" + me + ",\"sessionId\":\"mine\",\"status\":\"busy\"}");
            Assert(sessions.Read(DateTime.UtcNow, new HashSet<string>()) && sessions.Counts.Working == 1 && sessions.Counts.NeedsYou == 0, "A changed session file wasn't read again.");
        });

        check("Claude tokens per minute: each reply once, no cache reads, API errors mark the session blocked", () => {
            var lines = ClaudeTokens.Lines(String.Join("\n", new[] {
                "{\"type\":\"user\",\"message\":{\"content\":\"hi\"}}",
                Reply("r1", "2026-11-04T11:58:00Z", 10, 20, 30, 4000),
                "{\"type\":\"assistant\",\"isApiErrorMessage\":true,\"timestamp\":\"2026-11-04T11:59:00Z\",\"message\":{\"model\":\"<synthetic>\",\"content\":[{\"type\":\"text\",\"text\":\"API Error\"}]}}",
                "{\"type\":\"user\",\"isMeta\":true,\"message\":{\"content\":\"note\"}}",
                "not json", "" }));
            Assert(lines.Count == 4 && lines[1].Tokens == 60 && lines[1].Key == "r1" && lines[2].Error && lines[3].Type == null, "Log lines parsed wrongly.");
            var bins = ClaudeTokens.Bins(new[] { new KeyValuePair<DateTime, long>(Now.AddSeconds(-10), 600), new KeyValuePair<DateTime, long>(Now.AddMinutes(-4).AddSeconds(-1), 400), new KeyValuePair<DateTime, long>(Now.AddMinutes(-40), 999) }, Now);
            Assert(bins.Length == 30 && bins[29] == 600 && bins[25] == 400 && bins.Sum() == 1000 && ClaudeTokens.Rate(bins) == 200, "Minutes or the rate wrong.");
            Assert(ClaudeTokens.Format(940) == "940" && ClaudeTokens.Format(9450) == "9.4k" && ClaudeTokens.Format(12400) == "12k" && ClaudeTokens.Format(1250000) == "1.2M" && ClaudeTokens.Format(Double.NaN) == "—", "Token numbers formatted wrongly.");
            // A log on disk: read from its tail, then only what's added.
            string config = Fresh(directory, "tokens-config"), folder = Path.Combine(config, "projects", "C--Code-a");
            Directory.CreateDirectory(Path.Combine(folder, "s1", "subagents"));
            DateTime now = DateTime.UtcNow;
            string log = Path.Combine(folder, "s1.jsonl");
            File.WriteAllText(log, Reply("a", Iso(now.AddMinutes(-2)), 100, 100, 0, 9999) + "\n" + Reply("a", Iso(now.AddMinutes(-2)), 100, 100, 0, 9999) + "\n" + Reply("b", Iso(now.AddHours(-2)), 5000, 0, 0, 0) + "\n");
            File.WriteAllText(Path.Combine(folder, "s1", "subagents", "agent-x.jsonl"), Reply("c", Iso(now.AddMinutes(-1)), 300, 0, 0, 0) + "\n{\"type\":\"assistant\",\"isApiErrorMessage\":true,\"timestamp\":\"" + Iso(now) + "\",\"message\":{}}\n");
            var tokens = new ClaudeTokens(config);
            Assert(!tokens.Scanned, "Scanned before a scan.");
            tokens.Scan(now);
            Assert(tokens.Found && tokens.Scanned && tokens.History.Sum() == 500 && tokens.Errored.Count == 0, "The first scan counted " + tokens.History.Sum() + " tokens (helpers count, repeats and old replies don't), or a helper's error blocked its session.");
            File.AppendAllText(log, "{\"type\":\"assistant\",\"isApiErrorMessage\":true,\"timestamp\":\"" + Iso(now) + "\",\"message\":{\"model\":\"<synthetic>\"}}\n");
            tokens.Scan(now);
            Assert(tokens.Errored.Contains("s1") && tokens.History.Sum() == 500, "An API error at the end of the log didn't mark it blocked, or a scan counted again.");
            File.AppendAllText(log, "{\"type\":\"user\",\"message\":{\"content\":\"try again\"}}\n" + Reply("d", Iso(now), 50, 50, 0, 0) + "\n");
            tokens.Scan(now);
            Assert(!tokens.Errored.Contains("s1") && tokens.History.Sum() == 600, "The next prompt didn't clear blocked, or the new reply wasn't counted.");
            Assert(!new ClaudeTokens(Fresh(directory, "tokens-none")).Found, "A missing logs folder was found.");
            var none = new ClaudeTokens(Path.Combine(directory, "claude-checks", "nowhere")); none.Scan(now);
            Assert(!none.Found && none.Scanned && ClaudeText.TokensValue(new ClaudeReading { LogsScanned = true }) == "—", "No logs don't say so.");
        });

        check("Claude removes the old Claude Code connection exactly, once, and leaves the rest of settings.json", () => {
            string config = Fresh(directory, "cleanup-config"), bridge = Fresh(directory, "cleanup-bridge");
            string hook = "\\\"C:/Users/u/AppData/Local/WidgetScreen/claude/ClaudeHook.exe\\\"";
            File.WriteAllText(Path.Combine(bridge, "previous-statusline.json"), "{\"type\":\"command\",\"command\":\"my-line\"}");
            File.WriteAllText(Path.Combine(bridge, "connected-env.json"), "{\"keys\":[\"CLAUDE_CODE_ENABLE_TELEMETRY\",\"OTEL_EXPORTER_OTLP_ENDPOINT\",\"OTEL_LOGS_EXPORTER\"]}");
            File.WriteAllText(Path.Combine(bridge, "ClaudeHook.exe"), "old");
            string settings = "{\n  \"model\": \"opus\",\n  \"hooks\": {\n    \"Stop\": [\n      {\n        \"hooks\": [\n          {\n            \"type\": \"command\",\n            \"command\": \"" + hook + " hook\"\n          },\n          {\n            \"type\": \"command\",\n            \"command\": \"mine\"\n          }\n        ]\n      }\n    ],\n    \"SessionStart\": [\n      {\n        \"hooks\": [\n          {\n            \"type\": \"command\",\n            \"command\": \"" + hook + " hook\"\n          }\n        ]\n      }\n    ]\n  },\n"
                + "  \"statusLine\": {\n    \"type\": \"command\",\n    \"command\": \"" + hook + " statusline\"\n  },\n  \"env\": {\n    \"CLAUDE_CODE_ENABLE_TELEMETRY\": \"1\",\n    \"OTEL_EXPORTER_OTLP_ENDPOINT\": \"http://127.0.0.1:43832\",\n    \"OTEL_LOGS_EXPORTER\": \"console\",\n    \"MY_OWN\": \"x\"\n  }\n}\n";
            File.WriteAllText(Path.Combine(config, "settings.json"), settings);
            var cleanup = new ClaudeCleanup(config, bridge);
            Assert(cleanup.Connected() && cleanup.Run() == null && !cleanup.Connected(), "The old connection wasn't taken out.");
            var root = ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(cleanup.SettingsPath)));
            var stop = (List<object>)ClaudeJson.Obj(root, "hooks").Get("Stop");
            var env = ClaudeJson.Obj(root, "env");
            Assert(ClaudeJson.Str(root, "model") == "opus" && stop.Count == 1 && ((List<object>)((JsonObject)stop[0]).Get("hooks")).Count == 1 && !ClaudeJson.Obj(root, "hooks").Contains("SessionStart"), "The user's own hook or keys were touched, or ours stayed.");
            Assert(ClaudeJson.Str(ClaudeJson.Obj(root, "statusLine"), "command") == "my-line", "The earlier status line didn't come back.");
            Assert(!env.Contains("CLAUDE_CODE_ENABLE_TELEMETRY") && !env.Contains("OTEL_EXPORTER_OTLP_ENDPOINT") && ClaudeJson.Str(env, "OTEL_LOGS_EXPORTER") == "console" && ClaudeJson.Str(env, "MY_OWN") == "x", "The env keys weren't exactly the ones Connect set.");
            Assert(!Directory.Exists(bridge) && Directory.GetFiles(config, "settings.json" + ClaudeCleanup.BackupSuffix + "-*").Length == 1, "The bridge folder stayed or no backup was kept.");
            string again = File.ReadAllText(cleanup.SettingsPath);
            Assert(cleanup.Run() == null && File.ReadAllText(cleanup.SettingsPath) == again && Directory.GetFiles(config, "settings.json" + ClaudeCleanup.BackupSuffix + "-*").Length == 1, "A second run changed settings.json again.");
            string broken = Fresh(directory, "cleanup-broken");
            File.WriteAllText(Path.Combine(broken, "settings.json"), "{\"statusLine\": {\"command\": \"ClaudeHook.exe statusline\"},");
            var refused = new ClaudeCleanup(broken, Fresh(directory, "cleanup-broken-bridge"));
            Assert(!refused.Connected() && refused.Run() == null && File.ReadAllText(refused.SettingsPath).EndsWith(","), "An invalid settings.json was rewritten.");
            Assert(new ClaudeCleanup(Fresh(directory, "cleanup-empty"), Path.Combine(directory, "claude-checks", "no-bridge")).Run() == null, "No settings.json at all failed.");
        });

        check("Claude tiles: a row on a long card, a square on a squarer one, a column on a tall one", () => {
            Assert(CardSizing.ClaudeColumns(1808, 228) == 4 && CardSizing.ClaudeColumns(900, 180) == 4 && CardSizing.ClaudeColumns(412, 376) == 2 && CardSizing.ClaudeColumns(424, 226) == 2 && CardSizing.ClaudeColumns(252, 640) == 1 && CardSizing.ClaudeColumns(252, 156) == 2, "Tile arrangement wrong.");
            Assert(CardSizing.ClaudeMascotSize(200, 180) % 16 == 0 && CardSizing.ClaudeMascotSize(60, 60) == 32 && CardSizing.ClaudeMascotSize(2000, 2000) == 192, "The mascot isn't a whole number of units a pixel, or leaves its bounds.");
            Assert(ClaudeMascot.Sprite.Length == 16 && ClaudeMascot.Sprite.All(r => r.Length == 16) && ClaudeMascot.Sprite.All(r => r.SequenceEqual(r.Reverse())), "The mascot isn't 16 × 16 and symmetric.");
            foreach (ClaudeMood mood in Enum.GetValues(typeof(ClaudeMood)))
                foreach (int at in ClaudeMascot.EyeColumns)
                    for (int row = 0; row < 4; row++) for (int col = 0; col < 3; col++)
                        Assert(ClaudeMascot.Eye(mood)[row][col] != 'e' || ClaudeMascot.Sprite[ClaudeMascot.EyeRow + row][at + col] == 'v', "An eye leaves the visor.");
            Assert(ClaudeText.Mood(new ClaudeCounts { Done = 1, NeedsYou = 1, Blocked = 1 }) == ClaudeMood.NeedsYou && ClaudeText.Mood(new ClaudeCounts { Done = 1, Blocked = 1 }) == ClaudeMood.Blocked && ClaudeText.Mood(new ClaudeCounts { Done = 2, Working = 1 }) == ClaudeMood.Working && ClaudeText.Mood(new ClaudeCounts()) == ClaudeMood.Sleeping, "The mascot's mood doesn't follow the most urgent count.");
            Assert(ClaudeText.Summary(new ClaudeCounts { Done = 2, NeedsYou = 3, Blocked = 1 }) == "3 need you, 1 blocked, 2 done" && ClaudeText.Summary(new ClaudeCounts { NeedsYou = 1 }) == "1 needs you", "Count words wrong.");
            var reading = new ClaudeReading { Plan = ClaudePlanState.Current, Now = Now, Limits = new ClaudeLimits { UpdatedAt = Now, FiveHour = new ClaudeLimitWindow { Percent = 64.4, ResetsAt = Now.AddMinutes(90) }, Week = null } };
            Assert(ClaudeText.Percent(reading, false) == "64%" && ClaudeText.PlanNote(reading, false, true).StartsWith("Resets ") && ClaudeText.Percent(reading, true) == "—" && ClaudeText.PlanNote(reading, true, true) == "No weekly limit", "Plan tile words wrong.");
            Assert(ClaudeText.PlanNote(new ClaudeReading(), false, false) == "Sign in: Settings → Claude" && ClaudeText.Percent(new ClaudeReading { Plan = ClaudePlanState.Reading }, true) == "…", "Signed out or reading doesn't say so.");
        });

        check("Claude card keeps its tiles inside the card at every size, at text 100% and 140%", () => {
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (var size in new[] { WidgetLayout.Minimum("Claude"), new Size(472, 310), new Size(1856, 300), new Size(460, 460), new Size(320, 576), new Size(1856, 576) })
                {
                    string path = Path.Combine(directory, "claude-card.xml");
                    var settings = new AppSettings { TextScale = scale };
                    settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Claude"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                    SettingsStore.Save(path, settings);
                    Window window;
                    using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml")) window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            controller.UseClaudeSample(true);
                            var dashboard = (FrameworkElement)window.FindName("Dashboard");
                            dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                            var card = (FrameworkElement)window.FindName("ClaudeCard");
                            var view = controller.ClaudeCardView;
                            int expected = CardSizing.ClaudeColumns(card.ActualWidth - 48, card.ActualHeight - 84);
                            Assert(view.TileColumns == expected, "The card at " + size + " has " + view.TileColumns + " columns, not " + expected + ".");
                            Assert(view.Mascot.Mood == ClaudeMood.NeedsYou && ((TextBlock)window.FindName("ClaudeStatus")).Text == "SAMPLE" && ((TextBlock)window.FindName("ClaudeFiveValue")).Text == "64%", "The sample isn't labelled, or the tiles don't show it.");
                            foreach (var element in Descendants(card).OfType<FrameworkElement>().Where(e => (e is TextBlock || e is ClaudeMascot || e is MeterBar) && Shown(e, card) && e.ActualWidth > 0))
                            {
                                var box = element.TransformToAncestor(card).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                                Assert(box.Left >= -0.5 && box.Top >= -0.5 && box.Right <= card.ActualWidth + 0.5 && box.Bottom <= card.ActualHeight + 0.5, element.GetType().Name + " '" + (element is TextBlock ? ((TextBlock)element).Text : "") + "' leaves the " + size + " card at " + scale * 100 + "%: " + box);
                            }
                            var counts = (Panel)window.FindName("ClaudeCounts");
                            Assert(counts.Children.Count == 4, "The sample's four counts don't all show.");
                        }
                    }
                    finally { window.Close(); }
                }
        });
    }
}
