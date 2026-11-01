using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WidgetScreen
{
    // The Sticker Sheet parts every card shares (decision 017, docs/design/sticker-sheet.md): the label strip, the status
    // sticker, fact-rail chips, the state language and die-cut sticker shapes. Nothing here animates or uses effects
    // (no DropShadowEffect or BlurEffect); a sticker's lift is a solid copy in the Page colour. Geometries are built
    // once and frozen. Colours come from resources, so palettes, accents and inks apply live: "InkLime"…"InkLemon" are
    // the eight inks and "InkOf<id>" is the ink each widget wears (DashboardController.ApplyInks).
    public static class Stickers
    {
        public const double StripHeight = 28, StickerHeight = 24, StickerBorder = 1.5, Keyline = 3, Lift = 4, MaxTilt = 3;
        public static string InkKey(string id) { return "InkOf" + id; }
        public static readonly FontFamily Segoe = new FontFamily("Segoe UI");
        public static readonly FontFamily Glyphs = new FontFamily("Segoe MDL2 Assets");

        // Amber caution tape for warnings: diagonal Amber and dark stripes, 8 units a pair. Always shown with a glyph
        // and words, never as colour alone.
        public static readonly Brush Caution = MakeCaution();
        private static Brush MakeCaution()
        {
            var amber = (Color)ColorConverter.ConvertFromString(Inks.Hex("Amber")); var dark = (Color)ColorConverter.ConvertFromString(Inks.DarkText);
            var brush = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, SpreadMethod = GradientSpreadMethod.Repeat, StartPoint = new Point(0, 0), EndPoint = new Point(5.66, 5.66) };
            brush.GradientStops.Add(new GradientStop(amber, 0)); brush.GradientStops.Add(new GradientStop(amber, 0.5));
            brush.GradientStops.Add(new GradientStop(dark, 0.5)); brush.GradientStops.Add(new GradientStop(dark, 1));
            brush.Freeze();
            return brush;
        }

        // A fact-rail chip: a muted, letter-spaced caption (13, the caption floor) over a 17-unit semibold value.
        public static Border FactChip(string caption, string value, out TextBlock valueText)
        {
            var label = new SpacedText { Text = caption.ToUpperInvariant(), FontSize = 13, FontWeight = FontWeights.SemiBold, Spacing = 0.06 };
            label.SetResourceReference(SpacedText.ForegroundProperty, "Muted");
            valueText = new TextBlock { Text = value, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
            Typography.SetNumeralAlignment(valueText, FontNumeralAlignment.Tabular);
            var stack = new StackPanel(); stack.Children.Add(label); stack.Children.Add(valueText);
            var chip = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 7, 12, 8), Child = stack };
            chip.SetResourceReference(Border.BackgroundProperty, "Raised");
            AutomationProperties.SetName(chip, caption + ", " + value);
            return chip;
        }

        // Not set up or unavailable: a dashed outline with no fill around the reason in plain words.
        public static Grid Unavailable(string reason)
        {
            var grid = new Grid { MinHeight = 48 };
            var outline = new Rectangle { RadiusX = 12, RadiusY = 12, StrokeThickness = StickerBorder, StrokeDashArray = new DoubleCollection { 3, 2 } };
            outline.SetResourceReference(Shape.StrokeProperty, "Muted");
            var text = new TextBlock { Text = reason, FontSize = 15, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 10, 14, 10) };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            grid.Children.Add(outline); grid.Children.Add(text);
            return grid;
        }

        // Offline or stale: the last values stay, at 60%.
        public static void Stale(UIElement values, bool stale) { values.Opacity = stale ? 0.6 : 1; }

        // A hero sticker may tilt, by 3?� at most, and only when it holds at most one short number.
        public static void Tilt(UIElement sticker, double degrees)
        {
            double angle = Math.Max(-MaxTilt, Math.Min(MaxTilt, degrees));
            if (angle == 0) { sticker.RenderTransform = Transform.Identity; return; }
            var rotate = new RotateTransform(angle); rotate.Freeze();
            sticker.RenderTransformOrigin = new Point(0.5, 0.5); sticker.RenderTransform = rotate;
        }

        // Die-cut outlines, built once per size and frozen.
        private static readonly Dictionary<string, Geometry> shapes = new Dictionary<string, Geometry>();
        private static Geometry Cached(string key, Func<Geometry> build)
        {
            Geometry geometry;
            if (shapes.TryGetValue(key, out geometry)) return geometry;
            geometry = build(); geometry.Freeze(); shapes[key] = geometry;
            return geometry;
        }
        public static Geometry Rounded(double width, double height, double radius)
        {
            return Cached("r" + width + "x" + height + "r" + radius, () => new RectangleGeometry(new Rect(0, 0, width, height), radius, radius));
        }
        public static Geometry Disc(double size)
        {
            return Cached("d" + size, () => new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2));
        }
        // A scalloped badge: points bumps around a circle, for a sticker that holds one short number.
        public static Geometry Burst(double size, int points)
        {
            return Cached("b" + size + "p" + points, () => {
                var geometry = new StreamGeometry(); double r = size / 2, inner = r * 0.86;
                using (var figure = geometry.Open())
                {
                    int steps = Math.Max(6, points) * 2;
                    for (int i = 0; i <= steps; i++)
                    {
                        double angle = i * Math.PI * 2 / steps - Math.PI / 2, radius = i % 2 == 0 ? r : inner;
                        var point = new Point(r + radius * Math.Cos(angle), r + radius * Math.Sin(angle));
                        if (i == 0) figure.BeginFigure(point, true, true); else figure.LineTo(point, true, true);
                    }
                }
                return geometry;
            });
        }
    }

    // Letter-spaced capitals. WPF's TextBlock has no letter spacing, so each character is laid out once with
    // FormattedText and drawn at its spaced place; it ends in "…" when it doesn't fit.
    public sealed class SpacedText : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(string), typeof(SpacedText), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register("FontSize", typeof(double), typeof(SpacedText), new FrameworkPropertyMetadata(13.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register("Foreground", typeof(Brush), typeof(SpacedText), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
        public string Text { get { return (string)GetValue(TextProperty); } set { SetValue(TextProperty, value); } }
        public double FontSize { get { return (double)GetValue(FontSizeProperty); } set { SetValue(FontSizeProperty, value); } }
        public Brush Foreground { get { return (Brush)GetValue(ForegroundProperty); } set { SetValue(ForegroundProperty, value); } }
        private FontWeight weight = FontWeights.Normal; private double spacing;
        public FontWeight FontWeight { get { return weight; } set { weight = value; laid = null; InvalidateMeasure(); } }
        // Extra space after each character, in ems.
        public double Spacing { get { return spacing; } set { spacing = value; laid = null; InvalidateMeasure(); } }

        private FormattedText[] laid; private FormattedText ellipsis; private double height;
        private static void Changed(DependencyObject owner, DependencyPropertyChangedEventArgs e) { ((SpacedText)owner).laid = null; }

        private void Lay()
        {
            if (laid != null) return;
            string text = Text ?? ""; double dip = 1;
            try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch (Exception) { }
            var face = new Typeface(Stickers.Segoe, FontStyles.Normal, weight, FontStretches.Normal);
            Brush brush = Foreground ?? Brushes.Black;
            laid = new FormattedText[text.Length];
            for (int i = 0; i < text.Length; i++) laid[i] = new FormattedText(text[i].ToString(), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, brush, dip);
            ellipsis = new FormattedText("…", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, FontSize, brush, dip);
            height = ellipsis.Height;
        }
        private double Gap { get { return spacing * FontSize; } }
        public double NaturalWidth
        {
            get { Lay(); double width = 0; foreach (var c in laid) width += c.WidthIncludingTrailingWhitespace + Gap; return Math.Ceiling(Math.Max(0, width - Gap)); }
        }
        protected override Size MeasureOverride(Size available)
        {
            Lay();
            return new Size(Math.Min(NaturalWidth, available.Width), height);
        }
        protected override void OnRender(DrawingContext context)
        {
            Lay();
            double room = ActualWidth + 0.5, x = 0; bool fits = NaturalWidth <= room;
            double stop = fits ? room : room - ellipsis.WidthIncludingTrailingWhitespace;
            foreach (var c in laid)
            {
                double advance = c.WidthIncludingTrailingWhitespace;
                if (x + advance > stop) break;
                context.DrawText(c, new Point(x, 0)); x += advance + Gap;
            }
            if (!fits) context.DrawText(ellipsis, new Point(x, 0));
        }
    }

    // The label strip that names a card (replaces the grey uppercase title): a 28-unit pill in the widget's ink with the
    // name in Segoe UI Black 13, letter-spaced 0.09 em, in dark text. Under 200 units wide it folds to a 12-unit ink
    // dot and the short name. The strip draws in a host Border, so XAML can place it and keep its name.
    public sealed class LabelStrip
    {
        public readonly Border Host;
        public readonly string Id;
        private readonly Ellipse dot;
        private readonly SpacedText text;
        public bool IsCompact { get; private set; }
        public SpacedText Words { get { return text; } }

        public LabelStrip(Border host, string id)
        {
            Host = host; Id = id;
            host.Height = Stickers.StripHeight; host.CornerRadius = new CornerRadius(Stickers.StripHeight / 2);
            if (host.ReadLocalValue(FrameworkElement.HorizontalAlignmentProperty) == DependencyProperty.UnsetValue) host.HorizontalAlignment = HorizontalAlignment.Left;
            host.VerticalAlignment = VerticalAlignment.Center; host.SnapsToDevicePixels = true;
            dot = new Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            dot.SetResourceReference(Shape.FillProperty, Stickers.InkKey(id));
            text = new SpacedText { FontWeight = FontWeights.Black, Spacing = 0.09, VerticalAlignment = VerticalAlignment.Center };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(dot); row.Children.Add(text);
            host.Child = row;
            AutomationProperties.SetName(host, WidgetLayout.Title(id));
            Fit(Double.PositiveInfinity, 1);
        }
        public static LabelStrip Create(string id) { return new LabelStrip(new Border(), id); }

        // The strip's text follows the text size (up to 140%) and stays inside its 28-unit pill.
        public static double TextSize(double textScale) { return Math.Round(13 * Math.Max(0.9, Math.Min(1.4, Double.IsNaN(textScale) ? 1 : textScale)) * 2) / 2; }

        public void Fit(double cardWidth, double textScale)
        {
            IsCompact = Inks.Compact(cardWidth);
            text.Text = IsCompact ? Inks.ShortName(Id) : Inks.StripName(Id);
            text.FontSize = TextSize(textScale);
            dot.Visibility = IsCompact ? Visibility.Visible : Visibility.Collapsed;
            Host.Padding = IsCompact ? new Thickness(0) : new Thickness(12, 0, 12, 0);
            if (IsCompact) { Host.Background = null; text.SetResourceReference(SpacedText.ForegroundProperty, "Text"); }
            else { Host.SetResourceReference(Border.BackgroundProperty, Stickers.InkKey(Id)); text.SetResourceReference(SpacedText.ForegroundProperty, "InkText"); }
        }
    }

    // The status sticker at a card's top right: one short live fact in an outlined pill (ink border and text, 1.5-unit
    // border, 13 bold), or filled for an active state. Warnings are Amber with caution stripes and a glyph; unavailable
    // is a dashed outline; offline or stale is a stamp; loading is a static outline. It wraps the card's existing caption
    // TextBlock, which keeps its name and text, and hides while the caption is empty.
    public sealed class StatusSticker : Grid
    {
        public readonly TextBlock Caption;
        public readonly string Id;
        private bool warns;
        private readonly Rectangle outline;
        private readonly Border stripes;
        private readonly TextBlock glyph;
        private bool filled;
        private static readonly DependencyProperty WordsProperty = DependencyProperty.Register("Words", typeof(string), typeof(StatusSticker), new PropertyMetadata(null, (o, e) => ((StatusSticker)o).Update()));
        private static readonly DependencyProperty ShownProperty = DependencyProperty.Register("Shown", typeof(Visibility), typeof(StatusSticker), new PropertyMetadata(Visibility.Visible, (o, e) => ((StatusSticker)o).Update()));
        public Inks.StickerState State { get; private set; }
        // Filled: an active state in the ink (a prayer starting now). Warns: the words are a problem (Clipboard paused).
        public bool Filled { get { return filled; } set { if (filled != value) { filled = value; Update(); } } }
        public bool Warns { get { return warns; } set { if (warns != value) { warns = value; Update(); } } }
        // A glyph before a fact's words (the mixer's microphone in ON AIR), in the words' colour; null for none.
        private string factGlyph;
        public string Glyph { get { return factGlyph; } set { if (factGlyph != value) { factGlyph = value; Update(); } } }
        // The sticker around a caption, for cards that only hold the caption.
        public static StatusSticker For(TextBlock caption)
        {
            for (DependencyObject parent = caption == null ? null : caption.Parent; parent != null; parent = LogicalTreeHelper.GetParent(parent))
                if (parent is StatusSticker) return (StatusSticker)parent;
            return null;
        }

        public StatusSticker(TextBlock caption, string id, bool warns)
        {
            Caption = caption; Id = id; this.warns = warns;
            Height = Stickers.StickerHeight; VerticalAlignment = VerticalAlignment.Center; SnapsToDevicePixels = true; ClipToBounds = true;
            outline = new Rectangle { RadiusX = Stickers.StickerHeight / 2, RadiusY = Stickers.StickerHeight / 2, StrokeThickness = Stickers.StickerBorder };
            stripes = new Border { Width = 14, CornerRadius = new CornerRadius(Stickers.StickerHeight / 2, 0, 0, Stickers.StickerHeight / 2), Background = Stickers.Caution, Visibility = Visibility.Collapsed };
            glyph = new TextBlock { Text = "", FontFamily = Stickers.Glyphs, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "InkText");
            caption.FontSize = 13; caption.FontWeight = FontWeights.Bold; caption.VerticalAlignment = VerticalAlignment.Center;
            caption.HorizontalAlignment = HorizontalAlignment.Left; caption.Margin = new Thickness(10, 0, 10, 0);
            Typography.SetNumeralAlignment(caption, FontNumeralAlignment.Tabular);
            // A star column for the words, so a narrow card trims them instead of pushing the sticker out.
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(glyph, 1); Grid.SetColumn(caption, 2);
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Children.Add(stripes); row.Children.Add(glyph); row.Children.Add(caption);
            Children.Add(outline); Children.Add(row);
            // A binding (not a property descriptor, which would hold the caption forever) tells the sticker when the words change.
            SetBinding(WordsProperty, new System.Windows.Data.Binding("Text") { Source = caption });
            SetBinding(ShownProperty, new System.Windows.Data.Binding("Visibility") { Source = caption });
            Update();
        }

        // With less room than its padding and an ellipsis, the sticker steps aside rather than leave the card.
        // A warning also carries its stripes and glyph.
        public const double MinRoom = 40, MinWarningRoom = 64;
        private double Room { get { return State == Inks.StickerState.Warning ? MinWarningRoom : MinRoom; } }
        protected override Size MeasureOverride(Size constraint)
        {
            // Collapsing the parts (not the sticker, whose visibility follows its words) keeps them out of the layout.
            var parts = constraint.Width < Room ? Visibility.Collapsed : Visibility.Visible;
            foreach (UIElement child in Children) if (child.Visibility != parts) child.Visibility = parts;
            return parts == Visibility.Collapsed ? new Size(0, 0) : base.MeasureOverride(constraint);
        }
        protected override Size ArrangeOverride(Size arrangeSize)
        {
            return arrangeSize.Width < Room ? new Size(0, 0) : base.ArrangeOverride(arrangeSize);
        }

        // Puts a sticker where a caption was: same parent, place, alignment and margin.
        public static StatusSticker Wrap(TextBlock caption, string id, bool warns)
        {
            var parent = caption.Parent as Panel;
            int index = parent == null ? -1 : parent.Children.IndexOf(caption);
            var margin = caption.Margin; var horizontal = caption.HorizontalAlignment;
            if (parent != null) parent.Children.RemoveAt(index);
            var sticker = new StatusSticker(caption, id, warns) { Margin = margin, HorizontalAlignment = horizontal };
            DockPanel.SetDock(sticker, DockPanel.GetDock(caption));
            Grid.SetRow(sticker, Grid.GetRow(caption)); Grid.SetColumn(sticker, Grid.GetColumn(caption)); Grid.SetColumnSpan(sticker, Grid.GetColumnSpan(caption));
            if (parent != null) parent.Children.Insert(index, sticker);
            return sticker;
        }

        private void Update()
        {
            if (outline == null) return;
            string words = Caption.Text ?? "";
            Visibility = words.Length == 0 || Caption.Visibility != Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            State = Inks.StateOf(words, warns); InvalidateMeasure();
            bool warning = State == Inks.StickerState.Warning;
            bool factGlyphShown = State == Inks.StickerState.Fact && !String.IsNullOrEmpty(factGlyph);
            stripes.Visibility = warning ? Visibility.Visible : Visibility.Collapsed;
            glyph.Visibility = warning || factGlyphShown ? Visibility.Visible : Visibility.Collapsed;
            glyph.Text = factGlyphShown ? factGlyph : "";
            glyph.Margin = new Thickness(factGlyphShown ? 10 : 6, 0, 0, 0);
            glyph.SetResourceReference(TextBlock.ForegroundProperty, warning || (factGlyphShown && filled) ? "InkText" : Stickers.InkKey(Id));
            outline.StrokeDashArray = State == Inks.StickerState.Unavailable ? new DoubleCollection { 3, 2 } : null;
            outline.StrokeThickness = State == Inks.StickerState.Offline ? 2 : Stickers.StickerBorder;
            outline.RenderTransform = Transform.Identity;
            switch (State)
            {
                case Inks.StickerState.Warning:
                    outline.SetResourceReference(Shape.FillProperty, "Warning"); outline.SetResourceReference(Shape.StrokeProperty, "Warning");
                    Caption.SetResourceReference(TextBlock.ForegroundProperty, "InkText"); Caption.Margin = new Thickness(6, 0, 10, 0);
                    break;
                case Inks.StickerState.Unavailable: case Inks.StickerState.Loading:
                    outline.Fill = null; outline.SetResourceReference(Shape.StrokeProperty, "Muted");
                    Caption.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); Caption.Margin = new Thickness(10, 0, 10, 0);
                    break;
                case Inks.StickerState.Offline:
                    outline.Fill = null; outline.SetResourceReference(Shape.StrokeProperty, "Muted");
                    Caption.SetResourceReference(TextBlock.ForegroundProperty, "Text"); Caption.Margin = new Thickness(10, 0, 10, 0);
                    break;
                default:
                    if (filled) { outline.SetResourceReference(Shape.FillProperty, Stickers.InkKey(Id)); Caption.SetResourceReference(TextBlock.ForegroundProperty, "InkText"); }
                    else { outline.Fill = null; Caption.SetResourceReference(TextBlock.ForegroundProperty, Stickers.InkKey(Id)); }
                    outline.SetResourceReference(Shape.StrokeProperty, Stickers.InkKey(Id)); Caption.Margin = new Thickness(factGlyphShown ? 6 : 10, 0, 10, 0);
                    break;
            }
        }
    }

    // A die-cut hero sticker: the shape in its fill with a 3-unit light keyline (Text colour), over a solid copy of
    // itself 4 units lower in the Page colour. Its geometry comes frozen from Stickers (Rounded, Disc, Burst).
    public sealed class StickerShape : FrameworkElement
    {
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register("Fill", typeof(Brush), typeof(StickerShape), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty KeylineProperty = DependencyProperty.Register("Keyline", typeof(Brush), typeof(StickerShape), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (o, e) => ((StickerShape)o).pen = null));
        public static readonly DependencyProperty LiftProperty = DependencyProperty.Register("Lift", typeof(Brush), typeof(StickerShape), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Fill { get { return (Brush)GetValue(FillProperty); } set { SetValue(FillProperty, value); } }
        public Brush Keyline { get { return (Brush)GetValue(KeylineProperty); } set { SetValue(KeylineProperty, value); } }
        public Brush Lift { get { return (Brush)GetValue(LiftProperty); } set { SetValue(LiftProperty, value); } }
        private static readonly Transform lifted = Frozen(new TranslateTransform(0, Stickers.Lift));
        private static Transform Frozen(Transform t) { t.Freeze(); return t; }
        private Geometry shape; private Pen pen;

        public StickerShape(Geometry shape, string ink)
        {
            Shape = shape;
            SetResourceReference(FillProperty, ink); SetResourceReference(KeylineProperty, "Text"); SetResourceReference(LiftProperty, "Page");
        }
        public Geometry Shape
        {
            get { return shape; }
            set { shape = value; if (shape != null && !shape.IsFrozen && shape.CanFreeze) shape.Freeze(); InvalidateMeasure(); InvalidateVisual(); }
        }
        protected override Size MeasureOverride(Size available)
        {
            if (shape == null) return new Size(0, 0);
            var bounds = shape.Bounds;
            return new Size(bounds.Right + Stickers.Keyline / 2, bounds.Bottom + Stickers.Lift + Stickers.Keyline / 2);
        }
        protected override void OnRender(DrawingContext context)
        {
            if (shape == null) return;
            if (pen == null && Keyline != null) { pen = new Pen(Keyline, Stickers.Keyline) { LineJoin = PenLineJoin.Round }; if (pen.CanFreeze) pen.Freeze(); }
            if (Lift != null) { context.PushTransform(lifted); context.DrawGeometry(Lift, null, shape); context.Pop(); }
            context.DrawGeometry(Fill, pen, shape);
        }
    }
}
