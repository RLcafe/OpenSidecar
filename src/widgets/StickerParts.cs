using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace WidgetScreen
{
    // Drawn parts of the Sticker Sheet's default five (docs/design/sticker-sheet.md, phase 2): the Focus kitchen-timer
    // dial and session pips, the disc that peeks out of Now Playing's sleeve, and the mixer's LED meters. They draw only
    // when their value changes; nothing animates and nothing uses effects. Fixed geometry (ticks, grooves) is built once
    // per size and frozen; brushes come from resources so palettes and inks apply live.
    internal static class PartCache
    {
        private static readonly Dictionary<string, Geometry> geometries = new Dictionary<string, Geometry>();
        public static Geometry Get(string key, Func<Geometry> build)
        {
            Geometry geometry;
            if (geometries.TryGetValue(key, out geometry)) return geometry;
            geometry = build(); geometry.Freeze(); geometries[key] = geometry;
            return geometry;
        }
        private static readonly Dictionary<Brush, Dictionary<double, Pen>> pens = new Dictionary<Brush, Dictionary<double, Pen>>();
        // A frozen pen per brush and thickness; brushes from resources are frozen, so pens can be shared.
        public static Pen Pen(Brush brush, double thickness)
        {
            if (brush == null) return null;
            Dictionary<double, Pen> byThickness; Pen pen;
            // Palettes make new brushes on each appearance change; old pens are dropped rather than kept forever.
            if (pens.Count > 64) pens.Clear();
            if (!pens.TryGetValue(brush, out byThickness)) pens[brush] = byThickness = new Dictionary<double, Pen>();
            if (byThickness.TryGetValue(thickness, out pen)) return pen;
            pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (pen.CanFreeze) pen.Freeze();
            return byThickness[thickness] = pen;
        }
    }

    // The Focus kitchen timer: a face with 60 minute ticks (every fifth longer), the time left as a wedge in the ink
    // sweeping clockwise from the top, a knob at the top, a 3-unit keyline and a solid lift in the Page colour. The
    // countdown and end time sit in the middle (the card's own text, laid over it).
    public sealed class TimerDial : FrameworkElement
    {
        public static readonly DependencyProperty LeftProperty = DependencyProperty.Register("Left", typeof(double), typeof(TimerDial), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(TimerDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FaceProperty = DependencyProperty.Register("Face", typeof(Brush), typeof(TimerDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TickProperty = DependencyProperty.Register("Tick", typeof(Brush), typeof(TimerDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty KeylineProperty = DependencyProperty.Register("Keyline", typeof(Brush), typeof(TimerDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LiftProperty = DependencyProperty.Register("Lift", typeof(Brush), typeof(TimerDial), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        // The part of the session left, 0 to 1.
        public double Left { get { return (double)GetValue(LeftProperty); } set { SetValue(LeftProperty, value); } }
        public Brush Ink { get { return (Brush)GetValue(InkProperty); } set { SetValue(InkProperty, value); } }
        public Brush Face { get { return (Brush)GetValue(FaceProperty); } set { SetValue(FaceProperty, value); } }
        public Brush Tick { get { return (Brush)GetValue(TickProperty); } set { SetValue(TickProperty, value); } }
        public Brush Keyline { get { return (Brush)GetValue(KeylineProperty); } set { SetValue(KeylineProperty, value); } }
        public Brush Lift { get { return (Brush)GetValue(LiftProperty); } set { SetValue(LiftProperty, value); } }

        public TimerDial()
        {
            IsHitTestVisible = false;
            SetResourceReference(InkProperty, Stickers.InkKey("Focus")); SetResourceReference(FaceProperty, "Raised");
            SetResourceReference(TickProperty, "Muted"); SetResourceReference(KeylineProperty, "Text"); SetResourceReference(LiftProperty, "Page");
        }

        // The inner disc that holds the countdown, as a part of the dial's size.
        public const double Hole = 0.62;

        protected override void OnRender(DrawingContext context)
        {
            double size = Math.Floor(Math.Min(ActualWidth, ActualHeight - Stickers.Lift));
            if (size < 40) return;
            double r = size / 2 - Stickers.Keyline / 2; var c = new Point(ActualWidth / 2, size / 2);
            if (Lift != null) context.DrawEllipse(Lift, null, new Point(c.X, c.Y + Stickers.Lift), r, r);
            context.DrawEllipse(Face, null, c, r, r);
            double outer = r * 0.94, inner = r * Hole;
            double left = Math.Max(0, Math.Min(1, Double.IsNaN(Left) ? 0 : Left));
            if (left >= 0.999) { context.DrawEllipse(Ink, null, c, outer, outer); }
            else if (left > 0.001) context.DrawGeometry(Ink, null, Wedge(c, outer, left));
            context.DrawEllipse(Face, null, c, inner, inner);
            // Ticks over the wedge, so the minutes read on the ink too.
            context.DrawGeometry(null, PartCache.Pen(Tick, Math.Max(1, size / 160)), PartCache.Get("ticks" + size, () => Ticks(size, r)));
            context.DrawEllipse(null, PartCache.Pen(Keyline, Stickers.Keyline), c, r, r);
            // The knob at the top.
            double knob = Math.Max(10, size * 0.09);
            context.DrawRoundedRectangle(Ink, PartCache.Pen(Keyline, 2), new Rect(c.X - knob * 0.7, c.Y - r - knob * 0.55, knob * 1.4, knob * 0.8), knob * 0.2, knob * 0.2);
        }

        private static Geometry Wedge(Point c, double radius, double part)
        {
            double angle = part * 2 * Math.PI;
            var geometry = new StreamGeometry();
            using (var figure = geometry.Open())
            {
                figure.BeginFigure(c, true, true);
                figure.LineTo(new Point(c.X, c.Y - radius), false, false);
                figure.ArcTo(new Point(c.X + radius * Math.Sin(angle), c.Y - radius * Math.Cos(angle)), new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, false, false);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Geometry Ticks(double size, double r)
        {
            var geometry = new StreamGeometry(); var c = new Point(size / 2, size / 2);
            using (var figure = geometry.Open())
                for (int i = 0; i < 60; i++)
                {
                    double angle = i * Math.PI / 30, from = r * (i % 5 == 0 ? 0.74 : 0.82), to = r * 0.9;
                    figure.BeginFigure(new Point(c.X + from * Math.Sin(angle), c.Y - from * Math.Cos(angle)), false, false);
                    figure.LineTo(new Point(c.X + to * Math.Sin(angle), c.Y - to * Math.Cos(angle)), true, false);
                }
            return geometry;
        }
        // The dial is drawn centred in its width; its own TextBlocks are offset by this much from the top.
        public static double Centre(double size) { return size / 2; }
    }

    // Today's focus sessions as tomato pips: one filled pip in the ink per finished session, dashed empty ones up to the
    // day's four, and "+N" past eight.
    public sealed class PipRow : FrameworkElement
    {
        public const int Goal = 4, MostShown = 8;
        public static readonly DependencyProperty CountProperty = DependencyProperty.Register("Count", typeof(int), typeof(PipRow), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
        public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(PipRow), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty EmptyProperty = DependencyProperty.Register("Empty", typeof(Brush), typeof(PipRow), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public int Count { get { return (int)GetValue(CountProperty); } set { SetValue(CountProperty, value); } }
        public Brush Ink { get { return (Brush)GetValue(InkProperty); } set { SetValue(InkProperty, value); } }
        public Brush Empty { get { return (Brush)GetValue(EmptyProperty); } set { SetValue(EmptyProperty, value); } }
        public double Pip { get; set; }
        private static Pen dashed;

        public PipRow()
        {
            Pip = 22; IsHitTestVisible = false;
            SetResourceReference(InkProperty, Stickers.InkKey("Focus")); SetResourceReference(EmptyProperty, "Muted");
        }
        public static int Shown(int count) { return Math.Max(Goal, Math.Min(MostShown, count)); }
        protected override Size MeasureOverride(Size available) { int n = Shown(Count); return new Size(n * Pip + (n - 1) * Pip * 0.4, Pip); }
        protected override void OnRender(DrawingContext context)
        {
            int n = Shown(Count); double r = Pip / 2 - 1, step = Pip * 1.4;
            if (dashed == null || dashed.Brush != Empty) { dashed = new Pen(Empty, 1.5) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) }; if (dashed.CanFreeze) dashed.Freeze(); }
            for (int i = 0; i < n; i++)
            {
                var c = new Point(Pip / 2 + i * step, Pip / 2);
                if (i < Count) { context.DrawEllipse(Ink, null, c, r, r); context.DrawRoundedRectangle(Empty, null, new Rect(c.X - 1.5, c.Y - r - 1, 3, 4), 1, 1); }
                else context.DrawEllipse(null, dashed, c, r - 0.75, r - 0.75);
            }
        }
    }

    // A record peeking out of Now Playing's sleeve: a dark disc with grooves and a label in the ink.
    public sealed class DiscPeek : FrameworkElement
    {
        public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(DiscPeek), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty GrooveProperty = DependencyProperty.Register("Groove", typeof(Brush), typeof(DiscPeek), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Ink { get { return (Brush)GetValue(InkProperty); } set { SetValue(InkProperty, value); } }
        public Brush Groove { get { return (Brush)GetValue(GrooveProperty); } set { SetValue(GrooveProperty, value); } }
        private static readonly Brush vinyl = Frozen(Color.FromRgb(0x0B, 0x0C, 0x0F));
        private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
        public DiscPeek() { IsHitTestVisible = false; SetResourceReference(InkProperty, Stickers.InkKey("Media")); SetResourceReference(GrooveProperty, "Track"); }
        protected override void OnRender(DrawingContext context)
        {
            double size = Math.Min(ActualWidth, ActualHeight); if (size < 20) return;
            double r = size / 2; var c = new Point(ActualWidth - r, ActualHeight / 2);
            context.DrawEllipse(vinyl, PartCache.Pen(Groove, 1), c, r - 0.5, r - 0.5);
            var groove = PartCache.Pen(Groove, 1);
            foreach (double part in Grooves) context.DrawEllipse(null, groove, c, r * part, r * part);
            context.DrawEllipse(Ink, null, c, r * 0.3, r * 0.3);
            context.DrawEllipse(vinyl, null, c, Math.Max(1.5, r * 0.04), Math.Max(1.5, r * 0.04));
        }
        private static readonly double[] Grooves = { 0.45, 0.6, 0.75, 0.88 };
    }

    // An LED level meter: square segments, lit from the bottom (vertical) or the left, in the ink with the top two in
    // Amber. The mixer's master strip uses it as a ladder; the Medium card under the master slider.
    public sealed class LedMeter : FrameworkElement
    {
        public static readonly DependencyProperty LevelProperty = DependencyProperty.Register("Level", typeof(double), typeof(LedMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(LedMeter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty UnlitProperty = DependencyProperty.Register("Unlit", typeof(Brush), typeof(LedMeter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty HotProperty = DependencyProperty.Register("Hot", typeof(Brush), typeof(LedMeter), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        // 0 to 1.
        public double Level { get { return (double)GetValue(LevelProperty); } set { SetValue(LevelProperty, value); } }
        public Brush Ink { get { return (Brush)GetValue(InkProperty); } set { SetValue(InkProperty, value); } }
        public Brush Unlit { get { return (Brush)GetValue(UnlitProperty); } set { SetValue(UnlitProperty, value); } }
        public Brush Hot { get { return (Brush)GetValue(HotProperty); } set { SetValue(HotProperty, value); } }
        public bool Vertical { get; set; }
        public double Segment { get; set; }
        public LedMeter()
        {
            Segment = 10; IsHitTestVisible = false;
            SetResourceReference(InkProperty, Stickers.InkKey("Mixer")); SetResourceReference(UnlitProperty, "Track"); SetResourceReference(HotProperty, "Warning");
        }
        // How many segments are lit for a level: none for silence, at least one for any sound.
        public static int Lit(double level, int count)
        {
            if (Double.IsNaN(level) || level <= 0.005) return 0;
            return Math.Max(1, Math.Min(count, (int)Math.Round(Math.Sqrt(Math.Min(1, level)) * count)));
        }
        public int Count { get { double length = Vertical ? ActualHeight : ActualWidth; return Math.Max(4, (int)Math.Floor((length + 3) / (Segment + 3))); } }
        protected override void OnRender(DrawingContext context)
        {
            if (ActualWidth <= 0 || ActualHeight <= 0) return;
            int count = Count, lit = Lit(Level, count);
            double gap = 3, length = Vertical ? ActualHeight : ActualWidth, cell = (length - gap * (count - 1)) / count, thick = Vertical ? ActualWidth : ActualHeight;
            for (int i = 0; i < count; i++)
            {
                Brush brush = i >= lit ? Unlit : i >= count - 2 ? Hot : Ink;
                double at = i * (cell + gap);
                var rect = Vertical ? new Rect(0, ActualHeight - at - cell, thick, cell) : new Rect(at, 0, cell, thick);
                context.DrawRoundedRectangle(brush, null, rect, 2, 2);
            }
        }
    }
}
