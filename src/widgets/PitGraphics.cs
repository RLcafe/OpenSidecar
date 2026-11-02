using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace WidgetScreen
{
    // The Claude card's pit wall instruments (docs/design/claude-watcher-v2.html; decision 017), drawn as vectors in the
    // theme's colours: the needle gauges, the pace meter, the week's laps, the burn line, the model mix and the 5-hour
    // lanes. They read their brushes from the theme at draw time; the card redraws them on every update. The phone's
    // PitViews.java draws the same.
    public abstract class PitInstrument : FrameworkElement
    {
        protected Brush B(string key, Brush fallback = null) { return TryFindResource(key) as Brush ?? fallback ?? Brushes.Gray; }
        protected static Brush Fixed(uint color) { var b = new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color)); b.Freeze(); return b; }
        protected static Brush Faded(Brush brush, double opacity) { var b = brush.CloneCurrentValue(); b.Opacity = opacity; b.Freeze(); return b; }
        protected static Pen Line(Brush brush, double width, bool round = true) { var p = new Pen(brush, width); if (round) { p.StartLineCap = PenLineCap.Round; p.EndLineCap = PenLineCap.Round; } p.Freeze(); return p; }
        public static readonly Typeface Ui = new Typeface("Segoe UI");
        protected FormattedText Words(string text, double size, Brush brush)
        {
            return new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Ui, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
        // Diagonal hatching inside a rectangle: the projection's "where this pace ends up".
        protected static void Hatch(DrawingContext dc, Rect area, Brush brush, double radius)
        {
            if (area.Width <= 0 || area.Height <= 0) return;
            dc.PushClip(new RectangleGeometry(area, radius, radius));
            dc.DrawRectangle(Faded(brush, 0.18), null, area);
            var pen = Line(Faded(brush, 0.55), 2, false);
            for (double x = area.Left - area.Height; x < area.Right; x += 6) dc.DrawLine(pen, new Point(x, area.Bottom), new Point(x + area.Height, area.Top));
            dc.Pop();
        }
    }

    // A half-circle needle gauge in a 140 × 84 box: the value as an arc, a needle, the projection as a dashed Amber arc
    // past it, a pace tick (how much of the window has passed) and a redline. Values are 0–1; NaN leaves a part out.
    public sealed class PitGauge : PitInstrument
    {
        public double Value, Projection = Double.NaN, Pace = Double.NaN, Red;
        public string Low = "0", High = "100";
        public bool Warn;
        public PitGauge() { Width = 140; Height = 84; }
        private static double Angle(double f) { return Math.PI + Math.Max(0, Math.Min(1, f)) * Math.PI; }
        private static Point At(double angle, double r) { return new Point(70 + r * Math.Cos(angle), 74 + r * Math.Sin(angle)); }
        private static Geometry Arc(double from, double to, double r)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                double a0 = Angle(from), a1 = Angle(to);
                c.BeginFigure(At(a0, r), false, false);
                c.ArcTo(At(a1, r), new Size(r, r), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }
        protected override void OnRender(DrawingContext dc)
        {
            double k = Math.Min(ActualWidth / 140, ActualHeight / 84);
            if (k <= 0) return;
            dc.PushTransform(new ScaleTransform(k, k));
            const double r = 58;
            dc.DrawGeometry(null, Line(B("Track"), 10), Arc(0, 1, r));
            if (Red > 0) dc.DrawGeometry(null, Line(Fixed(0x8CF09A9A), 10, false), Arc(Red, 1, r));
            if (!Double.IsNaN(Projection) && Projection > Value)
            {
                var dashed = new Pen(Faded(B("Warning"), 0.55), 10) { DashStyle = new DashStyle(new double[] { 0.3, 0.3 }, 0) }; dashed.Freeze();
                dc.DrawGeometry(null, dashed, Arc(Value, Math.Min(1, Projection), r));
            }
            if (Value > 0.002) dc.DrawGeometry(null, Line(Warn ? B("Warning") : B("Accent"), 10), Arc(0, Value, r));
            var tick = Line(B("Muted"), 1.5, false);
            for (int i = 0; i <= 10; i++) { double a = Angle(i / 10.0); dc.DrawLine(tick, At(a, r - 12), At(a, r - (i % 5 == 0 ? 20 : 16))); }
            if (!Double.IsNaN(Pace)) { double a = Angle(Pace); dc.DrawLine(Line(B("Text"), 2.5), At(a, r - 7), At(a, r + 8)); }
            double n = Angle(Value);
            dc.DrawLine(Line(B("Text"), 3), new Point(70, 74), At(n, r - 18));
            dc.DrawEllipse(B("Text"), null, new Point(70, 74), 6, 6);
            var low = Words(Low, 10, B("Muted")); dc.DrawText(low, new Point(10, 84 - low.Baseline));
            var high = Words(High, 10, B("Muted")); dc.DrawText(high, new Point(130 - high.Width, 84 - high.Baseline));
            dc.Pop();
        }
    }

    // A flat meter: used, the projection hatched past it, and a pace tick. Values are 0–1.
    public sealed class PaceBar : PitInstrument
    {
        public double Value, Projection = Double.NaN, Pace = Double.NaN;
        public bool Warn;
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, radius = h / 2;
            if (w <= 0 || h <= 0) return;
            dc.DrawRoundedRectangle(B("Track"), null, new Rect(0, 0, w, h), radius, radius);
            if (!Double.IsNaN(Projection) && Projection > Value) Hatch(dc, new Rect(0, 0, w * Math.Min(1, Projection), h), Warn ? B("Warning") : B("Accent"), radius);
            if (Value > 0) dc.DrawRoundedRectangle(Warn && Value >= 0.8 ? B("Warning") : B("Accent"), null, new Rect(0, 0, Math.Max(h, w * Math.Min(1, Value)), h), radius, radius);
            if (!Double.IsNaN(Pace)) dc.DrawRoundedRectangle(B("Text"), null, new Rect(Math.Max(0, Math.Min(w - 2, w * Pace - 1)), -4, 2, h + 8), 1, 1);
        }
    }

    // The week as seven laps, one per day from the window's start, with the day's name under each, the lap it's on
    // outlined, the projection hatched, and a chequered flag at the reset.
    public sealed class WeekLaps : PitInstrument
    {
        public double Percent, Projection = Double.NaN;
        public int Now = -1;
        public string[] Days = new string[7];
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, flag = 22, gap = 3, lap = (w - flag - gap * 7) / 7, bar = 14;
            if (lap <= 4) return;
            for (int i = 0; i < 7; i++)
            {
                var box = new Rect(i * (lap + gap), 0, lap, bar);
                dc.DrawRoundedRectangle(B("Track"), null, box, 4, 4);
                double used = ClaudePace.Lap(Percent, i), proj = Double.IsNaN(Projection) ? 0 : ClaudePace.Lap(Projection, i);
                if (proj > used) Hatch(dc, new Rect(box.X, 0, lap * proj, bar), B("Accent"), 4);
                if (used > 0) { dc.PushClip(new RectangleGeometry(box, 4, 4)); dc.DrawRectangle(B("Accent"), null, new Rect(box.X, 0, lap * used, bar)); dc.Pop(); }
                if (i == Now) dc.DrawRoundedRectangle(null, new Pen(B("Text"), 1), new Rect(box.X + 0.5, 0.5, lap - 1, bar - 1), 4, 4);
                var day = Words(Days[i] ?? "", 11, B("Muted"));
                dc.DrawText(day, new Point(box.X + (lap - day.Width) / 2, bar + 2));
            }
            // The chequered flag.
            double fx = 7 * (lap + gap);
            dc.DrawRoundedRectangle(B("Text"), null, new Rect(fx, 0, flag, bar), 3, 3);
            var page = B("Page");
            for (int c = 0; c < 4; c++) dc.DrawRectangle(page, null, new Rect(fx + c * flag / 4, c % 2 == 0 ? 0 : bar / 2, flag / 4, bar / 2));
        }
    }

    // The last hour's tokens a minute as a line over a soft fill, with the redline dashed and the latest point marked.
    public sealed class BurnLine : PitInstrument
    {
        public double[] Values = new double[0];
        public double Maximum = 1;
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || Values.Length < 2) return;
            var guide = new Pen(B("Track"), 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) }; guide.Freeze();
            double redY = h - h * ClaudePace.Redline;
            dc.DrawLine(guide, new Point(0, redY), new Point(w, redY));
            Func<int, Point> at = i => new Point(i * w / (Values.Length - 1), h - 2 - (h - 4) * Math.Max(0, Math.Min(1, Values[i] / Math.Max(1, Maximum))));
            var line = new StreamGeometry(); var area = new StreamGeometry();
            using (var c = line.Open()) { c.BeginFigure(at(0), false, false); for (int i = 1; i < Values.Length; i++) c.LineTo(at(i), true, true); }
            using (var c = area.Open()) { c.BeginFigure(new Point(0, h), true, true); for (int i = 0; i < Values.Length; i++) c.LineTo(at(i), true, false); c.LineTo(new Point(w, h), true, false); }
            line.Freeze(); area.Freeze();
            dc.DrawGeometry(Faded(B("Accent"), 0.16), null, area);
            dc.DrawGeometry(null, Line(B("Accent"), 2), line);
            dc.DrawEllipse(B("Accent"), null, at(Values.Length - 1), 3.5, 3.5);
        }
    }

    // A thread's or project's model mix: Opus, Sonnet and Haiku shares as one bar.
    public sealed class MixBar : PitInstrument
    {
        public static readonly uint[] Colors = { 0xFFBAF28B, 0xFF8ED8F8, 0xFFC5AEFF };
        public double[] Shares = new double[3];
        public MixBar() { Height = 6; }
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var box = new Rect(0, 0, w, h);
            dc.PushClip(new RectangleGeometry(box, h / 2, h / 2));
            dc.DrawRectangle(B("Track"), null, box);
            double x = 0;
            for (int i = 0; i < 3; i++) { double part = w * Math.Max(0, Shares[i]); if (part > 0) dc.DrawRectangle(Fixed(Colors[i]), null, new Rect(x, 0, part, h)); x += part; }
            dc.Pop();
        }
    }

    // One project's lane on the 5-hour timeline: a bar per 10-minute bin, as tall and as strong as its share of the
    // busiest bin on the timeline.
    public sealed class LaneBins : PitInstrument
    {
        public double[] Bins = new double[ClaudePace.LaneBins];
        public double Maximum = 1;
        public uint Color = PitArt.Lime;
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, gap = 2, bar = (w - gap * (Bins.Length - 1)) / Bins.Length;
            if (bar <= 0) return;
            var empty = Faded(B("Track"), 0.6);
            for (int i = 0; i < Bins.Length; i++)
            {
                double v = Maximum <= 0 ? 0 : Bins[i] / Maximum;
                double height = v > 0 ? Math.Max(3, h * v) : 2;
                var brush = v > 0 ? Faded(Fixed(Color), 0.45 + 0.55 * v) : empty;
                dc.DrawRoundedRectangle(brush, null, new Rect(i * (bar + gap), h - height, bar, height), 2, 2);
            }
        }
    }

    // A car's context ring (decision 023): how full the thread's context window is, as an arc from the top, from 60%
    // (Muted), 80% (Amber, compaction soon) and 95% (Amber with a filled centre). Draws nothing below 60% or when the
    // reading is unknown. It sits on the car's top right corner on a Page-coloured disc, so it reads over any paint.
    public sealed class ContextRing : PitInstrument
    {
        public double Percent = Double.NaN;
        protected override void OnRender(DrawingContext dc)
        {
            int level = ClaudePace.ContextLevel(Percent);
            double size = Math.Min(ActualWidth, ActualHeight);
            if (level == 0 || size <= 0) return;
            var centre = new Point(ActualWidth / 2, ActualHeight / 2);
            double stroke = Math.Max(1.5, size / 6), r = size / 2 - stroke / 2 - 0.5;
            Brush ink = level == 1 ? B("Muted") : B("Warning");
            dc.DrawEllipse(B("Page"), null, centre, size / 2, size / 2);
            dc.DrawEllipse(null, Line(B("Track"), stroke), centre, r, r);
            double f = Math.Max(0, Math.Min(0.999, Percent / 100)), a0 = -Math.PI / 2, a1 = a0 + f * 2 * Math.PI;
            var arc = new StreamGeometry();
            using (var c = arc.Open())
            {
                c.BeginFigure(new Point(centre.X + r * Math.Cos(a0), centre.Y + r * Math.Sin(a0)), false, false);
                c.ArcTo(new Point(centre.X + r * Math.Cos(a1), centre.Y + r * Math.Sin(a1)), new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true, false);
            }
            arc.Freeze();
            dc.DrawGeometry(null, Line(ink, stroke), arc);
            if (level == 3) dc.DrawEllipse(ink, null, centre, Math.Max(1, r - stroke), Math.Max(1, r - stroke));
        }
    }
}
