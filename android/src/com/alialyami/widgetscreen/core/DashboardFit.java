package com.alialyami.widgetscreen.core;

// How the dashboard fits this screen: the same rule as the Windows ScreenFit (docs/design/any-screen-size.md). It is
// designed at the 1920 × 720 scale; the screen gets the largest scale at which the 1920 × 720 reference (720 × 1920
// upright) still fits, and a logical size shaped like the screen, so extra room becomes canvas, not bigger text. On
// small screens such as phones the scale may grow to 0.75 dp per design unit as long as the canvas keeps 80% of the
// reference canvas in each direction; widgets keep their places and only free space inside the cards gets smaller.
//
// Which arrangement: any screen held sideways and an upright tablet (smallest width 600 dp or more) get the dashboard;
// an upright phone gets the cards in a scrolling list. Settings → Display can force either.
public final class DashboardFit {
    public static final double MARGIN_X = 64, MARGIN_Y = 20 + 16, HEADER = 72, STACKED_HEADER = 136, FOOTER = 36;
    public static final double COMPACT_BELOW = 0.75, MIN_CANVAS = 0.8;
    // Narrower dashboards (upright tablets) put the header's buttons on a second row under the clock.
    public static final double STACK_BELOW = 1400;
    public static final double TABLET_DP = 600;

    public final boolean list, stacked;
    // Pixels per design unit, and the logical size in design units.
    public final double scale, logicalWidth, logicalHeight;

    private DashboardFit(boolean list, boolean stacked, double scale, double logicalWidth, double logicalHeight) {
        this.list = list; this.stacked = stacked; this.scale = scale; this.logicalWidth = logicalWidth; this.logicalHeight = logicalHeight;
    }

    public double headerHeight() { return stacked ? STACKED_HEADER : HEADER; }
    // The widget canvas, rounded down to the 8-unit snap grid; layouts are saved per canvas size.
    public double canvasWidth() { return Math.max(8, Math.floor((logicalWidth - MARGIN_X) / 8) * 8); }
    public double canvasHeight() { return Math.max(8, Math.floor((logicalHeight - MARGIN_Y - headerHeight() - FOOTER) / 8) * 8); }

    public static DashboardFit choose(int widthPx, int heightPx, double density, String mode) {
        boolean upright = heightPx > widthPx;
        boolean tablet = Math.min(widthPx, heightPx) / density >= TABLET_DP;
        boolean list = "List".equals(mode) || ("Auto".equals(mode) && upright && !tablet);
        if (list) {
            double scale = density * Math.max(0.85, Math.min(1, widthPx / density / 560));
            return new DashboardFit(true, true, scale, Math.floor(widthPx / scale), 0);
        }
        return fit(widthPx, heightPx, density, STACK_BELOW);
    }

    // The dashboard fit alone; Windows uses the same rule with density 1 (pixels) and its own stackBelow.
    public static DashboardFit fit(double width, double height, double density, double stackBelow) {
        if (!(width > 0) || !(height > 0) || Double.isInfinite(width) || Double.isInfinite(height)) { width = 1920; height = 720; }
        if (!(density > 0)) density = 1;
        boolean upright = height > width;
        double referenceWidth = upright ? 720 : 1920, referenceHeight = upright ? 1920 : 720;
        double full = Math.min(width / referenceWidth, height / referenceHeight), scale = full;
        if (full / density < COMPACT_BELOW) {
            // A compact dashboard may be narrow enough to stack its header, which costs canvas height.
            double chrome = MARGIN_Y + ((referenceWidth - MARGIN_X) * MIN_CANVAS + MARGIN_X < stackBelow ? STACKED_HEADER : HEADER) + FOOTER;
            double compact = Math.min(width / ((referenceWidth - MARGIN_X) * MIN_CANVAS + MARGIN_X), height / ((referenceHeight - chrome) * MIN_CANVAS + chrome));
            scale = Math.max(full, Math.min(compact, density * COMPACT_BELOW));
        }
        double logicalWidth = Math.floor(width / scale + 1e-6), logicalHeight = Math.floor(height / scale + 1e-6);
        return new DashboardFit(false, logicalWidth < stackBelow, scale, logicalWidth, logicalHeight);
    }
}
