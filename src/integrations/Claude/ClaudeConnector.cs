using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // Settings → Claude → Connect Claude Code (docs/design/claude-watcher.md, decisions 014 and 015): adds Widget Screen's
    // hooks, status line and telemetry settings (the env block) to Claude Code's settings.json, and Disconnect takes
    // exactly those out again. Other hooks are
    // never replaced, other keys stay where they were, a backup is saved next to the file first, and a file that
    // isn't valid JSON is left alone. Only the user's tap on Connect or Disconnect runs this against the real folder;
    // the checks run it in temporary folders.
    public sealed class ClaudeConnector
    {
        public const string BackupSuffix = ".widgetscreen-backup";
        // Besides the latest backup, dated copies (settings.json.widgetscreen-backup-20261101-174512); the newest
        // KeptBackups stay, so a second Connect never loses the original (review fix 11).
        public const int KeptBackups = 5;
        // The events the bridge listens to (decision 023 added the second and third rows). Notification only for the
        // types that change a session: questions, background sessions and the usage-limit wait. PreModelSwitch is left
        // out: PostModelSwitch carries the same names after the fact, and a hook before the switch could only slow it.
        public const string NotificationTypes = "permission_prompt|elicitation_dialog|elicitation_url_dialog|agent_needs_input|agent_completed|quota_auto_resume_fired|quota_auto_resume_stale|quota_auto_resume_disabled";
        public static readonly string[][] Events = {
            new[] { "SessionStart", null }, new[] { "UserPromptSubmit", null }, new[] { "PostToolUse", "*" },
            new[] { "Notification", NotificationTypes }, new[] { "PermissionRequest", "*" }, new[] { "Stop", null }, new[] { "SessionEnd", null }, new[] { "PreCompact", null },
            new[] { "StopFailure", null }, new[] { "PostToolUseFailure", "*" }, new[] { "PostCompact", null }, new[] { "PermissionDenied", "*" },
            new[] { "SubagentStart", null }, new[] { "SubagentStop", null }, new[] { "TaskCreated", null }, new[] { "TaskCompleted", null },
            new[] { "PostModelSwitch", null }, new[] { "CwdChanged", null } };
        // Claude Code's OpenTelemetry export, to Widget Screen's loopback receiver (ClaudeTelemetry). Usage numbers and
        // event names only: prompts, responses and tool details stay off (OTEL_LOG_USER_PROMPTS, OTEL_LOG_TOOL_DETAILS
        // are never set).
        public static string[][] Telemetry(int port)
        {
            return new[] {
                new[] { "CLAUDE_CODE_ENABLE_TELEMETRY", "1" }, new[] { "OTEL_METRICS_EXPORTER", "otlp" }, new[] { "OTEL_LOGS_EXPORTER", "otlp" },
                new[] { "OTEL_EXPORTER_OTLP_PROTOCOL", "http/json" }, new[] { "OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:" + port },
                new[] { "OTEL_METRIC_EXPORT_INTERVAL", "10000" }, new[] { "OTEL_METRICS_INCLUDE_REPOSITORY", "true" }, new[] { "OTEL_METRICS_INCLUDE_ENTRYPOINT", "true" } };
        }
        public const string Endpoint = "OTEL_EXPORTER_OTLP_ENDPOINT", Loopback = "http://127.0.0.1:";
        public readonly int TelemetryPort;

        public readonly string ConfigFolder, BridgeFolder;
        public ClaudeConnector(string configFolder, string bridgeFolder, int telemetryPort = ClaudeTelemetry.DefaultPort) { ConfigFolder = configFolder; BridgeFolder = bridgeFolder; TelemetryPort = telemetryPort; }
        // The env keys Connect added (so Disconnect removes exactly those), beside the bridge.
        public string EnvRecordPath { get { return Path.Combine(BridgeFolder, "connected-env.json"); } }

        // %USERPROFILE%\.claude, or CLAUDE_CONFIG_DIR when Claude Code is set up to use another folder.
        public static string DefaultConfigFolder
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (!String.IsNullOrWhiteSpace(configured)) return Environment.ExpandEnvironmentVariables(configured.Trim());
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            }
        }

        public string SettingsPath { get { return Path.Combine(ConfigFolder, "settings.json"); } }
        public string HookPath { get { return Path.Combine(BridgeFolder, ClaudeBridge.HookExe); } }
        // The command as Claude Code runs it: forward slashes (Git Bash eats backslashes) and quotes for spaces.
        public string Command(string mode) { return "\"" + HookPath.Replace('\\', '/') + "\" " + mode; }

        public static bool Ours(object hook)
        {
            var command = ClaudeJson.Str(hook as JsonObject, "command");
            return command != null && command.Replace('\\', '/').IndexOf(ClaudeBridge.Marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Whether settings.json has Widget Screen's hooks or status line. False when the file is missing or unreadable.
        public bool IsConnected()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                var root = ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8)));
                if (root == null) return false;
                if (Ours(ClaudeJson.Obj(root, "statusLine"))) return true;
                var hooks = ClaudeJson.Obj(root, "hooks");
                return hooks != null && hooks.Any(e => (e.Value as List<object> ?? new List<object>()).OfType<JsonObject>().Any(g => (ClaudeJson.List(g, "hooks") ?? new List<object>()).Any(Ours)));
            }
            catch (Exception) { return false; }
        }

        // Telemetry settings the user already has in settings.json's env with other values: Connect leaves telemetry out
        // rather than overwrite them, and Settings → Claude names them.
        public List<string> TelemetryConflicts()
        {
            try
            {
                var env = File.Exists(SettingsPath) ? ClaudeJson.Obj(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8))), "env") : null;
                return Conflicts(env, OurEnv());
            }
            catch (Exception) { return new List<string>(); }
        }
        private static string Text(object value) { return Convert.ToString(value is JsonNumber ? (object)((JsonNumber)value).Value : value, System.Globalization.CultureInfo.InvariantCulture); }
        private static List<string> Conflicts(JsonObject env, HashSet<string> ours)
        {
            var result = new List<string>();
            if (env == null) return result;
            foreach (var pair in env)
            {
                bool related = pair.Key.StartsWith("OTEL_", StringComparison.Ordinal) || pair.Key == "CLAUDE_CODE_ENABLE_TELEMETRY";
                if (!related || ours.Contains(pair.Key)) continue;
                string wanted = null;
                foreach (var t in Telemetry(0)) if (t[0] == pair.Key) wanted = t[1];
                // The same value the widget would set is no conflict, except an endpoint somewhere else.
                if (wanted != null && pair.Key != "OTEL_EXPORTER_OTLP_ENDPOINT" && Text(pair.Value) == wanted) continue;
                result.Add(pair.Key);
            }
            return result;
        }
        private HashSet<string> OurEnv()
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            try { if (File.Exists(EnvRecordPath)) foreach (var key in (ClaudeJson.List(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(EnvRecordPath, Encoding.UTF8))), "keys") ?? new List<object>()).OfType<string>()) keys.Add(key); }
            catch (Exception) { }
            return keys;
        }

        // The loopback port settings.json sends telemetry to, when Widget Screen set it (Connect writes the port it could
        // bind, decision 023); 0 otherwise.
        public int ConfiguredPort()
        {
            try
            {
                if (!File.Exists(SettingsPath) || !OurEnv().Contains(Endpoint)) return 0;
                var env = ClaudeJson.Obj(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8))), "env");
                return PortOf(env == null ? null : Text(env.Get(Endpoint)));
            }
            catch (Exception) { return 0; }
        }
        // "http://127.0.0.1:43833" is 43833; anything else is 0.
        public static int PortOf(string endpoint)
        {
            int port;
            if (endpoint == null || !endpoint.StartsWith(Loopback, StringComparison.Ordinal)) return 0;
            return Int32.TryParse(endpoint.Substring(Loopback.Length).TrimEnd('/'), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out port) && port > 0 && port < 65536 ? port : 0;
        }

        // Whether a connection made by an older Widget Screen lacks something this one adds (a hook event or its
        // matcher, the telemetry settings). Settings → Claude then offers Update connection.
        public const int StatusRefresh = 30;
        public bool NeedsUpdate()
        {
            try
            {
                if (!IsConnected()) return false;
                var root = ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(SettingsPath, Encoding.UTF8)));
                var hooks = ClaudeJson.Obj(root, "hooks");
                foreach (var entry in Events)
                {
                    string matcher = entry[1];
                    if (!((hooks == null ? null : hooks.Get(entry[0]) as List<object>) ?? new List<object>()).OfType<JsonObject>()
                        .Any(g => ClaudeJson.Str(g, "matcher") == matcher && (ClaudeJson.List(g, "hooks") ?? new List<object>()).Any(h => Ours(h) && ClaudeJson.Str(h as JsonObject, "command") == Command("hook")))) return true;
                }
                // Package 2's status line refreshes on a timer too.
                var status = ClaudeJson.Obj(root, "statusLine");
                if (Ours(status) && !(status.Get("refreshInterval") is JsonNumber)) return true;
                var env = ClaudeJson.Obj(root, "env");
                if (Conflicts(env, OurEnv()).Count > 0) return false;
                foreach (var t in Telemetry(TelemetryPort))
                {
                    string value = env == null ? null : Text(env.Get(t[0]));
                    // The endpoint may name any loopback port Connect could bind.
                    if (t[0] == Endpoint ? PortOf(value) == 0 : value != t[1]) return true;
                }
                return false;
            }
            catch (Exception) { return false; }
        }

        // Copies the bridge into its stable folder, then adds the hooks and status line. hookSource is the
        // ClaudeHook.exe that came with this app; port is the loopback port telemetry goes to (the one the receiver
        // could bind; 0 for TelemetryPort). Throws InvalidOperationException with a message for the user.
        public void Connect(string hookSource, int port = 0)
        {
            if (port <= 0) port = TelemetryPort;
            var root = Load();
            if (String.IsNullOrEmpty(hookSource) || !File.Exists(hookSource)) throw new InvalidOperationException(ClaudeBridge.HookExe + " is missing from Widget Screen's folder. Reinstall Widget Screen, then try again.");
            Directory.CreateDirectory(BridgeFolder);
            Directory.CreateDirectory(ClaudeBridge.SessionsFolder(BridgeFolder));
            CopyHook(hookSource);
            Backup();

            RemoveOurs(root);
            var hooks = ClaudeJson.Obj(root, "hooks");
            if (hooks == null) { hooks = new JsonObject(); root["hooks"] = hooks; }
            foreach (var entry in Events)
            {
                var list = hooks.Get(entry[0]) as List<object>;
                if (list == null) { list = new List<object>(); hooks[entry[0]] = list; }
                var group = new JsonObject();
                if (entry[1] != null) group["matcher"] = entry[1];
                var hook = new JsonObject(); hook["type"] = "command"; hook["command"] = Command("hook"); hook["async"] = true; hook["timeout"] = new JsonNumber(10);
                group["hooks"] = new List<object> { hook };
                list.Add(group);
            }

            // The user's own status line keeps showing: the bridge runs it and prints what it prints.
            var line = ClaudeJson.Obj(root, "statusLine");
            string previous = ClaudeBridge.PreviousStatusLinePath(BridgeFolder);
            if (line != null && !Ours(line)) ClaudeBridge.WriteAtomic(previous, ClaudeJson.Write(line));
            else if (line == null && File.Exists(previous)) File.Delete(previous);
            var ours = new JsonObject(); ours["type"] = "command"; ours["command"] = Command("statusline");
            if (line != null && line.Contains("padding")) ours["padding"] = line.Get("padding");
            // Package 2: re-run every 30 s as well as on events, so cost, time and the plan stay current while a
            // coordinator waits on helpers. A refresh the user already chose is kept.
            object refresh = line == null ? null : line.Get("refreshInterval");
            ours["refreshInterval"] = refresh is JsonNumber && ((JsonNumber)refresh).Value >= 1 ? refresh : new JsonNumber(StatusRefresh);
            root["statusLine"] = ours;

            // Telemetry to the loopback receiver, unless the user already sends it somewhere: then it's left alone.
            var env = ClaudeJson.Obj(root, "env");
            var previouslyOurs = OurEnv();
            var added = new List<object>();
            if (Conflicts(env, previouslyOurs).Count == 0)
            {
                if (env == null) { env = new JsonObject(); root["env"] = env; }
                foreach (var t in Telemetry(port))
                {
                    if (env.Contains(t[0]) && !previouslyOurs.Contains(t[0])) continue;
                    env[t[0]] = t[1]; added.Add(t[0]);
                }
            }
            Save(root);
            var record = new JsonObject(); record["keys"] = added;
            ClaudeBridge.WriteAtomic(EnvRecordPath, ClaudeJson.Write(record));
        }

        // Takes out exactly Widget Screen's entries and puts the earlier status line back.
        public void Disconnect()
        {
            if (!File.Exists(SettingsPath)) { ForgetPrevious(); return; }
            var root = Load();
            Backup();
            RemoveOurs(root);
            // Exactly the env keys Connect added, and only while they still hold its values.
            var env = ClaudeJson.Obj(root, "env");
            if (env != null)
            {
                var ours = OurEnv();
                foreach (var t in Telemetry(TelemetryPort))
                {
                    if (!ours.Contains(t[0]) || !env.Contains(t[0])) continue;
                    string value = Text(env.Get(t[0]));
                    if (t[0] == Endpoint ? value.StartsWith(Loopback, StringComparison.Ordinal) : value == t[1]) env.Remove(t[0]);
                }
                if (env.Count == 0) root.Remove("env");
            }
            if (Ours(ClaudeJson.Obj(root, "statusLine")))
            {
                JsonObject restored = null;
                try { string previous = ClaudeBridge.PreviousStatusLinePath(BridgeFolder); if (File.Exists(previous)) restored = ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(previous, Encoding.UTF8))); }
                catch (Exception) { restored = null; }
                if (restored != null) root["statusLine"] = restored; else root.Remove("statusLine");
            }
            Save(root);
            ForgetPrevious();
        }

        // After an update, Claude Code should run the bridge that came with it. A copy in use stays until next time.
        public void RefreshHook(string hookSource)
        {
            try
            {
                if (String.IsNullOrEmpty(hookSource) || !File.Exists(hookSource) || !File.Exists(HookPath)) return;
                var a = new FileInfo(hookSource); var b = new FileInfo(HookPath);
                if (a.Length == b.Length && File.ReadAllBytes(hookSource).SequenceEqual(File.ReadAllBytes(HookPath))) return;
                CopyHook(hookSource);
            }
            catch (Exception) { }
        }

        private void CopyHook(string hookSource)
        {
            if (String.Equals(Path.GetFullPath(hookSource), Path.GetFullPath(HookPath), StringComparison.OrdinalIgnoreCase)) return;
            string temp = HookPath + ".new";
            File.Copy(hookSource, temp, true);
            try { if (File.Exists(HookPath)) File.Replace(temp, HookPath, null, true); else File.Move(temp, HookPath); }
            catch (IOException) { File.Delete(temp); throw new InvalidOperationException("Claude Code is using the bridge right now. Try again in a moment."); }
        }

        private void ForgetPrevious()
        {
            try { string previous = ClaudeBridge.PreviousStatusLinePath(BridgeFolder); if (File.Exists(previous)) File.Delete(previous); } catch (Exception) { }
            try { if (File.Exists(EnvRecordPath)) File.Delete(EnvRecordPath); } catch (Exception) { }
        }

        private JsonObject Load()
        {
            if (!File.Exists(SettingsPath)) return new JsonObject();
            string text;
            try { text = File.ReadAllText(SettingsPath, Encoding.UTF8); }
            catch (Exception e) { throw new InvalidOperationException("Couldn't read " + SettingsPath + ": " + e.Message); }
            if (String.IsNullOrWhiteSpace(text)) return new JsonObject();
            object parsed;
            if (!ClaudeJson.TryParse(text, out parsed) || !(parsed is JsonObject)) throw new InvalidOperationException("Claude Code's settings.json isn't valid JSON, so Widget Screen left it alone. Fix the file (or let Claude Code rewrite it), then try again.");
            return (JsonObject)parsed;
        }

        private void Backup()
        {
            if (!File.Exists(SettingsPath)) return;
            File.Copy(SettingsPath, SettingsPath + BackupSuffix, true);
            string dated = SettingsPath + BackupSuffix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            for (int n = 2; File.Exists(dated); n++) dated = SettingsPath + BackupSuffix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + n;
            File.Copy(SettingsPath, dated);
            try
            {
                foreach (var old in DatedBackups().Skip(KeptBackups)) File.Delete(old);
            }
            catch (Exception) { }
        }
        // The dated backups, newest first.
        public List<string> DatedBackups()
        {
            string folder = Path.GetDirectoryName(SettingsPath), prefix = Path.GetFileName(SettingsPath) + BackupSuffix + "-";
            if (!Directory.Exists(folder)) return new List<string>();
            return Directory.GetFiles(folder, prefix + "*").OrderByDescending(f => File.GetLastWriteTimeUtc(f)).ThenByDescending(f => f, StringComparer.Ordinal).ToList();
        }

        private void Save(JsonObject root)
        {
            Directory.CreateDirectory(ConfigFolder);
            ClaudeBridge.WriteAtomic(SettingsPath, ClaudeJson.Write(root) + "\n");
        }

        // Removes every hook command of ours; groups and event lists left empty go too, and an empty hooks object.
        private static void RemoveOurs(JsonObject root)
        {
            var hooks = ClaudeJson.Obj(root, "hooks");
            if (hooks == null) return;
            foreach (var key in hooks.Select(p => p.Key).ToList())
            {
                var list = hooks.Get(key) as List<object>;
                if (list == null) continue;
                bool touched = false;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var group = list[i] as JsonObject; var inner = ClaudeJson.List(group, "hooks");
                    if (inner == null || !inner.Any(Ours)) continue;
                    inner.RemoveAll(Ours); touched = true;
                    if (inner.Count == 0) list.RemoveAt(i);
                }
                if (touched && list.Count == 0) hooks.Remove(key);
            }
            if (hooks.Count == 0) root.Remove("hooks");
        }
    }
}
