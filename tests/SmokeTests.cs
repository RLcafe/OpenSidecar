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
        // Dashboards in the checks are laid out for the 1920 × 720 reference screen, whatever monitors this PC has.
        Program.ScreenSize = new System.Windows.Size(1920, 720);
        SpotifyTests.Run(directory, Check);
        DeckTests.Run(directory, Check);
        DiscordTests.Run(directory, Check);
        RemoteTests.Run(directory, Check);
        ScreenSizeTests.Run(directory, Check);
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
                    var tabs = (System.Windows.Controls.TabControl)window.FindName("SettingsTabs"); Assert(tabs.Items.Count == 8, "Settings tabs missing.");
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
        Check("Footer Cancel discards the same settings draft as the header Cancel", () => {
            string path = Path.Combine(directory, "footer-cancel.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    var page = ((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color;
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Warm";
                    ((System.Windows.Controls.Slider)window.FindName("ScaleChoice")).Value = 115;
                    ((System.Windows.Controls.CheckBox)window.FindName("CpuChoice")).IsChecked = false;
                    click("CancelSettingsFooter");
                    Assert(((System.Windows.Controls.Grid)window.FindName("SettingsOverlay")).Visibility == System.Windows.Visibility.Collapsed, "Footer Cancel left Settings open.");
                    Assert(((System.Windows.Media.SolidColorBrush)window.Resources["Page"]).Color == page && (double)window.Resources["SmallSize"] == 15, "Footer Cancel kept the previewed appearance.");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Midnight" && saved.ShowCpu && saved.TextScale == 1, "Footer Cancel saved the draft.");
                    click("SettingsButton");
                    Assert((string)((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem == "Midnight" && ((System.Windows.Controls.CheckBox)window.FindName("CpuChoice")).IsChecked == true, "Reopened Settings showed the discarded draft.");
                    click("CancelSettings");
                    var panel = (System.Windows.Controls.Border)((System.Windows.Controls.Grid)window.FindName("SettingsOverlay")).Children[0];
                    Assert(panel.Width == 1440 && panel.Height == 672, "Settings panel is not 1440 × 672.");
                }
                window.Close();
            }
        });
        Check("Settings copy is deep and complete", () => {
            var original = new AppSettings { Accent = "Amber", RemoteEnabled = true, HideToTrayWithPhone = false, QuietMode = true, CurrentPage = 1, PageName = "Work",
                MorePages = new[] { new LayoutPage { Name = "Games", WidgetLayout = WidgetLayout.DefaultFor(new AppSettings(), WidgetLayout.Reference) } },
                CanvasLayouts = new[] { new CanvasLayout { Width = 1000, Height = 500, Widgets = WidgetLayout.DefaultFor(new AppSettings(), new System.Windows.Size(1000, 500)) } } };
            original.WidgetLayout = WidgetLayout.DefaultFor(original, WidgetLayout.Reference);
            var copy = original.Copy();
            Func<AppSettings, string> xml = s => { using (var writer = new StringWriter()) { new System.Xml.Serialization.XmlSerializer(typeof(AppSettings)).Serialize(writer, s); return writer.ToString(); } };
            Assert(xml(copy) == xml(original), "The copy differs from the original.");
            Assert(copy.Shortcuts != original.Shortcuts && copy.WidgetLayout != original.WidgetLayout && copy.CanvasLayouts != original.CanvasLayouts && copy.MorePages != original.MorePages, "The copy shares collections with the original.");
            copy.Shortcuts[0].Label = "Changed"; copy.WidgetLayout[0].X += 8; copy.MorePages[0].Name = "Changed"; copy.CanvasLayouts[0].Widgets[0].Y += 8;
            Assert(original.Shortcuts[0].Label != "Changed" && original.WidgetLayout[0].X != copy.WidgetLayout[0].X && original.MorePages[0].Name == "Games" && original.CanvasLayouts[0].Widgets[0].Y != copy.CanvasLayouts[0].Widgets[0].Y, "Changing the copy changed the original.");
        });
        Check("Settings → Save keeps pages, the current page, Quiet mode and the phone link", () => {
            string path = Path.Combine(directory, "save-keeps.xml");
            SettingsStore.Save(path, new AppSettings { RemoteEnabled = true, HideToTrayWithPhone = false, QuietMode = true, PageName = "Work", MorePages = new[] { new LayoutPage { Name = "Games" }, new LayoutPage { Name = "Music" } }, CurrentPage = 1 });
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Warm"; click("SaveSettings");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.Theme == "Warm", "The theme change was not saved.");
                    Assert(WidgetLayout.PageCount(saved) == 3 && saved.PageName == "Work" && saved.MorePages[1].Name == "Music" && saved.CurrentPage == 1, "Save changes dropped the pages or the current page.");
                    Assert(saved.RemoteEnabled && !saved.HideToTrayWithPhone && saved.QuietMode, "Save changes reset the phone link or Quiet mode.");
                    click("SettingsButton");
                    Assert(((System.Windows.Controls.CheckBox)window.FindName("RemoteEnabledChoice")).IsChecked == true && ((System.Windows.Controls.TextBlock)window.FindName("PageNameText")).Text == "Games", "The dashboard shows a different state than it saved.");
                    click("CancelSettings");
                }
                window.Close();
            }
        });
        Check("Quick Launch confirms in a themed sheet, and the tips go away with Got it", () => {
            string path = Path.Combine(directory, "confirm-sheet.xml");
            var settings = new AppSettings(); settings.Shortcuts.Insert(0, new Shortcut { Label = "Quiet", Action = "Control", Target = "quiet", Glyph = "", Confirm = true });
            SettingsStore.Save(path, settings);
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<System.Windows.Controls.Button> click = button => button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Action pump = () => System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var overlay = (System.Windows.Controls.Grid)window.FindName("ConfirmOverlay");
                    var deck = ((System.Windows.Controls.Primitives.UniformGrid)window.FindName("ShortcutGrid")).Children.OfType<System.Windows.Controls.Button>().First();
                    Assert(overlay.Visibility == System.Windows.Visibility.Collapsed && !settings.QuietMode, "The sheet started open.");
                    click(deck); pump();
                    Assert(overlay.Visibility == System.Windows.Visibility.Visible && ((System.Windows.Controls.TextBlock)window.FindName("ConfirmSubject")).Text == "Quiet" && (string)((System.Windows.Controls.Button)window.FindName("ConfirmAction")).Content == "Run Quiet", "The sheet didn't open for a confirmed button.");
                    click((System.Windows.Controls.Button)window.FindName("ConfirmCancel")); pump();
                    string warning;
                    Assert(overlay.Visibility == System.Windows.Visibility.Collapsed && !SettingsStore.Load(path, out warning).QuietMode, "Cancel ran the action, or left the sheet open.");
                    click(deck); pump(); click((System.Windows.Controls.Button)window.FindName("ConfirmAction")); pump();
                    Assert(overlay.Visibility == System.Windows.Visibility.Collapsed && SettingsStore.Load(path, out warning).QuietMode, "Run didn't run the action.");
                    foreach (string name in new[] { "ConfirmCancel", "ConfirmAction" }) Assert(((System.Windows.FrameworkElement)window.FindName(name)).MinHeight >= 52, name + " is shorter than 52.");
                    var hint = (System.Windows.Controls.StackPanel)window.FindName("HintRow");
                    Assert(hint.Visibility == System.Windows.Visibility.Visible, "The first-launch tips aren't shown.");
                    click((System.Windows.Controls.Button)window.FindName("HintDismiss"));
                    Assert(hint.Visibility == System.Windows.Visibility.Collapsed && SettingsStore.Load(path, out warning).HintDismissed, "Got it didn't put the tips away for good.");
                    var tabs = (System.Windows.Controls.TabControl)window.FindName("SettingsTabs");
                    Assert((string)((System.Windows.Controls.TabItem)tabs.Items[6]).Header == "Phone" && (string)((System.Windows.Controls.TabItem)tabs.Items[7]).Header == "Discord", "The Phone tab doesn't come before Discord.");
                    Assert(!new AppSettings().HideToTrayWithPhone, "Tray mode with the phone is on by default.");
                }
                window.Close();
            }
        });
        Check("Touch sliders map to the rendered track, clamp, snap and follow direction changes", () => {
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                var style = (System.Windows.Style)window.Resources[typeof(System.Windows.Controls.Slider)];
                Func<double, double, double, System.Windows.Controls.Slider> make = (min, max, tick) => {
                    var s = new System.Windows.Controls.Slider { Style = style, Minimum = min, Maximum = max, TickFrequency = tick, IsSnapToTickEnabled = tick > 0 };
                    s.ApplyTemplate(); s.Measure(new System.Windows.Size(448, 48)); s.Arrange(new System.Windows.Rect(0, 0, 448, 48)); s.UpdateLayout(); return s;
                };
                var volume = make(0, 100, 0);
                var track = (System.Windows.Controls.Primitives.Track)volume.Template.FindName("PART_Track", volume);
                Assert(track.Thumb.ActualWidth == 48 && track.Thumb.ActualHeight == 48, "Slider thumb target is not 48 × 48.");
                double start = track.TranslatePoint(new System.Windows.Point(0, 0), volume).X, length = track.ActualWidth;
                Func<double, double> at = fraction => start + 24 + fraction * (length - 48);
                Assert(Math.Abs(TouchSlider.ValueAt(volume, at(.5)) - 50) < .01 && Math.Abs(TouchSlider.ValueAt(volume, at(.25)) - 25) < .01, "Touch does not map to the track centre line.");
                Assert(TouchSlider.ValueAt(volume, -500) == 0 && TouchSlider.ValueAt(volume, 5000) == 100 && TouchSlider.ValueAt(volume, at(0)) == 0 && TouchSlider.ValueAt(volume, at(1)) == 100, "Endpoints are not clamped.");
                // Rapid changes: the thumb has not been re-arranged, so mapping must not depend on the current value.
                foreach (double fraction in new[] { .9, .1, .7, .3, .95 }) { volume.Value = TouchSlider.ValueAt(volume, at(fraction)); Assert(Math.Abs(volume.Value - fraction * 100) < .01, "Rapid or reversed movement drifted before relayout."); }
                var corners = make(0, 24, 4);
                Assert(TouchSlider.ValueAt(corners, at(.30)) == 8 && TouchSlider.ValueAt(corners, at(.36)) == 8 && TouchSlider.ValueAt(corners, at(.42)) == 12 && TouchSlider.ValueAt(corners, at(1.2)) == 24, "Corner steps do not snap.");
                var scale = make(90, 115, 5);
                Assert(TouchSlider.ValueAt(scale, at(0)) == 90 && TouchSlider.ValueAt(scale, at(.62)) == 105 && TouchSlider.ValueAt(scale, at(.58)) == 105 && TouchSlider.ValueAt(scale, at(.5)) % 5 == 0, "Text size steps do not snap.");
                Assert(System.Windows.Input.Stylus.GetIsPressAndHoldEnabled(window) == false && System.Windows.Input.Stylus.GetIsFlicksEnabled(window) == false, "Press-and-hold or flicks are still enabled.");
                window.Close();
            }
        });
        Check("Settings panel touch targets and Focus preset selection", () => {
            string path = Path.Combine(directory, "touch-targets.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    click("SettingsButton");
                    var dashboard = (System.Windows.FrameworkElement)window.FindName("Dashboard");
                    dashboard.Measure(new System.Windows.Size(1920, 720)); dashboard.Arrange(new System.Windows.Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                    foreach (string name in new[] { "SaveSettings", "CancelSettingsFooter", "ThemeChoice", "AccentChoice" })
                        Assert(((System.Windows.FrameworkElement)window.FindName(name)).ActualHeight >= 52, name + " is shorter than 52.");
                    click("CancelSettings");
                    Func<string, string> nameOf = id => System.Windows.Automation.AutomationProperties.GetName((System.Windows.DependencyObject)window.FindName(id));
                    Assert(nameOf("Focus25") == "25 minutes, selected" && nameOf("Focus50") == "50 minutes", "Default Focus preset not marked selected.");
                    click("Focus50"); Assert(nameOf("Focus50") == "50 minutes, selected" && nameOf("Focus25") == "25 minutes", "Focus selection did not follow.");
                    click("Focus5"); Assert(nameOf("Focus5") == "5 minutes, selected" && nameOf("Focus50") == "50 minutes", "Focus selection did not follow.");
                    Assert(((System.Windows.Controls.Button)window.FindName("MicrophoneButton")).MinHeight >= 48, "Microphone button shorter than 48.");
                }
                window.Close();
            }
        });
        Check("Web requests skip IPv6 only when the host also has IPv4", () => {
            var v6 = System.Net.IPAddress.Parse("2a04:4e42:54::810"); var v4 = System.Net.IPAddress.Parse("199.232.83.42");
            Assert(PreferIPv4Handler.SkipAddress(v6, new[] { v6, v4 }), "Dual-stack host still tries IPv6 first.");
            Assert(!PreferIPv4Handler.SkipAddress(v4, new[] { v6, v4 }), "IPv4 address was skipped.");
            Assert(!PreferIPv4Handler.SkipAddress(v6, new[] { v6 }) && !PreferIPv4Handler.SkipAddress(v6, new System.Net.IPAddress[0]), "IPv6-only host became unreachable.");
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
            defaults[0].X = 104; defaults[0].Y = 32; defaults[0].Width = 400; defaults[0].Height = 320;
            defaults[1].Width = 410; defaults[1].Visible = false; settings.WidgetLayout = defaults;
            SettingsStore.Save(path, settings); string warning; var restored = SettingsStore.Load(path, out warning);
            Assert(warning == null && restored.WidgetLayout.Length == WidgetLayout.Ids.Length && restored.WidgetLayout[0].X == 104 && restored.WidgetLayout[0].Y == 32 && restored.WidgetLayout[0].Width == 400 && restored.WidgetLayout[1].Width == 410 && !restored.WidgetLayout[1].Visible, "Independent layout or visibility was lost.");
            restored.WidgetLayout = new[] { new WidgetPlacement { Id = "Focus", X = Double.NaN, Y = -900, Width = Double.PositiveInfinity, Height = -1 }, new WidgetPlacement { Id = "unknown" }, null };
            restored.Normalize();
            Assert(restored.WidgetLayout.Length == WidgetLayout.Ids.Length && restored.WidgetLayout.All(p => p.X >= 0 && p.Y >= 0 && p.X + p.Width <= WidgetLayout.ReferenceWidth && p.Y + p.Height <= WidgetLayout.ReferenceHeight), "Malformed layout escaped the canvas or lost widgets.");
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
                Assert(maximum.X >= 0 && maximum.Y >= 0 && maximum.Right <= WidgetLayout.ReferenceWidth && maximum.Bottom <= WidgetLayout.ReferenceHeight, "Resizing escaped screen.");
            }
            var moved = WidgetLayout.Transform("Focus", start, "", 13, 11, true);
            Assert(moved.X == 416 && moved.Y == 88 && moved.Size == start.Size, "Move snapping changed widget size or used wrong coordinates.");
            moved = WidgetLayout.Transform("Focus", start, "", -10000, 10000, false);
            Assert(moved.X == 0 && moved.Bottom == WidgetLayout.ReferenceHeight, "Movement escaped screen.");
        });
        Check("Live editor transactions, undo/redo, hide/show, cancel and restart", () => {
            string path = Path.Combine(directory, "editor-transactions.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    var editor = controller.Editor; var original = editor.Bounds("Focus"); var media = editor.Bounds("Media");
                    click("EditLayoutButton"); editor.Select("Focus"); editor.TransformSelected("SE", 80, 24, false);
                    Assert(editor.Bounds("Focus").Width == original.Width + 80 && editor.Bounds("Media") == media, "Resizing changed a neighboring widget.");
                    var host = (System.Windows.FrameworkElement)((System.Windows.Controls.Border)window.FindName("FocusCard")).Parent;
                    Assert(host.Width == editor.Bounds("Focus").Width && host.Height == editor.Bounds("Focus").Height, "Resize was not live in the displayed host.");
                    Assert(!((System.Windows.Controls.Border)window.FindName("MixerCard")).IsHitTestVisible, "Mixer controls are active while dragging widgets.");
                    editor.TransformSelected("", 32, 24, false); var edited = editor.Bounds("Focus");
                    click("UndoLayoutButton"); Assert(editor.Bounds("Focus").X == original.X, "Undo did not restore previous position.");
                    click("RedoLayoutButton"); Assert(editor.Bounds("Focus") == edited, "Redo did not restore position.");
                    editor.SetVisible("Media", false); Assert(!editor.IsVisible("Media"), "Hide did not remove media widget.");
                    editor.Undo(); Assert(editor.IsVisible("Media"), "Undo did not restore hidden widget.");
                    click("CancelLayoutButton"); Assert(!editor.Editing && editor.Bounds("Focus") == original && !editor.CanUndo, "Cancel did not restore saved layout.");
                    Assert(((System.Windows.Controls.Border)window.FindName("MixerCard")).IsHitTestVisible, "Normal controls did not resume after cancel.");
                    click("EditLayoutButton"); editor.Select("Focus"); editor.TransformSelected("", 40, 32, false); editor.SetVisible("Shortcuts", false); click("SaveLayoutButton");
                    string warning; var saved = SettingsStore.Load(path, out warning);
                    Assert(saved.WidgetLayout != null && saved.WidgetLayout.First(p => p.Id == "Focus").X == 40 && saved.WidgetLayout.First(p => p.Id == "Focus").Layer > 0 && !saved.WidgetLayout.First(p => p.Id == "Shortcuts").Visible, "Layout save lost edited positions, stacking order or visibility.");
                    click("SettingsButton"); ((System.Windows.Controls.ComboBox)window.FindName("ThemeChoice")).SelectedItem = "Warm"; click("SaveSettings");
                    saved = SettingsStore.Load(path, out warning); Assert(saved.WidgetLayout.First(p => p.Id == "Focus").X == 40 && saved.Theme == "Warm", "Saving theme erased custom geometry.");
                    click("EditLayoutButton"); click("ResetLayoutButton"); editor.Undo(); Assert(editor.Bounds("Focus").X == 40 && !editor.IsVisible("Shortcuts"), "Reset was not undoable."); click("CancelLayoutButton");
                }
                window.Close();
            }
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true)) Assert(controller.Editor.Bounds("Focus").X == 40 && !controller.Editor.IsVisible("Shortcuts"), "Restart lost layout.");
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
                    var original = controller.Editor.Bounds("Focus"); click("EditLayoutButton"); controller.Editor.Select("Focus"); controller.Editor.TransformSelected("", 32, 16, false);
                    var edited = controller.Editor.Bounds("Focus"); click("SaveLayoutButton");
                    Assert(controller.Editor.Editing && controller.Editor.Bounds("Focus") == edited, "Failed save discarded the draft or exited editing.");
                    Assert(((System.Windows.Controls.TextBlock)window.FindName("StatusMessage")).Text.Contains("Could not save layout"), "Save failure was not explained.");
                    click("CancelLayoutButton"); Assert(controller.Editor.Bounds("Focus") == original, "Cancel after failed save did not restore original.");
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
                    foreach (var pair in new[] { new[] { "MediaHint", "MediaCard" }, new[] { "UptimeValue", "SystemCard" }, new[] { "WeatherButton", "WeatherCard" } })
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
                    var header = (System.Windows.FrameworkElement)window.FindName("HeaderClock");
                    Assert(header.ActualHeight <= 72 + 8 && header.ActualWidth > 0, "Header clock does not fit the 72 px header.");
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
        Check("Header clock replaces the Clock widget and old layouts give Focus its space", () => {
            Assert(!WidgetLayout.Ids.Contains("Clock"), "Clock is still a dashboard widget.");
            var settings = new AppSettings();
            var focus = WidgetLayout.SpotifyDefault(settings).First(p => p.Id == "Focus");
            Assert(focus.X == 0 && focus.Y == 0 && focus.Height == WidgetLayout.ReferenceHeight, "New default does not give Focus the full column.");
            var old = WidgetLayout.SpotifyDefault(settings).Where(p => p.Id != "Focus").Concat(new[] {
                new WidgetPlacement { Id = "Clock", X = 0, Y = 0, Width = 320, Height = 230 },
                new WidgetPlacement { Id = "Focus", X = 0, Y = 246, Width = 320, Height = 330 } }).ToArray();
            var migrated = WidgetLayout.Normalize(old, settings);
            Assert(migrated.Length == WidgetLayout.Ids.Length && migrated.All(p => p.Id != "Clock"), "Saved Clock placement survived normalization.");
            var grown = migrated.First(p => p.Id == "Focus");
            Assert(grown.Y == 0 && grown.Height == 576 && grown.Width == 320, "Focus did not take over the old clock space.");
            old.First(p => p.Id == "Focus").X = 900;
            Assert(WidgetLayout.Normalize(old, settings).First(p => p.Id == "Focus").Y == 246, "A custom Focus position was moved.");
            string path = Path.Combine(directory, "header-clock.xml"); SettingsStore.Save(path, new AppSettings { Use24HourClock = true });
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Func<string, string> text = name => ((System.Windows.Controls.TextBlock)window.FindName(name)).Text;
                    Assert(System.Text.RegularExpressions.Regex.IsMatch(text("ClockTime"), "^[0-2][0-9]:[0-5][0-9]$") && text("ClockDate").Length > 0 && text("ClockDay").Length > 0, "Header clock shows no time or date.");
                    Assert(((System.Windows.FrameworkElement)window.FindName("ClockPeriod")).Visibility == System.Windows.Visibility.Collapsed, "24-hour clock still shows AM/PM.");
                    click("EditLayoutButton");
                    Assert(((System.Windows.FrameworkElement)window.FindName("HeaderClock")).Visibility == System.Windows.Visibility.Visible, "Header clock hides while editing the layout.");
                    click("CancelLayoutButton");
                }
                window.Close();
            }
        });
        Check("System stats pick columns and scale with their widget", () => {
            var cell = new System.Windows.Size(336, 46);
            Assert(SystemStatsLayout.BestColumns(new System.Windows.Size(332, 226), cell, 5) == 1, "Compact stats did not stay in one column.");
            Assert(SystemStatsLayout.BestColumns(new System.Windows.Size(900, 226), cell, 5) == 2, "Wide stats did not use two columns.");
            Assert(SystemStatsLayout.BestColumns(new System.Windows.Size(1800, 120), cell, 5) == 3, "Very wide stats did not use three columns.");
            Assert(SystemStatsLayout.BestColumns(new System.Windows.Size(0, 0), cell, 5) == 1 && SystemStatsLayout.BestColumns(new System.Windows.Size(900, 226), cell, 1) == 1, "Degenerate sizes picked several columns.");
            Func<double, double, double> cpuHeight = (width, height) => {
                string path = Path.Combine(directory, "stats-" + width + ".xml");
                var settings = new AppSettings();
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { if (p.Id == "System") { p.Visible = true; p.X = 0; p.Y = 0; p.Width = width; p.Height = height; p.Layer = 5; } return p; }).ToArray();
                SettingsStore.Save(path, settings);
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            var dashboard = (System.Windows.FrameworkElement)window.FindName("Dashboard");
                            dashboard.Measure(new System.Windows.Size(1920, 720)); dashboard.Arrange(new System.Windows.Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                            var card = (System.Windows.FrameworkElement)window.FindName("SystemCard");
                            foreach (string name in new[] { "CpuSection", "UptimeSection" })
                            {
                                var row = (System.Windows.FrameworkElement)window.FindName(name);
                                var bounds = row.TransformToAncestor(card).TransformBounds(new System.Windows.Rect(0, 0, row.ActualWidth, row.ActualHeight));
                                Assert(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= card.ActualWidth && bounds.Bottom <= card.ActualHeight - 20, name + " escapes the system widget at " + width + " × " + height + ".");
                            }
                            var value = (System.Windows.FrameworkElement)window.FindName("CpuValue");
                            return value.TransformToAncestor(card).TransformBounds(new System.Windows.Rect(0, 0, value.ActualWidth, value.ActualHeight)).Height;
                        }
                    }
                    finally { window.Close(); }
                }
            };
            double small = cpuHeight(380, 310), large = cpuHeight(760, 576);
            Assert(large >= small * 1.5, "System stats did not grow with their widget (" + small.ToString("0.0") + " → " + large.ToString("0.0") + ").");
        });
        Check("Focus start, pause, reset and completion boundary", () => {
            var timer = new FocusTimer(); timer.Reset(5); timer.Toggle(); Assert(timer.Running, "Timer did not start."); Thread.Sleep(20); timer.Toggle();
            var paused = timer.Remaining; Thread.Sleep(20); Assert(timer.Remaining == paused, "Paused timer continued.");
            Assert(timer.RemainingAt(TimeSpan.FromMinutes(6)) == TimeSpan.Zero, "Timer became negative."); timer.Reset(25); Assert(!timer.Running && timer.Remaining == TimeSpan.FromMinutes(25), "Reset failed.");
            Assert(timer.Paused == false, "A fresh timer reads as paused."); timer.Toggle(); Thread.Sleep(20); timer.Toggle(); Assert(timer.Paused, "A stopped timer with time left isn't paused.");
        });
        Check("Focus finish shows for a minute, is dismissed by a tap, and counts sessions", () => {
            DateTime clock = new DateTime(2026, 10, 22, 9, 0, 0, DateTimeKind.Utc);
            var timer = new FocusTimer(() => clock);
            timer.Reset(TimeSpan.FromMilliseconds(30)); timer.Toggle(); Thread.Sleep(80);
            Assert(timer.FinishIfElapsed() && timer.Finished && !timer.Running && timer.Remaining == TimeSpan.Zero && timer.Progress == 1, "The session didn't finish.");
            Assert(!timer.FinishIfElapsed() && timer.Finished, "Finishing reported twice, or the finish was lost.");
            clock = clock.AddSeconds(61); Assert(!timer.Finished, "The finish didn't clear after a minute.");
            timer.Reset(TimeSpan.FromMilliseconds(30)); timer.Toggle(); Thread.Sleep(80); timer.FinishIfElapsed();
            Assert(timer.Finished, "The second finish wasn't shown."); timer.Dismiss(); Assert(!timer.Finished, "Dismiss didn't clear the finish.");
            timer.Reset(TimeSpan.FromMilliseconds(30)); timer.Toggle(); Thread.Sleep(80); timer.FinishIfElapsed(); timer.Toggle(); Assert(!timer.Finished && timer.Running, "Starting again kept the finish.");
            Assert(FocusTimer.SessionsText(0, 0) == "" && FocusTimer.SessionsText(1, 25) == "1 session today  ·  25 m" && FocusTimer.SessionsText(3, 100) == "3 sessions today  ·  1 h 40 m", "Session text is wrong.");
            var settings = new AppSettings { FocusCustomMinutes = 3, FocusSessionsToday = -2 }; settings.Normalize();
            Assert(settings.FocusCustomMinutes == 5 && settings.FocusSessionsToday == 0 && new AppSettings().FocusChime, "Focus settings don't normalize or default.");
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
        Check("Mixer lists apps on every output once, where they play", () => {
            Func<string, string, uint, bool, AudioSession> make = (id, device, pid, active) => new AudioSession { Id = id, Name = id, DeviceId = device, ProcessId = pid, Active = active };
            var sessions = new[] {
                make("system-a", "A", 0, false), make("system-b", "B", 0, false),
                make("moved-old", "a", 10, false), make("moved-new", "B", 10, true),
                make("elsewhere", "B", 20, true),
                make("routed-old", "A", 30, false), make("routed-idle", "B", 30, false),
                make("default-idle", "A", 40, false), make("default-old", "B", 40, false),
                make("same-1", "A", 50, true), make("same-2", "A", 50, false),
            };
            var lookups = new System.Collections.Generic.List<uint>();
            var visible = AudioService.VisibleSessions(sessions, "A", pid => { lookups.Add(pid); return pid == 30 ? "b" : null; }).Select(s => s.Id).ToArray();
            Assert(String.Join(",", visible) == "system-a,moved-new,elsewhere,routed-idle,default-idle,same-1,same-2", "Unexpected sessions: " + String.Join(",", visible));
            Assert(lookups.Count == 2 && lookups.Contains(30) && lookups.Contains(40), "Routing looked up for apps that did not need it.");
            Assert(AudioService.VisibleSessions(sessions, "A", pid => { throw new InvalidOperationException(); }).Count == 7, "A failing routing lookup hid apps.");
            string endpoint = "{0.0.0.00000000}.{6a3b6a1e-1111-4c2d-9e0f-0123456789ab}";
            Assert(AudioService.PolicyDeviceId(endpoint) == @"\\?\SWD#MMDEVAPI#" + endpoint + "#{e6327cad-dcec-4949-ae8a-991e976a79d2}", "Routing device id is not in the policy format.");
            Assert(AudioService.EndpointId(AudioService.PolicyDeviceId(endpoint)) == endpoint && AudioService.EndpointId("") == null && AudioService.EndpointId(null) == null, "Routing device id does not round trip.");
        });
        Check("Mixer output buttons and picker keep touch targets inside the card", () => {
            string path = Path.Combine(directory, "mixer-outputs.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                    typeof(DashboardController).GetField("outputs", flags).SetValue(controller, new System.Collections.Generic.List<AudioDevice> {
                        new AudioDevice { Id = "a", Name = "Speakers", FormFactor = 1, IsDefault = true }, new AudioDevice { Id = "b", Name = "Headphones", FormFactor = 3 } });
                    var update = typeof(DashboardController).GetMethod("UpdateSessions", flags);
                    Func<string, string, string, uint, AudioSession> session = (id, name, device, pid) => new AudioSession { Id = id, Name = name, DeviceId = device, ProcessId = pid, Active = true, Volume = .5f };
                    update.Invoke(controller, new object[] { new System.Collections.Generic.List<AudioSession> { session("1", "Spotify", "b", 5), session("2", "Discord", "a", 6) } });
                    var dashboard = (System.Windows.FrameworkElement)window.FindName("Dashboard");
                    Action layout = () => { dashboard.Measure(new System.Windows.Size(1920, 720)); dashboard.Arrange(new System.Windows.Rect(0, 0, 1920, 720)); dashboard.UpdateLayout(); };
                    layout();
                    var panel = (System.Windows.Controls.StackPanel)window.FindName("SessionPanel");
                    var spotifyRow = (System.Windows.Controls.Grid)panel.Children[0];
                    Func<System.Windows.Controls.Grid, System.Windows.Controls.Button> outputOf = row => row.Children.OfType<System.Windows.Controls.Button>().Last();
                    Func<System.Windows.DependencyObject, string> nameOf = element => System.Windows.Automation.AutomationProperties.GetName(element);
                    var output = outputOf(spotifyRow);
                    Assert(output.ActualHeight >= 48 && output.ActualWidth >= 48, "App output button is smaller than 48.");
                    Assert(nameOf(output) == "Output for Spotify: Headphones" && (string)output.Content == "\uE7F6", "App output button does not show its device.");
                    var label = spotifyRow.Children.OfType<System.Windows.Controls.TextBlock>().First();
                    Assert(new System.Windows.Documents.TextRange(label.ContentStart, label.ContentEnd).Text.Contains("Headphones"), "App on another output is not labelled with it.");
                    Assert(nameOf(outputOf((System.Windows.Controls.Grid)panel.Children[1])) == "Output for Discord: Speakers", "Default-output app button is wrong.");
                    update.Invoke(controller, new object[] { new System.Collections.Generic.List<AudioSession> { session("2", "Discord", "a", 6), session("3", "Spotify", "a", 5) } });
                    // The row is reused for the moved app, and rows follow the reading's order.
                    Assert(panel.Children.Count == 2 && panel.Children[1] == spotifyRow && nameOf(outputOf(spotifyRow)) == "Output for Spotify: Speakers", "A moved app lost its row, or rows ignore the reading's order.");
                    var master = (System.Windows.Controls.Button)window.FindName("OutputDeviceButton");
                    Assert(master.ActualHeight >= 48, "Master output button is shorter than 48.");

                    var picker = new OutputPicker(window); OutputChoice chosen = null;
                    picker.Show("APP OUTPUT", "Spotify", new[] { new OutputChoice { Label = "Windows default", Selected = true }, new OutputChoice { Id = "a", Label = "Speakers" }, new OutputChoice { Id = "b", Label = "Headphones", Detail = "Playing here now" } }, true, "Note", choice => chosen = choice);
                    layout();
                    var card = (System.Windows.FrameworkElement)window.FindName("MixerCard"); var overlay = (System.Windows.FrameworkElement)window.FindName("OutputPicker");
                    var options = ((System.Windows.Controls.StackPanel)window.FindName("OutputOptions")).Children.OfType<System.Windows.Controls.Button>().ToArray();
                    Assert(picker.IsOpen && options.Length == 3 && options.All(b => b.ActualHeight >= 52), "Output choices are missing or shorter than 52.");
                    Assert(((System.Windows.FrameworkElement)window.FindName("OutputPickerClose")).ActualHeight >= 48, "Picker close button is shorter than 48.");
                    Assert(overlay.TransformToAncestor(card).Transform(new System.Windows.Point(0, overlay.ActualHeight)).Y <= card.ActualHeight, "Output picker escapes the mixer card.");
                    Assert(nameOf(options[0]) == "Windows default, selected" && nameOf(options[1]) == "Speakers", "Selected output is not marked.");
                    options[0].RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Assert(!picker.IsOpen && chosen == null, "Choosing the current output should only close the picker.");
                    picker.Show("APP OUTPUT", "Spotify", new[] { new OutputChoice { Label = "Windows default", Selected = true }, new OutputChoice { Id = "b", Label = "Headphones" } }, true, null, choice => chosen = choice);
                    ((System.Windows.Controls.StackPanel)window.FindName("OutputOptions")).Children.OfType<System.Windows.Controls.Button>().Last().RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    Assert(!picker.IsOpen && chosen != null && chosen.Id == "b", "Choosing an output did not apply it.");
                }
                window.Close();
            }
        });
        Check("Mixer orders playing apps first and System sounds last", () => {
            Func<string, uint, bool, AudioSession> make = (id, pid, active) => new AudioSession { Id = id, Name = id, ProcessId = pid, Active = active };
            var ordered = AudioService.Ordered(new System.Collections.Generic.List<AudioSession> { make("system", 0, false), make("idle-a", 1, false), make("playing-a", 2, true), make("idle-b", 3, false), make("playing-b", 4, true) }).Select(s => s.Id).ToArray();
            Assert(String.Join(",", ordered) == "playing-a,playing-b,idle-a,idle-b,system", "Unexpected order: " + String.Join(",", ordered));
        });
        Check("Audio work runs on its own thread and caches output names", () => {
            using (var worker = new AudioWorker())
            {
                var thread = worker.Run(a => Thread.CurrentThread.ManagedThreadId);
                Assert(thread.Wait(5000) && thread.Result != Thread.CurrentThread.ManagedThreadId, "Audio ran on the calling thread.");
                var read = worker.Run(a => a.Read());
                Assert(read.Wait(10000) && read.Result.Volume >= 0 && read.Result.Volume <= 1, "Audio read through the worker failed.");
                var again = worker.Run(a => a.Read());
                Assert(again.Wait(10000) && again.Result.Devices.Count == read.Result.Devices.Count && again.Result.Devices.Select(d => d.Name).SequenceEqual(read.Result.Devices.Select(d => d.Name)), "Cached output names differ from the first read.");
                Console.WriteLine("  Outputs through the worker: " + read.Result.Devices.Count + "; routing " + (read.Result.CanRouteApps ? "available" : "unavailable"));
            }
        });
        Check("Live audio read (does not change volume)", () => {
            using (var audio = new AudioService()) { var result = audio.Read(); Assert(result.Volume >= 0 && result.Volume <= 1, "Master volume invalid."); Console.WriteLine("  Output: " + result.Device + "; sessions: " + result.Sessions.Count); }
        });
        Check("Live output list and app routing lookup (reads only)", () => {
            using (var audio = new AudioService())
            {
                var result = audio.Read();
                Assert(result.Devices.Count >= 1 && result.Devices.Count(d => d.IsDefault) == 1 && result.Devices.First(d => d.IsDefault).Id == result.DeviceId, "Output list does not contain exactly one default.");
                Assert(result.Sessions.All(s => result.Devices.Any(d => String.Equals(d.Id, s.DeviceId, StringComparison.OrdinalIgnoreCase))), "A session belongs to an unlisted output.");
                string routed = audio.CanRouteApps ? audio.AppOutput((uint)System.Diagnostics.Process.GetCurrentProcess().Id) : null;
                Console.WriteLine("  Outputs: " + result.Devices.Count + "; per-app routing " + (audio.CanRouteApps ? "available" : "unavailable") + "; this process follows " + (routed ?? "the default"));
            }
        });
        Check("Now Playing names any media app and finds its mixer row", () => {
            Assert(MediaService.SourceName("Spotify.exe") == "Spotify" && MediaService.SourceName("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") == "Spotify", "Spotify not named.");
            Assert(MediaService.SourceName("MSEdge") == "Edge" && MediaService.SourceName("Chrome") == "Chrome" && MediaService.SourceName("308046B0AF4A39CB") == "Firefox" && MediaService.SourceName("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic") == "Media Player", "Known apps not named.");
            Assert(MediaService.SourceName("Contoso.Player_abc123def456!App") == "Player" && MediaService.SourceName(@"C:\Tools\player.exe") == "player" && MediaService.SourceName("") == "" && MediaService.SourceName(null) == "", "Unknown apps not tidied: " + MediaService.SourceName("Contoso.Player_abc123def456!App"));
            var sessions = new System.Collections.Generic.List<AudioSession> { new AudioSession { Id = "sys", Name = "System sounds", ProcessId = 0 }, new AudioSession { Id = "s", Name = "Spotify", ProcessId = 5 }, new AudioSession { Id = "c", Name = "chrome", ProcessId = 6 } };
            Assert(DashboardController.MatchSession(sessions, "Spotify").Id == "s" && DashboardController.MatchSession(sessions, "Chrome").Id == "c" && DashboardController.MatchSession(sessions, "VLC") == null && DashboardController.MatchSession(sessions, "") == null, "Mixer row not matched to the playing app.");
            var scrubber = new PlaybackScrubber(new System.Windows.Controls.Slider(), (key, position) => { }, position => { });
            scrubber.Update("song", 10000, 200000, true); scrubber.Advance(1000); scrubber.Advance(1000);
            Assert(((System.Windows.Controls.Slider)typeof(PlaybackScrubber).GetField("slider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scrubber)).Value == 12000, "The scrubber didn't advance between readings.");
            scrubber.Update("song", 250000, 200000, true); scrubber.Advance(1000);
            Assert(((System.Windows.Controls.Slider)typeof(PlaybackScrubber).GetField("slider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(scrubber)).Value == 200000, "The scrubber ran past the end.");
        });
        Check("Windows media session access (does not control playback)", () => {
            var task = new MediaService().ReadAsync(); Assert(task.Wait(10000), "Media read timed out."); var result = task.Result; Console.WriteLine(result.Available ? "  A media session is available." : "  No media session is active; empty state is expected.");
        });
        Console.WriteLine(failures == 0 ? "All checks passed." : failures + " check(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
