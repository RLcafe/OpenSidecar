using System;

namespace WidgetScreen
{
    // How each card's content follows its size (docs/design/any-screen-size.md), the same numbers as the Android
    // app's CardSizing.java. Quick Launch and the Soundboard show six buttons a page that grow with the card; Focus, Now
    // Playing and Weather grow their main reading; Spotify adds columns on wide cards. The mixer list and System
    // stats already fill their cards. Text never goes below the 13-unit caption and buttons keep 48 units.
    public static class CardSizing
    {
        // Quick Launch and the Soundboard always show six buttons a page, at any card size (user request, 31 Oct 2026):
        // 3 × 2, 2 × 3, 6 × 1 or 1 × 6, whichever keeps 48-unit buttons and gives cells closest to the usual button
        // shape. Their minimum sizes (WidgetLayout.Minimum) leave room for six.
        public const int PageButtons = 6;
        // A Quick Launch button plus its 4-unit margins, and the smallest cell that keeps a 48-unit button.
        public const double DeckCellWidth = 124, DeckCellHeight = 76, DeckMinimumCellWidth = 76, DeckMinimumCellHeight = 56;
        // A Soundboard button plus its 8-unit gap, and the smallest cell that keeps a 48-unit button.
        public const double SoundCellWidth = 156, SoundCellHeight = 64, SoundMinimumCellWidth = 88, SoundMinimumCellHeight = 56;

        // Columns and rows of Quick Launch buttons for the grid area (the card less its padding and header).
        public static int[] DeckGrid(double width, double height) { return DeckGrid(width, height, PageButtons); }
        public static int[] DeckGrid(double width, double height, int count) { return PageGrid(width, height, count, DeckCellWidth, DeckCellHeight, DeckMinimumCellWidth, DeckMinimumCellHeight); }
        // Columns and rows of Soundboard buttons for its grid.
        public static int[] SoundGrid(double width, double height, int count) { return PageGrid(width, height, count, SoundCellWidth, SoundCellHeight, SoundMinimumCellWidth, SoundMinimumCellHeight); }
        // Six to a page; with fewer than six, the smallest grid that holds them all, so the buttons grow into the card
        // instead of leaving empty cells. Grids never leave a whole row or column empty.
        public static int[] PageGrid(double width, double height, int count, double cellWidth, double cellHeight, double minimumWidth, double minimumHeight)
        {
            int n = count <= 0 ? PageButtons : Math.Min(count, PageButtons);
            int[] best = new[] { n, 1 }; double bestScore = Double.MaxValue;
            for (int c = 1; c <= n; c++)
                for (int r = 1; r <= n; r++)
                {
                    if (c * r < n || c * r >= n + Math.Min(c, r)) continue;
                    double w = Math.Max(1, width) / c, h = Math.Max(1, height) / r;
                    double shape = Math.Abs(Math.Log(w / h / (cellWidth / cellHeight)));
                    double score = (w >= minimumWidth && h >= minimumHeight ? 0 : 1000) + (c * r - n) * 10 + shape;
                    if (score < bestScore) { bestScore = score; best = new[] { c, r }; }
                }
            return best;
        }
        // A Quick Launch button's label goes under its glyph when the cell is tall enough, beside it in a wide short
        // cell, and into the tooltip and spoken name when the cell has room for the glyph alone.
        public enum DeckLabelMode { Below, Beside, None }
        public static DeckLabelMode DeckLabel(double cellWidth, double cellHeight)
        {
            return cellHeight >= DeckCellHeight ? DeckLabelMode.Below : cellWidth >= 112 ? DeckLabelMode.Beside : DeckLabelMode.None;
        }
        // Beside its label the glyph stays small; otherwise it sits above the label (or alone) and grows with the button.
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
