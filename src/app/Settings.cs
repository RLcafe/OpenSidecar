using System;
using System.Collections.Generic;
using System.IO;
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
        public Shortcut Copy() { return (Shortcut)MemberwiseClone(); }
    }

    public sealed class AppSettings
    {
        public int Version { get; set; }
        public string Accent { get; set; }
        public string SpotifyClientId { get; set; }
        public string Theme { get; set; }
        public double CornerRadius { get; set; }
        public bool StartFullscreen { get; set; }
        public bool ShowCpu { get; set; }
        public bool ShowGpu { get; set; }
        public bool ShowRam { get; set; }
        public bool ShowNetwork { get; set; }
        public bool ShowUptime { get; set; }
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
        public bool SnapWidgets { get; set; }
        public double TextScale { get; set; }
        public string Monitor { get; set; }
        public bool QuietMode { get; set; }
        public bool ShowLevels { get; set; }
        public bool Use24HourClock { get; set; }
        public bool SwapSideWidgets { get; set; }
        // Phone remote (Settings → Phone). Off until the user turns it on.
        public bool RemoteEnabled { get; set; }
        // Move the dashboard to the tray while a paired phone shows it (Settings → Phone).
        public bool HideToTrayWithPhone { get; set; }
        public string WeatherLocation { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
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
            Theme = "Midnight";
            CornerRadius = 20;
            StartFullscreen = true;
            SnapWidgets = true;
            ShowCpu = ShowGpu = ShowRam = ShowNetwork = ShowUptime = true;
            ClockWidth = 1; MediaWidth = 3; MixerWidth = 3; SystemWidth = 2;
            ClockHeight = 246; MediaHeight = 326; SystemHeight = 326; MixerHeight = 576;
            TextScale = 1;
            ShowLevels = true;
            Use24HourClock = false;
            HideToTrayWithPhone = true;
            Monitor = "";
            WeatherLocation = "";
            Shortcuts = DefaultShortcuts();
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
            TextScale = Math.Max(0.9, Math.Min(1.15, Double.IsNaN(TextScale) ? 1 : TextScale));
            if (Accent != "Lime" && Accent != "Glacier" && Accent != "Lavender" && Accent != "Amber") Accent = "Lime";
            if (Theme != "Midnight" && Theme != "Slate" && Theme != "Warm") Theme = "Midnight";
            CornerRadius = Clamp(CornerRadius, 0, 24, 20);
            ClockWidth = Clamp(ClockWidth, 0, 10, 1); MediaWidth = Clamp(MediaWidth, 0, 10, 3);
            MixerWidth = Clamp(MixerWidth, 0, 10, 3); SystemWidth = Clamp(SystemWidth, 0, 10, 2);
            ClockHeight = Clamp(ClockHeight, 216, 276, 246);
            MediaHeight = Clamp(MediaHeight, 326, 376, 326);
            SystemHeight = Clamp(SystemHeight, 326, 356, 326);
            MixerHeight = Clamp(MixerHeight, 476, 576, 576);
            if (Shortcuts == null) Shortcuts = DefaultShortcuts();
            Shortcuts.RemoveAll(s => s == null);
            foreach (var shortcut in Shortcuts)
            {
                shortcut.Label = shortcut.Label ?? "Button";
                shortcut.Target = shortcut.Target ?? "";
                shortcut.Arguments = shortcut.Arguments ?? "";
                if (String.IsNullOrEmpty(shortcut.Action)) shortcut.Action = DeckActions.InferAction(shortcut.Target);
                if (String.IsNullOrEmpty(shortcut.Glyph)) shortcut.Glyph = "\uE8A7";
            }
            if (Double.IsNaN(Latitude) || Double.IsInfinity(Latitude) || Double.IsNaN(Longitude) || Double.IsInfinity(Longitude) || Math.Abs(Latitude) > 90 || Math.Abs(Longitude) > 180) WeatherLocation = "";
            WeatherLocation = WeatherLocation ?? "";
            if (CanvasLayouts != null)
            {
                var layouts = new List<CanvasLayout>();
                foreach (var entry in CanvasLayouts)
                {
                    if (entry == null || entry.Widgets == null || !(entry.Width >= 64 && entry.Width <= 100000) || !(entry.Height >= 64 && entry.Height <= 100000)) continue;
                    var key = WidgetScreen.WidgetLayout.Key(entry.Size);
                    if (key == WidgetScreen.WidgetLayout.Reference) { if (WidgetLayout == null) WidgetLayout = entry.Widgets; continue; }
                    if (layouts.Exists(c => c.Size == key)) continue;
                    layouts.Add(new CanvasLayout { Width = key.Width, Height = key.Height, Widgets = WidgetScreen.WidgetLayout.Normalize(entry.Widgets, this, key) });
                }
                CanvasLayouts = layouts.Count == 0 ? null : layouts.ToArray();
            }
            if (WidgetLayout != null) WidgetLayout = WidgetScreen.WidgetLayout.Normalize(WidgetLayout, this);
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
