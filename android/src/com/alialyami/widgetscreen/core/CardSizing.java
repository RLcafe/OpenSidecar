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

    // The Focus countdown: as wide as the card allows, and only as tall as the room above the buttons.
    public static double focusTime(double width, double height) { return clamp(Math.round(Math.min((width - 48) * 0.31, (height - 300) * 0.6)), 42, 160); }

    // The Now Playing cover: at least the reference 104, growing with the card's free height and width.
    public static double mediaArt(double width, double height) { return clamp(Math.round(Math.min(height - 220, (width - 48) * 0.36)), 104, 220); }

    // The Weather temperature (54 on the reference 250-unit card); its column is twice as wide.
    public static double weatherTemperature(double width, double height) { return clamp(Math.round(Math.min(height * 0.216, width * 0.14)), 40, 96); }

    // Spotify rows side by side once the card fits two 340-unit rows.
    public static int spotifyColumns(double width) { return (int) clamp(Math.floor((width - 48) / 340), 1, 3); }

    private static double clamp(double value, double min, double max) { return Double.isNaN(value) ? min : Math.max(min, Math.min(max, value)); }
}
