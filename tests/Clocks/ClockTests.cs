using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WidgetScreen;

// The World clocks widget (docs/design/world-clocks.md): zone names, days and hours against the PC, the saved choice,
// the three designs and the default layout. Times come from Windows' time zones; nothing is fetched.
internal static class ClockTests
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

    public static void Run(string directory, Action<string, Action> check)
    {
        check("World clocks name zones by their first city and word days and hours", () => {
            Assert(WorldClockService.DefaultLabel(WorldClockService.Find("Tokyo Standard Time")) == "Osaka" && WorldClockService.DefaultLabel(WorldClockService.Find("Eastern Standard Time")) == "Eastern Time" && WorldClockService.DefaultLabel(WorldClockService.Find("UTC")) == "UTC", "Default names are wrong: " + WorldClockService.DefaultLabel(WorldClockService.Find("Eastern Standard Time")));
            Assert(WorldClockService.Difference(0) == "Same time" && WorldClockService.Difference(180) == "+3 h" && WorldClockService.Difference(-330) == "−5:30 h" && WorldClockService.Difference(345) == "+5:45 h", "Hour differences are worded wrongly.");
            Assert(WorldClockService.Day(0) == "Today" && WorldClockService.Day(1) == "Tomorrow" && WorldClockService.Day(-1) == "Yesterday" && WorldClockService.Day(2) == "+2 days", "Days are worded wrongly.");
            var at = new DateTime(2026, 11, 1, 22, 5, 0);
            Assert(WorldClockService.Time(at, false) == "10:05" && WorldClockService.Time(at, true) == "22:05" && WorldClockService.Period(at) == "PM", "Times are formatted wrongly.");
        });
        check("World clocks work out time, day and hours against the PC's zone", () => {
            var riyadh = WorldClockService.Find("Arab Standard Time");
            var clocks = new[] { new WorldClock { Zone = "Tokyo Standard Time", Label = "Tokyo" }, new WorldClock { Zone = "Hawaiian Standard Time", Label = "Honolulu" }, new WorldClock { Zone = "Arab Standard Time", Label = "Riyadh" } };
            // 1 Nov 2026 20:30 UTC: 23:30 in Riyadh, 05:30 on the 2nd in Tokyo, 10:30 on the 1st in Honolulu.
            var readings = WorldClockService.Read(clocks, new DateTime(2026, 11, 1, 20, 30, 0, DateTimeKind.Utc), riyadh);
            Assert(readings.Count == 3 && readings[0].Time.Hour == 5 && readings[0].DayOffset == 1 && readings[0].DifferenceMinutes == 360 && readings[0].OffsetMinutes == 540, "Tokyo is read wrongly.");
            Assert(readings[1].Time.Hour == 10 && readings[1].DayOffset == 0 && readings[1].DifferenceMinutes == -780 && readings[2].DifferenceMinutes == 0 && readings[2].DayOffset == 0, "Honolulu or Riyadh is read wrongly.");
            Assert(WorldClockService.Date(readings[0]) == "Tomorrow, Nov 2", "The large date line is wrong.");
            // Daylight saving follows Windows' rules: London is UTC+1 in July and UTC in December.
            var london = new[] { new WorldClock { Zone = "GMT Standard Time", Label = "London" } };
            Assert(WorldClockService.Read(london, new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc)[0].OffsetMinutes == 60 && WorldClockService.Read(london, new DateTime(2026, 12, 1, 12, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc)[0].OffsetMinutes == 0, "Daylight saving isn't followed.");
        });
        check("World clocks keep three known zones, fill empty names and are saved", () => {
            var cleaned = WorldClockService.Clean(new[] { new WorldClock { Zone = "Nowhere Standard Time", Label = "X" }, null, new WorldClock { Zone = "Tokyo Standard Time", Label = "  " }, new WorldClock { Zone = "UTC", Label = "A very long city name that keeps going" }, new WorldClock { Zone = "GMT Standard Time" }, new WorldClock { Zone = "Arab Standard Time" } });
            Assert(cleaned.Length == 3 && cleaned[0].Label == "Osaka" && cleaned[1].Label.Length <= WorldClockService.MaxLabel && cleaned[2].Zone == "GMT Standard Time", "Clocks aren't cleaned.");
            Assert(new AppSettings().WorldClocks == null || new AppSettings().WorldClocks.Length == 0, "Clocks aren't empty by default.");
            string path = Path.Combine(directory, "clocks-settings.xml"), warning;
            SettingsStore.Save(path, new AppSettings { WorldClocks = new[] { new WorldClock { Zone = "Tokyo Standard Time", Label = "Tokyo" }, new WorldClock { Zone = "Gone Standard Time", Label = "Gone" } } });
            var loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.WorldClocks.Length == 1 && loaded.WorldClocks[0].Label == "Tokyo" && loaded.Copy().WorldClocks[0] != loaded.WorldClocks[0], "Clocks weren't saved, cleaned or copied.");
            File.WriteAllText(path, "<?xml version=\"1.0\"?><AppSettings><Theme>Slate</Theme></AppSettings>");
            Assert(SettingsStore.Load(path, out warning).WorldClocks.Length == 0, "Older settings didn't get an empty clock list.");
        });
        check("World clocks start hidden and older layouts get a hidden placement", () => {
            var settings = new AppSettings();
            Assert(WidgetLayout.Ids.Contains("Clocks") && WidgetLayout.Title("Clocks") == "World clocks" && !WidgetLayout.SpotifyDefault(settings).First(p => p.Id == "Clocks").Visible, "World clocks are visible by default.");
            var old = WidgetLayout.Default(settings).Where(p => p.Id != "Clocks").ToArray();
            Assert(WidgetLayout.Normalize(old, settings).Any(p => p.Id == "Clocks" && !p.Visible), "Layouts saved before World clocks did not get a hidden placement.");
            foreach (var canvas in new[] { new Size(1856, 576), new Size(1200, 640), new Size(656, 1712) })
                Assert(!WidgetLayout.DefaultFor(settings, canvas).First(p => p.Id == "Clocks").Visible, "World clocks show by default on " + canvas + ".");
        });
        check("World clocks pick small, medium and large designs", () => {
            Assert(CardSizing.Clocks(240, 152) == CardSizing.Design.Small && CardSizing.Clocks(600, 199) == CardSizing.Design.Small && CardSizing.Clocks(472, 250) == CardSizing.Design.Medium && CardSizing.Clocks(439, 576) == CardSizing.Design.Medium && CardSizing.Clocks(440, 300) == CardSizing.Design.Large, "Clock designs are picked at the wrong sizes.");
            Assert(CardSizing.ClockRows(68) == 1 && CardSizing.ClockRows(112) == 3 && CardSizing.ClockTilesAcross(1000, 3) && !CardSizing.ClockTilesAcross(392, 3) && CardSizing.ClockTilesAcross(392, 2) && CardSizing.ClockColumnTime(133, 166, false) == 36 && CardSizing.ClockListTime(50) == 28, "Clock sizes are wrong.");
        });
        check("World clock designs keep every city's text inside the card", () => {
            var clocks = new[] { new WorldClock { Zone = "Tokyo Standard Time", Label = "Tokyo" }, new WorldClock { Zone = "Pacific Standard Time", Label = "San Francisco Bay" }, new WorldClock { Zone = "India Standard Time", Label = "Mumbai" } };
            foreach (bool use24 in new[] { false, true })
                foreach (var size in new[] { new Size(240, 152), new Size(320, 400), new Size(340, 200), new Size(472, 250), new Size(440, 340), new Size(472, 576), new Size(1200, 420) })
                {
                    string path = Path.Combine(directory, "clocks-" + size.Width + "x" + size.Height + ".xml");
                    var settings = new AppSettings { TextScale = 1.4, Use24HourClock = use24, WorldClocks = clocks };
                    settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Clocks"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                    SettingsStore.Save(path, settings);
                    using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                    {
                        var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                        try
                        {
                            using (var controller = new DashboardController(window, path, true))
                            {
                                var dashboard = (FrameworkElement)window.FindName("Dashboard");
                                dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                                var card = (FrameworkElement)window.FindName("ClocksCard");
                                Assert(card.Visibility == Visibility.Visible && window.FindName("ClockTime0") != null && ((System.Windows.Controls.TextBlock)window.FindName("ClockTime0")).Text.Contains(":"), "World clocks built no design at " + size + ".");
                                Assert(controller.ClocksView.Current == CardSizing.Clocks(size.Width, size.Height), "World clocks picked the wrong design at " + size + ".");
                                int shown = controller.ClocksView.Shown;
                                Assert(shown == 3 || ((System.Windows.Controls.TextBlock)window.FindName("ClocksStatus")).Text == "+" + (3 - shown) + " MORE", "Clocks that don't fit aren't counted at " + size + ".");
                                foreach (var part in Descendants(card).OfType<System.Windows.Controls.TextBlock>().Where(e => Shown(e, card) && e.ActualWidth > 0 && e.Text.Length > 0))
                                {
                                    var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                    Assert(box.Left >= 23.5 && box.Top >= 23.5 && box.Right <= card.ActualWidth - 23.5 && box.Bottom <= card.ActualHeight - 23.5, "World clocks push \"" + part.Text + "\" out of the " + size + " card" + (use24 ? " (24 h)" : "") + ": " + box);
                                }
                                // Settings → Widgets shows the saved zones and names (its fields never clash with the card's parts).
                                if (size.Width == 472 && size.Height == 250 && !use24)
                                {
                                    ((System.Windows.Controls.Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    var zone = (System.Windows.Controls.ComboBox)window.FindName("ClockZone1"); var name = (System.Windows.Controls.TextBox)window.FindName("ClockName1");
                                    Assert(zone.SelectedItem != null && zone.SelectedItem.ToString().Contains("Pacific") && name.Text == "San Francisco Bay" && ((System.Windows.Controls.ComboBox)window.FindName("ClockZone2")).SelectedItem.ToString().Contains("Mumbai"), "Settings don't show the saved clocks.");
                                    ((System.Windows.Controls.Button)window.FindName("CancelSettings")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                }
                                // A time is never cut short; names and lines may end in an ellipsis.
                                for (int i = 0; i < shown; i++)
                                {
                                    var time = (System.Windows.Controls.TextBlock)window.FindName("ClockTime" + i);
                                    var full = new System.Windows.Controls.TextBlock { Text = time.Text, FontSize = time.FontSize, FontWeight = time.FontWeight, FontFamily = time.FontFamily, LineStackingStrategy = time.LineStackingStrategy, LineHeight = time.LineHeight }; System.Windows.Documents.Typography.SetNumeralAlignment(full, System.Windows.FontNumeralAlignment.Tabular); System.Windows.Media.TextOptions.SetTextFormattingMode(full, System.Windows.Media.TextOptions.GetTextFormattingMode(time));
                                    full.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                                    Assert(full.DesiredSize.Width <= time.ActualWidth + 1.5, "The time \"" + time.Text + "\" is cut at " + size + (use24 ? " (24 h)" : "") + ": " + full.DesiredSize.Width + " in " + time.ActualWidth + " at " + time.FontSize + ", row " + ((FrameworkElement)time.Parent).ActualWidth);
                                }
                            }
                        }
                        finally { window.Close(); }
                    }
                }
        });
    }
}
