using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WidgetScreen;

// Package 1 of the watcher plan, "Trust the sources" (decision 023): a session lives until its own SessionEnd or its
// process is gone, every hook event Claude Code documents is read (fixtures shaped like the hooks reference), Stuck has
// a reason, the plan check tells "no meters" from an ended sign-in, telemetry binds a free port, and the car shows its
// context. Everything runs in throwaway folders; Claude Code's real folder is never read or written.
internal static class ClaudeTrustTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-trust", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static void Write(string bridge, ClaudeSessionFile file) { ClaudeBridge.WriteAtomic(ClaudeBridge.SessionPath(bridge, file.Id), ClaudeJson.Compact(file.ToJson())); }
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Send;
        public readonly List<string> Hosts = new List<string>();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Hosts.Add(request.RequestUri.Host);
            return Task.FromResult(Send(request));
        }
    }
    private static HttpResponseMessage Reply(HttpStatusCode status, string body) { return new HttpResponseMessage(status) { Content = new StringContent(body ?? "") }; }
    // A process id that has just exited.
    private static int DeadPid()
    {
        using (var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false }))
        {
            p.WaitForExit();
            return p.Id;
        }
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude sessions stay until their own SessionEnd or their process is gone, whatever the process is called", () => {
            string bridge = Fresh(directory, "alive");
            DateTime real = DateTime.UtcNow;
            var me = Process.GetCurrentProcess();
            DateTime started = me.StartTime.ToUniversalTime();
            // This test exe isn't claude.exe or node.exe: its session still runs (it used to be dropped by name).
            Write(bridge, new ClaudeSessionFile { Id = "renamed-exe", State = ClaudeState.Working, Since = ClaudeBridge.Epoch(real), LastEvent = ClaudeBridge.Epoch(real), Pid = me.Id });
            Write(bridge, new ClaudeSessionFile { Id = "exited", State = ClaudeState.Working, Since = ClaudeBridge.Epoch(real), LastEvent = ClaudeBridge.Epoch(real), Pid = DeadPid() });
            // The id now belongs to a process that started after the session was first seen: reused, so gone.
            Write(bridge, new ClaudeSessionFile { Id = "reused", State = ClaudeState.Done, Since = ClaudeBridge.Epoch(started.AddMinutes(-10)), LastEvent = ClaudeBridge.Epoch(started.AddMinutes(-10)), Pid = me.Id });
            // No known process: only 12 quiet hours take it away.
            Write(bridge, new ClaudeSessionFile { Id = "no-pid-quiet", State = ClaudeState.Done, Since = ClaudeBridge.Epoch(real.AddHours(-13)), LastEvent = ClaudeBridge.Epoch(real.AddHours(-13)) });
            Write(bridge, new ClaudeSessionFile { Id = "no-pid-recent", State = ClaudeState.Done, Since = ClaudeBridge.Epoch(real.AddHours(-11)), LastEvent = ClaudeBridge.Epoch(real.AddHours(-11)) });
            var service = new ClaudeService(bridge, Fresh(directory, "alive-config"), appThreads: new ClaudeAppThreads(() => new string[0]));
            service.ReadSessions(real, true);
            Func<string, bool> kept = id => File.Exists(ClaudeBridge.SessionPath(bridge, id));
            Assert(kept("renamed-exe") && kept("no-pid-recent"), "A running session (or one with no known process, quiet under 12 hours) was dropped.");
            Assert(!kept("exited") && !kept("reused") && !kept("no-pid-quiet"), "A session whose process exited, whose id was reused, or with no process for 12 hours stayed.");
            // A tick later the running one is still there.
            service.ReadSessions(real.AddSeconds(5), true);
            Assert(kept("renamed-exe") && service.View(real.AddSeconds(5), null).Sessions.Any(s => s.Id == "renamed-exe"), "A running session didn't survive a tick.");
            Assert(ClaudeProcesses.Alive(me.Id) && ClaudeProcesses.Alive(me.Id, real) && !ClaudeProcesses.Alive(me.Id, started.AddMinutes(-10)) && ClaudeProcesses.Alive(0), "Alive doesn't follow the process.");
        });

        check("Claude bridge reads every hook event Claude Code documents (one fixture each)", () => {
            string bridge = Fresh(directory, "events");
            string path = ClaudeBridge.SessionPath(bridge, "ev-1");
            Func<string, string, string> ev = (name, rest) => "{\"session_id\":\"ev-1\",\"transcript_path\":\"C:/Users/a/.claude/projects/x/ev-1.jsonl\",\"cwd\":\"C:\\\\Code\\\\api\",\"permission_mode\":\"default\",\"hook_event_name\":\"" + name + "\"" + rest + "}";
            Action<string, string, int> fire = (name, rest, seconds) => ClaudeBridge.Hook(ev(name, rest), bridge, null, Now.AddSeconds(seconds));
            Func<ClaudeSessionFile> read = () => ClaudeBridge.Read(path);
            fire("UserPromptSubmit", ",\"prompt\":\"Write a function\"", 0);
            // StopFailure: the turn ended on an API error. Stuck, with the reason, until the next turn.
            fire("StopFailure", ",\"error\":\"rate_limit\",\"error_details\":\"429 Too Many Requests\",\"last_assistant_message\":\"API Error: Rate limit reached\"", 5);
            var s = read();
            Assert(s.State == ClaudeState.Stuck && s.Error == "rate_limit" && s.ErrorText == "429 Too Many Requests" && s.Since == ClaudeBridge.Epoch(Now.AddSeconds(5)), "StopFailure didn't make the session Stuck with its reason.");
            fire("Notification", ",\"message\":\"Claude is waiting for your input\",\"notification_type\":\"idle_prompt\"", 70);
            Assert(read().State == ClaudeState.Stuck && read().Error == "rate_limit", "An idle prompt undid Stuck.");
            fire("UserPromptSubmit", ",\"prompt\":\"try again\"", 80);
            Assert(read().State == ClaudeState.Working && read().Error == "" && read().ErrorText == "", "The next prompt didn't clear Stuck.");
            // PostToolUseFailure: counted, with the tool and the error's first line; an interrupt isn't a failure.
            fire("PostToolUseFailure", ",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"npm test\"},\"tool_use_id\":\"toolu_01ABC\",\"error\":\"Command exited with non-zero status code 1\\nFAIL src/a.test.js\",\"is_interrupt\":false,\"duration_ms\":4187", 85);
            fire("PostToolUseFailure", ",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"sleep 60\"},\"tool_use_id\":\"toolu_01ABD\",\"error\":\"Interrupted\",\"is_interrupt\":true", 86);
            s = read();
            Assert(s.Failed == 1 && s.LastError == "Bash: Command exited with non-zero status code 1" && s.State == ClaudeState.Working, "PostToolUseFailure wasn't counted once with its first line: " + s.Failed + " " + s.LastError);
            fire("PermissionDenied", ",\"tool_name\":\"Bash\",\"tool_input\":{\"command\":\"rm -rf /tmp/build\"},\"tool_use_id\":\"toolu_01ABE\",\"reason\":\"Auto mode denied: command targets a path outside the project\"", 87);
            Assert(read().Denied == 1 && read().State == ClaudeState.Working, "PermissionDenied wasn't counted.");
            // Subagents: SubagentStart adds, SubagentStop takes away only ids SubagentStart named.
            fire("SubagentStart", ",\"agent_id\":\"agent-abc123\",\"agent_type\":\"Explore\"", 88);
            fire("SubagentStart", ",\"agent_id\":\"agent-def456\",\"agent_type\":\"general-purpose\"", 89);
            fire("SubagentStop", ",\"stop_hook_active\":false,\"agent_id\":\"prompt-suggestion-1\",\"agent_type\":\"prompt_suggestion\",\"agent_transcript_path\":\"x.jsonl\",\"last_assistant_message\":\"\"", 90);
            Assert(read().Workers.Count == 2, "An internal agent's SubagentStop took a worker away.");
            fire("SubagentStop", ",\"stop_hook_active\":false,\"agent_id\":\"agent-abc123\",\"agent_type\":\"Explore\",\"agent_transcript_path\":\"x.jsonl\",\"last_assistant_message\":\"Found it\"", 91);
            Assert(read().Workers.SequenceEqual(new[] { "agent-def456" }), "SubagentStop didn't take its worker away.");
            // Tasks: created and completed.
            fire("TaskCreated", ",\"task_id\":\"task-001\",\"task_subject\":\"Implement user authentication\",\"task_description\":\"Add login and signup endpoints\"", 92);
            fire("TaskCreated", ",\"task_id\":\"task-002\",\"task_subject\":\"Write tests\"", 93);
            fire("TaskCompleted", ",\"task_id\":\"task-001\",\"task_subject\":\"Implement user authentication\"", 94);
            Assert(read().Tasks.Count == 2 && read().OpenTasks == 1, "Tasks weren't counted.");
            // PostModelSwitch keeps Claude Code's model name; the card shows it by name.
            fire("PostModelSwitch", ",\"from_model\":\"claude-sonnet-5-5\",\"to_model\":\"claude-opus-5-5\",\"requested_model\":\"opus\",\"source\":\"user\"", 95);
            Assert(read().Model == "claude-opus-5-5" && ClaudeService.ModelName(read().Model) == "Opus 5.5", "PostModelSwitch didn't record the model.");
            // CwdChanged follows the shell; the project stays where the session started.
            fire("CwdChanged", ",\"old_cwd\":\"C:\\\\Code\\\\api\",\"new_cwd\":\"C:\\\\Code\\\\api\\\\.worktrees\\\\fix\"", 96);
            Assert(read().Cwd == "C:\\Code\\api\\.worktrees\\fix" && read().Project == "C:\\Code\\api", "CwdChanged wasn't recorded apart from the project.");
            // PreCompact puts the car on the jack; PostCompact takes it off.
            fire("PreCompact", ",\"trigger\":\"auto\",\"custom_instructions\":\"\"", 97);
            Assert(read().Compacting, "PreCompact didn't start compacting.");
            fire("PostCompact", ",\"trigger\":\"auto\",\"compact_summary\":\"Summary of the conversation\"", 98);
            Assert(!read().Compacting && read().State == ClaudeState.Working, "PostCompact didn't end compacting.");
            // Stop with only a shell in flight: no subagent runs any more.
            fire("Stop", ",\"stop_hook_active\":false,\"last_assistant_message\":\"Done.\",\"background_tasks\":[{\"id\":\"task-1\",\"type\":\"shell\",\"status\":\"running\",\"description\":\"tail logs\",\"command\":\"tail -f x\"}],\"session_crons\":[]", 100);
            Assert(read().State == ClaudeState.Done && read().Workers.Count == 0, "Stop didn't finish the turn or clear the workers.");
            // The notification types that change a session.
            fire("Notification", ",\"message\":\"Open this link to sign in\",\"notification_type\":\"elicitation_url_dialog\"", 110);
            Assert(read().State == ClaudeState.NeedsYou, "elicitation_url_dialog isn't Needs you.");
            fire("Notification", ",\"message\":\"A background session needs your input\",\"notification_type\":\"agent_needs_input\"", 120);
            Assert(read().State == ClaudeState.NeedsYou && read().Message == "A background session needs your input", "agent_needs_input isn't Needs you.");
            fire("Notification", ",\"message\":\"A background session finished\",\"notification_type\":\"agent_completed\"", 130);
            Assert(read().State == ClaudeState.Done && read().Message == "", "agent_completed isn't Done.");
            fire("Notification", ",\"message\":\"Stopped waiting for the usage limit\",\"notification_type\":\"quota_auto_resume_disabled\"", 140);
            Assert(read().State == ClaudeState.Stuck && read().Error == ClaudeBridge.QuotaDisabled, "quota_auto_resume_disabled isn't Stuck.");
            fire("Notification", ",\"message\":\"Continuing\",\"notification_type\":\"quota_auto_resume_fired\"", 150);
            Assert(read().State == ClaudeState.Working && read().Error == "", "quota_auto_resume_fired didn't carry on working.");
            fire("Notification", ",\"message\":\"\",\"notification_type\":\"quota_auto_resume_stale\"", 160);
            Assert(read().State == ClaudeState.NeedsYou && read().Message == ClaudeBridge.QuotaStale, "quota_auto_resume_stale doesn't ask for Enter.");
            // SessionEnd: /clear, an exit at the prompt and a logout keep the thread, ended (package 3: resolved); any other
            // reason takes the file away.
            fire("SessionEnd", ",\"reason\":\"clear\"", 170);
            s = read();
            Assert(s != null && s.Ended && s.EndReason == "clear" && s.State == ClaudeState.Ready && s.Workers.Count == 0, "SessionEnd after /clear didn't keep the thread, ended.");
            var service = new ClaudeService(bridge, Fresh(directory, "events-config"), appThreads: new ClaudeAppThreads(() => new string[0]));
            service.ReadSessions(Now.AddSeconds(171), false);
            var view = service.View(Now.AddSeconds(171), null);
            Assert(!view.Sessions.Any(x => x.Id == "ev-1") && view.Threads.Any(x => x.Id == "ev-1" && !x.Running && x.Pit == PitState.Resolved), "A cleared session still shows as running, or isn't parked.");
            fire("SessionEnd", ",\"reason\":\"prompt_input_exit\"", 180);
            Assert(File.Exists(path) && read().EndReason == "prompt_input_exit", "An exit at the prompt didn't keep the session, ended.");
            fire("SessionEnd", ",\"reason\":\"other\"", 190);
            Assert(!File.Exists(path), "SessionEnd didn't remove the session.");
            // The connector registers all of them, and Notification for exactly these types.
            foreach (var name in new[] { "StopFailure", "PostToolUseFailure", "PostCompact", "PermissionDenied", "SubagentStart", "SubagentStop", "TaskCreated", "TaskCompleted", "PostModelSwitch", "CwdChanged" })
                Assert(ClaudeConnector.Events.Any(e => e[0] == name), name + " isn't registered.");
            foreach (var type in new[] { "permission_prompt", "elicitation_dialog", "elicitation_url_dialog", "agent_needs_input", "agent_completed", "quota_auto_resume_fired", "quota_auto_resume_stale", "quota_auto_resume_disabled" })
                Assert(ClaudeConnector.NotificationTypes.Split('|').Contains(type), type + " isn't in the Notification matcher.");
            Assert(!ClaudeConnector.NotificationTypes.Contains("idle_prompt") && !ClaudeConnector.Events.Any(e => e[0] == "PreModelSwitch"), "idle_prompt or PreModelSwitch is registered.");
        });

        check("Claude Stuck persists past a minute, has its reason, and clears on the next prompt", () => {
            string bridge = Fresh(directory, "stuck"), config = Fresh(directory, "stuck-config");
            ClaudeBridge.Hook("{\"session_id\":\"st-1\",\"cwd\":\"C:\\\\Code\\\\api\",\"hook_event_name\":\"StopFailure\",\"error\":\"rate_limit\",\"error_details\":\"429\"}", bridge, null, Now.AddMinutes(-5));
            // Claude Code's own list says idle after the failure: that doesn't undo Stuck.
            string sessions = Path.Combine(config, "sessions"); Directory.CreateDirectory(sessions);
            File.WriteAllText(Path.Combine(sessions, "4242.json"), "{\"pid\":4242,\"sessionId\":\"st-1\",\"cwd\":\"C:\\\\Code\\\\api\",\"status\":\"idle\",\"entrypoint\":\"claude-cursor\",\"startedAt\":" + ClaudeBridge.Epoch(Now.AddHours(-1)) + ",\"statusUpdatedAt\":" + ClaudeBridge.Epoch(Now.AddMinutes(-4)) + "}");
            var service = new ClaudeService(bridge, config, appThreads: new ClaudeAppThreads(() => new string[0])) { RegistryAlive = pid => pid == 4242 };
            service.ReadSessions(Now, false);
            var view = service.View(Now, null);
            var t = view.Sessions.Single(x => x.Id == "st-1");
            Assert(t.State == ClaudeState.Stuck && t.Pit == PitState.Stuck && t.Reason == "rate_limit" && t.Where == "Cursor", "Stuck didn't last five minutes past the error, or lost its reason.");
            Assert(ClaudeText.Chip(t) == "Stuck" && ClaudeText.State(ClaudeState.Stuck) == "Stuck" && ClaudeText.Summary(view, ClaudePeriod.Today, false) == "1 stuck", "Stuck isn't named on the card: " + ClaudeText.Summary(view, ClaudePeriod.Today, false));
            view.Limits = new ClaudeLimits { FiveHour = new ClaudeLimitWindow { Percent = 100, ResetsAt = Now.AddHours(2) }, UpdatedAt = Now };
            Assert(ClaudeText.StuckReason(t, view, Now, true) == "Rate limited, resets " + ClaudeText.When(Now.AddHours(2), Now, true), "The rate limit's reset isn't named: " + ClaudeText.StuckReason(t, view, Now, true));
            Func<string, string> reason = error => ClaudeText.StuckReason(new ClaudeSession { Reason = error }, null, Now, true);
            Assert(reason("overloaded") == "Overloaded, try again" && reason("authentication_failed") == "Sign in again" && reason("max_output_tokens") == "Reply too long" && reason(ClaudeBridge.QuotaDisabled) == "Stopped waiting for the usage limit" && reason("something_new") == "API error", "Stuck reasons are worded wrongly.");
            // A working car spinning after a telemetry API error is only retrying.
            var retrying = new ClaudeSession { Running = true, State = ClaudeState.Working, Since = Now.AddMinutes(-1) };
            retrying.Pit = ClaudePace.State(retrying, Now, DateTime.MinValue, DateTime.MinValue, Now.AddSeconds(-10), Now.AddSeconds(-20));
            Assert(retrying.Pit == PitState.Stuck && ClaudeText.Chip(retrying) == "Retrying", "A retry reads as Stuck.");
            Assert(ClaudeView.Rank(ClaudeState.NeedsYou) < ClaudeView.Rank(ClaudeState.Stuck) && ClaudeView.Rank(ClaudeState.Stuck) < ClaudeView.Rank(ClaudeState.Working), "Stuck isn't ranked after Needs you and before Working.");
            ClaudeBridge.Hook("{\"session_id\":\"st-1\",\"cwd\":\"C:\\\\Code\\\\api\",\"hook_event_name\":\"UserPromptSubmit\",\"prompt\":\"again\"}", bridge, null, Now.AddSeconds(5));
            service.ReadSessions(Now.AddSeconds(6), false);
            t = service.View(Now.AddSeconds(6), null).Sessions.Single(x => x.Id == "st-1");
            Assert(t.State == ClaudeState.Working && t.Reason == "" && t.Pit == PitState.Work, "The next prompt didn't clear Stuck.");
            Assert(ClaudeRegistry.Where("sdk-py") == "Agent SDK (Python)" && ClaudeRegistry.Where("claude-jetbrains") == "JetBrains" && ClaudeRegistry.Where("claude-zed") == "Zed" && ClaudeRegistry.Where("sdk-ts") == "Agent SDK (TypeScript)" && ClaudeRegistry.Where("claude-new-place") == "claude-new-place" && ClaudeRegistry.Where(null) == "", "Entrypoints aren't named.");
        });

        check("Claude sessions only Claude Code's list or its logs know show as running", () => {
            string bridge = Fresh(directory, "seed"), config = Fresh(directory, "seed-config");
            string sessions = Path.Combine(config, "sessions"); Directory.CreateDirectory(sessions);
            File.WriteAllText(Path.Combine(sessions, "5151.json"), "{\"pid\":5151,\"sessionId\":\"listed-only\",\"cwd\":\"C:\\\\Code\\\\desk\",\"status\":\"idle\",\"entrypoint\":\"cli\",\"startedAt\":" + ClaudeBridge.Epoch(Now.AddMinutes(-30)) + ",\"statusUpdatedAt\":" + ClaudeBridge.Epoch(Now.AddMinutes(-30)) + "}");
            string logs = Path.Combine(config, "projects", "C--Code-quiet"); Directory.CreateDirectory(logs);
            Func<string, DateTime, string> line = (session, at) => "{\"type\":\"assistant\",\"sessionId\":\"" + session + "\",\"cwd\":\"C:\\\\Code\\\\quiet\",\"timestamp\":\"" + at.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture) + "\",\"requestId\":\"req_" + session + "\",\"message\":{\"id\":\"msg_" + session + "\",\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";
            File.WriteAllText(Path.Combine(logs, "logs-only.jsonl"), line("logs-only", Now.AddSeconds(-30)) + "\n");
            File.WriteAllText(Path.Combine(logs, "logs-old.jsonl"), line("logs-old", Now.AddMinutes(-20)) + "\n");
            File.WriteAllText(Path.Combine(logs, "hook-gone.jsonl"), line("hook-gone", Now.AddSeconds(-20)) + "\n");
            // Claude Code touches a log long after its last reply: the reply's own time decides, not the file's.
            File.SetLastWriteTimeUtc(Path.Combine(logs, "logs-only.jsonl"), Now.AddSeconds(-30));
            File.SetLastWriteTimeUtc(Path.Combine(logs, "logs-old.jsonl"), Now.AddSeconds(-5));
            File.SetLastWriteTimeUtc(Path.Combine(logs, "hook-gone.jsonl"), Now.AddSeconds(-20));
            var service = new ClaudeService(bridge, config, appThreads: new ClaudeAppThreads(() => new string[0])) { RegistryAlive = pid => pid == 5151 };
            // A session whose hook file went (its SessionEnd) isn't brought back by its logs.
            Write(bridge, new ClaudeSessionFile { Id = "hook-gone", State = ClaudeState.Done, Since = ClaudeBridge.Epoch(Now.AddSeconds(-20)), LastEvent = ClaudeBridge.Epoch(Now.AddSeconds(-20)) });
            service.ReadSessions(Now, false);
            File.Delete(ClaudeBridge.SessionPath(bridge, "hook-gone"));
            service.ReadSessions(Now, false);
            service.Usage.Scan(Now);
            var view = service.View(Now, service.Usage.Entries);
            Func<string, ClaudeSession> get = id => view.Threads.FirstOrDefault(x => x.Id == id);
            Assert(get("listed-only") != null && get("listed-only").Running && view.Sessions.Any(x => x.Id == "listed-only"), "A session only Claude Code's list knows doesn't show.");
            Assert(get("logs-only") != null && get("logs-only").Running && get("logs-only").State == ClaudeState.Working, "A session only the logs know, active seconds ago, isn't running.");
            Assert(get("logs-old") != null && !get("logs-old").Running && get("hook-gone") != null && !get("hook-gone").Running, "An old or ended session shows as running.");
        });

        check("Claude plan check: 403 means no meters and keeps the sign-in; only a failed refresh ends it", () => {
            string folder = Fresh(directory, "meters"), path = Path.Combine(folder, "claude-session.dat");
            new ClaudeSignIn { AccessToken = "access", RefreshToken = "refresh", ExpiresUtc = DateTime.UtcNow.AddHours(1) }.Save(path);
            var forbidden = new Handler { Send = request => Reply(HttpStatusCode.Forbidden, "{\"error\":{\"type\":\"permission_error\"}}") };
            var service = new ClaudeService(Fresh(directory, "meters-bridge"), Fresh(directory, "meters-config"), account: new ClaudeAccount(path, forbidden, url => { }));
            Assert(service.CheckOnlineAsync(Now).GetAwaiter().GetResult() && service.Account.NoMeters && service.Account.SignedIn && !service.Account.Expired && !ClaudeSignIn.Load(path).Expired, "A 403 ended the sign-in.");
            Assert(service.Plan == ClaudePlanState.NoMeters && ClaudeText.PlanName(service.Plan) == "noMeters", "A 403 isn't the no-meters state.");
            var view = new ClaudeView { Plan = service.Plan };
            Assert(ClaudeText.LimitsMissing(view) == "Your plan has no usage meters.", "The plan row doesn't say there are no meters.");
            Assert(forbidden.Hosts.All(h => h == "api.anthropic.com" || h == "platform.claude.com"), "A token went to another host.");
            service.Dispose();
            // A 401 after a refresh that worked is a failed check, not an ended sign-in.
            new ClaudeSignIn { AccessToken = "access", RefreshToken = "refresh", ExpiresUtc = DateTime.UtcNow.AddHours(1) }.Save(path);
            var unauthorized = new Handler { Send = request => request.RequestUri.AbsoluteUri == ClaudeAccount.TokenUrl ? Reply(HttpStatusCode.OK, "{\"access_token\":\"new\",\"refresh_token\":\"refresh-2\",\"expires_in\":3600}") : Reply(HttpStatusCode.Unauthorized, "{}") };
            using (var account = new ClaudeAccount(path, unauthorized, url => { }))
            {
                bool thrown = false;
                try { account.UsageAsync(Now).GetAwaiter().GetResult(); } catch (ClaudeHttpException e) { thrown = e.Status == 401; }
                Assert(thrown && account.SignedIn && !account.Expired, "A 401 after a working refresh ended the sign-in.");
            }
            // The newer usage fields.
            var limits = ClaudeBridge.ParseUsageResponse("{\"five_hour\":{\"utilization\":12,\"resets_at\":\"2026-11-01T15:00:00Z\"},\"seven_day\":{\"utilization\":40,\"resets_at\":\"2026-11-06T04:00:00Z\"},"
                + "\"seven_day_cowork\":{\"utilization\":7,\"resets_at\":\"2026-11-06T04:00:00Z\"},\"seven_day_oauth_apps\":{\"utilization\":3,\"resets_at\":null},"
                + "\"extra_usage\":{\"is_enabled\":true,\"monthly_limit\":50,\"used_credits\":12.5,\"utilization\":25}}", Now);
            Assert(limits.Extra.Any(p => p.Key == "Cowork week" && p.Value.Percent == 7) && limits.Extra.Any(p => p.Key == "Apps week" && p.Value.Percent == 3), "seven_day_cowork or seven_day_oauth_apps wasn't read.");
            Assert(limits.ExtraUsage != null && limits.ExtraUsage.Enabled && limits.ExtraUsage.Limit == 50 && limits.ExtraUsage.Used == 12.5 && limits.ExtraUsage.Percent == 25, "extra_usage wasn't read.");
            var off = ClaudeBridge.ParseUsageResponse("{\"five_hour\":{\"utilization\":1},\"extra_usage\":{\"is_enabled\":false,\"monthly_limit\":null,\"used_credits\":null,\"utilization\":null}}", Now);
            Assert(off.ExtraUsage != null && !off.ExtraUsage.Enabled && Double.IsNaN(off.ExtraUsage.Percent), "Extra usage that's off wasn't read.");
        });

        check("Claude Connect binds a free telemetry port and writes it; a busy port shows a warning", () => {
            string config = Fresh(directory, "port-config"), bridge = Fresh(directory, "port-bridge");
            string hook = Path.Combine(directory, ClaudeBridge.HookExe);
            var blocker = new TcpListener(IPAddress.Loopback, 0); blocker.Start();
            int busy = ((IPEndPoint)blocker.LocalEndpoint).Port;
            TcpListener second = null;
            try
            {
                using (var service = new ClaudeService(bridge, config, telemetryPort: busy, appThreads: new ClaudeAppThreads(() => new string[0])))
                {
                    service.Connect(hook);
                    service.CheckConnected();
                    int bound = service.Telemetry.Port;
                    Assert(service.Telemetry.Running && bound != busy && bound > busy && bound < busy + ClaudeTelemetry.PortsTried, "Connect didn't move to a free port: " + bound);
                    Assert(service.Connector.ConfiguredPort() == bound && !service.Connector.NeedsUpdate(), "Connect didn't write the port it bound.");
                    var env = ClaudeJson.Obj(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(service.Connector.SettingsPath))), "env");
                    Assert(ClaudeJson.Str(env, "OTEL_EXPORTER_OTLP_ENDPOINT") == "http://127.0.0.1:" + bound && ClaudeJson.Str(env, "OTEL_METRICS_INCLUDE_ENTRYPOINT") == "true", "The endpoint or the entrypoint setting is wrong.");
                    Assert(service.TelemetryWarning() == "", "A working receiver has a warning.");
                    // Something else takes the port while the receiver is stopped: the card says telemetry is off.
                    service.Telemetry.Stop();
                    second = new TcpListener(IPAddress.Loopback, bound); second.Start();
                    Assert(!service.StartTelemetry() && service.TelemetryWarning() == "Telemetry off: port " + bound + " in use", "A busy port gives no warning: " + service.TelemetryWarning());
                    Assert(service.View(Now, null).TelemetryWarning == "Telemetry off: port " + bound + " in use", "The card doesn't get the warning.");
                    second.Stop(); second = null;
                    Assert(service.StartTelemetry() && service.Telemetry.Port == bound && service.TelemetryWarning() == "", "The receiver didn't come back on its port.");
                    service.Connector.Disconnect();
                    Assert(!File.ReadAllText(service.Connector.SettingsPath).Contains("OTEL_"), "Disconnect left telemetry settings behind.");
                }
            }
            finally { blocker.Stop(); if (second != null) second.Stop(); }
            // An older connection: a missing event, or the old Notification matcher, asks for an update.
            string older = Fresh(directory, "port-older");
            var connector = new ClaudeConnector(older, Fresh(directory, "port-older-bridge"));
            connector.Connect(hook);
            Assert(!connector.NeedsUpdate(), "A fresh connection asks for an update.");
            var root = (JsonObject)ClaudeJson.Parse(File.ReadAllText(connector.SettingsPath));
            var hooks = ClaudeJson.Obj(root, "hooks");
            ClaudeJson.Obj(((List<object>)hooks.Get("Notification"))[0])["matcher"] = "permission_prompt|elicitation_dialog";
            File.WriteAllText(connector.SettingsPath, ClaudeJson.Write(root));
            Assert(connector.NeedsUpdate(), "The old Notification matcher doesn't ask for an update.");
            connector.Connect(hook);
            root = (JsonObject)ClaudeJson.Parse(File.ReadAllText(connector.SettingsPath));
            ClaudeJson.Obj(root, "hooks").Remove("StopFailure");
            File.WriteAllText(connector.SettingsPath, ClaudeJson.Write(root));
            Assert(connector.NeedsUpdate(), "A missing StopFailure hook doesn't ask for an update.");
            Assert(ClaudeConnector.PortOf("http://127.0.0.1:43833") == 43833 && ClaudeConnector.PortOf("http://collector:4318") == 0 && ClaudeConnector.PortOf(null) == 0, "Ports aren't read from endpoints.");
        });

        check("Claude context rings: 60, 80 and 95%, with the card's words", () => {
            Assert(ClaudePace.ContextLevel(Double.NaN) == 0 && ClaudePace.ContextLevel(59.9) == 0 && ClaudePace.ContextLevel(60) == 1 && ClaudePace.ContextLevel(79.9) == 1 && ClaudePace.ContextLevel(80) == 2 && ClaudePace.ContextLevel(95) == 3 && ClaudePace.ContextLevel(100) == 3, "Context levels wrong.");
            Assert(ClaudeText.ContextTip(82) == "Context 82% used, compaction soon" && ClaudeText.ContextTip(64) == "Context 64% used" && ClaudeText.ContextTip(97) == "Context 97% used, compaction next" && ClaudeText.ContextTip(30) == "", "Context words wrong.");
            // The status line's context reaches the card's thread.
            string bridge = Fresh(directory, "context");
            ClaudeBridge.StatusLine("{\"session_id\":\"cx\",\"cwd\":\"C:/Code/api\",\"model\":{\"display_name\":\"Opus 5.5\"},\"context_window\":{\"used_percentage\":82.4}}", bridge, null, Now);
            var service = new ClaudeService(bridge, Fresh(directory, "context-config"), appThreads: new ClaudeAppThreads(() => new string[0]));
            service.ReadSessions(Now, false);
            var t = service.View(Now, null).Sessions.Single(x => x.Id == "cx");
            Assert(Math.Round(t.Context) == 82 && ClaudeText.ThreadTip(t, ClaudePeriod.Today, Now, true, false).Contains("Context 82% used, compaction soon"), "The thread's context didn't reach the card.");
            var sample = ClaudeView.Fixture(Now, false);
            Assert(sample.Threads.Any(x => ClaudePace.ContextLevel(x.Context) == 2), "The labelled sample has no context ring to render.");
        });
    }
}
