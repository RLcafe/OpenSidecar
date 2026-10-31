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
        // The Focus countdown: as wide as the card allows, and only as tall as the room above the buttons. A short card
        // has only the start row under it (FocusShort).
        public static double FocusTime(double width, double height)
        {
            bool folded = FocusShort(height);
            double room = folded ? (height - 140) * 0.75 : (height - 300) * 0.6;
            return Clamp(Math.Round(Math.Min((width - 48) * 0.31, room)), folded ? 40 : 42, 160);
        }
        // The Now Playing cover: the reference 104 and up on a full card, as tall as the title row on a short one.
        public static double MediaArt(double width, double height)
        {
            if (MediaShort(height)) return Clamp(Math.Round(Math.Min(height - 136, (width - 48) * 0.3)), 44, 104);
            return Clamp(Math.Round(Math.Min(height - 220, (width - 48) * 0.36)), 104, 220);
        }
        // The Weather temperature (54 on the reference 250-unit card); its column is twice as wide.
        public static double WeatherTemperature(double width, double height) { return Clamp(Math.Round(Math.Min(height * 0.216, width * 0.14)), 40, 96); }
        // Spotify rows side by side once the card fits two 340-unit rows.
        public static int SpotifyColumns(double width) { return (int)Clamp(Math.Floor((width - 48) / 340), 1, 3); }

        // Small cards (docs/design/resizable-widgets.md). Each card keeps every control it has, rearranged, until a
        // threshold; below it the card trades secondary rows for room, never its main reading or main action.
        // Focus under 300 tall: the presets fold into one length button beside Start that steps through them.
        public static bool FocusShort(double height) { return height < 300; }
        // Now Playing under 290 tall: the seek bar and the hint row fold away; under 400 wide the buttons draw closer.
        public static bool MediaShort(double height) { return height < 290; }
        public static bool MediaNarrow(double width) { return width < 400; }
        // The mixer: Full at 420 tall and up; Compact (the microphone moves beside the master slider, the level meter
        // and the APPS heading fold away, the app list scrolls); Master under 300 tall (the master volume, with the
        // app list one tap away in the same card).
        public enum MixerMode { Full, Compact, Master }
        public static MixerMode Mixer(double height) { return height >= 420 ? MixerMode.Full : height >= 300 ? MixerMode.Compact : MixerMode.Master; }
        // The Windows sound settings button gives its room to the slider on narrow compact cards.
        public static bool MixerSettingsShown(double width, MixerMode mode) { return mode == MixerMode.Full || width >= 400; }
        // Card headers with pages: the Edit button needs 342 units (Settings → Quick Launch still opens the deck),
        // and a paged card under 300 wide keeps its pager over its heading (Quick Launch and Soundboard).
        public static bool DeckEditShown(double width) { return width >= 342; }
        public static bool HeadingShown(double width, bool paged) { return !paged || width >= 300; }
        // System stats rows drop their graph and bar under 380 wide, so more columns fit and the numbers stay large.
        public static bool StatsNarrow(double width) { return width < 380; }
        // Weather under 200 tall keeps the temperature and city and drops the details line.
        public static bool WeatherTiny(double height) { return height < 200; }
        // Spotify under 500 tall: a smaller now-playing strip, and the status row only while it has something to say.
        public static bool SpotifyCompact(double height) { return height < 500; }
        // Empty states ("Connect Spotify", "Connect Discord") for the room under the card's heading: 3 shows the icon,
        // heading, sentence and button; 2 drops the icon; 1 keeps the heading and button; 0 the button alone.
        public static int EmptyDetail(double room) { return room >= 268 ? 3 : room >= 156 ? 2 : room >= 100 ? 1 : 0; }
        // The Discord channel name beside the Soundboard heading.
        public static bool SoundboardChannelShown(double width) { return width >= 360; }
        private static double Clamp(double value, double min, double max) { return Double.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value)); }
    }
}
