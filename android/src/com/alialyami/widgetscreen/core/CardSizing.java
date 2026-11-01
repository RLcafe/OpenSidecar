package com.alialyami.widgetscreen.core;

// How each card's content follows its size, the same numbers as the Windows CardSizing.cs
// (docs/design/any-screen-size.md). Quick Launch and the Soundboard show six buttons a page that grow with the card;
// Focus, Now Playing and Weather grow their main reading; Spotify adds columns on wide cards. The mixer list and System stats
// already fill their cards. Text never goes below the 13-unit caption and buttons keep 48 units.
public final class CardSizing {
    // Quick Launch and the Soundboard always show six buttons a page, at any card size (user request, 31 Oct 2026):
    // 3 × 2, 2 × 3, 6 × 1 or 1 × 6, whichever keeps 48-unit buttons and gives cells closest to the usual button shape.
    // Their minimum sizes (WidgetLayout.minimum) leave room for six.
    public static final int PAGE_BUTTONS = 6;
    // A Quick Launch button plus its 4-unit margins, and the smallest cell that keeps a 48-unit button.
    public static final double DECK_CELL_WIDTH = 124, DECK_CELL_HEIGHT = 76, DECK_MINIMUM_CELL_WIDTH = 76, DECK_MINIMUM_CELL_HEIGHT = 56;
    // A Soundboard button plus its 8-unit gap, and the smallest cell that keeps a 48-unit button.
    public static final double SOUND_CELL_WIDTH = 156, SOUND_CELL_HEIGHT = 64, SOUND_MINIMUM_CELL_WIDTH = 88, SOUND_MINIMUM_CELL_HEIGHT = 56;

    // Columns and rows of Quick Launch buttons for the grid area (the card less its padding and header).
    public static int[] deckGrid(double width, double height) { return deckGrid(width, height, PAGE_BUTTONS); }
    public static int[] deckGrid(double width, double height, int count) { return pageGrid(width, height, count, DECK_CELL_WIDTH, DECK_CELL_HEIGHT, DECK_MINIMUM_CELL_WIDTH, DECK_MINIMUM_CELL_HEIGHT); }
    // Columns and rows of Soundboard buttons for its grid.
    public static int[] soundGrid(double width, double height, int count) { return pageGrid(width, height, count, SOUND_CELL_WIDTH, SOUND_CELL_HEIGHT, SOUND_MINIMUM_CELL_WIDTH, SOUND_MINIMUM_CELL_HEIGHT); }

    // Six to a page; with fewer than six, the smallest grid that holds them all, so the buttons grow into the card
    // instead of leaving empty cells. Grids never leave a whole row or column empty.
    public static int[] pageGrid(double width, double height, int count, double cellWidth, double cellHeight, double minimumWidth, double minimumHeight) {
        int n = count <= 0 ? PAGE_BUTTONS : Math.min(count, PAGE_BUTTONS);
        int[] best = new int[] { n, 1 };
        double bestScore = Double.MAX_VALUE;
        for (int c = 1; c <= n; c++)
            for (int r = 1; r <= n; r++) {
                if (c * r < n || c * r >= n + Math.min(c, r)) continue;
                double w = Math.max(1, width) / c, h = Math.max(1, height) / r;
                double shape = Math.abs(Math.log(w / h / (cellWidth / cellHeight)));
                double score = (w >= minimumWidth && h >= minimumHeight ? 0 : 1000) + (c * r - n) * 10 + shape;
                if (score < bestScore) { bestScore = score; best = new int[] { c, r }; }
            }
        return best;
    }

    // A Quick Launch button's label goes under its glyph when the cell is tall enough, beside it in a wide short cell,
    // and into the spoken name when the cell has room for the glyph alone.
    public static final int DECK_LABEL_BELOW = 0, DECK_LABEL_BESIDE = 1, DECK_LABEL_NONE = 2;
    public static int deckLabel(double cellWidth, double cellHeight) {
        return cellHeight >= DECK_CELL_HEIGHT ? DECK_LABEL_BELOW : cellWidth >= 112 ? DECK_LABEL_BESIDE : DECK_LABEL_NONE;
    }

    // Beside its label the glyph stays small; otherwise it sits above the label (or alone) and grows with the button.
    public static double deckGlyph(double cellHeight, boolean beside) {
        return beside ? clamp(Math.round(cellHeight * 0.18), 18, 32) : clamp(Math.round(cellHeight * 0.3), 24, 44);
    }

    // The Focus countdown: as wide as the card allows, and only as tall as the room above the buttons. A short card
    // has only the start row under it (focusShort).
    public static double focusTime(double width, double height) {
        boolean folded = focusShort(height);
        double room = folded ? (height - 140) * 0.75 : (height - 300) * 0.6;
        return clamp(Math.round(Math.min((width - 48) * 0.31, room)), folded ? 40 : 42, 160);
    }

    // The Now Playing cover: the reference 104 and up on a full card, as tall as the title row on a short one.
    public static double mediaArt(double width, double height) {
        if (mediaShort(height)) return clamp(Math.round(Math.min(height - 136, (width - 48) * 0.3)), 44, 104);
        return clamp(Math.round(Math.min(height - 220, (width - 48) * 0.36)), 104, 220);
    }

    // Spotify rows side by side once the card fits two 340-unit rows.
    public static int spotifyColumns(double width) { return (int) clamp(Math.floor((width - 48) / 340), 1, 3); }

    // Small cards (docs/design/resizable-widgets.md), the same thresholds as CardSizing.cs. Each card keeps every
    // control it has, rearranged, until a threshold; below it the card trades secondary rows for room, never its main
    // reading or main action.
    // Focus under 300 tall: the presets fold into one length button beside Start that steps through them.
    public static boolean focusShort(double height) { return height < 300; }
    // Now Playing under 290 tall: the seek bar and the hint row fold away; under 400 wide the buttons draw closer.
    public static boolean mediaShort(double height) { return height < 290; }
    public static boolean mediaNarrow(double width) { return width < 400; }
    // The mixer: FULL at 420 tall and up; COMPACT (the microphone moves beside the master slider, the level meter and
    // the APPS heading fold away, the app list scrolls); MASTER under 300 tall (the master volume, with the app list one
    // tap away in the same card).
    public static final int MIXER_FULL = 0, MIXER_COMPACT = 1, MIXER_MASTER = 2;
    public static int mixer(double height) { return height >= 420 ? MIXER_FULL : height >= 300 ? MIXER_COMPACT : MIXER_MASTER; }
    // The sound settings button gives its room to the slider on narrow compact cards.
    public static boolean mixerSettingsShown(double width, int mode) { return mode == MIXER_FULL || width >= 400; }
    // Card headers with pages: the Edit button needs 342 units, and a paged card under 300 wide keeps its pager over
    // its heading (Quick Launch and Soundboard).
    public static boolean deckEditShown(double width) { return width >= 342; }
    public static boolean headingShown(double width, boolean paged) { return !paged || width >= 300; }
    // Spotify under 500 tall: a smaller now-playing strip, and the status row only while it has something to say.
    public static boolean spotifyCompact(double height) { return height < 500; }
    // Empty states for the room under the card's heading: 3 shows the icon, heading, sentence and button; 2 drops the
    // icon; 1 keeps the heading and button; 0 the button alone.
    public static int emptyDetail(double room) { return room >= 268 ? 3 : room >= 156 ? 2 : room >= 100 ? 1 : 0; }
    // The Discord channel name beside the Soundboard heading.
    public static boolean soundboardChannelShown(double width) { return width >= 360; }

    // Small, medium and large designs (docs/design/widget-size-views.md), the same numbers as CardSizing.cs. A card
    // picks one of three layouts by its size, not just a scale of one layout; System stats and Weather are the first two.
    public static final int SMALL = 0, MEDIUM = 1, LARGE = 2;
    // System stats: a list of readings under 340 wide (or 240 tall), rings at the reference 472 × 310, and tiles with
    // history graphs from 400 × 420.
    // Smart home (HomeCard on the PC): a scrolling list of 56-unit rows under 340 wide or 220 tall, tiles at the
    // reference size, and tiles with dimmer sliders from 560 x 360. A tile is at least 180 wide and homeCell tall;
    // controls go under it from homeTile. With more devices than fit, a 56-wide pager column takes the right edge.
    public static int home(double width, double height) { return width < 340 || height < 220 ? SMALL : width >= 560 && height >= 360 ? LARGE : MEDIUM; }
    public static double homeContent(double textScale) { return Math.ceil(24 + 44 * textScale); }
    public static double homeCell(double textScale) { return Math.max(108, homeContent(textScale)); }
    public static double homeTile(double textScale) { return homeContent(textScale) + 56; }
    public static final double HOME_PAGER = 56;
    // { columns, rows, paged (1 or 0) } for the tiles' area.
    public static int[] homeGrid(double width, double height, double textScale, int count) {
        count = Math.max(1, count);
        int rows = (int) clamp(Math.floor(height / homeCell(textScale)), 1, 4), columns = (int) clamp(Math.floor(width / 180), 1, 5);
        boolean paged = columns * rows < count;
        if (paged) columns = (int) clamp(Math.floor((width - HOME_PAGER) / 180), 1, 5);
        else { columns = Math.min(columns, count); rows = Math.min(rows, (count + columns - 1) / columns); }
        return new int[] { columns, rows, paged ? 1 : 0 };
    }

    public static int stats(double width, double height) { return width < 340 || height < 240 ? SMALL : width >= 400 && height >= 420 ? LARGE : MEDIUM; }
    // Weather: a glance under 340 wide (or 220 tall), today's reading with three facts at the reference 472 × 250, and a
    // forecast with five hours from 400 × 340.
    public static int weather(double width, double height) { return width < 340 || height < 220 ? SMALL : width >= 400 && height >= 340 ? LARGE : MEDIUM; }
    // A Small stats row's reading for its row height; Medium ring diameter for its cell; a Large tile's reading.
    public static double statsListValue(double rowHeight) { return clamp(Math.round(rowHeight * 0.56), 20, 40); }
    public static double statsRing(double cellWidth, double height) { return clamp(Math.floor(Math.min(cellWidth - 16, height)), 56, 200); }
    public static double statsTileValue(double tileHeight) { return clamp(Math.round(tileHeight * 0.24), 30, 64); }
    public static int statsTileColumns(double width, int count) { return count <= 1 ? 1 : width >= 960 ? Math.min(count, 4) : 2; }
    // Hardware sensors (SystemStatsCard on the PC): with up to nine readings each design checks they fit, with the same
    // numbers. Small rows at least statsListRow tall, columns at least 140 wide; { columns, rows, shown }.
    public static double statsListRow(double textScale) { return Math.ceil(32 + 8 * textScale); }
    public static int[] statsListGrid(double width, double height, int count, double textScale) {
        count = Math.max(1, count);
        int perColumn = Math.max(1, (int) Math.floor((height + 8) / (statsListRow(textScale) + 8)));
        int most = Math.max(1, (int) Math.floor((width + 16) / (140 + 16)));
        int columns = Math.min(most, Math.min(count, (count + perColumn - 1) / perColumn));
        int room = columns * perColumn;
        if (count <= room) return new int[] { columns, (count + columns - 1) / columns, count };
        return new int[] { columns, perColumn, Math.max(1, room - 1) };
    }
    public static double statsDialText(double textScale) { return Math.ceil(26 + 20 * textScale); }
    // Medium cells at least 100 wide with room for a 56-unit dial and its text; null when none fit (Small instead).
    public static int[] statsCellGrid(double width, double height, int count, double textScale) {
        int[] best = null; double bestDial = 0;
        for (int columns = 1; columns <= count; columns++) {
            int rows = (count + columns - 1) / columns;
            double cellWidth = width / columns, cellHeight = (height - 12 * (rows - 1)) / rows;
            if (cellWidth < 100 || cellHeight < 56 + statsDialText(textScale)) continue;
            double dial = Math.min(cellWidth - 16, cellHeight - statsDialText(textScale));
            if (dial > bestDial + 0.5) { bestDial = dial; best = new int[] { columns, rows }; }
        }
        return best;
    }
    // Large tiles at least 150 wide and statsTileMinimum tall, shaped closest to 1.6 : 1; null when none fit (Medium).
    public static double statsTileMinimum(double textScale) { return Math.ceil(108 + 20 * textScale); }
    public static int[] statsTileGrid(double width, double height, int count, double textScale) {
        int[] best = null; double bestShape = Double.MAX_VALUE;
        for (int columns = 1; columns <= Math.min(4, Math.max(1, count)); columns++) {
            int rows = (Math.max(1, count) + columns - 1) / columns;
            double tileWidth = (width - 12 * (columns - 1)) / columns, tileHeight = (height - 12 * (rows - 1)) / rows;
            if (tileWidth < 150 || tileHeight < statsTileMinimum(textScale)) continue;
            double shape = Math.abs(Math.log(tileWidth / tileHeight / 1.6));
            if (shape < bestShape) { bestShape = shape; best = new int[] { columns, rows }; }
        }
        return best;
    }
    // The weather temperature for the room left to it (the card less its fixed rows) and the card's width.
    public static double weatherTemperature(int design, double width, double room) {
        if (design == SMALL) return clamp(Math.round(Math.min(room / 1.12, width * 0.26)), 36, 96);
        if (design == MEDIUM) return clamp(Math.round(Math.min(room / 1.12, width * 0.15)), 40, 80);
        return clamp(Math.round(Math.min(room / 1.12, width * 0.2)), 52, 128);
    }
    // The other cards' designs, the same numbers as CardSizing.cs (docs/design/widget-size-views.md, "Every widget").
    // Small is the folded card from resizable-widgets.md; Large is a new layout.
    // Focus: Small under 300 tall; Large (the progress as a ring around the countdown) from 300 × 460.
    public static int focus(double width, double height) { return focusShort(height) ? SMALL : width >= 300 && height >= 460 ? LARGE : MEDIUM; }
    public static double focusRing(double width, double height) { return clamp(Math.floor(Math.min(width - 48, height - 186)), 160, 420); }
    // Now Playing: Small under 290 tall; Large (a big centred cover over the song) from 360 × 460.
    public static int media(double width, double height) { return mediaShort(height) ? SMALL : width >= 360 && height >= 460 ? LARGE : MEDIUM; }
    public static double mediaCover(double width, double height) { return clamp(Math.floor(Math.min(width - 88, height - 296)), 120, 360); }
    // The mixer's MASTER, COMPACT and FULL arrangements are its SMALL, MEDIUM and LARGE; a LARGE card 760 wide lists the
    // apps in two columns.
    public static int mixerDesign(double height) { int mode = mixer(height); return mode == MIXER_MASTER ? SMALL : mode == MIXER_COMPACT ? MEDIUM : LARGE; }
    public static int mixerColumns(double width, double height) { return mixer(height) == MIXER_FULL && width >= 760 ? 2 : 1; }
    // Spotify: SMALL is the compact card under 500 tall; LARGE (the song in a side panel with a big cover) from 760 wide.
    public static int spotify(double width, double height) { return spotifyCompact(height) ? SMALL : width >= 760 ? LARGE : MEDIUM; }
    // Quick Launch and Soundboard buttons by their cell: SMALL (glyph or name alone), MEDIUM (glyph over the name), LARGE
    // (a tile with the glyph, the name and what it does).
    public static int tile(double cellWidth, double cellHeight) { return cellHeight < 64 || cellWidth < 100 ? SMALL : cellWidth >= 150 && cellHeight >= 110 ? LARGE : MEDIUM; }
    // The hours a Large forecast shows: up to five, as many as fit 72 units each.
    public static int weatherHours(double width) { return (int) clamp(Math.floor((width - 48 - 16) / 72), 3, 5); }

    // Storage (docs/design/storage.md): a list under 340 wide or 200 tall, rows with names and sizes at the reference
    // 472 × 250, and ring tiles from 440 × 340. Each design holds as many drives as its rows or tiles fit; the rest are
    // counted in a last line. The same numbers as CardSizing.cs.
    public static int storage(double width, double height) { return width < 340 || height < 200 ? SMALL : width >= 440 && height >= 340 ? LARGE : MEDIUM; }
    // Medium rows carry a used-of-total line while every drive fits with it, and drop it to fit more drives.
    public static final double STORAGE_LIST_ROW = 40, STORAGE_ROW_HEIGHT = 76, STORAGE_COMPACT_ROW = 36, STORAGE_TILE_HEIGHT = 150;
    public static boolean storageDetailRows(double height, int count) { return count * STORAGE_ROW_HEIGHT + (count - 1) * 8 <= height; }
    public static int storageCapacity(int design, double width, double height, int count) {
        if (design == SMALL) return Math.max(1, (int) Math.floor((height + 8) / (STORAGE_LIST_ROW + 8)));
        if (design == MEDIUM) return Math.max(1, (int) Math.floor((height + 4) / (STORAGE_COMPACT_ROW + 4)));
        return storageTileColumns(width, count) * Math.max(1, (int) Math.floor((height + 12) / (STORAGE_TILE_HEIGHT + 12)));
    }
    public static int storageTileColumns(double width, int count) { return count <= 1 ? 1 : width >= 960 ? Math.min(count, 4) : Math.min(count, 2); }
    public static double storageRing(double tileWidth, double tileHeight) { return clamp(Math.floor(Math.min(tileWidth - 24, tileHeight - 24 - 72)), 56, 200); }
    public static double storageListValue(double rowHeight) { return clamp(Math.round(rowHeight * 0.5), 18, 32); }

    // World clocks (docs/design/world-clocks.md): rows under 340 wide or 200 tall, cities side by side at the reference
    // 472 × 250, and tiles from 440 × 300 (side by side while each is 170 wide, stacked otherwise). The same numbers as
    // CardSizing.cs.
    public static int clocks(double width, double height) { return width < 340 || height < 200 ? SMALL : width >= 440 && height >= 300 ? LARGE : MEDIUM; }
    public static final double CLOCK_ROW = 32, CLOCK_DETAIL_ROW = 46;
    public static int clockRows(double height) { return Math.max(1, (int) Math.floor((height + 8) / (CLOCK_ROW + 8))); }
    public static double clockListTime(double rowHeight) { return clamp(Math.round(rowHeight * 0.55), 18, 40); }
    public static double clockColumnTime(double column, double height, boolean use24) { return clamp(Math.round(Math.min((column - (use24 ? 4 : 34)) * 0.36, (height - 80) * 0.75)), 22, 72); }
    public static boolean clockTilesAcross(double width, int count) { return count <= 1 || (width - 12 * (count - 1)) / count >= 170; }
    public static double clockTileTime(double tileWidth, double tileHeight, boolean use24) { return clamp(Math.round(Math.min((tileWidth - 32 - (use24 ? 4 : 34)) * 0.36, (tileHeight - 32 - 80) * 0.8)), 28, 120); }
    public static double clockStackedTime(double tileWidth, double tileHeight) { return clamp(Math.round(Math.min((tileWidth - 32) * 0.18, (tileHeight - 20) * 0.75)), 24, 72); }

    // Prayer times (docs/design/prayer-times.md): the same numbers as CardSizing.cs.
    public static int prayer(double width, double height) { return width < 340 || height < 220 ? SMALL : width >= 440 && height >= 360 ? LARGE : MEDIUM; }
    public static double prayerTime(double width, double height) { return clamp(Math.round(Math.min(width * 0.24, (height - 60) * 0.7)), 28, 96); }
    public static double prayerNextColumn(double width) { return Math.floor(width * 0.46); }
    public static boolean prayerTilesAcross(double width) { return width >= 860; }
    public static double prayerLargeTop(double height, boolean across) { return across ? clamp(height - 120, 140, 260) : clamp(Math.round(height * 0.38), 140, 220); }
    public static double prayerTileTime(double tileWidth, boolean use24) { return clamp(Math.round((tileWidth - 24) / (use24 ? 2.7 : 3.9)), 18, 34); }

    // Notes and to-do (docs/design/notes.md): the same numbers as CardSizing.cs.
    public static int notes(double width, double height) { return width < 340 || height < 220 ? SMALL : width >= 700 && height >= 300 ? LARGE : MEDIUM; }
    public static double notesListColumn(double width) { return clamp(Math.floor(width * 0.46), 300, 520); }

    // Claude Code (docs/design/claude-watcher-v2.html; decision 017), the same numbers as the Windows CardSizing: the car
    // that needs you over the 5-hour meter under 360 wide or 280 tall, the two plan meters over the top garages between,
    // and the pit wall (plan row, pit stop strip, ledger, and from 1000 units of body the lanes and facts) from 640 × 420.
    public static int claude(double width, double height) { return width < 360 || height < 280 ? SMALL : width >= 640 && height >= 420 ? LARGE : MEDIUM; }
    public static final double CLAUDE_PLAN_HEIGHT = 124, CLAUDE_SIDE_WIDTH = 300;
    public static boolean claudeRevs(double bodyWidth) { return bodyWidth >= 860; }
    public static boolean claudeSide(double bodyWidth) { return bodyWidth >= 1000; }
    public static boolean claudeModels(double tableWidth) { return tableWidth >= 560; }
    public static boolean claudeLines(double tableWidth) { return tableWidth >= 660; }
    public static final double CLAUDE_MINI_HEIGHT = 84;
    public static double claudeMediumRow(double textScale) { return Math.max(44, Math.ceil(Math.round(15 * textScale) * 1.33 + 12 * 1.33 + 8)); }
    public static int claudeInlineCars(double nameRoom, int threads) {
        int fit = (int) Math.max(0, Math.floor((nameRoom - 110) / 32));
        if (threads <= Math.min(3, fit)) return threads;
        return (int) clamp(Math.min(3, fit - 1), 0, 3);
    }
    public static final double CLAUDE_SMALL_METER_HEIGHT = 52;
    public static boolean claudeSmallMeter(double bodyHeight, double textScale) { return bodyHeight >= CLAUDE_SMALL_METER_HEIGHT + Math.ceil(Math.round(17 * textScale) * 1.33) + 34 + 17; }
    public static double claudeSmallCar(double bodyWidth, double room) { return clamp(Math.floor(Math.min(bodyWidth * 0.34, room * 140 / 60)), 48, 120); }
    // Clipboard history (docs/design/clipboard-history.md): the same numbers as CardSizing.cs.
    public static int clip(double width, double height) { return width < 340 || height < 220 ? SMALL : width >= 440 && height >= 400 ? LARGE : MEDIUM; }

    // Calendar (docs/design/calendar.md): the same numbers as CardSizing.cs.
    public static int calendar(double width, double height) { return width < 340 || height < 220 ? SMALL : width >= 440 && height >= 380 ? LARGE : MEDIUM; }
    public static final double CALENDAR_ROW = 30;
    public static double calendarNextHeight(double textScale) { return Math.ceil(27 + 72 * textScale); }
    public static double calendarTimeColumn(boolean use24) { return use24 ? 96 : 128; }

    private static double clamp(double value, double min, double max) { return Double.isNaN(value) ? min : Math.max(min, Math.min(max, value)); }
}
