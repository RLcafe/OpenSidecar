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
    public sealed partial class DashboardController : IDisposable
    {
        private readonly Window window;
        private readonly string settingsPath;
        private readonly bool preview;
        private AppSettings settings;
        // All audio COM work runs on the worker's thread (decision 011); the dashboard only awaits it.
        private readonly AudioWorker audio = new AudioWorker();
        private bool audioBusy, audioAgain, levelBusy;
        // The last readings, shared between the dashboard and a phone's /state for a second so neither reads twice.
        public static readonly TimeSpan SnapshotLifetime = TimeSpan.FromSeconds(1);
        private Task<AudioSnapshot> audioRead; private AudioSnapshot lastAudio; private DateTime lastAudioAt;
        private Task<SystemSnapshot> systemRead; private SystemSnapshot lastSystem; private DateTime lastSystemAt; private string lastSystemFlags;
        private Task<MediaSnapshot> mediaRead; private MediaSnapshot lastMedia; private DateTime lastMediaAt;
        // Counts settings saves, so the phone's layout version only has to be recomputed when something was saved.
        private int settingsVersion;
        private readonly SystemService system = new SystemService();
        private readonly MediaService media = new MediaService();
        private readonly SpotifyService spotifyService;
        private readonly DiscordService discordService;
        private readonly SpotifyWidget spotify;
        private readonly SpotifyAccount spotifyAccount;
        private readonly SoundboardWidget soundboard;
        private readonly DiscordAccount discordAccount;
        private readonly PlaybackScrubber mediaScrubber;
        private readonly WeatherService weather = new WeatherService();
        private readonly FocusTimer focus = new FocusTimer();
        private readonly LevelVisualizer levels = new LevelVisualizer();
        private readonly OutputPicker outputPicker;
        private readonly ConfirmSheet confirm;
        private List<AudioDevice> outputs = new List<AudioDevice>();
        private readonly DispatcherTimer clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        private readonly DispatcherTimer levelTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        private readonly Dictionary<string, SessionRow> sessionRows = new Dictionary<string, SessionRow>();
        private readonly SystemStatsCard systemCard;
        private readonly WeatherCard weatherCard;
        private readonly DeckEditor deckEditor;
        private readonly DeckHotkeys deckHotkeys;
        private readonly FocusKeeper focusKeeper;
        private int deckPage;
        public LayoutEditor Editor { get; private set; }
        private bool syncingEditor;
        private string deckShape = "";
        private int selectedFocusPreset = -1;
        private bool openingSettings, systemBusy;
        private bool updatingAudio, audioAvailable, mediaBusy, mediaCommandBusy, weatherBusy, disposed, fullscreen;
        private int ticks, weatherGeneration, settingsGeneration, statusClearsAt;
        private DateTime lastWeatherAttempt = DateTime.MinValue;
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
            confirm = new ConfirmSheet(window);
            systemCard = new SystemStatsCard(window, Control<Grid>("SystemBody"), Control<TextBlock>("SystemUptime"));
            weatherCard = new WeatherCard(window, Control<Grid>("WeatherBody"));
            InitializeStorage();
            InitializeClocks();
            InitializePrayer();
            InitializeNotes();
            InitializeClaude();
            InitializeClipboard();
            InitializeMacros();
            InitializeCalendar();
            InitializeGames();
            InitializeHome();
            Editor.Changed += UpdateEditor;
            // Account state belongs to the settings directory, never to a build/output folder.
            spotifyService = new SpotifyService(SettingsStore.SpotifyPath(settingsPath));
            spotify = new SpotifyWidget(window, spotifyService);
            if (warning == null && String.IsNullOrEmpty(settings.SpotifyClientId) && SpotifyService.ValidClientId(spotifyService.ClientId))
            {
                settings.SpotifyClientId = spotifyService.ClientId;
                try { SaveSettingsFile(settings); } catch (Exception) { pendingMessage = "Could not save the Spotify Client ID. Check Settings."; }
            }
            spotifyAccount = new SpotifyAccount(window, spotifyService, settings.SpotifyClientId, value => {
                string previous = settings.SpotifyClientId; settings.SpotifyClientId = value;
                try { SaveSettingsFile(settings); } catch { settings.SpotifyClientId = previous; throw; }
            }, () => { spotify.AccountChanged(); spotify.Tick(Editor.IsVisible("Spotify"), settings.QuietMode); });
            discordService = new DiscordService(SettingsStore.DiscordPath(settingsPath));
            soundboard = new SoundboardWidget(window, discordService) { OpenSetup = OpenDiscordSettings };
            discordAccount = new DiscordAccount(window, discordService, () => { soundboard.AccountChanged(); soundboard.Tick(Editor.IsVisible("Soundboard") && !Editor.Editing, settings.QuietMode); });
            mediaScrubber = new PlaybackScrubber(Control<Slider>("MediaProgress"), SeekMedia, value => Text("MediaPosition", FormatTime(TimeSpan.FromMilliseconds(value))));
            deckEditor = new DeckEditor(window, message => Text("SettingsStatus", message));
            deckHotkeys = new DeckHotkeys();
            focusKeeper = new FocusKeeper(window);
            BindActions();
            InitializeRemote();
            InitializePages();
            FitScreen(); ApplySettings(); UpdateFocus();
            clockTimer.Tick += Tick;
            // An output appeared, left or became the default: read the mixer now instead of at the next beat.
            audio.DevicesChanged += () => window.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) RefreshAudio(true); }));
            levelTimer.Tick += async (sender, e) => {
                if (levelBusy || !Showing || disposed) return;
                levelBusy = true;
                try { float peak = await audio.Run(a => a.Peak()); if (!disposed) levels.Push(peak); }
                catch (Exception) { }
                finally { levelBusy = false; }
            };
            window.Loaded += Loaded;
            window.PreviewKeyDown += KeyDown;
            window.StateChanged += (sender, e) => UpdateLevelTimer();
            window.SizeChanged += (sender, e) => FitScreen();
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
        // Status messages are transient: they clear after about ten seconds on the existing heartbeat. A sticky message stays until replaced.
        private void Notify(string text, bool sticky = false) { Text("StatusMessage", text); statusClearsAt = sticky ? int.MaxValue : ticks + 10; }

        private void BindActions()
        {
            Button("CloseButton", () => window.Close());
            Button("SettingsButton", OpenSettings);
            Button("HintDismiss", () => { settings.HintDismissed = true; Persist(); UpdateHint(); });
            Button("EditDeck", OpenDeckSettings);
            Button("EmptyDeck", OpenDeckSettings);
            Button("DeckPrevious", () => { deckPage--; BuildShortcuts(); });
            Button("DeckNext", () => { deckPage++; BuildShortcuts(); });
            Button("SpotifyAccount", () => OpenSettingsTab("Spotify"));
            Button("SpotifySetup", () => OpenSettingsTab("Spotify"));
            Button("EditLayoutButton", BeginLayoutEdit);
            Button("OpenLayoutEditor", () => { CloseSettings(); BeginLayoutEdit(); });
            Button("UndoLayoutButton", () => Editor.Undo());
            Button("RedoLayoutButton", () => Editor.Redo());
            Button("ResetLayoutButton", () => Editor.Reset());
            Button("ArrangeLayoutButton", () => { if (!Editor.Arrange()) Notify("The widgets are already arranged."); });
            Button("CancelLayoutButton", CancelLayoutEdit);
            Button("SaveLayoutButton", SaveLayoutEdit);
            InitializeEditPanel();
            Control<CheckBox>("SnapLayoutChoice").Click += (sender, e) => Editor.Snap = Control<CheckBox>("SnapLayoutChoice").IsChecked == true;
            Button("CancelSettings", CloseSettings);
            Button("CancelSettingsFooter", CloseSettings);
            Button("SaveSettings", SaveSettings);
            Button("FullscreenButton", ToggleFullscreen);
            Button("QuietButton", () => {
                settings.QuietMode = !settings.QuietMode; ApplySettings(); Persist();
                Notify(settings.QuietMode ? "Quiet mode on. Output levels are paused and updates are slower." : "Quiet mode off.");
            });
            Button("FocusStart", () => { focus.Toggle(); UpdateFocus(); });
            Button("FocusReset", () => { focus.Reset((int)focus.Duration.TotalMinutes); UpdateFocus(); });
            Button("FocusLength", NextFocusLength);
            Button("Focus25", () => SetFocus(25)); Button("Focus50", () => SetFocus(50)); Button("Focus5", () => SetFocus(5));
            // The fourth preset: tap to pick a length once, then it stays as a button like the others.
            Button("FocusCustom", () => { if (settings.FocusCustomMinutes > 0 && Control<Border>("FocusPicker").Visibility != Visibility.Visible && (int)focus.Duration.TotalMinutes != settings.FocusCustomMinutes) SetFocus(settings.FocusCustomMinutes); else OpenFocusPicker(); });
            Button("FocusPickerClose", CloseFocusPicker);
            Button("FocusLengthUse", () => StartCustomFocus((int)Control<Slider>("FocusLengthChoice").Value));
            Control<Slider>("FocusLengthChoice").ValueChanged += (sender, e) => Text("FocusLengthLabel", Math.Round(e.NewValue) + " min");
            // A finished session is dismissed with a tap anywhere on the card.
            Control<Border>("FocusCard").PreviewMouseLeftButtonDown += (sender, e) => DismissFocus();
            Control<Border>("FocusCard").PreviewTouchDown += (sender, e) => DismissFocus();
            Button("MuteButton", () => AudioAction(a => a.ToggleMute()));
            Button("MicrophoneButton", () => AudioAction(a => a.ToggleMicrophone()));
            Button("MicrophoneCompact", () => AudioAction(a => a.ToggleMicrophone()));
            Button("MixerApps", ToggleMixerApps);
            Button("SoundSettingsButton", () => Launch("ms-settings:sound", "Windows sound settings"));
            Button("OutputDeviceButton", OpenMasterOutputs);
            foreach (string name in new[] { "MasterSlider", "ScaleChoice", "CornersChoice", "FocusLengthChoice" }) TouchSlider.Attach(Control<Slider>(name));
            Control<Slider>("MasterSlider").ValueChanged += (sender, e) => {
                if (!updatingAudio) { double value = e.NewValue / 100; AudioAction(a => a.SetVolume(value), false); Text("MasterPercent", Math.Round(e.NewValue).ToString() + "%"); }
            };
            Button("PreviousButton", () => MediaCommand("previous")); Button("PlayButton", () => MediaCommand("toggle")); Button("NextButton", () => MediaCommand("next"));
            Button("MediaVolumeButton", OpenMediaVolume); Button("MediaVolumeClose", CloseMediaVolume);
            TouchSlider.Attach(Control<Slider>("MediaVolumeChoice"));
            Control<Slider>("MediaVolumeChoice").ValueChanged += (sender, e) => {
                if (updatingAudio || Control<Border>("MediaVolumePanel").Visibility != Visibility.Visible) return;
                Text("MediaVolumePercent", Math.Round(e.NewValue) + "%");
                string id = mediaVolumeSession; double value = e.NewValue / 100;
                AudioAction(a => { if (id == null) a.SetVolume(value); else a.SetSessionVolume(id, value); }, false);
            };
            // The whole Weather card is the refresh tap; without a city it opens Settings → Weather.
            Action weatherTapped = () => {
                if (Editor.Editing) return;
                if (String.IsNullOrWhiteSpace(settings.WeatherLocation))
                {
                    OpenSettingsTab("Weather"); window.UpdateLayout(); Control<TextBox>("CitySearch").BringIntoView(); Control<TextBox>("CitySearch").Focus();
                }
                else RefreshWeather();
            };
            Control<Border>("WeatherCard").PreviewMouseLeftButtonUp += (sender, e) => weatherTapped();
            Control<Border>("WeatherCard").PreviewTouchUp += (sender, e) => weatherTapped();
            Button("SearchCityButton", () => SearchCity());
            Button("ClearCityButton", () => { pendingLocation = null; ShowCityResults(null); Text("SettingsStatus", "Weather will be turned off when you save."); });
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
            FitScreen(); UpdateDisplayStatus();
            UpdateClock(); UpdateFocus(); RefreshAudio(); RefreshSystem(); RefreshMedia(); RefreshWeather(); RefreshStorage();
            clockTimer.Start(); UpdateLevelTimer();
            if (!Program.Rendering) ApplyRemote();
            if (!String.IsNullOrEmpty(pendingMessage)) Notify(pendingMessage, true);
        }

        private void Tick(object sender, EventArgs e)
        {
            ticks++; UpdateClock(); UpdateFocus();
            if (pairing.Code != null && Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) UpdateRemoteSettings();
            if (ticks >= statusClearsAt && !Editor.Editing) { Text("StatusMessage", ""); statusClearsAt = int.MaxValue; }
            UpdateTray();
            spotify.Tick(Showing && Editor.IsVisible("Spotify") && !Editor.Editing, settings.QuietMode);
            soundboard.Tick(Showing && Editor.IsVisible("Soundboard") && !Editor.Editing, settings.QuietMode);
            TickPrayer(); TickNotes(); TickClaudeOnline();
            // Minimized or in the tray: the dashboard isn't visible, so it doesn't poll (a phone still gets answers).
            if (!Showing) return;
            int interval = settings.QuietMode ? 5 : 2;
            if (ticks % interval == 0)
            {
                RefreshAudio();
                if (Editor.IsVisible("System") && (settings.ShowCpu || settings.ShowGpu || settings.ShowRam || settings.ShowNetwork || settings.ShowUptime)) RefreshSystem();
            }
            if (ticks % (settings.QuietMode ? 6 : 3) == 0) RefreshMedia();
            else if (lastMedia != null && lastMedia.Playing && Editor.IsVisible("Media")) mediaScrubber.Advance(1000);
            if (!String.IsNullOrWhiteSpace(settings.WeatherLocation) && DateTime.UtcNow - lastWeatherAttempt >= TimeSpan.FromMinutes(15)) RefreshWeather();
            TickStorage(); TickClocks(); TickCalendar(); TickGames(); TickHome();
            TickClaude();
        }

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Text("ClockTime", now.ToString(settings.Use24HourClock ? "HH:mm" : "h:mm", culture));
            Text("ClockPeriod", now.ToString("tt", culture));
            Control<TextBlock>("ClockPeriod").Visibility = settings.Use24HourClock ? Visibility.Collapsed : Visibility.Visible;
            Text("ClockDay", now.ToString("dddd", culture).ToUpperInvariant());
            Text("ClockDate", now.ToString("MMMM d", culture));
        }
        private void SetFocus(int minutes) { focus.Reset(minutes); UpdateFocus(); }
        private void OpenFocusPicker()
        {
            Control<Slider>("FocusLengthChoice").Value = settings.FocusCustomMinutes > 0 ? settings.FocusCustomMinutes : 45;
            Text("FocusLengthLabel", Math.Round(Control<Slider>("FocusLengthChoice").Value) + " min");
            Control<Border>("FocusPicker").Visibility = Visibility.Visible;
            Control<Slider>("FocusLengthChoice").Focus();
        }
        private void CloseFocusPicker() { Control<Border>("FocusPicker").Visibility = Visibility.Collapsed; }
        // Keeps the length as the fourth preset and starts a session with it (the PC's picker and the phone alike).
        private void StartCustomFocus(int minutes)
        {
            minutes = Math.Max(5, Math.Min(180, minutes));
            if (settings.FocusCustomMinutes != minutes) { settings.FocusCustomMinutes = minutes; Persist(); selectedFocusPreset = -1; }
            CloseFocusPicker();
            focus.Reset(minutes); focus.Toggle(); UpdateFocus();
        }
        private void DismissFocus() { if (focus.Finished) { focus.Dismiss(); UpdateFocus(); } }
        // A finished session counts towards today; the count resets with the first session of a new day.
        private void RecordFocusSession()
        {
            string today = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (settings.FocusSessionDate != today) { settings.FocusSessionDate = today; settings.FocusSessionsToday = 0; settings.FocusMinutesToday = 0; }
            settings.FocusSessionsToday++; settings.FocusMinutesToday += (int)focus.Duration.TotalMinutes;
            Persist();
        }
        private int FocusSessionsToday { get { return settings.FocusSessionDate == DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ? settings.FocusSessionsToday : 0; } }
        private int FocusMinutesToday { get { return FocusSessionsToday == 0 ? 0 : settings.FocusMinutesToday; } }
        private static void Chime() { try { System.Media.SystemSounds.Asterisk.Play(); } catch (Exception) { } }
        private void UpdateFocus()
        {
            bool finished = focus.FinishIfElapsed();
            if (finished) RecordFocusSession();
            TimeSpan remaining = focus.Remaining;
            Text("FocusTime", ((int)Math.Ceiling(remaining.TotalSeconds) / 60).ToString("00") + ":" + ((int)Math.Ceiling(remaining.TotalSeconds) % 60).ToString("00"));
            // Paused reads dimmer from across the room; a finished session lights the whole card.
            Control<TextBlock>("FocusTime").SetResourceReference(TextBlock.ForegroundProperty, focus.Paused ? "Muted" : "Text");
            Control<Border>("FocusFinished").Visibility = focus.Finished ? Visibility.Visible : Visibility.Collapsed;
            string ends = (DateTime.Now + remaining).ToString(settings.Use24HourClock ? "HH:mm" : "h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            Text("FocusStatus", remaining == TimeSpan.Zero ? (focus.Finished ? "Done. Take a break. Tap to clear." : "Done. Take a break.") : focus.Running ? "Ends at " + ends : focus.Progress > 0 ? "Paused" : "");
            Control<ProgressBar>("FocusProgress").Value = focus.Progress; if (focusRing != null) focusRing.Value = focus.Progress * 100;
            Text("FocusSessions", FocusTimer.SessionsText(FocusSessionsToday, FocusMinutesToday));
            Control<Button>("FocusStart").Content = focus.Running ? "Pause" : focus.Progress > 0 && remaining > TimeSpan.Zero ? "Resume" : CardSizing.FocusShort(Editor.Bounds("Focus").Height) ? "Start" : "Start focus";
            int minutes = (int)focus.Duration.TotalMinutes;
            var length = Control<Button>("FocusLength"); length.Content = minutes + "m"; length.IsEnabled = !focus.Running;
            AutomationProperties.SetName(length, "Session length " + minutes + " minutes. Tap for the next length.");
            if (minutes != selectedFocusPreset)
            {
                selectedFocusPreset = minutes;
                int custom = settings.FocusCustomMinutes;
                foreach (int preset in new[] { 25, 50, 5, 0 })
                {
                    var button = Control<Button>(preset == 0 ? "FocusCustom" : "Focus" + preset);
                    bool selected = preset == 0 ? custom > 0 && minutes == custom && custom != 25 && custom != 50 && custom != 5 : preset == minutes;
                    button.SetResourceReference(System.Windows.Controls.Button.BorderBrushProperty, selected ? "Accent" : "ControlBorder");
                    button.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, selected ? "Selected" : "Raised");
                    button.BorderThickness = new Thickness(selected ? 2 : 1);
                    if (preset == 0) { button.Content = custom > 0 ? custom + "m" : "…"; AutomationProperties.SetName(button, (custom > 0 ? custom + " minutes" : "Choose another length") + (selected ? ", selected" : "")); }
                    else AutomationProperties.SetName(button, preset + " minutes" + (selected ? ", selected" : ""));
                }
            }
            if (finished) { Notify("Focus session complete."); if (settings.FocusChime && !settings.QuietMode) Chime(); }
            UpdateDeckStates();
        }

        private void ApplySettings()
        {
            ApplyAppearance(settings);
            ApplyLayout(settings);
            focusKeeper.Enabled = settings.KeepGameFocus && !Program.Rendering;
            foreach (var row in sessionRows.Values) { row.Label.FontSize = 16 * settings.TextScale; row.Percent.FontSize = 14 * settings.TextScale; }
            // Quiet mode is a toggle: when on it takes the same selected look as the chosen Focus preset.
            var quiet = Control<Button>("QuietButton");
            quiet.SetResourceReference(System.Windows.Controls.Button.BorderBrushProperty, settings.QuietMode ? "Accent" : "ControlBorder");
            quiet.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, settings.QuietMode ? "Selected" : "Raised");
            quiet.BorderThickness = new Thickness(settings.QuietMode ? 2 : 1);
            AutomationProperties.SetName(quiet, settings.QuietMode ? "Quiet mode, on" : "Quiet mode, off");
            BuildShortcuts(); UpdateClock(); weatherCard.Configure(settings.WeatherLocation, WeatherImperial, settings.Use24HourClock); storageCard.Configure(settings.StorageLowPercent); clocksCard.Configure(settings.WorldClocks, settings.Use24HourClock); ApplyPrayer(); claudeCard.Configure(settings.Use24HourClock, ClaudePeriodChosen); claude.PlanEvery = TimeSpan.FromSeconds(settings.ClaudePlanSeconds); ApplyClipboard(); BuildMacros(); ApplyCalendar(); UpdateLevelTimer();
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
            window.Resources["HeaderClockSize"] = 52.0 * Math.Min(1.06, appearance.TextScale);
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
            systemCard.Choose(layout.ShowCpu, layout.ShowGpu, layout.ShowRam, layout.ShowNetwork, layout.ShowUptime);
        }

        private void BeginLayoutEdit()
        {
            Editor.Begin(); UpdateLevelTimer();
            Control<Button>("SaveLayoutButton").Focus();
        }
        private void CancelLayoutEdit()
        {
            CloseEditPanel(); Editor.Cancel(); UpdateLevelTimer(); UpdateDisplayStatus();
            Notify("Layout changes discarded."); Control<Button>("EditLayoutButton").Focus();
        }
        private void SaveLayoutEdit()
        {
            // The layout is saved for this canvas size of the page shown; other screen shapes keep their own.
            var previous = WidgetLayout.Pages(settings).Select(p => p.Copy()).ToList(); bool previousSnap = settings.SnapWidgets;
            WidgetLayout.Store(settings, Editor.CanvasSize, Editor.Export()); settings.SnapWidgets = Editor.Snap;
            try { SaveSettingsFile(settings); }
            catch (Exception e)
            {
                WidgetLayout.SetPages(settings, previous); settings.SnapWidgets = previousSnap;
                Notify("Could not save layout: " + e.Message + ". Your changes are still in the editor."); return;
            }
            CloseEditPanel(); Editor.Accept(); ApplySettings(); UpdateDisplayStatus();
            if (window.IsLoaded) { RefreshSystem(); RefreshAudio(); RefreshMedia(); RefreshWeather(); RefreshStorage(); }
            Notify("Layout saved."); Control<Button>("EditLayoutButton").Focus();
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
                Control<StackPanel>("MainActions").Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
                if (editing)
                {
                    Control<Button>("UndoLayoutButton").IsEnabled = Editor.CanUndo && !Editor.GestureActive;
                    Control<Button>("RedoLayoutButton").IsEnabled = Editor.CanRedo && !Editor.GestureActive;
                    Control<Button>("ArrangeLayoutButton").IsEnabled = !Editor.GestureActive;
                    Control<CheckBox>("SnapLayoutChoice").IsChecked = Editor.Snap;
                    UpdateEditPanel();
                    var bounds = Editor.Bounds(Editor.SelectedId);
                    string info = WidgetLayout.Title(Editor.SelectedId) + " · " + Math.Round(bounds.Width) + " × " + Math.Round(bounds.Height) + " · X " + Math.Round(bounds.X) + ", Y " + Math.Round(bounds.Y);
                    if (Editor.SelectedId == "System" && !Editor.IsVisible("System") && Editor.IsEnabled("System")) info += " · Enable metrics in Settings to show system stats";
                    else if (!Editor.IsEnabled(Editor.SelectedId)) info += " · Hidden · Turn on Show widget to bring it back";
                    if (Editor.HasOverlaps()) info += " · Widgets overlap";
                    Notify(info, true);
                    Text("DisplayStatus", "ARROWS MOVE · SHIFT+ARROWS RESIZE · ESC CANCELS");
                }
                if (deckShape != DeckShape()) BuildShortcuts();
                // Card contents follow their card's size (CardSizing, DashboardController.Cards.cs).
                FitCards();
                UpdatePages(); UpdateClaudePill();
            }
            finally { syncingEditor = false; }
        }

        private void UpdateLevelTimer()
        {
            // Only a Full mixer card has room for the meter (CardSizing.Mixer), so smaller cards don't run it.
            bool room = CardSizing.Mixer(Editor.Bounds("Mixer").Height) == CardSizing.MixerMode.Full;
            bool enabled = audioAvailable && settings.ShowLevels && !settings.QuietMode && !Editor.Editing && Editor.IsVisible("Mixer") && Showing && room;
            if (enabled) levelTimer.Start(); else { levelTimer.Stop(); levels.Clear(); }
            // The meter's row shows while levels run, or to say they're unavailable; paused levels take no room.
            bool unavailable = settings.ShowLevels && !audioAvailable && Editor.IsVisible("Mixer") && room;
            Control<RowDefinition>("LevelRow").Height = new GridLength(enabled || unavailable ? 28 : 0);
            ((FrameworkElement)Control<Border>("LevelHost").Parent).Visibility = room ? Visibility.Visible : Visibility.Collapsed;
            Text("LevelStatus", unavailable ? "OUTPUT LEVEL UNAVAILABLE" : "");
        }

        private void BuildShortcuts()
        {
            var grid = Control<System.Windows.Controls.Primitives.UniformGrid>("ShortcutGrid"); grid.Children.Clear(); deckStateButtons.Clear();
            // Six buttons a page that grow with the card, like the Soundboard (CardSizing.DeckGrid).
            deckShape = DeckShape();
            var area = DeckArea(); int[] shape = CardSizing.DeckGrid(area.Width, area.Height, settings.Shortcuts.Count);
            int pageSize = shape[0] * shape[1];
            double cellWidth = area.Width / shape[0], cellHeight = area.Height / shape[1];
            var labelMode = CardSizing.DeckLabel(cellWidth, cellHeight); bool beside = labelMode == CardSizing.DeckLabelMode.Beside;
            int pages = Math.Max(1, (settings.Shortcuts.Count + pageSize - 1) / pageSize);
            deckPage = Math.Max(0, Math.Min(pages - 1, deckPage));
            grid.Columns = shape[0]; grid.Rows = shape[1];
            Control<StackPanel>("DeckPages").Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            // A narrow card keeps its pager and buttons first (CardSizing.DeckEditShown, HeadingShown).
            double cardWidth = Editor.Bounds("Shortcuts").Width;
            Control<Button>("EditDeck").Visibility = CardSizing.DeckEditShown(cardWidth) ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("DeckHeading").Visibility = CardSizing.HeadingShown(cardWidth, pages > 1) ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("DeckPrevious").IsEnabled = deckPage > 0; Control<Button>("DeckNext").IsEnabled = deckPage < pages - 1;
            Text("DeckPageLabel", (deckPage + 1) + "/" + pages);
            Control<Button>("EmptyDeck").Visibility = settings.Shortcuts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var shortcut in settings.Shortcuts.Skip(deckPage * pageSize).Take(pageSize))
            {
                // A glyph-only button names itself in its tooltip.
                string named = labelMode == CardSizing.DeckLabelMode.None ? shortcut.Label + Environment.NewLine : "";
                var button = new Button { Margin = new Thickness(4), Padding = new Thickness(8), ToolTip = named + DeckActions.Describe(shortcut), IsEnabled = !String.IsNullOrWhiteSpace(shortcut.Target) };
                bool compact = beside; double glyphSize = CardSizing.DeckGlyph(cellHeight, beside);
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Orientation = compact ? Orientation.Horizontal : Orientation.Vertical };
                var glyph = new TextBlock { Text = String.IsNullOrEmpty(shortcut.Glyph) ? "\uE8A7" : shortcut.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = glyphSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                // A button's own colour tells it apart at a glance; otherwise the theme's accent.
                string hex = DeckActions.AccentHex(shortcut.Color);
                if (hex != null) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); glyph.Foreground = brush; } else glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                content.Children.Add(glyph);
                // A hotkey's tooltip names the app it would go to right now.
                if (shortcut.Action == "Hotkey") { var hotkey = shortcut; button.ToolTipOpening += (sender, e) => button.ToolTip = named + DeckActions.DescribeWithTarget(hotkey, deckHotkeys.TargetName); }
                if (DeckActions.IsStateful(shortcut)) deckStateButtons.Add(new KeyValuePair<Button, Shortcut>(button, shortcut));
                var name = new TextBlock { Text = shortcut.Label, FontSize = (compact ? 14 : 15) * settings.TextScale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = compact ? new Thickness(8, 0, 0, 0) : new Thickness(0, 8, 0, 0), MaxWidth = Math.Max(40, compact ? cellWidth - glyphSize - 40 : cellWidth - 24), TextTrimming = TextTrimming.CharacterEllipsis };
                if (labelMode == CardSizing.DeckLabelMode.None) name.Visibility = Visibility.Collapsed;
                content.Children.Add(name);
                // Large cells are tiles: the glyph, the name and what the button does, from the top left.
                if (CardSizing.Tile(cellWidth, cellHeight) == CardSizing.Design.Large)
                {
                    button.Padding = new Thickness(16, 12, 12, 12); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    content.HorizontalAlignment = HorizontalAlignment.Left; content.VerticalAlignment = VerticalAlignment.Stretch;
                    glyph.HorizontalAlignment = HorizontalAlignment.Left; glyph.FontSize = Math.Min(40, glyphSize);
                    name.HorizontalAlignment = HorizontalAlignment.Left; name.FontWeight = FontWeights.SemiBold; name.FontSize = 17 * settings.TextScale; name.Margin = new Thickness(0, 12, 0, 0); name.MaxWidth = cellWidth - 44;
                    var kind = new TextBlock { Text = DeckActions.Kind(shortcut), FontSize = 13, Margin = new Thickness(0, 3, 0, 0), MaxWidth = cellWidth - 44, TextTrimming = TextTrimming.CharacterEllipsis };
                    kind.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(kind);
                }
                button.Content = content; AutomationProperties.SetName(button, shortcut.Label);
                var action = shortcut; button.Click += (sender, e) => RunDeckAction(action); grid.Children.Add(button);
            }
            UpdateDeckStates();
        }
        // Control buttons that toggle something show whether it's on (mute, microphone, focus, Quiet mode), the
        // same selected look as the Focus presets. The phone receives the same flag.
        private readonly List<KeyValuePair<Button, Shortcut>> deckStateButtons = new List<KeyValuePair<Button, Shortcut>>();
        private bool DeckStateOn(Shortcut shortcut)
        {
            if (shortcut.Action != "Control") return false;
            switch (shortcut.Target)
            {
                case "mute": return lastAudio != null && lastAudio.Muted;
                case "microphone": return lastAudio != null && lastAudio.MicrophoneMuted == true;
                case "focus": return focus.Running;
                case "quiet": return settings.QuietMode;
                default: return false;
            }
        }
        private void UpdateDeckStates()
        {
            foreach (var pair in deckStateButtons)
            {
                bool on = DeckStateOn(pair.Value); var button = pair.Key;
                if (button.BorderThickness.Left == (on ? 2 : 1) && button.Tag is bool && (bool)button.Tag == on) continue;
                button.Tag = on;
                button.SetResourceReference(System.Windows.Controls.Button.BorderBrushProperty, on ? "Accent" : "ControlBorder");
                button.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, on ? "Selected" : "Raised");
                button.BorderThickness = new Thickness(on ? 2 : 1);
                AutomationProperties.SetName(button, pair.Value.Label + (on ? ", on" : ""));
            }
        }
        // The button grid: the Quick Launch card less its 20-unit padding, border and 48-unit header.
        private Size DeckArea() { var b = Editor.Bounds("Shortcuts"); return new Size(Math.Max(0, b.Width - 42), Math.Max(0, b.Height - 90)); }
        private string DeckShape() { var a = DeckArea(); var s = CardSizing.DeckGrid(a.Width, a.Height, settings.Shortcuts.Count); return s[0] + "x" + s[1] + "|" + Math.Round(a.Width) + "x" + Math.Round(a.Height); }
        private void OpenDeckSettings() { OpenSettingsTab("Quick Launch"); }
        private void OpenDiscordSettings() { OpenSettingsTab("Discord"); }
        // Opens Settings on a tab by its header, so tabs can be added without renumbering.
        private void OpenSettingsTab(string header) { OpenSettings(); SelectSettingsTab(window, header); }
        public static void SelectSettingsTab(Window window, string header)
        {
            var tabs = (TabControl)window.FindName("SettingsTabs");
            var tab = tabs.Items.OfType<TabItem>().FirstOrDefault(t => (string)t.Header == header);
            if (tab != null) tabs.SelectedItem = tab;
        }
        // A phone that runs a button has already asked the user, so it passes confirmed.
        private async void RunDeckAction(Shortcut shortcut) { await RunDeckActionAsync(shortcut, false); }
        // Returns null on success, or the message the dashboard showed.
        private async Task<string> RunDeckActionAsync(Shortcut shortcut, bool confirmed)
        {
            try
            {
                string error = DeckActions.Validate(shortcut); if (error != null) throw new InvalidOperationException(error);
                if (!confirmed && DeckActions.NeedsConfirmation(shortcut))
                {
                    bool run = await confirm.AskAsync("QUICK LAUNCH", shortcut.Label, DeckActions.Describe(shortcut), "Run " + shortcut.Label);
                    if (disposed || !run) return null;
                }
                if (shortcut.Action == "Hotkey") { await deckHotkeys.SendAsync(shortcut.Target); if (!disposed) Notify("Sent " + shortcut.Target + " to your previous app."); }
                else if (shortcut.Action == "Control")
                {
                    RunControl(shortcut.Target);
                }
                else { DeckActions.Open(shortcut); Notify("Opened " + shortcut.Label); }
                return null;
            }
            catch (Exception e) { if (!disposed) Notify(shortcut.Label + ": " + e.Message); return shortcut.Label + ": " + e.Message; }
        }
        // A dashboard or media control, from a Quick Launch button or a macro step.
        private void RunControl(string target)
        {
            switch (target)
            {
                case "play": MediaCommand("toggle"); break;
                case "previous": MediaCommand("previous"); break;
                case "next": MediaCommand("next"); break;
                case "mute": AudioAction(a => a.ToggleMute()); break;
                case "microphone": AudioAction(a => a.ToggleMicrophone()); break;
                case "volume-up": AudioAction(a => a.StepVolume(.05)); break;
                case "volume-down": AudioAction(a => a.StepVolume(-.05)); break;
                case "focus": focus.Toggle(); UpdateFocus(); break;
                case "quiet": settings.QuietMode = !settings.QuietMode; ApplySettings(); Persist(); break;
                case "sound": Launch("ms-settings:sound", "Windows sound settings"); break;
            }
        }
        private void Launch(string target, string label) { try { Launcher.Open(target); Notify("Opened " + label + "."); } catch (Exception e) { Notify("Couldn't open " + label + ": " + e.Message); } }

        // An unhandled error on the UI thread (Program.Main): the dashboard stays up and says where the details are.
        public void ReportError(Exception error)
        {
            if (disposed) return;
            try { Notify("Something went wrong: " + error.GetBaseException().Message + " Details are in error.log next to settings.xml.", true); } catch (Exception) { }
        }

        // Reads the mixer on the audio thread and shows it. fresh skips the shared one-second snapshot (after a change).
        // A read asked for while one is running happens right after it, so a change is never missed.
        private async void RefreshAudio(bool fresh = false)
        {
            if (disposed || !Editor.IsVisible("Mixer")) return;
            if (audioBusy) { audioAgain = true; return; }
            audioBusy = true;
            try
            {
                AudioSnapshot snapshot;
                try { snapshot = await ReadAudioAsync(fresh); } catch (Exception) { snapshot = null; }
                if (disposed) return;
                if (snapshot != null) ShowAudio(snapshot); else ShowAudioUnavailable();
            }
            finally
            {
                audioBusy = false;
                if (!disposed) { UpdateLevelTimer(); if (audioAgain) { audioAgain = false; RefreshAudio(true); } }
            }
        }

        private void ShowAudio(AudioSnapshot snapshot)
        {
            updatingAudio = true;
            try
            {
                audioAvailable = true;
                Text("AudioDevice", snapshot.Device); Text("AudioStatus", "");
                outputs = snapshot.Devices;
                var outputButton = Control<Button>("OutputDeviceButton"); outputButton.IsEnabled = true;
                outputButton.ToolTip = "Output: " + snapshot.Device + ". Tap to choose another output.";
                AutomationProperties.SetName(outputButton, "Choose output device. Current: " + snapshot.Device);
                Text("MasterPercent", Math.Round(snapshot.Volume * 100).ToString() + "%");
                var slider = Control<Slider>("MasterSlider"); slider.IsEnabled = true;
                if (!slider.IsMouseCaptureWithin && !slider.AreAnyTouchesCapturedWithin) slider.Value = snapshot.Volume * 100;
                Control<Button>("MuteButton").IsEnabled = true;
                Control<Button>("MuteButton").Content = snapshot.Muted ? "\uE74F" : "\uE767";
                Control<Button>("MuteButton").ToolTip = snapshot.Muted ? "Unmute output" : "Mute output";
                AutomationProperties.SetName(Control<Button>("MuteButton"), snapshot.Muted ? "Unmute output" : "Mute output");
                ShowMicrophone(!snapshot.MicrophoneMuted.HasValue ? "No microphone" : snapshot.MicrophoneMuted.Value ? "Microphone muted" : "Microphone on", snapshot.MicrophoneMuted);
                UpdateSessions(snapshot.Sessions);
                Text("NoSessions", snapshot.SessionsAvailable ? "Apps show up here when they play sound." : "App volumes aren't available right now. Master volume still works.");
                if (!snapshot.SessionsAvailable) Text("SessionCount", "UNAVAILABLE");
                UpdateDeckStates();
            }
            finally { updatingAudio = false; }
        }

        private void ShowAudioUnavailable()
        {
            updatingAudio = true;
            try
            {
                audioAvailable = false;
                Text("AudioStatus", "UNAVAILABLE"); Text("AudioDevice", "No audio output found"); Text("MasterPercent", "—");
                outputs = new List<AudioDevice>(); outputPicker.Close(); Control<Button>("OutputDeviceButton").IsEnabled = false;
                Control<Slider>("MasterSlider").IsEnabled = false; Control<Button>("MuteButton").IsEnabled = false;
                ShowMicrophone("Microphone unavailable", null);
                UpdateSessions(new List<AudioSession>());
                Text("SessionCount", "—");
                Text("NoSessions", "Reconnect your speakers or headphones, or open Windows sound settings with the gear above. Retrying automatically.");
            }
            finally { updatingAudio = false; }
        }

        private void UpdateSessions(List<AudioSession> sessions)
        {
            var panel = Control<System.Windows.Controls.Primitives.UniformGrid>("SessionPanel");
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
            // Rows follow the reading's order (playing apps first, System sounds last) without being rebuilt.
            for (int i = 0; i < sessions.Count; i++)
            {
                var root = sessionRows[sessions[i].Id].Root;
                if (panel.Children.IndexOf(root) != i) { panel.Children.Remove(root); panel.Children.Insert(i, root); }
            }
            SpaceSessionColumns();
            Text("SessionCount", sessions.Count == 0 ? "" : sessions.Count.ToString()); UpdateMixerApps();
            Control<TextBlock>("NoSessions").Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // The microphone toggle states what is true now; the muted state takes the accent so it stands out.
        private void ShowMicrophone(string label, bool? muted)
        {
            var microphone = Control<Button>("MicrophoneButton"); microphone.IsEnabled = muted.HasValue;
            Text("MicrophoneLabel", label);
            AutomationProperties.SetName(microphone, label);
            microphone.ToolTip = !muted.HasValue ? null : muted.Value ? "Unmute microphone" : "Mute microphone";
            foreach (string name in new[] { "MicrophoneGlyph", "MicrophoneLabel" }) Control<TextBlock>(name).SetResourceReference(TextBlock.ForegroundProperty, muted == true ? "Accent" : "Text");
            // The compact cards' microphone button beside the master slider says the same with its glyph and tooltip.
            var compact = Control<Button>("MicrophoneCompact"); compact.IsEnabled = muted.HasValue;
            compact.Content = muted == true ? "" : ""; compact.ToolTip = label;
            compact.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, muted == true ? "Accent" : "Text");
            AutomationProperties.SetName(compact, label);
        }

        private SessionRow CreateSessionRow()
        {
            var row = new SessionRow { Root = new Grid { Margin = new Thickness(0, 8, 0, 0), Height = 74 } };
            row.Root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(24) }); row.Root.RowDefinitions.Add(new RowDefinition());
            row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); row.Root.ColumnDefinitions.Add(new ColumnDefinition());
            row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); row.Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.Label = new TextBlock { FontSize = 16 * settings.TextScale, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumnSpan(row.Label, 2); row.Root.Children.Add(row.Label);
            row.Percent = new TextBlock { FontSize = 14 * settings.TextScale, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; row.Percent.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Grid.SetColumn(row.Percent, 2); Grid.SetColumnSpan(row.Percent, 2); row.Root.Children.Add(row.Percent);
            row.Mute = new Button { Style = (Style)window.Resources["IconButton"], BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 18, MinHeight = 48 };
            Grid.SetRow(row.Mute, 1); row.Root.Children.Add(row.Mute); row.Mute.Click += (sender, e) => { string id = row.Session.Id; AudioAction(a => a.ToggleSessionMute(id)); };
            row.Slider = new Slider { Margin = new Thickness(10, 0, 0, 0) }; Grid.SetColumn(row.Slider, 1); Grid.SetColumnSpan(row.Slider, 2); Grid.SetRow(row.Slider, 1); row.Root.Children.Add(row.Slider); TouchSlider.Attach(row.Slider);
            row.Slider.ValueChanged += (sender, e) => { if (!updatingAudio) { string id = row.Session.Id; double value = e.NewValue / 100; AudioAction(a => a.SetSessionVolume(id, value), false); row.Percent.Text = Math.Round(e.NewValue) + "%"; } };
            row.Output = new Button { Style = (Style)window.Resources["IconButton"], BorderThickness = new Thickness(0), Background = Brushes.Transparent, FontSize = 18, MinHeight = 48, Margin = new Thickness(4, 0, 0, 0) };
            Grid.SetRow(row.Output, 1); Grid.SetColumn(row.Output, 3); row.Root.Children.Add(row.Output); row.Output.Click += (sender, e) => OpenAppOutputs(row.Session);
            return row;
        }

        private static bool SameOutput(string first, string second) { return String.Equals(first, second, StringComparison.OrdinalIgnoreCase); }

        private void OpenMasterOutputs()
        {
            if (outputs.Count == 0) return;
            var choices = outputs.Select(d => new OutputChoice { Id = d.Id, Label = d.Name, Selected = d.IsDefault }).ToList();
            outputPicker.Show("MASTER OUTPUT", "Master volume", choices, true, "Apps set to Windows default follow this output.", async choice => {
                try { await audio.Run(a => a.SetDefaultOutput(choice.Id)); if (!disposed) Notify("Sound now plays on " + choice.Label + "."); }
                catch (Exception) { if (!disposed) Notify("Couldn't switch to " + choice.Label + ". Try again, or use the gear to open Windows sound settings."); }
                if (!disposed) RefreshAudio(true);
            });
        }

        private async void OpenAppOutputs(AudioSession session)
        {
            if (session == null || outputs.Count == 0) return;
            bool system = session.ProcessId == 0, routable = false;
            string routed = null;
            if (!system)
            {
                try { var routing = await audio.Run(a => Tuple.Create(a.CanRouteApps, a.CanRouteApps ? a.AppOutput(session.ProcessId) : null)); routable = routing.Item1; routed = routing.Item2; }
                catch (Exception) { routable = false; }
                if (disposed) return;
            }
            bool missing = routed != null && !outputs.Any(d => SameOutput(d.Id, routed));
            var master = outputs.FirstOrDefault(d => d.IsDefault);
            var choices = new List<OutputChoice> { new OutputChoice { Label = "Windows default", Detail = master == null ? null : "Same as master  ·  " + master.Name, Selected = routed == null } };
            choices.AddRange(outputs.Select(d => new OutputChoice { Id = d.Id, Label = d.Name, Detail = SameOutput(d.Id, session.DeviceId) ? "Playing here now" : null, Selected = routed != null && SameOutput(d.Id, routed) }));
            string note = system ? "System sounds always play on the master output. Change it at the top of the mixer."
                : !routable ? "Windows didn't allow changing this app's output here. Use the gear to open Windows sound settings."
                : missing ? "This app is set to an output that isn't connected. Choose another output or Windows default."
                : "Windows remembers this for the app. Some apps switch only after they restart.";
            outputPicker.Show("APP OUTPUT", session.Name, choices, routable, note, async choice => {
                try { await audio.Run(a => a.SetAppOutput(session.ProcessId, choice.Id)); if (!disposed) Notify(session.Name + (choice.Id == null ? " now follows the master output." : " now plays on " + choice.Label + ".")); }
                catch (Exception) { if (!disposed) Notify("Couldn't change the output for " + session.Name + ". Use the gear to open Windows sound settings."); }
                if (!disposed) RefreshAudio(true);
            });
        }

        // Runs a mixer change on the audio thread, then reads the mixer again so the dashboard and a phone see it.
        private async void AudioAction(Action<AudioService> action, bool refresh = true)
        {
            try { await audio.Run(action); if (!disposed && refresh) RefreshAudio(true); }
            catch (Exception) { if (!disposed) { Notify("The audio device changed or went away. Reconnecting…"); RefreshAudio(true); } }
        }

        // The last audio reading, shared with a phone's /state for a second; fresh reads again (after a change).
        private Task<AudioSnapshot> ReadAudioAsync(bool fresh)
        {
            if (!fresh && lastAudio != null && DateTime.UtcNow - lastAudioAt < SnapshotLifetime) return Task.FromResult(lastAudio);
            if (audioRead != null && !audioRead.IsCompleted) return audioRead;
            audioRead = StoreAudio(audio.Run(a => a.Read()));
            return audioRead;
        }
        private async Task<AudioSnapshot> StoreAudio(Task<AudioSnapshot> read)
        {
            try { var snapshot = await read; lastAudio = snapshot; lastAudioAt = DateTime.UtcNow; return snapshot; }
            catch (Exception) { lastAudio = null; throw; }
        }
        private Task<SystemSnapshot> ReadSystemAsync(bool cpu, bool gpu, bool ram, bool network, bool uptime)
        {
            string flags = "" + cpu + gpu + ram + network + uptime;
            if (lastSystem != null && flags == lastSystemFlags && DateTime.UtcNow - lastSystemAt < SnapshotLifetime) return Task.FromResult(lastSystem);
            if (systemRead != null && !systemRead.IsCompleted && flags == lastSystemFlags) return systemRead;
            lastSystemFlags = flags;
            systemRead = StoreSystem(Task.Run(() => system.Read(cpu, gpu, ram, network, uptime)));
            return systemRead;
        }
        private async Task<SystemSnapshot> StoreSystem(Task<SystemSnapshot> read)
        {
            try { var snapshot = await read; lastSystem = snapshot; lastSystemAt = DateTime.UtcNow; return snapshot; }
            catch (Exception) { lastSystem = null; throw; }
        }
        private Task<MediaSnapshot> ReadMediaAsync(bool fresh)
        {
            if (!fresh && lastMedia != null && DateTime.UtcNow - lastMediaAt < SnapshotLifetime) return Task.FromResult(lastMedia);
            if (mediaRead != null && !mediaRead.IsCompleted) return mediaRead;
            mediaRead = StoreMedia(media.ReadAsync());
            return mediaRead;
        }
        private async Task<MediaSnapshot> StoreMedia(Task<MediaSnapshot> read)
        {
            try { var snapshot = await read; lastMedia = snapshot; lastMediaAt = DateTime.UtcNow; return snapshot; }
            catch (Exception) { lastMedia = null; throw; }
        }

        private async void RefreshSystem()
        {
            if (disposed || systemBusy || !window.IsLoaded) return;
            systemBusy = true;
            var requested = settings;
            bool readStats = Editor.IsVisible("System");
            try
            {
                var snapshot = await ReadSystemAsync(readStats && requested.ShowCpu, readStats && requested.ShowGpu, readStats && requested.ShowRam, readStats && requested.ShowNetwork, readStats && requested.ShowUptime);
                if (disposed || requested != settings) return;
                Text("SystemStatus", ""); systemCard.Update(snapshot);
            }
            catch (Exception)
            {
                if (disposed) return;
                Text("SystemStatus", "UNAVAILABLE"); systemCard.Unavailable();
            }
            finally { systemBusy = false; if (!disposed && requested != settings) RefreshSystem(); }
        }

        private async void RefreshMedia(bool fresh = false)
        {
            if (disposed || !Editor.IsVisible("Media")) return;
            if (mediaBusy || mediaCommandBusy || mediaScrubber.Active) return; mediaBusy = true;
            try
            {
                var snapshot = await ReadMediaAsync(fresh); if (disposed) return;
                Text("TrackTitle", snapshot.Available ? snapshot.Title : "Nothing playing");
                Text("TrackArtist", snapshot.Available ? (snapshot.Artist ?? "") : "Start music or a video in any app.");
                Text("MediaSource", !snapshot.Available ? "" : MediaService.SourceName(snapshot.Source).ToUpperInvariant());
                Control<Button>("MediaVolumeButton").IsEnabled = snapshot.Available;
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
                Text("MediaHint", "");
            }
            catch (Exception)
            {
                if (disposed) return;
                Text("TrackTitle", "Media controls unavailable"); Text("TrackArtist", "Windows isn't sharing what's playing right now.");
                Text("MediaSource", ""); Text("MediaPosition", "—:—"); Text("MediaDuration", "—:—");
                mediaScrubber.Update(null, 0, 0, false); Control<Image>("AlbumArt").Source = null;
                Text("MediaHint", "Retrying automatically.");
                Control<Button>("PlayButton").IsEnabled = false; Control<Button>("PreviousButton").IsEnabled = false; Control<Button>("NextButton").IsEnabled = false;
                Control<Button>("MediaVolumeButton").IsEnabled = false; CloseMediaVolume();
            }
            finally { mediaBusy = false; }
        }
        private async void MediaCommand(string command)
        {
            if (mediaCommandBusy || disposed) return;
            mediaCommandBusy = true;
            Control<Button>("PlayButton").IsEnabled = false; Control<Button>("PreviousButton").IsEnabled = false; Control<Button>("NextButton").IsEnabled = false;
            try { if (!await media.CommandAsync(command)) Notify("The media app didn't respond to that control."); }
            catch (Exception) { Notify("Media controls aren't available. Check that your media app is open."); }
            finally { mediaCommandBusy = false; if (!disposed) RefreshMedia(true); }
        }
        private async void SeekMedia(string key, double position)
        {
            if (mediaCommandBusy || disposed) return;
            mediaCommandBusy = true;
            try { if (!await media.SeekAsync(key, position)) Text("MediaHint", "Couldn't jump there. The app may not allow it, or the song changed."); }
            catch (Exception) { Text("MediaHint", "This media app doesn't allow jumping to a position."); }
            finally { mediaCommandBusy = false; }
        }
        private static string FormatTime(TimeSpan time) { return ((int)time.TotalMinutes).ToString("0") + ":" + time.Seconds.ToString("00"); }

        // The playing app's mixer row, one tap from the Now Playing card: a small panel with that app's slider (master
        // volume when the app isn't in the mixer, or the mixer hasn't been read). Null means master.
        private string mediaVolumeSession;
        private async void OpenMediaVolume()
        {
            if (lastMedia == null || !lastMedia.Available) return;
            AudioSnapshot snapshot = null;
            try { snapshot = await ReadAudioAsync(false); } catch (Exception) { }
            if (disposed || snapshot == null) { Text("MediaHint", "The mixer isn't available right now."); return; }
            var session = MatchSession(snapshot.Sessions, MediaService.SourceName(lastMedia.Source));
            mediaVolumeSession = session == null ? null : session.Id;
            Text("MediaVolumeSubject", session == null ? "Master volume" : session.Name);
            Text("MediaVolumeNote", session == null ? "This app isn't in the mixer yet, so this is the master volume." : "Only " + session.Name + ". Master volume stays where it is.");
            updatingAudio = true;
            try { Control<Slider>("MediaVolumeChoice").Value = (session == null ? snapshot.Volume : session.Volume) * 100; }
            finally { updatingAudio = false; }
            Text("MediaVolumePercent", Math.Round((session == null ? snapshot.Volume : session.Volume) * 100) + "%");
            Control<Border>("MediaVolumePanel").Visibility = Visibility.Visible;
            Control<Slider>("MediaVolumeChoice").Focus();
        }
        private void CloseMediaVolume() { Control<Border>("MediaVolumePanel").Visibility = Visibility.Collapsed; mediaVolumeSession = null; }
        // The mixer session that belongs to the playing app: its process name and the app's short name share a word.
        public static AudioSession MatchSession(IList<AudioSession> sessions, string appName)
        {
            if (String.IsNullOrWhiteSpace(appName)) return null;
            string wanted = appName.Replace(" ", "").ToLowerInvariant();
            foreach (var session in sessions)
            {
                if (session.ProcessId == 0 || String.IsNullOrEmpty(session.Name)) continue;
                string name = session.Name.Replace(" ", "").ToLowerInvariant();
                if (name.Contains(wanted) || wanted.Contains(name)) return session;
            }
            return null;
        }

        private bool WeatherImperial { get { return settings.WeatherUnits == "imperial"; } }
        // WeatherCard shows the reading in the design for its size; the "updated" time follows the clock's choice.
        private async void RefreshWeather()
        {
            if (disposed || weatherBusy || !Editor.IsVisible("Weather")) return;
            weatherCard.Configure(settings.WeatherLocation, WeatherImperial, settings.Use24HourClock);
            if (String.IsNullOrWhiteSpace(settings.WeatherLocation)) return;
            weatherBusy = true; lastWeatherAttempt = DateTime.UtcNow; int generation = weatherGeneration;
            weatherCard.Updating();
            try
            {
                var snapshot = await weather.ReadAsync(settings.Latitude, settings.Longitude, WeatherImperial);
                if (disposed || generation != weatherGeneration) return;
                weatherCard.Reading(snapshot);
            }
            catch (Exception)
            {
                if (generation == weatherGeneration) weatherCard.Failed();
            }
            finally
            {
                weatherBusy = false;
                if (!disposed && generation != weatherGeneration) RefreshWeather();
            }
        }

        private void OpenSettings()
        {
            openingSettings = true;
            outputPicker.Close();
            spotifyAccount.Prepare(); discordAccount.Prepare();
            settingsGeneration++;
            var monitors = DisplayService.Screens.Where(s => !s.Primary).Select(s => new MonitorOption { Id = s.DeviceName, Label = s.DeviceName + "  ·  " + s.Bounds.Width + " × " + s.Bounds.Height }).ToList();
            if (monitors.Count == 0) monitors.Add(new MonitorOption { Id = "", Label = "No second display connected" });
            var monitorChoice = Control<ComboBox>("MonitorChoice"); monitorChoice.ItemsSource = monitors; monitorChoice.SelectedItem = monitors.FirstOrDefault(m => m.Id == settings.Monitor) ?? monitors[0];
            var accent = Control<ComboBox>("AccentChoice"); accent.ItemsSource = new[] { "Lime", "Glacier", "Lavender", "Amber" }; accent.SelectedItem = settings.Accent;
            var theme = Control<ComboBox>("ThemeChoice"); theme.ItemsSource = new[] { "Midnight", "Slate", "Warm" }; theme.SelectedItem = settings.Theme;
            Control<Slider>("CornersChoice").Value = settings.CornerRadius;
            Control<CheckBox>("StartFullscreenChoice").IsChecked = settings.StartFullscreen;
            Control<CheckBox>("KeepFocusChoice").IsChecked = settings.KeepGameFocus;
            Control<CheckBox>("CpuChoice").IsChecked = settings.ShowCpu; Control<CheckBox>("GpuChoice").IsChecked = settings.ShowGpu;
            Control<CheckBox>("RamChoice").IsChecked = settings.ShowRam; Control<CheckBox>("NetworkChoice").IsChecked = settings.ShowNetwork; Control<CheckBox>("UptimeChoice").IsChecked = settings.ShowUptime;
            Control<Slider>("ScaleChoice").Value = settings.TextScale * 100;
            Control<CheckBox>("ClockChoice").IsChecked = settings.Use24HourClock; Control<CheckBox>("LevelsChoice").IsChecked = settings.ShowLevels;
            Control<CheckBox>("FocusChimeChoice").IsChecked = settings.FocusChime;
            Control<Slider>("StorageLowChoice").Value = settings.StorageLowPercent; Text("StorageLowLabel", settings.StorageLowPercent + "% free");
            pendingLocation = String.IsNullOrWhiteSpace(settings.WeatherLocation) ? null : new WeatherLocation { Name = settings.WeatherLocation, Latitude = settings.Latitude, Longitude = settings.Longitude };
            ShowCityResults(pendingLocation == null ? null : new List<WeatherLocation> { pendingLocation });
            Control<ComboBox>("CityResults").SelectedItem = pendingLocation; Control<TextBox>("CitySearch").Text = "";
            var units = Control<ComboBox>("WeatherUnitsChoice"); units.ItemsSource = new[] { "Celsius and km/h", "Fahrenheit and mph" }; units.SelectedIndex = WeatherImperial ? 1 : 0;
            OpenClockSettings(); OpenPrayerSettings(); OpenClipboardSettings(); macroEditor.Begin(settings.Macros); OpenCalendarSettings(); OpenHomeSettings();
            Control<CheckBox>("SteamGamesChoice").IsChecked = settings.ShowSteamGames; Control<CheckBox>("EpicGamesChoice").IsChecked = settings.ShowEpicGames; Control<CheckBox>("XboxGamesChoice").IsChecked = settings.ShowXboxGames;
            Text("SettingsStatus", ""); UpdateRemoteSettings(); UpdateClaudeSettings();
            deckEditor.Begin(settings.Shortcuts); Control<Grid>("DashboardContent").IsEnabled = false; Control<Grid>("SettingsOverlay").Visibility = Visibility.Visible;
            Control<TabControl>("SettingsTabs").SelectedIndex = 0;
            openingSettings = false; PreviewAppearance();
            ((TabItem)Control<TabControl>("SettingsTabs").Items[0]).Focus();
        }
        private void CloseSettings() { spotifyAccount.Cancel(); discordAccount.Cancel(); settingsGeneration++; Control<Grid>("SettingsOverlay").Visibility = Visibility.Collapsed; ApplySettings(); Control<Grid>("DashboardContent").IsEnabled = true; Control<Button>("SettingsButton").Focus(); }

        private void SaveSettings()
        {
            string spotifyId = Control<TextBox>("SpotifyClientId").Text.Trim();
            if (spotifyId.Length > 0 && !SpotifyService.ValidClientId(spotifyId)) { Text("SettingsStatus", "Spotify: the Client ID must be 32 letters and numbers. Fix it, or clear the field to keep the saved one."); return; }
            List<Shortcut> shortcuts;
            if (!deckEditor.TryGet(out shortcuts)) return;
            List<Macro> macros;
            if (!macroEditor.TryGet(out macros)) return;
            string calendarError = SaveCalendarFeeds();
            if (calendarError != null) { Text("SettingsStatus", calendarError); return; }
            string discordError = discordAccount.SaveDraft();
            if (discordError != null) { Text("SettingsStatus", discordError); return; }
            var monitor = Control<ComboBox>("MonitorChoice").SelectedItem as MonitorOption;
            // Only the fields the panel shows change; pages, the phone link and everything else come along unchanged.
            var candidate = settings.Copy();
            candidate.SpotifyClientId = spotifyId.Length == 0 ? settings.SpotifyClientId : spotifyId;
            candidate.Theme = (string)Control<ComboBox>("ThemeChoice").SelectedItem; candidate.CornerRadius = Control<Slider>("CornersChoice").Value;
            candidate.StartFullscreen = Control<CheckBox>("StartFullscreenChoice").IsChecked == true;
            candidate.KeepGameFocus = Control<CheckBox>("KeepFocusChoice").IsChecked == true;
            candidate.ShowCpu = Control<CheckBox>("CpuChoice").IsChecked == true; candidate.ShowGpu = Control<CheckBox>("GpuChoice").IsChecked == true;
            candidate.ShowRam = Control<CheckBox>("RamChoice").IsChecked == true; candidate.ShowNetwork = Control<CheckBox>("NetworkChoice").IsChecked == true; candidate.ShowUptime = Control<CheckBox>("UptimeChoice").IsChecked == true;
            candidate.Accent = (string)Control<ComboBox>("AccentChoice").SelectedItem; candidate.TextScale = Control<Slider>("ScaleChoice").Value / 100;
            candidate.Monitor = monitor == null ? "" : monitor.Id; candidate.ShowLevels = Control<CheckBox>("LevelsChoice").IsChecked == true;
            candidate.Use24HourClock = Control<CheckBox>("ClockChoice").IsChecked == true;
            candidate.FocusChime = Control<CheckBox>("FocusChimeChoice").IsChecked == true;
            candidate.StorageLowPercent = (int)Math.Round(Control<Slider>("StorageLowChoice").Value);
            candidate.WorldClocks = ClockChoices();
            SavePrayerChoices(candidate); SaveClipboardChoices(candidate); candidate.Macros = macros.ToArray(); candidate.HomeEntities = HomeChoicesToSave();
            candidate.ShowSteamGames = Control<CheckBox>("SteamGamesChoice").IsChecked == true; candidate.ShowEpicGames = Control<CheckBox>("EpicGamesChoice").IsChecked == true; candidate.ShowXboxGames = Control<CheckBox>("XboxGamesChoice").IsChecked == true;
            SaveClaudeChoices(candidate);
            candidate.Shortcuts = shortcuts; candidate.WeatherLocation = pendingLocation == null ? "" : pendingLocation.Name;
            candidate.Latitude = pendingLocation == null ? 0 : pendingLocation.Latitude; candidate.Longitude = pendingLocation == null ? 0 : pendingLocation.Longitude;
            candidate.WeatherUnits = Control<ComboBox>("WeatherUnitsChoice").SelectedIndex == 1 ? "imperial" : "metric";
            try { SaveSettingsFile(candidate); }
            catch (Exception e) { Text("SettingsStatus", "Could not save settings: " + e.Message); return; }
            bool locationChanged = settings.WeatherLocation != candidate.WeatherLocation || settings.Latitude != candidate.Latitude || settings.Longitude != candidate.Longitude || settings.WeatherUnits != candidate.WeatherUnits;
            bool storesChanged = settings.ShowSteamGames != candidate.ShowSteamGames || settings.ShowEpicGames != candidate.ShowEpicGames || settings.ShowXboxGames != candidate.ShowXboxGames;
            settings = candidate; CloseSettings(); if (storesChanged) ApplyGames(); lastHomeAttempt = DateTime.MinValue; ShowHome(); RefreshSystem();
            if (fullscreen) { if (!DisplayService.Fullscreen(window, settings.Monitor)) { DisplayService.Windowed(window); fullscreen = false; } UpdateDisplayStatus(); }
            FitScreen();
            if (locationChanged) { weatherGeneration++; weatherCard.Forget(); RefreshWeather(); }
            Notify("Settings saved.");
        }

        // The results list only appears once there is something to pick.
        private void ShowCityResults(List<WeatherLocation> results)
        {
            var list = Control<ComboBox>("CityResults"); list.ItemsSource = results;
            list.Visibility = results != null && results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SearchCity()
        {
            int generation = settingsGeneration;
            var button = Control<Button>("SearchCityButton"); button.IsEnabled = false; button.Content = "Searching…"; Text("SettingsStatus", "Searching for cities…");
            try
            {
                var results = await weather.SearchAsync(Control<TextBox>("CitySearch").Text);
                if (disposed || generation != settingsGeneration) return;
                ShowCityResults(results);
                Text("SettingsStatus", results.Count == 0 ? "No cities found. Try a nearby city or another spelling." : "Pick your city from the list, then save.");
                if (results.Count > 0) Control<ComboBox>("CityResults").IsDropDownOpen = true;
            }
            catch (Exception e) { if (!disposed && generation == settingsGeneration) Text("SettingsStatus", "City search didn't work. Check your connection and try again. (" + e.GetBaseException().Message + ")"); }
            finally { if (!disposed) { button.IsEnabled = true; button.Content = "Search"; } }
        }

        private void Persist() { try { SaveSettingsFile(settings); } catch (Exception e) { Notify("Settings could not be saved: " + e.Message); } }
        // Every settings save goes through here, so the phone's layout version can tell that something changed.
        private void SaveSettingsFile(AppSettings which) { SettingsStore.Save(settingsPath, which); settingsVersion++; }
        private void ToggleFullscreen()
        {
            if (fullscreen) { DisplayService.Windowed(window); fullscreen = false; }
            else if (DisplayService.Fullscreen(window, settings.Monitor)) fullscreen = true;
            else Notify("Fullscreen needs a second display. Connect one, then try again.");
            FitScreen(); UpdateDisplayStatus();
        }
        private void UpdateDisplayStatus()
        {
            if (!Editor.Editing) Text("DisplayStatus", "");
            Control<Button>("FullscreenButton").Content = fullscreen ? "Window preview" : "Second screen";
        }
        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (Editor.Editing)
            {
                if (e.Key == Key.Escape) { if (editPanelOpen) CloseEditPanel(); else CancelLayoutEdit(); e.Handled = true; return; }
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && (e.Key == Key.Z || e.Key == Key.Y))
                {
                    if (e.Key == Key.Y || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) Editor.Redo(); else Editor.Undo();
                    e.Handled = true; return;
                }
                if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                {
                    // Keep dropdown navigation intact; arrows on a widget move it, Shift+arrows resize it.
                    if (Keyboard.FocusedElement is ComboBox || Keyboard.FocusedElement is TextBox) return;
                    double step = (Keyboard.Modifiers & ModifierKeys.Control) != 0 ? 1 : 8;
                    Editor.TransformSelected((Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? "SE" : "", e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0, e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0, false);
                    e.Handled = true; return;
                }
            }
            if (e.Key == Key.Escape)
            {
                if (confirm.IsOpen) confirm.Cancel();
                else if (Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) CloseSettings(); else if (fullscreen) ToggleFullscreen(); e.Handled = true;
            }
            else if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; }
        }
        private void DisplaysChanged(object sender, EventArgs e)
        {
            if (disposed) return;
            window.Dispatcher.BeginInvoke(new Action(() => {
                if (disposed) return;
                if (fullscreen && !DisplayService.Fullscreen(window, settings.Monitor)) { DisplayService.Windowed(window); fullscreen = false; Notify("Your second display was disconnected, so the dashboard moved to a window.", true); }
                FitScreen(); UpdateDisplayStatus();
            }));
        }
        public void Dispose()
        {
            SaveNotesDraft(); disposed = true; DisposeRemote(); tray.Dispose(); clockTimer.Stop(); levelTimer.Stop(); SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            weather.Dispose(); calendar.Dispose(); home.Dispose(); prayerAlert.Dispose(); clipboard.Dispose(); spotifyAccount.Dispose(); spotify.Dispose(); discordAccount.Dispose(); soundboard.Dispose(); claude.Dispose(); deckEditor.Dispose(); deckHotkeys.Dispose(); audio.Dispose();
            // Closing the UI must not wait for a performance-counter provider.
            Task.Run(() => system.Dispose());
        }
    }
}
