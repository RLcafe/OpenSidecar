using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace WidgetScreen
{
    // One step of a macro (docs/design/macros.md). Kind is one of MacroActions.Kinds; Target is what the matching
    // Quick Launch action takes (a hotkey, a path, an address, a control id), DelayMs is for Delay.
    public sealed class MacroStep
    {
        public string Kind { get; set; }
        public string Target { get; set; }
        public string Arguments { get; set; }
        public int DelayMs { get; set; }
        public MacroStep Copy() { return (MacroStep)MemberwiseClone(); }
    }

    // A Macros button: a name, an icon and colour like a Quick Launch button, and the steps it runs in order.
    public sealed class Macro
    {
        public string Label { get; set; }
        public string Glyph { get; set; }
        public string Color { get; set; }
        public bool Confirm { get; set; }
        [XmlArray("Steps"), XmlArrayItem("Step")]
        public MacroStep[] Steps { get; set; }
        public Macro Copy() { var copy = (Macro)MemberwiseClone(); copy.Steps = Steps == null ? null : Steps.Where(s => s != null).Select(s => s.Copy()).ToArray(); return copy; }
    }

    // What macro steps can do, how they're described and checked, and which ones ask first. Every step but Delay and
    // Script is a Quick Launch action, so it's validated, described and confirmed by DeckActions.
    public static class MacroActions
    {
        public const int MaxMacros = 60, MaxSteps = 12, MinDelay = 100, MaxDelay = 30000;
        public static readonly DeckOption[] Kinds = {
            DeckActions.Option("Hotkey", "Keyboard shortcut"), DeckActions.Option("App", "Open an app"), DeckActions.Option("File", "Open a file"),
            DeckActions.Option("Folder", "Open a folder"), DeckActions.Option("Website", "Open a website"), DeckActions.Option("Control", "Dashboard / media control"),
            DeckActions.Option("Delay", "Wait"), DeckActions.Option("Script", "Run a script")
        };
        public static readonly string[] ScriptExtensions = { ".ps1", ".bat", ".cmd", ".vbs" };

        public static Shortcut AsShortcut(MacroStep step) { return new Shortcut { Label = "Step", Action = step.Kind, Target = step.Target ?? "", Arguments = step.Arguments ?? "" }; }

        public static string Validate(MacroStep step)
        {
            if (step == null || !Kinds.Any(k => k.Id == step.Kind)) return "Choose what this step does.";
            if (step.Kind == "Delay") return step.DelayMs >= MinDelay && step.DelayMs <= MaxDelay ? null : "A wait is 0.1 to 30 seconds.";
            if (step.Kind == "Script")
            {
                string path = DeckActions.Expanded(step.Target);
                if (path.Length == 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathRooted(path)) return "Browse to the script using its full path.";
                if (!ScriptExtensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return "A script is a .ps1, .bat, .cmd or .vbs file.";
                return File.Exists(path) ? null : "This script is unavailable. Reconnect its drive or choose another file.";
            }
            return DeckActions.Validate(AsShortcut(step));
        }
        public static string Validate(Macro macro)
        {
            if (String.IsNullOrWhiteSpace(macro.Label)) return "Give this macro a name.";
            var steps = macro.Steps ?? new MacroStep[0];
            if (steps.Length == 0) return "Add at least one step.";
            for (int i = 0; i < steps.Length; i++) { string error = Validate(steps[i]); if (error != null) return "Step " + (i + 1) + ": " + error; }
            return null;
        }

        // Scripts always ask; other steps ask when the same Quick Launch button would (arguments, Delete, Alt+F4, script files).
        public static bool NeedsConfirmation(MacroStep step) { return step.Kind == "Script" || (step.Kind != "Delay" && DeckActions.NeedsConfirmation(AsShortcut(step))); }
        public static bool NeedsConfirmation(Macro macro) { return macro.Confirm || (macro.Steps ?? new MacroStep[0]).Any(NeedsConfirmation); }

        public static string Seconds(int ms) { return (ms / 1000.0).ToString(ms % 1000 == 0 ? "0" : ms % 100 == 0 ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture) + " s"; }
        // "Hotkey · Ctrl+C", "Wait 0.5 s", "Script · C:\Tools\backup.ps1", "Control · Mute / unmute microphone".
        public static string Describe(MacroStep step)
        {
            if (step.Kind == "Delay") return "Wait " + Seconds(step.DelayMs);
            if (step.Kind == "Script") return "Script  \u00b7  " + (step.Target ?? "") + (String.IsNullOrWhiteSpace(step.Arguments) ? "" : " " + step.Arguments);
            var kind = step.Kind == "Control" ? "Control" : step.Kind;
            return kind + "  \u00b7  " + DeckActions.Describe(AsShortcut(step));
        }
        public static string Summary(Macro macro) { int n = (macro.Steps ?? new MacroStep[0]).Length; return n == 1 ? "1 step" : n + " steps"; }
        public static string Detail(Macro macro) { var steps = macro.Steps ?? new MacroStep[0]; return String.Join("\n", steps.Select((s, i) => (i + 1) + ".  " + Describe(s))); }

        // Up to 60 macros of up to 12 steps; a wait is clamped to 0.1–30 s, and missing parts get their defaults.
        public static List<Macro> Clean(IEnumerable<Macro> macros)
        {
            var result = new List<Macro>();
            foreach (var macro in macros ?? new Macro[0])
            {
                if (macro == null || result.Count >= MaxMacros) continue;
                macro.Label = macro.Label ?? "Macro";
                if (String.IsNullOrEmpty(macro.Glyph)) macro.Glyph = "\uE768";
                if (macro.Color != "Lime" && macro.Color != "Glacier" && macro.Color != "Lavender" && macro.Color != "Amber") macro.Color = "";
                macro.Steps = (macro.Steps ?? new MacroStep[0]).Where(s => s != null).Take(MaxSteps).ToArray();
                foreach (var step in macro.Steps)
                {
                    if (!Kinds.Any(k => k.Id == step.Kind)) step.Kind = "Delay";
                    step.Target = step.Target ?? ""; step.Arguments = step.Arguments ?? "";
                    if (step.Kind == "Delay") step.DelayMs = Math.Max(MinDelay, Math.Min(MaxDelay, step.DelayMs == 0 ? 500 : step.DelayMs));
                }
                result.Add(macro);
            }
            return result;
        }

        // Starts a script without waiting for it: PowerShell scripts through powershell.exe (so they run instead of
        // opening in Notepad), batch files through cmd.exe, VBScript through wscript.exe.
        public static void StartScript(MacroStep step)
        {
            string error = Validate(step); if (error != null) throw new InvalidOperationException(error);
            string path = DeckActions.Expanded(step.Target), extension = Path.GetExtension(path).ToLowerInvariant(), arguments = step.Arguments ?? "";
            if (extension == ".ps1") ProcessLauncher.Start("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\" " + arguments, Path.GetDirectoryName(path));
            else if (extension == ".vbs") ProcessLauncher.Start("wscript.exe", "\"" + path + "\" " + arguments, Path.GetDirectoryName(path));
            else ProcessLauncher.Start("cmd.exe", "/c \"\"" + path + "\" " + arguments + "\"", Path.GetDirectoryName(path));
        }
    }
}
