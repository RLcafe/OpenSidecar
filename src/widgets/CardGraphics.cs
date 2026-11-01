using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WidgetScreen
{
    // Drawn parts for the System stats and Weather designs (docs/design/widget-size-views.md). They draw only when a
    // reading changes; nothing animates. Brushes are set with SetResourceReference so palettes and accents apply live.

    // A ring that fills clockwise from the top with a percentage.
    public sealed class RingGauge : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(RingGauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(RingGauge), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public Brush Track { get { return (Brush)GetValue(TrackProperty); } set { SetValue(TrackProperty, value); } }
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public double Thickness { get; set; }

        protected override void OnRender(DrawingContext context)
        {
            double size = Math.Min(ActualWidth, ActualHeight), thickness = Math.Max(1, Thickness);
            if (size <= thickness) return;
            double radius = (size - thickness) / 2; var centre = new Point(ActualWidth / 2, ActualHeight / 2);
            if (Track != null) context.DrawEllipse(null, new Pen(Track, thickness), centre, radius, radius);
            double part = Math.Max(0, Math.Min(100, Double.IsNaN(Value) ? 0 : Value)) / 100;
            if (part <= 0 || Fill == null) return;
            var pen = new Pen(Fill, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (part >= 0.999) { context.DrawEllipse(null, pen, centre, radius, radius); return; }
            double angle = part * 2 * Math.PI;
            var geometry = new StreamGeometry();
            using (var figure = geometry.Open())
            {
                figure.BeginFigure(new Point(centre.X, centre.Y - radius), false, false);
                figure.ArcTo(new Point(centre.X + radius * Math.Sin(angle), centre.Y - radius * Math.Cos(angle)), new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
            }
            geometry.Freeze();
            context.DrawGeometry(null, pen, geometry);
        }
    }

    // A thin rounded bar for a percentage.
    public sealed class MeterBar : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(MeterBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(MeterBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(MeterBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public Brush Track { get { return (Brush)GetValue(TrackProperty); } set { SetValue(TrackProperty, value); } }
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }

        protected override void OnRender(DrawingContext context)
        {
            double width = ActualWidth, height = ActualHeight, radius = height / 2;
            if (width <= 0 || height <= 0) return;
            if (Track != null) context.DrawRoundedRectangle(Track, null, new Rect(0, 0, width, height), radius, radius);
            double part = Math.Max(0, Math.Min(100, Double.IsNaN(Value) ? 0 : Value)) / 100;
            if (part > 0 && Fill != null) context.DrawRoundedRectangle(Fill, null, new Rect(0, 0, Math.Max(height, width * part), height), radius, radius);
        }
    }

    // A chunky meter in ten segments (Claude Code's plan limits): 4-unit gaps, radius 3, a segment lit for each 10%.
    public sealed class SegmentBar : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register("Value", typeof(double), typeof(SegmentBar), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackProperty = DependencyProperty.Register("Track", typeof(Brush), typeof(SegmentBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(SegmentBar), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public Brush Track { get { return (Brush)GetValue(TrackProperty); } set { SetValue(TrackProperty, value); } }
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public const int Segments = 10;
        // Segments lit for a percentage: any use lights the first, and a full one only at 100%.
        public static int Lit(double percent) { if (Double.IsNaN(percent) || percent <= 0) return 0; return Math.Max(1, Math.Min(Segments, (int)Math.Floor(percent / 10 + 0.0001))); }

        protected override void OnRender(DrawingContext context)
        {
            double gap = 4, width = (ActualWidth - gap * (Segments - 1)) / Segments, height = ActualHeight;
            if (width <= 1 || height <= 0) return;
            int lit = Lit(Value); double radius = Math.Min(3, height / 2);
            for (int i = 0; i < Segments; i++)
            {
                var brush = i < lit ? Fill : Track;
                if (brush != null) context.DrawRoundedRectangle(brush, null, new Rect(i * (width + gap), 0, width, height), radius, radius);
            }
        }
    }

    // The recent history of a reading, newest at the right: a line over a faint area. Percentages use a 0–100 scale;
    // rates (Maximum 0) scale to the largest value shown, and an optional second series is drawn as a muted line.
    public sealed class HistoryGraph : FrameworkElement
    {
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register("Stroke", typeof(Brush), typeof(HistoryGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty SecondStrokeProperty = DependencyProperty.Register("SecondStroke", typeof(Brush), typeof(HistoryGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Stroke { get { return (Brush)GetValue(StrokeProperty); } set { SetValue(StrokeProperty, value); } }
        public Brush SecondStroke { get { return (Brush)GetValue(SecondStrokeProperty); } set { SetValue(SecondStrokeProperty, value); } }
        // Hairlines at the floor and, for percentages, at 50%, so an idle or new graph still reads as a graph.
        public static readonly DependencyProperty GuideProperty = DependencyProperty.Register("Guide", typeof(Brush), typeof(HistoryGraph), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Guide { get { return (Brush)GetValue(GuideProperty); } set { SetValue(GuideProperty, value); } }
        public double Maximum { get; set; }
        public int Capacity { get; set; }
        private readonly List<double> values = new List<double>(), second = new List<double>();

        public HistoryGraph() { Maximum = 100; Capacity = 48; }
        public void SetValues(IList<double> next, IList<double> other = null)
        {
            values.Clear(); values.AddRange(next); second.Clear(); if (other != null) second.AddRange(other);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext context)
        {
            double width = ActualWidth, height = ActualHeight;
            if (width <= 0 || height <= 4 || Stroke == null) return;
            if (Guide != null)
            {
                context.DrawRectangle(Guide, null, new Rect(0, height - 1, width, 1));
                if (Maximum > 0) context.DrawRectangle(Guide, null, new Rect(0, Math.Round(height / 2), width, 1));
            }
            double maximum = Maximum;
            if (maximum <= 0) { maximum = 1; foreach (double v in values) maximum = Math.Max(maximum, v); foreach (double v in second) maximum = Math.Max(maximum, v); maximum *= 1.15; }
            if (second.Count >= 2 && SecondStroke != null) context.DrawGeometry(null, new Pen(SecondStroke, 1.5) { LineJoin = PenLineJoin.Round }, Line(second, width, height, maximum, false));
            if (values.Count < 2) return;
            var fill = Stroke.Clone(); fill.Opacity = 0.16; fill.Freeze();
            context.DrawGeometry(fill, null, Line(values, width, height, maximum, true));
            context.DrawGeometry(null, new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, Line(values, width, height, maximum, false));
        }
        private StreamGeometry Line(List<double> series, double width, double height, double maximum, bool area)
        {
            int capacity = Math.Max(2, Capacity); double pad = 2;
            var geometry = new StreamGeometry();
            using (var figure = geometry.Open())
            {
                for (int i = 0; i < series.Count; i++)
                {
                    double x = width * (capacity - series.Count + i) / (capacity - 1.0);
                    double y = pad + (height - 2 * pad) * (1 - Math.Max(0, Math.Min(maximum, series[i])) / maximum);
                    if (i == 0) { if (area) { figure.BeginFigure(new Point(x, height), true, true); figure.LineTo(new Point(x, y), false, false); } else figure.BeginFigure(new Point(x, y), false, false); }
                    else figure.LineTo(new Point(x, y), true, true);
                }
                if (area) figure.LineTo(new Point(width, height), false, false);
            }
            geometry.Freeze();
            return geometry;
        }
    }

    // The shared pieces of the size designs. A card's builder derives from it to call them by their short names.
    public abstract class CardParts
    {
        // A weather glyph (WeatherService.Glyph, a 24-unit grid) at any size, with a stroke that stays in proportion.
        public static Viewbox WeatherGlyph(string family, double size, string brush)
        {
            var path = new Path { Data = Geometry.Parse(WeatherService.Glyph(family)), StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            path.StrokeThickness = Math.Max(2, Math.Min(3.6, size * 0.05)) * 24 / size;
            path.SetResourceReference(Shape.StrokeProperty, brush);
            var grid = new Canvas { Width = 24, Height = 24 }; grid.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = grid, Stretch = Stretch.Uniform };
        }
        // Large readings: Segoe UI Light with tabular figures and a line only as tall as the digits need.
        public static TextBlock Reading(string text, double size)
        {
            var block = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.Light, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, LineHeight = Math.Round(size * 1.12) };
            Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular);
            TextOptions.SetTextFormattingMode(block, TextFormattingMode.Ideal);
            return block;
        }
        public static TextBlock Text(string text, string sizeKey, string brush)
        {
            var block = new TextBlock { Text = text };
            block.SetResourceReference(TextBlock.FontSizeProperty, sizeKey); block.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return block;
        }
        public static TextBlock Text(string text, double size, string brush)
        {
            var block = new TextBlock { Text = text, FontSize = size };
            block.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return block;
        }
        // The card heading style ("SYSTEM STATS"): SmallSize, semibold, muted.
        public static TextBlock Label(string text) { var block = Text(text, "SmallSize", "Muted"); block.FontWeight = FontWeights.SemiBold; return block; }
        public static TextBlock Caption(string text) { return Text(text, 13, "Muted"); }
        public static Border Tile(UIElement child, double radius, Thickness padding)
        {
            var tile = new Border { CornerRadius = new CornerRadius(radius), Padding = padding, Child = child };
            tile.SetResourceReference(Border.BackgroundProperty, "Raised");
            return tile;
        }
        public static T At<T>(T element, int row, int column = 0, int columnSpan = 1) where T : UIElement
        {
            Grid.SetRow(element, row); Grid.SetColumn(element, column); if (columnSpan > 1) Grid.SetColumnSpan(element, columnSpan);
            return element;
        }
        public static Grid Rows(params GridLength[] heights)
        {
            var grid = new Grid(); foreach (var height in heights) grid.RowDefinitions.Add(new RowDefinition { Height = height });
            return grid;
        }
        public static Grid Columns(params GridLength[] widths)
        {
            var grid = new Grid(); foreach (var width in widths) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            return grid;
        }
        public static readonly GridLength Star = new GridLength(1, GridUnitType.Star), Auto = GridLength.Auto;
    }

    // Parts of a built design that tests and the controller find by name; names are re-registered on each rebuild.
    public sealed class PartNames
    {
        private readonly FrameworkElement owner;
        private readonly List<string> names = new List<string>();
        public PartNames(FrameworkElement owner) { this.owner = owner; }
        public T Add<T>(T element, string name) where T : FrameworkElement
        {
            var scope = NameScope.GetNameScope(owner);
            if (scope == null) return element;
            if (scope.FindName(name) != null) scope.UnregisterName(name);
            scope.RegisterName(name, element); names.Add(name);
            return element;
        }
        public void Clear()
        {
            var scope = NameScope.GetNameScope(owner);
            if (scope != null) foreach (string name in names) if (scope.FindName(name) != null) scope.UnregisterName(name);
            names.Clear();
        }
    }
}
