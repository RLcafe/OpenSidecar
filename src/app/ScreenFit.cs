using System;

namespace WidgetScreen
{
    // How the dashboard fits a screen of any size (docs/design/any-screen-size.md). It is designed at the 1920 × 720
    // scale: on the owner's screen one design unit is one pixel. Another screen gets the largest scale at which the
    // 1920 × 720 reference (720 × 1920 on an upright screen) still fits, and a logical size shaped like the screen,
    // so extra room becomes canvas instead of bigger text. The Android app's DashboardFit uses the same rule.
    public sealed class ScreenFit
    {
        public const double MarginX = 64, MarginY = 20 + 16, Header = 72, StackedHeader = 136, Footer = 36;
        // Below this many device units (Windows pixels, Android dp) per design unit, text and buttons would get too
        // small, so the scale may grow up to it as long as the canvas keeps 80% of the reference canvas each way.
        public const double CompactBelow = 0.75, MinCanvas = 0.8;

        public double Scale { get; private set; }
        public double LogicalWidth { get; private set; }
        public double LogicalHeight { get; private set; }
        // Narrow dashboards (upright screens) put the header's buttons on a second row under the clock.
        public bool Stacked { get; private set; }
        public double HeaderHeight { get { return Stacked ? StackedHeader : Header; } }
        // The widget canvas, rounded down to the 8-unit snap grid; layouts are saved per canvas size.
        public double CanvasWidth { get { return Math.Max(8, Math.Floor((LogicalWidth - MarginX) / 8) * 8); } }
        public double CanvasHeight { get { return Math.Max(8, Math.Floor((LogicalHeight - MarginY - HeaderHeight - Footer) / 8) * 8); } }

        // width and height in device units; density is device units per Android dp (1 on Windows, which measures in
        // pixels so a screen keeps the same arrangement at any Windows display scaling); stackBelow is the logical
        // width under which the header stacks.
        public static ScreenFit For(double width, double height, double density, double stackBelow) { return For(width, height, density, stackBelow, MinCanvas); }
        // minCanvas: how much of the reference canvas a compact dashboard must keep (80% by default). A small
        // high-DPI screen passes less (down to 60%) so the text stays readable and the canvas gives way instead.
        public static ScreenFit For(double width, double height, double density, double stackBelow, double minCanvas)
        {
            if (!(width > 0) || !(height > 0) || Double.IsInfinity(width) || Double.IsInfinity(height)) { width = 1920; height = 720; }
            if (!(density > 0)) density = 1;
            minCanvas = Math.Max(0.6, Math.Min(MinCanvas, Double.IsNaN(minCanvas) ? MinCanvas : minCanvas));
            bool upright = height > width;
            double referenceWidth = upright ? 720 : 1920, referenceHeight = upright ? 1920 : 720;
            double full = Math.Min(width / referenceWidth, height / referenceHeight), scale = full;
            // A screen at least the reference size is never changed by display scaling: it looks the same at any.
            if (full >= 1) density = 1;
            if (full / density < CompactBelow)
            {
                // A compact dashboard may be narrow enough to stack its header, which costs canvas height.
                double chrome = MarginY + ((referenceWidth - MarginX) * minCanvas + MarginX < stackBelow ? StackedHeader : Header) + Footer;
                double compact = Math.Min(width / ((referenceWidth - MarginX) * minCanvas + MarginX), height / ((referenceHeight - chrome) * minCanvas + chrome));
                scale = Math.Max(full, Math.Min(compact, density * CompactBelow));
            }
            double logicalWidth = Math.Floor(width / scale + 1e-6), logicalHeight = Math.Floor(height / scale + 1e-6);
            return new ScreenFit { Scale = scale, LogicalWidth = logicalWidth, LogicalHeight = logicalHeight, Stacked = logicalWidth < stackBelow };
        }

        public bool SameAs(ScreenFit other)
        {
            return other != null && other.LogicalWidth == LogicalWidth && other.LogicalHeight == LogicalHeight && other.Stacked == Stacked && other.Scale == Scale;
        }
    }
}
