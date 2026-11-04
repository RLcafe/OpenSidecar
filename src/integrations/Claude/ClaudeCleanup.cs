using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // The simple Claude card (decision 027) no longer hooks into Claude Code. Earlier versions' Connect Claude Code added
    // hooks and a status line that ran ClaudeHook.exe, and telemetry settings that sent to a receiver on this PC. This
    // takes exactly those out of Claude Code's settings.json once, after a backup next to it (settings.json is left
    // alone if it isn't valid JSON), puts the user's earlier status line back, and deletes the old bridge folder beside
    // settings.xml. Other hooks, keys and env values stay as they are.
    public sealed class ClaudeCleanup
    {
        public const string Marker = "ClaudeHook.exe";
        public const string BackupSuffix = ".widgetscreen-backup";
        // What Connect set in env (the endpoint only when it still points at this PC).
        private static readonly string[][] TelemetryEnv = {
            new[] { "CLAUDE_CODE_ENABLE_TELEMETRY", "1" }, new[] { "OTEL_METRICS_EXPORTER", "otlp" }, new[] { "OTEL_LOGS_EXPORTER", "otlp" },
            new[] { "OTEL_EXPORTER_OTLP_PROTOCOL", "http/json" }, new[] { "OTEL_EXPORTER_OTLP_ENDPOINT", "http://127.0.0.1:" },
            new[] { "OTEL_METRIC_EXPORT_INTERVAL", "10000" }, new[] { "OTEL_METRICS_INCLUDE_REPOSITORY", "true" }, new[] { "OTEL_METRICS_INCLUDE_ENTRYPOINT", "true" } };

        public readonly string ConfigFolder, BridgeFolder;
        public ClaudeCleanup(string configFolder, string bridgeFolder) { ConfigFolder = configFolder; BridgeFolder = bridgeFolder; }
        public string SettingsPath { get { return Path.Combine(ConfigFolder, "settings.json"); } }
        private string PreviousStatusLine { get { return Path.Combine(BridgeFolder, "previous-statusline.json"); } }
        private string EnvRecord { get { return Path.Combine(BridgeFolder, "connected-env.json"); } }

        // %USERPROFILE%\.claude, or CLAUDE_CONFIG_DIR when Claude Code is set up to use another folder (this process's,
        // or the user's own).
        public static string DefaultConfigFolder
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
                if (String.IsNullOrWhiteSpace(configured)) { try { configured = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR", EnvironmentVariableTarget.User); } catch (Exception) { configured = null; } }
                if (!String.IsNullOrWhiteSpace(configured)) return Environment.ExpandEnvironmentVariables(configured.Trim());
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            }
        }

        public static bool Ours(object hook)
        {
            var command = ClaudeJson.Str(hook as JsonObject, "command");
            return command != null && command.Replace('\\', '/').IndexOf(Marker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Whether settings.json still has the old hooks or status line. False when the file is missing or unreadable.
        public bool Connected()
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

        // Takes the old connection out, if it's there, then deletes the bridge folder. Returns null when nothing is
        // left, or what's in the way.
        public string Run()
        {
            string problem = null;
            try { if (Connected()) Remove(); }
            catch (Exception e) { problem = e is InvalidOperationException ? e.Message : "Couldn't update Claude Code's settings.json: " + e.Message; }
            if (problem == null)
            {
                try { if (Directory.Exists(BridgeFolder)) Directory.Delete(BridgeFolder, true); }
                catch (Exception) { /* ClaudeHook.exe may be running for a session started earlier; the next start tries again. */ }
            }
            return problem;
        }

        private void Remove()
        {
            string text = File.ReadAllText(SettingsPath, Encoding.UTF8);
            object parsed;
            if (!ClaudeJson.TryParse(text, out parsed) || !(parsed is JsonObject)) throw new InvalidOperationException("Claude Code's settings.json isn't valid JSON, so Widget Screen left its old hooks in it. Fix the file (or let Claude Code rewrite it), then restart Widget Screen.");
            var root = (JsonObject)parsed;
            File.Copy(SettingsPath, SettingsPath + BackupSuffix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), true);
            var hooks = ClaudeJson.Obj(root, "hooks");
            if (hooks != null)
            {
                foreach (var key in hooks.Select(p => p.Key).ToList())
                {
                    var list = hooks.Get(key) as List<object>;
                    if (list == null) continue;
                    bool touched = false;
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var inner = ClaudeJson.List(list[i] as JsonObject, "hooks");
                        if (inner == null || !inner.Any(Ours)) continue;
                        inner.RemoveAll(Ours); touched = true;
                        if (inner.Count == 0) list.RemoveAt(i);
                    }
                    if (touched && list.Count == 0) hooks.Remove(key);
                }
                if (hooks.Count == 0) root.Remove("hooks");
            }
            // The env keys Connect recorded, and only while they still hold its values.
            var env = ClaudeJson.Obj(root, "env");
            if (env != null)
            {
                var recorded = new HashSet<string>(StringComparer.Ordinal);
                try { if (File.Exists(EnvRecord)) foreach (var k in (ClaudeJson.List(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(EnvRecord, Encoding.UTF8))), "keys") ?? new List<object>()).OfType<string>()) recorded.Add(k); }
                catch (Exception) { }
                foreach (var pair in TelemetryEnv)
                {
                    if (!recorded.Contains(pair[0]) || !env.Contains(pair[0])) continue;
                    object raw = env.Get(pair[0]);
                    string value = Convert.ToString(raw is JsonNumber ? (object)((JsonNumber)raw).Value : raw, CultureInfo.InvariantCulture) ?? "";
                    if (pair[0] == "OTEL_EXPORTER_OTLP_ENDPOINT" ? value.StartsWith(pair[1], StringComparison.Ordinal) : value == pair[1]) env.Remove(pair[0]);
                }
                if (env.Count == 0) root.Remove("env");
            }
            if (Ours(ClaudeJson.Obj(root, "statusLine")))
            {
                JsonObject restored = null;
                try { if (File.Exists(PreviousStatusLine)) restored = ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(PreviousStatusLine, Encoding.UTF8))); } catch (Exception) { restored = null; }
                if (restored != null) root["statusLine"] = restored; else root.Remove("statusLine");
            }
            string temp = SettingsPath + ".widgetscreen-tmp";
            File.WriteAllText(temp, ClaudeJson.Write(root) + "\n", new UTF8Encoding(false));
            File.Replace(temp, SettingsPath, null, true);
        }
    }
}
