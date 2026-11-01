using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WidgetScreen;

// Sticker Sheet foundations (decision 017, docs/design/sticker-sheet.md): the eight inks and their defaults, Widget
// colours, label strips and status stickers on every card but Claude Code, the state language, frozen sticker shapes
// with no effects, and the ink picker in Edit layout. Writes ink-defaults.json for the phone's checks to compare.
internal static class StickerTests
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
    // Visibility up to the card (the windows in checks are never shown, so IsVisible is always false).
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
    private static string Ink(Window window, string id) { return ((SolidColorBrush)window.Resources[Stickers.InkKey(id)]).Color.ToString(); }
    private static string Hex(string ink) { return "#FF" + Inks.Hex(ink).Substring(1); }
    private static void Click(Window window, string name) { ((Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); }
    private static string CardName(string id)
    {
        switch (id) { case "Shortcuts": return "ShortcutsCard"; case "Clocks": return "ClocksCard"; default: return id + "Card"; }
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Sticker inks: eight tokens, the approved defaults, Amber kept for warnings", () => {
            Assert(Inks.Names.SequenceEqual(new[] { "Lime", "Glacier", "Lavender", "Amber", "Coral", "Mint", "Rose", "Lemon" }), "The eight ink names changed.");
            var hex = new Dictionary<string, string> { { "Lime", "#BAF28B" }, { "Glacier", "#8ED8F8" }, { "Lavender", "#C5AEFF" }, { "Amber", "#F5C47D" }, { "Coral", "#FF9E85" }, { "Mint", "#7FE6C0" }, { "Rose", "#FFA6CF" }, { "Lemon", "#F0E36E" } };
            Assert(hex.All(pair => Inks.Hex(pair.Key) == pair.Value), "An ink's colour isn't the approved one.");
            var owners = new Dictionary<string, string> { { "Mixer", "Lime" }, { "Games", "Lime" }, { "Shortcuts", "Glacier" }, { "Clipboard", "Glacier" }, { "Soundboard", "Lavender" }, { "Storage", "Lavender" },
                { "Focus", "Coral" }, { "Calendar", "Coral" }, { "Spotify", "Mint" }, { "Prayer", "Mint" }, { "Media", "Rose" }, { "Macros", "Rose" }, { "System", "Lemon" }, { "Notes", "Lemon" },
                { "Weather", "Glacier" }, { "Clocks", "Glacier" }, { "Home", "Glacier" } };
            Assert(owners.All(pair => Inks.Default(pair.Key) == pair.Value) && WidgetLayout.Ids.Where(Inks.InScope).All(id => owners.ContainsKey(id)), "A widget's default ink isn't the approved one.");
            Assert(!Inks.InScope("Claude") && !Inks.Choices.Contains("Amber") && WidgetLayout.Ids.All(id => Inks.Default(id) != "Amber"), "Claude Code is in scope, or Amber became a widget's ink.");
            // Each ink passes 9:1 with the dark text on it.
            Func<string, double> luminance = h => { var c = (Color)ColorConverter.ConvertFromString(h); Func<double, double> lin = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B); };
            double dark = luminance(Inks.DarkText);
            Assert(Inks.Names.All(name => (luminance(Inks.Hex(name)) + 0.05) / (dark + 0.05) >= 9), "An ink is under 9:1 with its dark text.");
            File.WriteAllText(Path.Combine(directory, "ink-defaults.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new Dictionary<string, object> {
                { "hex", Inks.Names.ToDictionary(n => n, n => (object)Inks.Hex(n)) }, { "defaults", WidgetLayout.Ids.Where(Inks.InScope).ToDictionary(id => id, id => (object)Inks.Default(id)) },
                { "strips", WidgetLayout.Ids.Where(Inks.InScope).ToDictionary(id => id, id => (object)(Inks.StripName(id) + "|" + Inks.ShortName(id))) } }));
        });
        check("Widget colours: Accent only gives every widget the accent; changed inks round-trip", () => {
            foreach (string accent in new[] { "Lime", "Glacier", "Lavender", "Amber" })
                Assert(WidgetLayout.Ids.All(id => Inks.Resolve(id, "Focus=Mint;Media=Lemon", Inks.AccentOnly, accent) == accent), "Accent only left a widget in its own ink with " + accent + ".");
            Assert(Inks.Resolve("Focus", "Focus=Mint", Inks.EachOwn, "Lime") == "Mint" && Inks.Resolve("Media", "Focus=Mint", Inks.EachOwn, "Lime") == "Rose", "Each its own doesn't use the chosen ink or the default.");
            Assert(Inks.Clean("Media=Rose;Focus=Mint;Claude=Lime;Notes=Amber;Bogus=Mint;Home=Sparkle") == "Focus=Mint", "Defaults, Claude, Amber or unknown widgets and inks were kept.");
            Assert(Inks.With("Focus=Mint", "Focus", "Coral") == "" && Inks.Mode("Accent") == "Accent" && Inks.Mode("anything") == "Each", "Going back to a default or reading Widget colours is wrong.");
            string path = Path.Combine(directory, "inks.xml"); string warning;
            SettingsStore.Save(path, new AppSettings { WidgetInks = "Spotify=Lemon", WidgetColours = "Accent" });
            var loaded = SettingsStore.Load(path, out warning);
            Assert(warning == null && loaded.WidgetInks == "Spotify=Lemon" && loaded.WidgetColours == "Accent", "Inks or Widget colours weren't saved.");
            File.WriteAllText(path, "<?xml version=\"1.0\"?><AppSettings><Theme>Slate</Theme></AppSettings>");
            loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.WidgetInks == "" && loaded.WidgetColours == "Each", "Older settings don't start with each widget in its own ink.");
            var window = Window();
            try
            {
                SettingsStore.Save(path, new AppSettings { WidgetColours = "Accent", Accent = "Lavender" });
                using (var controller = new DashboardController(window, path, true))
                    Assert(WidgetLayout.Ids.All(id => Ink(window, id) == Hex("Lavender")) && Inks.Names.All(n => ((SolidColorBrush)window.Resources["Ink" + n]).Color.ToString() == Hex(n)), "Accent only didn't paint every card in the accent, or an ink token is missing.");
            }
            finally { window.Close(); }
        });
        check("Status stickers speak the state language and hide while empty", () => {
            Assert(Inks.StateOf("UNAVAILABLE", true) == Inks.StickerState.Unavailable && Inks.StateOf("PC OFFLINE", false) == Inks.StickerState.Offline && Inks.StateOf("stale", false) == Inks.StickerState.Offline
                && Inks.StateOf("…", false) == Inks.StickerState.Loading && Inks.StateOf("C: LOW", true) == Inks.StickerState.Warning && Inks.StateOf("SPOTIFY", false) == Inks.StickerState.Fact, "The state words map to the wrong looks.");
            var caption = new TextBlock(); var host = new StackPanel(); host.Children.Add(caption);
            var sticker = StatusSticker.Wrap(caption, "Media", false);
            Assert(host.Children.Count == 1 && host.Children[0] == sticker && StatusSticker.For(caption) == sticker, "The sticker didn't take the caption's place.");
            Assert(sticker.Visibility == Visibility.Collapsed, "An empty sticker shows.");
            caption.Text = "SPOTIFY"; Assert(sticker.Visibility == Visibility.Visible && sticker.State == Inks.StickerState.Fact, "A fact didn't show.");
            caption.Text = "UNAVAILABLE"; Assert(sticker.State == Inks.StickerState.Unavailable, "Unavailable isn't the dashed look.");
            caption.Text = "PAUSED"; sticker.Warns = true; Assert(sticker.State == Inks.StickerState.Warning, "A warning sticker isn't a warning.");
            caption.Visibility = Visibility.Collapsed; Assert(sticker.Visibility == Visibility.Collapsed, "A hidden caption left its sticker showing.");
        });
        check("Sticker shapes are frozen, tilt at most 3°, and the dashboard uses no shadow or blur effects", () => {
            var burst = Stickers.Burst(64, 12);
            Assert(burst.IsFrozen && ReferenceEquals(burst, Stickers.Burst(64, 12)) && Stickers.Disc(40).IsFrozen && Stickers.Rounded(80, 40, 12).IsFrozen && Stickers.Caution.IsFrozen, "A sticker geometry or the caution tape isn't built once and frozen.");
            var shape = new StickerShape(burst, "InkCoral"); Stickers.Tilt(shape, 12);
            Assert(Math.Abs(((RotateTransform)shape.RenderTransform).Angle) == 3 && shape.Effect == null, "A tilt went past 3° or a sticker has an effect.");
            shape.Measure(new Size(200, 200)); Assert(shape.DesiredSize.Height >= 64 + Stickers.Lift, "The lift isn't below the sticker.");
            var window = Window();
            try
            {
                string path = Path.Combine(directory, "stickers-effects.xml"); var settings = new AppSettings();
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = true; return p; }).ToArray();
                SettingsStore.Save(path, settings);
                using (var controller = new DashboardController(window, path, true))
                {
                    Lay(window);
                    var effects = Descendants((DependencyObject)window.FindName("Dashboard")).OfType<UIElement>().Where(e => e.Effect != null).ToList();
                    Assert(effects.Count == 0, "Something on the dashboard uses an effect: " + String.Join(", ", effects.Select(e => e.GetType().Name)));
                }
            }
            finally { window.Close(); }
        });
        check("Every card but Claude Code has a label strip that stays inside it, at text 100% and 140%", () => {
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (string id in WidgetLayout.Ids)
                    foreach (var size in new[] { WidgetLayout.Minimum(id), new Size(Math.Max(WidgetLayout.Minimum(id).Width, 560), Math.Max(WidgetLayout.Minimum(id).Height, 400)) })
                    {
                        string path = Path.Combine(directory, "strip-" + id + ".xml");
                        var settings = new AppSettings { TextScale = scale };
                        settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == id; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                        SettingsStore.Save(path, settings);
                        var window = Window();
                        try
                        {
                            using (var controller = new DashboardController(window, path, true))
                            {
                                Lay(window);
                                var card = (FrameworkElement)window.FindName(CardName(id));
                                if (id == "Claude") { Assert(!controller.Strips.ContainsKey("Claude") && !Descendants(card).OfType<SpacedText>().Any(), "Claude Code got a label strip."); continue; }
                                LabelStrip strip; controller.Strips.TryGetValue(id, out strip);
                                if (strip == null && id == "Weather") strip = controller.WeatherStrip;
                                if (id == "Weather" && CardSizing.Weather(size.Width, size.Height) == CardSizing.Design.Small) { Assert(strip == null, "Small weather grew a heading."); continue; }
                                Assert(strip != null, id + " has no label strip.");
                                if (!Shown(strip.Host, card)) { Assert(id == "Shortcuts" || id == "Soundboard" || id == "Macros" || id == "Games", id + "'s strip is hidden."); continue; }
                                var box = strip.Host.TransformToAncestor(card).TransformBounds(new Rect(0, 0, strip.Host.ActualWidth, strip.Host.ActualHeight));
                                Assert(box.Left >= 19.5 && box.Top >= 19.5 && box.Right <= card.ActualWidth - 19.5 && box.Height <= Stickers.StripHeight + 0.5, id + "'s strip leaves its " + size + " card at " + scale * 100 + "%: " + box);
                                Assert(strip.Words.DesiredSize.Height <= Stickers.StripHeight && strip.Words.FontSize <= 13 * 1.4 + 0.01 && strip.IsCompact == Inks.Compact(size.Width), id + "'s strip text doesn't fit its pill, or it folded at the wrong width.");
                                foreach (var sticker in controller.StatusStickers.Where(s => s.Id == id && Shown(s, card)))
                                {
                                    var place = sticker.TransformToAncestor(card).TransformBounds(new Rect(0, 0, sticker.ActualWidth, sticker.ActualHeight));
                                    Assert(place.Right <= card.ActualWidth - 19.5 && place.Height <= Stickers.StickerHeight + 0.5, id + "'s status sticker leaves its card: " + place);
                                }
                            }
                        }
                        finally { window.Close(); }
                    }
            var narrow = LabelStrip.Create("Clipboard"); narrow.Fit(180, 1.4);
            Assert(narrow.IsCompact && narrow.Words.Text == "CLIPS" && narrow.Host.Background == null, "A strip under 200 units doesn't fold to its dot and short name.");
        });
        check("Edit layout's ink picker: the chosen ink shows at once, Save keeps it, Cancel drops it", () => {
            string path = Path.Combine(directory, "ink-picker.xml"); SettingsStore.Save(path, new AppSettings()); string warning;
            var window = Window();
            try
            {
                using (var controller = new DashboardController(window, path, true))
                {
                    Assert(Ink(window, "Focus") == Hex("Coral") && Ink(window, "Media") == Hex("Rose"), "Cards don't start in their own inks.");
                    Click(window, "EditLayoutButton"); Click(window, "EditWidgetsButton");
                    controller.Editor.Select("Focus"); controller.ChooseInk("Mint");
                    Assert(Ink(window, "Focus") == Hex("Mint") && SettingsStore.Load(path, out warning).WidgetInks == "", "The picked ink didn't show, or was saved before Save.");
                    controller.ChooseInk("Amber"); Assert(Ink(window, "Focus") == Hex("Mint"), "Amber was offered as a widget ink.");
                    Click(window, "CancelLayoutButton");
                    Assert(Ink(window, "Focus") == Hex("Coral") && controller.EditInks == null, "Cancel didn't drop the picked ink.");
                    Click(window, "EditLayoutButton"); controller.Editor.Select("Focus"); controller.ChooseInk("Lemon");
                    controller.Editor.Select("Claude"); controller.ChooseInk("Rose");
                    Click(window, "SaveLayoutButton");
                    Assert(SettingsStore.Load(path, out warning).WidgetInks == "Focus=Lemon" && Ink(window, "Focus") == Hex("Lemon"), "Save layout didn't keep the ink, or Claude Code took one.");
                }
            }
            finally { window.Close(); }
        });
    }
}
