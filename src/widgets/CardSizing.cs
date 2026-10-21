using System;

namespace WidgetScreen
{
    // How each card's content follows its size (docs/design/any-screen-size.md), the same numbers as the Android
    // app's CardSizing.java. Like the Soundboard, Quick Launch fits as many buttons as its card holds; Focus, Now
    // Playing and Weather grow their main reading; Spotify adds columns on wide cards. The mixer list and System
    // stats already fill their cards. Text never goes below the 13-unit caption and buttons keep 48 units.
    public static class CardSizing
    {
        // A Quick Launch button plus its 4-unit margins.
        public const double DeckCellWidth = 124, DeckCellHeight = 76;

        // Columns and rows of Quick Launch buttons for the grid area (the card less its padding and header).
        public static int[] DeckGrid(double width, double height)
        {
            int columns = (int)Math.Max(1, Math.Min(8, Math.Floor(Math.Max(0, width) / DeckCellWidth)));
            int rows = (int)Math.Max(1, Math.Min(6, Math.Floor(Math.Max(0, height) / DeckCellHeight)));
            return new[] { columns, rows };
        }
        // When every button fits on one page, the smallest grid that holds them all, so the buttons grow into the card
        // instead of leaving empty cells; ties go to cells closest to the usual button shape.
        public static int[] DeckGrid(double width, double height, int count)
        {
            int[] most = DeckGrid(width, height);
            if (count <= 0 || count >= most[0] * most[1]) return most;
            int[] best = most; double bestScore = Double.MaxValue;
            for (int c = 1; c <= most[0]; c++)
                for (int r = 1; r <= most[1]; r++)
                {
                    if (c * r < count) continue;
                    double shape = Math.Abs(Math.Log(width / c / (height / r) / (DeckCellWidth / DeckCellHeight)));
                    double score = (c * r - count) * 10 + shape;
                    if (score < bestScore) { bestScore = score; best = new[] { c, r }; }
                }
            return best;
        }
        // A single row puts the glyph beside the label; otherwise the glyph sits above it and grows with the button.
        public static double DeckGlyph(double cellHeight, bool beside)
        {
            return beside ? Clamp(Math.Round(cellHeight * 0.18), 18, 32) : Clamp(Math.Round(cellHeight * 0.3), 24, 44);
        }
        // The Focus countdown: as wide as the card allows, and only as tall as the room above the buttons.
        public static double FocusTime(double width, double height) { return Clamp(Math.Round(Math.Min((width - 48) * 0.31, (height - 300) * 0.6)), 42, 160); }
        // The Now Playing cover: at least the reference 104, growing with the card's free height and width.
        public static double MediaArt(double width, double height) { return Clamp(Math.Round(Math.Min(height - 220, (width - 48) * 0.36)), 104, 220); }
        // The Weather temperature (54 on the reference 250-unit card); its column is twice as wide.
        public static double WeatherTemperature(double width, double height) { return Clamp(Math.Round(Math.Min(height * 0.216, width * 0.14)), 40, 96); }
        // Spotify rows side by side once the card fits two 340-unit rows.
        public static int SpotifyColumns(double width) { return (int)Clamp(Math.Floor((width - 48) / 340), 1, 3); }
        private static double Clamp(double value, double min, double max) { return Double.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value)); }
    }
}
