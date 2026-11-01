using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace WidgetScreen
{
    public sealed class DeckOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public override string ToString() { return Name; }
    }

    public static class DeckActions
    {
        public static readonly DeckOption[] Types = {
            Option("App", "Open an app"), Option("File", "Open a file"), Option("Folder", "Open a folder"),
            Option("Website", "Open a website"), Option("Hotkey", "Keyboard shortcut"), Option("Control", "Dashboard / media control")
        };
        public static readonly DeckOption[] Controls = {
            Option("play", "Play / pause"), Option("previous", "Previous track"), Option("next", "Next track"),
            Option("mute", "Mute / unmute sound"), Option("microphone", "Mute / unmute microphone"),
            Option("volume-up", "Volume up 5%"), Option("volume-down", "Volume down 5%"),
            Option("focus", "Start / pause focus timer"), Option("quiet", "Toggle Quiet mode"), Option("sound", "Windows sound settings")
        };
        public static readonly DeckOption[] Icons = {
            Option("\uE8A7", "App"), Option("\uE774", "Globe"), Option("\uE8B7", "Folder"),
            Option("\uE8EF", "Calculator"), Option("\uE70B", "Notes"), Option("\uE8D6", "Music"),
            Option("\uE767", "Sound"), Option("\uE720", "Microphone"), Option("\uE765", "Keyboard"),
            Option("\uE768", "Play"), Option("\uE916", "Clock"), Option("\uE713", "Settings"),
            Option("\uE734", "Star"), Option("\uE722", "Camera"), Option("\uE7F4", "Game"), Option("\uE8A5", "Document")
        };
        // A button's glyph colour: the theme's accent, or one of the four accents so buttons tell apart at a glance.
        public static readonly DeckOption[] Colors = { Option("", "Theme accent"), Option("Lime", "Lime"), Option("Glacier", "Glacier"), Option("Lavender", "Lavender"), Option("Amber", "Amber") };
        public static string AccentHex(string name)
        {
            return name == "Glacier" ? "#8ED8F8" : name == "Lavender" ? "#C5AEFF" : name == "Amber" ? "#F5C47D" : name == "Lime" ? "#BAF28B" : null;
        }
        // Control buttons that show a state: on when the thing they toggle is on.
        public static bool IsStateful(Shortcut shortcut)
        {
            return shortcut.Action == "Control" && (shortcut.Target == "mute" || shortcut.Target == "microphone" || shortcut.Target == "focus" || shortcut.Target == "quiet");
        }
        // "Ctrl+Shift+S → Chrome" for a hotkey, once the app it would go to is known.
        public static string DescribeWithTarget(Shortcut shortcut, string targetApp)
        {
            string text = Describe(shortcut);
            if (shortcut.Action == "Hotkey") text += String.IsNullOrEmpty(targetApp) ? "  →  select an app first" : "  →  " + targetApp;
            return text;
        }
        public static DeckOption Option(string id, string name) { return new DeckOption { Id = id, Name = name }; }
        public static string InferAction(string target)
        {
            Uri uri;
            if (Uri.TryCreate(target, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http")) return "Website";
            return "App";
        }
        public static string Expanded(string target) { return Environment.ExpandEnvironmentVariables((target ?? "").Trim()); }
        public static bool IsAppUri(string target)
        {
            Uri uri;
            if (!Uri.TryCreate(target, UriKind.Absolute, out uri) || uri.IsFile || target.Any(Char.IsWhiteSpace)) return false;
            if (new[] { "javascript", "vbscript", "data", "http", "https", "shell" }.Contains(uri.Scheme)) return false;
            if (uri.Scheme == "spotify" || uri.Scheme == "ms-settings") return true;
            using (var key = Registry.ClassesRoot.OpenSubKey(uri.Scheme)) return key != null && key.GetValue("URL Protocol") != null;
        }
        public static string Validate(Shortcut shortcut)
        {
            if (String.IsNullOrWhiteSpace(shortcut.Label)) return "Give this button a name.";
            string target = Expanded(shortcut.Target), action = shortcut.Action ?? InferAction(target);
            if (target.Length == 0) return "Choose an action or enter a target for this button.";
            if (action == "Control") return Controls.Any(c => c.Id == target) ? null : "Choose a control from the list.";
            if (action == "Hotkey") { ushort[] keys; return DeckHotkeys.TryParse(target, out keys) ? null : "Use modifiers and one key, such as Ctrl+Shift+S or Win+D."; }
            if (action == "Website")
            {
                Uri uri; return Uri.TryCreate(target, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && !String.IsNullOrEmpty(uri.Host) ? null : "Enter a complete website address starting with https:// or http://.";
            }
            if (action == "App" && IsAppUri(target)) return String.IsNullOrWhiteSpace(shortcut.Arguments) ? null : "App links do not accept a separate arguments field.";
            if (target.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(target)) return "Browse to an existing app, file or folder using its full path.";
            if (action == "Folder") return Directory.Exists(target) ? null : "This folder is unavailable. Reconnect its drive or choose another folder.";
            if (action == "File") return File.Exists(target) ? null : "This file is unavailable. Reconnect its drive or choose another file.";
            if (action == "App") return File.Exists(target) && (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) ? null : "Choose an existing .exe, Start menu shortcut (.lnk), or installed app link.";
            return "This action type is unavailable. Choose another action.";
        }
        public static bool NeedsConfirmation(Shortcut shortcut)
        {
            ushort[] keys;
            if (shortcut.Action == "Hotkey" && DeckHotkeys.TryParse(shortcut.Target, out keys) &&
                (keys.Last() == 0x2E || (keys.Contains((ushort)0x12) && keys.Last() == 0x73))) return true;
            string extension = Path.GetExtension(shortcut.Target ?? "").ToLowerInvariant();
            return shortcut.Confirm || !String.IsNullOrWhiteSpace(shortcut.Arguments) ||
                (shortcut.Action == "File" && new[] { ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf", ".reg", ".msi" }.Contains(extension));
        }
        // The line under a Large Quick Launch tile (docs/design/widget-size-views.md): the kind of action and, for a
        // website or hotkey, where it goes. The phone gets it as kind.
        public static string Kind(Shortcut shortcut)
        {
            if (shortcut.Action == "Hotkey") return "Hotkey  ·  " + shortcut.Target;
            Uri uri;
            if (shortcut.Action == "Website") return Uri.TryCreate(shortcut.Target ?? "", UriKind.Absolute, out uri) && !String.IsNullOrEmpty(uri.Host) ? "Website  ·  " + uri.Host : "Website";
            return shortcut.Action ?? "";
        }
        public static string Describe(Shortcut shortcut)
        {
            var control = Controls.FirstOrDefault(c => shortcut.Action == "Control" && c.Id == shortcut.Target);
            return control == null ? shortcut.Target + (String.IsNullOrWhiteSpace(shortcut.Arguments) ? "" : " " + shortcut.Arguments) : control.Name;
        }
        public static void Open(Shortcut shortcut)
        {
            string error = Validate(shortcut); if (error != null) throw new InvalidOperationException(error);
            if (shortcut.Action == "Control" || shortcut.Action == "Hotkey") throw new InvalidOperationException("This action must be dispatched by the deck.");
            ProcessLauncher.Start(Expanded(shortcut.Target), shortcut.Action == "App" ? shortcut.Arguments ?? "" : "", null);
        }
        public static List<Shortcut> Presets()
        {
            var list = AppSettings.DefaultShortcuts();
            foreach (var item in list) item.Action = InferAction(item.Target);
            list.Add(new Shortcut { Label = "Screenshot", Action = "Hotkey", Target = "Win+Shift+S", Glyph = "\uE722" });
            list.AddRange(Controls.Select(c => new Shortcut { Label = c.Name, Action = "Control", Target = c.Id, Glyph = c.Id == "microphone" ? "\uE720" : c.Id == "focus" ? "\uE916" : "\uE767" }));
            return list;
        }
        // On-demand Start menu indexing; no background polling or shell execution.
        public static List<DeckOption> StartMenuApps()
        {
            var result = new List<DeckOption>();
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) })
                AddDirectory(root, result, 0);
            return result.GroupBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).Select(g => g.First()).OrderBy(a => a.Name).ToList();
        }
        private static void AddDirectory(string path, List<DeckOption> result, int depth)
        {
            if (depth > 8 || !Directory.Exists(path)) return;
            try
            {
                foreach (string file in Directory.GetFiles(path, "*.lnk")) result.Add(Option(file, Path.GetFileNameWithoutExtension(file)));
                foreach (string child in Directory.GetDirectories(path)) if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) AddDirectory(child, result, depth + 1);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
