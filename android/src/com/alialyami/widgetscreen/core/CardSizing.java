package com.alialyami.widgetscreen.core;

// How each card's content follows its size, the same numbers as the Windows CardSizing.cs
// (docs/design/any-screen-size.md). Like the Soundboard, Quick Launch fits as many buttons as its card holds; Focus,
// Now Playing and Weather grow their main reading; Spotify adds columns on wide cards. The mixer list and System stats
// already fill their cards. Text never goes below the 13-unit caption and buttons keep 48 units.
public final class CardSizing {
    // A Quick Launch button plus its 4-unit margins.
    public static final double DECK_CELL_WIDTH = 124, DECK_CELL_HEIGHT = 76;

    // Columns and rows of Quick Launch buttons for the grid area (the card less its padding and header).
    public static int[] deckGrid(double width, double height) {
        int columns = (int) Math.max(1, Math.min(8, Math.floor(Math.max(0, width) / DECK_CELL_WIDTH)));
        int rows = (int) Math.max(1, Math.min(6, Math.floor(Math.max(0, height) / DECK_CELL_HEIGHT)));
        return new int[] { columns, rows };
    }

    // When every button fits on one page, the smallest grid that holds them all, so the buttons grow into the card
    // instead of leaving empty cells; ties go to cells closest to the usual button shape.
    public static int[] deckGrid(double width, double height, int count) {
        int[] most = deckGrid(width, height);
        if (count <= 0 || count >= most[0] * most[1]) return most;
        int[] best = most;
        double bestScore = Double.MAX_VALUE;
        for (int c = 1; c <= most[0]; c++)
            for (int r = 1; r <= most[1]; r++) {
                if (c * r < count) continue;
                double shape = Math.abs(Math.log(width / c / (height / r) / (DECK_CELL_WIDTH / DECK_CELL_HEIGHT)));
                double score = (c * r - count) * 10 + shape;
                if (score < bestScore) { bestScore = score; best = new int[] { c, r }; }
            }
        return best;
    }

    // A single row puts the glyph beside the label; otherwise the glyph sits above it and grows with the button.
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

    // The Weather temperature (54 on the reference 250-unit card); its column is twice as wide.
    public static double weatherTemperature(double width, double height) { return clamp(Math.round(Math.min(height * 0.216, width * 0.14)), 40, 96); }

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
    // System stats rows drop their graph and bar under 380 wide, so the numbers stay large.
    public static boolean statsNarrow(double width) { return width < 380; }
    // Weather under 200 tall keeps the temperature and city and drops the details line.
    public static boolean weatherTiny(double height) { return height < 200; }
    // Spotify under 500 tall: a smaller now-playing strip, and the status row only while it has something to say.
    public static boolean spotifyCompact(double height) { return height < 500; }
    // Empty states for the room under the card's heading: 3 shows the icon, heading, sentence and button; 2 drops the
    // icon; 1 keeps the heading and button; 0 the button alone.
    public static int emptyDetail(double room) { return room >= 268 ? 3 : room >= 156 ? 2 : room >= 100 ? 1 : 0; }
    // The Discord channel name beside the Soundboard heading.
    public static boolean soundboardChannelShown(double width) { return width >= 360; }

    private static double clamp(double value, double min, double max) { return Double.isNaN(value) ? min : Math.max(min, Math.min(max, value)); }
}
