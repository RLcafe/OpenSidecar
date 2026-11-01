using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace WidgetScreen
{
    // Drawn parts of the Sticker Sheet's readings (docs/design/sticker-sheet.md, phase 3): Weather's 12-hour temperature
    // curve with rain bars and its sunrise–sunset arc, and Storage's jars. Like StickerParts, they draw only when their data changes, never
    // animate and use no effects; brushes come from resources so palettes and inks apply live. The phone's
    // views/ReadingParts.java draws the same.
    public sealed class TemperatureCurve : FrameworkElement
    {
        public static readonly DependencyProperty LineProperty = DependencyProperty.Register("Line", typeof(Brush), typeof(TemperatureCurve), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RainProperty = DependencyProperty.Register("Rain", typeof(Brush), typeof(TemperatureCurve), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(TemperatureCurve), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LabelProperty = DependencyProperty.Register("Label", typeof(Brush), typeof(TemperatureCurve), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Line { get { return (Brush)GetValue(LineProperty); } set { SetValue(LineProperty, value); } }
        public Brush Rain { get { return (Brush)GetValue(RainProperty); } set { SetValue(RainProperty, value); } }
        public Brush Ink { get { return (Brush)GetValue(InkProperty); } set { SetValue(InkProperty, value); } }
        public Brush Label { get { return (Brush)GetValue(LabelProperty); } set { SetValue(LabelProperty, value); } }

        private double[] temperatures = new double[0];
        private int?[] rain = new int?[0];
        private string[] hours = new string[0];
        public int Points { get { return temperatures.Length; } }

        public TemperatureCurve()
        {
            IsHitTestVisible = false;
            SetResourceReference(LineProperty, "Text"); SetResourceReference(RainProperty, "InkGlacier");
            SetResourceReference(InkProperty, Stickers.InkKey("Weather")); SetResourceReference(LabelProperty, "Muted");
        }

        // Each hour from now: its temperature, its chance of rain (or null) and its label (or null for none).
        public void Show(double[] temperatures, int?[] rain, string[] hours)
        {
            this.temperatures = temperatures ?? new double[0]; this.rain = rain ?? new int?[0]; this.hours = hours ?? new string[0];
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext context)
        {
            int n = temperatures.Length;
            if (n < 2 || ActualWidth < 60 || ActualHeight < 40) return;
            double dip = 1; try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch (Exception) { }
            const double labelRoom = 18, valueRoom = 18, pad = 14;
            double top = valueRoom, bottom = ActualHeight - labelRoom, chart = bottom - top;
            double step = (ActualWidth - pad * 2) / (n - 1);
            // Rain bars stand on the base line, up to half the chart, in the Glacier ink at half strength.
            context.PushOpacity(0.55);
            for (int i = 0; i < n && i < rain.Length; i++)
            {
                if (!rain[i].HasValue || rain[i].Value < 5) continue;
                double h = chart * 0.5 * Math.Min(100, rain[i].Value) / 100, w = Math.Max(3, step * 0.5);
                context.DrawRoundedRectangle(Rain, null, new Rect(pad + i * step - w / 2, bottom - h, w, h), 2, 2);
            }
            context.Pop();
            // The curve keeps to the upper part of the chart so the bars stay readable under it.
            double[] levels = WeatherService.CurveLevels(temperatures);
            var points = new Point[n];
            for (int i = 0; i < n; i++) points[i] = new Point(pad + i * step, top + (1 - levels[i]) * chart * 0.62 + 4);
            var line = new StreamGeometry();
            using (var figure = line.Open()) { figure.BeginFigure(points[0], false, false); for (int i = 1; i < n; i++) figure.LineTo(points[i], true, true); }
            line.Freeze();
            context.DrawGeometry(null, PartCache.Pen(Line, 2.5), line);
            // Now is a dot in the ink with a keyline; the highest and lowest temperatures are written by their points.
            context.DrawEllipse(Ink, PartCache.Pen(Line, 2), points[0], 5, 5);
            var face = new Typeface(Stickers.Segoe, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
            int high = 0, low = 0;
            for (int i = 1; i < n; i++) { if (temperatures[i] > temperatures[high]) high = i; if (temperatures[i] < temperatures[low]) low = i; }
            foreach (int i in high == low ? new[] { high } : new[] { high, low })
            {
                var text = new FormattedText(Math.Round(temperatures[i]) + "°", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 13, Line, dip);
                double x = Math.Max(0, Math.Min(ActualWidth - text.Width, points[i].X - text.Width / 2));
                double y = i == high ? points[i].Y - text.Height - 3 : points[i].Y + 5;
                context.DrawText(text, new Point(x, Math.Max(0, Math.Min(bottom - text.Height, y))));
            }
            var regular = new Typeface(Stickers.Segoe, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            for (int i = 0; i < n && i < hours.Length; i++)
            {
                if (String.IsNullOrEmpty(hours[i])) continue;
                var text = new FormattedText(hours[i], CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, regular, 13, i == 0 ? Ink : Label, dip);
                double x = Math.Max(0, Math.Min(ActualWidth - text.Width, points[i].X - text.Width / 2));
                context.DrawText(text, new Point(x, ActualHeight - text.Height));
            }
        }
    }

    // The day as an arc from sunrise (left) to sunset (right): a dashed track, the part of the day gone in the Lemon
    // ink and the sun as a disc with a keyline. Before sunrise or after sunset the sun is a hollow dot at that end.
    public sealed class SunArc : FrameworkElement
    {
        public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(SunArc), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(SunArc), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty KeylineProperty = DependencyProperty.Register("Keyline", typeof(Brush), typeof(SunArc), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PartProperty = DependencyProperty.Register("Part", typeof(double), typeof(SunArc), new FrameworkPropertyMetadata(Double.NaN, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Ink { get { return (Brush)GetValue(InkProperty); } set { SetValue(InkProperty, value); } }
        public Brush Track { get { return (Brush)GetValue(TrackProperty); } set { SetValue(TrackProperty, value); } }
        public Brush Keyline { get { return (Brush)GetValue(KeylineProperty); } set { SetValue(KeylineProperty, value); } }
        // Where the sun is (WeatherService.SunPart), or NaN without sunrise and sunset.
        public double Part { get { return (double)GetValue(PartProperty); } set { SetValue(PartProperty, value); } }
        private static Pen dashed;

        public SunArc()
        {
            IsHitTestVisible = false;
            SetResourceReference(InkProperty, "InkLemon"); SetResourceReference(TrackProperty, "Muted"); SetResourceReference(KeylineProperty, "Text");
        }

        // The sun's place on an arc of radius r around c, for a part of the day from 0 to 1.
        public static Point Place(Point c, double r, double part)
        {
            double p = Math.Max(0, Math.Min(1, part));
            return new Point(c.X - r * Math.Cos(Math.PI * p), c.Y - r * Math.Sin(Math.PI * p));
        }

        protected override void OnRender(DrawingContext context)
        {
            if (Double.IsNaN(Part) || ActualWidth < 40 || ActualHeight < 24) return;
            double r = Math.Min(ActualWidth / 2 - 9, ActualHeight - 12); if (r < 12) return;
            var c = new Point(ActualWidth / 2, ActualHeight - 4);
            if (dashed == null || dashed.Brush != Track) { dashed = new Pen(Track, 2) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0), DashCap = PenLineCap.Flat }; dashed.Freeze(); }
            context.DrawLine(PartCache.Pen(Track, 1.5), new Point(c.X - r - 8, c.Y), new Point(c.X + r + 8, c.Y));
            context.DrawGeometry(null, dashed, PartCache.Get("sunarc" + Math.Round(r) + "x" + Math.Round(c.X) + "y" + Math.Round(c.Y), () => Arc(c, r, 1)));
            bool up = Part >= 0 && Part <= 1;
            if (up && Part > 0.01) { var lit = Arc(c, r, Part); lit.Freeze(); context.DrawGeometry(null, PartCache.Pen(Ink, 3), lit); }
            var sun = Place(c, r, Part);
            if (up) context.DrawEllipse(Ink, PartCache.Pen(Keyline, 2), sun, 7, 7);
            else context.DrawEllipse(null, PartCache.Pen(Track, 2), sun, 6, 6);
        }
        private static Geometry Arc(Point c, double r, double part)
        {
            var geometry = new StreamGeometry();
            using (var figure = geometry.Open())
            {
                figure.BeginFigure(Place(c, r, 0), false, false);
                figure.ArcTo(Place(c, r, part), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
            }
            return geometry;
        }
    }

    // A storage jar (Storage, phase 3): a lidded jar with a keyline and a solid lift, filled from the bottom by the used
    // space in the ink; a drive under the low-space threshold fills with Amber caution tape instead.
    public sealed class StorageJar : FrameworkElement
    {
        public static readonly DependencyProperty UsedProperty = DependencyProperty.Register("Used", typeof(double), typeof(StorageJar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LowProperty = DependencyProperty.Register("Low", typeof(bool), typeof(StorageJar), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty InkProperty = DependencyProperty.Register("Ink", typeof(Brush), typeof(StorageJar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty GlassProperty = DependencyProperty.Register("Glass", typeof(Brush), typeof(StorageJar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty KeylineProperty = DependencyProperty.Register("Keyline", typeof(Brush), typeof(StorageJar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LiftProperty = DependencyProperty.Register("Lift", typeof(Brush), typeof(StorageJar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        // Used space, 0–100.
        public double Used { get { return (double)GetValue(UsedProperty); } set { SetValue(UsedProperty, value); } }
        public bool Low { get { return (bool)GetValue(LowProperty); } set { SetValue(LowProperty, value); } }
        public Brush Ink { get { return (Brush)GetValue(InkProperty); } set { SetValue(InkProperty, value); } }
        public Brush Glass { get { return (Brush)GetValue(GlassProperty); } set { SetValue(GlassProperty, value); } }
        public Brush Keyline { get { return (Brush)GetValue(KeylineProperty); } set { SetValue(KeylineProperty, value); } }
        public Brush Lift { get { return (Brush)GetValue(LiftProperty); } set { SetValue(LiftProperty, value); } }

        public StorageJar()
        {
            IsHitTestVisible = false;
            SetResourceReference(InkProperty, Stickers.InkKey("Storage")); SetResourceReference(GlassProperty, "Track");
            SetResourceReference(KeylineProperty, "Text"); SetResourceReference(LiftProperty, "Page");
        }

        public static double WidthFor(double height) { return CardSizing.JarWidth(height); }

        protected override void OnRender(DrawingContext context)
        {
            double w = ActualWidth, h = ActualHeight; if (w < 12 || h < 16) return;
            double key = w < 40 ? 2 : Stickers.Keyline, lift = w < 40 ? 2 : Stickers.Lift, lid = Math.Max(3, Math.Round(h * 0.09));
            var body = new Rect(key / 2, lid + key, w - key, h - lift - lid - key * 1.5);
            double radius = Math.Min(body.Width * 0.24, 16);
            context.DrawRoundedRectangle(Lift, null, new Rect(body.X, body.Y + lift, body.Width, body.Height), radius, radius);
            context.DrawRoundedRectangle(Glass, null, body, radius, radius);
            double level = CardSizing.JarLevel(Used);
            if (level > 0)
            {
                var shape = new RectangleGeometry(body, radius, radius); shape.Freeze();
                context.PushClip(shape);
                double fill = body.Height * level;
                context.DrawRectangle(Low ? Stickers.Caution : Ink, null, new Rect(body.X, body.Bottom - fill, body.Width, fill));
                context.Pop();
            }
            context.DrawRoundedRectangle(null, PartCache.Pen(Keyline, key), body, radius, radius);
            var cap = new Rect(w * 0.14, key / 2, w * 0.72, lid);
            context.DrawRoundedRectangle(Ink, PartCache.Pen(Keyline, Math.Max(1.5, key - 1)), cap, Math.Min(4, lid / 2), Math.Min(4, lid / 2));
        }
    }
}
