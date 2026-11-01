using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WidgetScreen;

// Clipboard history (docs/design/clipboard-history.md): the history's rules, the formats that keep a copy private, the
// encrypted copy, the card and Settings. The checks never write to the real clipboard; texts are offered directly.
internal static class ClipboardTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Click(Window window, string name) { ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        return true;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Clipboard history keeps the newest texts once each, up to its size", () => {
            var history = new ClipboardHistory { Size = 5 };
            var start = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
            for (int i = 0; i < 7; i++) history.Add("text " + i, start.AddMinutes(i));
            Assert(history.Items.Count == 5 && history.Items[0].Text == "text 6" && history.Items[4].Text == "text 2", "The history isn't newest first, or isn't trimmed.");
            string id = history.Items[3].Id;
            Assert(history.Add("text 3", start.AddMinutes(10)) && history.Items[0].Text == "text 3" && history.Items[0].Id == id && history.Items.Count == 5, "A repeated copy wasn't moved to the top.");
            Assert(!history.Add("   ", start) && !history.Add(null, start), "Blank text was kept.");
            history.Add(new string('x', ClipboardHistory.MaxText + 10), start); Assert(history.Items[0].Text.Length == ClipboardHistory.MaxText, "Long text isn't cut.");
            Assert(history.Remove(id) && history.Find(id) == null && !history.Remove("missing"), "Removing failed.");
            Assert(ClipboardHistory.ClampSize(0) == 10 && ClipboardHistory.ClampSize(2) == 5 && ClipboardHistory.ClampSize(99) == 50, "Sizes aren't clamped.");
            Assert(ClipboardHistory.Preview("  a\r\n\tb   c ", 40) == "a b c" && ClipboardHistory.Preview("abcdefghij", 6) == "abcde…", "Previews are wrong.");
            Assert(ClipboardHistory.Ago(start, start.AddSeconds(30)) == "just now" && ClipboardHistory.Ago(start, start.AddMinutes(4)) == "4 min ago" && ClipboardHistory.Ago(start, start.AddHours(2)) == "2 h ago" && ClipboardHistory.Ago(start, start.AddDays(1.2)) == "yesterday", "Times are worded wrongly.");
        });
        check("Clipboard history skips texts that password managers mark as private", () => {
            var plain = new DataObject(DataFormats.UnicodeText, "hello");
            Assert(!ClipboardService.Excluded(plain), "Plain text was skipped.");
            var excluded = new DataObject(DataFormats.UnicodeText, "secret"); excluded.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            Assert(ClipboardService.Excluded(excluded), "ExcludeClipboardContentFromMonitorProcessing wasn't honoured.");
            var ignore = new DataObject(DataFormats.UnicodeText, "secret"); ignore.SetData("Clipboard Viewer Ignore", new MemoryStream(new byte[0]));
            Assert(ClipboardService.Excluded(ignore), "Clipboard Viewer Ignore wasn't honoured.");
            var noHistory = new DataObject(DataFormats.UnicodeText, "secret"); noHistory.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
            var allowed = new DataObject(DataFormats.UnicodeText, "fine"); allowed.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(1)));
            Assert(ClipboardService.Excluded(noHistory) && !ClipboardService.Excluded(allowed), "CanIncludeInClipboardHistory wasn't read.");
        });
        check("Clipboard history stays in memory unless kept, and then only encrypted", () => {
            string folder = Path.Combine(directory, "clipboard-keep"); Directory.CreateDirectory(folder);
            string store = Path.Combine(folder, "clipboard-history.dat"); if (File.Exists(store)) File.Delete(store);
            var service = new ClipboardService(store);
            service.Offer("TEST FIXTURE one"); Assert(!File.Exists(store), "The history was written without being asked.");
            service.Paused = true; Assert(!service.Offer("while paused") && service.History.Items.Count == 1, "A copy was kept while paused.");
            service.Paused = false; service.SetKeep(true); service.Offer("TEST FIXTURE two");
            Assert(File.Exists(store) && !System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(store)).Contains("FIXTURE"), "The kept history isn't encrypted.");
            var again = new ClipboardService(store); again.SetKeep(true);
            Assert(again.History.Items.Count == 2 && again.History.Items[0].Text == "TEST FIXTURE two", "The kept history didn't come back.");
            again.SetKeep(false); Assert(!File.Exists(store), "Turning keep off left the file.");
        });
        check("Clipboard settings are saved and the widget starts hidden", () => {
            Assert(new AppSettings().ClipboardSize == 10 && !new AppSettings().ClipboardKeep, "Clipboard defaults are wrong.");
            string path = Path.Combine(directory, "clipboard-settings.xml"), warning;
            SettingsStore.Save(path, new AppSettings { ClipboardSize = 99, ClipboardKeep = true });
            var loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.ClipboardSize == 50 && loaded.ClipboardKeep, "Clipboard settings weren't saved or clamped.");
            var settings = new AppSettings();
            Assert(WidgetLayout.Ids.Contains("Clipboard") && !WidgetLayout.SpotifyDefault(settings).First(p => p.Id == "Clipboard").Visible && !WidgetLayout.ShownAnywhere(settings, "Clipboard") && WidgetLayout.ShownAnywhere(settings, "Focus") == false, "The Clipboard widget isn't hidden by default.");
            settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings);
            Assert(WidgetLayout.ShownAnywhere(settings, "Focus") && !WidgetLayout.ShownAnywhere(settings, "Clipboard"), "ShownAnywhere is wrong.");
            Assert(CardSizing.Clip(240, 200) == CardSizing.Design.Small && CardSizing.Clip(472, 310) == CardSizing.Design.Medium && CardSizing.Clip(439, 576) == CardSizing.Design.Medium && CardSizing.Clip(440, 400) == CardSizing.Design.Large, "Clipboard designs are wrong.");
        });
        check("Clipboard card shows, pauses and clears the history, keeping its buttons inside the card", () => {
            foreach (var size in new[] { new Size(240, 200), new Size(472, 310), new Size(600, 500) })
            {
                string folder = Path.Combine(directory, "clipboard-" + size.Width + "x" + size.Height); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "settings.xml");
                var settings = new AppSettings { TextScale = 1.4 };
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Clipboard"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                SettingsStore.Save(path, settings);
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            Assert(controller.Clipboard.Watching, "The clipboard isn't watched while the widget is shown.");
                            // A labelled test fixture: texts offered directly, never through the real clipboard.
                            controller.Clipboard.Offer("TEST FIXTURE: an address line\r\nwith a second line");
                            controller.Clipboard.Offer("TEST FIXTURE: a much longer copied paragraph that goes on well past the width of any card so it has to end in an ellipsis somewhere");
                            controller.Clipboard.Offer("TEST FIXTURE: 42");
                            var dashboard = (FrameworkElement)window.FindName("Dashboard");
                            dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                            Assert(controller.ClipboardView.Current == CardSizing.Clip(size.Width, size.Height) && window.FindName("ClipItem2") != null && ((TextBlock)window.FindName("ClipStatus")).Text == "3", "The history isn't shown at " + size + ".");
                            var card = (FrameworkElement)window.FindName("ClipboardCard");
                            var scroll = Descendants(card).OfType<ScrollViewer>().First();
                            foreach (var part in Descendants(card).OfType<Button>().Where(e => Shown(e, card) && e.ActualWidth > 0))
                            {
                                var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                Assert(box.Left >= 23.5 && box.Right <= card.ActualWidth - 23.5 && part.ActualHeight >= 47.5, "A clipboard button is outside the " + size + " card or shorter than 48: " + box);
                                if (!(scroll.IsAncestorOf(part))) Assert(box.Bottom <= card.ActualHeight - 23.5, "A clipboard tool is below the " + size + " card.");
                            }
                            Click(window, "ClipPause"); Assert(controller.Clipboard.Paused && ((TextBlock)window.FindName("ClipStatus")).Text == "PAUSED", "Pause didn't pause.");
                            Click(window, "ClipPause"); Assert(!controller.Clipboard.Paused, "Resume didn't resume.");
                            Click(window, "ClipClear"); Click(window, "ConfirmCancel"); Assert(controller.Clipboard.History.Items.Count == 3, "Cancel cleared the history.");
                            Click(window, "ClipClear"); Click(window, "ConfirmAction"); Assert(controller.Clipboard.History.Items.Count == 0 && ((TextBlock)window.FindName("ClipEmpty")).Visibility == Visibility.Visible, "Clear didn't clear.");
                            if (size.Width == 472)
                            {
                                Click(window, "SettingsButton");
                                ((System.Windows.Controls.Primitives.RangeBase)window.FindName("ClipSizeChoice")).Value = 20; ((CheckBox)window.FindName("ClipKeepChoice")).IsChecked = false;
                                Click(window, "SaveSettings");
                                string warning; Assert(SettingsStore.Load(path, out warning).ClipboardSize == 20 && controller.Clipboard.History.Size == 20, "Settings → Widgets didn't save the clipboard size.");
                            }
                        }
                        Assert(!File.Exists(Path.Combine(folder, "clipboard-history.dat")), "The history was written to disk without being asked.");
                    }
                    finally { window.Close(); }
                }
            }
        });
    }
}
