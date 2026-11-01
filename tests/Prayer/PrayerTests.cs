using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WidgetScreen;

// Prayer times (docs/design/prayer-times.md): the calculation against published timetables, the next prayer and its
// countdown, the saved choices, the designs and the alert chime. Nothing is played aloud.
internal static class PrayerTests
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
    // Compares a UTC time with a published local "HH:mm", within two minutes.
    private static void Near(DateTime utc, string expected, TimeZoneInfo zone, string what)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
        var parts = expected.Split(':'); double minutes = Math.Abs(local.TimeOfDay.TotalMinutes - (int.Parse(parts[0]) * 60 + int.Parse(parts[1])));
        Assert(utc != DateTime.MinValue && minutes <= 2, what + " is " + local.ToString("HH:mm") + ", the timetable says " + expected + ".");
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Prayer times match published timetables (Riyadh, and London in June)", () => {
            // Reference: the Aladhan timetable for 1 Nov 2026 in Riyadh, Umm al-Qura, standard Asr.
            var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
            var day = PrayerService.Calculate(new DateTime(2026, 11, 1), 24.7136, 46.6753, "Makkah", false);
            Near(day.Fajr, "04:41", riyadh, "Riyadh Fajr"); Near(day.Sunrise, "06:00", riyadh, "Riyadh sunrise"); Near(day.Dhuhr, "11:37", riyadh, "Riyadh Dhuhr");
            Near(day.Asr, "14:50", riyadh, "Riyadh Asr"); Near(day.Maghrib, "17:13", riyadh, "Riyadh Maghrib"); Near(day.Isha, "18:43", riyadh, "Riyadh Isha");
            // 15 Jun 2026 in London, Muslim World League, Hanafi Asr: the sun never reaches 18°, so Fajr and Isha use the
            // angle-based rule, as the published timetable does.
            var london = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
            day = PrayerService.Calculate(new DateTime(2026, 6, 15), 51.5074, -0.1278, "MWL", true);
            Near(day.Fajr, "02:30", london, "London Fajr"); Near(day.Sunrise, "04:43", london, "London sunrise"); Near(day.Dhuhr, "13:01", london, "London Dhuhr");
            Near(day.Asr, "18:38", london, "London Asr"); Near(day.Maghrib, "21:19", london, "London Maghrib"); Near(day.Isha, "23:25", london, "London Isha");
        });
        check("Prayer methods: Umm al-Qura's Ramadan Isha, Tehran's Maghrib and every method gives five times", () => {
            var ramadan = PrayerService.Calculate(new DateTime(2026, 3, 1), 21.4225, 39.8262, "Makkah", false);
            var other = PrayerService.Calculate(new DateTime(2026, 11, 1), 21.4225, 39.8262, "Makkah", false);
            Assert((ramadan.Isha - ramadan.Maghrib).TotalMinutes == 120 && (other.Isha - other.Maghrib).TotalMinutes == 90, "Umm al-Qura's Isha isn't 90 minutes, or 120 in Ramadan.");
            var tehran = PrayerService.Calculate(new DateTime(2026, 11, 1), 35.6892, 51.389, "Tehran", false);
            Assert(tehran.Maghrib > tehran.Sunset && (tehran.Maghrib - tehran.Sunset).TotalMinutes < 30, "Tehran's Maghrib isn't after sunset.");
            foreach (var method in PrayerService.Methods)
                foreach (var place in new[] { new[] { 24.7, 46.7 }, new[] { 59.9, 10.75 }, new[] { -33.9, 151.2 }, new[] { 64.1, -21.9 } })
                {
                    var times = PrayerService.Calculate(new DateTime(2026, 6, 21), place[0], place[1], method.Id, false);
                    var all = new[] { times.Fajr, times.Sunrise, times.Dhuhr, times.Asr, times.Maghrib, times.Isha };
                    Assert(all.All(t => t != DateTime.MinValue) && all.Zip(all.Skip(1), (a, b) => a <= b).All(x => x), method.Id + " at " + place[0] + " gives missing or unordered times.");
                }
            Assert(PrayerService.KnownMethod(PrayerService.RegionMethod()) && PrayerService.Method("Nope").Id == "MWL", "Region method or fallback is wrong.");
        });
        check("Prayer times count down to the next prayer, across midnight too", () => {
            var schedule = new PrayerSchedule("Riyadh", 24.7136, 46.6753, "Makkah", false);
            var riyadh = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
            // 15:00 in Riyadh: Asr began at 14:50, Maghrib is next.
            var next = schedule.Next(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), riyadh);
            Assert(next.Index == 3 && next.CurrentIndex == 2 && next.JustStarted, "Maghrib isn't next after Asr.");
            // 23:00 in Riyadh: tomorrow's Fajr is next and nothing just started.
            next = schedule.Next(new DateTime(2026, 11, 1, 20, 0, 0, DateTimeKind.Utc), riyadh);
            Assert(next.Index == 0 && next.At > new DateTime(2026, 11, 2, 0, 0, 0, DateTimeKind.Utc) && next.CurrentIndex == 4 && !next.JustStarted, "Tomorrow's Fajr isn't next after Isha.");
            Assert(PrayerService.Countdown(TimeSpan.FromMinutes(72.2)) == "in 1 h 13 m" && PrayerService.Countdown(TimeSpan.FromMinutes(12)) == "in 12 m" && PrayerService.Countdown(TimeSpan.FromSeconds(20)) == "in under a minute", "Countdowns are worded wrongly.");
            Assert(PrayerService.Time(new DateTime(2026, 11, 1, 14, 50, 0), false) == "2:50 PM" && PrayerService.Time(new DateTime(2026, 11, 1, 14, 50, 0), true) == "14:50", "Times are formatted wrongly.");
            Assert(PrayerService.Hijri(new DateTime(2026, 11, 1)) == "21 Jumada al-Awwal 1448", "The Hijri date is wrong: " + PrayerService.Hijri(new DateTime(2026, 11, 1)));
        });
        check("Prayer settings follow the weather city or their own, and are saved", () => {
            var settings = new AppSettings();
            Assert(PrayerSchedule.For(settings) == null && !settings.PrayerAlert && settings.PrayerSound == "" && PrayerService.KnownMethod(settings.PrayerMethod), "Prayer defaults are wrong.");
            settings.WeatherLocation = "London"; settings.Latitude = 51.5; settings.Longitude = -0.12;
            Assert(PrayerSchedule.For(settings).City == "London", "The weather city isn't followed.");
            settings.PrayerLocation = "Riyadh, Saudi Arabia"; settings.PrayerLatitude = 24.7; settings.PrayerLongitude = 46.7; settings.PrayerMethod = "Makkah"; settings.PrayerHanafi = true; settings.PrayerAlert = true; settings.PrayerSound = @"C:\Sounds\adhan.mp3";
            Assert(PrayerSchedule.For(settings).City == "Riyadh, Saudi Arabia" && PrayerSchedule.For(settings).Hanafi, "The prayer city isn't used.");
            string path = Path.Combine(directory, "prayer-settings.xml"), warning;
            SettingsStore.Save(path, settings);
            var loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.PrayerLocation == "Riyadh, Saudi Arabia" && loaded.PrayerMethod == "Makkah" && loaded.PrayerHanafi && loaded.PrayerAlert && loaded.PrayerSound == @"C:\Sounds\adhan.mp3", "Prayer settings weren't saved.");
            File.WriteAllText(path, "<?xml version=\"1.0\"?><AppSettings><PrayerMethod>Moon</PrayerMethod><PrayerLocation>Nowhere</PrayerLocation><PrayerLatitude>123</PrayerLatitude></AppSettings>");
            loaded = SettingsStore.Load(path, out warning);
            Assert(PrayerService.KnownMethod(loaded.PrayerMethod) && loaded.PrayerLocation == "", "Bad prayer settings weren't repaired.");
            Assert(!PrayerAlert.Usable(@"C:\nothing-here\adhan.mp3") && !PrayerAlert.Usable(Path.Combine(directory, "prayer-settings.xml")), "A missing or non-audio file counts as usable.");
        });
        check("Prayer alert chime is a short built-in WAV, not a bundled recording", () => {
            var wav = PrayerAlert.Chime();
            Assert(wav.Length > 44 && wav[0] == 'R' && wav[8] == 'W' && BitConverter.ToInt32(wav, 24) == 22050 && BitConverter.ToInt32(wav, 40) == wav.Length - 44 && wav.Length < 100000, "The chime isn't a valid short WAV.");
        });
        check("Prayer times start hidden and older layouts get a hidden placement", () => {
            var settings = new AppSettings();
            Assert(WidgetLayout.Ids.Contains("Prayer") && WidgetLayout.Title("Prayer") == "Prayer times" && !WidgetLayout.SpotifyDefault(settings).First(p => p.Id == "Prayer").Visible, "Prayer times are visible by default.");
            Assert(WidgetLayout.Normalize(WidgetLayout.Default(settings).Where(p => p.Id != "Prayer").ToArray(), settings).Any(p => p.Id == "Prayer" && !p.Visible), "Older layouts didn't get a hidden placement.");
            foreach (var canvas in new[] { new Size(1856, 576), new Size(1200, 640), new Size(656, 1712) })
                Assert(!WidgetLayout.DefaultFor(settings, canvas).First(p => p.Id == "Prayer").Visible, "Prayer times show by default on " + canvas + ".");
            Assert(CardSizing.Prayer(240, 152) == CardSizing.Design.Small && CardSizing.Prayer(600, 219) == CardSizing.Design.Small && CardSizing.Prayer(472, 250) == CardSizing.Design.Medium && CardSizing.Prayer(439, 576) == CardSizing.Design.Medium && CardSizing.Prayer(440, 360) == CardSizing.Design.Large, "Prayer designs are picked at the wrong sizes.");
            Assert(CardSizing.PrayerTilesAcross(860) && !CardSizing.PrayerTilesAcross(859) && CardSizing.PrayerNextColumn(424) == 195 && CardSizing.PrayerTime(195, 166) == 47 && CardSizing.PrayerLargeTop(492, false) == 187 && CardSizing.PrayerTileTime(120, false) == 25, "Prayer sizes are wrong.");
        });
        check("Prayer designs keep every time inside the card, and Settings → Prayer shows the saved choices", () => {
            foreach (bool use24 in new[] { false, true })
                foreach (var size in new[] { new Size(240, 152), new Size(320, 400), new Size(340, 220), new Size(472, 250), new Size(440, 360), new Size(472, 576), new Size(1200, 420), new Size(1856, 576) })
                {
                    string path = Path.Combine(directory, "prayer-" + size.Width + "x" + size.Height + ".xml");
                    var settings = new AppSettings { TextScale = 1.4, Use24HourClock = use24, PrayerLocation = "Riyadh, Riyadh Region, Saudi Arabia", PrayerLatitude = 24.7136, PrayerLongitude = 46.6753, PrayerMethod = "Makkah", PrayerSound = @"C:\Sounds\adhan.mp3" };
                    settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Prayer"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                    SettingsStore.Save(path, settings);
                    using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                    {
                        var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                        try
                        {
                            using (var controller = new DashboardController(window, path, true))
                            {
                                controller.PrayerView.Render(controller.Prayer, DateTime.UtcNow, false);
                                var dashboard = (FrameworkElement)window.FindName("Dashboard");
                                dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                                var card = (FrameworkElement)window.FindName("PrayerCard");
                                var next = (System.Windows.Controls.TextBlock)window.FindName("PrayerNextTime");
                                Assert(card.Visibility == Visibility.Visible && next != null && next.Text.Contains(":") && controller.PrayerView.Current == CardSizing.Prayer(size.Width, size.Height), "Prayer times built the wrong design at " + size + ".");
                                foreach (var part in Descendants(card).OfType<System.Windows.Controls.TextBlock>().Where(e => Shown(e, card) && e.ActualWidth > 0 && e.Text.Length > 0))
                                {
                                    var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                    Assert(box.Left >= 23.5 && box.Top >= 23.5 && box.Right <= card.ActualWidth - 23.5 && box.Bottom <= card.ActualHeight - 23.5, "Prayer times push \"" + part.Text + "\" out of the " + size + " card" + (use24 ? " (24 h)" : "") + ": " + box);
                                    if (part.Text.Contains(":") && part.Text.Length <= 8)
                                    {
                                        var full = new System.Windows.Controls.TextBlock { Text = part.Text, FontSize = part.FontSize, FontWeight = part.FontWeight, FontFamily = part.FontFamily };
                                        System.Windows.Documents.Typography.SetNumeralAlignment(full, System.Windows.Documents.Typography.GetNumeralAlignment(part));
                                        System.Windows.Media.TextOptions.SetTextFormattingMode(full, System.Windows.Media.TextOptions.GetTextFormattingMode(part));
                                        full.Measure(new Size(Double.PositiveInfinity, Double.PositiveInfinity));
                                        Assert(full.DesiredSize.Width <= part.ActualWidth + 1.5, "The time \"" + part.Text + "\" is cut at " + size + (use24 ? " (24 h)" : "") + ".");
                                    }
                                }
                                if (size.Width == 472 && size.Height == 250 && !use24)
                                {
                                    ((System.Windows.Controls.Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    Assert(((PrayerMethod)((System.Windows.Controls.ComboBox)window.FindName("PrayerMethodChoice")).SelectedItem).Id == "Makkah" && ((System.Windows.Controls.TextBlock)window.FindName("PrayerCityNote")).Text.StartsWith("Riyadh") && ((System.Windows.Controls.TextBox)window.FindName("PrayerSoundPath")).Text == @"C:\Sounds\adhan.mp3", "Settings → Prayer doesn't show the saved choices.");
                                    ((System.Windows.Controls.Button)window.FindName("PrayerUseWeather")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    ((System.Windows.Controls.Button)window.FindName("PrayerSoundDefault")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    ((System.Windows.Controls.ComboBox)window.FindName("PrayerAsrChoice")).SelectedIndex = 1;
                                    ((System.Windows.Controls.Button)window.FindName("SaveSettings")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    string warning; var saved = SettingsStore.Load(path, out warning);
                                    Assert(saved.PrayerLocation == "" && saved.PrayerSound == "" && saved.PrayerHanafi && controller.Prayer == null, "Settings → Prayer didn't save.");
                                    Assert(((System.Windows.Controls.TextBlock)window.FindName("PrayerStatus")).Text == "", "The card still names a city without one.");
                                }
                            }
                        }
                        finally { window.Close(); }
                    }
                }
        });
    }
}
