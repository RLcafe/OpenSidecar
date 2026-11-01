using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WidgetScreen;

// Sticker Sheet phase 3, the readings (docs/design/sticker-sheet.md): System stats' model names, peak sticker,
// temperature badges and min/max lines; Weather's sky band, glyph sticker, chips, curve and sun arc; Storage, World
// clocks and Prayer as they land. The phone's checks (android/tests) assert the same values, so both apps read alike.
internal static class ReadingsTests
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
    // One widget alone on the reference canvas at the given size.
    private static AppSettings Alone(string id, Size size, double scale)
    {
        var settings = new AppSettings { TextScale = scale };
        settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == id; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
        return settings;
    }
    private static void Inside(FrameworkElement part, FrameworkElement card, string what, double edge = 19.5)
    {
        var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
        Assert(box.Left >= edge && box.Top >= edge && box.Right <= card.ActualWidth - edge && box.Bottom <= card.ActualHeight - edge, what + " leaves its card: " + box + " in " + card.ActualWidth + " × " + card.ActualHeight);
    }
    private static T Field<T>(object owner, string name) { return (T)owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(owner); }

    // An Open-Meteo answer with every part phase 3 reads: London on 1 Nov 2026 at 21:00 (UTC+0), overcast, 82% humidity.
    public const string WeatherSample = "{\"utc_offset_seconds\":0,\"current\":{\"temperature_2m\":10.2,\"apparent_temperature\":8.1,\"wind_speed_10m\":14,\"weather_code\":3,\"relative_humidity_2m\":82},"
        + "\"daily\":{\"temperature_2m_max\":[11.4],\"temperature_2m_min\":[8.6],\"uv_index_max\":[1.2],\"sunrise\":[\"2026-11-01T06:53\"],\"sunset\":[\"2026-11-01T16:33\"]},"
        + "\"hourly\":{\"time\":[\"2026-11-01T20:00\",\"2026-11-01T21:00\",\"2026-11-01T22:00\",\"2026-11-01T23:00\",\"2026-11-02T00:00\",\"2026-11-02T01:00\",\"2026-11-02T02:00\",\"2026-11-02T03:00\",\"2026-11-02T04:00\",\"2026-11-02T05:00\",\"2026-11-02T06:00\",\"2026-11-02T07:00\",\"2026-11-02T08:00\",\"2026-11-02T09:00\",\"2026-11-02T10:00\"],"
        + "\"temperature_2m\":[10.6,10.2,10,10.4,11,11.3,11,10.5,10,9.6,9.2,9,9.4,10.1,10.8],\"weather_code\":[3,3,3,61,61,61,3,3,2,2,1,0,0,0,1],\"precipitation_probability\":[5,10,20,45,60,40,20,10,5,0,0,0,0,0,null]}}";

    public static void Run(string directory, Action<string, Action> check)
    {
        check("System stats: processor and graphics card names shorten to what a person calls them", () => {
            Assert(SystemService.ShortModel("AMD Ryzen 7 7800X3D 8-Core Processor") == "Ryzen 7 7800X3D", "Ryzen: " + SystemService.ShortModel("AMD Ryzen 7 7800X3D 8-Core Processor"));
            Assert(SystemService.ShortModel("Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz") == "i7-12700K", "Intel: " + SystemService.ShortModel("Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz"));
            Assert(SystemService.ShortModel("NVIDIA GeForce RTX 4070") == "RTX 4070", "NVIDIA");
            Assert(SystemService.ShortModel("AMD Radeon RX 7900 XTX") == "Radeon RX 7900 XTX", "Radeon");
            Assert(SystemService.ShortModel("AMD Ryzen 5 5600G with Radeon Graphics") == "Ryzen 5 5600G", "APU");
            Assert(SystemService.ShortModel(null) == "" && SystemService.ShortModel("  ") == "", "empty");
        });
        check("System stats: the peak sticker names the busiest shown reading and fills from 80%", () => {
            var s = new SystemSnapshot { Cpu = 41, CpuAvailable = true, Gpu = 92, GpuName = "NVIDIA GeForce RTX 4070", CpuName = "AMD Ryzen 7 7700 8-Core Processor", MemoryAvailable = true, MemoryPercent = 55, UsedMemoryGB = 17.6, TotalMemoryGB = 32 };
            var readings = SystemReadings.From(s, false, 60);
            Assert(readings["Cpu"].Detail == "Ryzen 7 7700" && readings["Gpu"].Detail == "RTX 4070", "details " + readings["Cpu"].Detail + " / " + readings["Gpu"].Detail);
            string peak = SystemReadings.PeakId(readings, id => true);
            Assert(peak == "Gpu" && SystemReadings.PeakText(peak, readings[peak]) == "GPU 92%" && readings[peak].Percent >= SystemReadings.Strained, "peak " + peak);
            Assert(SystemReadings.PeakId(readings, id => id != "Gpu") == "Ram", "peak without GPU");
            Assert(SystemReadings.PeakId(readings, id => false) == null && SystemReadings.PeakText(null, null) == "", "no peak");
            var noGpu = SystemReadings.From(new SystemSnapshot { Cpu = 12, CpuAvailable = true, GpuStatus = "No GPU counters" }, false, 60);
            Assert(noGpu["Gpu"].Detail == "No GPU counters", "GPU status kept without a reading");
        });
        check("System stats: temperature badges and min/max lines read the same on both apps", () => {
            var hot = new SystemSnapshot { CpuTemperature = 58.4, CpuTemperatureState = "Working", GpuTemperature = 71, GpuTemperatureState = "Working" };
            var readings = SystemReadings.From(hot, false, 60);
            Assert(SystemReadings.Badge(readings["CpuTemp"]) == "58°" && SystemReadings.Badge(SystemReadings.From(hot, true, 60)["GpuTemp"]) == "160°", "badges");
            Assert(SystemReadings.Badge(SystemReadings.From(new SystemSnapshot { CpuTemperatureState = "NeedsPawnIO" }, false, 60)["CpuTemp"]) == "", "no badge without a reading");
            Assert(SystemReadings.Range("Cpu", new List<double> { 12.2, 87.4, 40 }, false) == "min 12% · max 87%", "percent range");
            Assert(SystemReadings.Range("CpuTemp", new List<double> { 41, 42.4 }, true) == "min 106° · max 108°", "temperature range " + SystemReadings.Range("CpuTemp", new List<double> { 41, 42.4 }, true));
            Assert(SystemReadings.Range("Fps", new List<double> { 58.6, 144 }, false) == "min 59 · max 144", "fps range");
            Assert(SystemReadings.Range("Ram", new List<double> { 50 }, false) == "" && SystemReadings.Range("Ram", null, false) == "", "too few readings");
        });

        check("Weather: one Open-Meteo request brings humidity, rain chances, UV, sunrise and sunset", () => {
            string url = WeatherService.ForecastUrl(51.5, -0.12, false);
            foreach (string part in new[] { "relative_humidity_2m", "precipitation_probability", "uv_index_max", "sunrise", "sunset" }) Assert(url.Contains(part), "The request lacks " + part + ".");
            var s = WeatherService.Parse(WeatherSample, new DateTime(2026, 11, 1, 21, 10, 0));
            Assert(s.Humidity == 82 && Math.Abs(s.Uv.Value - 1.2) < 0.001 && s.Sunrise == new DateTime(2026, 11, 1, 6, 53, 0) && s.Sunset == new DateTime(2026, 11, 1, 16, 33, 0) && s.UtcOffset == TimeSpan.Zero, "Phase 3 fields parse wrongly.");
            Assert(s.Curve.Count == 13 && s.Curve[0].Time.Hour == 21 && s.Curve[0].Rain == 10 && s.Curve[12].Rain == 0 && s.Hours.Count == 5, "The 12-hour curve starts or ends wrongly: " + s.Curve.Count);
            var none = WeatherService.Parse("{\"current\":{\"temperature_2m\":20,\"apparent_temperature\":19,\"wind_speed_10m\":8,\"weather_code\":0}}");
            Assert(!none.Humidity.HasValue && !none.Uv.HasValue && !none.Sunrise.HasValue && none.Curve.Count == 0, "Missing extras became readings.");
        });
        check("Weather: chips, UV words, sky inks, the sun's place and the curve read the same on both apps", () => {
            var s = WeatherService.Parse(WeatherSample, new DateTime(2026, 11, 1, 21, 10, 0));
            var three = WeatherService.Chips(s, false, 3);
            Assert(string.Join("|", three.Select(c => c[0] + "=" + c[1])) == "FEELS=8°|HUMIDITY=82%|RAIN=10%", "Medium chips: " + string.Join("|", three.Select(c => c[0] + "=" + c[1])));
            Assert(string.Join("|", WeatherService.Chips(s, true, 6).Select(c => c[0])) == "FEELS|HUMIDITY|RAIN|UV|WIND|HIGH / LOW" && WeatherService.Chips(s, true, 6)[4][1] == "14 mph", "Large chips.");
            Assert(WeatherService.UvText(1.2) == "1 Low" && WeatherService.UvText(5.4) == "5 Moderate" && WeatherService.UvText(7) == "7 High" && WeatherService.UvText(9.6) == "10 Very high" && WeatherService.UvText(11) == "11 Extreme", "UV words.");
            Assert(WeatherService.SkyInk("sun", true) == "Lemon" && WeatherService.SkyInk("rain", true) == "Glacier" && WeatherService.SkyInk("storm", true) == "Lavender" && WeatherService.SkyInk("sun", false) == "Lavender", "Sky inks.");
            Assert(WeatherService.SkyStrength("sun", true) == 0.42 && WeatherService.SkyStrength("cloud", true) == 0.22 && WeatherService.SkyStrength("sun", false) == 0.32, "Sky strengths.");
            var rise = new DateTime(2026, 11, 1, 6, 0, 0); var set = new DateTime(2026, 11, 1, 18, 0, 0);
            Assert(WeatherService.SunPart(new DateTime(2026, 11, 1, 12, 0, 0), rise, set) == 0.5 && WeatherService.SunPart(new DateTime(2026, 11, 1, 3, 0, 0), rise, set) < 0 && WeatherService.SunPart(rise, null, set) == null, "Sun part.");
            Assert(WeatherService.IsDay(new DateTime(2026, 11, 1, 12, 0, 0), rise, set) && !WeatherService.IsDay(new DateTime(2026, 11, 1, 21, 0, 0), rise, set) && WeatherService.IsDay(new DateTime(2026, 11, 1, 8, 0, 0), null, null) && !WeatherService.IsDay(new DateTime(2026, 11, 1, 20, 0, 0), null, null), "Day and night.");
            Assert(WeatherService.CityNow(s, new DateTime(2026, 11, 1, 21, 0, 0, DateTimeKind.Utc)) == new DateTime(2026, 11, 1, 21, 0, 0), "The city's clock.");
            Assert(WeatherService.CurveLevels(new[] { 10.0, 12, 14 }).SequenceEqual(new[] { 0, 0.5, 1 }) && WeatherService.CurveLevels(new[] { 10.0, 10.2 }).All(v => v == 0.5) && WeatherService.CurveLevels(new double[0]).Length == 0, "Curve levels.");
            Assert(WeatherService.ClockTime(new DateTime(2026, 11, 1, 6, 53, 0), false) == "6:53 AM" && WeatherService.ClockTime(new DateTime(2026, 11, 1, 16, 33, 0), true) == "16:33", "Clock times.");
            Assert(CardSizing.WeatherChips(472) == 3 && CardSizing.WeatherChips(560) == 4 && CardSizing.WeatherChips(900) == 6, "Chip counts.");
        });
        check("Weather: the glyph sticker, sky band, chips, curve and sun arc fit their card at every design at 100% and 140%", () => {
            var sample = WeatherService.Parse(WeatherSample, new DateTime(2026, 11, 1, 21, 10, 0));
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (var size in new[] { new Size(300, 200), new Size(472, 250), new Size(472, 320), new Size(400, 340), new Size(560, 576), new Size(900, 420) })
                {
                    string path = Path.Combine(directory, "weather-readings.xml"); SettingsStore.Save(path, Alone("Weather", size, scale));
                    var window = Window();
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            Lay(window);
                            var weather = Field<WeatherCard>(controller, "weatherCard");
                            weather.Configure("Testville", false, false); weather.Reading(sample);
                            Lay(window);
                            var card = (FrameworkElement)window.FindName("WeatherCard"); var design = CardSizing.Weather(size.Width, size.Height);
                            Assert(window.FindName("WeatherSky") is System.Windows.Shapes.Rectangle && Descendants(card).OfType<StickerShape>().Any(), "No sky band or glyph sticker at " + size + ".");
                            var curve = Descendants(card).OfType<TemperatureCurve>().FirstOrDefault(); var arc = Descendants(card).OfType<SunArc>().FirstOrDefault();
                            bool wantCurve = design == CardSizing.Design.Large || (design == CardSizing.Design.Medium && size.Height >= 300);
                            Assert((curve != null && Shown(curve, card) && curve.Points == 13) == wantCurve, "The curve shows on the wrong design at " + size + ".");
                            Assert((arc != null && Shown(arc, card)) == (design == CardSizing.Design.Large && size.Width - 48 >= 380), "The sun arc shows on the wrong design at " + size + ".");
                            if (design != CardSizing.Design.Small)
                            {
                                var chips = (System.Windows.Controls.Primitives.UniformGrid)window.FindName("WeatherChips");
                                Assert(chips.Children.Count == (design == CardSizing.Design.Large ? CardSizing.WeatherChips(size.Width) : 3), "Wrong chip count at " + size + ".");
                            }
                            foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is TextBlock || e is TemperatureCurve || e is SunArc || e is StickerShape)))
                                Inside(part, card, "Weather " + part.GetType().Name + " at " + size + " and " + scale * 100 + "%");
                            weather.Failed(); Lay(window);
                            Assert(((TextBlock)window.FindName("WeatherTemperature")).Text == "10°", "Offline lost the last reading.");
                            if (design != CardSizing.Design.Small) Assert(((TextBlock)window.FindName("WeatherCondition")).Text == "OFFLINE", "Offline isn't stamped.");
                        }
                    }
                    finally { window.Close(); }
                }
        });
        check("Storage: jars fill by used space and a drive under the threshold fills with caution tape, inside the card at 100% and 140%", () => {
            Assert(CardSizing.JarWidth(100) == 72 && CardSizing.JarLevel(0) == 0 && CardSizing.JarLevel(1) == 0.03 && CardSizing.JarLevel(85) == 0.85 && CardSizing.JarLevel(120) == 1 && CardSizing.JarLevel(Double.NaN) == 0, "Jar sizes or levels.");
            var drives = new List<StorageDrive> {
                new StorageDrive { Name = "C:", Label = "Windows", TotalBytes = 1000L << 30, FreeBytes = 60L << 30 },
                new StorageDrive { Name = "D:", Label = "Games", TotalBytes = 2000L << 30, FreeBytes = 900L << 30 } };
            foreach (double scale in new[] { 1.0, 1.4 })
                foreach (var size in new[] { new Size(300, 180), new Size(456, 250), new Size(624, 360) })
                {
                    string path = Path.Combine(directory, "storage-readings.xml"); SettingsStore.Save(path, Alone("Storage", size, scale));
                    var window = Window();
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            Lay(window);
                            controller.StorageView.Configure(10); controller.StorageView.Update(drives);
                            Lay(window);
                            var card = (FrameworkElement)window.FindName("StorageCard"); var design = CardSizing.Storage(size.Width, size.Height);
                            var jars = Descendants(card).OfType<StorageJar>().Where(j => Shown(j, card)).ToList();
                            Assert(design == CardSizing.Design.Small ? jars.Count == 0 : jars.Count == 2 && jars[0].Low && !jars[1].Low && Math.Abs(jars[0].Used - 94) < 0.01, "Jars wrong on " + design + " at " + size + ": " + jars.Count);
                            var bars = Descendants(card).OfType<MeterBar>().Where(b => Shown(b, card)).ToList();
                            if (design == CardSizing.Design.Small) Assert(bars.Count == 2 && bars[0].Fill == Stickers.Caution && bars[1].Fill != Stickers.Caution, "The low drive's bar isn't caution tape.");
                            Assert(((TextBlock)window.FindName("StorageStatus")).Text == "C: LOW", "The heading doesn't name the low drive.");
                            foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is TextBlock || e is StorageJar || e is MeterBar)))
                                Inside(part, card, "Storage " + part.GetType().Name + " at " + size + " and " + scale * 100 + "%");
                        }
                    }
                    finally { window.Close(); }
                }
        });
    }
}