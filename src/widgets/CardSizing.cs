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
        // Card headers with pages: the Edit button needs 342 units (Settings â†’ Quick Launch still opens the deck),
        // and a paged card under 300 wide keeps its pager over its heading (Quick Launch and Soundboard).
        public static bool DeckEditShown(double width) { return width >= 342; }
        public static bool HeadingShown(double width, bool paged) { return !paged || width >= 300; }
        // Spotify under 500 tall: a smaller now-playing strip, and the status row only while it has something to say.
        public static bool SpotifyCompact(double height) { return height < 500; }
        // Empty states ("Connect Spotify", "Connect Discord") for the room under the card's heading: 3 shows the icon,
        // heading, sentence and button; 2 drops the icon; 1 keeps the heading and button; 0 the button alone.
        public static int EmptyDetail(double room) { return room >= 268 ? 3 : room >= 156 ? 2 : room >= 100 ? 1 : 0; }
        // The Discord channel name beside the Soundboard heading.
        public static bool SoundboardChannelShown(double width) { return width >= 360; }

        // Small, medium and large designs (docs/design/widget-size-views.md). A card picks one of three layouts by its
        // size, not just a scale of one layout; System stats and Weather are the first two.
        public enum Design { Small, Medium, Large }
        // System stats: a list of readings under 340 wide (or 240 tall), rings at the reference 472 أ— 310, and tiles
        // with history graphs from 400 أ— 420.
        public static Design Stats(double width, double height) { return width < 340 || height < 240 ? Design.Small : width >= 400 && height >= 420 ? Design.Large : Design.Medium; }
        // Weather: a glance under 340 wide (or 220 tall), today's reading with three facts at the reference 472 أ— 250,
        // and a forecast with five hours from 400 أ— 340.
        public static Design Weather(double width, double height) { return width < 340 || height < 220 ? Design.Small : width >= 400 && height >= 340 ? Design.Large : Design.Medium; }
        // A Small stats row's reading for its row height; Medium ring diameter for its cell; a Large tile's reading.
        public static double StatsListValue(double rowHeight) { return Clamp(Math.Round(rowHeight * 0.56), 20, 40); }
        public static double StatsRing(double cellWidth, double height) { return Clamp(Math.Floor(Math.Min(cellWidth - 16, height)), 56, 200); }
        public static double StatsTileValue(double tileHeight) { return Clamp(Math.Round(tileHeight * 0.24), 30, 64); }
        public static int StatsTileColumns(double width, int count) { return count <= 1 ? 1 : width >= 960 ? Math.Min(count, 4) : 2; }
        // The weather temperature for the room left to it (the card less its fixed rows) and the card's width.
        public static double WeatherTemperature(Design size, double width, double room)
        {
            if (size == Design.Small) return Clamp(Math.Round(Math.Min(room / 1.12, width * 0.26)), 36, 96);
            if (size == Design.Medium) return Clamp(Math.Round(Math.Min(room / 1.12, width * 0.15)), 40, 80);
            return Clamp(Math.Round(Math.Min(room / 1.12, width * 0.2)), 52, 128);
        }
        // The other cards' designs (docs/design/widget-size-views.md, "Every widget"). Small is the folded card from
        // resizable-widgets.md; Large is a new layout.
        // Focus: Small under 300 tall; Large (the progress as a ring around the countdown) from 300 × 460.
        public static Design Focus(double width, double height) { return FocusShort(height) ? Design.Small : width >= 300 && height >= 460 ? Design.Large : Design.Medium; }
        public static double FocusRing(double width, double height) { return Clamp(Math.Floor(Math.Min(width - 48, height - 186)), 160, 420); }
        // Now Playing: Small under 290 tall; Large (a big centred cover over the song) from 360 × 460.
        public static Design Media(double width, double height) { return MediaShort(height) ? Design.Small : width >= 360 && height >= 460 ? Design.Large : Design.Medium; }
        public static double MediaCover(double width, double height) { return Clamp(Math.Floor(Math.Min(width - 88, height - 296)), 120, 360); }
        // The mixer's Master, Compact and Full arrangements are its Small, Medium and Large; a Large card 760 wide lists
        // the apps in two columns.
        public static Design MixerDesign(double height) { var mode = Mixer(height); return mode == MixerMode.Master ? Design.Small : mode == MixerMode.Compact ? Design.Medium : Design.Large; }
        public static int MixerColumns(double width, double height) { return Mixer(height) == MixerMode.Full && width >= 760 ? 2 : 1; }
        // Spotify: Small is the compact card under 500 tall; Large (the song in a side panel with a big cover) from 760 wide.
        public static Design Spotify(double width, double height) { return SpotifyCompact(height) ? Design.Small : width >= 760 ? Design.Large : Design.Medium; }
        // Quick Launch and Soundboard buttons by their cell: Small (glyph or name alone), Medium (glyph over the name),
        // Large (a tile with the glyph, the name and what it does).
        public static Design Tile(double cellWidth, double cellHeight) { return cellHeight < 64 || cellWidth < 100 ? Design.Small : cellWidth >= 150 && cellHeight >= 110 ? Design.Large : Design.Medium; }
        // The hours a Large forecast shows: up to five, as many as fit 72 units each.
        public static int WeatherHours(double width) { return (int)Clamp(Math.Floor((width - 48 - 16) / 72), 3, 5); }
        // Storage (docs/design/storage.md): a list under 340 wide or 200 tall, rows with names and sizes at the reference
        // 472 × 250, and ring tiles from 440 × 340. Each design holds as many drives as its rows or tiles fit; the rest are
        // counted in a last line.
        public static Design Storage(double width, double height) { return width < 340 || height < 200 ? Design.Small : width >= 440 && height >= 340 ? Design.Large : Design.Medium; }
        // Medium rows carry a used-of-total line while every drive fits with it, and drop it to fit more drives.
        public const double StorageListRow = 40, StorageRowHeight = 76, StorageCompactRow = 36, StorageTileHeight = 150;
        public static bool StorageDetailRows(double height, int count) { return count * StorageRowHeight + (count - 1) * 8 <= height; }
        public static int StorageCapacity(Design design, double width, double height, int count)
        {
            if (design == Design.Small) return Math.Max(1, (int)Math.Floor((height + 8) / (StorageListRow + 8)));
            if (design == Design.Medium) return Math.Max(1, (int)Math.Floor((height + 4) / (StorageCompactRow + 4)));
            return StorageTileColumns(width, count) * Math.Max(1, (int)Math.Floor((height + 12) / (StorageTileHeight + 12)));
        }
        public static int StorageTileColumns(double width, int count) { return count <= 1 ? 1 : width >= 960 ? Math.Min(count, 4) : Math.Min(count, 2); }
        public static double StorageRing(double tileWidth, double tileHeight) { return Clamp(Math.Floor(Math.Min(tileWidth - 24, tileHeight - 24 - 72)), 56, 200); }
        public static double StorageListValue(double rowHeight) { return Clamp(Math.Round(rowHeight * 0.5), 18, 32); }
        // World clocks (docs/design/world-clocks.md): rows under 340 wide or 200 tall, cities side by side at the
        // reference 472 × 250, and tiles from 440 × 300 (side by side while each is 170 wide, stacked otherwise). A
        // small row keeps its day line from 46 units; a small card holds as many 32-unit rows as fit.
        public static Design Clocks(double width, double height) { return width < 340 || height < 200 ? Design.Small : width >= 440 && height >= 300 ? Design.Large : Design.Medium; }
        public const double ClockRow = 32, ClockDetailRow = 46;
        public static int ClockRows(double height) { return Math.Max(1, (int)Math.Floor((height + 8) / (ClockRow + 8))); }
        public static double ClockListTime(double rowHeight) { return Clamp(Math.Round(rowHeight * 0.55), 18, 40); }
        // A time is about 2.5 ems wide; a 12-hour clock leaves room for AM or PM beside it.
        public static double ClockColumnTime(double column, double height, bool use24) { return Clamp(Math.Round(Math.Min((column - (use24 ? 4 : 34)) * 0.36, (height - 80) * 0.75)), 22, 72); }
        public static bool ClockTilesAcross(double width, int count) { return count <= 1 || (width - 12 * (count - 1)) / count >= 170; }
        public static double ClockTileTime(double tileWidth, double tileHeight, bool use24) { return Clamp(Math.Round(Math.Min((tileWidth - 32 - (use24 ? 4 : 34)) * 0.36, (tileHeight - 32 - 80) * 0.8)), 28, 120); }
        public static double ClockStackedTime(double tileWidth, double tileHeight) { return Clamp(Math.Round(Math.Min((tileWidth - 32) * 0.18, (tileHeight - 20) * 0.75)), 24, 72); }
        // Prayer times (docs/design/prayer-times.md): the next prayer under 340 wide or 220 tall, the next prayer beside
        // today's five times at the reference 472 × 250, and from 440 × 360 the next prayer with its progress over the
        // times, as rows or (with a body 860 wide) six tiles across.
        public static Design Prayer(double width, double height) { return width < 340 || height < 220 ? Design.Small : width >= 440 && height >= 360 ? Design.Large : Design.Medium; }
        public static double PrayerTime(double width, double height) { return Clamp(Math.Round(Math.Min(width * 0.24, (height - 60) * 0.7)), 28, 96); }
        public static double PrayerNextColumn(double width) { return Math.Floor(width * 0.46); }
        public static bool PrayerTilesAcross(double width) { return width >= 860; }
        public static double PrayerLargeTop(double height, bool across) { return across ? Clamp(height - 120, 140, 260) : Clamp(Math.Round(height * 0.38), 140, 220); }
        public static double PrayerTileTime(double tileWidth, bool use24) { return Clamp(Math.Round((tileWidth - 24) / (use24 ? 2.7 : 3.9)), 18, 34); }
        // Notes and to-do (docs/design/notes.md): the scratchpad or the list behind two tabs under 700 wide or 300 tall
        // (Small under 340 × 220 keeps the same tabs in less room), and both side by side from 700 × 300.
        public static Design Notes(double width, double height) { return width < 340 || height < 220 ? Design.Small : width >= 700 && height >= 300 ? Design.Large : Design.Medium; }
        public static double NotesListColumn(double width) { return Clamp(Math.Floor(width * 0.46), 300, 520); }
        // Claude Code (docs/design/claude-watcher.md): one loud critter under 360 wide or 280 tall, the lineup of critters
        // over the plan meters between, and the burrow (session rows beside a usage panel) from 640 × 420. Heights that
        // hold text follow the text size, so nothing leaves the card at 140%.
        public static Design Claude(double width, double height) { return width < 360 || height < 280 ? Design.Small : width >= 640 && height >= 420 ? Design.Large : Design.Medium; }
        // The Medium meter strip: two meters side by side, each a label line, a 10-unit bar and a reset line.
        public static double ClaudeStrip(double textScale) { return Math.Ceiling(60 + 24 * textScale); }
        // Critters in the Medium lineup: one per 108 units of width, at most five.
        public static int ClaudeLineup(double bodyWidth) { return (int)Clamp(Math.Floor((bodyWidth + 12) / 108), 1, 5); }
        // A lineup critter: what's left of the row after its name (and state, when there's room), at most 96 units.
        public static double ClaudeLineupCritter(double rowHeight, double columnWidth, double textScale, bool state)
        {
            return Clamp(Math.Floor(Math.Min(rowHeight - 20 * textScale - 10 - (state ? 19 : 0), columnWidth - 8)), 32, 96);
        }
        public static bool ClaudeLineupState(double rowHeight, double textScale) { return rowHeight - 20 * textScale - 10 - 19 >= 48; }
        // A Large session row: the title over the state, at least 64 units.
        public static double ClaudeRowHeight(double textScale) { return Math.Max(64, Math.Ceiling(8 + 44 * textScale)); }
        public static int ClaudeRows(double height, double textScale) { return Math.Max(1, (int)Math.Floor((height + 8) / (ClaudeRowHeight(textScale) + 8))); }
        // The Large usage panel's width, and how many project rows fit under its meters and period switch.
        public static double ClaudePanel(double bodyWidth) { return Clamp(Math.Round(bodyWidth * 0.38), 240, 380); }
        public static double ClaudeMeterBlock(double textScale) { return Math.Ceiling(Math.Max(20 * textScale, 34) + 42); }
        public static double ClaudeProjectRow(double textScale) { return Math.Ceiling(16 + 20 * textScale); }
        public static int ClaudeProjects(double panelHeight, double textScale)
        {
            double used = 2 * ClaudeMeterBlock(textScale) + 14 + 48 + 16 + Math.Ceiling(20 * textScale) + 8 + 22;
            return (int)Clamp(Math.Floor((panelHeight - used) / ClaudeProjectRow(textScale)), 0, 6);
        }
        // The Small critter: as tall as the card allows, at most 42% of its width.
        public static double ClaudeSmallCritter(double bodyWidth, double bodyHeight) { return Clamp(Math.Floor(Math.Min(bodyHeight - 16, bodyWidth * 0.42)), 48, 140); }
        // Clipboard history (docs/design/clipboard-history.md): one-line rows under 340 wide or 220 tall, rows with when
        // each text was copied at the reference size, and two-line previews with their length from 440 × 400.
        public static Design Clip(double width, double height) { return width < 340 || height < 220 ? Design.Small : width >= 440 && height >= 400 ? Design.Large : Design.Medium; }
        // Calendar (docs/design/calendar.md): the next event under 340 wide or 220 tall, the next event over the ones after
        // it at the reference 472 × 250, and the agenda grouped by day from 440 × 380. Medium rows are 30 units under the
        // next-event block (its three lines and the rule under it, which grow with the text size); the time column fits
        // "12:30 PM" (or "Tmrw 12:30") at the small size.
        public static Design Calendar(double width, double height) { return width < 340 || height < 220 ? Design.Small : width >= 440 && height >= 380 ? Design.Large : Design.Medium; }
        public const double CalendarRow = 30;
        public static double CalendarNextHeight(double textScale) { return Math.Ceiling(27 + 72 * textScale); }
        public static double CalendarTimeColumn(bool use24) { return use24 ? 96 : 128; }
        // Smart home (docs/design/smart-home.md): a scrolling list of 56-unit rows under 340 wide or 220 tall, tiles at
        // the reference size, and tiles with dimmer sliders from 560 x 360. A tile is at least 180 wide and HomeCell tall
        // (its name and state, or a thermostat's - and + stacked beside it); controls go under it from HomeTile. With
        // more devices than fit, a 56-wide pager column takes the right edge. Few devices make the tiles grow.
        public static Design Home(double width, double height) { return width < 340 || height < 220 ? Design.Small : width >= 560 && height >= 360 ? Design.Large : Design.Medium; }
        public static double HomeContent(double textScale) { return Math.Ceiling(24 + 44 * textScale); }
        public static double HomeCell(double textScale) { return Math.Max(108, HomeContent(textScale)); }
        public static double HomeTile(double textScale) { return HomeContent(textScale) + 56; }
        public const double HomePager = 56;
        // { columns, rows, paged (1 or 0) } for the tiles' area.
        public static int[] HomeGrid(double width, double height, double textScale, int count)
        {
            count = Math.Max(1, count);
            int rows = (int)Clamp(Math.Floor(height / HomeCell(textScale)), 1, 4), columns = (int)Clamp(Math.Floor(width / 180), 1, 5);
            bool paged = columns * rows < count;
            if (paged) columns = (int)Clamp(Math.Floor((width - HomePager) / 180), 1, 5);
            else { columns = Math.Min(columns, count); rows = Math.Min(rows, (count + columns - 1) / columns); }
            return new[] { columns, rows, paged ? 1 : 0 };
        }
        private static double Clamp(double value, double min, double max) { return Double.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value)); }
    }
}
