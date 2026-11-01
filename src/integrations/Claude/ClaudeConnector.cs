using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WidgetScreen
{
    // Settings → Claude → Connect Claude Code (docs/design/claude-watcher.md, decision 014): adds Widget Screen's hooks
    // and status line to Claude Code's settings.json, and Disconnect takes exactly those out again. Other hooks are
    // never replaced, other keys stay where they were, a backup is saved next to the file first, and a file that
    // isn't valid JSON is left alone. Only the user's tap on Connect or Disconnect runs this against the real folder;
    // the checks run it in temporary folders.
    public sealed class ClaudeConnector
    {
        public const string BackupSuffix = ".widgetscreen-backup";
        // The events the bridge listens to. Notification only for questions (a permission or an elicitation).
        public static readonly string[][] Events = {
            new[] { "SessionStart", null }, new[] { "UserPromptSubmit", null }, new[] { "PostToolUse", "*" },
            new[] { "Notification", "permission_prompt|elicitation_dialog" }, new[] { "Stop", null }, new[] { "SessionEnd", null } };

        public readonly string ConfigFolder, BridgeFolder;
        public ClaudeConnector(string configFolder, string bridgeFolder) { ConfigFolder = configFolder; BridgeFolder = bridgeFolder; }

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

        // Copies the bridge into its stable folder, then adds the hooks and status line. hookSource is the
        // ClaudeHook.exe that came with this app. Throws InvalidOperationException with a message for the user.
        public void Connect(string hookSource)
        {
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
            root["statusLine"] = ours;
            Save(root);
        }

        // Takes out exactly Widget Screen's entries and puts the earlier status line back.
        public void Disconnect()
        {
            if (!File.Exists(SettingsPath)) { ForgetPrevious(); return; }
            var root = Load();
            Backup();
            RemoveOurs(root);
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

        private void ForgetPrevious() { try { string previous = ClaudeBridge.PreviousStatusLinePath(BridgeFolder); if (File.Exists(previous)) File.Delete(previous); } catch (Exception) { } }

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
            if (File.Exists(SettingsPath)) File.Copy(SettingsPath, SettingsPath + BackupSuffix, true);
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
