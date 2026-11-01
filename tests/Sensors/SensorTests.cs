using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using WidgetScreen;

// Hardware sensors (decision 016; docs/design/system-sensors.md): the pinned LibreHardwareMonitor files and their checks,
// what each reading says when it can't be had, graph styles, RTSS frame rates from a labelled fixture, the frame counter,
// the full-sensors task definition, and the System stats designs with all nine readings. Nothing here elevates, installs
// a task or a driver, or reads anything but this PC's own sensors.
internal static class SensorTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateFileMapping(IntPtr file, IntPtr security, uint protect, uint high, uint low, string name);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint high, uint low, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(IntPtr view);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

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

    // A labelled fixture: every reading present, or the states a PC without the sensors gives.
    public static SystemSnapshot Fixture(bool full)
    {
        var s = new SystemSnapshot { Cpu = 34, CpuAvailable = true, Gpu = 91, GpuStatus = "Busiest engine", MemoryAvailable = true, MemoryPercent = 52, UsedMemoryGB = 16.1, TotalMemoryGB = 31.1, NetworkAvailable = true, DownloadBytes = 2400000, UploadBytes = 180000, Uptime = new TimeSpan(3, 4, 12, 0) };
        s.GpuName = "TEST FIXTURE GPU"; s.GpuTemperature = 71; s.GpuTemperatureState = "Working"; s.VramUsedGB = 6.2; s.VramTotalGB = 12; s.VramState = "Working";
        if (full) { s.CpuTemperature = 88; s.CpuTemperatureState = "Working"; s.Fps = 143.6; s.FpsState = "Working"; s.FpsSource = "RTSS"; s.FpsApp = "FixtureGame"; }
        else { s.CpuTemperatureState = "NeedsFullSensors"; s.FpsState = "NeedsFullSensors"; }
        return s;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        string projectRoot = Path.GetFullPath(Path.Combine(directory, "..", ".."));
        string bin = Path.Combine(projectRoot, "third-party", "LibreHardwareMonitor", "bin");

        check("Hardware sensors: the pinned hashes match SHA256SUMS.txt, and missing or changed files are never loaded", () => {
            var sums = File.ReadAllLines(Path.Combine(projectRoot, "third-party", "LibreHardwareMonitor", "SHA256SUMS.txt")).Select(l => l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length == 2 && p[1].EndsWith(".dll")).ToDictionary(p => p[1], p => p[0]);
            Assert(sums.Count == SensorPackage.Files.Length && SensorPackage.Files.All(f => sums.ContainsKey(f.Key) && sums[f.Key] == f.Value), "SensorPackage.Files and SHA256SUMS.txt differ.");
            string empty = Path.Combine(directory, "sensors-missing"); if (Directory.Exists(empty)) Directory.Delete(empty, true);
            string damaged;
            Assert(SensorPackage.Check(empty, out damaged) == SensorPackage.State.Missing, "A missing folder isn't Missing.");
            string swapped = Path.Combine(directory, "sensors-swapped"); Directory.CreateDirectory(swapped);
            foreach (var file in SensorPackage.Files) File.WriteAllText(Path.Combine(swapped, file.Key), "TEST FIXTURE: not the real " + file.Key);
            Assert(SensorPackage.Check(swapped, out damaged) == SensorPackage.State.Damaged && damaged == "LibreHardwareMonitorLib.dll", "Changed files aren't Damaged.");
            using (var sensors = new HardwareSensors(swapped))
                Assert(sensors.Read(false, true) == null && sensors.Problem.Contains("doesn't match"), "Changed files were loaded, or the reason is missing: " + sensors.Problem);
        });
        if (File.Exists(Path.Combine(bin, "LibreHardwareMonitorLib.dll")))
            check("Hardware sensors: the real LibreHardwareMonitor 0.9.6 loads from checked bytes and reads this PC's GPU (read only)", () => {
                string damaged;
                Assert(SensorPackage.Check(bin, out damaged) == SensorPackage.State.Installed, "The fetched files don't match: " + damaged);
                using (var sensors = new HardwareSensors(bin))
                {
                    var reading = sensors.Read(false, true);
                    Assert(reading != null && sensors.Problem == null, "The library didn't open: " + sensors.Problem);
                    Assert(!reading.GpuTemperature.HasValue || (reading.GpuTemperature > 0 && reading.GpuTemperature < 120), "A GPU temperature out of range: " + reading.GpuTemperature);
                }
                using (var service = new SystemService(bin))
                {
                    var s = service.Read(new SystemWants { CpuTemperature = true, Fps = true });
                    // Not elevated (the checks never are): CPU temperature says it needs full-sensors mode, never a number.
                    if (!HardwareSensors.Elevated) Assert(!s.CpuTemperature.HasValue && s.CpuTemperatureState == "NeedsFullSensors", "CPU temperature without admin rights: " + s.CpuTemperatureState);
                    Assert(s.FpsState == "Working" || s.FpsState == "NoGame" || s.FpsState == "NeedsFullSensors", "An unknown frame-rate state: " + s.FpsState);
                }
            });
        check("Hardware sensors: GPU temperature and video memory come from Windows without the library", () => {
            var adapters = GpuTelemetry.Adapters(true);
            Assert(adapters.All(a => a.DedicatedBytes > 0 && (!a.Temperature.HasValue || (a.Temperature > 0 && a.Temperature < 150))), "An adapter reading is out of range.");
            Assert(GpuTelemetry.LuidName(0x0000000100017B1AL) == "luid_0x00000001_0x00017B1A", "The counter instance name is wrong: " + GpuTelemetry.LuidName(0x0000000100017B1AL));
            var main = GpuTelemetry.MainAdapter(new List<GpuTelemetry.Adapter> { new GpuTelemetry.Adapter { Name = "", DedicatedBytes = 12 }, new GpuTelemetry.Adapter { Name = "Fixture discrete", DedicatedBytes = 12 }, new GpuTelemetry.Adapter { Name = "Fixture integrated", DedicatedBytes = 1 } });
            Assert(main.Name == "Fixture discrete", "The main adapter isn't the named one with the most memory.");
            using (var service = new SystemService(Path.Combine(directory, "sensors-missing")))
            {
                var s = service.Read(new SystemWants { CpuTemperature = true, GpuTemperature = true, Vram = true });
                Assert(!s.CpuTemperature.HasValue && s.CpuTemperatureState == "NotInstalled", "Without the files CPU temperature should say they aren't installed: " + s.CpuTemperatureState);
                Assert(s.GpuTemperatureState == (s.GpuTemperature.HasValue ? "Working" : "Unavailable") && s.VramState == (s.VramUsedGB.HasValue ? "Working" : "Unavailable"), "GPU states don't match their readings.");
            }
        });
        check("Hardware sensors: missing readings say why, temperatures show their unit, and styles parse", () => {
            var bare = SystemReadings.From(Fixture(false), false, 60);
            Assert(bare["CpuTemp"].Text == "—" && bare["CpuTemp"].Short == "Needs admin" && bare["CpuTemp"].Long == "Needs admin: full-sensors mode" && bare["Fps"].Long == "Needs full-sensors mode or RivaTuner", "Missing readings don't say why.");
            Assert(bare["GpuTemp"].Text == "71°C" && bare["Vram"].Text == "52%" && bare["Vram"].Detail == "6.2 / 12 GB" && bare["Gpu"].Warning && !bare["Cpu"].Warning, "Readings are worded wrong.");
            var full = SystemReadings.From(Fixture(true), true, 144);
            Assert(full["CpuTemp"].Text == "190°F" && full["CpuTemp"].Warning && full["Fps"].Text == "144" && full["Fps"].Detail == "FixtureGame  ·  RTSS" && Math.Abs(full["Fps"].Percent - 99.7) < 0.1, "Full readings are worded wrong: " + full["CpuTemp"].Text + " " + full["Fps"].Detail);
            Assert(SystemReadings.FpsScale(new double[] { 58 }) == 60 && SystemReadings.FpsScale(new double[] { 130 }) == 144 && SystemReadings.FpsScale(new double[] { 400 }) == 420, "The frame-rate scale is wrong.");
            var styles = SystemStyles.Parse("Cpu=Bars;Uptime=Ring;Network=Ring;Fps=Gauge;Nope=Line;Gpu=Auto");
            Assert(styles.Count == 2 && styles["Cpu"] == "Bars" && styles["Fps"] == "Gauge" && SystemStyles.Write(styles) == "Cpu=Bars;Fps=Gauge", "Styles don't parse: " + SystemStyles.Write(styles));
            Assert(SystemStyles.Resolve("Auto", "Cpu", CardSizing.Design.Medium) == "Ring" && SystemStyles.Resolve("Auto", "GpuTemp", CardSizing.Design.Medium) == "Gauge" && SystemStyles.Resolve("Auto", "Fps", CardSizing.Design.Medium) == "Sparkline" && SystemStyles.Resolve("Auto", "Cpu", CardSizing.Design.Large) == "Line" && SystemStyles.Resolve("Bars", "Cpu", CardSizing.Design.Large) == "Bars", "Auto resolves wrongly.");
            var settings = new AppSettings();
            Assert(SystemStyles.ShownIds(settings).Length == 9, "New settings don't show all nine readings.");
            foreach (string id in SystemStyles.Ids) SystemStyles.Show(settings, id, false);
            Assert(!SystemStyles.Any(settings), "Hiding every reading left one.");
        });
        check("Hardware sensors: RTSS frame rates for the game in front (labelled fixture memory)", () => {
            string name = "WidgetScreen-TEST-FIXTURE-RTSS-" + Guid.NewGuid().ToString("N");
            double? fps;
            Assert(!RtssReader.Read(1234, out fps, name) && !fps.HasValue, "A missing RTSS reads as running.");
            IntPtr mapping = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, 4, 0, 4096, name);
            IntPtr view = MapViewOfFile(mapping, 2, 0, 0, UIntPtr.Zero);
            try
            {
                Marshal.WriteInt32(view, 0, unchecked((int)RtssReader.Signature)); Marshal.WriteInt32(view, 4, 0x00020005);
                Marshal.WriteInt32(view, 8, 1024); Marshal.WriteInt32(view, 12, 256); Marshal.WriteInt32(view, 16, 2);
                // Two apps: the one in front has drawn 144 frames in one second.
                Marshal.WriteInt32(view, 256, 999); Marshal.WriteInt32(view, 256 + 268, 0); Marshal.WriteInt32(view, 256 + 272, 1000); Marshal.WriteInt32(view, 256 + 276, 30);
                Marshal.WriteInt32(view, 1280, 1234); Marshal.WriteInt32(view, 1280 + 268, 5000); Marshal.WriteInt32(view, 1280 + 272, 6000); Marshal.WriteInt32(view, 1280 + 276, 144);
                Assert(RtssReader.Read(1234, out fps, name) && fps == 144, "The fixture's frame rate is wrong: " + fps);
                Assert(RtssReader.Read(4321, out fps, name) && !fps.HasValue, "An app RTSS doesn't track got a frame rate.");
                Marshal.WriteInt32(view, 0, 0);
                Assert(!RtssReader.Read(1234, out fps, name), "A mapping without RTSS's signature was trusted.");
            }
            finally { UnmapViewOfFile(view); CloseHandle(mapping); }
        });
        check("Hardware sensors: the frame counter counts only DirectX Present events of the game in front", () => {
            using (var counter = new PresentCounter())
            {
                var start = DateTime.UtcNow; counter.Rate(1, start);
                for (int i = 0; i < 120; i++) counter.Count(PresentCounter.Dxgi, PresentCounter.DxgiPresentStart, 42);
                for (int i = 0; i < 30; i++) counter.Count(PresentCounter.D3d9, PresentCounter.D3d9PresentStart, 42);
                for (int i = 0; i < 50; i++) counter.Count(PresentCounter.Dxgi, 43, 42);
                for (int i = 0; i < 99; i++) counter.Count(PresentCounter.Dxgi, PresentCounter.DxgiPresentStart, 7);
                Assert(counter.Rate(42, start.AddSeconds(1)) == 150 && counter.Rate(42, start.AddSeconds(2)) == null, "Present events are counted wrongly.");
                // Windows decides who may count: administrators and Performance Log Users. Otherwise it says why.
                if (HardwareSensors.CanTrace) { Assert(counter.Start() && counter.Running, "Frame counting didn't start: " + counter.Problem); counter.Stop(); Assert(!counter.Running, "Frame counting didn't stop."); }
                else Assert(!counter.Start() && !counter.Running && counter.Problem.Contains("full-sensors"), "Frame counting started without the right: " + counter.Problem);
            }
        });
        check("Full-sensors mode: the task runs this exe with the highest rights, has no trigger, and launches from the shell", () => {
            string xml = FullSensors.TaskXml(@"C:\Fixture & Co\Widget Screen\WidgetScreen.exe", @"PC\Fixture <User>");
            var document = new XmlDocument(); document.LoadXml(xml.Substring(xml.IndexOf("<Task", StringComparison.Ordinal)));
            var ns = new XmlNamespaceManager(document.NameTable); ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
            Assert(document.SelectSingleNode("//t:RunLevel", ns).InnerText == "HighestAvailable" && document.SelectSingleNode("//t:Triggers", ns).ChildNodes.Count == 0, "The task isn't elevated, or has a trigger.");
            Assert(document.SelectSingleNode("//t:Command", ns).InnerText == "\"C:\\Fixture & Co\\Widget Screen\\WidgetScreen.exe\"" && document.SelectSingleNode("//t:Arguments", ns).InnerText == FullSensors.Argument && document.SelectSingleNode("//t:UserId", ns).InnerText == @"PC\Fixture <User>", "The task's command or user is wrong.");
            Assert(document.SelectSingleNode("//t:ExecutionTimeLimit", ns).InnerText == "PT0S" && document.SelectSingleNode("//t:DisallowStartIfOnBatteries", ns).InnerText == "false", "The task would be stopped.");
            Assert(FullSensors.PointsTo(@"c:\APPS\widgetscreen.EXE", @"C:\Apps\WidgetScreen.exe") && !FullSensors.PointsTo(@"C:\Old\WidgetScreen.exe", @"C:\Apps\WidgetScreen.exe") && !FullSensors.PointsTo(null, @"C:\Apps\WidgetScreen.exe"), "PointsTo is wrong.");
            Assert(ProcessLauncher.StartThroughShell(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/c exit", null, true), "The desktop shell didn't start a program.");
        });
        check("System stats with all nine readings and every style stay inside their cards at 140%", () => {
            string folder = Path.Combine(directory, "sensor-designs"); Directory.CreateDirectory(folder);
            var cases = new[] {
                new { Size = new Size(260, 280), Styles = "" }, new { Size = new Size(472, 310), Styles = "" }, new { Size = new Size(900, 400), Styles = "Cpu=Line;Gpu=Bars;Vram=Gauge;Fps=Line" },
                new { Size = new Size(760, 576), Styles = "" }, new { Size = new Size(1200, 576), Styles = "Cpu=Bars;CpuTemp=Gauge;Gpu=Ring;GpuTemp=Gauge;Vram=Sparkline;Ram=Line;Fps=Bars;Network=Bars" } };
            foreach (var item in cases)
                foreach (bool full in new[] { false, true })
                {
                    string path = Path.Combine(folder, "settings.xml");
                    var settings = new AppSettings { TextScale = 1.4, StatStyles = item.Styles, WeatherUnits = "metric" };
                    settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "System"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = item.Size.Width; p.Height = item.Size.Height; } return p; }).ToArray();
                    SettingsStore.Save(path, settings);
                    using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                    {
                        var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                        try
                        {
                            using (var controller = new DashboardController(window, path, true))
                            {
                                for (int i = 0; i < 6; i++) controller.SystemView.Update(Fixture(full));
                                var dashboard = (FrameworkElement)window.FindName("Dashboard");
                                dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                                var card = (FrameworkElement)window.FindName("SystemCard");
                                foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => Shown(e, card) && e.ActualWidth > 0 && (e is TextBlock || e is RingGauge || e is ArcGauge || e is HistoryGraph || e is HistoryBars || e is MeterBar)))
                                {
                                    var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                    Assert(box.Left >= 23.5 && box.Top >= 23.5 && box.Right <= card.ActualWidth - 23.5 && box.Bottom <= card.ActualHeight - 23.5, part.GetType().Name + " " + (part is TextBlock ? "\"" + ((TextBlock)part).Text + "\" " : "") + "leaves the " + item.Size + " card: " + box);
                                }
                                var design = controller.SystemView.Current;
                                var cpuTemp = (TextBlock)window.FindName("CpuTempValue");
                                if (cpuTemp != null) Assert(full ? cpuTemp.Text == "88°C" : cpuTemp.Text == "Needs admin" || cpuTemp.Text == "—", "CPU temperature shows \"" + cpuTemp.Text + "\" at " + item.Size + ".");
                                if (item.Styles.Contains("Vram=Gauge") && design != CardSizing.Design.Small) Assert(controller.SystemView.StyleOf("Vram") == "Gauge", "The chosen style isn't drawn at " + item.Size + ".");
                                if (item.Size.Width == 260) Assert(window.FindName("SystemMore") != null && design == CardSizing.Design.Small, "A small card doesn't count the readings that don't fit.");
                                if (item.Size.Width == 1200) Assert(design == CardSizing.Design.Large && controller.SystemView.StyleOf("Cpu") == "Bars" && controller.SystemView.StyleOf("GpuTemp") == "Gauge", "The large card's styles are wrong.");
                            }
                        }
                        finally { window.Close(); }
                    }
                }
        });
        check("Settings → Stats saves readings and styles, and says what the sensors need", () => {
            string path = Path.Combine(directory, "sensor-settings.xml"); if (File.Exists(path)) File.Delete(path);
            SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                try
                {
                    using (var controller = new DashboardController(window, path, true))
                    {
                        ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Assert(((TextBlock)window.FindName("SensorPackageStatus")).Text.Length > 0 && ((TextBlock)window.FindName("PawnIOStatus")).Text.Contains("PawnIO") && ((TextBlock)window.FindName("FpsStatus")).Text.Length > 0, "The sensor notes are empty.");
                        Assert(((ComboBox)window.FindName("FpsStyle")).Items.Count == 6 && ((ComboBox)window.FindName("NetworkStyle")).Items.Count == 4 && window.FindName("UptimeStyle") == null, "The style choices are wrong.");
                        ((CheckBox)window.FindName("CpuTempChoice")).IsChecked = false;
                        ((ComboBox)window.FindName("GpuTempStyle")).SelectedItem = "Bars"; ((ComboBox)window.FindName("CpuStyle")).SelectedItem = "Gauge";
                        ((Button)window.FindName("SaveSettings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        string warning; var saved = SettingsStore.Load(path, out warning);
                        Assert(!saved.ShowCpuTemperature && saved.ShowGpuTemperature && saved.StatStyles == "Cpu=Gauge;GpuTemp=Bars" && !saved.FullSensors, "Stats weren't saved: " + saved.StatStyles);
                    }
                }
                finally { window.Close(); }
            }
        });
    }
}
