using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using WidgetScreen;

// Package 3 of the watcher plan, "Instant and everywhere" (decision 025): folder watchers instead of the 2-second look,
// the project groups worked out once per view, alerts for Windows notifications and the phone, helpers and tasks on
// the car, sessions resolved without the Claude app, worktree garages and managed settings. Everything runs in
// throwaway folders; Claude Code's real folder, the Claude app's records and the PC's managed settings are never read.
internal static class ClaudeInstantTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-instant", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static void Fire(string bridge, string id, string name, string extra, DateTime at)
    {
        ClaudeBridge.Hook("{\"session_id\":\"" + id + "\",\"hook_event_name\":\"" + name + "\",\"cwd\":\"C:/Code/api\"" + extra + "}", bridge, null, at);
    }
    // Waits up to five seconds for a condition a watcher thread sets.
    private static bool Soon(Func<bool> done)
    {
        for (int i = 0; i < 100; i++) { if (done()) return true; Thread.Sleep(50); }
        return false;
    }
    private static ClaudeSession Running(string id, ClaudeState state, DateTime since, string title = "Fix it")
    {
        return new ClaudeSession { Id = id, Running = true, State = state, Since = since, ProjectName = "api", Title = title };
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude watchers turn a burst of written files into one look, by kind", () => {
            string bridge = Fresh(directory, "watch-bridge"), logs = Fresh(directory, "watch-logs");
            Directory.CreateDirectory(Path.Combine(bridge, "sessions")); Directory.CreateDirectory(Path.Combine(logs, "C--Code-api"));
            int calls = 0;
            using (var watch = new ClaudeWatch(() => Interlocked.Increment(ref calls)))
            {
                watch.Watch(new[] { Tuple.Create(bridge, ClaudeWatch.Kinds.Sessions, true), Tuple.Create(logs, ClaudeWatch.Kinds.Logs, true), Tuple.Create(Path.Combine(directory, "claude-instant", "missing"), ClaudeWatch.Kinds.Sessions, false) });
                Assert(watch.Running && watch.Folders == 2, "The existing folders aren't watched, or a missing one is: " + watch.Folders);
                for (int i = 0; i < 3; i++) File.WriteAllText(Path.Combine(bridge, "sessions", "s" + i + ".json"), "{}");
                Assert(Soon(() => calls >= 1), "A written file didn't ask for a look.");
                Thread.Sleep(300);
                Assert(calls == 1, "A burst of writes asked for " + calls + " looks, not one.");
                var kinds = watch.Take();
                Assert(kinds == ClaudeWatch.Kinds.Sessions && watch.Take() == ClaudeWatch.Kinds.None, "The look didn't say sessions changed: " + kinds);
                // A log line in a project's folder (a subfolder) is a log change, and asks again once taken.
                File.AppendAllText(Path.Combine(logs, "C--Code-api", "a.jsonl"), "{}\n");
                Assert(Soon(() => calls >= 2), "A log line didn't ask for a look.");
                Assert(Soon(() => (watch.Take() & ClaudeWatch.Kinds.Logs) != 0), "The log change wasn't marked as one.");
                // Telemetry pokes the same way (late file events drained first).
                Thread.Sleep(300); watch.Take();
                int before = calls;
                watch.Poke(ClaudeWatch.Kinds.Telemetry);
                Assert(calls == before + 1 && watch.Take() == ClaudeWatch.Kinds.Telemetry, "Telemetry didn't ask for a look.");
                // The same list again keeps the watchers; a stop leaves none.
                watch.Watch(new[] { Tuple.Create(bridge, ClaudeWatch.Kinds.Sessions, true), Tuple.Create(logs, ClaudeWatch.Kinds.Logs, true) });
                Assert(watch.Folders == 2, "Watching the same folders again lost one.");
                watch.Stop();
                Assert(!watch.Running && watch.Folders == 0, "Stop left a watcher.");
            }
            // The service watches its bridge, Claude Code's session list and logs, and the Claude app's folders.
            var service = new ClaudeService(bridge, logs, appThreads: new ClaudeAppThreads(() => new string[0]));
            var folders = service.WatchFolders();
            Assert(folders.Any(f => f.Item1 == bridge && f.Item2 == ClaudeWatch.Kinds.Sessions && f.Item3) && folders.Any(f => f.Item1 == Path.Combine(logs, "projects") && f.Item2 == ClaudeWatch.Kinds.Logs)
                && folders.Any(f => f.Item1 == Path.Combine(logs, "sessions")), "The service doesn't watch its sources.");
            Assert(ClaudeWatch.Fallback == TimeSpan.FromSeconds(30), "The fallback isn't 30 seconds.");
            service.Dispose();
        });

        check("Claude project groups are worked out once per view and filter, with folds applied each time", () => {
            var view = ClaudeView.Fixture(Now, false);
            for (int i = 0; i < 5; i++) view.Groups(ClaudePeriod.Today);
            Assert(view.GroupsBuilt == 1, "Groups were worked out " + view.GroupsBuilt + " times for one period.");
            view.Groups(ClaudePeriod.Week); view.Groups(ClaudePeriod.Week);
            Assert(view.GroupsBuilt == 2, "Another period wasn't worked out once.");
            string folder = view.Groups(ClaudePeriod.Week)[0].Folder;
            view.Collapsed.Add(folder);
            Assert(view.Groups(ClaudePeriod.Week)[0].Collapsed && view.GroupsBuilt == 2, "A fold needed the groups again or didn't show.");
            view.HideIdle = true; view.Groups(ClaudePeriod.Week);
            Assert(view.GroupsBuilt == 3, "Hide idle threads didn't work the groups out again.");
            view.Threads = view.Threads.Take(3).ToList(); view.Groups(ClaudePeriod.Week);
            Assert(view.GroupsBuilt == 4, "New threads kept the old groups.");
        });

        check("Claude alerts: Needs you and Stuck once each, a long turn's end, and the last ten minutes for the phone", () => {
            var alerts = new ClaudeAlerts();
            Assert(alerts.Update(new[] { Running("a", ClaudeState.Working, Now) }, Now).Count == 0, "Working raised an alert.");
            var need = Running("a", ClaudeState.NeedsYou, Now.AddMinutes(1)); need.Message = "Claude needs your permission to use Bash";
            var fresh = alerts.Update(new[] { need }, Now.AddMinutes(1));
            Assert(fresh.Count == 1 && fresh[0].Kind == "need" && fresh[0].Seq == 1 && fresh[0].Text == need.Message && fresh[0].Title == "Fix it" && fresh[0].Project == "api", "Needs you didn't raise one alert.");
            Assert(alerts.Update(new[] { need }, Now.AddMinutes(2)).Count == 0, "Still needing you raised another alert.");
            var stuck = Running("a", ClaudeState.Stuck, Now.AddMinutes(3)); stuck.Reason = "rate_limit"; stuck.ReasonText = "Rate limited";
            fresh = alerts.Update(new[] { stuck }, Now.AddMinutes(3));
            Assert(fresh.Count == 1 && fresh[0].Kind == "stuck" && fresh[0].Text == "Rate limited", "Stuck didn't raise an alert.");
            // A turn of 12 minutes ends: an alert. One of 3 minutes: none.
            alerts.Update(new[] { Running("a", ClaudeState.Working, Now.AddMinutes(4)), Running("b", ClaudeState.Working, Now.AddMinutes(13)) }, Now.AddMinutes(4));
            alerts.Update(new[] { Running("a", ClaudeState.Working, Now.AddMinutes(10)), Running("b", ClaudeState.Working, Now.AddMinutes(13)) }, Now.AddMinutes(10));
            fresh = alerts.Update(new[] { Running("a", ClaudeState.Done, Now.AddMinutes(16)), Running("b", ClaudeState.Done, Now.AddMinutes(16)) }, Now.AddMinutes(16));
            Assert(fresh.Count == 1 && fresh[0].Kind == "done" && fresh[0].Session == "a" && fresh[0].Text == "Finished after 12 min", "A long turn's end raised the wrong alerts: " + String.Join(",", fresh.Select(f => f.Session + f.Kind)));
            // Needs you (+1 min) and Stuck (+3 min) are over ten minutes old by now: only the turn's end is left.
            Assert(alerts.Seq == 3 && alerts.Recent(Now.AddMinutes(16)).Select(a => a.Seq).SequenceEqual(new long[] { 3 }), "The phone's alerts aren't the last ten minutes, numbered: " + String.Join(",", alerts.Recent(Now.AddMinutes(16)).Select(a => a.Seq)));
            // A session that leaves and comes back needing you alerts again; an untitled one is named by its id.
            alerts.Update(new ClaudeSession[0], Now.AddMinutes(17));
            var back = Running("abcdef123", ClaudeState.NeedsYou, Now.AddMinutes(18), "");
            fresh = alerts.Update(new[] { back }, Now.AddMinutes(18));
            Assert(fresh.Count == 1 && fresh[0].Title == "Thread abcdef", "A returning or untitled session's alert is wrong.");
        });

        check("Claude helpers and tasks show on the car, and a lead with helpers out is working", () => {
            string bridge = Fresh(directory, "crew-bridge");
            Fire(bridge, "lead", "UserPromptSubmit", "", Now);
            Fire(bridge, "lead", "SubagentStart", ",\"agent_id\":\"w1\"", Now.AddSeconds(5));
            Fire(bridge, "lead", "SubagentStart", ",\"agent_id\":\"w2\"", Now.AddSeconds(6));
            for (int i = 1; i <= 5; i++) Fire(bridge, "lead", "TaskCreated", ",\"task_id\":\"t" + i + "\"", Now.AddSeconds(10 + i));
            for (int i = 1; i <= 3; i++) Fire(bridge, "lead", "TaskCompleted", ",\"task_id\":\"t" + i + "\"", Now.AddSeconds(20 + i));
            // The lead's turn ends with a helper still out in the background.
            Fire(bridge, "lead", "Stop", ",\"background_tasks\":[{\"type\":\"subagent\",\"id\":\"w2\"}]", Now.AddSeconds(30));
            Fire(bridge, "lead", "SubagentStop", ",\"agent_id\":\"w1\"", Now.AddSeconds(31));
            var service = new ClaudeService(bridge, Fresh(directory, "crew-config"), appThreads: new ClaudeAppThreads(() => new string[0]));
            service.ReadSessions(Now.AddSeconds(40), false);
            var lead = service.View(Now.AddSeconds(40), null).Threads.First(t => t.Id == "lead");
            Assert(lead.State == ClaudeState.Done && lead.Workers == 1 && lead.Pit == PitState.Work, "A lead with a helper out isn't working: " + lead.State + "/" + lead.Pit + "/" + lead.Workers);
            Assert(lead.TasksDone == 3 && lead.TasksTotal == 5 && lead.Tasks == 2 && ClaudeText.Tasks(lead) == "3 / 5" && ClaudeText.Crew(lead) == "1 helper working · tasks 3 / 5", "The task chip or crew words are wrong: " + ClaudeText.Crew(lead));
            Assert(ClaudeText.ThreadTip(lead, ClaudePeriod.Today, Now.AddSeconds(40), true, false).Contains("1 helper working · tasks 3 / 5"), "The car's tip doesn't name its crew.");
            Fire(bridge, "lead", "SubagentStop", ",\"agent_id\":\"w2\"", Now.AddSeconds(50));
            service.ReadSessions(Now.AddSeconds(60), false);
            lead = service.View(Now.AddSeconds(60), null).Threads.First(t => t.Id == "lead");
            Assert(lead.Workers == 0 && lead.Pit == PitState.Done, "With its helpers back, the lead isn't on your turn: " + lead.Pit);
            service.Dispose();
        });

        check("Claude resolves a thread Claude Code closed without the Claude app, unless a reply came after or the app says otherwise", () => {
            string bridge = Fresh(directory, "end-bridge"), apps = Fresh(directory, "end-apps");
            Fire(bridge, "quit", "SessionStart", ",\"source\":\"startup\",\"session_title\":\"Tidy the readme\"", Now);
            Assert(ClaudeBridge.Read(ClaudeBridge.SessionPath(bridge, "quit")).Title == "Tidy the readme", "SessionStart's title wasn't kept.");
            Fire(bridge, "quit", "Stop", "", Now.AddMinutes(1));
            Fire(bridge, "quit", "SessionEnd", ",\"reason\":\"prompt_input_exit\"", Now.AddMinutes(2));
            var kept = ClaudeBridge.Read(ClaudeBridge.SessionPath(bridge, "quit"));
            Assert(kept != null && kept.Ended && kept.EndReason == "prompt_input_exit", "An exit at the prompt didn't keep the thread, ended.");
            // It had a process, which has gone by the time the dashboard looks.
            kept.Pid = 424242; ClaudeBridge.WriteAtomic(ClaudeBridge.SessionPath(bridge, "quit"), ClaudeJson.Compact(kept.ToJson()));
            foreach (var pair in new[] { new[] { "out", "logout" }, new[] { "cleared", "clear" }, new[] { "other", "other" } })
            { Fire(bridge, pair[0], "UserPromptSubmit", "", Now); Fire(bridge, pair[0], "SessionEnd", ",\"reason\":\"" + pair[1] + "\"", Now.AddMinutes(2)); }
            Assert(File.Exists(ClaudeBridge.SessionPath(bridge, "out")) && File.Exists(ClaudeBridge.SessionPath(bridge, "cleared")) && !File.Exists(ClaudeBridge.SessionPath(bridge, "other")), "The wrong SessionEnd reasons keep their file.");
            // The app has a record for "out" that isn't completed: the app wins.
            File.WriteAllText(Path.Combine(apps, "local_out.json"), "{\"cliSessionId\":\"out\",\"originCwd\":\"C:/Code/api\",\"title\":\"Logout thread\",\"lastActivityAt\":" + ClaudeBridge.Epoch(Now.AddMinutes(2)) + ",\"postTurnSummary\":{\"status_category\":\"blocked\"}}");
            var service = new ClaudeService(bridge, Fresh(directory, "end-config"), appThreads: new ClaudeAppThreads(() => new[] { apps }));
            // Its process has gone: an ended file stays all the same.
            service.SessionAlive = f => false;
            DateTime at = Now.AddMinutes(5);
            service.ReadSessions(at, true);
            Assert(File.Exists(ClaudeBridge.SessionPath(bridge, "quit")), "The alive check took an ended thread's file.");
            var view = service.View(at, new List<ClaudeUsageEntry>());
            var quit = view.Threads.First(t => t.Id == "quit");
            Assert(!quit.Running && quit.Resolved && quit.Pit == PitState.Resolved && quit.Title == "Tidy the readme" && ClaudeText.ThreadTip(quit, ClaudePeriod.Today, at, true, false).Contains("Closed in Claude Code"), "An exited thread isn't resolved and named: " + quit.Pit + " " + quit.Title);
            Assert(view.Threads.First(t => t.Id == "cleared").Pit == PitState.Resolved, "/clear didn't park the thread.");
            Assert(!view.Threads.First(t => t.Id == "out").Resolved, "The app's record didn't win.");
            // A reply logged after the end (the thread was resumed elsewhere): not resolved.
            var later = new List<ClaudeUsageEntry> { new ClaudeUsageEntry { Session = "quit", At = Now.AddMinutes(9), Tokens = 10, Priced = true } };
            Assert(!service.View(Now.AddMinutes(10), later).Threads.First(t => t.Id == "quit").Resolved, "A reply after the end left the thread resolved.");
            // Picked up again with a new SessionStart: running, not ended.
            Fire(bridge, "quit", "SessionStart", ",\"source\":\"resume\"", Now.AddMinutes(11));
            service.SessionAlive = f => true;
            service.ReadSessions(Now.AddMinutes(11), false);
            var resumed = service.View(Now.AddMinutes(11), new List<ClaudeUsageEntry>()).Threads.First(t => t.Id == "quit");
            Assert(resumed.Running && !resumed.Resolved, "A resumed thread stayed ended.");
            service.Dispose();
        });

        check("Claude puts a --worktree session in the garage of the folder its worktree came from", () => {
            string bridge = Fresh(directory, "tree-bridge");
            string input = "{\"session_id\":\"wt\",\"cwd\":\"D:/Elsewhere/fix-login\",\"workspace\":{\"project_dir\":\"D:/Elsewhere/fix-login\"},\"worktree\":{\"name\":\"fix-login\",\"branch\":\"fix/login\",\"path\":\"D:/Elsewhere/fix-login\",\"original_cwd\":\"C:\\\\Code\\\\api\"}}";
            ClaudeBridge.StatusLine(input, bridge, null, Now);
            var file = ClaudeBridge.Read(ClaudeBridge.SessionPath(bridge, "wt"));
            Assert(file.WorktreeOrigin == "C:\\Code\\api", "The worktree's original folder wasn't kept: " + file.WorktreeOrigin);
            var service = new ClaudeService(bridge, Fresh(directory, "tree-config"), appThreads: new ClaudeAppThreads(() => new string[0]));
            service.ReadSessions(Now, false);
            var thread = service.View(Now, null).Threads.First(t => t.Id == "wt");
            Assert(thread.Project == ClaudeUsage.Key("C:\\Code\\api") && thread.ProjectName == "api", "The worktree session isn't in its folder's garage: " + thread.Project);
            service.Dispose();
        });

        check("Claude says what an organisation's managed settings override, and reads no real managed file in checks", () => {
            string folder = Fresh(directory, "managed"), path = Path.Combine(folder, "managed-settings.json");
            string before = Environment.GetEnvironmentVariable(ClaudeConnector.ManagedVariable);
            try
            {
                var connector = new ClaudeConnector(Fresh(directory, "managed-config"), Fresh(directory, "managed-bridge"));
                Environment.SetEnvironmentVariable(ClaudeConnector.ManagedVariable, path);
                Assert(connector.ManagedPath == "" && connector.ManagedOverrides().Count == 0, "A missing managed file overrides something.");
                File.WriteAllText(path, "{\"disableAllHooks\":true}");
                Assert(connector.ManagedPath == path && connector.ManagedOverrides().SequenceEqual(new[] { "hooks", "status line" }), "disableAllHooks isn't hooks and status line: " + String.Join(",", connector.ManagedOverrides()));
                File.WriteAllText(path, "{\"allowManagedHooksOnly\":true,\"env\":{\"OTEL_EXPORTER_OTLP_ENDPOINT\":\"https://otel.example.com\"}}");
                Assert(connector.ManagedOverrides().SequenceEqual(new[] { "hooks", "telemetry" }), "Managed hooks or a managed endpoint aren't named: " + String.Join(",", connector.ManagedOverrides()));
                // The same values Widget Screen sets, or an unrelated key, override nothing.
                File.WriteAllText(path, "{\"env\":{\"CLAUDE_CODE_ENABLE_TELEMETRY\":\"1\",\"OTEL_METRICS_EXPORTER\":\"otlp\",\"OTEL_EXPORTER_OTLP_ENDPOINT\":\"http://127.0.0.1:43832\",\"OTHER\":\"x\"},\"model\":\"opus\"}");
                Assert(connector.ManagedOverrides().Count == 0, "Harmless managed settings were named: " + String.Join(",", connector.ManagedOverrides()));
                File.WriteAllText(path, "{\"statusLine\":{\"type\":\"command\",\"command\":\"x\"},\"env\":{\"OTEL_LOGS_EXPORTER\":\"none\"}}");
                Assert(connector.ManagedOverrides().SequenceEqual(new[] { "status line", "telemetry" }), "A managed status line or exporter isn't named.");
                File.WriteAllText(path, "not json");
                Assert(connector.ManagedOverrides().Count == 0, "An unreadable managed file broke the check.");
            }
            finally { Environment.SetEnvironmentVariable(ClaudeConnector.ManagedVariable, before); }
        });
    }
}
