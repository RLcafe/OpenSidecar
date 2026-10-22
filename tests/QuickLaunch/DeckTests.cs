using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WidgetScreen;

internal static class DeckTests
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Click(Window window, string name) { ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static Window Window() { using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml")) return (Window)System.Windows.Markup.XamlReader.Load(stream); }
    public static void Run(string directory, Action<string, Action> check)
    {
        check("Deck migration preserves old assignments, empty decks and arbitrary counts", () => {
            string path = Path.Combine(directory, "deck-migration.xml"), warning;
            File.WriteAllText(path, "<AppSettings><Shortcuts><Shortcut><Label>Custom</Label><Target>https://example.com</Target><Glyph>X</Glyph></Shortcut></Shortcuts></AppSettings>");
            var settings = SettingsStore.Load(path, out warning);
            Assert(settings.Shortcuts.Count == 1 && settings.Shortcuts[0].Action == "Website" && settings.Shortcuts[0].Glyph == "X", "Old custom button was reset.");
            settings.Shortcuts.Clear(); SettingsStore.Save(path, settings);
            Assert(SettingsStore.Load(path, out warning).Shortcuts.Count == 0, "Empty deck was replaced by defaults.");
            for (int i = 0; i < 19; i++) settings.Shortcuts.Add(new Shortcut { Label = "Action " + i, Action = "Hotkey", Target = "Ctrl+Shift+S", Glyph = "\uE765", Confirm = true });
            SettingsStore.Save(path, settings); settings = SettingsStore.Load(path, out warning);
            Assert(settings.Shortcuts.Count == 19 && settings.Shortcuts[18].Label == "Action 18" && settings.Shortcuts[18].Confirm, "Expanded deck lost order or metadata.");
            File.WriteAllText(path, "<AppSettings />"); Assert(SettingsStore.Load(path, out warning).Shortcuts.Count == 6, "Missing deck no longer inherits defaults.");
        });
        check("Deck validates action-specific targets, arguments and unavailable resources", () => {
            string file = Path.Combine(directory, "deck-example.txt"); File.WriteAllText(file, "Fixture only");
            Assert(DeckActions.Validate(new Shortcut { Label = "File", Action = "File", Target = file }) == null, "Existing file rejected.");
            Assert(DeckActions.Validate(new Shortcut { Label = "Folder", Action = "Folder", Target = directory }) == null, "Existing folder rejected.");
            foreach (var item in new[] {
                new Shortcut { Label = "Bad", Action = "Website", Target = "javascript:alert(1)" },
                new Shortcut { Label = "Bad", Action = "Website", Target = "file:///C:/a.cmd" },
                new Shortcut { Label = "Bad", Action = "App", Target = "cmd.exe /c something" },
                new Shortcut { Label = "Bad", Action = "App", Target = "C:\\missing-app.exe" },
                new Shortcut { Label = "Bad", Action = "File", Target = "C:\\bad|path" },
                new Shortcut { Label = "Bad", Action = "Control", Target = "shutdown" },
                new Shortcut { Label = "Bad", Action = "FutureAction", Target = file }
            }) Assert(DeckActions.Validate(item) != null, "Invalid action accepted: " + item.Target);
            Assert(DeckActions.NeedsConfirmation(new Shortcut { Action = "File", Target = "C:\\example.cmd" }), "Script did not require confirmation.");
            Assert(DeckActions.NeedsConfirmation(new Shortcut { Action = "App", Target = "%windir%\\explorer.exe", Arguments = "/select,example" }), "Arguments did not require confirmation.");
            Assert(DeckActions.NeedsConfirmation(new Shortcut { Action = "Hotkey", Target = "Alt+F4" }), "Close shortcut bypasses confirmation.");
        });
        check("Deck hotkeys parse without sending input", () => {
            ushort[] keys;
            Assert(DeckHotkeys.TryParse("Ctrl+Shift+S", out keys) && keys.SequenceEqual(new ushort[] { 17, 16, 83 }), "Modifier order is incorrect.");
            foreach (string combo in new[] { "Win+D", "Alt+F4", "F5", "Ctrl+1", "Ctrl+OemPlus", "Win+Shift+S" }) Assert(DeckHotkeys.TryParse(combo, out keys), "Valid shortcut rejected: " + combo);
            foreach (string combo in new[] { "Ctrl+Ctrl+A", "Ctrl+", "Ctrl+Alt+Delete", "Ctrl+LeftCtrl", "unrecognized", "Ctrl+A+B" }) Assert(!DeckHotkeys.TryParse(combo, out keys), "Invalid shortcut accepted: " + combo);
        });
        check("Deck editing saves, reorders and cancels without executing actions", () => {
            string path = Path.Combine(directory, "deck-ui.xml"), warning; SettingsStore.Save(path, new AppSettings());
            var window = Window();
            using (var controller = new DashboardController(window, path, true))
            {
                Click(window, "EditDeck"); Assert(((TabControl)window.FindName("SettingsTabs")).SelectedIndex == 5, "Edit does not open Quick Launch settings.");
                Click(window, "DeckAdd");
                ((TextBox)window.FindName("DeckName")).Text = "My site";
                ((ComboBox)window.FindName("DeckAction")).SelectedItem = DeckActions.Types.First(t => t.Id == "Website");
                ((TextBox)window.FindName("DeckTarget")).Text = "https://example.com";
                ((ComboBox)window.FindName("DeckIcon")).SelectedItem = DeckActions.Icons.First(i => i.Name == "Star");
                Click(window, "DeckMoveUp"); Click(window, "SaveSettings");
                var saved = SettingsStore.Load(path, out warning);
                Assert(saved.Shortcuts.Count == 7 && saved.Shortcuts[5].Label == "My site" && saved.Shortcuts[5].Action == "Website" && saved.Shortcuts[5].Glyph == "\uE734", "Save/reorder lost customization.");
                Click(window, "DeckNext"); Assert(((TextBlock)window.FindName("DeckPageLabel")).Text == "2/2", "Extra page inaccessible.");
                Assert(((UniformGrid)window.FindName("ShortcutGrid")).Children.Count == 1, "Last page has the wrong button count.");
                Click(window, "EditDeck"); Click(window, "DeckRemove"); Click(window, "CancelSettings");
                Assert(SettingsStore.Load(path, out warning).Shortcuts.Count == 7 && ((UniformGrid)window.FindName("ShortcutGrid")).Children.Count == 1, "Cancel changed the saved or live deck.");
                Click(window, "EditDeck"); Click(window, "DeckAdd"); Click(window, "SaveSettings");
                Assert(((Grid)window.FindName("SettingsOverlay")).Visibility == Visibility.Visible && ((TextBlock)window.FindName("SettingsStatus")).Text.Contains("Button 8"), "Incomplete new action was saved.");
                Click(window, "DeckRemove");
                for (int i = 0; i < 7; i++) Click(window, "DeckRemove");
                Click(window, "SaveSettings"); Assert(SettingsStore.Load(path, out warning).Shortcuts.Count == 0 && ((Button)window.FindName("EmptyDeck")).Visibility == Visibility.Visible, "Removing every button failed.");
            }
            window.Close();
        });
        check("Deck failed save preserves draft and unrelated settings tolerate missing apps", () => {
            string path = Path.Combine(directory, "deck-failure.xml");
            var settings = new AppSettings(); settings.Shortcuts[0].Target = "C:\\missing.exe"; SettingsStore.Save(path, settings);
            var window = Window();
            using (var controller = new DashboardController(window, path, true))
            {
                Click(window, "EditDeck"); ((TextBox)window.FindName("DeckName")).Text = "Unavailable app";
                using (var held = new FileStream(path + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    Click(window, "SaveSettings"); Assert(((TextBlock)window.FindName("SettingsStatus")).Text.Contains("Could not save"), "Missing original target blocks save or failed save is not reported.");
                    Assert(((TextBox)window.FindName("DeckName")).Text == "Unavailable app", "Failed save discarded draft.");
                }
                Click(window, "SaveSettings"); string warning; Assert(SettingsStore.Load(path, out warning).Shortcuts[0].Label == "Unavailable app", "Retry lost edits.");
            }
            window.Close();
        });
        check("Deck buttons keep a colour, show control state and name the hotkey's target", () => {
            string path = Path.Combine(directory, "deck-polish.xml"), warning;
            var settings = new AppSettings();
            settings.Shortcuts.Insert(0, new Shortcut { Label = "Quiet", Action = "Control", Target = "quiet", Glyph = "", Color = "Amber" });
            settings.Shortcuts.Insert(1, new Shortcut { Label = "Snap", Action = "Hotkey", Target = "Win+Shift+S", Glyph = "", Color = "bogus" });
            SettingsStore.Save(path, settings); var loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.Shortcuts[0].Color == "Amber" && loaded.Shortcuts[1].Color == "", "Button colours don't save, or an unknown colour wasn't cleared.");
            Assert(DeckActions.AccentHex("Amber") == "#F5C47D" && DeckActions.AccentHex("") == null && DeckActions.IsStateful(loaded.Shortcuts[0]) && !DeckActions.IsStateful(loaded.Shortcuts[1]), "Colour or state helpers are wrong.");
            Assert(DeckActions.DescribeWithTarget(loaded.Shortcuts[1], "chrome") == "Win+Shift+S  →  chrome" && DeckActions.DescribeWithTarget(loaded.Shortcuts[1], null).EndsWith("select an app first") && DeckActions.DescribeWithTarget(loaded.Shortcuts[0], "chrome") == "Toggle Quiet mode", "Hotkey target description is wrong.");
            var window = Window();
            using (var controller = new DashboardController(window, path, true))
            {
                // Quiet mode rebuilds the deck, so the button is looked up again after each toggle.
                Func<Button> quietButton = () => ((UniformGrid)window.FindName("ShortcutGrid")).Children.OfType<Button>().First(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("Quiet"));
                Assert(quietButton().BorderThickness.Left == 1, "Quiet button shows on before Quiet mode is on.");
                Click(window, "QuietButton");
                Assert(quietButton().BorderThickness.Left == 2 && System.Windows.Automation.AutomationProperties.GetName(quietButton()) == "Quiet, on", "Quiet button doesn't show that Quiet mode is on.");
                Click(window, "QuietButton");
                Assert(quietButton().BorderThickness.Left == 1, "Quiet button doesn't show that Quiet mode went off.");
                var glyph = ((StackPanel)quietButton().Content).Children.OfType<TextBlock>().First();
                Assert(((System.Windows.Media.SolidColorBrush)glyph.Foreground).Color == (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F5C47D"), "The button's own colour isn't used.");
                // Reordering by drag ends in Reorder; it saves like the up/down buttons.
                Click(window, "EditDeck");
                var editor = typeof(DashboardController).GetField("deckEditor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(controller);
                editor.GetType().GetMethod("Reorder").Invoke(editor, new object[] { 0, 2 });
                Click(window, "SaveSettings");
                var saved = SettingsStore.Load(path, out warning);
                Assert(saved.Shortcuts[2].Label == "Quiet" && saved.Shortcuts[0].Label == "Snap", "Reorder didn't move the button.");
            }
            window.Close();
        });
        check("Deck compact paging and editor controls fit at 115% text", () => {
            string path = Path.Combine(directory, "deck-sizing.xml"); var settings = new AppSettings { TextScale = 1.15 };
            settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings); var placement = settings.WidgetLayout.First(w => w.Id == "Shortcuts"); placement.Width = 420; placement.Height = 190;
            SettingsStore.Save(path, settings); var window = Window();
            using (var controller = new DashboardController(window, path, true))
            {
                var dashboard = (FrameworkElement)window.FindName("Dashboard"); Action layout = () => { dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout(); }; layout();
                Assert(((UniformGrid)window.FindName("ShortcutGrid")).Children.Count == 3, "Compact deck should show three touch-sized buttons.");
                foreach (Button button in ((UniformGrid)window.FindName("ShortcutGrid")).Children) Assert(button.ActualHeight >= 48 && button.ActualWidth >= 48, "Small deck has undersized targets.");
                Click(window, "EditDeck"); layout();
                foreach (string name in new[] { "DeckName", "DeckAction", "DeckIcon", "DeckTarget", "DeckAdd", "DeckRemove" })
                {
                    var element = (FrameworkElement)window.FindName(name); Assert(element.ActualHeight >= 48 && element.ActualWidth >= 48, name + " is not touch sized.");
                    Point corner = element.TransformToAncestor(dashboard).Transform(new Point(element.ActualWidth, element.ActualHeight)); Assert(corner.X <= 1920 && corner.Y <= 720, name + " escapes the dashboard.");
                }
            }
            window.Close();
        });
    }
}
