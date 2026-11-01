using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WidgetScreen;

// The Storage widget (docs/design/storage.md): sizes, the low-space threshold, the three designs and the default
// layout. The drive reading is live but only read, never written.
internal static class StorageTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
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
    private const long GB = 1024L * 1024 * 1024;

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Storage sizes read like File Explorer and low drives are flagged", () => {
            Assert(StorageService.FormatBytes(820.0 * 1024 * 1024) == "820 MB" && StorageService.FormatBytes(45.24 * GB) == "45.2 GB" && StorageService.FormatBytes(212.4 * GB) == "212 GB" && StorageService.FormatBytes(1.82 * 1024 * GB) == "1.82 TB" && StorageService.FormatBytes(12.5 * 1024 * GB) == "12.5 TB" && StorageService.FormatBytes(-5) == "0 KB", "Sizes are formatted wrongly.");
            var c = new StorageDrive { Name = "C:", Label = "Windows", TotalBytes = 500 * GB, FreeBytes = 40 * GB };
            var d = new StorageDrive { Name = "D:", Label = "", TotalBytes = 1000 * GB, FreeBytes = 600 * GB };
            var e = new StorageDrive { Name = "E:", Label = "Games", TotalBytes = 2000 * GB, FreeBytes = 100 * GB };
            Assert(StorageService.Low(c, 10) && !StorageService.Low(d, 10) && !StorageService.Low(e, 4) && StorageService.Low(e, 10) && Math.Round(c.FreePercent) == 8 && Math.Round(c.UsedPercent) == 92, "Low space is judged wrongly.");
            Assert(StorageService.LowFlag(new[] { c, d }, 10) == "C: LOW" && StorageService.LowFlag(new[] { c, d, e }, 10) == "2 DRIVES LOW" && StorageService.LowFlag(new[] { d }, 10) == "", "The heading flag is wrong.");
            Assert(StorageService.Title(c) == "C:  Windows" && StorageService.Title(d) == "D:", "Drive titles are wrong.");
            Assert(StorageService.Order(new[] { e, c, d }).Select(x => x.Name).SequenceEqual(new[] { "C:", "D:", "E:" }), "Drives are not in letter order.");
            Assert(StorageService.ClampPercent(0) == 1 && StorageService.ClampPercent(90) == 50 && StorageService.ClampPercent(15) == 15, "The threshold isn't clamped.");
        });
        check("Storage reads this PC's fixed drives without failing", () => {
            var drives = new StorageService().Read();
            Assert(drives.All(x => x.TotalBytes > 0 && x.FreeBytes >= 0 && x.FreeBytes <= x.TotalBytes && x.Name.EndsWith(":")), "A drive reading is impossible.");
            string system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd('\\');
            Assert(drives.Any(x => String.Equals(x.Name, system, StringComparison.OrdinalIgnoreCase)), "The Windows drive " + system + " is missing.");
        });
        check("Storage threshold is saved, clamped and defaults to 10%", () => {
            string path = Path.Combine(directory, "storage-settings.xml");
            Assert(new AppSettings().StorageLowPercent == 10, "The default threshold isn't 10%.");
            var settings = new AppSettings { StorageLowPercent = 25 }; SettingsStore.Save(path, settings);
            string warning; Assert(SettingsStore.Load(path, out warning).StorageLowPercent == 25, "The threshold wasn't saved.");
            settings.StorageLowPercent = 99; SettingsStore.Save(path, settings); Assert(SettingsStore.Load(path, out warning).StorageLowPercent == 50, "The threshold wasn't clamped.");
            File.WriteAllText(path, "<?xml version=\"1.0\"?><AppSettings><Theme>Slate</Theme></AppSettings>");
            Assert(SettingsStore.Load(path, out warning).StorageLowPercent == 10, "Older settings didn't get the default threshold.");
        });
        check("Storage starts hidden and older layouts get a hidden placement", () => {
            var settings = new AppSettings();
            Assert(WidgetLayout.Ids.Contains("Storage") && !WidgetLayout.Default(settings).First(p => p.Id == "Storage").Visible && !WidgetLayout.SpotifyDefault(settings).First(p => p.Id == "Storage").Visible, "Storage is visible by default.");
            Assert(WidgetLayout.SpotifyDefault(settings).Where(p => p.Visible).Select(p => p.Id).OrderBy(x => x).SequenceEqual(WidgetLayout.DefaultShown.OrderBy(x => x)), "The default layout changed.");
            var old = WidgetLayout.Default(settings).Where(p => p.Id != "Storage").ToArray();
            Assert(WidgetLayout.Normalize(old, settings).Any(p => p.Id == "Storage" && !p.Visible), "Layouts saved before Storage did not get a hidden placement.");
            foreach (var canvas in new[] { new Size(1856, 576), new Size(1200, 640), new Size(656, 1712) })
                Assert(!WidgetLayout.DefaultFor(settings, canvas).First(p => p.Id == "Storage").Visible, "Storage shows by default on " + canvas + ".");
        });
        check("Storage picks small, medium and large designs and counts drives that don't fit", () => {
            Assert(CardSizing.Storage(240, 152) == CardSizing.Design.Small && CardSizing.Storage(600, 199) == CardSizing.Design.Small && CardSizing.Storage(472, 250) == CardSizing.Design.Medium && CardSizing.Storage(439, 576) == CardSizing.Design.Medium && CardSizing.Storage(440, 340) == CardSizing.Design.Large, "Storage designs are picked at the wrong sizes.");
            Assert(CardSizing.StorageCapacity(CardSizing.Design.Small, 192, 68, 3) == 1 && CardSizing.StorageCapacity(CardSizing.Design.Medium, 424, 166, 5) == 4 && CardSizing.StorageDetailRows(166, 2) && !CardSizing.StorageDetailRows(166, 3) && CardSizing.StorageCapacity(CardSizing.Design.Large, 424, 492, 3) == 6 && CardSizing.StorageCapacity(CardSizing.Design.Large, 1200, 300, 6) == 4, "Storage capacity is wrong.");
            Assert(CardSizing.StorageTileColumns(472, 1) == 1 && CardSizing.StorageTileColumns(472, 3) == 2 && CardSizing.StorageTileColumns(1200, 6) == 4 && CardSizing.StorageRing(200, 240) == 144 && CardSizing.StorageListValue(40) == 20, "Storage tile sizes are wrong.");
        });
        check("Storage designs keep every drive's text and bars inside the card", () => {
            // A labelled test fixture: four made-up drives, one of them low.
            var fixture = new List<StorageDrive> {
                new StorageDrive { Name = "C:", Label = "Windows with a long volume name", TotalBytes = 1000 * GB, FreeBytes = 60 * GB },
                new StorageDrive { Name = "D:", Label = "Data", TotalBytes = 2000 * GB, FreeBytes = 1200 * GB },
                new StorageDrive { Name = "E:", Label = "Games", TotalBytes = 4000 * GB, FreeBytes = 900 * GB },
                new StorageDrive { Name = "F:", Label = "", TotalBytes = 256 * GB, FreeBytes = 200 * GB } };
            foreach (var size in new[] { new Size(240, 152), new Size(320, 400), new Size(472, 250), new Size(472, 576), new Size(1200, 420) })
            {
                string path = Path.Combine(directory, "storage-" + size.Width + "x" + size.Height + ".xml");
                var settings = new AppSettings { TextScale = 1.4 };
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Storage"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                SettingsStore.Save(path, settings);
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            controller.StorageView.Update(fixture);
                            var dashboard = (FrameworkElement)window.FindName("Dashboard");
                            dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                            var card = (FrameworkElement)window.FindName("StorageCard");
                            Assert(card.Visibility == Visibility.Visible && window.FindName("StorageView") != null && window.FindName("StorageFree0") != null, "Storage built no design at " + size + ".");
                            Assert(controller.StorageView.Current == CardSizing.Storage(size.Width, size.Height), "Storage picked the wrong design at " + size + ".");
                            Assert(((System.Windows.Controls.TextBlock)window.FindName("StorageStatus")).Text == "C: LOW", "The low drive isn't named in the heading.");
                            int shown = Enumerable.Range(0, 4).Count(i => window.FindName("StorageFree" + i) != null);
                            Assert(shown == 4 || (window.FindName("StorageMore") != null && ((System.Windows.Controls.TextBlock)window.FindName("StorageMore")).Text.StartsWith("+" + (4 - shown) + " more")), "Drives that don't fit aren't counted at " + size + ".");
                            foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is System.Windows.Controls.TextBlock || e is RingGauge || e is MeterBar)))
                            {
                                var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                Assert(box.Left >= 23.5 && box.Top >= 23.5 && box.Right <= card.ActualWidth - 23.5 && box.Bottom <= card.ActualHeight - 23.5, "Storage pushes " + part.GetType().Name + " " + (part is System.Windows.Controls.TextBlock ? "\"" + ((System.Windows.Controls.TextBlock)part).Text + "\" " : "") + "out of its " + size + " card: " + box);
                            }
                        }
                    }
                    finally { window.Close(); }
                }
            }
        });
    }
}
