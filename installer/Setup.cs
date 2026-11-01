using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WidgetScreenSetup
{
    // Widget Screen setup: one exe that installs, updates and (copied into the install folder) uninstalls.
    //   WidgetScreen-Setup-vX.Y.Z.exe                 install or update, with a window
    //   ... /S [--no-startup] [--no-sensors] [--launch]  install silently (--no-sensors leaves the hardware sensors out)
    //   ... /uninstall [/S] [--remove-settings]       uninstall (what Installed apps runs)
    //   ... --root <folder>                           sandbox every path and the registry key (automated checks)
    // Silent runs return 0 on success and 1 on failure, with the reason in %TEMP%\WidgetScreen-Setup.log.
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool silent = false, uninstall = false, noStartup = false, launch = false, removeSettings = false, noSensors = false;
            string root = null;
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                if (arg == "/s" || arg == "--silent") silent = true;
                else if (arg == "/uninstall" || arg == "--uninstall") uninstall = true;
                else if (arg == "--no-startup") noStartup = true;
                else if (arg == "--launch") launch = true;
                else if (arg == "--no-sensors") noSensors = true;
                else if (arg == "--remove-settings") removeSettings = true;
                else if (arg == "--root" && i + 1 < args.Length) root = args[++i];
            }
            var installer = new Installer(root);
            if (silent)
            {
                try
                {
                    if (uninstall) installer.Uninstall(removeSettings, null);
                    else { installer.Install(!noStartup, !noSensors, null); if (launch) installer.Launch(); }
                    return 0;
                }
                catch (Exception error)
                {
                    try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "WidgetScreen-Setup.log"), DateTime.Now.ToString("s") + " " + error + Environment.NewLine); } catch { }
                    return 1;
                }
            }
            var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
            return application.Run(new SetupWindow(installer, uninstall));
        }
    }

    // The setup window, in the dashboard's Midnight palette with the Lime accent and 52 px touch targets.
    sealed class SetupWindow : Window
    {
        static readonly Brush Page = Hex("#0C0E13"), Card = Hex("#161A22"), Raised = Hex("#252B36"), Border = Hex("#353D49"),
            Text = Hex("#F2F4F8"), Muted = Hex("#A0AABB"), Accent = Hex("#BAF28B"), OnAccent = Hex("#11170E"), Warning = Hex("#F5C47D");

        readonly Installer installer;
        readonly bool uninstall;
        readonly TextBlock heading, body, status;
        readonly StackPanel options;
        readonly StackPanel buttons;
        CheckBox startup, open, removeSettings, sensors;

        public SetupWindow(Installer installer, bool uninstall)
        {
            this.installer = installer; this.uninstall = uninstall;
            Title = uninstall ? "Uninstall Widget Screen" : "Widget Screen setup";
            Width = 600; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = Page; FontFamily = new FontFamily("Segoe UI"); FontSize = 15; Foreground = Text;
            UseLayoutRounding = true;

            var column = new StackPanel { Margin = new Thickness(32, 28, 32, 28) };
            heading = new TextBlock { FontFamily = new FontFamily("Segoe UI Light"), FontSize = 30, Foreground = Text, TextWrapping = TextWrapping.Wrap };
            body = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), LineHeight = 22 };
            options = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
            status = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 14, 0, 0), Visibility = Visibility.Collapsed };
            buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            column.Children.Add(heading); column.Children.Add(body); column.Children.Add(options); column.Children.Add(status); column.Children.Add(buttons);
            Content = new Border { Background = Card, BorderBrush = Border, BorderThickness = new Thickness(0, 1, 0, 0), Child = column };

            if (uninstall) ShowUninstall(); else ShowInstall();
            // A dark title bar to match (Windows 10 20H1 and later; older versions keep the light one).
            SourceInitialized += (s, e) =>
            {
                int on = 1;
                try { DwmSetWindowAttribute(new System.Windows.Interop.WindowInteropHelper(this).Handle, 20, ref on, sizeof(int)); } catch { }
            };
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        void ShowInstall()
        {
            string installed = installer.InstalledVersion();
            bool update = installed != null;
            heading.Text = update ? "Update Widget Screen" : "Install Widget Screen";
            body.Text = (update ? "Version " + installed + " is installed. This replaces it with version " + SetupInfo.Version + ". Your settings, sign-ins and paired phones stay as they are."
                                : "Version " + SetupInfo.Version + ", for you only. No administrator rights are needed.")
                + "\nIt goes in " + installer.InstallDir + ", with a Start menu shortcut and an entry in Settings → Apps → Installed apps for uninstalling.";
            startup = Check("Start Widget Screen when I sign in to Windows", update ? installer.StartsWithWindows() : true);
            open = Check("Open Widget Screen when setup finishes", true);
            options.Children.Add(startup);
            // The extra packages (user request, 1 Nov 2026): said plainly, on by default for a new install, kept as chosen on an update.
            if (Installer.HasSensors)
            {
                sensors = Check("Install hardware sensors (LibreHardwareMonitor)", update ? installer.SensorsInstalled() : true);
                sensors.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty, "Adds extra packages for CPU temperatures.");
                options.Children.Add(sensors);
                options.Children.Add(new TextBlock
                {
                    Text = "Extra packages: LibreHardwareMonitor " + SetupInfo.SensorsVersion + " (LibreHardwareMonitorLib.dll, HidSharp.dll and " + (Installer.SensorLibraries() - 2) + " libraries they need, about "
                        + (Installer.SensorBytes() / 1048576.0).ToString("0.0") + " MB), under the MPL-2.0 licence (HidSharp Apache-2.0, the .NET parts MIT), with their licence texts and a third-party notice. Widget Screen uses them for CPU temperatures; "
                        + "without them it still shows GPU temperature and video memory. CPU temperatures also need the PawnIO driver, which setup doesn't install.",
                    Foreground = Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(44, 0, 0, 8)
                });
            }
            options.Children.Add(open);
            var others = installer.OtherStartupShortcuts();
            if (others.Length > 0)
                Say("Another startup shortcut also opens Widget Screen (" + string.Join(", ", others.Select(Path.GetFileName)) + "). Delete it from the Startup folder so the app doesn't open twice.", Warning);
            if (installer.RunningCopies().Length > 0) Say("Widget Screen is running. Setup will close it first.", Muted);
            SetButtons(Primary(update ? "Update" : "Install", Install), Secondary("Cancel", Close));
        }

        void ShowUninstall()
        {
            string installed = installer.InstalledVersion();
            heading.Text = "Uninstall Widget Screen";
            body.Text = installed == null
                ? "Widget Screen isn't installed for this account. Uninstalling removes any shortcuts and files that are left."
                : "This removes version " + installed + ", its Start menu and startup shortcuts, and its Installed apps entry.";
            removeSettings = Check("Also delete my settings, Spotify and Discord sign-ins and paired phones", false);
            options.Children.Add(removeSettings);
            options.Children.Add(new TextBlock { Text = "They're in " + installer.SettingsDir + ". Keep them to pick up where you left off if you install again.", Foreground = Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(44, 0, 0, 0) });
            if (installer.RunningCopies().Length > 0) Say("Widget Screen is running. Uninstalling will close it first.", Muted);
            SetButtons(Primary("Uninstall", Uninstall), Secondary("Cancel", Close));
        }

        async void Install()
        {
            bool startWithWindows = startup.IsChecked == true, openAfter = open.IsChecked == true, withSensors = sensors == null || sensors.IsChecked == true;
            Busy();
            try
            {
                await Task.Run(() => installer.Install(startWithWindows, withSensors, Progress));
                if (openAfter) installer.Launch();
                heading.Text = "Widget Screen is installed";
                body.Text = "Find it in the Start menu" + (startWithWindows ? ". It also starts when you sign in." : ".") + " To remove it, use Settings → Apps → Installed apps.";
                Done();
            }
            catch (Exception error) { Failed(error); SetButtons(Primary("Try again", Install), Secondary("Close", Close)); }
        }

        async void Uninstall()
        {
            bool alsoSettings = removeSettings.IsChecked == true;
            Busy();
            try
            {
                await Task.Run(() => installer.Uninstall(alsoSettings, Progress));
                heading.Text = "Widget Screen is uninstalled";
                body.Text = alsoSettings ? "Its settings, sign-ins and paired phones were deleted too." : "Your settings are still in " + installer.SettingsDir + ", ready if you install it again.";
                Done();
            }
            catch (Exception error) { Failed(error); SetButtons(Primary("Try again", Uninstall), Secondary("Close", Close)); }
        }

        void Progress(string text) { Dispatcher.BeginInvoke(new Action(() => Say(text, Muted))); }

        void Busy() { options.IsEnabled = false; foreach (UIElement button in buttons.Children) button.IsEnabled = false; }

        void Done()
        {
            options.Visibility = Visibility.Collapsed; status.Visibility = Visibility.Collapsed;
            SetButtons(Primary("Close", Close));
        }

        void Failed(Exception error)
        {
            options.IsEnabled = true;
            Say("Setup couldn't finish: " + error.Message, Warning);
        }

        void Say(string text, Brush color) { status.Text = text; status.Foreground = color; status.Visibility = Visibility.Visible; }

        void SetButtons(params Button[] list)
        {
            buttons.Children.Clear();
            for (int i = 0; i < list.Length; i++) { if (i > 0) list[i].Margin = new Thickness(12, 0, 0, 0); buttons.Children.Add(list[i]); }
            list[0].IsDefault = true; list[0].Focus();
        }

        static CheckBox Check(string text, bool on)
        {
            var box = new CheckBox { IsChecked = on, Foreground = Text, MinHeight = 52, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(12, 0, 0, 0) };
            box.Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
            return box;
        }

        static Button Primary(string text, Action click) { return MakeButton(text, Accent, OnAccent, Accent, click); }
        static Button Secondary(string text, Action click) { return MakeButton(text, Raised, Text, Border, click); }

        // A flat rounded button: the system's chrome would paint blue hover states over the palette.
        static Button MakeButton(string text, Brush background, Brush foreground, Brush border, Action click)
        {
            var template = new ControlTemplate(typeof(Button));
            var frame = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            frame.SetValue(System.Windows.Controls.Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            frame.SetValue(System.Windows.Controls.Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            frame.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new Thickness(1));
            frame.SetValue(System.Windows.Controls.Border.CornerRadiusProperty, new CornerRadius(12));
            frame.SetValue(System.Windows.Controls.Border.PaddingProperty, new Thickness(24, 0, 24, 0));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            frame.AppendChild(content);
            template.VisualTree = frame;
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            template.Triggers.Add(disabled);
            var button = new Button { Content = text, Template = template, Background = background, Foreground = foreground, BorderBrush = border, MinHeight = 52, MinWidth = 140, FontSize = 16, Cursor = System.Windows.Input.Cursors.Hand };
            button.Click += (s, e) => click();
            return button;
        }

        static Brush Hex(string value) { var brush = (Brush)new BrushConverter().ConvertFromString(value); brush.Freeze(); return brush; }
    }
}
