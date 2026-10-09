using System;
using System.IO;
using System.Linq;
using System.Threading;
using WidgetScreen;

internal static class SmokeTests
{
    private static int failures;
    private static void Check(string name, Action action)
    {
        try { action(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--spotify-restore-check")
        {
            using (var service = new SpotifyService(args[1]))
                return service.Connected && service.ClientId == new string('a', 32) && service.CanControl ? 0 : 1;
        }
        string directory = args.Length > 0 ? args[0] : AppDomain.CurrentDomain.BaseDirectory;
        SpotifyTests.Run(directory, Check);
        DeckTests.Run(directory, Check);
        Check("Native XAML and control templates load", () => {
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                Assert(window.FindName("SettingsOverlay") != null && window.FindName("MasterSlider") != null, "Dashboard controls missing.");
                window.Close();
            }
        });
        Check("Settings round trip and atomic replacement", () => {
            string path = Path.Combine(directory, "round-trip.xml");
            var settings = new AppSettings { Accent = "Lavender", TextScale = 1.15, QuietMode = true, WeatherLocation = "Test city", Latitude = 24.7, Longitude = 46.7 };
            settings.Shortcuts[0].Label = "My music";
            settings.Shortcuts[0].Target = "https://example.com/music";
            SettingsStore.Save(path, settings); settings.Use24HourClock = true; SettingsStore.Save(path, settings);
            string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(warning == null && loaded.Accent == "Lavender" && loaded.TextScale == 1.15 && loaded.Use24HourClock && loaded.QuietMode && loaded.Shortcuts.Count == 6 && loaded.Shortcuts[0].Label == "My music" && loaded.Shortcuts[0].Target == "https://example.com/music", "Saved settings were not restored.");
            SettingsStore.Save(path, loaded); loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.Shortcuts.Count == 6 && loaded.Shortcuts[0].Label == "My music", "Repeated loads duplicated shortcuts.");
        });
        Check("Malformed settings fall back without overwriting", () => {
            string path = Path.Combine(directory, "malformed.xml"); File.WriteAllText(path, "<broken");
            string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(warning != null && loaded.Shortcuts.Count == 6 && File.ReadAllText(path) == "<broken", "Corrupt settings recovery changed the original file.");
        });
        Check("Appearance, sizes and metric selection survive save", () => {
            string path = Path.Combine(directory, "customization.xml");
            var settings = new AppSettings { Theme = "Warm", CornerRadius = 0, StartFullscreen = false, ShowCpu = false, ShowGpu = true, ShowRam = false, ShowNetwork = false, ShowUptime = false,
                ClockWidth = 10, MediaWidth = 0, MixerWidth = 4, SystemWidth = 2, ClockHeight = 276, MediaHeight = 376, SystemHeight = 356, MixerHeight = 476 };
            SettingsStore.Save(path, settings); string warning; var loaded = SettingsStore.Load(path, out warning);
            Assert(warning == null && loaded.Theme == "Warm" && loaded.CornerRadius == 0 && !loaded.StartFullscreen, "Appearance or launch preference lost.");
            Assert(!loaded.ShowCpu && loaded.ShowGpu && !loaded.ShowRam && !loaded.ShowNetwork && !loaded.ShowUptime, "Metric selections lost.");
            Assert(loaded.ClockWidth == 10 && loaded.MediaWidth == 0 && loaded.MixerWidth == 4 && loaded.ClockHeight == 276 && loaded.MediaHeight == 376 && loaded.SystemHeight == 356 && loaded.MixerHeight == 476, "Widget sizes lost.");
            File.WriteAllText(path, "<AppSettings><Accent>Glacier</Accent></AppSettings>"); loaded = SettingsStore.Load(path, out warning);
            Assert(loaded.ShowCpu && loaded.ShowGpu && loaded.ShowRam && loaded.StartFullscreen && loaded.Theme == "Midnight", "Older settings did not inherit defaults.");
            loaded.ClockWidth = Double.NaN; loaded.MediaWidth = Double.PositiveInfinity; loaded.SystemHeight = -1; loaded.MixerHeight = Double.NaN; loaded.CornerRadius = 100; loaded.Theme = "invalid"; loaded.Normalize();
            double[] widths = DashboardAppearance.ColumnWidths(loaded);
            Assert(widths[0] >= 312 && widths[1] >= 460 && widths[2] >= 440 && widths[3] >= 424 && Math.Abs(widths[0] + widths[1] + widths[2] + widths[3] - 1856) < .01, "Layout escaped screen bounds.");
            Assert(loaded.SystemHeight >= 316 && loaded.MixerHeight == 576 && loaded.CornerRadius == 24 && loaded.Theme == "Midnight", "Invalid sizing not normalized.");
        });
        Check("GPU aggregates processes per engine, not independent engines", () => {
            var samples = new[] {
                new System.Collections.Generic.KeyValuePair<string,double>("pid_1_luid_a_phys_0_eng_0_engtype_3D", 30),
                new System.Collections.Generic.KeyValuePair<string,double>("pid_2_luid_a_phys_0_eng_0_engtype_3D", 40),
                new System.Collections.Generic.KeyValuePair<string,double>("pid_1_luid_a_phys_0_eng_1_engtype_Copy", 50),
                new System.Collections.Generic.KeyValuePair<string,double>("pid_1_luid_b_phys_0_eng_0_engtype_3D", 60),
                new System.Collections.Generic.KeyValuePair<string,double>("pid_3_luid_a_phys_0_eng_0_engtype_3D", Double.NaN) };
            Assert(GpuService.BusiestEngine(samples) == 70, "GPU incorrectly summed engines or devices.");
            Assert(!GpuService.BusiestEngine(new System.Collections.Generic.KeyValuePair<string,double>[0]).HasValue, "Missing GPU reported as 0%.");
            Assert(GpuService.BusiestEngine(new[] { new System.Collections.Generic.KeyValuePair<string,double>("pid_1_luid_a_phys_0_eng_0", 120) }) == 100, "GPU utilization not clamped.");
        });
        Check("Settings tabs preview, cancel and save without losing selections", () => {
            string path = Path.Combine(directory, "ui-settings.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    click("SettingsButton");
                    var tabs = (System.Windows.Controls.TabControl)window.FindName("SettingsTabs"); Assert(tabs.Items.Count == 6, "Settings tabs missing.");
                    ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Warm";
                    var preview = ((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color;
                    click("CancelSettings");
                    Assert(((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color != preview, "Cancel retained preview theme.");
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Slate";
                    foreach (string name in new[] { "CpuChoice", "GpuChoice", "RamChoice", "NetworkChoice", "UptimeChoice" }) ((System.Windows.Controls.CheckBox)window.FindName(name)).IsChecked = false;
                    ((System.Windows.Controls.CheckBox)window.FindName("StartFullscreenChoice")).IsChecked = false;
                    tabs.SelectedIndex = 2; click("SaveSettings");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Slate" && !saved.ShowGpu && !saved.ShowRam && !saved.StartFullscreen, "Cross-tab save lost values.");
                    Assert(((System.Windows.Controls.Border)window.FindName("SystemCard")).Visibility == System.Windows.Visibility.Collapsed, "All-off stats still occupy a card.");
                    click("SettingsButton"); ((System.Windows.Controls.CheckBox)window.FindName("RamChoice")).IsChecked = true; click("SaveSettings");
                    Assert(((System.Windows.FrameworkElement)window.FindName("RamSection")).Visibility == System.Windows.Visibility.Visible && ((System.Windows.FrameworkElement)window.FindName("GpuSection")).Visibility == System.Windows.Visibility.Collapsed, "Individual stats not restored.");
                }
                window.Close();
            }
        });
        Check("Settings normalization", () => {
            var settings = new AppSettings { TextScale = 9, Accent = "invalid", Shortcuts = null, Latitude = double.NaN, WeatherLocation = "Invalid" }; settings.Normalize();
            Assert(settings.TextScale == 1.15 && settings.Accent == "Lime" && settings.Shortcuts.Count == 6 && settings.WeatherLocation == "", "Invalid settings were not normalized.");
        });
        Check("Free layout migration, independent sizes and invalid coordinates", () => {
            string path = Path.Combine(directory, "free-layout.xml");
            var settings = new AppSettings { SwapSideWidgets = true, ClockHeight = 276 };
            var defaults = WidgetLayout.Normalize(null, settings);
            Assert(defaults.Length == WidgetLayout.Ids.Length && defaults[0].X > defaults[5].X, "Legacy swapped arrangement was not preserved.");
            defaults[0].X = 104; defaults[0].Y = 32; defaults[0].Width = 400; defaults[0].Height = 220;
            defaults[1].Width = 310; defaults[1].Visible = false; settings.WidgetLayout = defaults;
            SettingsStore.Save(path, settings); string warning; var restored = SettingsStore.Load(path, out warning);
            Assert(warning == null && restored.WidgetLayout.Length == WidgetLayout.Ids.Length && restored.WidgetLayout[0].X == 104 && restored.WidgetLayout[0].Y == 32 && restored.WidgetLayout[0].Width == 400 && restored.WidgetLayout[1].Width == 310 && !restored.WidgetLayout[1].Visible, "Independent layout or visibility was lost.");
            restored.WidgetLayout = new[] { new WidgetPlacement { Id = "Clock", X = Double.NaN, Y = -900, Width = Double.PositiveInfinity, Height = -1 }, new WidgetPlacement { Id = "unknown" }, null };
            restored.Normalize();
            Assert(restored.WidgetLayout.Length == WidgetLayout.Ids.Length && restored.WidgetLayout.All(p => p.X >= 0 && p.Y >= 0 && p.X + p.Width <= WidgetLayout.Width && p.Y + p.Height <= WidgetLayout.Height), "Malformed layout escaped the canvas or lost widgets.");
        });
        Check("All four resize corners retain opposite anchors and screen bounds", () => {
            var start = new System.Windows.Rect(400, 80, 420, 360);
            foreach (string corner in new[] { "NW", "NE", "SW", "SE" })
            {
                var resized = WidgetLayout.Transform("Focus", start, corner, corner.Contains("W") ? -24 : 24, corner.Contains("N") ? -16 : 16, false);
                Assert(resized.Width == 444 && resized.Height == 376, "Corner did not stretch correctly: " + corner);
                Assert(corner.Contains("W") ? resized.Right == start.Right : resized.Left == start.Left, "Horizontal opposite anchor moved.");
                Assert(corner.Contains("N") ? resized.Bottom == start.Bottom : resized.Top == start.Top, "Vertical opposite anchor moved.");
                var minimum = WidgetLayout.Transform("Focus", start, corner, corner.Contains("W") ? 10000 : -10000, corner.Contains("N") ? 10000 : -10000, false);
                Assert(minimum.Width == WidgetLayout.Minimum("Focus").Width && minimum.Height == WidgetLayout.Minimum("Focus").Height, "Minimum touch size was crossed.");
                var maximum = WidgetLayout.Transform("Focus", start, corner, corner.Contains("W") ? -10000 : 10000, corner.Contains("N") ? -10000 : 10000, false);
                Assert(maximum.X >= 0 && maximum.Y >= 0 && maximum.Right <= WidgetLayout.Width && maximum.Bottom <= WidgetLayout.Height, "Resizing escaped screen.");
            }
            var moved = WidgetLayout.Transform("Focus", start, "", 13, 11, true);
            Assert(moved.X == 416 && moved.Y == 88 && moved.Size == start.Size, "Move snapping changed widget size or used wrong coordinates.");
            moved = WidgetLayout.Transform("Focus", start, "", -10000, 10000, false);
            Assert(moved.X == 0 && moved.Bottom == WidgetLayout.Height, "Movement escaped screen.");
        });
        Check("Live editor transactions, undo/redo, hide/show, cancel and restart", () => {
            string path = Path.Combine(directory, "editor-transactions.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    var editor = controller.Editor; var original = editor.Bounds("Clock"); var focus = editor.Bounds("Focus");
                    click("EditLayoutButton"); editor.Select("Clock"); editor.TransformSelected("SE", 80, 24, false);
                    Assert(editor.Bounds("Clock").Width == original.Width + 80 && editor.Bounds("Focus") == focus, "Resizing changed a neighboring widget.");
                    var host = (System.Windows.FrameworkElement)((System.Windows.Controls.Border)window.FindName("ClockCard")).Parent;
                    Assert(host.Width == editor.Bounds("Clock").Width && host.Height == editor.Bounds("Clock").Height, "Resize was not live in the displayed host.");
                    Assert(!((System.Windows.Controls.Border)window.FindName("MixerCard")).IsHitTestVisible, "Mixer controls are active while dragging widgets.");
                    editor.TransformSelected("", 32, 24, false); var edited = editor.Bounds("Clock");
                    click("UndoLayoutButton"); Assert(editor.Bounds("Clock").X == original.X, "Undo did not restore previous position.");
                    click("RedoLayoutButton"); Assert(editor.Bounds("Clock") == edited, "Redo did not restore position.");
                    editor.SetVisible("Media", false); Assert(!editor.IsVisible("Media"), "Hide did not remove media widget.");
                    editor.Undo(); Assert(editor.IsVisible("Media"), "Undo did not restore hidden widget.");
                    click("CancelLayoutButton"); Assert(!editor.Editing && editor.Bounds("Clock") == original && !editor.CanUndo, "Cancel did not restore saved layout.");
                    Assert(((System.Windows.Controls.Border)window.FindName("MixerCard")).IsHitTestVisible, "Normal controls did not resume after cancel.");
                    click("EditLayoutButton"); editor.Select("Clock"); editor.TransformSelected("", 40, 32, false); editor.SetVisible("Focus", false); click("SaveLayoutButton");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.WidgetLayout != null && saved.WidgetLayout.First(p => p.Id == "Clock").X == 40 && saved.WidgetLayout.First(p => p.Id == "Clock").Layer > 0 && !saved.WidgetLayout.First(p => p.Id == "Focus").Visible, "Layout save lost edited positions, stacking order or visibility.");
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Warm"; click("SaveSettings");
                    saved = SettingsStore.Load(path, out warning); Assert(saved.WidgetLayout.First(p => p.Id == "Clock").X == 40 && saved.Theme == "Warm", "Saving theme erased custom geometry.");
                    click("EditLayoutButton"); click("ResetLayoutButton"); editor.Undo(); Assert(editor.Bounds("Clock").X == 40 && !editor.IsVisible("Focus"), "Reset was not undoable."); click("CancelLayoutButton");
                }
                window.Close();
            }
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true)) Assert(controller.Editor.Bounds("Clock").X == 40 && !controller.Editor.IsVisible("Focus"), "Restart lost layout.");
                window.Close();
            }
        });
        Check("Layout save failure retains the draft and remains cancelable", () => {
            string blockedPath = Path.Combine(directory, "save-layout-directory"); Directory.CreateDirectory(blockedPath);
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, blockedPath, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    var original = controller.Editor.Bounds("Clock"); click("EditLayoutButton"); controller.Editor.Select("Clock"); controller.Editor.TransformSelected("", 32, 16, false);
                    var edited = controller.Editor.Bounds("Clock"); click("SaveLayoutButton");
                    Assert(controller.Editor.Editing && controller.Editor.Bounds("Clock") == edited, "Failed save discarded the draft or exited editing.");
                    Assert(((System.Windows.Controls.TextBlock)window.FindName("StatusMessage")).Text.Contains("Could not save layout"), "Save failure was not explained.");
                    click("CancelLayoutButton"); Assert(controller.Editor.Bounds("Clock") == original, "Cancel after failed save did not restore original.");
                }
                window.Close();
            }
        });
        Check("Smallest free widgets keep essential content and touch controls in bounds", () => {
            string path = Path.Combine(directory, "minimum-widgets.xml");
            var settings = new AppSettings { TextScale = 1.15 };
            settings.WidgetLayout = WidgetLayout.Ids.Select(id => new WidgetPlacement { Id = id, X = 0, Y = 0, Width = WidgetLayout.Minimum(id).Width, Height = WidgetLayout.Minimum(id).Height }).ToArray();
            SettingsStore.Save(path, settings);
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    ((System.Windows.Controls.TextBlock)window.FindName("FocusStatus")).Text = "Session complete. Take a breath.";
                    ((System.Windows.Controls.TextBlock)window.FindName("WeatherCity")).Text = "A long weather location that cannot fit on one line";
                    var dashboard = (System.Windows.FrameworkElement)window.FindName("Dashboard");
                    dashboard.Measure(new System.Windows.Size(1920, 720)); dashboard.Arrange(new System.Windows.Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                    foreach (var pair in new[] { new[] { "ClockPeriod", "ClockCard" }, new[] { "MediaHint", "MediaCard" }, new[] { "UptimeValue", "SystemCard" }, new[] { "WeatherButton", "WeatherCard" } })
                    {
                        var child = (System.Windows.FrameworkElement)window.FindName(pair[0]); var card = (System.Windows.FrameworkElement)window.FindName(pair[1]);
                        Assert(child.TransformToAncestor(card).Transform(new System.Windows.Point(0, child.ActualHeight)).Y <= card.ActualHeight - 20, pair[0] + " escapes minimum widget height.");
                    }
                    var progress = (System.Windows.FrameworkElement)window.FindName("FocusProgress"); var focusCard = (System.Windows.FrameworkElement)window.FindName("FocusCard"); var presets = (System.Windows.FrameworkElement)window.FindName("Focus25");
                    Assert(progress.TransformToAncestor(focusCard).Transform(new System.Windows.Point(0, progress.ActualHeight)).Y <= presets.TransformToAncestor(focusCard).Transform(new System.Windows.Point(0, 0)).Y, "Compact focus content overlaps its controls.");
                    foreach (System.Windows.Controls.Button button in ((System.Windows.Controls.Primitives.UniformGrid)window.FindName("ShortcutGrid")).Children)
                    {
                        var content = (System.Windows.FrameworkElement)button.Content;
                        Assert(button.ActualHeight >= 48 && content.ActualHeight <= button.ActualHeight - 16 && content.ActualWidth <= button.ActualWidth - 16, "Minimum quick-launch clips content or shrinks touch targets.");
                    }
                }
                window.Close();
            }
        });
        Check("Resized dashboard keeps stats and shortcut content inside widgets", () => {
            string path = Path.Combine(directory, "layout-boundary.xml");
            SettingsStore.Save(path, new AppSettings { TextScale = 1.15, ClockWidth = 10, MediaWidth = 0, MixerWidth = 0, SystemWidth = 0, ClockHeight = 276, MediaHeight = 376, SystemHeight = 316, MixerHeight = 476 });
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    var dashboard = (System.Windows.FrameworkElement)window.FindName("Dashboard");
                    dashboard.Measure(new System.Windows.Size(1920, 720)); dashboard.Arrange(new System.Windows.Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                    var uptime = (System.Windows.FrameworkElement)window.FindName("UptimeValue"); var card = (System.Windows.FrameworkElement)window.FindName("SystemCard");
                    var corner = uptime.TransformToAncestor(card).Transform(new System.Windows.Point(0, uptime.ActualHeight));
                    Assert(corner.Y <= card.ActualHeight - 20, "Uptime overflows the compact system widget.");
                    var shortcuts = (System.Windows.Controls.Primitives.UniformGrid)window.FindName("ShortcutGrid");
                    foreach (System.Windows.Controls.Button button in shortcuts.Children)
                    {
                        var content = (System.Windows.FrameworkElement)button.Content;
                        Assert(content.ActualHeight <= button.ActualHeight - 16 && content.ActualWidth <= button.ActualWidth - 14, "Compact shortcut content is clipped.");
                    }
                }
                window.Close();
            }
        });
        Check("Focus start, pause, reset and completion boundary", () => {
            var timer = new FocusTimer(); timer.Reset(5); timer.Toggle(); Assert(timer.Running, "Timer did not start."); Thread.Sleep(20); timer.Toggle();
            var paused = timer.Remaining; Thread.Sleep(20); Assert(timer.Remaining == paused, "Paused timer continued.");
            Assert(timer.RemainingAt(TimeSpan.FromMinutes(6)) == TimeSpan.Zero, "Timer became negative."); timer.Reset(25); Assert(!timer.Running && timer.Remaining == TimeSpan.FromMinutes(25), "Reset failed.");
        });
        Check("Shortcut validation", () => {
            Assert(Launcher.IsAllowed("https://example.com") && Launcher.IsAllowed("spotify:") && Launcher.IsAllowed("%windir%\\explorer.exe"), "Valid shortcut rejected.");
            Assert(!Launcher.IsAllowed("cmd.exe /c anything") && !Launcher.IsAllowed("javascript:alert(1)") && !Launcher.IsAllowed("file:///C:/a.bat") && !Launcher.IsAllowed(""), "Unsafe or invalid shortcut accepted.");
        });
        Check("Weather parsing and absent-data handling", () => {
            var result = WeatherService.Parse("{\"current\":{\"temperature_2m\":26.4,\"apparent_temperature\":25,\"wind_speed_10m\":12.3,\"weather_code\":0}}");
            Assert(Math.Abs(result.Temperature - 26.4) < .001 && WeatherService.Describe(result.Code) == "Clear skies", "Weather parsed incorrectly.");
            bool threw = false; try { WeatherService.Parse("{\"current\":{\"temperature_2m\":null}}"); } catch (InvalidOperationException) { threw = true; } Assert(threw, "Missing weather became zero readings.");
        });
        Check("Live system readings", () => {
            using (var provider = new SystemService()) {
            provider.Read(); Thread.Sleep(250); var result = provider.Read();
            Assert(result.Cpu >= 0 && result.Cpu <= 100 && result.TotalMemoryGB > 0 && result.MemoryPercent > 0 && result.MemoryPercent <= 100, "System readings invalid.");
            Console.WriteLine("  CPU " + result.Cpu.ToString("0") + "%; memory " + result.UsedMemoryGB.ToString("0.0") + "/" + result.TotalMemoryGB.ToString("0.0") + " GB");
            Assert(!result.Gpu.HasValue || result.Gpu.Value >= 0 && result.Gpu.Value <= 100, "GPU value invalid.");
            Console.WriteLine("  GPU " + (result.Gpu.HasValue ? result.Gpu.Value.ToString("0") + "%" : result.GpuStatus));
            var disabled = provider.Read(false, false, false, false, false);
            Assert(!disabled.Gpu.HasValue && !disabled.CpuAvailable && !disabled.MemoryAvailable && !disabled.NetworkAvailable, "Hidden metrics were still sampled.");
            }
        });
        Check("Live audio read (does not change volume)", () => {
            using (var audio = new AudioService()) { var result = audio.Read(); Assert(result.Volume >= 0 && result.Volume <= 1, "Master volume invalid."); Console.WriteLine("  Output: " + result.Device + "; sessions: " + result.Sessions.Count); }
        });
        Check("Windows media session access (does not control playback)", () => {
            var task = new MediaService().ReadAsync(); Assert(task.Wait(10000), "Media read timed out."); var result = task.Result; Console.WriteLine(result.Available ? "  A media session is available." : "  No media session is active; empty state is expected.");
        });
        Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
