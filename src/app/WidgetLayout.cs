using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Xml.Serialization;

namespace WidgetScreen
{
    public sealed class WidgetPlacement
    {
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Visible { get; set; }
        public int Layer { get; set; }
        public WidgetPlacement() { Visible = true; }
        [XmlIgnore] public Rect Bounds { get { return new Rect(X, Y, Width, Height); } }
        public WidgetPlacement Copy() { return (WidgetPlacement)MemberwiseClone(); }
    }

    public static class WidgetLayout
    {
        public const double Width = 1856, Height = 576;
        public static readonly string[] Ids = { "Clock", "Focus", "Media", "Shortcuts", "Mixer", "System", "Weather", "Spotify" };
        public static string Title(string id)
        {
            switch (id) {
                case "Focus": return "Focus timer";
                case "Media": return "Now playing";
                case "Shortcuts": return "Quick launch";
                case "Mixer": return "Sound mixer";
                case "System": return "System stats";
                default: return id;
            }
        }
        public static Size Minimum(string id)
        {
            switch (id) {
                case "Clock": return new Size(296, 200);
                case "Focus": return new Size(296, 300);
                case "Media": return new Size(360, 310);
                case "Spotify": return new Size(560, 500);
                case "Shortcuts": return new Size(420, 190);
                case "Mixer": return new Size(380, 420);
                case "System": return new Size(380, 310);
                default: return new Size(360, 220);
            }
        }
        public static WidgetPlacement[] Copy(WidgetPlacement[] layout) { return layout.Select(p => p.Copy()).ToArray(); }
        private static WidgetPlacement Place(string id, double x, double y, double width, double height)
        {
            return new WidgetPlacement { Id = id, X = x, Y = y, Width = width, Height = height };
        }
        public static WidgetPlacement[] Default(AppSettings settings)
        {
            double[] widths = DashboardAppearance.ColumnWidths(settings);
            double clockX = settings.SwapSideWidgets ? Width - widths[0] + 16 : 0;
            double mediaX = settings.SwapSideWidgets ? widths[3] + 16 : widths[0];
            double mixerX = mediaX + widths[1];
            double infoX = settings.SwapSideWidgets ? 0 : mixerX + widths[2];
            bool stats = settings.ShowCpu || settings.ShowGpu || settings.ShowRam || settings.ShowNetwork || settings.ShowUptime;
            return new[] {
                Place("Clock", clockX, 0, widths[0] - 16, settings.ClockHeight - 16),
                Place("Focus", clockX, settings.ClockHeight, widths[0] - 16, Height - settings.ClockHeight),
                Place("Media", mediaX, 0, widths[1] - 16, settings.MediaHeight - 16),
                Place("Shortcuts", mediaX, settings.MediaHeight, widths[1] - 16, Height - settings.MediaHeight),
                Place("Mixer", mixerX, 0, widths[2] - 16, settings.MixerHeight),
                Place("System", infoX, 0, widths[3], settings.SystemHeight - 16),
                Place("Weather", infoX, stats ? settings.SystemHeight : 0, widths[3], stats ? Height - settings.SystemHeight : Height),
                new WidgetPlacement { Id = "Spotify", X = 376, Y = 0, Width = 600, Height = 576, Visible = false }
            };
        }
        public static WidgetPlacement[] SpotifyDefault(AppSettings settings)
        {
            var result = Default(settings);
            foreach (var p in result)
            {
                p.Visible = p.Id != "System" && p.Id != "Weather";
                if (p.Id == "Clock") SetBounds(p, new Rect(0, 0, 320, 230));
                if (p.Id == "Focus") SetBounds(p, new Rect(0, 246, 320, 330));
                if (p.Id == "Media") SetBounds(p, new Rect(336, 0, 472, 310));
                if (p.Id == "Shortcuts") SetBounds(p, new Rect(336, 326, 472, 250));
                if (p.Id == "Mixer") SetBounds(p, new Rect(824, 0, 456, 576));
                if (p.Id == "Spotify") SetBounds(p, new Rect(1296, 0, 560, 576));
            }
            return result;
        }
        private static double Finite(double value, double fallback)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : value;
        }
        public static WidgetPlacement[] Normalize(WidgetPlacement[] layout, AppSettings settings)
        {
            var defaults = Default(settings);
            var result = new List<WidgetPlacement>();
            foreach (var fallback in defaults)
            {
                var supplied = layout == null ? null : layout.FirstOrDefault(entry => entry != null && entry.Id == fallback.Id);
                var p = supplied == null ? fallback.Copy() : supplied.Copy(); var minimum = Minimum(p.Id);
                p.Width = Math.Max(minimum.Width, Math.Min(Width, Finite(p.Width, fallback.Width)));
                p.Height = Math.Max(minimum.Height, Math.Min(Height, Finite(p.Height, fallback.Height)));
                p.X = Math.Max(0, Math.Min(Width - p.Width, Finite(p.X, fallback.X)));
                p.Y = Math.Max(0, Math.Min(Height - p.Height, Finite(p.Y, fallback.Y)));
                p.Layer = Math.Max(0, Math.Min(1000000, p.Layer));
                result.Add(p);
            }
            return result.ToArray();
        }
        private static double Snap(double value, bool enabled) { return enabled ? Math.Round(value / 8) * 8 : value; }
        public static Rect Transform(string id, Rect start, string corner, double dx, double dy, bool snap)
        {
            if (Double.IsNaN(dx) || Double.IsInfinity(dx) || Double.IsNaN(dy) || Double.IsInfinity(dy)) return start;
            var minimum = Minimum(id);
            double left = start.Left, right = start.Right, top = start.Top, bottom = start.Bottom;
            if (String.IsNullOrEmpty(corner))
                return new Rect(Math.Max(0, Math.Min(Width - start.Width, Snap(left + dx, snap))), Math.Max(0, Math.Min(Height - start.Height, Snap(top + dy, snap))), start.Width, start.Height);
            if (corner.Contains("W")) left = Math.Max(0, Math.Min(right - minimum.Width, Snap(left + dx, snap)));
            if (corner.Contains("E")) right = Math.Min(Width, Math.Max(left + minimum.Width, Snap(right + dx, snap)));
            if (corner.Contains("N")) top = Math.Max(0, Math.Min(bottom - minimum.Height, Snap(top + dy, snap)));
            if (corner.Contains("S")) bottom = Math.Min(Height, Math.Max(top + minimum.Height, Snap(bottom + dy, snap)));
            return new Rect(left, top, right - left, bottom - top);
        }
        public static void SetBounds(WidgetPlacement p, Rect bounds) { p.X = bounds.X; p.Y = bounds.Y; p.Width = bounds.Width; p.Height = bounds.Height; }
    }
}
