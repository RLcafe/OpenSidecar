using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace WidgetScreen
{
    // A session's state as the card shows it (docs/design/claude-watcher.md). Stuck (decision 023): Claude Code's
    // turn ended on an API error (StopFailure) or it stopped waiting for a usage limit, until the next turn starts.
    public enum ClaudeState { Ready, Working, NeedsYou, Done, Stuck }

    // One Claude Code session, as the bridge records it in sessions\<id>.json.
    public sealed class ClaudeSessionFile
    {
        public string Id = "", Project = "", Title = "", Message = "", Model = "", Transcript = "";
        public ClaudeState State;
        public long Since, LastEvent;
        public double Context = Double.NaN;
        public int Pid, WindowPid;
        public long Window;
        // Between PreCompact and PostCompact (or the SessionStart that follows a compaction, or the next turn): the car
        // is on the jack.
        public bool Compacting;
        // Decision 023. Why the last turn got stuck: StopFailure's error ("rate_limit", "overloaded", …) or
        // "quota_disabled", with its details (200 characters at most). Kept until the next turn starts.
        public string Error = "", ErrorText = "";
        // Tool calls that failed (PostToolUseFailure) and the last one ("Bash: Exit code 1", 80 characters at most), and
        // tool calls auto mode denied (PermissionDenied).
        public int Failed, Denied;
        public string LastError = "";
        // Subagents running now (SubagentStart until SubagentStop) and the task list's tasks (TaskCreated, TaskCompleted).
        public List<string> Workers = new List<string>(), Tasks = new List<string>(), TasksDone = new List<string>();
        // The folder Claude Code's shell is in now (CwdChanged); Project stays the one the session started in.
        public string Cwd = "";
        // SessionEnd with reason "clear": the thread ended but its Claude Code process goes on with a new session.
        public bool Ended;
        public string EndReason = "";
        // Package 2: what Claude Code's status line says about the session (statusline.md "Available data"). Its own
        // cost estimate (cost.total_cost_usd, NaN until it sends one), wall-clock and API time, lines changed, the
        // context window's size and whether the last reply passed 200k tokens, the output style, the --agent name and
        // the worktree it is in.
        public double CostUsd = Double.NaN;
        public long DurationMs, ApiMs, LinesAdded, LinesRemoved, ContextSize;
        public bool Over200k;
        public string Style = "", Agent = "", Worktree = "", WorktreeBranch = "";
        public const int MostWorkers = 32, MostTasks = 64;

        public static string StateName(ClaudeState state) { return state == ClaudeState.Working ? "working" : state == ClaudeState.NeedsYou ? "needs" : state == ClaudeState.Done ? "done" : state == ClaudeState.Stuck ? "stuck" : "ready"; }
        public static ClaudeState ParseState(string name) { return name == "working" ? ClaudeState.Working : name == "needs" ? ClaudeState.NeedsYou : name == "done" ? ClaudeState.Done : name == "stuck" ? ClaudeState.Stuck : ClaudeState.Ready; }
        // Tasks created and not yet completed.
        public int OpenTasks { get { return Tasks.Count(t => !TasksDone.Contains(t)); } }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o["id"] = Id; o["state"] = StateName(State); o["since"] = new JsonNumber(Since); o["lastEvent"] = new JsonNumber(LastEvent);
            o["project"] = Project; o["title"] = Title; o["message"] = Message; o["model"] = Model; o["transcript"] = Transcript;
            if (!Double.IsNaN(Context)) o["context"] = new JsonNumber(Math.Round(Context, 1));
            o["pid"] = new JsonNumber(Pid); o["windowPid"] = new JsonNumber(WindowPid); o["window"] = new JsonNumber(Window);
            if (Compacting) o["compacting"] = true;
            if (Error.Length > 0) o["error"] = Error;
            if (ErrorText.Length > 0) o["errorText"] = ErrorText;
            if (Failed > 0) o["failed"] = new JsonNumber(Failed);
            if (Denied > 0) o["denied"] = new JsonNumber(Denied);
            if (LastError.Length > 0) o["lastError"] = LastError;
            if (Workers.Count > 0) o["workers"] = Workers.Cast<object>().ToList();
            if (Tasks.Count > 0) o["tasks"] = Tasks.Cast<object>().ToList();
            if (TasksDone.Count > 0) o["tasksDone"] = TasksDone.Cast<object>().ToList();
            if (Cwd.Length > 0) o["cwd"] = Cwd;
            if (Ended) o["ended"] = true;
            if (EndReason.Length > 0) o["endReason"] = EndReason;
            if (!Double.IsNaN(CostUsd)) o["costUsd"] = new JsonNumber(Math.Round(CostUsd, 6));
            if (DurationMs > 0) o["durationMs"] = new JsonNumber(DurationMs);
            if (ApiMs > 0) o["apiMs"] = new JsonNumber(ApiMs);
            if (LinesAdded > 0) o["linesAdded"] = new JsonNumber(LinesAdded);
            if (LinesRemoved > 0) o["linesRemoved"] = new JsonNumber(LinesRemoved);
            if (ContextSize > 0) o["contextSize"] = new JsonNumber(ContextSize);
            if (Over200k) o["over200k"] = true;
            if (Style.Length > 0) o["style"] = Style;
            if (Agent.Length > 0) o["agent"] = Agent;
            if (Worktree.Length > 0) o["worktree"] = Worktree;
            if (WorktreeBranch.Length > 0) o["worktreeBranch"] = WorktreeBranch;
            return o;
        }
        public static ClaudeSessionFile FromJson(JsonObject o)
        {
            if (o == null || String.IsNullOrEmpty(ClaudeJson.Str(o, "id"))) return null;
            var s = new ClaudeSessionFile { Id = ClaudeJson.Str(o, "id"), State = ParseState(ClaudeJson.Str(o, "state")),
                Project = ClaudeJson.Str(o, "project") ?? "", Title = ClaudeJson.Str(o, "title") ?? "", Message = ClaudeJson.Str(o, "message") ?? "",
                Model = ClaudeJson.Str(o, "model") ?? "", Transcript = ClaudeJson.Str(o, "transcript") ?? "", Context = ClaudeJson.Num(o, "context") };
            s.Since = Long(o, "since"); s.LastEvent = Long(o, "lastEvent"); s.Pid = (int)Long(o, "pid"); s.WindowPid = (int)Long(o, "windowPid"); s.Window = Long(o, "window");
            s.Compacting = ClaudeJson.Bool(o, "compacting");
            s.Error = ClaudeJson.Str(o, "error") ?? ""; s.ErrorText = ClaudeJson.Str(o, "errorText") ?? ""; s.LastError = ClaudeJson.Str(o, "lastError") ?? "";
            s.Failed = (int)Long(o, "failed"); s.Denied = (int)Long(o, "denied");
            s.Workers = Strings(o, "workers"); s.Tasks = Strings(o, "tasks"); s.TasksDone = Strings(o, "tasksDone");
            s.Cwd = ClaudeJson.Str(o, "cwd") ?? ""; s.Ended = ClaudeJson.Bool(o, "ended"); s.EndReason = ClaudeJson.Str(o, "endReason") ?? "";
            s.CostUsd = ClaudeJson.Num(o, "costUsd"); s.DurationMs = Long(o, "durationMs"); s.ApiMs = Long(o, "apiMs");
            s.LinesAdded = Long(o, "linesAdded"); s.LinesRemoved = Long(o, "linesRemoved"); s.ContextSize = Long(o, "contextSize"); s.Over200k = ClaudeJson.Bool(o, "over200k");
            s.Style = ClaudeJson.Str(o, "style") ?? ""; s.Agent = ClaudeJson.Str(o, "agent") ?? ""; s.Worktree = ClaudeJson.Str(o, "worktree") ?? ""; s.WorktreeBranch = ClaudeJson.Str(o, "worktreeBranch") ?? "";
            return s;
        }
        private static long Long(JsonObject o, string key) { double v = ClaudeJson.Num(o, key); return Double.IsNaN(v) ? 0 : (long)v; }
        private static List<string> Strings(JsonObject o, string key) { return (ClaudeJson.List(o, key) ?? new List<object>()).OfType<string>().ToList(); }
    }

    // One plan window: how much of it is used (0–100) and when it resets.
    public sealed class ClaudeLimitWindow
    {
        public double Percent;
        public DateTime? ResetsAt;
        // A window whose reset time has passed starts again at nothing.
        public bool Current(DateTime now) { return !ResetsAt.HasValue || ResetsAt.Value > now; }
    }

    // The plan's extra usage (paid credits past the limits), from the usage check's extra_usage: whether it is on, the
    // monthly limit and the credits used, and the share used (0–100, NaN when not given).
    public sealed class ClaudeExtraUsage
    {
        public bool Enabled;
        public double Limit = Double.NaN, Used = Double.NaN, Percent = Double.NaN;
    }

    // A Claude apps gateway's spend limit, from the status line's rate_limits.spend_limit (package 2): the share used
    // (0–100, more once past it), when its period resets, and the dollars used and allowed with the period's name when
    // the gateway sends them (NaN and "" when not).
    public sealed class ClaudeSpendLimit
    {
        public double Percent, UsedUsd = Double.NaN, LimitUsd = Double.NaN;
        public DateTime? ResetsAt;
        public string Period = "";
    }

    // The plan's five-hour and weekly limits, from Claude Code's status line or the usage check.
    public sealed class ClaudeLimits
    {
        // A gateway's spend limit, when Claude Code's status line reported one.
        public ClaudeSpendLimit Spend;
        // Extra usage, when the usage check reported it (decision 023).
        public ClaudeExtraUsage ExtraUsage;
        public ClaudeLimitWindow FiveHour, Week;
        // Narrower weekly windows the usage check may add ("Opus week", "Sonnet week", a model's own), for the Large panel.
        public List<KeyValuePair<string, ClaudeLimitWindow>> Extra = new List<KeyValuePair<string, ClaudeLimitWindow>>();
        public DateTime UpdatedAt;
        public string Source = "statusline";
        // When Claude Code last answered without rate limits (an API key or a logged-out session).
        public DateTime? MissingAt;
        public bool Known { get { return FiveHour != null || Week != null; } }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o["updatedAt"] = new JsonNumber(ClaudeBridge.Epoch(UpdatedAt)); o["source"] = Source;
            if (FiveHour != null) o["fiveHour"] = Window(FiveHour);
            if (Week != null) o["week"] = Window(Week);
            if (MissingAt.HasValue) o["missingAt"] = new JsonNumber(ClaudeBridge.Epoch(MissingAt.Value));
            if (Spend != null)
            {
                var spend = new JsonObject(); spend["pct"] = new JsonNumber(Math.Round(Spend.Percent, 1));
                if (Spend.ResetsAt.HasValue) spend["resetsAt"] = new JsonNumber(ClaudeBridge.Epoch(Spend.ResetsAt.Value));
                if (!Double.IsNaN(Spend.UsedUsd)) spend["usedUsd"] = new JsonNumber(Math.Round(Spend.UsedUsd, 2));
                if (!Double.IsNaN(Spend.LimitUsd)) spend["limitUsd"] = new JsonNumber(Math.Round(Spend.LimitUsd, 2));
                if (Spend.Period.Length > 0) spend["period"] = Spend.Period;
                o["spend"] = spend;
            }
            return o;
        }
        private static JsonObject Window(ClaudeLimitWindow w)
        {
            var o = new JsonObject(); o["pct"] = new JsonNumber(Math.Round(w.Percent, 1));
            if (w.ResetsAt.HasValue) o["resetsAt"] = new JsonNumber(ClaudeBridge.Epoch(w.ResetsAt.Value));
            return o;
        }
        public static ClaudeLimits FromJson(JsonObject o)
        {
            if (o == null) return null;
            var limits = new ClaudeLimits { FiveHour = ParseWindow(ClaudeJson.Obj(o, "fiveHour")), Week = ParseWindow(ClaudeJson.Obj(o, "week")), Source = ClaudeJson.Str(o, "source") ?? "statusline" };
            double updated = ClaudeJson.Num(o, "updatedAt"), missing = ClaudeJson.Num(o, "missingAt");
            limits.UpdatedAt = Double.IsNaN(updated) ? DateTime.MinValue : ClaudeBridge.FromEpoch(updated);
            if (!Double.IsNaN(missing)) limits.MissingAt = ClaudeBridge.FromEpoch(missing);
            var spend = ClaudeJson.Obj(o, "spend");
            if (spend != null && !Double.IsNaN(ClaudeJson.Num(spend, "pct")))
            {
                double resets = ClaudeJson.Num(spend, "resetsAt");
                limits.Spend = new ClaudeSpendLimit { Percent = ClaudeJson.Num(spend, "pct"), UsedUsd = ClaudeJson.Num(spend, "usedUsd"), LimitUsd = ClaudeJson.Num(spend, "limitUsd"), Period = ClaudeJson.Str(spend, "period") ?? "" };
                if (!Double.IsNaN(resets)) limits.Spend.ResetsAt = ClaudeBridge.FromEpoch(resets);
            }
            return limits;
        }
        private static ClaudeLimitWindow ParseWindow(JsonObject o)
        {
            if (o == null) return null;
            double pct = ClaudeJson.Num(o, "pct"); if (Double.IsNaN(pct)) return null;
            var w = new ClaudeLimitWindow { Percent = pct };
            double resets = ClaudeJson.Num(o, "resetsAt"); if (!Double.IsNaN(resets)) w.ResetsAt = ClaudeBridge.FromEpoch(resets);
            return w;
        }
    }

    // What ClaudeHook.exe does (docs/design/claude-watcher.md, decision 014). Claude Code runs it for its hooks
    // ("hook") and its status line ("statusline") with JSON on stdin; it writes one file per session and the plan
    // limits into its own folder, which the dashboard reads. Everything here swallows its errors: a broken widget must
    // never break Claude Code. The app shares this code for its checks and for reading the files back.
    public static class ClaudeBridge
    {
        // Every command Widget Screen adds to Claude Code's settings runs this exe (normally
        // %LOCALAPPDATA%/WidgetScreen/claude/ClaudeHook.exe), so its name finds Widget Screen's own entries again.
        public const string Marker = "ClaudeHook.exe";
        public const string HookExe = "ClaudeHook.exe";
        public static readonly TimeSpan Forgotten = TimeSpan.FromHours(12);

        public static string SessionsFolder(string bridge) { return Path.Combine(bridge, "sessions"); }
        public static string LimitsPath(string bridge) { return Path.Combine(bridge, "limits.json"); }
        public static string PreviousStatusLinePath(string bridge) { return Path.Combine(bridge, "previous-statusline.json"); }
        public static string SessionPath(string bridge, string id) { return Path.Combine(SessionsFolder(bridge), SafeName(id) + ".json"); }

        public static long Epoch(DateTime time) { return (long)Math.Round((time.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds); }
        public static DateTime FromEpoch(double ms) { return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms); }

        // A session id as a file name: letters, digits, '-' and '_' only.
        public static string SafeName(string id)
        {
            var b = new StringBuilder();
            foreach (char c in id ?? "") if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_') b.Append(c);
            string name = b.ToString();
            if (name.Length == 0) name = "session";
            return name.Length > 80 ? name.Substring(0, 80) : name;
        }

        // The project a folder belongs to, as the card names it: the last part of the folder path.
        public static string ProjectName(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) return "Claude Code";
            string trimmed = folder.Trim().TrimEnd('\\', '/');
            int cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
            string name = cut >= 0 ? trimmed.Substring(cut + 1) : trimmed;
            return name.Length == 0 ? trimmed : name;
        }

        // ----- Hook mode -----

        // Reads one hook event and updates (or removes) the session's file. owner finds the Claude Code process and
        // window; it is only asked when the session is new or its process isn't known yet.
        public static void Hook(string input, string bridge, Func<ClaudeProcesses.Owner> owner, DateTime now)
        {
            var o = ClaudeJson.Obj(ClaudeJson.Parse(input));
            string id = ClaudeJson.Str(o, "session_id"), name = ClaudeJson.Str(o, "hook_event_name") ?? "";
            if (String.IsNullOrEmpty(id)) return;
            string path = SessionPath(bridge, id);
            if (name == "SessionEnd")
            {
                // A session is alive until its own SessionEnd or its process is gone (decision 023). After /clear the
                // process goes on with a new session, so the old one is kept, marked ended, for the card's ended
                // threads; every other reason (exit, logout, resume elsewhere) takes the file away.
                string reason = ClaudeJson.Str(o, "reason") ?? "";
                WithSession(id, () => {
                    var ended = reason == "clear" ? Read(path) : null;
                    if (ended == null) { Delete(path); return; }
                    ended.Ended = true; ended.EndReason = reason; ended.State = ClaudeState.Ready; ended.Compacting = false; ended.Workers.Clear();
                    ended.LastEvent = Epoch(now);
                    WriteAtomic(path, ClaudeJson.Compact(ended.ToJson()));
                });
                return;
            }
            ClaudeState? next = null; string message = "";
            // A compaction starts with PreCompact and ends with PostCompact or the SessionStart after it; a turn's news
            // ends it too. Side events (subagents, tasks, a failed tool, a model switch) leave it as it is.
            bool? compacting = null;
            // What the event adds to the file, applied under the session's lock.
            Func<ClaudeSessionFile, bool> extra = null;
            switch (name)
            {
                case "PreCompact": compacting = true; break;
                case "PostCompact": compacting = false; break;
                case "SessionStart":
                    {
                        compacting = false;
                        if (ClaudeJson.Str(o, "source") == "compact") break;
                        next = ClaudeState.Ready;
                        extra = s => { bool had = s.Workers.Count > 0 || s.Ended; s.Workers.Clear(); s.Ended = false; s.EndReason = ""; return had; };
                        break;
                    }
                case "UserPromptSubmit": case "PostToolUse": case "PreToolUse": next = ClaudeState.Working; compacting = false; break;
                // A permission dialog is showing (the desktop app's Code tab sends no permission Notification).
                case "PermissionRequest":
                    {
                        string tool = ClaudeJson.Str(o, "tool_name") ?? "";
                        message = tool.Length > 0 ? "Claude needs your permission to use " + tool : "Claude needs your permission";
                        next = ClaudeState.NeedsYou; compacting = false;
                        break;
                    }
                case "Stop":
                    {
                        next = ClaudeState.Done; compacting = false;
                        // Stop lists what is still in flight; with no subagent among it, none is running any more.
                        var tasks = ClaudeJson.List(o, "background_tasks");
                        if (tasks != null && !tasks.OfType<JsonObject>().Any(t => (ClaudeJson.Str(t, "type") ?? "") == "subagent"))
                            extra = s => { bool had = s.Workers.Count > 0; s.Workers.Clear(); return had; };
                        break;
                    }
                // The turn ended on an API error (decision 023): Stuck with its reason until the next turn.
                case "StopFailure":
                    {
                        string error = ClaudeJson.Str(o, "error") ?? "", details = ClaudeJson.Str(o, "error_details") ?? ClaudeJson.Str(o, "last_assistant_message") ?? "";
                        next = ClaudeState.Stuck; compacting = false;
                        extra = s => Stuck(s, error.Length > 0 ? error : "unknown", details);
                        break;
                    }
                case "PostToolUseFailure":
                    {
                        // An interrupt is the user pressing Esc, not a failure.
                        if (ClaudeJson.Bool(o, "is_interrupt")) return;
                        string failure = Short((ClaudeJson.Str(o, "tool_name") ?? "Tool") + ": " + FirstLine(ClaudeJson.Str(o, "error")), 80);
                        extra = s => { s.Failed++; s.LastError = failure; return true; };
                        break;
                    }
                case "PermissionDenied": extra = s => { s.Denied++; return true; }; break;
                case "SubagentStart":
                    {
                        string agent = ClaudeJson.Str(o, "agent_id") ?? "";
                        if (agent.Length == 0) return;
                        extra = s => { if (s.Workers.Contains(agent)) return false; s.Workers.Add(agent); if (s.Workers.Count > ClaudeSessionFile.MostWorkers) s.Workers.RemoveAt(0); return true; };
                        break;
                    }
                // SubagentStop also fires for Claude Code's internal agents (prompt suggestions, /btw): only ids
                // SubagentStart named count.
                case "SubagentStop":
                    {
                        string agent = ClaudeJson.Str(o, "agent_id") ?? "";
                        extra = s => s.Workers.Remove(agent);
                        break;
                    }
                case "TaskCreated":
                    {
                        string task = ClaudeJson.Str(o, "task_id") ?? "";
                        if (task.Length == 0) return;
                        extra = s => { if (s.Tasks.Contains(task)) return false; s.Tasks.Add(task); if (s.Tasks.Count > ClaudeSessionFile.MostTasks) s.Tasks.RemoveAt(0); return true; };
                        break;
                    }
                case "TaskCompleted":
                    {
                        string task = ClaudeJson.Str(o, "task_id") ?? "";
                        if (task.Length == 0) return;
                        extra = s => {
                            if (s.TasksDone.Contains(task)) return false;
                            if (!s.Tasks.Contains(task)) { s.Tasks.Add(task); if (s.Tasks.Count > ClaudeSessionFile.MostTasks) s.Tasks.RemoveAt(0); }
                            s.TasksDone.Add(task); if (s.TasksDone.Count > ClaudeSessionFile.MostTasks) s.TasksDone.RemoveAt(0);
                            return true;
                        };
                        break;
                    }
                // The model as Claude Code names it ("claude-opus-5-5"); the dashboard shows it by name.
                case "PostModelSwitch":
                    {
                        string model = ClaudeJson.Str(o, "to_model") ?? "";
                        if (model.Length == 0) return;
                        extra = s => { if (s.Model == model) return false; s.Model = model; return true; };
                        break;
                    }
                case "CwdChanged":
                    {
                        string cwd = ClaudeJson.Str(o, "new_cwd") ?? "";
                        if (cwd.Length == 0) return;
                        extra = s => { if (s.Cwd == cwd) return false; s.Cwd = cwd; return true; };
                        break;
                    }
                case "Notification":
                    {
                        string type = ClaudeJson.Str(o, "notification_type") ?? "";
                        message = ClaudeJson.Str(o, "message") ?? "";
                        switch (type)
                        {
                            // A question waits on the user; "Claude is waiting for your input" (idle_prompt) stays Done.
                            case "permission_prompt": case "elicitation_dialog": case "elicitation_url_dialog": case "agent_needs_input": next = ClaudeState.NeedsYou; break;
                            case "agent_completed": next = ClaudeState.Done; message = ""; break;
                            // Claude Code waited out a usage limit and carries on.
                            case "quota_auto_resume_fired": next = ClaudeState.Working; message = ""; break;
                            // The limit reset while the PC slept: Claude Code waits for Enter.
                            case "quota_auto_resume_stale": next = ClaudeState.NeedsYou; if (message.Length == 0) message = QuotaStale; break;
                            // Claude Code stopped waiting for the limit without carrying on.
                            case "quota_auto_resume_disabled":
                                {
                                    string details = message; message = "";
                                    next = ClaudeState.Stuck;
                                    extra = s => Stuck(s, QuotaDisabled, details);
                                    break;
                                }
                            default:
                                if (type.Length == 0 && message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0) { next = ClaudeState.NeedsYou; break; }
                                return;
                        }
                        break;
                    }
                default: return;
            }
            WithSession(id, () => {
                var session = Read(path);
                bool fresh = session == null;
                if (fresh) session = new ClaudeSessionFile { Id = id, Since = Epoch(now), State = ClaudeState.Ready };
                if (String.IsNullOrEmpty(session.Project)) session.Project = ClaudeJson.Str(o, "cwd") ?? "";
                if (String.IsNullOrEmpty(session.Transcript)) session.Transcript = ClaudeJson.Str(o, "transcript_path") ?? "";
                bool changed = fresh;
                if (next.HasValue && (next.Value != session.State || message != session.Message))
                {
                    if (next.Value != session.State) session.Since = Epoch(now);
                    session.State = next.Value; session.Message = message; changed = true;
                }
                // A new turn (anything but Stuck) clears the last turn's error.
                if (next.HasValue && next.Value != ClaudeState.Stuck && session.Error.Length > 0) { session.Error = ""; session.ErrorText = ""; changed = true; }
                if (compacting.HasValue && compacting.Value != session.Compacting) { session.Compacting = compacting.Value; changed = true; }
                if (extra != null && extra(session)) changed = true;
                if (session.Pid == 0 || name == "SessionStart")
                {
                    var found = owner == null ? new ClaudeProcesses.Owner() : owner();
                    if (found.ClaudePid != 0 || found.Window != 0) { session.Pid = found.ClaudePid; session.WindowPid = found.WindowPid; session.Window = found.Window; changed = true; }
                }
                // Tool after tool while Working: the file only needs touching now and then, to show the session is alive.
                if (!changed && Epoch(now) - session.LastEvent < 60000) return;
                session.LastEvent = Epoch(now);
                WriteAtomic(path, ClaudeJson.Compact(session.ToJson()));
            });
        }
        // The error Stuck keeps when Claude Code stops waiting for a usage limit, and the message a reset during sleep
        // shows.
        public const string QuotaDisabled = "quota_disabled", QuotaStale = "Your usage limit reset. Press Enter in Claude Code to carry on.";
        private static bool Stuck(ClaudeSessionFile s, string error, string details)
        {
            string text = Short(details, 200);
            if (s.Error == error && s.ErrorText == text) return false;
            s.Error = error; s.ErrorText = text;
            return true;
        }
        private static string FirstLine(string text)
        {
            if (String.IsNullOrEmpty(text)) return "failed";
            int cut = text.IndexOfAny(new[] { '\r', '\n' });
            string line = (cut >= 0 ? text.Substring(0, cut) : text).Trim();
            return line.Length == 0 ? "failed" : line;
        }
        private static string Short(string text, int most)
        {
            text = (text ?? "").Trim();
            return text.Length <= most ? text : text.Substring(0, most - 1) + "…";
        }

        // ----- Status line mode -----

        // Reads one status line update: keeps the plan limits and the session's name, model and context, and returns
        // the line Widget Screen prints when there was no status line before ("5h 23% · week 41% · ctx 8%").
        public static string StatusLine(string input, string bridge, Func<ClaudeProcesses.Owner> owner, DateTime now)
        {
            var o = ClaudeJson.Obj(ClaudeJson.Parse(input));
            string id = ClaudeJson.Str(o, "session_id");
            var context = ClaudeJson.Obj(o, "context_window");
            double contextPercent = ClaudeJson.Num(context, "used_percentage");

            var previous = ReadLimits(bridge);
            var rate = ClaudeJson.Obj(o, "rate_limits");
            ClaudeLimits limits;
            if (rate != null) { limits = ParseRateLimits(rate, now); limits.MissingAt = null; }
            else
            {
                limits = previous ?? new ClaudeLimits();
                // Claude Code has answered (it spent API time) but sent no limits: not a Pro or Max login.
                double apiMs = ClaudeJson.Num(ClaudeJson.Obj(o, "cost"), "total_api_duration_ms");
                if (!Double.IsNaN(apiMs) && apiMs > 0) limits.MissingAt = now;
            }
            if (rate != null || limits.MissingAt.HasValue)
            {
                bool same = previous != null && rate != null && previous.Source == "statusline" && Same(previous.FiveHour, limits.FiveHour) && Same(previous.Week, limits.Week) && SameSpend(previous.Spend, limits.Spend) && now - previous.UpdatedAt < TimeSpan.FromSeconds(60);
                bool sameMissing = previous != null && rate == null && previous.MissingAt.HasValue && now - previous.MissingAt.Value < TimeSpan.FromSeconds(60);
                if (rate != null) { limits.UpdatedAt = now; limits.Source = "statusline"; }
                if (!same && !sameMissing) WriteAtomic(LimitsPath(bridge), ClaudeJson.Compact(limits.ToJson()));
            }

            if (!String.IsNullOrEmpty(id))
            {
                string title = ClaudeJson.Str(o, "session_name") ?? "", model = ClaudeJson.Str(ClaudeJson.Obj(o, "model"), "display_name") ?? "";
                string project = ClaudeJson.Str(ClaudeJson.Obj(o, "workspace"), "project_dir") ?? ClaudeJson.Str(o, "cwd") ?? "";
                var cost = ClaudeJson.Obj(o, "cost"); var worktree = ClaudeJson.Obj(o, "worktree");
                double costUsd = ClaudeJson.Num(cost, "total_cost_usd"), duration = ClaudeJson.Num(cost, "total_duration_ms"), api = ClaudeJson.Num(cost, "total_api_duration_ms");
                double added = ClaudeJson.Num(cost, "total_lines_added"), removed = ClaudeJson.Num(cost, "total_lines_removed"), size = ClaudeJson.Num(context, "context_window_size");
                bool over = ClaudeJson.Bool(o, "exceeds_200k_tokens");
                string style = Short(ClaudeJson.Str(ClaudeJson.Obj(o, "output_style"), "name"), 40), agent = Short(ClaudeJson.Str(ClaudeJson.Obj(o, "agent"), "name"), 60);
                string tree = Short(ClaudeJson.Str(worktree, "name"), 80), branch = Short(ClaudeJson.Str(worktree, "branch"), 120);
                string path = SessionPath(bridge, id);
                WithSession(id, () => {
                    var session = Read(path);
                    bool changed = false;
                    if (session == null)
                    {
                        session = new ClaudeSessionFile { Id = id, Since = Epoch(now), State = ClaudeState.Ready };
                        var found = owner == null ? new ClaudeProcesses.Owner() : owner();
                        session.Pid = found.ClaudePid; session.WindowPid = found.WindowPid; session.Window = found.Window;
                        changed = true;
                    }
                    if (title.Length > 0 && title != session.Title) { session.Title = title; changed = true; }
                    if (model.Length > 0 && model != session.Model) { session.Model = model; changed = true; }
                    if (project.Length > 0 && project != session.Project) { session.Project = project; changed = true; }
                    if (!Double.IsNaN(contextPercent) && (Double.IsNaN(session.Context) || Math.Abs(contextPercent - session.Context) >= 1)) { session.Context = contextPercent; changed = true; }
                    // Package 2: Claude Code's own numbers. A cent of cost, a line or a setting is a change; the clocks
                    // only count once they have moved a minute, so a refresh every 30 s doesn't rewrite the file.
                    if (!Double.IsNaN(costUsd) && costUsd >= 0 && (Double.IsNaN(session.CostUsd) || Math.Abs(costUsd - session.CostUsd) >= 0.005)) { session.CostUsd = costUsd; changed = true; }
                    if (!Double.IsNaN(added) && (long)added != session.LinesAdded) { session.LinesAdded = Math.Max(0, (long)added); changed = true; }
                    if (!Double.IsNaN(removed) && (long)removed != session.LinesRemoved) { session.LinesRemoved = Math.Max(0, (long)removed); changed = true; }
                    if (!Double.IsNaN(size) && size > 0 && (long)size != session.ContextSize) { session.ContextSize = (long)size; changed = true; }
                    if (over != session.Over200k) { session.Over200k = over; changed = true; }
                    if (style != session.Style) { session.Style = style; changed = true; }
                    if (agent != session.Agent) { session.Agent = agent; changed = true; }
                    if (tree != session.Worktree || branch != session.WorktreeBranch) { session.Worktree = tree; session.WorktreeBranch = branch; changed = true; }
                    if (!Double.IsNaN(duration) && duration >= 0 && (changed || Math.Abs(duration - session.DurationMs) >= 60000)) { session.DurationMs = (long)duration; changed = true; }
                    if (!Double.IsNaN(api) && api >= 0 && (changed || Math.Abs(api - session.ApiMs) >= 60000)) { session.ApiMs = (long)api; changed = true; }
                    if (!changed) return;
                    session.LastEvent = Math.Max(session.LastEvent, Epoch(now));
                    WriteAtomic(path, ClaudeJson.Compact(session.ToJson()));
                });
            }
            return DefaultLine(rate != null ? limits : null, contextPercent, now);
        }
        private static bool SameSpend(ClaudeSpendLimit a, ClaudeSpendLimit b)
        {
            if (a == null || b == null) return a == b;
            return Math.Abs(a.Percent - b.Percent) < 0.05 && a.ResetsAt == b.ResetsAt && (a.UsedUsd == b.UsedUsd || (Double.IsNaN(a.UsedUsd) && Double.IsNaN(b.UsedUsd))) && (a.LimitUsd == b.LimitUsd || (Double.IsNaN(a.LimitUsd) && Double.IsNaN(b.LimitUsd))) && a.Period == b.Period;
        }
        private static bool Same(ClaudeLimitWindow a, ClaudeLimitWindow b)
        {
            if (a == null || b == null) return a == b;
            return Math.Abs(a.Percent - b.Percent) < 0.05 && a.ResetsAt == b.ResetsAt;
        }

        // "5h 23% · week 41% · ctx 8%", leaving out what isn't known.
        public static string DefaultLine(ClaudeLimits limits, double context, DateTime now)
        {
            var parts = new List<string>();
            if (limits != null && limits.FiveHour != null && limits.FiveHour.Current(now)) parts.Add("5h " + Math.Round(limits.FiveHour.Percent) + "%");
            if (limits != null && limits.Week != null && limits.Week.Current(now)) parts.Add("week " + Math.Round(limits.Week.Percent) + "%");
            if (!Double.IsNaN(context)) parts.Add("ctx " + Math.Round(context) + "%");
            return parts.Count == 0 ? "Claude Code" : String.Join(" · ", parts);
        }

        // The status line's rate_limits: five_hour and seven_day, each used_percentage and resets_at (Unix seconds per
        // the docs; an ISO time has been seen too). Either window may be missing.
        public static ClaudeLimits ParseRateLimits(JsonObject rate, DateTime now)
        {
            var limits = new ClaudeLimits { FiveHour = ParseWindow(ClaudeJson.Obj(rate, "five_hour"), "used_percentage"), Week = ParseWindow(ClaudeJson.Obj(rate, "seven_day"), "used_percentage"), UpdatedAt = now, Source = "statusline" };
            // Behind a Claude apps gateway: the spend limit (its percentage can pass 100; the dollars may come later).
            var spend = ClaudeJson.Obj(rate, "spend_limit");
            double pct = ClaudeJson.Num(spend, "used_percentage");
            if (spend != null && !Double.IsNaN(pct))
                limits.Spend = new ClaudeSpendLimit { Percent = Math.Max(0, pct), ResetsAt = ParseTime(spend.Get("resets_at")), UsedUsd = ClaudeJson.Num(spend, "used_usd"), LimitUsd = ClaudeJson.Num(spend, "limit_usd"), Period = Short(ClaudeJson.Str(spend, "period") ?? "", 20) };
            return limits;
        }

        // The read-only usage check's answer (GET api.anthropic.com/api/oauth/usage, undocumented): five_hour and
        // seven_day, each utilization 0–100 and resets_at as ISO-8601; either may be null. seven_day_opus,
        // seven_day_sonnet and model_scoped ([{display_name, utilization, resets_at}]) become the extra windows. Null
        // when nothing is usable.
        public static ClaudeLimits ParseUsageResponse(string json, DateTime now)
        {
            object parsed; if (!ClaudeJson.TryParse(json, out parsed)) return null;
            var o = ClaudeJson.Obj(parsed); if (o == null) return null;
            var limits = new ClaudeLimits { FiveHour = ParseWindow(ClaudeJson.Obj(o, "five_hour"), "utilization"), Week = ParseWindow(ClaudeJson.Obj(o, "seven_day"), "utilization"), UpdatedAt = now, Source = "online" };
            foreach (var pair in new[] { new[] { "seven_day_opus", "Opus week" }, new[] { "seven_day_sonnet", "Sonnet week" }, new[] { "seven_day_cowork", "Cowork week" }, new[] { "seven_day_oauth_apps", "Apps week" } })
            {
                var window = ParseWindow(ClaudeJson.Obj(o, pair[0]), "utilization");
                if (window != null) limits.Extra.Add(new KeyValuePair<string, ClaudeLimitWindow>(pair[1], window));
            }
            foreach (var scoped in (ClaudeJson.List(o, "model_scoped") ?? new List<object>()).OfType<JsonObject>())
            {
                string name = ClaudeJson.Str(scoped, "display_name");
                var window = ParseWindow(scoped, "utilization");
                if (!String.IsNullOrWhiteSpace(name) && window != null && !limits.Extra.Any(p => p.Key.StartsWith(name.Trim() + " ", StringComparison.OrdinalIgnoreCase)))
                    limits.Extra.Add(new KeyValuePair<string, ClaudeLimitWindow>(name.Trim() + " week", window));
            }
            // The newer "limits" list ({kind, percent, resets_at, scope}) is what the Claude app shows: it fills a window
            // the older fields left out, and its model-scoped weekly windows ("Fable 2%") become extra lines.
            foreach (var item in (ClaudeJson.List(o, "limits") ?? new List<object>()).OfType<JsonObject>())
            {
                string kind = (ClaudeJson.Str(item, "kind") ?? "").ToLowerInvariant();
                var window = ParseWindow(item, "percent");
                if (window == null) continue;
                if (kind == "weekly_all") { if (limits.Week == null) limits.Week = window; }
                else if (kind.Contains("five_hour") || kind.Contains("session") || kind.Contains("5h")) { if (limits.FiveHour == null) limits.FiveHour = window; }
                else if (kind == "weekly_scoped")
                {
                    string name = ClaudeJson.Str(ClaudeJson.Obj(ClaudeJson.Obj(item, "scope"), "model"), "display_name");
                    if (!String.IsNullOrWhiteSpace(name) && !limits.Extra.Any(p => p.Key.StartsWith(name.Trim() + " ", StringComparison.OrdinalIgnoreCase)))
                        limits.Extra.Add(new KeyValuePair<string, ClaudeLimitWindow>(name.Trim() + " week", window));
                }
            }
            var extra = ClaudeJson.Obj(o, "extra_usage");
            if (extra != null)
            {
                double limit = ClaudeJson.Num(extra, "monthly_limit"), used = ClaudeJson.Num(extra, "used_credits"), pct = ClaudeJson.Num(extra, "utilization");
                if (Double.IsNaN(pct) && !Double.IsNaN(limit) && !Double.IsNaN(used) && limit > 0) pct = 100 * used / limit;
                limits.ExtraUsage = new ClaudeExtraUsage { Enabled = ClaudeJson.Bool(extra, "is_enabled"), Limit = limit, Used = used, Percent = Double.IsNaN(pct) ? pct : Math.Max(0, Math.Min(100, pct)) };
            }
            return limits.Known ? limits : null;
        }

        private static ClaudeLimitWindow ParseWindow(JsonObject o, string percentKey)
        {
            if (o == null) return null;
            double pct = ClaudeJson.Num(o, percentKey);
            if (Double.IsNaN(pct)) return null;
            return new ClaudeLimitWindow { Percent = Math.Max(0, Math.Min(100, pct)), ResetsAt = ParseTime(o.Get("resets_at")) };
        }

        // Unix seconds (or milliseconds, if the number is that large), or an ISO-8601 time.
        public static DateTime? ParseTime(object value)
        {
            double number = Double.NaN;
            if (value is JsonNumber) number = ((JsonNumber)value).Value;
            else if (value is string && !Double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) number = Double.NaN;
            if (!Double.IsNaN(number) && number > 0) return FromEpoch(number > 1e11 ? number : number * 1000);
            var text = value as string; DateTime time;
            if (!String.IsNullOrWhiteSpace(text) && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out time)) return DateTime.SpecifyKind(time, DateTimeKind.Utc);
            return null;
        }

        // ----- Files -----

        public static ClaudeSessionFile Read(string path)
        {
            try { if (!File.Exists(path)) return null; return ClaudeSessionFile.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(path, Encoding.UTF8)))); }
            catch (Exception) { return null; }
        }
        public static ClaudeLimits ReadLimits(string bridge)
        {
            try { string path = LimitsPath(bridge); return File.Exists(path) ? ClaudeLimits.FromJson(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(path, Encoding.UTF8)))) : null; }
            catch (Exception) { return null; }
        }

        // Writes next to the target and moves it into place, so a reader never sees half a file.
        public static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            try
            {
                if (File.Exists(path)) File.Replace(temp, path, null, true); else File.Move(temp, path);
            }
            catch (Exception) { try { File.Delete(temp); } catch (Exception) { } throw; }
        }
        private static void Delete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { } }

        // Hooks for one session can run at the same moment (a tool finishing while the status line updates); a named
        // mutex per session keeps their read-change-write from losing each other's change.
        private static void WithSession(string id, Action action)
        {
            using (var mutex = new Mutex(false, "Local\\WidgetScreen.Claude." + SafeName(id)))
            {
                bool owned = false;
                try { try { owned = mutex.WaitOne(2000); } catch (AbandonedMutexException) { owned = true; } action(); }
                finally { if (owned) mutex.ReleaseMutex(); }
            }
        }
    }
}
