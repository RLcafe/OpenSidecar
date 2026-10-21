package com.alialyami.widgetscreen.core;

// How the 1920 × 720 Windows dashboard fits this screen. Landscape always gets the dashboard arrangement;
// portrait gets the scrolling list (unless Settings → Display says otherwise). On small landscape screens such as
// phones, the widget canvas may shrink to 80% in each direction so text and buttons stay as large as possible;
// widgets keep their places and proportions, and only free space inside the cards gets smaller.
public final class DashboardFit {
    public static final double MARGIN_X = 64, CHROME_Y = 20 + 16 + 72 + 36, MIN_CANVAS = 0.8;
    // Below this many dp per design unit, the full canvas would make text and buttons too small.
    public static final double COMPACT_BELOW = 0.75;

    public final boolean list;
    // Pixels per design unit, and the logical size in design units.
    public final double scale, logicalWidth, logicalHeight;

    private DashboardFit(boolean list, double scale, double logicalWidth, double logicalHeight) {
        this.list = list; this.scale = scale; this.logicalWidth = logicalWidth; this.logicalHeight = logicalHeight;
    }

    public double canvasWidth() { return logicalWidth - MARGIN_X; }
    public double canvasHeight() { return logicalHeight - CHROME_Y; }

    public static DashboardFit choose(int widthPx, int heightPx, double density, String mode) {
        boolean landscape = widthPx >= heightPx;
        boolean list = "List".equals(mode) || ("Auto".equals(mode) && !landscape);
        if (list) {
            double scale = density * Math.max(0.85, Math.min(1, widthPx / density / 560));
            return new DashboardFit(true, scale, Math.floor(widthPx / scale), 0);
        }
        double full = Math.min(widthPx / 1920.0, heightPx / 720.0);
        double scale = full;
        if (full / density < COMPACT_BELOW) {
            double compact = Math.min(widthPx / (WidgetLayout.WIDTH * MIN_CANVAS + MARGIN_X), heightPx / (WidgetLayout.HEIGHT * MIN_CANVAS + CHROME_Y));
            scale = Math.max(full, Math.min(compact, density * COMPACT_BELOW));
        }
        return new DashboardFit(false, scale, Math.floor(widthPx / scale), Math.floor(heightPx / scale));
    }
}
