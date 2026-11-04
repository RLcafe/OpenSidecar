using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WidgetScreen
{
    // The Claude card's instruments (decision 028, docs/design/claude-card.md), brought back from the old card's pit
    // wall: the needle gauge (the 5-hour use and tokens per minute), the week as seven laps, and the burn line. Drawn as
    // vectors in the theme's colours, which they take as resource references so a theme or ink change redraws them.
    // The card sets their values and calls InvalidateVisual. The phone's ClaudeGauges.java draws the same.
    public abstract class ClaudeInstrument : FrameworkElement
    {
        public static readonly DependencyProperty FillProperty = Register("Fill"), TrackProperty = Register("Track"), TextProperty = Register("Text"),
            MutedProperty = Register("Muted"), PageProperty = Register("Page"), WarningProperty = Register("Warning");
        private static DependencyProperty Register(string name)
        {
            return DependencyProperty.Register(name, typeof(Brush), typeof(ClaudeInstrument), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        }
        // The fill is the card's ink; the rest follow the theme.
        protected ClaudeInstrument(string ink)
        {
            SetResourceReference(FillProperty, ink); SetResourceReference(TrackProperty, "Track"); SetResourceReference(TextProperty, "Text");
            SetResourceReference(MutedProperty, "Muted"); SetResourceReference(PageProperty, "Page"); SetResourceReference(WarningProperty, "Warning");
        }
        protected Brush Fill { get { return (Brush)GetValue(FillProperty); } }
        protected Brush Track { get { return (Brush)GetValue(TrackProperty); } }
        protected Brush Ink { get { return (Brush)GetValue(TextProperty); } }
        protected Brush Muted { get { return (Brush)GetValue(MutedProperty); } }
        protected Brush Page { get { return (Brush)GetValue(PageProperty); } }
        protected Brush Warning { get { return (Brush)GetValue(WarningProperty); } }

        protected static Brush Fixed(uint color) { var b = new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color)); b.Freeze(); return b; }
        protected static Brush Faded(Brush brush, double opacity) { var b = brush.CloneCurrentValue(); b.Opacity = opacity; b.Freeze(); return b; }
        protected static Pen Line(Brush brush, double width, bool round = true)
        {
            var p = new Pen(brush, width);
            if (round) { p.StartLineCap = PenLineCap.Round; p.EndLineCap = PenLineCap.Round; }
            p.Freeze();
            return p;
        }
        private static readonly Typeface Ui = new Typeface("Segoe UI");
        protected FormattedText Words(string text, double size, Brush brush)
        {
            return new FormattedText(text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Ui, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
        // Diagonal hatching inside a rectangle: where this pace ends up.
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

    // A half-circle needle gauge drawn in a 140 × 84 box, scaled to fit and centred: the value as an arc in the ink
    // (Amber when Warn), a needle, the projection as a dashed Amber arc past it, a pace tick (how much of the window has
    // passed) and a redline. Values are 0–1; NaN leaves a part out. The 0 and 100 labels step aside when it's small.
    public sealed class ClaudeGauge : ClaudeInstrument
    {
        public const double BoxWidth = 140, BoxHeight = 84;
        public double Value, Projection = Double.NaN, Pace = Double.NaN, Red;
        public string Low = "0", High = "100";
        public bool Warn;
        private static readonly Brush Redline = Fixed(0x8CF09A9A);
        public ClaudeGauge(string ink) : base(ink) { }
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
        // The scale the box is drawn at.
        public double Scale { get { return Math.Max(0, Math.Min(ActualWidth / BoxWidth, ActualHeight / BoxHeight)); } }
        protected override void OnRender(DrawingContext dc)
        {
            double k = Scale;
            if (k <= 0) return;
            dc.PushTransform(new TranslateTransform(Math.Round((ActualWidth - BoxWidth * k) / 2), Math.Round((ActualHeight - BoxHeight * k) / 2)));
            dc.PushTransform(new ScaleTransform(k, k));
            const double r = 58;
            dc.DrawGeometry(null, Line(Track, 10), Arc(0, 1, r));
            if (Red > 0) dc.DrawGeometry(null, Line(Redline, 10, false), Arc(Red, 1, r));
            if (!Double.IsNaN(Projection) && Projection > Value)
            {
                var dashed = new Pen(Faded(Warning, 0.55), 10) { DashStyle = new DashStyle(new double[] { 0.3, 0.3 }, 0) }; dashed.Freeze();
                dc.DrawGeometry(null, dashed, Arc(Value, Math.Min(1, Projection), r));
            }
            if (Value > 0.002) dc.DrawGeometry(null, Line(Warn ? Warning : Fill, 10), Arc(0, Value, r));
            var tick = Line(Muted, 1.5, false);
            for (int i = 0; i <= 10; i++) { double a = Angle(i / 10.0); dc.DrawLine(tick, At(a, r - 12), At(a, r - (i % 5 == 0 ? 20 : 16))); }
            if (!Double.IsNaN(Pace)) { double a = Angle(Pace); dc.DrawLine(Line(Ink, 2.5), At(a, r - 7), At(a, r + 8)); }
            double n = Angle(Value);
            dc.DrawLine(Line(Ink, 3), new Point(70, 74), At(n, r - 18));
            dc.DrawEllipse(Ink, null, new Point(70, 74), 6, 6);
            if (k >= 0.7)
            {
                var low = Words(Low, 10, Muted); dc.DrawText(low, new Point(10, 84 - low.Baseline));
                var high = Words(High, 10, Muted); dc.DrawText(high, new Point(130 - high.Width, 84 - high.Baseline));
            }
            dc.Pop(); dc.Pop();
        }
    }

    // The week as seven laps, one per day from the window's start, with the day's name under each (or its first
    // letter, when the laps are narrow), the lap it's on outlined, the projection hatched, and a chequered flag at the
    // reset. 30 tall with the day names, 14 without.
    public sealed class ClaudeLaps : ClaudeInstrument
    {
        public const double Bar = 14, WithDays = 30;
        public double Percent, Projection = Double.NaN;
        public int Now = -1;
        public bool Warn;
        public string[] Days = new string[7];
        public ClaudeLaps(string ink) : base(ink) { }
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, flag = 22, gap = 3, lap = (w - flag - gap * 7) / 7;
            if (lap <= 4) return;
            bool days = ActualHeight >= WithDays - 0.5;
            Brush fill = Warn ? Warning : Fill;
            for (int i = 0; i < 7; i++)
            {
                var box = new Rect(i * (lap + gap), 0, lap, Bar);
                dc.DrawRoundedRectangle(Track, null, box, 4, 4);
                double used = ClaudePace.Lap(Percent, i), proj = Double.IsNaN(Projection) ? 0 : ClaudePace.Lap(Projection, i);
                if (proj > used) Hatch(dc, new Rect(box.X, 0, lap * proj, Bar), fill, 4);
                if (used > 0) { dc.PushClip(new RectangleGeometry(box, 4, 4)); dc.DrawRectangle(fill, null, new Rect(box.X, 0, lap * used, Bar)); dc.Pop(); }
                if (i == Now) dc.DrawRoundedRectangle(null, new Pen(Ink, 1), new Rect(box.X + 0.5, 0.5, lap - 1, Bar - 1), 4, 4);
                if (!days) continue;
                string name = Days[i] ?? "";
                var day = Words(name, 11, i == Now ? Ink : Muted);
                if (day.Width > lap + gap && name.Length > 1) day = Words(name.Substring(0, 1), 11, i == Now ? Ink : Muted);
                if (day.Width <= lap + gap) dc.DrawText(day, new Point(box.X + (lap - day.Width) / 2, Bar + 2));
            }
            // The chequered flag.
            double fx = 7 * (lap + gap);
            dc.DrawRoundedRectangle(Ink, null, new Rect(fx, 0, flag, Bar), 3, 3);
            for (int c = 0; c < 4; c++) dc.DrawRectangle(Page, null, new Rect(fx + c * flag / 4, c % 2 == 0 ? 0 : Bar / 2, flag / 4, Bar / 2));
        }
    }

    // The half hour's tokens a minute as a line over a soft fill, with the redline dashed and the latest minute marked.
    public sealed class ClaudeBurn : ClaudeInstrument
    {
        public double[] Values = new double[0];
        public double Maximum = 1;
        public ClaudeBurn(string ink) : base(ink) { }
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || Values == null || Values.Length < 2) return;
            var guide = new Pen(Track, 1) { DashStyle = new DashStyle(new double[] { 3, 4 }, 0) }; guide.Freeze();
            double redY = h - h * ClaudePace.Redline;
            dc.DrawLine(guide, new Point(0, redY), new Point(w, redY));
            double inset = 3.5;
            Func<int, Point> at = i => new Point(i * (w - inset) / (Values.Length - 1), h - 2 - (h - 4) * Math.Max(0, Math.Min(1, Values[i] / Math.Max(1, Maximum))));
            var line = new StreamGeometry(); var area = new StreamGeometry();
            using (var c = line.Open()) { c.BeginFigure(at(0), false, false); for (int i = 1; i < Values.Length; i++) c.LineTo(at(i), true, true); }
            using (var c = area.Open()) { c.BeginFigure(new Point(0, h), true, true); for (int i = 0; i < Values.Length; i++) c.LineTo(at(i), true, false); c.LineTo(new Point(w - inset, h), true, false); }
            line.Freeze(); area.Freeze();
            dc.DrawGeometry(Faded(Fill, 0.16), null, area);
            dc.DrawGeometry(null, Line(Fill, 2), line);
            dc.DrawEllipse(Fill, null, at(Values.Length - 1), 3.5, 3.5);
        }
    }
}
