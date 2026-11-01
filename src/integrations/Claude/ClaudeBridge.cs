using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace WidgetScreen
{
    // A session's state as the critters show it (docs/design/claude-watcher.md).
    public enum ClaudeState { Ready, Working, NeedsYou, Done }

    // One Claude Code session, as the bridge records it in sessions\<id>.json.
    public sealed class ClaudeSessionFile
    {
        public string Id = "", Project = "", Title = "", Message = "", Model = "", Transcript = "";
        public ClaudeState State;
        public long Since, LastEvent;
        public double Context = Double.NaN;
        public int Pid, WindowPid;
        public long Window;

        public static string StateName(ClaudeState state) { return state == ClaudeState.Working ? "working" : state == ClaudeState.NeedsYou ? "needs" : state == ClaudeState.Done ? "done" : "ready"; }
        public static ClaudeState ParseState(string name) { return name == "working" ? ClaudeState.Working : name == "needs" ? ClaudeState.NeedsYou : name == "done" ? ClaudeState.Done : ClaudeState.Ready; }

        public JsonObject ToJson()
        {
            var o = new JsonObject();
            o["id"] = Id; o["state"] = StateName(State); o["since"] = new JsonNumber(Since); o["lastEvent"] = new JsonNumber(LastEvent);
            o["project"] = Project; o["title"] = Title; o["message"] = Message; o["model"] = Model; o["transcript"] = Transcript;
            if (!Double.IsNaN(Context)) o["context"] = new JsonNumber(Math.Round(Context, 1));
            o["pid"] = new JsonNumber(Pid); o["windowPid"] = new JsonNumber(WindowPid); o["window"] = new JsonNumber(Window);
            return o;
        }
        public static ClaudeSessionFile FromJson(JsonObject o)
        {
            if (o == null || String.IsNullOrEmpty(ClaudeJson.Str(o, "id"))) return null;
            var s = new ClaudeSessionFile { Id = ClaudeJson.Str(o, "id"), State = ParseState(ClaudeJson.Str(o, "state")),
                Project = ClaudeJson.Str(o, "project") ?? "", Title = ClaudeJson.Str(o, "title") ?? "", Message = ClaudeJson.Str(o, "message") ?? "",
                Model = ClaudeJson.Str(o, "model") ?? "", Transcript = ClaudeJson.Str(o, "transcript") ?? "", Context = ClaudeJson.Num(o, "context") };
            s.Since = Long(o, "since"); s.LastEvent = Long(o, "lastEvent"); s.Pid = (int)Long(o, "pid"); s.WindowPid = (int)Long(o, "windowPid"); s.Window = Long(o, "window");
            return s;
        }
        private static long Long(JsonObject o, string key) { double v = ClaudeJson.Num(o, key); return Double.IsNaN(v) ? 0 : (long)v; }
    }

    // One plan window: how much of it is used (0–100) and when it resets.
    public sealed class ClaudeLimitWindow
    {
        public double Percent;
        public DateTime? ResetsAt;
        // A window whose reset time has passed starts again at nothing.
        public bool Current(DateTime now) { return !ResetsAt.HasValue || ResetsAt.Value > now; }
    }

    // The plan's five-hour and weekly limits, from Claude Code's status line or the usage check.
    public sealed class ClaudeLimits
    {
        public ClaudeLimitWindow FiveHour, Week;
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
            if (name == "SessionEnd") { WithSession(id, () => Delete(path)); return; }
            ClaudeState? next = null; string message = "";
            switch (name)
            {
                case "SessionStart": next = ClaudeJson.Str(o, "source") == "compact" ? (ClaudeState?)null : ClaudeState.Ready; break;
                case "UserPromptSubmit": case "PostToolUse": case "PreToolUse": next = ClaudeState.Working; break;
                case "Stop": next = ClaudeState.Done; break;
                case "Notification":
                    {
                        string type = ClaudeJson.Str(o, "notification_type") ?? "";
                        message = ClaudeJson.Str(o, "message") ?? "";
                        // Only a question waits on the user; "Claude is waiting for your input" (idle_prompt) stays Done.
                        bool asks = type == "permission_prompt" || type == "elicitation_dialog" || (type.Length == 0 && message.IndexOf("permission", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!asks) return;
                        next = ClaudeState.NeedsYou;
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
                bool same = previous != null && rate != null && previous.Source == "statusline" && Same(previous.FiveHour, limits.FiveHour) && Same(previous.Week, limits.Week) && now - previous.UpdatedAt < TimeSpan.FromSeconds(60);
                bool sameMissing = previous != null && rate == null && previous.MissingAt.HasValue && now - previous.MissingAt.Value < TimeSpan.FromSeconds(60);
                if (rate != null) { limits.UpdatedAt = now; limits.Source = "statusline"; }
                if (!same && !sameMissing) WriteAtomic(LimitsPath(bridge), ClaudeJson.Compact(limits.ToJson()));
            }

            if (!String.IsNullOrEmpty(id))
            {
                string title = ClaudeJson.Str(o, "session_name") ?? "", model = ClaudeJson.Str(ClaudeJson.Obj(o, "model"), "display_name") ?? "";
                string project = ClaudeJson.Str(ClaudeJson.Obj(o, "workspace"), "project_dir") ?? ClaudeJson.Str(o, "cwd") ?? "";
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
                    if (!changed) return;
                    session.LastEvent = Math.Max(session.LastEvent, Epoch(now));
                    WriteAtomic(path, ClaudeJson.Compact(session.ToJson()));
                });
            }
            return DefaultLine(rate != null ? limits : null, contextPercent, now);
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
            return new ClaudeLimits { FiveHour = ParseWindow(ClaudeJson.Obj(rate, "five_hour"), "used_percentage"), Week = ParseWindow(ClaudeJson.Obj(rate, "seven_day"), "used_percentage"), UpdatedAt = now, Source = "statusline" };
        }

        // The read-only usage check's answer (GET api.anthropic.com/api/oauth/usage, undocumented): five_hour and
        // seven_day, each utilization 0–100 and resets_at as ISO-8601; either may be null. Null when nothing is usable.
        public static ClaudeLimits ParseUsageResponse(string json, DateTime now)
        {
            object parsed; if (!ClaudeJson.TryParse(json, out parsed)) return null;
            var o = ClaudeJson.Obj(parsed); if (o == null) return null;
            var limits = new ClaudeLimits { FiveHour = ParseWindow(ClaudeJson.Obj(o, "five_hour"), "utilization"), Week = ParseWindow(ClaudeJson.Obj(o, "seven_day"), "utilization"), UpdatedAt = now, Source = "online" };
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
