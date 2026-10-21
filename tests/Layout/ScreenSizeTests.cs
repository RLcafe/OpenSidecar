using System;
using System.IO;
using System.Linq;
using System.Windows;
using WidgetScreen;

// Any screen size (docs/design/any-screen-size.md): the screen fit, canvas sizes, layouts saved per canvas,
// stretching a saved layout to a new shape, the default arrangement for any canvas and minimum sizes.
internal static class ScreenSizeTests
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static Window Window() { using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml")) return (Window)System.Windows.Markup.XamlReader.Load(stream); }
    private static bool Overlaps(WidgetPlacement[] layout)
    {
        var shown = layout.Where(p => p.Visible).ToArray();
        for (int i = 0; i < shown.Length; i++)
            for (int j = i + 1; j < shown.Length; j++)
            {
                Rect overlap = Rect.Intersect(shown[i].Bounds, shown[j].Bounds);
                if (!overlap.IsEmpty && overlap.Width > 1 && overlap.Height > 1) return true;
            }
        return false;
    }
    private static bool Inside(WidgetPlacement[] layout, Size canvas)
    {
        return layout.All(p => p.X >= 0 && p.Y >= 0 && p.X + p.Width <= canvas.Width + 1e-9 && p.Y + p.Height <= canvas.Height + 1e-9);
    }
    private static Size Canvas(ScreenFit fit) { return new Size(fit.CanvasWidth, fit.CanvasHeight); }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Screen fit keeps the 1920 x 720 scale and shapes the canvas like the screen", () => {
            var reference = ScreenFit.For(1920, 720, 1, 1800);
            Assert(reference.Scale == 1 && reference.LogicalWidth == 1920 && reference.LogicalHeight == 720 && !reference.Stacked && Canvas(reference) == WidgetLayout.Reference, "1920 x 720 is not the reference canvas.");
            var wide = ScreenFit.For(2560, 720, 1, 1800);
            Assert(wide.Scale == 1 && wide.CanvasWidth == 2496 && wide.CanvasHeight == 576, "A wider screen did not widen the canvas: " + wide.CanvasWidth);
            var hd = ScreenFit.For(1920, 1080, 1, 1800);
            Assert(hd.Scale == 1 && hd.CanvasWidth == 1856 && hd.CanvasHeight == 936, "A taller screen did not heighten the canvas: " + hd.CanvasHeight);
            var uhd = ScreenFit.For(3840, 2160, 1, 1800);
            Assert(uhd.Scale == 2 && uhd.LogicalWidth == 1920 && uhd.LogicalHeight == 1080, "4K should look like 1920 x 1080 at twice the pixels.");
            Assert(ScreenFit.For(1536, 576, 1, 1800).LogicalWidth == 1920, "A smaller screen of the same shape should scale down, not change the canvas.");
        });
        check("Small screens keep text larger and the canvas at 80% or more", () => {
            foreach (var screen in new[] { new Size(1280, 800), new Size(1024, 600), new Size(1920, 480), new Size(800, 480) })
            {
                var fit = ScreenFit.For(screen.Width, screen.Height, 1, 1800);
                double full = Math.Min(screen.Width / 1920, screen.Height / 720);
                Assert(fit.Scale >= full && fit.Scale <= Math.Max(full, ScreenFit.CompactBelow) + 1e-9, "Compact scale out of range for " + screen);
                Assert(fit.LogicalWidth - ScreenFit.MarginX >= 1856 * 0.8 - 1 && fit.CanvasHeight >= 576 * 0.8 - 8, "Canvas shrank past 80% on " + screen + ": " + fit.CanvasWidth + " x " + fit.CanvasHeight);
                Assert(fit.LogicalWidth * fit.Scale <= screen.Width + 1e-6 && fit.LogicalHeight * fit.Scale <= screen.Height + 1e-6, "Logical size overflows " + screen);
            }
            var small = ScreenFit.For(1280, 800, 1, 1800);
            Assert(Math.Abs(small.Scale - 0.75) < 1e-9 && small.Stacked, "1280 x 800 should use the 0.75 floor and a stacked header: " + small.Scale);
        });
        check("Upright screens get an upright canvas with a stacked header", () => {
            var portrait = ScreenFit.For(1080, 1920, 1, 1800);
            Assert(portrait.Scale == 1 && portrait.LogicalWidth == 1080 && portrait.LogicalHeight == 1920 && portrait.Stacked, "Portrait monitor fit.");
            Assert(portrait.CanvasHeight == Math.Floor((1920 - 36 - 136 - 36) / 8.0) * 8 && portrait.CanvasHeight > portrait.CanvasWidth, "Portrait canvas is not upright.");
        });
        check("Canvas keys round down to the snap grid", () => {
            Assert(WidgetLayout.Key(new Size(1485.7, 764.2)) == new Size(1480, 760) && WidgetLayout.IsReference(new Size(1859, 579)) && !WidgetLayout.IsReference(new Size(1848, 576)), "Keys.");
        });
        check("Minimum sizes shrink to about 80% at most on compact canvases", () => {
            Assert(WidgetLayout.Minimum("Spotify", WidgetLayout.Reference) == WidgetLayout.Minimum("Spotify") && WidgetLayout.Minimum("Spotify", new Size(2496, 936)) == WidgetLayout.Minimum("Spotify"), "Reference or larger canvases changed minimums.");
            Assert(WidgetLayout.Minimum("Spotify", new Size(1480, 552)) == new Size(447, 479), "Compact minimum: " + WidgetLayout.Minimum("Spotify", new Size(1480, 552)));
            Assert(WidgetLayout.Minimum("Spotify", new Size(900, 400)) == new Size(437, 390), "Minimums must also fit the canvas.");
            var resized = WidgetLayout.Transform("Spotify", new Rect(0, 0, 600, 552), "SE", -1000, -1000, false, new Size(1480, 552));
            Assert(resized.Width == 447 && resized.Height == 479, "Resizing ignored the compact minimum.");
            var moved = WidgetLayout.Transform("Focus", new Rect(0, 0, 300, 300), "", 5000, 5000, false, new Size(2496, 576));
            Assert(moved.Right == 2496 && moved.Bottom == 576, "Moving stopped at the reference canvas instead of this one.");
        });
        check("Default arrangement fits every screen shape without overlaps", () => {
            Assert(WidgetLayout.Arrange(WidgetLayout.Reference).Select(p => p.Visible ? p.Bounds : Rect.Empty).Where(r => !r.IsEmpty).SequenceEqual(
                WidgetLayout.SpotifyDefault(new AppSettings()).Where(p => p.Visible).Select(p => p.Bounds)), "Columns on the reference canvas must be the Spotify default.");
            foreach (var screen in new[] { new Size(1920, 720), new Size(2560, 720), new Size(1920, 1080), new Size(1280, 800), new Size(1024, 600), new Size(1080, 1920), new Size(2560, 1080), new Size(1280, 1024), new Size(800, 1280) })
            {
                var fit = ScreenFit.For(screen.Width, screen.Height, 1, 1800); var canvas = Canvas(fit);
                var layout = WidgetLayout.DefaultFor(new AppSettings(), canvas);
                Assert(layout.Length == WidgetLayout.Ids.Length && Inside(layout, canvas) && WidgetLayout.Fits(layout, canvas), "Default escapes or squeezes on " + screen + " (" + WidgetLayout.TemplateFor(canvas) + ")");
                Assert(!Overlaps(layout), "Default overlaps on " + screen + " (" + WidgetLayout.TemplateFor(canvas) + ")");
                Assert(new[] { "Focus", "Media", "Shortcuts", "Mixer", "Spotify" }.All(id => layout.First(p => p.Id == id).Visible) && !layout.First(p => p.Id == "System").Visible, "Default widget set changed on " + screen);
            }
            Assert(WidgetLayout.TemplateFor(new Size(2496, 576)) == "Columns" && WidgetLayout.TemplateFor(new Size(1000, 1560)) == "Tall", "Wide canvases get columns and upright ones rows.");
            var shares = WidgetLayout.Distribute(100, new double[] { 1, 1 }, new double[] { 70, 10 });
            Assert(shares[0] == 70 && shares[1] == 30, "Distribution ignored a minimum.");
        });
        check("Old settings load as the 1856 x 576 layout and other shapes are saved apart", () => {
            string path = Path.Combine(directory, "canvas-layouts.xml"), warning;
            File.WriteAllText(path, "<AppSettings><WidgetLayout><WidgetPlacement><Id>Focus</Id><X>40</X><Y>0</Y><Width>320</Width><Height>576</Height><Visible>true</Visible></WidgetPlacement></WidgetLayout></AppSettings>");
            var settings = SettingsStore.Load(path, out warning);
            Assert(warning == null && settings.CanvasLayouts == null && WidgetLayout.Saved(settings, WidgetLayout.Reference).First(p => p.Id == "Focus").X == 40, "Old layout was not kept as the reference layout.");
            var tall = new Size(1856, 936);
            var edited = WidgetLayout.ForCanvas(settings, tall); edited.First(p => p.Id == "Focus").X = 64;
            WidgetLayout.Store(settings, tall, edited);
            SettingsStore.Save(path, settings); var restored = SettingsStore.Load(path, out warning);
            Assert(warning == null && restored.WidgetLayout.First(p => p.Id == "Focus").X == 40 && restored.CanvasLayouts.Length == 1 && restored.CanvasLayouts[0].Size == tall, "Saving another shape changed the reference layout.");
            Assert(WidgetLayout.ForCanvas(restored, tall).First(p => p.Id == "Focus").X == 64, "The tall layout was not used for its own canvas.");
            Assert(File.ReadAllText(path).Contains("<WidgetLayout>") && File.ReadAllText(path).Contains("<CanvasLayouts>"), "Settings file shape.");
            restored.CanvasLayouts = new[] { new CanvasLayout { Width = 1856, Height = 576, Widgets = edited }, new CanvasLayout { Width = Double.NaN, Height = 4, Widgets = edited }, null,
                new CanvasLayout { Width = 1859, Height = 939, Widgets = new[] { new WidgetPlacement { Id = "Focus", X = -5, Y = 9000, Width = 1, Height = 1 } } } };
            restored.Normalize();
            Assert(restored.CanvasLayouts.Length == 1 && restored.CanvasLayouts[0].Size == tall && restored.CanvasLayouts[0].Widgets.Length == WidgetLayout.Ids.Length && Inside(restored.CanvasLayouts[0].Widgets, tall), "Invalid canvas layouts were not cleaned up.");
            Assert(restored.WidgetLayout.First(p => p.Id == "Focus").X == 40, "A duplicate reference entry replaced the reference layout.");
        });
        check("A new screen shape stretches the closest saved layout or falls back to the default", () => {
            var settings = new AppSettings { WidgetLayout = WidgetLayout.SpotifyDefault(new AppSettings()) };
            // System stats instead of Spotify in the right column.
            settings.WidgetLayout.First(p => p.Id == "Spotify").Visible = false;
            var system = settings.WidgetLayout.First(p => p.Id == "System"); system.Visible = true; WidgetLayout.SetBounds(system, new Rect(1296, 0, 560, 576));
            var wide = new Size(2496, 576);
            var stretched = WidgetLayout.ForCanvas(settings, wide);
            Assert(stretched.First(p => p.Id == "Mixer").X == Math.Round(824 * 2496 / 1856.0) && stretched.First(p => p.Id == "System").Visible && !stretched.First(p => p.Id == "Spotify").Visible, "Wide screen did not stretch the saved layout.");
            Assert(Inside(stretched, wide) && !Overlaps(stretched), "Stretched layout escapes or overlaps.");
            var phone = new Size(1480, 552);
            Assert(WidgetLayout.Fits(WidgetLayout.Stretch(settings.WidgetLayout, WidgetLayout.Reference, phone), phone) && WidgetLayout.ForCanvas(settings, phone).First(p => p.Id == "System").Visible, "A compact sideways canvas should stretch the saved layout like the old phone canvas did.");
            var upright = new Size(1000, 1560);
            var tall = WidgetLayout.ForCanvas(settings, upright);
            Assert(!tall.First(p => p.Id == "System").Visible && tall.First(p => p.Id == "Spotify").Visible && !Overlaps(tall), "An upright canvas should not squeeze a wide layout; it gets the default.");
            var squarer = new Size(1856, 1200);
            WidgetLayout.Store(settings, new Size(1856, 936), WidgetLayout.DefaultFor(new AppSettings(), new Size(1856, 936)));
            Assert(WidgetLayout.ForCanvas(settings, squarer).First(p => p.Id == "Focus").Bounds == WidgetLayout.Stretch(WidgetLayout.Saved(settings, new Size(1856, 936)), new Size(1856, 936), squarer).First(p => p.Id == "Focus").Bounds, "The closest shape was not the one stretched.");
        });
        check("Dashboard takes the screen's shape, edits and saves the layout for that canvas only", () => {
            string path = Path.Combine(directory, "screen-shape.xml"); SettingsStore.Save(path, new AppSettings());
            var previous = Program.ScreenSize;
            try
            {
                Program.ScreenSize = new Size(1920, 1080);
                var window = Window();
                using (var controller = new DashboardController(window, path, true))
                {
                    var dashboard = (FrameworkElement)window.FindName("Dashboard");
                    Assert(dashboard.Width == 1920 && dashboard.Height == 1080 && controller.Editor.CanvasSize == new Size(1856, 936), "Dashboard did not take the 1920 x 1080 shape.");
                    var canvas = (FrameworkElement)window.FindName("WidgetCanvas");
                    Assert(canvas.Width == 1856 && canvas.Height == 936, "Canvas element size.");
                    controller.Editor.Begin(); controller.Editor.Select("Focus"); controller.Editor.TransformSelected("", 40, 0, false);
                    var moved = controller.Editor.Bounds("Focus");
                    ((System.Windows.Controls.Button)window.FindName("SaveLayoutButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.WidgetLayout == null && saved.CanvasLayouts != null && saved.CanvasLayouts.Length == 1 && saved.CanvasLayouts[0].Widgets.First(p => p.Id == "Focus").X == moved.X, "Layout was not saved for the 1856 x 936 canvas.");
                    controller.Editor.Begin(); controller.Editor.TransformSelected("", 8, 0, false);
                    Assert(controller.Editor.SetCanvas(WidgetLayout.Reference) && !controller.Editor.Editing, "A screen change should end the edit.");
                    Assert(controller.Editor.Bounds("Focus") == WidgetLayout.SpotifyDefault(new AppSettings()).First(p => p.Id == "Focus").Bounds, "The reference canvas did not keep its own default.");
                }
                window.Close();
            }
            finally { Program.ScreenSize = previous; }
        });
    }
}
