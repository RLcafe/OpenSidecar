using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WidgetScreen;

// Sticker Sheet phase 5, the Soundboard (docs/design/sticker-sheet.md): sampler pads, the voice sticker and the
// not-set-up and no-sounds states. The phone's checks (android/tests) assert the same words.
internal static class SoundboardPadTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        return true;
    }
    private static Window Window()
    {
        using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            return (Window)System.Windows.Markup.XamlReader.Load(stream);
    }
    private static void Lay(Window window)
    {
        var dashboard = (FrameworkElement)window.FindName("Dashboard");
        dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
    }
    private static AppSettings Alone(string id, Size size, double scale)
    {
        var settings = new AppSettings { TextScale = scale };
        settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == id; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
        return settings;
    }
    private static void Inside(FrameworkElement part, FrameworkElement card, string what, double edge = 15.5)
    {
        var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
        Assert(box.Left >= edge && box.Top >= edge && box.Right <= card.ActualWidth - edge && box.Bottom <= card.ActualHeight - edge, what + " leaves its card: " + box + " in " + card.ActualWidth + " × " + card.ActualHeight);
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Soundboard: the voice sticker and pad numbers read the same on both apps", () => {
            Assert(SoundboardWidget.Voice(false, false, false, null) == "NOT SET UP" && SoundboardWidget.Voice(true, true, true, "General") == "UNAVAILABLE", "Not set up or unavailable.");
            Assert(SoundboardWidget.Voice(true, true, false, "General") == "General" && SoundboardWidget.Voice(true, true, false, "") == "NOT IN VOICE" && SoundboardWidget.Voice(true, false, false, null) == "…", "In voice, not in voice, connecting.");
            Assert(Inks.StateOf("NOT SET UP", false) == Inks.StickerState.Unavailable && Inks.StateOf("UNAVAILABLE", false) == Inks.StickerState.Unavailable && Inks.StateOf("…", false) == Inks.StickerState.Loading && Inks.StateOf("General", false) == Inks.StickerState.Fact, "Sticker looks.");
            Assert(SoundboardWidget.PadNumber(0) == "01" && SoundboardWidget.PadNumber(11) == "12" && SoundboardWidget.PadNumber(99) == "100", "Pad numbers.");
            Assert(SoundboardWidget.Flash == TimeSpan.FromMilliseconds(150), "Flash length.");
        });
        check("Soundboard: pads fit their card at every design at 100% and 140%, and the empty and not-set-up states say why", () => {
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (var size in new[] { new Size(300, 240), new Size(472, 300), new Size(900, 500) })
                {
                    string path = Path.Combine(directory, "soundboard-pads.xml"); SettingsStore.Save(path, Alone("Soundboard", size, scale));
                    var window = Window();
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            Lay(window);
                            var card = (FrameworkElement)window.FindName("SoundboardCard");
                            Assert(((TextBlock)window.FindName("SoundboardChannel")).Text == "NOT SET UP" && ((FrameworkElement)window.FindName("SoundboardEmpty")).Visibility == Visibility.Visible, "Not set up doesn't say so at " + size + ".");
                            controller.UseSoundboardSample(false); Lay(window);
                            var pads = Descendants(card).OfType<Button>().Where(b => b.Style == window.FindResource("Pad") && Shown(b, card)).ToList();
                            Assert(pads.Count == 6 && pads.All(p => p.IsEnabled && p.ActualHeight >= 48), "Six live pads of 48 units expected at " + size + ": " + pads.Count);
                            Assert(((TextBlock)window.FindName("SoundboardChannel")).Text == "SAMPLE" && ((FrameworkElement)window.FindName("SoundboardEmpty")).Visibility != Visibility.Visible, "The sample isn't labelled at " + size + ".");
                            foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is Button || e is TextBlock)))
                                Inside(part, card, "Soundboard " + part.GetType().Name + " at " + size + " and " + scale * 100 + "%");
                            controller.UseSoundboardSample(true); Lay(window);
                            Assert(((FrameworkElement)window.FindName("SoundboardProblem")).Visibility == Visibility.Visible && ((TextBlock)window.FindName("SoundboardProblemText")).Text.StartsWith("No soundboard sounds"), "No sounds isn't explained at " + size + ".");
                        }
                    }
                    finally { window.Close(); }
                }
        });
    }
}
