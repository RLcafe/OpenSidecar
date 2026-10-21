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
    private static AppSettings settingsOf(string path) { string warning; return SettingsStore.Load(path, out warning); }
    private static string Describe(WidgetPlacement[] layout) { return String.Join("; ", layout.Where(p => p.Visible).Select(p => p.Id + " " + p.X + "," + p.Y + " " + p.Width + "x" + p.Height)); }

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
            Assert(WidgetLayout.Arrange(WidgetLayout.Reference, WidgetLayout.DefaultShown).Select(p => p.Visible ? p.Bounds : Rect.Empty).Where(r => !r.IsEmpty).SequenceEqual(
                WidgetLayout.SpotifyDefault(new AppSettings()).Where(p => p.Visible).Select(p => p.Bounds)), "Columns on the reference canvas must be the Spotify default.");
            foreach (var screen in new[] { new Size(1920, 720), new Size(2560, 720), new Size(1920, 1080), new Size(1280, 800), new Size(1024, 600), new Size(1080, 1920), new Size(2560, 1080), new Size(1280, 1024), new Size(800, 1280) })
            {
                var fit = ScreenFit.For(screen.Width, screen.Height, 1, 1800); var canvas = Canvas(fit);
                var layout = WidgetLayout.DefaultFor(new AppSettings(), canvas);
                Assert(layout.Length == WidgetLayout.Ids.Length && Inside(layout, canvas) && WidgetLayout.Fits(layout, canvas), "Default escapes or squeezes on " + screen + ": " + Describe(layout));
                Assert(!Overlaps(layout), "Default overlaps on " + screen + ": " + Describe(layout));
                Assert(new[] { "Focus", "Media", "Shortcuts", "Mixer", "Spotify" }.All(id => layout.First(p => p.Id == id).Visible) && !layout.First(p => p.Id == "System").Visible, "Default widget set changed on " + screen);
            }
            var wide = WidgetLayout.Arrange(new Size(2496, 576), WidgetLayout.DefaultShown);
            Assert(wide.Where(p => p.Visible && p.Id != "Shortcuts").All(p => p.Y == 0), "A wide canvas should get columns: " + Describe(wide));
            var upright = WidgetLayout.Normalize(WidgetLayout.Arrange(new Size(1000, 1560), WidgetLayout.DefaultShown), new AppSettings(), new Size(1000, 1560));
            Assert(!Overlaps(upright) && WidgetLayout.Fits(upright, new Size(1000, 1560)) && upright.Where(p => p.Visible).Max(p => p.Y) > 0, "An upright canvas should stack cards: " + Describe(upright));
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
            Assert(tall.First(p => p.Id == "System").Visible && !tall.First(p => p.Id == "Spotify").Visible && !Overlaps(tall) && WidgetLayout.Fits(tall, upright), "An upright canvas should not squeeze a wide layout; it arranges the page's widgets afresh: " + Describe(tall));
            var squarer = new Size(1856, 1200);
            WidgetLayout.Store(settings, new Size(1856, 936), WidgetLayout.DefaultFor(new AppSettings(), new Size(1856, 936)));
            Assert(WidgetLayout.ForCanvas(settings, squarer).First(p => p.Id == "Focus").Bounds == WidgetLayout.Stretch(WidgetLayout.Saved(settings, new Size(1856, 936)), new Size(1856, 936), squarer).First(p => p.Id == "Focus").Bounds, "The closest shape was not the one stretched.");
        });
        check("Any set of shown widgets is arranged without overlaps on every shape", () => {
            var sets = new[] { new[] { "Mixer" }, new[] { "Focus", "Mixer", "System", "Weather" }, WidgetLayout.Ids, new[] { "Spotify", "Soundboard", "Shortcuts" } };
            foreach (var canvas in new[] { new Size(1856, 576), new Size(2496, 576), new Size(1856, 936), new Size(1480, 552), new Size(1000, 1560), new Size(1480, 696) })
                foreach (var shown in sets)
                {
                    var layout = WidgetLayout.Normalize(WidgetLayout.Arrange(canvas, shown), new AppSettings(), canvas);
                    Assert(Inside(layout, canvas) && layout.Count(p => p.Visible) == shown.Length && layout.Where(p => !shown.Contains(p.Id)).All(p => !p.Visible), "Arrangement escapes or shows the wrong widgets on " + canvas);
                    if (shown.Length < WidgetLayout.Ids.Length || canvas.Width * canvas.Height > 1500000) Assert(!Overlaps(layout), "Arrangement overlaps on " + canvas + ": " + Describe(layout));
                }
        });
        check("Pages: old settings are one page; add, rename, move and delete keep the page shown", () => {
            string path = Path.Combine(directory, "pages.xml"), warning;
            File.WriteAllText(path, "<AppSettings><WidgetLayout><WidgetPlacement><Id>Focus</Id><X>40</X><Y>0</Y><Width>320</Width><Height>576</Height><Visible>true</Visible></WidgetPlacement></WidgetLayout></AppSettings>");
            var s = SettingsStore.Load(path, out warning);
            Assert(WidgetLayout.PageCount(s) == 1 && WidgetLayout.CurrentPage(s) == 0 && s.PageName == "Main" && s.MorePages == null && WidgetLayout.ForCanvas(s, WidgetLayout.Reference).First(p => p.Id == "Focus").X == 40, "Old settings did not become one page.");
            Assert(WidgetLayout.AddPage(s, "  Gaming  ") == 1 && WidgetLayout.CurrentPage(s) == 1 && WidgetLayout.Page(s, 1).Name == "Gaming", "Add page.");
            Assert(WidgetLayout.ForCanvas(s, WidgetLayout.Reference).First(p => p.Id == "Focus").X == 0, "A new page should start from the default arrangement.");
            var gaming = WidgetLayout.ForCanvas(s, WidgetLayout.Reference); gaming.First(p => p.Id == "Spotify").Visible = false; gaming.First(p => p.Id == "System").Visible = true;
            WidgetLayout.Store(s, WidgetLayout.Reference, gaming);
            WidgetLayout.Store(s, 1, new Size(1856, 936), WidgetLayout.DefaultFor(new AppSettings(), new Size(1856, 936)));
            Assert(WidgetLayout.Saved(WidgetLayout.Page(s, 1), WidgetLayout.Reference).First(p => p.Id == "Spotify").Visible, "Saving one shape did not carry the page's shown widgets to its other shapes.");
            Assert(WidgetLayout.ForCanvas(s, 0, WidgetLayout.Reference).First(p => p.Id == "Focus").X == 40, "Another page changed the first page.");
            WidgetLayout.RenamePage(s, 1, new string('x', 40)); Assert(WidgetLayout.Page(s, 1).Name.Length == 24, "Long names are cut to 24.");
            WidgetLayout.AddPage(s, ""); Assert(WidgetLayout.Page(s, 2).Name == "Page 3" && WidgetLayout.CurrentPage(s) == 2, "Unnamed page.");
            WidgetLayout.MovePage(s, 2, 0); Assert(WidgetLayout.Page(s, 0).Name == "Page 3" && WidgetLayout.CurrentPage(s) == 0 && s.WidgetLayout == null, "Moving a page to the front.");
            SettingsStore.Save(path, s); var back = SettingsStore.Load(path, out warning);
            Assert(warning == null && WidgetLayout.PageCount(back) == 3 && WidgetLayout.Page(back, 1).Name == "Main" && WidgetLayout.ForCanvas(back, 1, WidgetLayout.Reference).First(p => p.Id == "Focus").X == 40 && WidgetLayout.CurrentPage(back) == 0, "Pages did not survive a save.");
            Assert(WidgetLayout.DeletePage(back, 0) && WidgetLayout.DeletePage(back, 0) && !WidgetLayout.DeletePage(back, 0) && WidgetLayout.PageCount(back) == 1, "The last page must stay.");
            for (int i = 0; i < 10; i++) WidgetLayout.AddPage(back, "More");
            Assert(WidgetLayout.PageCount(back) == WidgetLayout.MaxPages && WidgetLayout.AddPage(back, "Too many") == -1, "Page limit.");
            back.CurrentPage = 99; back.Normalize(); Assert(back.CurrentPage == WidgetLayout.MaxPages - 1, "Current page out of range.");
        });
        check("Switching pages shows that page's widgets and stops the others", () => {
            string path = Path.Combine(directory, "page-switch.xml");
            var s = new AppSettings(); WidgetLayout.AddPage(s, "Stats");
            var stats = WidgetLayout.ForCanvas(s, WidgetLayout.Reference); foreach (var p in stats) p.Visible = p.Id == "System" || p.Id == "Weather";
            WidgetLayout.Store(s, WidgetLayout.Reference, stats); s.CurrentPage = 0; SettingsStore.Save(path, s);
            var window = Window();
            using (var controller = new DashboardController(window, path, true))
            {
                Assert(controller.Editor.IsVisible("Mixer") && !controller.Editor.IsVisible("Weather"), "First page.");
                Assert(((FrameworkElement)window.FindName("PageSwitcher")).Visibility == Visibility.Visible && ((System.Windows.Controls.TextBlock)window.FindName("PageCountText")).Text == "1 of 2", "Page switcher.");
                ((System.Windows.Controls.Button)window.FindName("NextPageButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert(!controller.Editor.IsVisible("Mixer") && controller.Editor.IsVisible("Weather") && ((System.Windows.Controls.TextBlock)window.FindName("PageNameText")).Text == "Stats", "Next page did not show the second page's widgets.");
                string warning; Assert(SettingsStore.Load(path, out warning).CurrentPage == 1, "The page shown was not saved.");
                Assert(!controller.ShowPage(2) && !((System.Windows.Controls.Button)window.FindName("NextPageButton")).IsEnabled, "Paging past the last page.");
                controller.Editor.Begin(); Assert(!controller.ShowPage(0), "Pages switched during an edit."); controller.Editor.Cancel();
            }
            window.Close();
        });
        check("Edit layout's list shows and hides widgets and manages pages like the phone's", () => {
            string path = Path.Combine(directory, "edit-panel.xml"); SettingsStore.Save(path, new AppSettings());
            var window = Window();
            using (var controller = new DashboardController(window, path, true))
            {
                Action<string> click = element => ((System.Windows.Controls.Button)window.FindName(element)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var content = (System.Windows.Controls.StackPanel)window.FindName("EditPanelContent");
                Func<string, System.Windows.Controls.Button> button = text => content.Children.OfType<FrameworkElement>().SelectMany(e => e is System.Windows.Controls.Grid ? ((System.Windows.Controls.Grid)e).Children.OfType<FrameworkElement>() : new[] { e })
                    .OfType<System.Windows.Controls.Button>().First(b => (b.Content is string ? (string)b.Content : b.Content is System.Windows.Controls.TextBlock ? ((System.Windows.Controls.TextBlock)b.Content).Text : "") == text);
                Func<string, System.Windows.Controls.CheckBox> tick = title => content.Children.OfType<System.Windows.Controls.Border>().Select(b => b.Child).OfType<System.Windows.Controls.CheckBox>().First(c => (string)c.Content == title);
                click("EditLayoutButton"); click("EditWidgetsButton");
                Assert(controller.EditPanelOpen && ((FrameworkElement)window.FindName("EditPanelLayer")).Visibility == Visibility.Visible, "The list didn't open.");
                Assert(tick("Sound mixer").IsChecked == true && tick("System stats").IsChecked == false && tick("Sound mixer").MinHeight >= 52, "Check marks don't match the layout, or rows are under 52.");
                var mixer = tick("Sound mixer"); mixer.IsChecked = false; mixer.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(!controller.Editor.IsVisible("Mixer") && controller.Editor.CanUndo, "Unchecking didn't hide the widget.");
                var stats = tick("System stats"); stats.IsChecked = true; stats.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert(controller.Editor.IsEnabled("System") && controller.Editor.SelectedId == "System", "Checking didn't show and select the widget.");
                button("Add page").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                string warning; var saved = SettingsStore.Load(path, out warning);
                Assert(WidgetLayout.PageCount(saved) == 2 && WidgetLayout.CurrentPage(saved) == 1 && !WidgetLayout.ForCanvas(saved, 0, WidgetLayout.Reference).First(p => p.Id == "Mixer").Visible, "Adding a page didn't save the changes first or show the new page.");
                Assert(controller.Editor.Editing && controller.EditPanelOpen && controller.Editor.IsVisible("Mixer") && !controller.Editor.CanUndo, "The editor didn't continue on the new page.");
                var pageName = content.Children.OfType<System.Windows.Controls.Grid>().SelectMany(g => g.Children.OfType<System.Windows.Controls.TextBox>()).First();
                pageName.Text = "Games"; button("Rename").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert(WidgetLayout.Page(SettingsStore.Load(path, out warning), 1).Name == "Games", "Rename.");
                button("Main").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert(WidgetLayout.CurrentPage(settingsOf(path)) == 0 && controller.Editor.Editing && !controller.Editor.IsVisible("Mixer"), "Opening another page from the list.");
                var deleteGames = content.Children.OfType<System.Windows.Controls.Grid>().Where(g => g.Children.OfType<System.Windows.Controls.Button>().Any(b => b.Content is System.Windows.Controls.TextBlock && ((System.Windows.Controls.TextBlock)b.Content).Text == "Games"))
                    .SelectMany(g => g.Children.OfType<System.Windows.Controls.Button>()).Last();
                deleteGames.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert(WidgetLayout.PageCount(settingsOf(path)) == 2, "Delete didn't ask first.");
                deleteGames.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert(WidgetLayout.PageCount(settingsOf(path)) == 1, "Delete after confirming.");
                click("EditWidgetsButton"); Assert(!controller.EditPanelOpen, "The Widgets button closes the list.");
                click("CancelLayoutButton");
            }
            window.Close();
        });
        check("Cards size their content: Quick Launch fills its card, Focus, cover and temperature grow", () => {
            Assert(CardSizing.DeckGrid(430, 160).SequenceEqual(new[] { 3, 2 }) && CardSizing.DeckGrid(378, 100).SequenceEqual(new[] { 3, 1 }) && CardSizing.FocusTime(320, 576) == 84 && CardSizing.MediaArt(472, 310) == 104 && CardSizing.WeatherTemperature(472, 250) == 54 && CardSizing.SpotifyColumns(752) == 2, "The reference card sizes changed.");
            string path = Path.Combine(directory, "card-sizing.xml");
            var s = new AppSettings { Shortcuts = Enumerable.Range(1, 20).Select(i => new Shortcut { Label = "B" + i, Action = "Website", Target = "https://example.com/" + i, Glyph = "\uE774" }).ToList() };
            s.WidgetLayout = WidgetLayout.SpotifyDefault(s);
            WidgetLayout.SetBounds(s.WidgetLayout.First(p => p.Id == "Shortcuts"), new Rect(336, 0, 944, 576));
            foreach (var p in s.WidgetLayout) if (p.Id == "Media" || p.Id == "Mixer") p.Visible = false;
            WidgetLayout.SetBounds(s.WidgetLayout.First(p => p.Id == "Focus"), new Rect(0, 0, 320, 576));
            SettingsStore.Save(path, s);
            var window = Window();
            using (var controller = new DashboardController(window, path, true))
            {
                var dashboard = (FrameworkElement)window.FindName("Dashboard"); dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                var grid = (System.Windows.Controls.Primitives.UniformGrid)window.FindName("ShortcutGrid");
                Assert(grid.Columns * grid.Rows >= 20 && grid.Columns * grid.Rows < 24 && grid.Children.Count == 20, "A big Quick Launch card should hold more buttons: " + grid.Columns + " x " + grid.Rows);
                foreach (System.Windows.Controls.Button button in grid.Children) Assert(button.ActualWidth >= 48 && button.ActualHeight >= 48, "A deck button is under 48.");
                Assert(((System.Windows.Controls.TextBlock)window.FindName("FocusTime")).FontSize == 84, "Focus countdown on the reference card.");
            }
            window.Close();
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
                    Assert(controller.Editor.Bounds("Focus") == WidgetLayout.ForCanvas(SettingsStore.Load(path, out warning), WidgetLayout.Reference).First(p => p.Id == "Focus").Bounds && WidgetLayout.Saved(SettingsStore.Load(path, out warning), WidgetLayout.Reference) == null, "The reference canvas should follow the rules for an unsaved shape.");
                }
                window.Close();
            }
            finally { Program.ScreenSize = previous; }
        });
    }
}
