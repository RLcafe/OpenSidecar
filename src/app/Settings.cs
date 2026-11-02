using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace WidgetScreen
{
    public sealed class Shortcut
    {
        public string Label { get; set; }
        public string Target { get; set; }
        public string Glyph { get; set; }
        public string Action { get; set; }
        public string Arguments { get; set; }
        public bool Confirm { get; set; }
        // The glyph's colour: one of the four accents, or empty for the theme's accent.
        public string Color { get; set; }
        public Shortcut Copy() { return (Shortcut)MemberwiseClone(); }
    }

    // A project's tick in Settings → Claude → Projects, by its folder (docs/design/claude-watcher.md).
    public sealed class ClaudeProjectChoice
    {
        [XmlAttribute] public string Folder { get; set; }
        [XmlAttribute] public bool Shown { get; set; }
        public ClaudeProjectChoice Copy() { return (ClaudeProjectChoice)MemberwiseClone(); }
    }

    public sealed class AppSettings
    {
        public int Version { get; set; }
        public string Accent { get; set; }
        // Sticker Sheet inks (decision 021): "Focus=Coral;…" for widgets whose ink was changed in Edit layout, and
        // Settings → Theme → Widget colours: "Each" (every widget its own ink) or "Accent" (every ink is the accent).
        public string WidgetInks { get; set; }
        public string WidgetColours { get; set; }
        public string SpotifyClientId { get; set; }
        public string Theme { get; set; }
        public double CornerRadius { get; set; }
        public bool StartFullscreen { get; set; }
        // Taps on the dashboard leave the game or app in front focused (decision 013).
        public bool KeepGameFocus { get; set; }
        public bool ShowCpu { get; set; }
        public bool ShowGpu { get; set; }
        public bool ShowRam { get; set; }
        public bool ShowNetwork { get; set; }
        public bool ShowUptime { get; set; }
        // Hardware sensors (decision 016): more readings, each reading's graph style, and full-sensors mode.
        public bool ShowCpuTemperature { get; set; }
        public bool ShowGpuTemperature { get; set; }
        public bool ShowVram { get; set; }
        public bool ShowFps { get; set; }
        // "Cpu=Ring;Fps=Bars": Auto, Line, Bars, Sparkline, Ring or Gauge per reading (SystemStyles).
        public string StatStyles { get; set; }
        // Starts the elevated copy through the "Widget Screen full sensors" task (FullSensors).
        public bool FullSensors { get; set; }
        public double ClockWidth { get; set; }
        public double MediaWidth { get; set; }
        public double MixerWidth { get; set; }
        public double SystemWidth { get; set; }
        public double ClockHeight { get; set; }
        public double MediaHeight { get; set; }
        public double SystemHeight { get; set; }
        public double MixerHeight { get; set; }
        // The layout of the reference 1856 × 576 canvas (a 1920 × 720 screen), where it has always been.
        public WidgetPlacement[] WidgetLayout { get; set; }
        // Layouts saved on screens of other shapes, one per canvas size (docs/design/any-screen-size.md).
        public CanvasLayout[] CanvasLayouts { get; set; }
        // Pages: the fields above are the first page, named PageName; MorePages hold the rest. CurrentPage is the page
        // shown, on the PC and on a paired phone alike.
        public string PageName { get; set; }
        public LayoutPage[] MorePages { get; set; }
        public int CurrentPage { get; set; }
        public bool SnapWidgets { get; set; }
        public double TextScale { get; set; }
        public string Monitor { get; set; }
        public bool QuietMode { get; set; }
        public bool ShowLevels { get; set; }
        public bool Use24HourClock { get; set; }
        public bool SwapSideWidgets { get; set; }
        // Focus timer: a chime when a session ends (off in Quiet mode), the fourth preset's length (0 = none yet), and
        // today's finished sessions, which reset on the first session of a new day.
        public bool FocusChime { get; set; }
        // The one-time tips in the header were put away with Got it.
        public bool HintDismissed { get; set; }
        public int FocusCustomMinutes { get; set; }
        public string FocusSessionDate { get; set; }
        public int FocusSessionsToday { get; set; }
        public int FocusMinutesToday { get; set; }
        // Phone remote (Settings → Phone). Off until the user turns it on.
        public bool RemoteEnabled { get; set; }
        // Move the dashboard to the tray while a paired phone shows it (Settings → Phone).
        public bool HideToTrayWithPhone { get; set; }
        // Storage: a drive with less free space than this percentage shows in the warning colour (Settings → Widgets).
        public int StorageLowPercent { get; set; }
        // World clocks: up to three time zones and the city names shown for them (Settings → Widgets).
        public WorldClock[] WorldClocks { get; set; }
        // Prayer times (Settings → Prayer): a city of its own (empty follows the weather city), the calculation method,
        // the Asr school, and the alert at each prayer with an audio file (empty plays the built-in chime).
        public string PrayerLocation { get; set; }
        public double PrayerLatitude { get; set; }
        public double PrayerLongitude { get; set; }
        public string PrayerMethod { get; set; }
        public bool PrayerHanafi { get; set; }
        public bool PrayerAlert { get; set; }
        public string PrayerSound { get; set; }
        // Claude Code (Settings → Claude): a chime when a session starts needing you, and which projects the card shows.
        // A project with no choice follows ClaudeShowNewProjects (on by default). The plan meters always show the plan.
        public bool ClaudeChime { get; set; }
        // A Windows notification when a session needs you, gets stuck or finishes a turn of 10 minutes or more (package 3;
        // on by default, silent in Quiet mode).
        public bool ClaudeToast { get; set; }
        // Quiet mode stops other motion; the Claude cars keep moving unless this is set (Ali, 1 Nov 2026; decision 019).
        public bool ClaudeStillInQuiet { get; set; }
        // The ledger's "API value" (what the tokens would cost at API list prices): off by default, since a plan
        // doesn't pay those prices and Ali can't check them; replies show instead (decision 019).
        public bool ClaudeShowApiValue { get; set; }
        // Resolved and archived threads are hidden unless this is set ("Hide resolved and archived threads", on by default), and idle ones when
        // ClaudeHideIdle is (off by default). Hidden threads still count in their project's totals (Ali, 1 Nov 2026; decision 020).
        public bool ClaudeShowArchived { get; set; }
        public bool ClaudeHideIdle { get; set; }
        public bool ClaudeShowNewProjects { get; set; }
        [XmlArray("ClaudeProjects"), XmlArrayItem("Project")]
        public ClaudeProjectChoice[] ClaudeProjects { get; set; }
        // How often the card re-reads sessions, threads, telemetry totals and logs while it's on a page (default 2 s),
        // and how often the plan check asks Anthropic (default 300 s): the user's numbers, Quiet mode or not (Ali, 1 Nov 2026).
        public int ClaudeCardSeconds { get; set; }
        public int ClaudePlanSeconds { get; set; }
        // Project folders folded into their big critter on the card (the phone shares them), and the period the card
        // and the phone count threads and usage over: FiveHour, Today or Week.
        [XmlArray("ClaudeCollapsed"), XmlArrayItem("Folder")]
        public string[] ClaudeCollapsed { get; set; }
        public string ClaudePeriod { get; set; }
        // The four facts on the Claude card's side panel (package 2), from ClaudeFactKinds.All.
        [XmlArray("ClaudeFacts"), XmlArrayItem("Fact")]
        public string[] ClaudeFacts { get; set; }
        // Clipboard history (Settings → Widgets): how many texts to keep, and whether they outlast the app (encrypted).
        public int ClipboardSize { get; set; }
        public bool ClipboardKeep { get; set; }
        // Games (Settings → Widgets): which stores the Games widget lists.
        public bool ShowSteamGames { get; set; }
        public bool ShowEpicGames { get; set; }
        public bool ShowXboxGames { get; set; }
        // Smart home (Settings → Home): the Home Assistant devices the card shows, in order. The address and token are
        // kept in home-assistant.dat, never here.
        [XmlArray("HomeEntities"), XmlArrayItem("Entity")]
        public string[] HomeEntities { get; set; }
        // Macros (Settings → Macros): buttons that each run a chain of steps (docs/design/macros.md). Empty by default.
        [XmlArray("Macros"), XmlArrayItem("Macro")]
        public Macro[] Macros { get; set; }
        public string WeatherLocation { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        // "metric" (Celsius, km/h) or "imperial" (Fahrenheit, mph); anything else becomes the region's default.
        public string WeatherUnits { get; set; }
        [XmlIgnore]
        public List<Shortcut> Shortcuts { get; set; }

        // XmlSerializer appends to an initialized List. An array proxy replaces the
        // defaults on load while preserving the existing settings.xml structure.
        [XmlArray("Shortcuts"), XmlArrayItem("Shortcut")]
        public Shortcut[] SerializedShortcuts
        {
            get { return Shortcuts == null ? null : Shortcuts.ToArray(); }
            set { Shortcuts = value == null ? null : new List<Shortcut>(value); }
        }

        public AppSettings()
        {
            Version = 1;
            Accent = "Lime";
            WidgetInks = ""; WidgetColours = Inks.EachOwn;
            Theme = "Midnight";
            CornerRadius = 20;
            StartFullscreen = true;
            KeepGameFocus = true;
            SnapWidgets = true;
            ShowCpu = ShowGpu = ShowRam = ShowNetwork = ShowUptime = true;
            ShowCpuTemperature = ShowGpuTemperature = ShowVram = ShowFps = true;
            StatStyles = "";
            ClockWidth = 1; MediaWidth = 3; MixerWidth = 3; SystemWidth = 2;
            ClockHeight = 246; MediaHeight = 326; SystemHeight = 326; MixerHeight = 576;
            TextScale = 1;
            ShowLevels = true;
            Use24HourClock = false;
            // Tray mode with the phone is opt-in (audit, 22 Oct 2026): the dashboard vanishing when the phone app opens
            // surprised more than it helped when both screens were in use.
            HideToTrayWithPhone = false;
            FocusChime = true;
            StorageLowPercent = StorageService.DefaultLowPercent;
            ClaudeShowNewProjects = true;
            ClaudeToast = true;
            ClaudeCardSeconds = ClaudeIntervals.CardDefault; ClaudePlanSeconds = ClaudeIntervals.PlanDefault;
            ClaudePeriod = "Today";
            Monitor = "";
            WeatherLocation = "";
            WeatherUnits = RegionWeatherUnits();
            PrayerLocation = ""; PrayerSound = "";
            ClipboardSize = ClipboardHistory.DefaultSize;
            ShowSteamGames = ShowEpicGames = ShowXboxGames = true;
            PrayerMethod = PrayerService.RegionMethod();
            Shortcuts = DefaultShortcuts();
        }

        // A deep copy. Settings → Save starts from one and overwrites only the fields the panel owns, so everything
        // the panel doesn't show (pages, the current page, the phone link, Quiet mode) survives a save.
        public AppSettings Copy()
        {
            var copy = (AppSettings)MemberwiseClone();
            copy.Shortcuts = Shortcuts == null ? null : Shortcuts.Select(s => s.Copy()).ToList();
            copy.WidgetLayout = WidgetLayout == null ? null : WidgetScreen.WidgetLayout.Copy(WidgetLayout);
            copy.CanvasLayouts = CanvasLayouts == null ? null : CanvasLayouts.Where(c => c != null).Select(c => c.Copy()).ToArray();
            copy.Macros = Macros == null ? null : Macros.Where(m => m != null).Select(m => m.Copy()).ToArray();
            copy.WorldClocks = WorldClocks == null ? null : WorldClocks.Where(c => c != null).Select(c => c.Copy()).ToArray();
            copy.ClaudeProjects = ClaudeProjects == null ? null : ClaudeProjects.Where(c => c != null).Select(c => c.Copy()).ToArray();
            copy.ClaudeCollapsed = ClaudeCollapsed == null ? null : ClaudeCollapsed.ToArray();
            copy.ClaudeFacts = ClaudeFacts == null ? null : ClaudeFacts.ToArray();
            copy.MorePages = MorePages == null ? null : MorePages.Where(p => p != null).Select(p => p.Copy()).ToArray();
            return copy;
        }

        public static List<Shortcut> DefaultShortcuts()
        {
            return new List<Shortcut> {
                new Shortcut { Label = "Spotify", Action = "App", Target = "spotify:", Glyph = "\uE8D6" },
                new Shortcut { Label = "Browser", Action = "Website", Target = "https://www.google.com", Glyph = "\uE774" },
                new Shortcut { Label = "Files", Action = "App", Target = "%windir%\\explorer.exe", Glyph = "\uE8B7" },
                new Shortcut { Label = "Calculator", Action = "App", Target = "%windir%\\System32\\calc.exe", Glyph = "\uE8EF" },
                new Shortcut { Label = "Notes", Action = "App", Target = "%windir%\\System32\\notepad.exe", Glyph = "\uE70B" },
                new Shortcut { Label = "Sound", Action = "App", Target = "ms-settings:sound", Glyph = "\uE767" }
            };
        }

        public void Normalize()
        {
            // Up to 140% (audit, 22 Oct 2026): small or far-away screens need more than the 115% the baseline allowed.
            TextScale = Math.Max(0.9, Math.Min(1.4, Double.IsNaN(TextScale) ? 1 : TextScale));
            if (Accent != "Lime" && Accent != "Glacier" && Accent != "Lavender" && Accent != "Amber") Accent = "Lime";
            if (Theme != "Midnight" && Theme != "Slate" && Theme != "Warm") Theme = "Midnight";
            WidgetInks = Inks.Clean(WidgetInks); WidgetColours = Inks.Mode(WidgetColours);
            CornerRadius = Clamp(CornerRadius, 0, 24, 20);
            ClockWidth = Clamp(ClockWidth, 0, 10, 1); MediaWidth = Clamp(MediaWidth, 0, 10, 3);
            MixerWidth = Clamp(MixerWidth, 0, 10, 3); SystemWidth = Clamp(SystemWidth, 0, 10, 2);
            ClockHeight = Clamp(ClockHeight, 216, 276, 246);
            MediaHeight = Clamp(MediaHeight, 326, 376, 326);
            SystemHeight = Clamp(SystemHeight, 326, 356, 326);
            MixerHeight = Clamp(MixerHeight, 476, 576, 576);
            if (FocusCustomMinutes != 0) FocusCustomMinutes = Math.Max(5, Math.Min(180, FocusCustomMinutes));
            StorageLowPercent = StorageLowPercent == 0 ? StorageService.DefaultLowPercent : StorageService.ClampPercent(StorageLowPercent);
            WorldClocks = WorldClockService.Clean(WorldClocks);
            ClipboardSize = ClipboardHistory.ClampSize(ClipboardSize);
            ClaudeCardSeconds = ClaudeCardSeconds == 0 ? ClaudeIntervals.CardDefault : ClaudeIntervals.Card(ClaudeCardSeconds);
            ClaudePlanSeconds = ClaudePlanSeconds == 0 ? ClaudeIntervals.PlanDefault : ClaudeIntervals.Plan(ClaudePlanSeconds);
            ClaudeCollapsed = (ClaudeCollapsed ?? new string[0]).Select(ClaudeUsage.Key).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (ClaudePeriod != "FiveHour" && ClaudePeriod != "Today" && ClaudePeriod != "Week") ClaudePeriod = "Today";
            ClaudeFacts = ClaudeFactKinds.Clean(ClaudeFacts);
            HomeEntities = (HomeEntities ?? new string[0]).Where(id => !String.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct().Take(HomeAssistantService.MaxEntities).ToArray();
            Macros = MacroActions.Clean(Macros).ToArray();
            FocusSessionsToday = Math.Max(0, FocusSessionsToday); FocusMinutesToday = Math.Max(0, FocusMinutesToday);
            if (Shortcuts == null) Shortcuts = DefaultShortcuts();
            Shortcuts.RemoveAll(s => s == null);
            foreach (var shortcut in Shortcuts)
            {
                shortcut.Label = shortcut.Label ?? "Button";
                shortcut.Target = shortcut.Target ?? "";
                shortcut.Arguments = shortcut.Arguments ?? "";
                if (String.IsNullOrEmpty(shortcut.Action)) shortcut.Action = DeckActions.InferAction(shortcut.Target);
                if (String.IsNullOrEmpty(shortcut.Glyph)) shortcut.Glyph = "\uE8A7";
                if (shortcut.Color != "Lime" && shortcut.Color != "Glacier" && shortcut.Color != "Lavender" && shortcut.Color != "Amber") shortcut.Color = "";
            }
            if (Double.IsNaN(Latitude) || Double.IsInfinity(Latitude) || Double.IsNaN(Longitude) || Double.IsInfinity(Longitude) || Math.Abs(Latitude) > 90 || Math.Abs(Longitude) > 180) WeatherLocation = "";
            WeatherLocation = WeatherLocation ?? "";
            if (Double.IsNaN(PrayerLatitude) || Double.IsInfinity(PrayerLatitude) || Double.IsNaN(PrayerLongitude) || Double.IsInfinity(PrayerLongitude) || Math.Abs(PrayerLatitude) > 90 || Math.Abs(PrayerLongitude) > 180) PrayerLocation = "";
            PrayerLocation = PrayerLocation ?? ""; PrayerSound = PrayerSound ?? "";
            if (!PrayerService.KnownMethod(PrayerMethod)) PrayerMethod = PrayerService.RegionMethod();
            if (WeatherUnits != "metric" && WeatherUnits != "imperial") WeatherUnits = RegionWeatherUnits();
            var pages = WidgetScreen.WidgetLayout.Pages(this).Take(WidgetScreen.WidgetLayout.MaxPages).ToList();
            for (int i = 0; i < pages.Count; i++) WidgetScreen.WidgetLayout.NormalizePage(pages[i], this, i);
            WidgetScreen.WidgetLayout.SetPages(this, pages);
        }

        public static string RegionWeatherUnits()
        {
            try { return System.Globalization.RegionInfo.CurrentRegion.IsMetric ? "metric" : "imperial"; } catch (Exception) { return "metric"; }
        }

        private static double Clamp(double value, double min, double max, double fallback)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
        }
    }

    public static class SettingsStore
    {
        public static string SpotifyPath(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "spotify-session.dat"); }
        public static string DiscordPath(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "discord-session.dat"); }
        // The Claude Code bridge's folder (ClaudeHook.exe, sessions\, limits.json) beside settings.xml.
        public static string ClaudePath(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "claude"); }
        // The widget's own Claude sign-in (DPAPI-encrypted), beside settings.xml.
        public static string ClaudeSignInPath(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "claude-session.dat"); }
        public static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WidgetScreen", "settings.xml"); }
        }

        public static AppSettings Load(string path, out string warning)
        {
            warning = null;
            if (!File.Exists(path)) return new AppSettings();
            try
            {
                using (var file = File.OpenRead(path))
                {
                    var settings = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(file);
                    settings.Normalize();
                    return settings;
                }
            }
            catch (Exception e)
            {
                if (!(e is IOException) && !(e is InvalidOperationException) && !(e is UnauthorizedAccessException)) throw;
                warning = "Settings could not be read. Defaults are in use; the original file is preserved until you save.";
                return new AppSettings();
            }
        }

        public static void Save(string path, AppSettings settings)
        {
            settings.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            using (var file = File.Create(temporary)) new XmlSerializer(typeof(AppSettings)).Serialize(file, settings);
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
    }
}
