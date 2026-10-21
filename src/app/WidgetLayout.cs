using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Xml.Serialization;

namespace WidgetScreen
{
    public sealed class WidgetPlacement
    {
        public string Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Visible { get; set; }
        public int Layer { get; set; }
        public WidgetPlacement() { Visible = true; }
        [XmlIgnore] public Rect Bounds { get { return new Rect(X, Y, Width, Height); } }
        public WidgetPlacement Copy() { return (WidgetPlacement)MemberwiseClone(); }
    }

    // A layout saved for one canvas size (see ScreenFit). The 1856 × 576 layout stays in AppSettings.WidgetLayout,
    // so older versions keep reading it; every other canvas size gets one of these.
    public sealed class CanvasLayout
    {
        public double Width { get; set; }
        public double Height { get; set; }
        [XmlArray("Widgets"), XmlArrayItem("WidgetPlacement")]
        public WidgetPlacement[] Widgets { get; set; }
        [XmlIgnore] public Size Size { get { return new Size(Width, Height); } }
        public CanvasLayout Copy() { return new CanvasLayout { Width = Width, Height = Height, Widgets = Widgets == null ? null : WidgetLayout.Copy(Widgets) }; }
    }

    public static class WidgetLayout
    {
        // The canvas of the 1920 × 720 reference screen. Other screens get a canvas shaped like them (ScreenFit).
        public const double ReferenceWidth = 1856, ReferenceHeight = 576;
        public static readonly Size Reference = new Size(ReferenceWidth, ReferenceHeight);
        private const double Gap = 16;
        // 80% of the reference canvas, less what rounding the canvas down to the snap grid can take off.
        private const double MinimumShrink = 0.78;
        // The clock moved into the header, so "Clock" is no longer a widget. Saved Clock placements are dropped on load.
        public static readonly string[] Ids = { "Focus", "Media", "Shortcuts", "Mixer", "System", "Weather", "Spotify", "Soundboard" };
        public static string Title(string id)
        {
            switch (id) {
                case "Focus": return "Focus timer";
                case "Media": return "Now playing";
                case "Shortcuts": return "Quick launch";
                case "Mixer": return "Sound mixer";
                case "System": return "System stats";
                default: return id;
            }
        }
        public static Size Minimum(string id)
        {
            switch (id) {
                case "Focus": return new Size(296, 300);
                case "Media": return new Size(360, 310);
                case "Spotify": return new Size(560, 500);
                case "Shortcuts": return new Size(420, 190);
                case "Mixer": return new Size(380, 420);
                case "System": return new Size(380, 310);
                case "Soundboard": return new Size(360, 240);
                default: return new Size(360, 220);
            }
        }
        // On a canvas smaller than the reference (80% at least, see ScreenFit) minimum sizes shrink with it, so widgets
        // keep their places and proportions and only free space inside the cards gets smaller. Upright canvases compare
        // with the upright reference canvas (656 × 1712).
        public static Size Compact(Size canvas)
        {
            bool upright = canvas.Height > canvas.Width;
            double width = upright ? 720 - ScreenFit.MarginX : ReferenceWidth;
            double height = upright ? 1920 - ScreenFit.MarginY - ScreenFit.StackedHeader - ScreenFit.Footer : ReferenceHeight;
            return new Size(Math.Max(MinimumShrink, Math.Min(1, canvas.Width / width)), Math.Max(MinimumShrink, Math.Min(1, canvas.Height / height)));
        }
        public static Size Minimum(string id, Size canvas)
        {
            Size minimum = Minimum(id), k = Compact(canvas);
            return new Size(Math.Min(canvas.Width, Math.Round(minimum.Width * k.Width)), Math.Min(canvas.Height, Math.Round(minimum.Height * k.Height)));
        }
        // Layouts are kept per canvas size, rounded down to the 8-unit snap grid.
        public static Size Key(Size canvas) { return new Size(Math.Max(8, Math.Floor(canvas.Width / 8) * 8), Math.Max(8, Math.Floor(canvas.Height / 8) * 8)); }
        public static bool IsReference(Size canvas) { return Key(canvas) == Reference; }
        public static WidgetPlacement[] Copy(WidgetPlacement[] layout) { return layout.Select(p => p.Copy()).ToArray(); }
        private static WidgetPlacement Place(string id, double x, double y, double width, double height)
        {
            return new WidgetPlacement { Id = id, X = x, Y = y, Width = width, Height = height };
        }
        // The old column arrangement on the reference canvas, kept for settings that still carry column sizes.
        public static WidgetPlacement[] Default(AppSettings settings)
        {
            double[] widths = DashboardAppearance.ColumnWidths(settings);
            double focusX = settings.SwapSideWidgets ? ReferenceWidth - widths[0] + 16 : 0;
            double mediaX = settings.SwapSideWidgets ? widths[3] + 16 : widths[0];
            double mixerX = mediaX + widths[1];
            double infoX = settings.SwapSideWidgets ? 0 : mixerX + widths[2];
            bool stats = settings.ShowCpu || settings.ShowGpu || settings.ShowRam || settings.ShowNetwork || settings.ShowUptime;
            return new[] {
                Place("Focus", focusX, 0, widths[0] - 16, ReferenceHeight),
                Place("Media", mediaX, 0, widths[1] - 16, settings.MediaHeight - 16),
                Place("Shortcuts", mediaX, settings.MediaHeight, widths[1] - 16, ReferenceHeight - settings.MediaHeight),
                Place("Mixer", mixerX, 0, widths[2] - 16, settings.MixerHeight),
                Place("System", infoX, 0, widths[3], settings.SystemHeight - 16),
                Place("Weather", infoX, stats ? settings.SystemHeight : 0, widths[3], stats ? ReferenceHeight - settings.SystemHeight : ReferenceHeight),
                new WidgetPlacement { Id = "Spotify", X = 376, Y = 0, Width = 600, Height = 576, Visible = false },
                // Starts hidden; turned on in Edit layout once Discord is set up.
                new WidgetPlacement { Id = "Soundboard", X = 336, Y = 326, Width = 472, Height = 250, Visible = false }
            };
        }
        public static WidgetPlacement[] SpotifyDefault(AppSettings settings)
        {
            var result = Default(settings);
            foreach (var p in result)
            {
                p.Visible = p.Id != "System" && p.Id != "Weather" && p.Id != "Soundboard";
                if (p.Id == "Focus") SetBounds(p, new Rect(0, 0, 320, 576));
                if (p.Id == "Media") SetBounds(p, new Rect(336, 0, 472, 310));
                if (p.Id == "Shortcuts") SetBounds(p, new Rect(336, 326, 472, 250));
                if (p.Id == "Mixer") SetBounds(p, new Rect(824, 0, 456, 576));
                if (p.Id == "Spotify") SetBounds(p, new Rect(1296, 0, 560, 576));
            }
            return result;
        }
        private static double Finite(double value, double fallback)
        {
            return Double.IsNaN(value) || Double.IsInfinity(value) ? fallback : value;
        }
        // Layouts saved with the old Clock widget: when Focus sat directly under a visible Clock in the
        // same column (the old default), Focus grows up into the space the clock card leaves behind.
        private static void AbsorbClock(WidgetPlacement[] layout)
        {
            var clock = layout.FirstOrDefault(p => p != null && p.Id == "Clock");
            var focus = layout.FirstOrDefault(p => p != null && p.Id == "Focus");
            if (clock == null || focus == null || !clock.Visible) return;
            if (new[] { clock.X, clock.Y, clock.Width, clock.Height, focus.X, focus.Y, focus.Width, focus.Height }.Any(v => Double.IsNaN(v) || Double.IsInfinity(v))) return;
            double gap = focus.Y - (clock.Y + clock.Height);
            if (Math.Abs(focus.X - clock.X) > 1 || Math.Abs(focus.Width - clock.Width) > 1 || gap < 0 || gap > 32) return;
            var grown = new Rect(focus.X, clock.Y, focus.Width, focus.Y + focus.Height - clock.Y);
            foreach (var other in layout.Where(p => p != null && p.Visible && p.Id != "Clock" && p.Id != "Focus"))
            {
                Rect overlap = Rect.Intersect(other.Bounds, grown);
                if (!overlap.IsEmpty && overlap.Width > 1 && overlap.Height > 1) return;
            }
            focus.Height = grown.Height; focus.Y = grown.Y;
        }
        public static WidgetPlacement[] Normalize(WidgetPlacement[] layout, AppSettings settings) { return Normalize(layout, Default(settings), Reference); }
        public static WidgetPlacement[] Normalize(WidgetPlacement[] layout, AppSettings settings, Size canvas)
        {
            canvas = Key(canvas);
            return canvas == Reference ? Normalize(layout, settings) : Normalize(layout, Arrange(canvas), canvas);
        }
        // Every known widget gets exactly one placement, inside the canvas and at least its minimum size.
        private static WidgetPlacement[] Normalize(WidgetPlacement[] layout, WidgetPlacement[] defaults, Size canvas)
        {
            if (layout != null && layout.Any(p => p != null && p.Id == "Clock")) { layout = layout.Select(p => p == null ? null : p.Copy()).ToArray(); AbsorbClock(layout); }
            var result = new List<WidgetPlacement>();
            foreach (var fallback in defaults)
            {
                var supplied = layout == null ? null : layout.FirstOrDefault(entry => entry != null && entry.Id == fallback.Id);
                var p = supplied == null ? fallback.Copy() : supplied.Copy(); var minimum = Minimum(p.Id, canvas);
                p.Width = Math.Max(minimum.Width, Math.Min(canvas.Width, Finite(p.Width, fallback.Width)));
                p.Height = Math.Max(minimum.Height, Math.Min(canvas.Height, Finite(p.Height, fallback.Height)));
                p.X = Math.Max(0, Math.Min(canvas.Width - p.Width, Finite(p.X, fallback.X)));
                p.Y = Math.Max(0, Math.Min(canvas.Height - p.Height, Finite(p.Y, fallback.Y)));
                p.Layer = Math.Max(0, Math.Min(1000000, p.Layer));
                result.Add(p);
            }
            return result.ToArray();
        }

        // ----- Layouts per canvas size (docs/design/any-screen-size.md) -----

        // Settings from before free layouts (swapped sides or custom column sizes) keep their column arrangement as the
        // reference canvas default; everyone else gets the Spotify arrangement there.
        private static bool LegacyColumns(AppSettings s)
        {
            return s.SwapSideWidgets || s.ClockWidth != 1 || s.MediaWidth != 3 || s.MixerWidth != 3 || s.SystemWidth != 2
                || s.ClockHeight != 246 || s.MediaHeight != 326 || s.MixerHeight != 576 || s.SystemHeight != 326;
        }
        public static IEnumerable<CanvasLayout> SavedLayouts(AppSettings settings)
        {
            if (settings.WidgetLayout != null) yield return new CanvasLayout { Width = ReferenceWidth, Height = ReferenceHeight, Widgets = settings.WidgetLayout };
            if (settings.CanvasLayouts != null) foreach (var entry in settings.CanvasLayouts) if (entry != null && entry.Widgets != null) yield return entry;
        }
        public static WidgetPlacement[] Saved(AppSettings settings, Size canvas)
        {
            Size key = Key(canvas);
            var entry = SavedLayouts(settings).FirstOrDefault(c => c.Size == key);
            return entry == null ? null : entry.Widgets;
        }
        public static void Store(AppSettings settings, Size canvas, WidgetPlacement[] layout)
        {
            Size key = Key(canvas);
            if (key == Reference) { settings.WidgetLayout = Copy(layout); return; }
            var others = (settings.CanvasLayouts ?? new CanvasLayout[0]).Where(c => c != null && c.Size != key).ToList();
            others.Add(new CanvasLayout { Width = key.Width, Height = key.Height, Widgets = Copy(layout) });
            settings.CanvasLayouts = others.ToArray();
        }
        // The layout for a canvas: the one saved for its size; otherwise the saved layout of the closest shape that
        // stretches to it without squeezing a shown widget under its minimum; otherwise the default arrangement.
        public static WidgetPlacement[] ForCanvas(AppSettings settings, Size canvas)
        {
            canvas = Key(canvas);
            var saved = Saved(settings, canvas);
            if (saved != null) return Normalize(saved, settings, canvas);
            WidgetPlacement[] best = null; double distance = Double.MaxValue;
            foreach (var entry in SavedLayouts(settings))
            {
                var stretched = Stretch(Normalize(entry.Widgets, settings, entry.Size), entry.Size, canvas);
                if (!Fits(stretched, canvas)) continue;
                double d = Math.Abs(Math.Log(entry.Width / entry.Height / (canvas.Width / canvas.Height)));
                if (d < distance) { distance = d; best = stretched; }
            }
            return best != null ? Normalize(best, settings, canvas) : DefaultFor(settings, canvas);
        }
        public static WidgetPlacement[] DefaultFor(AppSettings settings, Size canvas)
        {
            canvas = Key(canvas);
            if (canvas == Reference) return Normalize(LegacyColumns(settings) ? null : SpotifyDefault(settings), settings);
            return Normalize(Arrange(canvas), settings, canvas);
        }
        // Scales positions and sizes from one canvas to another, rounded to whole units.
        public static WidgetPlacement[] Stretch(WidgetPlacement[] layout, Size from, Size to)
        {
            double fx = to.Width / from.Width, fy = to.Height / from.Height;
            return layout.Where(p => p != null).Select(p => {
                var q = p.Copy();
                double left = Math.Round(p.X * fx), top = Math.Round(p.Y * fy);
                q.X = left; q.Y = top; q.Width = Math.Round((p.X + p.Width) * fx) - left; q.Height = Math.Round((p.Y + p.Height) * fy) - top;
                return q;
            }).ToArray();
        }
        // Whether every shown widget keeps its minimum size on the canvas.
        public static bool Fits(WidgetPlacement[] layout, Size canvas)
        {
            foreach (var p in layout.Where(p => p != null && p.Visible))
            {
                var minimum = Minimum(p.Id, canvas);
                if (p.Width < minimum.Width - 1 || p.Height < minimum.Height - 1) return false;
            }
            return true;
        }

        // ----- Default arrangements for any canvas -----

        // A template is a tree of rows and columns of widgets. Weights are the reference sizes, so the Columns
        // template on the reference canvas gives exactly the Spotify default arrangement.
        private sealed class Node { public string Id; public bool Across; public double Weight; public Node[] Children; }
        private static Node Leaf(string id, double weight) { return new Node { Id = id, Weight = weight }; }
        private static Node Row(double weight, params Node[] children) { return new Node { Across = true, Weight = weight, Children = children }; }
        private static Node Column(double weight, params Node[] children) { return new Node { Weight = weight, Children = children }; }
        private sealed class Template { public string Name; public double Aspect; public Node Root; }
        // Wide canvases get columns, squarer ones three columns or two rows of cards, upright ones rows.
        private static readonly Template[] Templates = {
            new Template { Name = "Columns", Aspect = 3.2, Root = Row(1, Leaf("Focus", 320), Column(472, Leaf("Media", 310), Leaf("Shortcuts", 250)), Leaf("Mixer", 456), Leaf("Spotify", 560)) },
            new Template { Name = "Three columns", Aspect = 2.0, Root = Row(1, Column(472, Leaf("Focus", 330), Leaf("Media", 310)), Column(456, Leaf("Mixer", 460), Leaf("Shortcuts", 250)), Leaf("Spotify", 560)) },
            new Template { Name = "Two rows", Aspect = 1.4, Root = Column(1, Row(330, Leaf("Focus", 320), Leaf("Media", 472), Leaf("Shortcuts", 472)), Row(560, Leaf("Mixer", 456), Leaf("Spotify", 560))) },
            new Template { Name = "Tall", Aspect = 0.65, Root = Column(1, Row(330, Leaf("Focus", 320), Leaf("Media", 472)), Leaf("Shortcuts", 250), Row(560, Leaf("Mixer", 456), Leaf("Spotify", 560))) },
            new Template { Name = "Narrow", Aspect = 0.4, Root = Column(1, Row(330, Leaf("Focus", 320), Leaf("Media", 472)), Leaf("Shortcuts", 250), Leaf("Mixer", 480), Leaf("Spotify", 560)) }
        };
        private static Size MinimumOf(Node node, Size canvas)
        {
            if (node.Id != null) return Minimum(node.Id, canvas);
            double along = Gap * (node.Children.Length - 1), across = 0;
            foreach (var child in node.Children)
            {
                Size m = MinimumOf(child, canvas);
                along += node.Across ? m.Width : m.Height; across = Math.Max(across, node.Across ? m.Height : m.Width);
            }
            return node.Across ? new Size(along, across) : new Size(across, along);
        }
        // Shares a length between children by weight; a child that would get less than its minimum gets its minimum.
        public static double[] Distribute(double length, double[] weights, double[] minimums)
        {
            int n = weights.Length; var sizes = new double[n]; var atMinimum = new bool[n];
            for (bool changed = true; changed;)
            {
                changed = false; double rest = length, weight = 0;
                for (int i = 0; i < n; i++) if (atMinimum[i]) rest -= minimums[i]; else weight += weights[i];
                for (int i = 0; i < n; i++)
                {
                    sizes[i] = atMinimum[i] ? minimums[i] : weight > 0 ? Math.Max(0, rest) * weights[i] / weight : 0;
                    if (!atMinimum[i] && sizes[i] < minimums[i]) { atMinimum[i] = true; changed = true; }
                }
            }
            return sizes;
        }
        private static void Lay(Node node, Rect area, Size canvas, Dictionary<string, Rect> result)
        {
            if (node.Id != null) { result[node.Id] = area; return; }
            int n = node.Children.Length;
            double start = node.Across ? area.X : area.Y, end = start + (node.Across ? area.Width : area.Height);
            var sizes = Distribute(end - start - Gap * (n - 1), node.Children.Select(c => c.Weight).ToArray(),
                node.Children.Select(c => { var m = MinimumOf(c, canvas); return node.Across ? m.Width : m.Height; }).ToArray());
            double position = start;
            for (int i = 0; i < n; i++)
            {
                // Edges round to whole units; the last child ends exactly at the area's edge unless it overflows.
                double from = Math.Round(position), to = Math.Round(position + sizes[i]);
                if (i == n - 1 && to <= end + 0.5) to = end;
                Lay(node.Children[i], node.Across ? new Rect(from, area.Y, to - from, area.Height) : new Rect(area.X, from, area.Width, to - from), canvas, result);
                position += sizes[i] + Gap;
            }
        }
        // The template that fits the canvas and is closest to its shape; if none fits, the one that overflows least.
        public static string TemplateFor(Size canvas) { return Choose(Key(canvas)).Name; }
        private static Template Choose(Size canvas)
        {
            double aspect = canvas.Width / canvas.Height, bestScore = Double.MaxValue;
            Template best = Templates[0];
            foreach (var template in Templates)
            {
                Size m = MinimumOf(template.Root, canvas);
                double overflow = Math.Max(0, m.Width - canvas.Width) + Math.Max(0, m.Height - canvas.Height);
                double score = overflow > 0 ? 1000 + overflow : Math.Abs(Math.Log(aspect / template.Aspect));
                if (score < bestScore) { bestScore = score; best = template; }
            }
            return best;
        }
        // The default widgets (Focus, Now Playing, Quick Launch, the mixer and Spotify) arranged for the canvas.
        // Hidden widgets wait where they are most likely wanted: System stats over Now Playing, Weather and the
        // soundboard over Quick Launch.
        public static WidgetPlacement[] Arrange(Size canvas)
        {
            var rects = new Dictionary<string, Rect>();
            Lay(Choose(canvas).Root, new Rect(0, 0, canvas.Width, canvas.Height), canvas, rects);
            rects["System"] = rects["Media"]; rects["Weather"] = rects["Shortcuts"]; rects["Soundboard"] = rects["Shortcuts"];
            return Ids.Select(id => new WidgetPlacement { Id = id, X = rects[id].X, Y = rects[id].Y, Width = rects[id].Width, Height = rects[id].Height,
                Visible = id != "System" && id != "Weather" && id != "Soundboard" }).ToArray();
        }

        private static double Snap(double value, bool enabled) { return enabled ? Math.Round(value / 8) * 8 : value; }
        public static Rect Transform(string id, Rect start, string corner, double dx, double dy, bool snap) { return Transform(id, start, corner, dx, dy, snap, Reference); }
        public static Rect Transform(string id, Rect start, string corner, double dx, double dy, bool snap, Size canvas)
        {
            if (Double.IsNaN(dx) || Double.IsInfinity(dx) || Double.IsNaN(dy) || Double.IsInfinity(dy)) return start;
            var minimum = Minimum(id, canvas);
            double width = canvas.Width, height = canvas.Height;
            double left = start.Left, right = start.Right, top = start.Top, bottom = start.Bottom;
            if (String.IsNullOrEmpty(corner))
                return new Rect(Math.Max(0, Math.Min(width - start.Width, Snap(left + dx, snap))), Math.Max(0, Math.Min(height - start.Height, Snap(top + dy, snap))), start.Width, start.Height);
            if (corner.Contains("W")) left = Math.Max(0, Math.Min(right - minimum.Width, Snap(left + dx, snap)));
            if (corner.Contains("E")) right = Math.Min(width, Math.Max(left + minimum.Width, Snap(right + dx, snap)));
            if (corner.Contains("N")) top = Math.Max(0, Math.Min(bottom - minimum.Height, Snap(top + dy, snap)));
            if (corner.Contains("S")) bottom = Math.Min(height, Math.Max(top + minimum.Height, Snap(bottom + dy, snap)));
            return new Rect(left, top, right - left, bottom - top);
        }
        public static void SetBounds(WidgetPlacement p, Rect bounds) { p.X = bounds.X; p.Y = bounds.Y; p.Width = bounds.Width; p.Height = bounds.Height; }
    }
}
