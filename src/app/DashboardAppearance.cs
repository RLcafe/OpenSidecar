using System;
using System.Windows;
using System.Windows.Media;

namespace WidgetScreen
{
    public static class DashboardAppearance
    {
        // Minimum widths protect touch controls; the remaining space is shared by preference.
        public static double[] ColumnWidths(AppSettings settings)
        {
            double[] minimum = { 312, 460, 440, 424 };
            double[] weights = { settings.ClockWidth, settings.MediaWidth, settings.MixerWidth, settings.SystemWidth };
            double total = 0;
            foreach (double weight in weights) total += weight;
            for (int i = 0; i < minimum.Length; i++) minimum[i] += 220 * (total == 0 ? .25 : weights[i] / total);
            return minimum;
        }

        public static void Apply(Window window, AppSettings settings)
        {
            string[] keys = { "Page", "Surface", "Raised", "Input", "Track", "ControlBorder", "Selected", "Muted", "Text", "ClockSurface", "ClockBorder", "ClockMuted" };
            string[] colors = settings.Theme == "Slate"
                ? new[] { "#101923", "#1B2938", "#293C50", "#142130", "#40546A", "#4C637C", "#3A536E", "#B6C7DA", "#F0F6FF", "#1E3341", "#476175", "#B6CDD9" }
                : settings.Theme == "Warm"
                ? new[] { "#181411", "#27211D", "#3B3028", "#1F1915", "#544638", "#665344", "#564534", "#C8B8A6", "#FAF3E9", "#30291F", "#61533C", "#D2C4A8" }
                : new[] { "#0C0E13", "#161A22", "#252B36", "#10141C", "#353D49", "#343C49", "#394252", "#A0AABB", "#F2F4F8", "#1C2420", "#344235", "#A8BAAA" };
            for (int i = 0; i < keys.Length; i++)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
                brush.Freeze(); window.Resources[keys[i]] = brush;
            }
            window.Resources["CardRadius"] = new CornerRadius(settings.CornerRadius);
        }
    }
}
