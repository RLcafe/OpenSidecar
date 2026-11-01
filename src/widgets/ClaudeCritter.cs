using System;
using System.Windows;
using System.Windows.Media;

namespace WidgetScreen
{
    // The critters' shapes and faces (docs/design/claude-watcher.md), in a 100 × 100 box. The phone's Critter.java has
    // the same numbers. Each project always gets the same body and colour, from a stable hash of its name; Amber is
    // kept for Needs you.
    public static class CritterPose
    {
        public static readonly string[] Accents = { "#BAF28B", "#8ED8F8", "#C5AEFF" };
        public const string NeedsYouColor = "#F5C47D";

        // FNV-1a over the lower-case name, so the PC and the phone pick the same critter.
        public static uint Hash(string project)
        {
            uint hash = 2166136261;
            foreach (char c in (project ?? "").Trim().ToLowerInvariant()) { hash ^= c; hash *= 16777619; }
            return hash;
        }
        public static int Shape(string project) { return (int)(Hash(project) % 3); }
        public static int Accent(string project) { return (int)(Hash(project) / 3 % 3); }

        // Bean, tall pill and squat round: left, top, right, bottom and corner radius.
        public static double[] Body(int shape)
        {
            switch (shape)
            {
                case 1: return new double[] { 28, 26, 72, 94, 22 };
                case 2: return new double[] { 10, 44, 90, 94, 25 };
                default: return new double[] { 14, 32, 86, 94, 30 };
            }
        }
        // Needs you hops 4 units on alternate heartbeats; Quiet mode keeps it still.
        public static double Hop(ClaudeState state, bool phase, bool quiet) { return state == ClaudeState.NeedsYou && phase && !quiet ? -4 : 0; }
        public static double EyeY(double[] body) { return body[1] + (body[3] - body[1]) * 0.36; }
        public static double EyeGap(double[] body) { return (body[2] - body[0]) * 0.2; }
        public static double MouthY(double[] body) { return EyeY(body) + (body[3] - body[1]) * 0.28; }
        public static double EyeRadius(ClaudeState state) { return state == ClaudeState.NeedsYou ? 10.5 : 8.5; }
        public static double PupilRadius(ClaudeState state) { return state == ClaudeState.NeedsYou ? 2.6 : 4; }
        // Working pupils look down and to one side, and hop to the other side on alternate heartbeats.
        public static double PupilX(ClaudeState state, bool phase, bool quiet) { return state == ClaudeState.Working ? (phase && !quiet ? 3 : -3) : 0; }
        public static double PupilY(ClaudeState state) { return state == ClaudeState.Working ? 3 : state == ClaudeState.NeedsYou ? -1 : 0; }
        public static bool EyesOpen(ClaudeState state) { return state == ClaudeState.Working || state == ClaudeState.NeedsYou; }
        // A project critter that has eaten its threads: puffed cheeks beside a closed mouth, a belly and a count bubble
        // at its top right (where the thinking trail would be).
        public static double CheekRadius(double[] body) { return Math.Min(9, (body[2] - body[0]) * 0.12); }
        public static double CheekX(double[] body) { return (body[2] - body[0]) * 0.3; }
        public static double[] Belly(double[] body) { double w = (body[2] - body[0]) * 0.56, h = (body[3] - body[1]) * 0.2; return new[] { 50 - w / 2, body[3] - h - 4, w, h }; }
        // The bubble sits on the body's top right corner, whatever the body's shape.
        public const double BubbleRadius = 13;
        public static double BubbleX(double[] body) { return Math.Min(86, body[2] - 2); }
        public static double BubbleY(double[] body) { return Math.Max(14, body[1] - 2); }
        // The mouth opening as it eats or spits (0 closed, 1 wide): half-widths and half-height in the 100 box.
        public static double MouthWidth(double open) { return 6 + 8 * Math.Max(0, Math.Min(1, open)); }
        public static double MouthHeight(double open) { return 1.5 + 10 * Math.Max(0, Math.Min(1, open)); }
        // Babies are 40% of their project's critter (Ali, 1 Nov 2026), never under 24 units.
        public static double Baby(double parent) { return Math.Max(24, Math.Round(parent * 0.4)); }
    }

    // One drawn critter. Brushes follow the theme through resource references; nothing animates except the pupils
    // and the hop, which move only when the dashboard's heartbeat flips Phase.
    public sealed class ClaudeCritter : FrameworkElement
    {
        private static Brush Frozen(string hex) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        private static readonly Brush[] AccentBrushes = { Frozen(CritterPose.Accents[0]), Frozen(CritterPose.Accents[1]), Frozen(CritterPose.Accents[2]) };
        private static readonly Brush Amber = Frozen(CritterPose.NeedsYouColor);

        private static FrameworkPropertyMetadata Renders(object value) { return new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender); }
        public static readonly DependencyProperty StateProperty = DependencyProperty.Register("State", typeof(ClaudeState), typeof(ClaudeCritter), Renders(ClaudeState.Ready));
        public static readonly DependencyProperty PhaseProperty = DependencyProperty.Register("Phase", typeof(bool), typeof(ClaudeCritter), Renders(false));
        public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register("Project", typeof(string), typeof(ClaudeCritter), Renders(""));
        public static readonly DependencyProperty PageProperty = DependencyProperty.Register("Page", typeof(Brush), typeof(ClaudeCritter), Renders(null));
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register("Text", typeof(Brush), typeof(ClaudeCritter), Renders(null));
        public static readonly DependencyProperty RaisedProperty = DependencyProperty.Register("Raised", typeof(Brush), typeof(ClaudeCritter), Renders(null));
        public static readonly DependencyProperty OutlineProperty = DependencyProperty.Register("Outline", typeof(Brush), typeof(ClaudeCritter), Renders(null));
        public static readonly DependencyProperty MutedProperty = DependencyProperty.Register("Muted", typeof(Brush), typeof(ClaudeCritter), Renders(null));
        // The project critter's extras: Collapsed with Count threads inside it, and Mouth (0–1) while it eats or spits.
        public static readonly DependencyProperty CollapsedProperty = DependencyProperty.Register("Collapsed", typeof(bool), typeof(ClaudeCritter), Renders(false));
        public static readonly DependencyProperty CountProperty = DependencyProperty.Register("Count", typeof(int), typeof(ClaudeCritter), Renders(0));
        public static readonly DependencyProperty MouthProperty = DependencyProperty.Register("Mouth", typeof(double), typeof(ClaudeCritter), Renders(0.0));
        public bool Collapsed { get { return (bool)GetValue(CollapsedProperty); } set { SetValue(CollapsedProperty, value); } }
        public int Count { get { return (int)GetValue(CountProperty); } set { SetValue(CountProperty, value); } }
        public double Mouth { get { return (double)GetValue(MouthProperty); } set { SetValue(MouthProperty, value); } }
        public ClaudeState State { get { return (ClaudeState)GetValue(StateProperty); } set { SetValue(StateProperty, value); } }
        public bool Phase { get { return (bool)GetValue(PhaseProperty); } set { SetValue(PhaseProperty, value); } }
        public string Project { get { return (string)GetValue(ProjectProperty); } set { SetValue(ProjectProperty, value); } }
        public bool Quiet { get; set; }

        public ClaudeCritter()
        {
            SetResourceReference(PageProperty, "Page"); SetResourceReference(TextProperty, "Text"); SetResourceReference(RaisedProperty, "Raised");
            SetResourceReference(OutlineProperty, "ControlBorder"); SetResourceReference(MutedProperty, "Muted");
            IsHitTestVisible = false;
        }

        // Where the mouth is, in this element's coordinates (babies are eaten into it and spat out of it).
        public Point MouthPoint()
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            var b = CritterPose.Body(CritterPose.Shape(Project));
            return new Point(ActualWidth / 2, (ActualHeight - size) / 2 + CritterPose.MouthY(b) * size / 100);
        }

        protected override void OnRender(DrawingContext context)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size < 8) return;
            var state = State; bool phase = Phase;
            double scale = size / 100;
            context.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
            context.PushTransform(new ScaleTransform(scale, scale));
            context.PushTransform(new TranslateTransform(0, CritterPose.Hop(state, phase, Quiet)));
            var page = (Brush)GetValue(PageProperty) ?? Brushes.Black; var text = (Brush)GetValue(TextProperty) ?? Brushes.White;
            var muted = (Brush)GetValue(MutedProperty) ?? Brushes.Gray;
            Brush accent = AccentBrushes[CritterPose.Accent(Project)];
            Brush body = state == ClaudeState.NeedsYou ? Amber : state == ClaudeState.Ready ? (Brush)GetValue(RaisedProperty) ?? Brushes.DimGray : accent;
            double[] b = CritterPose.Body(CritterPose.Shape(Project));
            var round = new Func<Brush, double, Pen>((brush, width) => new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

            // Arms up, behind the body, when it wants you.
            if (state == ClaudeState.NeedsYou)
            {
                double armY = b[1] + (b[3] - b[1]) * 0.55;
                context.DrawLine(round(Amber, 7), new Point(b[0] + 6, armY), new Point(b[0] - 7, b[1] + 2));
                context.DrawLine(round(Amber, 7), new Point(b[2] - 6, armY), new Point(b[2] + 7, b[1] + 2));
            }
            var outline = state == ClaudeState.Ready ? new Pen(accent, 2 / scale * 1.0) : new Pen((Brush)GetValue(OutlineProperty) ?? Brushes.Gray, 1 / scale);
            context.DrawRoundedRectangle(body, outline, new Rect(b[0], b[1], b[2] - b[0], b[3] - b[1]), b[4], b[4]);

            double eyeY = CritterPose.EyeY(b), gap = CritterPose.EyeGap(b), mouthY = CritterPose.MouthY(b), r = CritterPose.EyeRadius(state);
            foreach (double side in new[] { -1.0, 1.0 })
            {
                double x = 50 + side * gap;
                if (CritterPose.EyesOpen(state))
                {
                    context.DrawEllipse(text, null, new Point(x, eyeY), r, r);
                    double p = CritterPose.PupilRadius(state);
                    context.DrawEllipse(page, null, new Point(x + CritterPose.PupilX(state, phase, Quiet), eyeY + CritterPose.PupilY(state)), p, p);
                }
                else if (state == ClaudeState.Done)
                {
                    // Happy closed eyes: two upward arcs.
                    var arc = new StreamGeometry();
                    using (var g = arc.Open()) { g.BeginFigure(new Point(x - 6.5, eyeY + 2), false, false); g.QuadraticBezierTo(new Point(x, eyeY - 7), new Point(x + 6.5, eyeY + 2), true, true); }
                    arc.Freeze(); context.DrawGeometry(null, round(page, 3.2), arc);
                }
                else context.DrawLine(round(page, 3.2), new Point(x - 6, eyeY + 1), new Point(x + 6, eyeY + 1));
            }

            bool collapsed = Collapsed; double open = Mouth;
            if (collapsed)
            {
                // Full of threads: a belly, and puffed cheeks either side of the mouth.
                var belly = CritterPose.Belly(b);
                context.PushOpacity(0.22); context.DrawRoundedRectangle(page, null, new Rect(belly[0], belly[1], belly[2], belly[3]), belly[3] / 2, belly[3] / 2); context.Pop();
                double cheek = CritterPose.CheekRadius(b), cx = CritterPose.CheekX(b);
                context.PushOpacity(0.45);
                context.DrawEllipse(text, null, new Point(50 - cx, mouthY - 1), cheek, cheek * 0.8);
                context.DrawEllipse(text, null, new Point(50 + cx, mouthY - 1), cheek, cheek * 0.8);
                context.Pop();
            }
            if (open > 0.05) context.DrawEllipse(page, null, new Point(50, mouthY), CritterPose.MouthWidth(open), CritterPose.MouthHeight(open));
            else if (collapsed) context.DrawLine(round(page, 3), new Point(45, mouthY), new Point(55, mouthY));
            else if (state == ClaudeState.NeedsYou) context.DrawEllipse(null, round(page, 3), new Point(50, mouthY), 4.5, 4.5);
            else if (state == ClaudeState.Done)
            {
                var smile = new StreamGeometry();
                using (var g = smile.Open()) { g.BeginFigure(new Point(41, mouthY - 2), false, false); g.QuadraticBezierTo(new Point(50, mouthY + 8), new Point(59, mouthY - 2), true, true); }
                smile.Freeze(); context.DrawGeometry(null, round(page, 3), smile);
            }
            else context.DrawLine(round(page, 3), new Point(state == ClaudeState.Working ? 44 : 45, mouthY), new Point(state == ClaudeState.Working ? 56 : 55, mouthY));

            if (collapsed)
            {
                // How many threads it holds, in a bubble where the thinking trail would be.
                double bx = CritterPose.BubbleX(b), by = CritterPose.BubbleY(b);
                context.DrawEllipse(page, new Pen(state == ClaudeState.NeedsYou ? Amber : accent, 2.4), new Point(bx, by), CritterPose.BubbleRadius, CritterPose.BubbleRadius);
                string count = Count > 99 ? "99+" : Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var label = new FormattedText(count, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), count.Length > 2 ? 10 : 14, text, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                context.DrawText(label, new Point(bx - label.Width / 2, by - label.Height / 2));
                if (state == ClaudeState.NeedsYou) { context.DrawLine(round(text, 5), new Point(50, 3), new Point(50, 14)); context.DrawEllipse(text, null, new Point(50, 21.5), 2.8, 2.8); }
            }
            else if (state == ClaudeState.Working)
            {
                // The thinking trail beside the head.
                context.DrawEllipse(muted, null, new Point(80, 24), 2.4, 2.4);
                context.DrawEllipse(muted, null, new Point(88, 15), 3, 3);
                context.DrawEllipse(muted, null, new Point(95, 6), 3.6, 3.6);
            }
            else if (state == ClaudeState.NeedsYou)
            {
                context.DrawLine(round(text, 5), new Point(50, 3), new Point(50, 14));
                context.DrawEllipse(text, null, new Point(50, 21.5), 2.8, 2.8);
            }
            else if (state == ClaudeState.Ready)
            {
                var z = new StreamGeometry();
                using (var g = z.Open()) { g.BeginFigure(new Point(77, 7), false, false); g.LineTo(new Point(90, 7), true, true); g.LineTo(new Point(77, 20), true, true); g.LineTo(new Point(90, 20), true, true); }
                z.Freeze(); context.DrawGeometry(null, round(muted, 2.6), z);
            }
            context.Pop(); context.Pop(); context.Pop();
        }
    }
}
