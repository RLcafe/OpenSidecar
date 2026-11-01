using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using WidgetScreen;

// Macros (docs/design/macros.md): checking, describing and confirming steps, running a chain on the dashboard, and
// building one in Settings → Macros. The chains run here only wait and toggle Quiet mode in test settings; nothing is
// launched and no keys are sent.
internal static class MacroTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static void Click(Window window, string name) { Click((Button)window.FindName(name)); }
    private static void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); System.Threading.Thread.Sleep(10); }
    }
    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            var element = child as DependencyObject; if (element == null) continue;
            yield return element;
            foreach (var deeper in All(element)) yield return deeper;
        }
    }
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject
    {
        var found = All(root).OfType<T>().FirstOrDefault(e => AutomationProperties.GetName(e) == name);
        if (found == null) throw new Exception("Missing " + typeof(T).Name + " named " + name);
        return found;
    }

    private static MacroStep Wait(int ms) { return new MacroStep { Kind = "Delay", DelayMs = ms }; }
    private static MacroStep Quiet() { return new MacroStep { Kind = "Control", Target = "quiet" }; }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Macro steps are checked, described and confirmed like Quick Launch buttons", () => {
            Assert(MacroActions.Validate(Wait(500)) == null && MacroActions.Validate(Wait(50)) != null && MacroActions.Validate(Wait(40000)) != null, "Waits aren't checked.");
            Assert(MacroActions.Validate(new MacroStep { Kind = "Hotkey", Target = "Ctrl+Shift+S" }) == null && MacroActions.Validate(new MacroStep { Kind = "Hotkey", Target = "Banana" }) != null, "Hotkeys aren't checked.");
            Assert(MacroActions.Validate(Quiet()) == null && MacroActions.Validate(new MacroStep { Kind = "Control", Target = "explode" }) != null && MacroActions.Validate(new MacroStep { Kind = "Teleport" }) != null, "Controls or kinds aren't checked.");
            string script = Path.Combine(directory, "macro-test.ps1"); File.WriteAllText(script, "# test fixture, never run");
            Assert(MacroActions.Validate(new MacroStep { Kind = "Script", Target = script }) == null && MacroActions.Validate(new MacroStep { Kind = "Script", Target = Path.Combine(directory, "missing.ps1") }) != null && MacroActions.Validate(new MacroStep { Kind = "Script", Target = Path.Combine(directory, "macro-settings.xml") }) != null, "Scripts aren't checked.");
            var safe = new Macro { Label = "Quiet break", Steps = new[] { Quiet(), Wait(1500) } };
            Assert(!MacroActions.NeedsConfirmation(safe) && MacroActions.Validate(safe) == null, "A safe macro asks first or fails.");
            Assert(MacroActions.NeedsConfirmation(new Macro { Label = "x", Steps = new[] { new MacroStep { Kind = "Script", Target = script } } }), "A script doesn't ask first.");
            Assert(MacroActions.NeedsConfirmation(new Macro { Label = "x", Steps = new[] { new MacroStep { Kind = "Hotkey", Target = "Alt+F4" } } }) && MacroActions.NeedsConfirmation(new Macro { Label = "x", Confirm = true, Steps = new[] { Wait(500) } }), "Alt+F4 or Ask first doesn't ask.");
            Assert(MacroActions.Validate(new Macro { Label = "x", Steps = new MacroStep[0] }) == "Add at least one step." && MacroActions.Validate(new Macro { Label = "x", Steps = new[] { Wait(500), Wait(10) } }).StartsWith("Step 2:"), "Macro problems don't name the step.");
            Assert(MacroActions.Describe(Wait(1500)) == "Wait 1.5 s" && MacroActions.Describe(Quiet()) == "Control  ·  Toggle Quiet mode" && MacroActions.Describe(new MacroStep { Kind = "Hotkey", Target = "Ctrl+C" }) == "Hotkey  ·  Ctrl+C" && MacroActions.Summary(safe) == "2 steps", "Steps are described wrongly.");
            Assert(MacroActions.Detail(safe) == "1.  Control  ·  Toggle Quiet mode\n2.  Wait 1.5 s", "The confirm sheet's list is wrong.");
            var cleaned = MacroActions.Clean(new[] { new Macro { Label = null, Color = "Pink", Steps = new[] { new MacroStep { Kind = "Delay", DelayMs = 99999 }, null, new MacroStep { Kind = "Nope" } } }, null });
            Assert(cleaned.Count == 1 && cleaned[0].Label == "Macro" && cleaned[0].Color == "" && cleaned[0].Steps.Length == 2 && cleaned[0].Steps[0].DelayMs == 30000 && cleaned[0].Steps[1].Kind == "Delay", "Saved macros aren't cleaned.");
        });
        check("Macros are saved, start hidden and keep six buttons a page", () => {
            string path = Path.Combine(directory, "macro-settings.xml"), warning;
            Assert((new AppSettings().Macros ?? new Macro[0]).Length == 0, "There are macros by default.");
            SettingsStore.Save(path, new AppSettings { Macros = new[] { new Macro { Label = "Break", Glyph = "", Color = "Amber", Steps = new[] { Quiet(), Wait(800) } } } });
            var loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.Macros.Length == 1 && loaded.Macros[0].Label == "Break" && loaded.Macros[0].Steps[1].DelayMs == 800 && loaded.Macros[0].Color == "Amber" && loaded.Copy().Macros[0] != loaded.Macros[0], "Macros weren't saved or copied.");
            var settings = new AppSettings();
            Assert(WidgetLayout.Ids.Contains("Macros") && !WidgetLayout.SpotifyDefault(settings).First(p => p.Id == "Macros").Visible && WidgetLayout.KeepsSix("Macros") && WidgetLayout.Minimum("Macros") == new Size(288, 204), "The Macros widget isn't set up like Quick Launch.");
        });
        check("A macro runs its steps in order, and one that asks first waits for the confirm sheet", () => {
            string path = Path.Combine(directory, "macro-run.xml");
            var settings = new AppSettings { Macros = new[] {
                new Macro { Label = "Quiet then wait", Glyph = "", Steps = new[] { Quiet(), Wait(300), Quiet(), Wait(100), Quiet() } },
                new Macro { Label = "Asks", Confirm = true, Steps = new[] { Quiet() } } } };
            settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Macros"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = 472; p.Height = 250; } return p; }).ToArray();
            SettingsStore.Save(path, settings);
            // Awaits resume on the UI thread, as in the app (Program.Main sets the same context).
            var context = System.Threading.SynchronizationContext.Current;
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try {
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                try
                {
                    using (var controller = new DashboardController(window, path, true))
                    {
                        var grid = (System.Windows.Controls.Primitives.UniformGrid)window.FindName("MacroGrid");
                        Assert(grid.Children.Count == 2 && grid.Columns * grid.Rows >= 2, "The macro buttons weren't built.");
                        string warning;
                        Click((Button)grid.Children[0]);
                        Pump(80);
                        Assert(controller.MacroRunning == 0 && SettingsStore.Load(path, out warning).QuietMode, "The first step didn't run, or the macro isn't shown running.");
                        Click((Button)grid.Children[1]); Assert(((TextBlock)window.FindName("StatusMessage")).Text.Contains("still running"), "A second macro started while one ran.");
                        Pump(700);
                        Assert(controller.MacroRunning == -1 && SettingsStore.Load(path, out warning).QuietMode && ((TextBlock)window.FindName("StatusMessage")).Text == "Ran Quiet then wait.", "The steps didn't all run in order: running " + controller.MacroRunning + ", quiet " + SettingsStore.Load(path, out warning).QuietMode + ", status " + ((TextBlock)window.FindName("StatusMessage")).Text);
                        Click((Button)grid.Children[1]); Pump(50);
                        Assert(((Grid)window.FindName("ConfirmOverlay")).Visibility == Visibility.Visible && ((TextBlock)window.FindName("ConfirmDetail")).Text == "1.  Control  ·  Toggle Quiet mode", "Ask first didn't show the steps.");
                        Click(window, "ConfirmCancel"); Pump(50);
                        Assert(SettingsStore.Load(path, out warning).QuietMode, "Cancel ran the macro.");
                        Click((Button)grid.Children[1]); Pump(50); Click(window, "ConfirmAction"); Pump(100);
                        Assert(!SettingsStore.Load(path, out warning).QuietMode, "Run didn't run the macro.");
                    }
                }
                finally { window.Close(); }
            }
            } finally { System.Threading.SynchronizationContext.SetSynchronizationContext(context); }
        });
        check("Settings → Macros builds a macro step by step and refuses a broken one", () => {
            string path = Path.Combine(directory, "macro-editor.xml");
            SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                try
                {
                    using (var controller = new DashboardController(window, path, true))
                    {
                        Click(window, "SettingsButton");
                        var host = (Grid)window.FindName("MacroEditorHost");
                        Click(Named<Button>(host, "Add a macro"));
                        Named<TextBox>(host, "Macro name").Text = "Fixture break";
                        Named<ComboBox>(host, "Step type").SelectedItem = MacroActions.Kinds.First(k => k.Id == "Control");
                        Named<ComboBox>(host, "Step control").SelectedItem = DeckActions.Controls.First(c => c.Id == "quiet");
                        Click(Named<Button>(host, "Add this step"));
                        Named<ComboBox>(host, "Step type").SelectedItem = MacroActions.Kinds.First(k => k.Id == "Delay");
                        Named<Slider>(host, "Wait length").Value = 2500;
                        Click(Named<Button>(host, "Add this step"));
                        Named<ComboBox>(host, "Step type").SelectedItem = MacroActions.Kinds.First(k => k.Id == "Hotkey");
                        Named<TextBox>(host, "Step target").Text = "not a hotkey";
                        Click(Named<Button>(host, "Add this step"));
                        Assert(((TextBlock)window.FindName("SettingsStatus")).Text.StartsWith("Use modifiers"), "A broken step was added without a word.");
                        Click(Named<Button>(host, "Move step 2 earlier"));
                        Click(window, "SaveSettings");
                        string warning; var saved = SettingsStore.Load(path, out warning).Macros;
                        Assert(saved.Length == 1 && saved[0].Label == "Fixture break" && saved[0].Steps.Length == 2 && saved[0].Steps[0].Kind == "Delay" && saved[0].Steps[0].DelayMs == 2500 && saved[0].Steps[1].Target == "quiet", "The macro wasn't saved as built.");
                        Click(window, "SettingsButton");
                        Click(Named<Button>(host, "Add a macro"));
                        Click(window, "SaveSettings");
                        Assert(((TextBlock)window.FindName("SettingsStatus")).Text == "Macro 2 (New macro): Add at least one step." && SettingsStore.Load(path, out warning).Macros.Length == 1, "A macro without steps was saved.");
                    }
                }
                finally { window.Close(); }
            }
        });
    }
}
