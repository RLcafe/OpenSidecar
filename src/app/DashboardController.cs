using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;

namespace WidgetScreen
{
    public sealed class DashboardController : IDisposable
    {
        private readonly Window window;
        private readonly string settingsPath;
        private readonly bool preview;
        private AppSettings settings;
        private AudioService audio;
        private readonly SystemService system = new SystemService();
        private readonly MediaService media = new MediaService();
        private readonly SpotifyWidget spotify;
        private readonly SpotifyAccount spotifyAccount;
        private readonly PlaybackScrubber mediaScrubber;
        private readonly WeatherService weather = new WeatherService();
        private readonly FocusTimer focus = new FocusTimer();
        private readonly LevelVisualizer levels = new LevelVisualizer();
        private readonly OutputPicker outputPicker;
        private List<AudioDevice> outputs = new List<AudioDevice>();
        private readonly DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer levelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly Dictionary<string, SessionRow> sessionRows = new Dictionary<string, SessionRow>();
        private readonly List<double> cpuHistory = new List<double>();
        private readonly DeckEditor deckEditor;
        private readonly DeckHotkeys deckHotkeys;
        private int deckPage;
        public LayoutEditor Editor { get; private set; }
        private bool syncingEditor, shortcutsCompact;
        private int selectedFocusPreset = -1;
        private sealed class WidgetOption
        {
            public string Id;
            public override string ToString() { return WidgetLayout.Title(Id); }
        }
        private bool openingSettings, systemBusy;
        private bool updatingAudio, audioAvailable, mediaBusy, mediaCommandBusy, weatherBusy, disposed, fullscreen;
        private int ticks, weatherGeneration, settingsGeneration;
        private DateTime lastWeatherAttempt = DateTime.MinValue;
        private WeatherSnapshot lastWeather;
        private WeatherLocation pendingLocation;
        private string pendingMessage;

        private sealed class SessionRow { public Grid Root; public Slider Slider; public Button Mute, Output; public TextBlock Percent, Label; public AudioSession Session; public string Shown; }
        private sealed class MonitorOption
        {
            public string Id, Label;
            public override string ToString() { return Label; }
        }

        public DashboardController(Window window, string settingsPath, bool preview)
        {
            this.window = window; this.settingsPath = settingsPath; this.preview = preview;
            string warning; settings = SettingsStore.Load(settingsPath, out warning); pendingMessage = warning;
            Control<Border>("LevelHost").Child = levels;
            Editor = new LayoutEditor(window);
            outputPicker = new OutputPicker(window);
            Editor.Changed += UpdateEditor;
            // Account state belongs to the settings directory, never to a build/output folder.
            var spotifyService = new SpotifyService(SettingsStore.SpotifyPath(settingsPath));
            spotify = new SpotifyWidget(window, spotifyService);
            if (warning == null && String.IsNullOrEmpty(settings.SpotifyClientId) && SpotifyService.ValidClientId(spotifyService.ClientId))
            {
                settings.SpotifyClientId = spotifyService.ClientId;
                try { SettingsStore.Save(settingsPath, settings); } catch (Exception) { pendingMessage = "Could not save the Spotify Client ID. Check Settings."; }
            }
            spotifyAccount = new SpotifyAccount(window, spotifyService, settings.SpotifyClientId, value => {
                string previous = settings.SpotifyClientId; settings.SpotifyClientId = value;
                try { SettingsStore.Save(settingsPath, settings); } catch { settings.SpotifyClientId = previous; throw; }
            }, () => { spotify.AccountChanged(); spotify.Tick(Editor.IsVisible("Spotify"), settings.QuietMode); });
            mediaScrubber = new PlaybackScrubber(Control<Slider>("MediaProgress"), SeekMedia, value => Text("MediaPosition", FormatTime(TimeSpan.FromMilliseconds(value))));
            if (settings.WidgetLayout == null && !settings.SwapSideWidgets && settings.ClockWidth == 1 && settings.MediaWidth == 3 && settings.MixerWidth == 3 && settings.SystemWidth == 2
                && settings.ClockHeight == 246 && settings.MediaHeight == 326 && settings.MixerHeight == 576 && settings.SystemHeight == 326)
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings);
            deckEditor = new DeckEditor(window, message => Text("SettingsStatus", message));
            deckHotkeys = new DeckHotkeys();
            BindActions();
            ApplySettings(); UpdateFocus();
            clockTimer.Tick += Tick;
            levelTimer.Tick += (sender, e) => { if (audio != null && window.WindowState != WindowState.Minimized) levels.Push(audio.Peak()); };
            window.Loaded += Loaded;
            window.PreviewKeyDown += KeyDown;
            window.StateChanged += (sender, e) => UpdateLevelTimer();
            SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        }

        private T Control<T>(string name) where T : class
        {
            var control = window.FindName(name) as T;
            if (control == null) throw new InvalidOperationException("Missing UI element: " + name);
            return control;
        }
        private void Text(string name, string value) { Control<TextBlock>(name).Text = value; }
        private void Button(string name, Action action) { Control<Button>(name).Click += (sender, e) => action(); }
        private void Notify(string text) { Text("StatusMessage", text); }

        private void BindActions()
        {
            Button("CloseButton", () => window.Close());
            Button("SettingsButton", OpenSettings);
            Button("EditDeck", OpenDeckSettings);
            Button("EmptyDeck", OpenDeckSettings);
            Button("DeckPrevious", () => { deckPage--; BuildShortcuts(); });
            Button("DeckNext", () => { deckPage++; BuildShortcuts(); });
            Button("SpotifyAccount", () => { OpenSettings(); Control<TabControl>("SettingsTabs").SelectedIndex = 4; });
            Button("SpotifyLayout", () => { CloseSettings(); BeginLayoutEdit(); Editor.ArrangeSpotify(); });
            Button("EditLayoutButton", BeginLayoutEdit);
            Button("OpenLayoutEditor", () => { CloseSettings(); BeginLayoutEdit(); });
            Button("UndoLayoutButton", () => Editor.Undo());
            Button("RedoLayoutButton", () => Editor.Redo());
            Button("ResetLayoutButton", () => Editor.Reset());
            Button("CancelLayoutButton", CancelLayoutEdit);
            Button("SaveLayoutButton", SaveLayoutEdit);
            Control<ComboBox>("EditWidgetChoice").ItemsSource = WidgetLayout.Ids.Select(id => new WidgetOption { Id = id }).ToArray();
            Control<ComboBox>("EditWidgetChoice").SelectionChanged += (sender, e) => {
                if (syncingEditor || !Editor.Editing) return;
                var selected = Control<ComboBox>("EditWidgetChoice").SelectedItem as WidgetOption;
                if (selected != null) Editor.Select(selected.Id);
            };
            Control<CheckBox>("VisibleWidgetChoice").Click += (sender, e) => {
                if (!syncingEditor && Editor.Editing) Editor.SetVisible(Editor.SelectedId, Control<CheckBox>("VisibleWidgetChoice").IsChecked == true);
            };
            Control<CheckBox>("SnapLayoutChoice").Click += (sender, e) => Editor.Snap = Control<CheckBox>("SnapLayoutChoice").IsChecked == true;
            Button("CancelSettings", CloseSettings);
            Button("CancelSettingsFooter", CloseSettings);
            Button("SaveSettings", SaveSettings);
            Button("FullscreenButton", ToggleFullscreen);
            Button("QuietButton", () => {
                settings.QuietMode = !settings.QuietMode; ApplySettings(); Persist();
                Notify(settings.QuietMode ? "Quiet mode is on. Live levels are paused; system and media updates are less frequent." : "Quiet mode is off. Normal refresh rates restored.");
            });
            Button("FocusStart", () => { focus.Toggle(); UpdateFocus(); });
            Button("FocusReset", () => { focus.Reset((int)focus.Duration.TotalMinutes); UpdateFocus(); });
            Button("Focus25", () => SetFocus(25)); Button("Focus50", () => SetFocus(50)); Button("Focus5", () => SetFocus(5));
            Button("MuteButton", () => AudioAction(() => audio.ToggleMute()));
            Button("MicrophoneButton", () => AudioAction(() => audio.ToggleMicrophone()));
            Button("SoundSettingsButton", () => Launch("ms-settings:sound"));
            Button("OutputDeviceButton", OpenMasterOutputs);
            foreach (string name in new[] { "MasterSlider", "ScaleChoice", "CornersChoice" }) TouchSlider.Attach(Control<Slider>(name));
            Control<Slider>("MasterSlider").ValueChanged += (sender, e) => {
                if (!updatingAudio && audio != null) { AudioAction(() => audio.SetVolume(e.NewValue / 100), false); Text("MasterPercent", Math.Round(e.NewValue).ToString() + "%"); }
            };
            Button("PreviousButton", () => MediaCommand("previous")); Button("PlayButton", () => MediaCommand("toggle")); Button("NextButton", () => MediaCommand("next"));
            Button("WeatherButton", () => {
                if (String.IsNullOrWhiteSpace(settings.WeatherLocation))
                {
                    OpenSettings(); Control<TabControl>("SettingsTabs").SelectedIndex = 3; window.UpdateLayout(); Control<TextBox>("CitySearch").BringIntoView(); Control<TextBox>("CitySearch").Focus();
                }
                else RefreshWeather();
            });
            Button("SearchCityButton", () => SearchCity());
            Button("ClearCityButton", () => { pendingLocation = null; Control<ComboBox>("CityResults").ItemsSource = null; Text("SettingsStatus", "Weather will be turned off when you save."); });
            Control<ComboBox>("CityResults").SelectionChanged += (sender, e) => { var selected = Control<ComboBox>("CityResults").SelectedItem as WeatherLocation; if (selected != null) pendingLocation = selected; };
            Control<Slider>("ScaleChoice").ValueChanged += (sender, e) => Text("ScaleLabel", Math.Round(e.NewValue) + "%");
            Control<ComboBox>("ThemeChoice").SelectionChanged += (sender, e) => PreviewAppearance();
            Control<ComboBox>("AccentChoice").SelectionChanged += (sender, e) => PreviewAppearance();
            Control<Slider>("CornersChoice").ValueChanged += (sender, e) => PreviewAppearance();
            Control<Slider>("ScaleChoice").ValueChanged += (sender, e) => PreviewAppearance();
        }

        private void Loaded(object sender, RoutedEventArgs e)
        {
            if (!preview && settings.StartFullscreen && DisplayService.Fullscreen(window, settings.Monitor)) fullscreen = true;
            else DisplayService.Windowed(window);
            UpdateDisplayStatus();
            UpdateClock(); UpdateFocus(); RefreshAudio(); RefreshSystem(); RefreshMedia(); RefreshWeather();
            clockTimer.Start(); UpdateLevelTimer();
            if (!String.IsNullOrEmpty(pendingMessage)) Notify(pendingMessage);
        }

        private void Tick(object sender, EventArgs e)
        {
            ticks++; UpdateClock(); UpdateFocus();
            spotify.Tick(window.WindowState != WindowState.Minimized && Editor.IsVisible("Spotify") && !Editor.Editing, settings.QuietMode);
            if (window.WindowState == WindowState.Minimized) return;
            int interval = settings.QuietMode ? 5 : 2;
            if (ticks % interval == 0)
            {
                RefreshAudio();
                if (Editor.IsVisible("System") && (settings.ShowCpu || settings.ShowGpu || settings.ShowRam || settings.ShowNetwork || settings.ShowUptime)) RefreshSystem();
            }
            if (ticks % (settings.QuietMode ? 6 : 3) == 0) RefreshMedia();
            if (!String.IsNullOrWhiteSpace(settings.WeatherLocation) && DateTime.UtcNow - lastWeatherAttempt >= TimeSpan.FromMinutes(15)) RefreshWeather();
        }

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            Text("ClockTime", now.ToString(settings.Use24HourClock ? "HH:mm" : "h:mm", System.Globalization.CultureInfo.InvariantCulture));
            Text("ClockPeriod", settings.Use24HourClock ? "LOCAL TIME  ·  24 HOUR" : now.ToString("tt", System.Globalization.CultureInfo.InvariantCulture) + "  ·  LOCAL TIME");
            Text("ClockDate", now.ToString("ddd, MMM d", System.Globalization.CultureInfo.InvariantCulture));
            Text("Greeting", now.Hour < 12 ? "GOOD MORNING" : now.Hour < 18 ? "GOOD AFTERNOON" : "GOOD EVENING");
        }
        private void SetFocus(int minutes) { focus.Reset(minutes); UpdateFocus(); }
        private void UpdateFocus()
        {
            bool finished = focus.FinishIfElapsed();
            TimeSpan remaining = focus.Remaining;
            Text("FocusTime", ((int)Math.Ceiling(remaining.TotalSeconds) / 60).ToString("00") + ":" + ((int)Math.Ceiling(remaining.TotalSeconds) % 60).ToString("00"));
            Text("FocusStatus", remaining == TimeSpan.Zero ? "Session complete. Take a breath." : focus.Running ? "Stay with it. You've got this." : focus.Progress > 0 ? "Paused. Pick up when you're ready." : "One thing at a time.");
            Control<ProgressBar>("FocusProgress").Value = focus.Progress;
            Control<Button>("FocusStart").Content = focus.Running ? "Pause" : focus.Progress > 0 && remaining > TimeSpan.Zero ? "Resume" : "Start focus";
            int minutes = (int)focus.Duration.TotalMinutes;
            if (minutes != selectedFocusPreset)
            {
                selectedFocusPreset = minutes;
                foreach (int preset in new[] { 25, 50, 5 })
                {
                    var button = Control<Button>("Focus" + preset); bool selected = preset == minutes;
                    button.SetResourceReference(System.Windows.Controls.Button.BorderBrushProperty, selected ? "Accent" : "ControlBorder");
                    button.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, selected ? "Selected" : "Raised");
                    button.BorderThickness = new Thickness(selected ? 2 : 1);
                    AutomationProperties.SetName(button, preset + " minutes" + (selected ? ", selected" : ""));
                }
            }
            if (finished) Notify("Focus session complete. Time for a break.");
        }

        private void ApplySettings()
        {
            ApplyAppearance(settings);
            ApplyLayout(settings);
            foreach (var row in sessionRows.Values) row.Label.FontSize = 16 * settings.TextScale;
            Control<Button>("QuietButton").Content = settings.QuietMode ? "Quiet mode: on" : "Quiet mode: off";
            Control<Button>("QuietButton").SetResourceReference(System.Windows.Controls.Button.BorderBrushProperty, settings.QuietMode ? "Accent" : "ControlBorder");
            Text("SystemStatus", settings.QuietMode ? "EVERY 5 SEC" : "EVERY 2 SEC");
            BuildShortcuts(); UpdateClock(); UpdateLevelTimer();
        }

        private void ApplyAppearance(AppSettings appearance)
        {
            DashboardAppearance.Apply(window, appearance);
            string selectedAccent = appearance.Accent;
            string color = selectedAccent == "Glacier" ? "#8ED8F8" : selectedAccent == "Lavender" ? "#C5AEFF" : selectedAccent == "Amber" ? "#F5C47D" : "#BAF28B";
            var accent = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); accent.Freeze(); window.Resources["Accent"] = accent;
            window.Resources["SmallSize"] = 15.0 * appearance.TextScale;
            window.Resources["BodySize"] = 18.0 * appearance.TextScale;
            window.Resources["TitleSize"] = 24.0 * appearance.TextScale;
            window.Resources["HeroSize"] = 76.0 * Math.Min(1.06, appearance.TextScale);
            levels.BarBrush = accent;
        }

        private void PreviewAppearance()
        {
            if (openingSettings || Control<Grid>("SettingsOverlay").Visibility != Visibility.Visible) return;
            var appearance = new AppSettings { Theme = (string)Control<ComboBox>("ThemeChoice").SelectedItem, Accent = (string)Control<ComboBox>("AccentChoice").SelectedItem,
                CornerRadius = Control<Slider>("CornersChoice").Value, TextScale = Control<Slider>("ScaleChoice").Value / 100 };
            ApplyAppearance(appearance);
            Editor.Refresh();
            Text("CornersLabel", appearance.CornerRadius == 0 ? "Square" : Math.Round(appearance.CornerRadius) + " px");
        }

        private void ApplyLayout(AppSettings layout)
        {
            Editor.Apply(layout);
            Control<FrameworkElement>("CpuSection").Visibility = layout.ShowCpu ? Visibility.Visible : Visibility.Collapsed;
            Control<FrameworkElement>("GpuSection").Visibility = layout.ShowGpu ? Visibility.Visible : Visibility.Collapsed;
            Control<FrameworkElement>("RamSection").Visibility = layout.ShowRam ? Visibility.Visible : Visibility.Collapsed;
            Control<FrameworkElement>("NetworkSection").Visibility = layout.ShowNetwork ? Visibility.Visible : Visibility.Collapsed;
            Control<FrameworkElement>("UptimeValue").Visibility = layout.ShowUptime ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BeginLayoutEdit()
        {
            Editor.Begin(); UpdateLevelTimer();
            Control<Button>("SaveLayoutButton").Focus();
        }
        private void CancelLayoutEdit()
        {
            Editor.Cancel(); UpdateLevelTimer(); UpdateDisplayStatus();
            Notify("Layout changes discarded."); Control<Button>("EditLayoutButton").Focus();
        }
        private void SaveLayoutEdit()
        {
            var previous = settings.WidgetLayout; bool previousSnap = settings.SnapWidgets;
            settings.WidgetLayout = Editor.Export(); settings.SnapWidgets = Editor.Snap;
            try { SettingsStore.Save(settingsPath, settings); }
            catch (Exception e)
            {
                settings.WidgetLayout = previous; settings.SnapWidgets = previousSnap;
                Notify("Could not save layout: " + e.Message + ". Your changes are still in the editor."); return;
            }
            Editor.Accept(); ApplySettings(); UpdateDisplayStatus();
            if (window.IsLoaded) { RefreshSystem(); RefreshAudio(); RefreshMedia(); RefreshWeather(); }
            Notify("Layout saved. Your widgets are ready."); Control<Button>("EditLayoutButton").Focus();
        }
        private void UpdateEditor()
        {
            if (syncingEditor) return;
            syncingEditor = true;
            try
            {
                bool editing = Editor.Editing;
                if (editing) outputPicker.Close();
                Control<Grid>("EditToolbar").Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
                Control<StackPanel>("HeaderBrand").Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
                Control<StackPanel>("MainActions").Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
                if (editing)
                {
                    Control<Button>("UndoLayoutButton").IsEnabled = Editor.CanUndo && !Editor.GestureActive;
                    Control<Button>("RedoLayoutButton").IsEnabled = Editor.CanRedo && !Editor.GestureActive;
                    Control<CheckBox>("SnapLayoutChoice").IsChecked = Editor.Snap;
                    var choices = Control<ComboBox>("EditWidgetChoice");
                    choices.SelectedItem = choices.Items.Cast<WidgetOption>().FirstOrDefault(p => p.Id == Editor.SelectedId);
                    Control<CheckBox>("VisibleWidgetChoice").IsChecked = Editor.IsEnabled(Editor.SelectedId);
                    var bounds = Editor.Bounds(Editor.SelectedId);
                    string info = WidgetLayout.Title(Editor.SelectedId) + " · " + Math.Round(bounds.Width) + " × " + Math.Round(bounds.Height) + " · X " + Math.Round(bounds.X) + ", Y " + Math.Round(bounds.Y);
                    if (Editor.SelectedId == "System" && !Editor.IsVisible("System") && Editor.IsEnabled("System")) info += " · Enable metrics in Settings to show system stats";
                    else if (!Editor.IsEnabled(Editor.SelectedId)) info += " · Hidden · Check Show widget to restore";
                    if (Editor.HasOverlaps()) info += " · Widgets overlap";
                    Notify(info);
                    Text("DisplayStatus", "ARROWS MOVE · SHIFT+ARROWS RESIZE · ESC CANCELS");
                }
                if (shortcutsCompact != (Editor.Bounds("Shortcuts").Height < 240)) BuildShortcuts();
                bool compactFocus = Editor.Bounds("Focus").Height < 320;
                Control<TextBlock>("FocusTime").FontSize = compactFocus ? 42 : 54;
                Control<TextBlock>("FocusStatus").Margin = compactFocus ? new Thickness(0, 2, 0, 8) : new Thickness(0, 6, 0, 12);
                Control<Grid>("FocusPresets").Margin = compactFocus ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 12);
                Control<TextBlock>("ClockTime").FontSize = Math.Min((double)window.Resources["HeroSize"], Math.Max(52, (Editor.Bounds("Clock").Height - 120) * .75));
                var city = Control<TextBlock>("WeatherCity"); bool compactWeather = Editor.Bounds("Weather").Height < 250;
                city.TextWrapping = compactWeather ? TextWrapping.NoWrap : TextWrapping.Wrap;
                city.MaxHeight = compactWeather ? 28 : 58;
            }
            finally { syncingEditor = false; }
        }

        private void UpdateLevelTimer()
        {
            bool enabled = audioAvailable && settings.ShowLevels && !settings.QuietMode && !Editor.Editing && Editor.IsVisible("Mixer") && window.WindowState != WindowState.Minimized;
            if (enabled) levelTimer.Start(); else { levelTimer.Stop(); levels.Clear(); }
            Text("LevelStatus", enabled ? "OUTPUT LEVEL  ·  LIVE" : !audioAvailable ? "OUTPUT LEVEL  ·  UNAVAILABLE" : Editor.Editing ? "OUTPUT LEVEL  ·  PAUSED WHILE EDITING" : settings.QuietMode ? "OUTPUT LEVEL  ·  PAUSED IN QUIET MODE" : "OUTPUT LEVEL  ·  OFF");
        }

        private void BuildShortcuts()
        {
            var grid = Control<System.Windows.Controls.Primitives.UniformGrid>("ShortcutGrid"); grid.Children.Clear();
            shortcutsCompact = Editor.Bounds("Shortcuts").Height < 240;
            int pageSize = shortcutsCompact ? 3 : 6;
            int pages = Math.Max(1, (settings.Shortcuts.Count + pageSize - 1) / pageSize);
            deckPage = Math.Max(0, Math.Min(pages - 1, deckPage));
            grid.Rows = shortcutsCompact ? 1 : 2;
            Control<StackPanel>("DeckPages").Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("DeckPrevious").IsEnabled = deckPage > 0; Control<Button>("DeckNext").IsEnabled = deckPage < pages - 1;
            Text("DeckPageLabel", (deckPage + 1) + "/" + pages);
            Control<Button>("EmptyDeck").Visibility = settings.Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var shortcut in settings.Shortcuts.Skip(deckPage * pageSize).Take(pageSize))
            {
                var button = new Button { Margin = new Thickness(4), Padding = new Thickness(8), ToolTip = DeckActions.Describe(shortcut), IsEnabled = !String.IsNullOrWhiteSpace(shortcut.Target) };
                bool compact = shortcutsCompact;
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Orientation = compact ? Orientation.Horizontal : Orientation.Vertical };
                var glyph = new TextBlock { Text = String.IsNullOrEmpty(shortcut.Glyph) ? "\uE8A7" : shortcut.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = compact ? 18 : 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); content.Children.Add(glyph);
                var name = new TextBlock { Text = shortcut.Label, FontSize = (compact ? 14 : 15) * settings.TextScale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = compact ? new Thickness(8, 0, 0, 0) : new Thickness(0, 8, 0, 0), MaxWidth = compact ? 70 : 138, TextTrimming = TextTrimming.CharacterEllipsis };
                content.Children.Add(name); button.Content = content; AutomationProperties.SetName(button, shortcut.Label);
                var action = shortcut; button.Click += (sender, e) => RunDeckAction(action); grid.Children.Add(button);
            }
        }
        private void OpenDeckSettings() { OpenSettings(); Control<TabControl>("SettingsTabs").SelectedIndex = 5; }
        private async void RunDeckAction(Shortcut shortcut)
        {
            try
            {
                string error = DeckActions.Validate(shortcut); if (error != null) throw new InvalidOperationException(error);
                if (DeckActions.NeedsConfirmation(shortcut) && MessageBox.Show(window, shortcut.Label + "\n\n" + DeckActions.Describe(shortcut) + "\n\nRun this action?", "Quick Launch", MessageBoxButton.OKCancel, MessageBoxImage.Question, MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
                if (shortcut.Action == "Hotkey") { await deckHotkeys.SendAsync(shortcut.Target); if (!disposed) Notify("Sent " + shortcut.Target + " to your previous app."); }
                else if (shortcut.Action == "Control")
                {
                    switch (shortcut.Target)
                    {
                        case "play": MediaCommand("toggle"); break;
                        case "previous": MediaCommand("previous"); break;
                        case "next": MediaCommand("next"); break;
                        case "mute": AudioAction(() => audio.ToggleMute()); break;
                        case "microphone": AudioAction(() => audio.ToggleMicrophone()); break;
                        case "volume-up": AudioAction(() => audio.SetVolume(audio.Read().Volume + .05)); break;
                        case "volume-down": AudioAction(() => audio.SetVolume(audio.Read().Volume - .05)); break;
                        case "focus": focus.Toggle(); UpdateFocus(); break;
                        case "quiet": settings.QuietMode = !settings.QuietMode; ApplySettings(); Persist(); break;
                        case "sound": Launch("ms-settings:sound"); break;
                    }
                }
                else { DeckActions.Open(shortcut); Notify("Opened " + shortcut.Label); }
            }
            catch (Exception e) { if (!disposed) Notify(shortcut.Label + ": " + e.Message); }
        }
        private void Launch(string target) { try { Launcher.Open(target); Notify("Opened " + target); } catch (Exception e) { Notify("Couldn't open shortcut: " + e.Message); } }

        private void RefreshAudio()
        {
            if (!Editor.IsVisible("Mixer")) return;
            updatingAudio = true;
            try
            {
                if (audio == null) audio = new AudioService();
                var snapshot = audio.Read();
                audioAvailable = true;
                Text("AudioDevice", snapshot.Device); Text("AudioStatus", "●  LIVE");
                outputs = snapshot.Devices;
                var outputButton = Control<Button>("OutputDeviceButton"); outputButton.IsEnabled = true;
                outputButton.ToolTip = "Output: " + snapshot.Device + ". Tap to choose another output.";
                AutomationProperties.SetName(outputButton, "Choose output device. Current: " + snapshot.Device);
                Control<TextBlock>("AudioStatus").SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                Text("MasterPercent", Math.Round(snapshot.Volume * 100).ToString() + "%");
                var slider = Control<Slider>("MasterSlider"); slider.IsEnabled = true;
                if (!slider.IsMouseCaptureWithin && !slider.AreAnyTouchesCapturedWithin) slider.Value = snapshot.Volume * 100;
                Control<Button>("MuteButton").IsEnabled = true;
                Control<Button>("MuteButton").Content = snapshot.Muted ? "\uE74F" : "\uE767";
                Control<Button>("MuteButton").ToolTip = snapshot.Muted ? "Unmute output" : "Mute output";
                AutomationProperties.SetName(Control<Button>("MuteButton"), snapshot.Muted ? "Unmute output" : "Mute output");
                var microphone = Control<Button>("MicrophoneButton"); microphone.IsEnabled = snapshot.MicrophoneMuted.HasValue;
                microphone.Content = !snapshot.MicrophoneMuted.HasValue ? "No default microphone" : snapshot.MicrophoneMuted.Value ? "Microphone muted  ·  tap to unmute" : "Microphone on  ·  tap to mute";
                microphone.Foreground = snapshot.MicrophoneMuted == true ? (Brush)window.Resources["Accent"] : (Brush)window.Resources["Text"];
                UpdateSessions(snapshot.Sessions);
                Text("NoSessions", snapshot.SessionsAvailable ? "Apps appear here when they use audio." : "App volumes are unavailable. Master volume still works.");
                if (!snapshot.SessionsAvailable) Text("SessionCount", "UNAVAILABLE");
            }
            catch (Exception)
            {
                audioAvailable = false;
                Text("AudioStatus", "UNAVAILABLE"); Text("AudioDevice", "Audio controls are unavailable"); Text("MasterPercent", "—");
                outputs = new List<AudioDevice>(); outputPicker.Close(); Control<Button>("OutputDeviceButton").IsEnabled = false;
                Control<TextBlock>("AudioStatus").SetResourceReference(TextBlock.ForegroundProperty, "Warning");
                Control<Slider>("MasterSlider").IsEnabled = false; Control<Button>("MuteButton").IsEnabled = false;
                Control<Button>("MicrophoneButton").IsEnabled = false; Control<Button>("MicrophoneButton").Content = "Audio device unavailable";
                UpdateSessions(new List<AudioSession>());
                Text("SessionCount", "—");
                Text("NoSessions", "Reconnect your audio output or open Windows sound settings using the gear above.");
            }
            finally { updatingAudio = false; UpdateLevelTimer(); }
        }

        private void UpdateSessions(List<AudioSession> sessions)
        {
            var panel = Control<StackPanel>("SessionPanel");
            var stale = sessionRows.Where(pair => !sessions.Any(s => s.Id == pair.Key)).ToList();
            foreach (var session in sessions)
            {
                SessionRow row;
                if (!sessionRows.TryGetValue(session.Id, out row))
                {
                    // An app moved to another output gets a new session there. Reuse its row so it stays in place.
                    var moved = stale.FirstOrDefault(pair => session.ProcessId != 0 && pair.Value.Session.ProcessId == session.ProcessId);
                    if (moved.Value != null) { stale.Remove(moved); sessionRows.Remove(moved.Key); row = moved.Value; }
                    else { row = CreateSessionRow(); panel.Children.Add(row.Root); }
                    sessionRows.Add(session.Id, row);
                }
                row.Session = session;
                var device = outputs.FirstOrDefault(d => SameOutput(d.Id, session.DeviceId));
                string deviceName = device != null ? device.Name : "Unknown output";
                bool elsewhere = device != null && !device.IsDefault;
                string shown = session.Name + "\n" + (elsewhere ? deviceName : "");
                if (row.Shown != shown)
                {
                    row.Shown = shown; row.Label.Inlines.Clear(); row.Label.Inlines.Add(new Run(session.Name));
                    if (elsewhere) { var run = new Run("  ·  " + deviceName); run.SetResourceReference(TextElement.ForegroundProperty, "Muted"); row.Label.Inlines.Add(run); }
                }
                row.Percent.Text = Math.Round(session.Volume * 100) + "%";
                if (!row.Slider.IsMouseCaptureWithin && !row.Slider.AreAnyTouchesCapturedWithin) row.Slider.Value = session.Volume * 100;
                row.Mute.Content = session.Muted ? "" : "";
                row.Mute.ToolTip = (session.Muted ? "Unmute " : "Mute ") + session.Name;
                AutomationProperties.SetName(row.Mute, (session.Muted ? "Unmute " : "Mute ") + session.Name);
                AutomationProperties.SetName(row.Slider, session.Name + " volume");
                row.Output.Content = OutputPicker.Glyph(device != null ? device.FormFactor : 0);
                row.Output.ToolTip = "Output: " + deviceName + ". Tap to change.";
                AutomationProperties.SetName(row.Output, "Output for " + session.Name + ": " + deviceName);
            }
            foreach (var pair in stale) { panel.Children.Remove(pair.Value.Root); sessionRows.Remove(pair.Key); }
            Text("SessionCount", sessions.Count == 1 ? "1 SESSION" : sessions.Count + " SESSIONS");
            Control<TextBlock>("NoSessions").Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private SessionRow CreateSessionRow()
        {
            var row = new SessionRow { Root = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 74 } };
            row.Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) }); row.Root.RowDefinitions.Add(new RowDefinition());
            row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); row.Root.ColumnDefinitions.Add(new ColumnDefinition());
            row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.Label = new TextBlock { FontSize = 16 * settings.TextScale, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumnSpan(row.Label, 2); row.Root.Children.Add(row.Label);
            row.Percent = new TextBlock { FontSize = 14, Foreground = (Brush)window.Resources["Muted"], HorizontalAlignment = HorizontalAlignment.Right }; Grid.SetColumn(row.Percent, 2); Grid.SetColumnSpan(row.Percent, 2); row.Root.Children.Add(row.Percent);
            row.Mute = new Button { Style = (Style)window.Resources["IconButton"], BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 18, MinHeight = 48 };
            Grid.SetRow(row.Mute, 1); row.Root.Children.Add(row.Mute); row.Mute.Click += (sender, e) => AudioAction(() => audio.ToggleSessionMute(row.Session.Id));
            row.Slider = new Slider { Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(row.Slider, 1); Grid.SetColumnSpan(row.Slider, 2); Grid.SetRow(row.Slider, 1); row.Root.Children.Add(row.Slider); TouchSlider.Attach(row.Slider);
            row.Slider.ValueChanged += (sender, e) => { if (!updatingAudio && audio != null) { AudioAction(() => audio.SetSessionVolume(row.Session.Id, e.NewValue / 100), false); row.Percent.Text = Math.Round(e.NewValue) + "%"; } };
            row.Output = new Button { Style = (Style)window.Resources["IconButton"], BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 18, MinHeight = 48, Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetRow(row.Output, 1); Grid.SetColumn(row.Output, 3); row.Root.Children.Add(row.Output); row.Output.Click += (sender, e) => OpenAppOutputs(row.Session);
            return row;
        }

        private static bool SameOutput(string first, string second) { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }

        private void OpenMasterOutputs()
        {
            if (audio == null || outputs.Count == 0) return;
            var choices = outputs.Select(d => new OutputChoice { Id = d.Id, Label = d.Name, Selected = d.IsDefault }).ToList();
            outputPicker.Show("MASTER OUTPUT", "Master volume", choices, true, "Apps set to Windows default follow this output.", choice => {
                try { audio.SetDefaultOutput(choice.Id); Notify("Sound now plays on " + choice.Label + "."); }
                catch (Exception) { Notify("Couldn't switch to " + choice.Label + ". Use the gear to open Windows sound settings."); }
                RefreshAudio();
            });
        }

        private void OpenAppOutputs(AudioSession session)
        {
            if (audio == null || session == null || outputs.Count == 0) return;
            bool system = session.ProcessId == 0, routable = !system && audio.CanRouteApps;
            string routed = null;
            if (routable) { try { routed = audio.AppOutput(session.ProcessId); } catch (Exception) { routable = false; } }
            bool missing = routed != null && !outputs.Any(d => SameOutput(d.Id, routed));
            var master = outputs.FirstOrDefault(d => d.IsDefault);
            var choices = new List<OutputChoice> { new OutputChoice { Label = "Windows default", Detail = master == null ? null : "Same as master  ·  " + master.Name, Selected = routed == null } };
            choices.AddRange(outputs.Select(d => new OutputChoice { Id = d.Id, Label = d.Name, Detail = SameOutput(d.Id, session.DeviceId) ? "Playing here now" : null, Selected = routed != null && SameOutput(d.Id, routed) }));
            string note = system ? "System sounds always play on the master output. Change it at the top of the mixer."
                : !routable ? "Windows didn't allow changing this app's output here. Use the gear to open Windows sound settings."
                : missing ? "This app is set to an output that isn't connected. Choose another output or Windows default."
                : "Windows remembers this for the app. Some apps switch only after they restart.";
            outputPicker.Show("APP OUTPUT", session.Name, choices, routable, note, choice => {
                try { audio.SetAppOutput(session.ProcessId, choice.Id); Notify(session.Name + (choice.Id == null ? " now follows the master output." : " now plays on " + choice.Label + ".")); }
                catch (Exception) { Notify("Couldn't change the output for " + session.Name + ". Use the gear to open Windows sound settings."); }
                RefreshAudio();
            });
        }

        private void AudioAction(Action action, bool refresh = true)
        {
            try { if (audio == null) audio = new AudioService(); action(); if (refresh) RefreshAudio(); }
            catch (Exception) { Notify("The audio device changed or is unavailable. Reconnecting…"); RefreshAudio(); }
        }

        private async void RefreshSystem()
        {
            if (disposed || systemBusy || !window.IsLoaded) return;
            systemBusy = true;
            var requested = settings;
            bool readStats = Editor.IsVisible("System");
            try
            {
                var snapshot = await Task.Run(() => system.Read(readStats && requested.ShowCpu, readStats && requested.ShowGpu, readStats && requested.ShowRam, readStats && requested.ShowNetwork, readStats && requested.ShowUptime));
                if (disposed || requested != settings) return;
                Text("CpuValue", snapshot.CpuAvailable ? Math.Round(snapshot.Cpu) + "%" : "—");
                Text("SystemStatus", settings.QuietMode ? "EVERY 5 SEC" : "EVERY 2 SEC");
                Text("GpuValue", snapshot.Gpu.HasValue ? Math.Round(snapshot.Gpu.Value) + "%" : "—");
                Text("GpuStatus", snapshot.GpuStatus ?? "Unavailable"); Control<ProgressBar>("GpuBar").Value = snapshot.Gpu ?? 0;
                Text("MemoryValue", snapshot.MemoryAvailable ? snapshot.UsedMemoryGB.ToString("0.0") + " / " + snapshot.TotalMemoryGB.ToString("0.0") + " GB" : "Unavailable");
                Text("MemoryPercent", snapshot.MemoryAvailable ? "RAM · " + Math.Round(snapshot.MemoryPercent) + "%" : "RAM · unavailable"); Control<ProgressBar>("MemoryBar").Value = snapshot.MemoryPercent;
                Text("DownloadValue", snapshot.NetworkAvailable ? SystemService.FormatRate(snapshot.DownloadBytes) : "— KB/s"); Text("UploadValue", snapshot.NetworkAvailable ? SystemService.FormatRate(snapshot.UploadBytes) : "— KB/s");
                Text("UptimeValue", "Uptime  ·  " + (int)snapshot.Uptime.TotalDays + "d " + snapshot.Uptime.Hours + "h " + snapshot.Uptime.Minutes + "m");
                if (snapshot.CpuAvailable) cpuHistory.Add(snapshot.Cpu); else cpuHistory.Clear();
                if (cpuHistory.Count > 48) cpuHistory.RemoveAt(0);
                var points = new PointCollection(); double width = Math.Max(1, Control<Canvas>("CpuGraphHost").ActualWidth);
                for (int i = 0; i < cpuHistory.Count; i++) points.Add(new Point(width * (48 - cpuHistory.Count + i) / 47.0, 30 - cpuHistory[i] * .28));
                Control<Polyline>("CpuGraph").Points = points;
            }
            catch (Exception)
            {
                if (disposed) return;
                Text("SystemStatus", "UNAVAILABLE"); Text("CpuValue", "—%"); Text("MemoryValue", "— / — GB");
                Text("GpuValue", "—"); Text("GpuStatus", "Unavailable"); Control<ProgressBar>("GpuBar").Value = 0;
                Text("MemoryPercent", "RAM · unavailable"); Text("DownloadValue", "— KB/s"); Text("UploadValue", "— KB/s");
                Control<ProgressBar>("MemoryBar").Value = 0; Control<Polyline>("CpuGraph").Points = new PointCollection(); cpuHistory.Clear();
            }
            finally { systemBusy = false; if (!disposed && requested != settings) RefreshSystem(); }
        }

        private async void RefreshMedia()
        {
            if (disposed || !Editor.IsVisible("Media")) return;
            if (mediaBusy || mediaCommandBusy || mediaScrubber.Active) return; mediaBusy = true;
            try
            {
                var snapshot = await media.ReadAsync(); if (disposed) return;
                Text("TrackTitle", snapshot.Available ? snapshot.Title : "Ready when you are");
                Text("TrackArtist", snapshot.Available ? (String.IsNullOrWhiteSpace(snapshot.Artist) ? "Your active media session" : snapshot.Artist) : "Play something in Spotify or a media app.");
                string source = snapshot.Source ?? "";
                Text("MediaSource", !snapshot.Available ? "WINDOWS MEDIA" : source.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0 ? "SPOTIFY" : source.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0 ? "CHROME" : source.IndexOf("edge", StringComparison.OrdinalIgnoreCase) >= 0 ? "EDGE" : "MEDIA SESSION");
                Text("MediaPosition", snapshot.Available ? FormatTime(snapshot.Position) : "—:—");
                Text("MediaDuration", snapshot.Duration > TimeSpan.Zero ? FormatTime(snapshot.Duration) : "—:—");
                mediaScrubber.Update(snapshot.Key, snapshot.Position.TotalMilliseconds, snapshot.Duration.TotalMilliseconds, snapshot.CanSeek);
                Control<Image>("AlbumArt").Source = snapshot.Artwork;
                Control<Button>("PlayButton").Content = snapshot.Playing ? "\uE769" : "\uE768";
                AutomationProperties.SetName(Control<Button>("PlayButton"), snapshot.Playing ? "Pause playback" : "Play media");
                Control<Button>("PlayButton").ToolTip = snapshot.Playing ? "Pause playback" : "Play media";
                Control<Button>("PlayButton").IsEnabled = snapshot.Available && snapshot.CanPlayPause;
                Control<Button>("PreviousButton").IsEnabled = snapshot.Available && snapshot.CanPrevious;
                Control<Button>("NextButton").IsEnabled = snapshot.Available && snapshot.CanNext;
                Text("MediaHint", snapshot.Available ? snapshot.Playing ? "Playing on this PC" : "Playback paused" : "Track information comes from your active media app.");
            }
            catch (Exception)
            {
                if (disposed) return;
                Text("TrackTitle", "Media unavailable"); Text("TrackArtist", "Open Spotify or another supported media app.");
                Text("MediaSource", "NOT CONNECTED"); Text("MediaPosition", "—:—"); Text("MediaDuration", "—:—");
                mediaScrubber.Update(null, 0, 0, false); Control<Image>("AlbumArt").Source = null;
                Text("MediaHint", "Waiting for a media session. Retrying automatically.");
                Control<Button>("PlayButton").IsEnabled = false; Control<Button>("PreviousButton").IsEnabled = false; Control<Button>("NextButton").IsEnabled = false;
            }
            finally { mediaBusy = false; }
        }
        private async void MediaCommand(string command)
        {
            if (mediaCommandBusy || disposed) return;
            mediaCommandBusy = true;
            Control<Button>("PlayButton").IsEnabled = false; Control<Button>("PreviousButton").IsEnabled = false; Control<Button>("NextButton").IsEnabled = false;
            try { if (!await media.CommandAsync(command)) Notify("The media app did not accept that control."); }
            catch (Exception) { Notify("Media control is unavailable. Check your media app."); }
            finally { mediaCommandBusy = false; if (!disposed) RefreshMedia(); }
        }
        private async void SeekMedia(string key, double position)
        {
            if (mediaCommandBusy || disposed) return;
            mediaCommandBusy = true;
            try { if (!await media.SeekAsync(key, position)) Text("MediaHint", "This media app could not seek, or the song changed."); }
            catch (Exception) { Text("MediaHint", "Seeking is unavailable in this media app."); }
            finally { mediaCommandBusy = false; }
        }
        private static string FormatTime(TimeSpan time) { return ((int)time.TotalMinutes).ToString("0") + ":" + time.Seconds.ToString("00"); }

        private async void RefreshWeather()
        {
            if (disposed || weatherBusy || !Editor.IsVisible("Weather")) return;
            if (String.IsNullOrWhiteSpace(settings.WeatherLocation))
            {
                Text("WeatherTemperature", "—°"); Text("WeatherCity", "Choose your city"); Text("WeatherDetails", "Weather is not configured.");
                Text("WeatherCondition", "YOUR LOCAL OUTLOOK"); Control<Button>("WeatherButton").Content = "Set location"; Text("WeatherCredit", "Weather data by Open-Meteo"); return;
            }
            weatherBusy = true; lastWeatherAttempt = DateTime.UtcNow; int generation = weatherGeneration;
            Text("WeatherCity", settings.WeatherLocation); Control<Button>("WeatherButton").Content = "Updating…"; Control<Button>("WeatherButton").IsEnabled = false;
            try
            {
                var snapshot = await weather.ReadAsync(settings.Latitude, settings.Longitude);
                if (disposed || generation != weatherGeneration) return;
                lastWeather = snapshot;
                Text("WeatherTemperature", Math.Round(snapshot.Temperature) + "°"); Text("WeatherCondition", WeatherService.Describe(snapshot.Code));
                Text("WeatherDetails", "Feels " + Math.Round(snapshot.FeelsLike) + "°C  ·  " + Math.Round(snapshot.Wind) + " km/h");
                Text("WeatherCredit", "Open-Meteo  ·  updated " + snapshot.Retrieved.ToString("HH:mm"));
            }
            catch (Exception)
            {
                if (generation == weatherGeneration) { Text("WeatherCondition", "OFFLINE"); Text("WeatherDetails", lastWeather == null ? "Can't reach weather. Tap to retry." : "Showing earlier data. Tap to retry."); }
            }
            finally
            {
                weatherBusy = false;
                if (!disposed) { Control<Button>("WeatherButton").IsEnabled = true; Control<Button>("WeatherButton").Content = "Refresh weather"; if (generation != weatherGeneration) RefreshWeather(); }
            }
        }

        private void OpenSettings()
        {
            openingSettings = true;
            outputPicker.Close();
            spotifyAccount.Prepare();
            settingsGeneration++;
            var monitors = DisplayService.Screens.Where(s => !s.Primary).Select(s => new MonitorOption { Id = s.DeviceName, Label = s.DeviceName + "  ·  " + s.Bounds.Width + " × " + s.Bounds.Height }).ToList();
            if (monitors.Count == 0) monitors.Add(new MonitorOption { Id = "", Label = "No secondary display — window preview" });
            var monitorChoice = Control<ComboBox>("MonitorChoice"); monitorChoice.ItemsSource = monitors; monitorChoice.SelectedItem = monitors.FirstOrDefault(m => m.Id == settings.Monitor) ?? monitors[0];
            var accent = Control<ComboBox>("AccentChoice"); accent.ItemsSource = new[] { "Lime", "Glacier", "Lavender", "Amber" }; accent.SelectedItem = settings.Accent;
            var theme = Control<ComboBox>("ThemeChoice"); theme.ItemsSource = new[] { "Midnight", "Slate", "Warm" }; theme.SelectedItem = settings.Theme;
            Control<Slider>("CornersChoice").Value = settings.CornerRadius;
            Control<CheckBox>("StartFullscreenChoice").IsChecked = settings.StartFullscreen;
            Control<CheckBox>("CpuChoice").IsChecked = settings.ShowCpu; Control<CheckBox>("GpuChoice").IsChecked = settings.ShowGpu;
            Control<CheckBox>("RamChoice").IsChecked = settings.ShowRam; Control<CheckBox>("NetworkChoice").IsChecked = settings.ShowNetwork; Control<CheckBox>("UptimeChoice").IsChecked = settings.ShowUptime;
            Control<Slider>("ScaleChoice").Value = settings.TextScale * 100;
            Control<CheckBox>("ClockChoice").IsChecked = settings.Use24HourClock; Control<CheckBox>("LevelsChoice").IsChecked = settings.ShowLevels;
            pendingLocation = String.IsNullOrWhiteSpace(settings.WeatherLocation) ? null : new WeatherLocation { Name = settings.WeatherLocation, Latitude = settings.Latitude, Longitude = settings.Longitude };
            Control<ComboBox>("CityResults").ItemsSource = pendingLocation == null ? new List<WeatherLocation>() : new List<WeatherLocation> { pendingLocation };
            Control<ComboBox>("CityResults").SelectedItem = pendingLocation; Control<TextBox>("CitySearch").Text = "";
            Text("SettingsStatus", "Settings are stored locally on this PC.");
            deckEditor.Begin(settings.Shortcuts); Control<Grid>("DashboardContent").IsEnabled = false; Control<Grid>("SettingsOverlay").Visibility = Visibility.Visible;
            Control<TabControl>("SettingsTabs").SelectedIndex = 0;
            openingSettings = false; PreviewAppearance();
            ((TabItem)Control<TabControl>("SettingsTabs").Items[0]).Focus();
        }
        private void CloseSettings() { spotifyAccount.Cancel(); settingsGeneration++; Control<Grid>("SettingsOverlay").Visibility = Visibility.Collapsed; ApplySettings(); Control<Grid>("DashboardContent").IsEnabled = true; Control<Button>("SettingsButton").Focus(); }

        private void SaveSettings()
        {
            string spotifyId = Control<TextBox>("SpotifyClientId").Text.Trim();
            if (spotifyId.Length > 0 && !SpotifyService.ValidClientId(spotifyId)) { Text("SettingsStatus", "Spotify: enter the 32-character Client ID, or clear this field to keep the saved ID."); return; }
            List<Shortcut> shortcuts;
            if (!deckEditor.TryGet(out shortcuts)) return;
            var monitor = Control<ComboBox>("MonitorChoice").SelectedItem as MonitorOption;
            var candidate = new AppSettings {
                SpotifyClientId = spotifyId.Length == 0 ? settings.SpotifyClientId : spotifyId,
                Theme = (string)Control<ComboBox>("ThemeChoice").SelectedItem, CornerRadius = Control<Slider>("CornersChoice").Value,
                StartFullscreen = Control<CheckBox>("StartFullscreenChoice").IsChecked == true,
                ShowCpu = Control<CheckBox>("CpuChoice").IsChecked == true, ShowGpu = Control<CheckBox>("GpuChoice").IsChecked == true,
                ShowRam = Control<CheckBox>("RamChoice").IsChecked == true, ShowNetwork = Control<CheckBox>("NetworkChoice").IsChecked == true, ShowUptime = Control<CheckBox>("UptimeChoice").IsChecked == true,
                Accent = (string)Control<ComboBox>("AccentChoice").SelectedItem, TextScale = Control<Slider>("ScaleChoice").Value / 100,
                Monitor = monitor == null ? "" : monitor.Id, ShowLevels = Control<CheckBox>("LevelsChoice").IsChecked == true,
                Use24HourClock = Control<CheckBox>("ClockChoice").IsChecked == true, SwapSideWidgets = settings.SwapSideWidgets,
                WidgetLayout = settings.WidgetLayout == null ? null : WidgetScreen.WidgetLayout.Copy(settings.WidgetLayout), SnapWidgets = settings.SnapWidgets,
                ClockWidth = settings.ClockWidth, MediaWidth = settings.MediaWidth, MixerWidth = settings.MixerWidth, SystemWidth = settings.SystemWidth,
                ClockHeight = settings.ClockHeight, MediaHeight = settings.MediaHeight, MixerHeight = settings.MixerHeight, SystemHeight = settings.SystemHeight,
                QuietMode = settings.QuietMode, Shortcuts = shortcuts, WeatherLocation = pendingLocation == null ? "" : pendingLocation.Name,
                Latitude = pendingLocation == null ? 0 : pendingLocation.Latitude, Longitude = pendingLocation == null ? 0 : pendingLocation.Longitude
            };
            try { SettingsStore.Save(settingsPath, candidate); }
            catch (Exception e) { Text("SettingsStatus", "Could not save settings: " + e.Message); return; }
            bool locationChanged = settings.WeatherLocation != candidate.WeatherLocation || settings.Latitude != candidate.Latitude || settings.Longitude != candidate.Longitude;
            settings = candidate; CloseSettings(); RefreshSystem();
            if (fullscreen) { if (!DisplayService.Fullscreen(window, settings.Monitor)) { DisplayService.Windowed(window); fullscreen = false; } UpdateDisplayStatus(); }
            if (locationChanged) { weatherGeneration++; lastWeather = null; Text("WeatherTemperature", "—°"); Text("WeatherDetails", "Updating weather…"); RefreshWeather(); }
            Notify("Saved. Your dashboard is ready.");
        }

        private async void SearchCity()
        {
            int generation = settingsGeneration;
            var button = Control<Button>("SearchCityButton"); button.IsEnabled = false; button.Content = "Searching…"; Text("SettingsStatus", "Finding matching cities…");
            try
            {
                var results = await weather.SearchAsync(Control<TextBox>("CitySearch").Text);
                if (disposed || generation != settingsGeneration) return;
                Control<ComboBox>("CityResults").ItemsSource = results;
                Text("SettingsStatus", results.Count == 0 ? "No cities found. Try a nearby city or another spelling." : "Choose your city from the results, then save changes.");
                if (results.Count > 0) Control<ComboBox>("CityResults").IsDropDownOpen = true;
            }
            catch (Exception e) { if (!disposed && generation == settingsGeneration) Text("SettingsStatus", "City search failed: " + e.GetBaseException().Message); }
            finally { if (!disposed) { button.IsEnabled = true; button.Content = "Search"; } }
        }

        private void Persist() { try { SettingsStore.Save(settingsPath, settings); } catch (Exception e) { Notify("Settings could not be saved: " + e.Message); } }
        private void ToggleFullscreen()
        {
            if (fullscreen) { DisplayService.Windowed(window); fullscreen = false; }
            else if (DisplayService.Fullscreen(window, settings.Monitor)) fullscreen = true;
            else Notify("Connect a second display to use fullscreen. This preview stays on the primary monitor.");
            UpdateDisplayStatus();
        }
        private void UpdateDisplayStatus()
        {
            Text("DisplayStatus", fullscreen ? "SECOND SCREEN  ·  ESC TO WINDOW" : "WINDOW PREVIEW  ·  F11 SECOND SCREEN");
            Control<Button>("FullscreenButton").Content = fullscreen ? "Window preview" : "Second screen";
        }
        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (Editor.Editing)
            {
                if (e.Key == Key.Escape) { CancelLayoutEdit(); e.Handled = true; return; }
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.Z || e.Key == Key.Y))
                {
                    if (e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) Editor.Redo(); else Editor.Undo();
                    e.Handled = true; return;
                }
                if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                {
                    // Keep dropdown navigation intact; arrows on a widget move it, Shift+arrows resize it.
                    if (Keyboard.FocusedElement is ComboBox || Control<ComboBox>("EditWidgetChoice").IsKeyboardFocusWithin) return;
                    double step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 1 : 8;
                    Editor.TransformSelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? "SE" : "", e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0, false);
                    e.Handled = true; return;
                }
            }
            if (e.Key == Key.Escape)
            {
                if (Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) CloseSettings(); else if (fullscreen) ToggleFullscreen(); e.Handled = true;
            }
            else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        }
        private void DisplaysChanged(object sender, EventArgs e)
        {
            if (disposed) return;
            window.Dispatcher.BeginInvoke(new Action(() => {
                if (disposed || !fullscreen) return;
                if (!DisplayService.Fullscreen(window, settings.Monitor)) { DisplayService.Windowed(window); fullscreen = false; Notify("Secondary display disconnected. Switched to window preview."); }
                UpdateDisplayStatus();
            }));
        }
        public void Dispose()
        {
            disposed = true; clockTimer.Stop(); levelTimer.Stop(); SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            weather.Dispose(); spotifyAccount.Dispose(); spotify.Dispose(); deckEditor.Dispose(); deckHotkeys.Dispose(); if (audio != null) audio.Dispose();
            // Closing the UI must not wait for a performance-counter provider.
            Task.Run(() => system.Dispose());
        }
    }
}
