using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WidgetScreen;

// Sticker Sheet phase 2, the default five (docs/design/sticker-sheet.md): Focus's dial, pips and chips, Now Playing's
// cover tint and album, Quick Launch's keycaps, the mixer's channel strips and ON AIR badge, and Spotify's captions.
// The phone's checks (android/tests) assert the same values, so both apps read alike.
internal static class DefaultFiveTests
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
    // Inside a scrolling list (the mixer's apps), where rows may sit below the fold on purpose.
    private static bool InList(DependencyObject element, DependencyObject root)
    {
        for (var e = VisualTreeHelper.GetParent(element); e != null && e != root; e = VisualTreeHelper.GetParent(e)) if (e is ScrollViewer) return true;
        return false;
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
    // One widget alone on the reference canvas at the given size.
    private static AppSettings Alone(string id, Size size, double scale)
    {
        var settings = new AppSettings { TextScale = scale, FocusSessionDate = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), FocusSessionsToday = 2, FocusMinutesToday = 50 };
        settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == id; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
        return settings;
    }
    // Inside the card's padding (20), or with edge = 0 just inside the card (Master volume's button reaches into the padding).
    private static void Inside(FrameworkElement part, FrameworkElement card, string what, double edge = 19.5)
    {
        var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
        Assert(box.Left >= edge && box.Top >= edge && box.Right <= card.ActualWidth - edge && box.Bottom <= card.ActualHeight - edge, what + " leaves its card: " + box + " in " + card.ActualWidth + " × " + card.ActualHeight);
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Focus facts read the same on both apps: today's minutes, the next break and the status sticker", () => {
            Assert(FocusTimer.MinutesText(0) == "0 min" && FocusTimer.MinutesText(50) == "50 min" && FocusTimer.MinutesText(100) == "1 h 40 min", "Today's minutes read wrongly.");
            var now = new DateTime(2026, 11, 1, 15, 0, 0);
            Assert(FocusTimer.BreakText(true, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), now, false) == "3:25 PM" && FocusTimer.BreakText(true, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), now, true) == "15:25", "A running session's break time is wrong.");
            Assert(FocusTimer.BreakText(false, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), now, false) == "In 25 min" && FocusTimer.BreakText(false, TimeSpan.FromMinutes(10.2), TimeSpan.FromMinutes(25), now, false) == "In 11 min", "A stopped or paused session's break is wrong.");
            Assert(FocusTimer.StateText(false, true, false, 3) == "DONE" && FocusTimer.StateText(true, false, false, 3) == "FOCUSING" && FocusTimer.StateText(false, false, true, 0) == "PAUSED" && FocusTimer.StateText(false, false, false, 2) == "2 TODAY" && FocusTimer.StateText(false, false, false, 0) == "", "The Focus sticker's words are wrong.");
            Assert(PipRow.Shown(0) == 4 && PipRow.Shown(6) == 6 && PipRow.Shown(20) == 8, "The pip row shows the wrong number of pips.");
        });
        check("Focus: Large draws the kitchen-timer dial with pips and chips, Medium the pips, Small neither, inside the card at 100% and 140%", () => {
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (var size in new[] { new Size(320, 576), new Size(300, 460), new Size(320, 420), new Size(280, 192) })
                {
                    string path = Path.Combine(directory, "focus-five.xml"); SettingsStore.Save(path, Alone("Focus", size, scale));
                    var window = Window();
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            Lay(window);
                            var card = (FrameworkElement)window.FindName("FocusCard"); var design = CardSizing.Focus(size.Width, size.Height);
                            var dial = Descendants(card).OfType<TimerDial>().FirstOrDefault(); var pips = Descendants(card).OfType<PipRow>().FirstOrDefault();
                            Assert(dial != null && Shown(dial, card) == (design == CardSizing.Design.Large), "The dial shows on the wrong design at " + size + ".");
                            Assert(pips != null && Shown(pips, card) == (design != CardSizing.Design.Small) && pips.Count == 2, "The pips show on the wrong design, or don't count today's sessions, at " + size + ".");
                            Assert(((TextBlock)window.FindName("FocusState")).Text == "2 TODAY", "The Focus sticker doesn't count today.");
                            foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is TextBlock || e is TimerDial || e is PipRow || e is Button)))
                                Inside(part, card, "Focus " + part.GetType().Name + " at " + size + " and " + scale * 100 + "%");
                            if (design == CardSizing.Design.Large) Assert(dial.ActualWidth >= 120 && Math.Abs(dial.Left - 1) < 0.001, "A ready dial isn't full, or it's too small.");
                        }
                    }
                    finally { window.Close(); }
                }
        });
        check("Now Playing: a cover's colour is its average, and the card's tint keeps text readable", () => {
            var pixels = new byte[16 * 16 * 4];
            for (int i = 0; i < pixels.Length; i += 4) { bool left = (i / 4) % 16 < 8; pixels[i] = 0; pixels[i + 1] = left ? (byte)255 : (byte)0; pixels[i + 2] = left ? (byte)0 : (byte)255; pixels[i + 3] = 255; }
            var bitmap = BitmapSource.Create(16, 16, 96, 96, PixelFormats.Bgra32, null, pixels, 64);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); byte[] png;
            using (var stream = new MemoryStream()) { encoder.Save(stream); png = stream.ToArray(); }
            var average = MediaService.AverageColour(png);
            Assert(average.HasValue && Math.Abs(average.Value.R - 128) <= 24 && Math.Abs(average.Value.G - 128) <= 24 && average.Value.B <= 8, "A half-red, half-green cover doesn't average to olive: " + average);
            Assert(MediaService.AverageColour(new byte[] { 1, 2, 3 }) == null && MediaService.Hex(null) == "" && MediaService.Hex(Color.FromRgb(0x7A, 0x4F, 0xB0)) == "#7A4FB0", "A broken cover or the hex is wrong.");
            Assert(MediaService.Blend(Color.FromRgb(0x16, 0x1A, 0x22), Color.FromRgb(0xFF, 0x00, 0x00), MediaService.TintPart) == Color.FromRgb(0x49, 0x14, 0x1B), "The tint doesn't mix 22% of the cover into the Surface.");
            // Even a white cover keeps the tinted top dark enough for the Text colour (at least 7:1).
            var top = MediaService.Blend(Color.FromRgb(0x16, 0x1A, 0x22), Colors.White, MediaService.TintPart);
            Func<Color, double> l = c => { Func<double, double> lin = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }; return 0.2126 * lin(c.R) + 0.7152 * lin(c.G) + 0.0722 * lin(c.B); };
            Assert((l(Color.FromRgb(0xF2, 0xF4, 0xF8)) + 0.05) / (l(top) + 0.05) >= 7, "A white cover's tint makes text hard to read.");
        });
        check("Now Playing keeps its sleeve, album line and chips inside the card at every size", () => {
            foreach (var size in new[] { new Size(312, 200), new Size(568, 326), new Size(620, 380), new Size(480, 576) })
            {
                string path = Path.Combine(directory, "media-five.xml"); SettingsStore.Save(path, Alone("Media", size, 1.4));
                var window = Window();
                try
                {
                    using (var controller = new DashboardController(window, path, true))
                    {
                        Lay(window);
                        var card = (FrameworkElement)window.FindName("MediaCard");
                        Assert(Descendants(card).OfType<DiscPeek>().Any(), "The sleeve has no record at " + size + ".");
                        foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is TextBlock || e is DiscPeek || e is Button)))
                            Inside(part, card, "Now Playing " + part.GetType().Name + (part is TextBlock ? " \"" + ((TextBlock)part).Text + "\"" : "") + " at " + size);
                    }
                }
                finally { window.Close(); }
            }
        });
        check("Quick Launch buttons are keycaps, with an LED for on and a hotkey legend on big caps", () => {
            string path = Path.Combine(directory, "keycaps.xml"); var settings = Alone("Shortcuts", new Size(1020, 400), 1);
            settings.Shortcuts = new List<Shortcut> { new Shortcut { Label = "Mute", Action = "Control", Target = "quiet", Glyph = "" }, new Shortcut { Label = "Mute Discord", Action = "Hotkey", Target = "Ctrl+Shift+M", Glyph = "" }, new Shortcut { Label = "Notes", Action = "App", Target = "%windir%\\System32\\notepad.exe", Glyph = "" } };
            settings.QuietMode = true;
            SettingsStore.Save(path, settings);
            var window = Window();
            try
            {
                using (var controller = new DashboardController(window, path, true))
                {
                    Lay(window);
                    var buttons = ((Panel)window.FindName("ShortcutGrid")).Children.OfType<Button>().ToList();
                    Assert(buttons.Count == 3 && buttons.All(b => b.Style == window.Resources["Keycap"]), "Quick Launch buttons aren't keycaps.");
                    Assert(Equals(buttons[0].Tag, true) && !Equals(buttons[2].Tag, true), "The LED doesn't follow the on state.");
                    var led = buttons[0].Template.FindName("Led", buttons[0]) as System.Windows.Shapes.Ellipse;
                    Assert(led != null && led.Visibility == Visibility.Visible && ((System.Windows.Shapes.Ellipse)buttons[2].Template.FindName("Led", buttons[2])).Visibility != Visibility.Visible, "The LED doesn't light for an on button only.");
                    var legend = Descendants(buttons[1]).OfType<SpacedText>().FirstOrDefault();
                    Assert(legend != null && legend.Text == "CTRL+SHIFT+M", "The hotkey's keys aren't its legend.");
                }
            }
            finally { window.Close(); }
        });
        check("Mixer: Large cards show channel strips (master first), the rest stays the list; ON AIR and LED rules", () => {
            Assert(CardSizing.MixerStripSlots(456) == 3 && CardSizing.MixerStripSlots(820) == 6 && CardSizing.MixerStripSlots(300) == 1 && CardSizing.MixerStripSlots(200) == 0, "The number of app strips is wrong.");
            Assert(DashboardController.ShortDevice("Speakers (Logi Z407)") == "Z407" && DashboardController.ShortDevice("Headset") == "Headset" && DashboardController.ShortDevice("Realtek (R) Audio") == "Audio" && DashboardController.ShortDevice("") == "", "Device names shorten wrongly.");
            Assert(DashboardController.MicText(false) == "ON AIR" && DashboardController.MicText(true) == "MUTED" && DashboardController.MicText(null) == "", "The microphone badge's words are wrong.");
            Assert(LedMeter.Lit(0, 10) == 0 && LedMeter.Lit(1, 10) == 10 && LedMeter.Lit(0.25, 10) == 5 && LedMeter.Lit(0.001, 10) == 0 && LedMeter.Lit(0.01, 10) == 1, "The LED meter lights the wrong number of segments.");
            foreach (var size in new[] { new Size(456, 576), new Size(820, 420), new Size(456, 380), new Size(420, 280) })
            {
                string path = Path.Combine(directory, "mixer-five.xml"); SettingsStore.Save(path, Alone("Mixer", size, 1.4));
                var window = Window();
                try
                {
                    using (var controller = new DashboardController(window, path, true))
                    {
                        Lay(window);
                        var card = (FrameworkElement)window.FindName("MixerCard"); bool large = CardSizing.MixerDesign(size.Height) == CardSizing.Design.Large;
                        Assert(controller.MixerStripsShown == large && (!large || controller.MixerStripSlots == CardSizing.MixerStripSlots(size.Width)), "Strips show on the wrong design at " + size + ".");
                        Assert(Shown((FrameworkElement)window.FindName("MixerStrips"), card) == large && Shown((FrameworkElement)window.FindName("MixerApps"), card) == (large || CardSizing.Mixer(size.Height) == CardSizing.MixerMode.Master), "The strips or the Apps button are shown wrongly at " + size + ".");
                        foreach (var slider in Descendants(card).OfType<Slider>().Where(s => Shown(s, card) && s.Orientation == Orientation.Vertical)) Assert(slider.ActualWidth >= 48 && slider.ActualHeight >= 48, "A strip's slider is under the 48-unit touch size at " + size + ".");
                        foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is TextBlock || e is Slider || e is Button || e is LedMeter) && !InList(e, card)))
                            Inside(part, card, "Mixer " + part.GetType().Name + " at " + size, 0.5);
                    }
                }
                finally { window.Close(); }
            }
        });
        check("Spotify captions come from the replies already fetched: songs and year, owner and songs, a song's year", () => {
            var album = new Dictionary<string, object> { { "uri", "spotify:album:4aawyAB9vmqN3uQ7FjRGTy" }, { "name", "Dune Sea" }, { "total_tracks", 11 }, { "release_date", "2024-03-01" } };
            var single = new Dictionary<string, object> { { "uri", "spotify:album:2" }, { "name", "One" }, { "total_tracks", 1 }, { "release_date", "1999" } };
            var playlist = new Dictionary<string, object> { { "uri", "spotify:playlist:3" }, { "name", "Mix" }, { "owner", new Dictionary<string, object> { { "display_name", "Ali" } } }, { "tracks", new Dictionary<string, object> { { "total", 24 } } } };
            var track = new Dictionary<string, object> { { "uri", "spotify:track:4" }, { "name", "Golden Hour" }, { "album", new Dictionary<string, object> { { "release_date", "2019-06-01" } } } };
            Assert(SpotifyService.CaptionOf(album, "spotify:album:4aawyAB9vmqN3uQ7FjRGTy") == "11 songs · 2024" && SpotifyService.CaptionOf(single, "spotify:album:2") == "1 song · 1999", "Album captions are wrong.");
            Assert(SpotifyService.CaptionOf(playlist, "spotify:playlist:3") == "Ali · 24 songs" && SpotifyService.CaptionOf(track, "spotify:track:4") == "2019", "Playlist or song captions are wrong.");
            Assert(SpotifyService.CaptionOf(new Dictionary<string, object>(), "spotify:album:5") == "" && SpotifyService.ParseItem(album) != null && SpotifyService.ParseItem(album).Caption == "11 songs · 2024", "An empty reply isn't empty, or items don't carry their caption.");
        });
    }
}
