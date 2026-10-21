package com.alialyami.widgetscreen.core;

// Port of the Windows SystemStatsLayout: rows have one design size and the card scales them uniformly,
// so this picks the column count (1–3) that lets them grow the most.
public final class StatsLayout {
    public static final int MAX_COLUMNS = 3;

    public static double scale(double availableWidth, double availableHeight, double designWidth, double designHeight) {
        if (designWidth <= 0 || designHeight <= 0) return 0;
        return Math.max(0, Math.min(availableWidth / designWidth, availableHeight / designHeight));
    }

    public static int bestColumns(double availableWidth, double availableHeight, double cellWidth, double cellHeight, int count) {
        int best = 1; double bestScale = -1;
        for (int columns = 1; columns <= Math.min(MAX_COLUMNS, count); columns++) {
            int rows = (count + columns - 1) / columns;
            double scale = scale(availableWidth, availableHeight, cellWidth * columns, cellHeight * rows);
            // Fewer columns win ties so a reading list stays in its natural top-to-bottom order.
            if (scale > bestScale + 0.001) { best = columns; bestScale = scale; }
        }
        return best;
    }

    // Rows widen after the columns are chosen, so the scaled grid fills the card's width exactly.
    public static double rowWidth(double availableWidth, double availableHeight, double minimumWidth, double horizontalMargin, double cellHeight, int count, int columns) {
        int rows = (count + columns - 1) / columns;
        double scale = scale(availableWidth, availableHeight, (minimumWidth + horizontalMargin) * columns, cellHeight * rows);
        return scale > 0 ? Math.max(minimumWidth, availableWidth / scale / columns - horizontalMargin) : minimumWidth;
    }
}
