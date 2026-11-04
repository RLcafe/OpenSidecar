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

    // One page of the dashboard: its name and its layouts per canvas size (see WidgetLayout.Pages).
    public sealed class LayoutPage
    {
        public string Name { get; set; }
        public WidgetPlacement[] WidgetLayout { get; set; }
        public CanvasLayout[] CanvasLayouts { get; set; }
        public LayoutPage Copy()
        {
            return new LayoutPage { Name = Name, WidgetLayout = WidgetLayout == null ? null : WidgetScreen.WidgetLayout.Copy(WidgetLayout),
                CanvasLayouts = CanvasLayouts == null ? null : CanvasLayouts.Where(c => c != null).Select(c => c.Copy()).ToArray() };
        }
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
        public static readonly string[] Ids = { "Focus", "Media", "Shortcuts", "Mixer", "System", "Weather", "Spotify", "Soundboard", "Storage", "Clocks", "Prayer", "Notes", "Claude", "Clipboard", "Macros", "Calendar", "Games", "Home" };
        public static string Title(string id)
        {
            switch (id) {
                case "Focus": return "Focus timer";
                case "Media": return "Now playing";
                case "Shortcuts": return "Quick launch";
                case "Mixer": return "Sound mixer";
                case "System": return "System stats";
                case "Storage": return "Storage";
                case "Clocks": return "World clocks";
                case "Prayer": return "Prayer times";
                case "Notes": return "Notes and to-do";
                case "Claude": return "Claude Code";
                case "Clipboard": return "Clipboard history";
                case "Macros": return "Macros";
                case "Calendar": return "Calendar";
                case "Games": return "Games";
                case "Home": return "Smart home";
                default: return id;
            }
        }
        // The smallest each card can be while it keeps its main reading and controls at full size (CardSizing folds
        // the rest away; docs/design/resizable-widgets.md). Preferred sizes stay where they were.
        public static Size Minimum(string id)
        {
            switch (id) {
                case "Focus": return new Size(280, 192);
                case "Media": return new Size(312, 200);
                case "Spotify": return new Size(360, 360);
                case "Shortcuts": return new Size(288, 204);
                case "Mixer": return new Size(320, 224);
                case "System": return new Size(260, 280);
                case "Soundboard": return new Size(300, 240);
                case "Storage": return new Size(240, 152);
                case "Clocks": return new Size(240, 152);
                case "Prayer": return new Size(240, 152);
                case "Notes": return new Size(260, 200);
                case "Claude": return new Size(300, 240);
                case "Clipboard": return new Size(240, 200);
                case "Macros": return new Size(288, 204);
                case "Calendar": return new Size(240, 152);
                case "Games": return new Size(288, 204);
                case "Home": return new Size(240, 152);
                default: return new Size(232, 152);
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
            // Quick Launch and the Soundboard need their full minimum for six 48-unit buttons (CardSizing.PageButtons).
            Size minimum = Minimum(id), k = KeepsSix(id) ? new Size(1, 1) : Compact(canvas);
            return new Size(Math.Min(canvas.Width, Math.Round(minimum.Width * k.Width)), Math.Min(canvas.Height, Math.Round(minimum.Height * k.Height)));
        }
        public static bool KeepsSix(string id) { return id == "Shortcuts" || id == "Soundboard" || id == "Macros" || id == "Games"; }
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
            bool stats = SystemStyles.Any(settings);
            return new[] {
                Place("Focus", focusX, 0, widths[0] - 16, ReferenceHeight),
                Place("Media", mediaX, 0, widths[1] - 16, settings.MediaHeight - 16),
                Place("Shortcuts", mediaX, settings.MediaHeight, widths[1] - 16, ReferenceHeight - settings.MediaHeight),
                Place("Mixer", mixerX, 0, widths[2] - 16, settings.MixerHeight),
                Place("System", infoX, 0, widths[3], settings.SystemHeight - 16),
                Place("Weather", infoX, stats ? settings.SystemHeight : 0, widths[3], stats ? ReferenceHeight - settings.SystemHeight : ReferenceHeight),
                new WidgetPlacement { Id = "Spotify", X = 376, Y = 0, Width = 600, Height = 576, Visible = false },
                // Starts hidden; turned on in Edit layout once Discord is set up.
                new WidgetPlacement { Id = "Soundboard", X = 336, Y = 326, Width = 472, Height = 250, Visible = false },
                // New widgets start hidden, so the default layout stays the same; they are turned on in Edit layout.
                new WidgetPlacement { Id = "Storage", X = 1384, Y = 326, Width = 472, Height = 250, Visible = false },
                new WidgetPlacement { Id = "Clocks", X = 1384, Y = 0, Width = 472, Height = 250, Visible = false },
                new WidgetPlacement { Id = "Prayer", X = 1384, Y = 326, Width = 472, Height = 250, Visible = false },
                new WidgetPlacement { Id = "Notes", X = 1384, Y = 0, Width = 472, Height = 310, Visible = false },
                new WidgetPlacement { Id = "Claude", X = 1384, Y = 0, Width = 472, Height = 310, Visible = false },
                new WidgetPlacement { Id = "Clipboard", X = 1384, Y = 0, Width = 472, Height = 310, Visible = false },
                new WidgetPlacement { Id = "Macros", X = 336, Y = 326, Width = 472, Height = 250, Visible = false },
                new WidgetPlacement { Id = "Calendar", X = 1384, Y = 0, Width = 472, Height = 310, Visible = false },
                new WidgetPlacement { Id = "Games", X = 336, Y = 326, Width = 472, Height = 250, Visible = false },
                new WidgetPlacement { Id = "Home", X = 1384, Y = 0, Width = 472, Height = 310, Visible = false }
            };
        }
        public static WidgetPlacement[] SpotifyDefault(AppSettings settings)
        {
            var result = Default(settings);
            foreach (var p in result)
            {
                p.Visible = DefaultShown.Contains(p.Id);
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
            return canvas == Reference ? Normalize(layout, settings) : Normalize(layout, Arrange(canvas, DefaultShown), canvas);
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

        // ----- Pages (docs/design/any-screen-size.md) -----

        // A dashboard has up to eight pages. The first page lives in AppSettings.PageName, WidgetLayout and
        // CanvasLayouts, where older versions read it; the others in AppSettings.MorePages.
        public const int MaxPages = 8;
        public static List<LayoutPage> Pages(AppSettings s)
        {
            var pages = new List<LayoutPage> { new LayoutPage { Name = s.PageName, WidgetLayout = s.WidgetLayout, CanvasLayouts = s.CanvasLayouts } };
            if (s.MorePages != null) pages.AddRange(s.MorePages.Where(p => p != null));
            return pages;
        }
        public static void SetPages(AppSettings s, List<LayoutPage> pages)
        {
            s.PageName = pages[0].Name; s.WidgetLayout = pages[0].WidgetLayout; s.CanvasLayouts = pages[0].CanvasLayouts;
            s.MorePages = pages.Count > 1 ? pages.Skip(1).ToArray() : null;
            s.CurrentPage = Math.Max(0, Math.Min(pages.Count - 1, s.CurrentPage));
        }
        public static int PageCount(AppSettings s) { return 1 + (s.MorePages == null ? 0 : s.MorePages.Count(p => p != null)); }
        public static int CurrentPage(AppSettings s) { return Math.Max(0, Math.Min(PageCount(s) - 1, s.CurrentPage)); }
        public static LayoutPage Page(AppSettings s, int index) { var pages = Pages(s); return pages[Math.Max(0, Math.Min(pages.Count - 1, index))]; }
        public static string PageName(string name, int index)
        {
            name = (name ?? "").Trim();
            if (name.Length > 24) name = name.Substring(0, 24).Trim();
            return name.Length == 0 ? (index == 0 ? "Main" : "Page " + (index + 1)) : name;
        }
        // Adds a page after the current one with the default arrangement and shows it. Returns its index, or -1 when full.
        public static int AddPage(AppSettings s, string name)
        {
            var pages = Pages(s);
            if (pages.Count >= MaxPages) return -1;
            int index = CurrentPage(s) + 1;
            pages.Insert(index, new LayoutPage { Name = PageName(name, pages.Count) });
            SetPages(s, pages); s.CurrentPage = index;
            return index;
        }
        public static void RenamePage(AppSettings s, int index, string name)
        {
            var pages = Pages(s);
            if (index < 0 || index >= pages.Count) throw new ArgumentException("That page doesn't exist.");
            pages[index].Name = PageName(name, index); SetPages(s, pages);
        }
        // The last page can't be deleted. The page before it is shown next.
        public static bool DeletePage(AppSettings s, int index)
        {
            var pages = Pages(s);
            if (pages.Count <= 1 || index < 0 || index >= pages.Count) return false;
            pages.RemoveAt(index);
            if (s.CurrentPage >= index && s.CurrentPage > 0) s.CurrentPage--;
            SetPages(s, pages);
            return true;
        }
        // Moves a page; the page shown stays the same page.
        public static void MovePage(AppSettings s, int from, int to)
        {
            var pages = Pages(s);
            if (from < 0 || from >= pages.Count) return;
            to = Math.Max(0, Math.Min(pages.Count - 1, to));
            if (from == to) return;
            var shown = pages[CurrentPage(s)];
            var page = pages[from]; pages.RemoveAt(from); pages.Insert(to, page);
            s.CurrentPage = pages.IndexOf(shown); SetPages(s, pages);
        }
        // Cleans one page's layouts: invalid canvas entries go, duplicates of a size keep the first.
        public static void NormalizePage(LayoutPage page, AppSettings s, int index)
        {
            page.Name = PageName(page.Name, index);
            if (page.CanvasLayouts != null)
            {
                var layouts = new List<CanvasLayout>();
                foreach (var entry in page.CanvasLayouts)
                {
                    if (entry == null || entry.Widgets == null || !(entry.Width >= 64 && entry.Width <= 100000) || !(entry.Height >= 64 && entry.Height <= 100000)) continue;
                    var key = Key(entry.Size);
                    if (key == Reference) { if (page.WidgetLayout == null) page.WidgetLayout = entry.Widgets; continue; }
                    if (layouts.Exists(c => c.Size == key)) continue;
                    layouts.Add(new CanvasLayout { Width = key.Width, Height = key.Height, Widgets = Normalize(entry.Widgets, s, key) });
                }
                page.CanvasLayouts = layouts.Count == 0 ? null : layouts.ToArray();
            }
            if (page.WidgetLayout != null) page.WidgetLayout = Normalize(page.WidgetLayout, s);
        }

        // ----- Layouts per canvas size -----

        // Settings from before free layouts (swapped sides or custom column sizes) keep their column arrangement as the
        // reference canvas default; everyone else gets the Spotify arrangement there.
        private static bool LegacyColumns(AppSettings s)
        {
            return s.SwapSideWidgets || s.ClockWidth != 1 || s.MediaWidth != 3 || s.MixerWidth != 3 || s.SystemWidth != 2
                || s.ClockHeight != 246 || s.MediaHeight != 326 || s.MixerHeight != 576 || s.SystemHeight != 326;
        }
        // Whether a widget is shown on any page, in any canvas shape's layout.
        public static bool ShownAnywhere(AppSettings settings, string id)
        {
            return Pages(settings).Any(page => SavedLayouts(page).Any(layout => layout.Widgets.Any(p => p != null && p.Id == id && p.Visible)));
        }
        public static IEnumerable<CanvasLayout> SavedLayouts(LayoutPage page)
        {
            if (page.WidgetLayout != null) yield return new CanvasLayout { Width = ReferenceWidth, Height = ReferenceHeight, Widgets = page.WidgetLayout };
            if (page.CanvasLayouts != null) foreach (var entry in page.CanvasLayouts) if (entry != null && entry.Widgets != null) yield return entry;
        }
        public static WidgetPlacement[] Saved(LayoutPage page, Size canvas)
        {
            Size key = Key(canvas);
            var entry = SavedLayouts(page).FirstOrDefault(c => c.Size == key);
            return entry == null ? null : entry.Widgets;
        }
        public static WidgetPlacement[] Saved(AppSettings settings, Size canvas) { return Saved(Page(settings, CurrentPage(settings)), canvas); }
        public static void Store(AppSettings settings, Size canvas, WidgetPlacement[] layout) { Store(settings, CurrentPage(settings), canvas, layout); }
        // Saves a layout for one canvas size of a page. Which widgets a page shows is the same on every screen, so the
        // page's layouts for other sizes take the new layout's shown and hidden widgets.
        public static void Store(AppSettings settings, int index, Size canvas, WidgetPlacement[] layout)
        {
            var pages = Pages(settings);
            if (index < 0 || index >= pages.Count) throw new ArgumentException("That page doesn't exist.");
            var page = pages[index]; Size key = Key(canvas);
            Func<WidgetPlacement[], WidgetPlacement[]> follow = other => other.Select(p => {
                var q = p.Copy(); var match = layout.FirstOrDefault(w => w != null && w.Id == p.Id);
                if (match != null) q.Visible = match.Visible;
                return q;
            }).ToArray();
            if (page.WidgetLayout != null) page.WidgetLayout = follow(page.WidgetLayout);
            var others = (page.CanvasLayouts ?? new CanvasLayout[0]).Where(c => c != null && c.Size != key && c.Widgets != null)
                .Select(c => new CanvasLayout { Width = c.Width, Height = c.Height, Widgets = follow(c.Widgets) }).ToList();
            if (key == Reference) page.WidgetLayout = Copy(layout);
            else others.Add(new CanvasLayout { Width = key.Width, Height = key.Height, Widgets = Copy(layout) });
            page.CanvasLayouts = others.Count == 0 ? null : others.ToArray();
            SetPages(settings, pages);
        }
        public static WidgetPlacement[] ForCanvas(AppSettings settings, Size canvas) { return ForCanvas(settings, CurrentPage(settings), canvas); }
        // The layout of a page for a canvas: the one saved for its size; otherwise the saved layout of the closest shape
        // that stretches to it without squeezing a shown widget under its minimum; otherwise the page's widgets arranged
        // for this canvas.
        public static WidgetPlacement[] ForCanvas(AppSettings settings, int index, Size canvas)
        {
            var page = Page(settings, index);
            canvas = Key(canvas);
            var saved = Saved(page, canvas);
            if (saved != null) return Normalize(saved, settings, canvas);
            WidgetPlacement[] best = null, nearest = null; double distance = Double.MaxValue, nearestDistance = Double.MaxValue;
            foreach (var entry in SavedLayouts(page))
            {
                var source = Normalize(entry.Widgets, settings, entry.Size);
                double d = Math.Abs(Math.Log(entry.Width / entry.Height / (canvas.Width / canvas.Height)));
                if (d < nearestDistance) { nearestDistance = d; nearest = source; }
                var stretched = Stretch(source, entry.Size, canvas);
                if (Fits(stretched, canvas) && d < distance) { distance = d; best = stretched; }
            }
            if (best != null) return Normalize(best, settings, canvas);
            if (nearest == null) return DefaultFor(settings, canvas);
            var shown = ReadingOrder(nearest);
            if (shown.SequenceEqual(DefaultShown) && canvas == Reference) return DefaultFor(settings, canvas);
            return Normalize(Arrange(canvas, shown), settings, canvas);
        }
        public static WidgetPlacement[] DefaultFor(AppSettings settings, Size canvas)
        {
            canvas = Key(canvas);
            if (canvas == Reference) return Normalize(LegacyColumns(settings) ? null : SpotifyDefault(settings), settings);
            return Normalize(Arrange(canvas, DefaultShown), settings, canvas);
        }
        // Shown widgets left to right, then top to bottom: the order of the phone's list and of new arrangements.
        public static string[] ReadingOrder(WidgetPlacement[] layout)
        {
            return layout.Where(p => p != null && p.Visible).OrderBy(p => p.X).ThenBy(p => p.Y).Select(p => p.Id).ToArray();
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

        // ----- Arrangements for any canvas -----

        public static readonly string[] DefaultShown = { "Focus", "Media", "Shortcuts", "Mixer", "Spotify" };
        // The size each card is happiest at, from the reference default; arrangements stay close to these.
        public static Size Preferred(string id)
        {
            switch (id) {
                case "Focus": return new Size(320, 400);
                case "Mixer": return new Size(456, 576);
                case "Spotify": return new Size(560, 576);
                case "Shortcuts": case "Macros": case "Games": case "Weather": case "Soundboard": case "Storage": case "Clocks": case "Prayer": return new Size(472, 250);
                default: return new Size(472, 310);
            }
        }
        // Arranges the shown widgets, in order, as columns of stacked cards or as rows of cards side by side, and picks
        // the split that fits the canvas and keeps every card closest to its preferred size (growing counts half as
        // much as shrinking). Hidden widgets wait in the top left corner at their minimum size.
        public static WidgetPlacement[] Arrange(Size canvas, IList<string> shown)
        {
            shown = shown.Where(id => Ids.Contains(id)).Distinct().ToList();
            Dictionary<string, Rect> best = null; double bestScore = Double.MaxValue;
            int n = shown.Count;
            for (int across = 0; across < 2 && n > 0; across++)
                for (int mask = 0; mask < 1 << Math.Max(0, n - 1); mask++)
                {
                    var groups = new List<List<string>> { new List<string>() };
                    for (int i = 0; i < n; i++) { groups[groups.Count - 1].Add(shown[i]); if (i < n - 1 && (mask & 1 << i) != 0) groups.Add(new List<string>()); }
                    double overflow;
                    var rects = LayGroups(groups, across == 1, canvas, out overflow);
                    double score = overflow > 0 ? 1000 + overflow : rects.Sum(r => Strain(r.Value.Width, Preferred(r.Key).Width) + Strain(r.Value.Height, Preferred(r.Key).Height));
                    if (score < bestScore - 1e-9) { bestScore = score; best = rects; }
                }
            return Ids.Select(id => {
                Rect r; var minimum = Minimum(id, canvas);
                bool visible = best != null && best.TryGetValue(id, out r);
                if (!visible) r = new Rect(0, 0, minimum.Width, minimum.Height);
                else r = best[id];
                return new WidgetPlacement { Id = id, X = r.X, Y = r.Y, Width = r.Width, Height = r.Height, Visible = visible };
            }).ToArray();
        }
        private static double Strain(double actual, double preferred) { double r = actual / preferred; return r >= 1 ? 0.5 * Math.Log(r) : -Math.Log(Math.Max(r, 1e-6)); }
        // Columns (across false): groups side by side, each group's cards stacked. Rows (across true): groups stacked,
        // each group's cards side by side.
        private static Dictionary<string, Rect> LayGroups(List<List<string>> groups, bool across, Size canvas, out double overflow)
        {
            Func<string, Size> min = id => Minimum(id, canvas);
            Func<Size, double> outer = s => across ? s.Height : s.Width, inner = s => across ? s.Width : s.Height;
            double outerLength = across ? canvas.Height : canvas.Width, innerLength = across ? canvas.Width : canvas.Height;
            var outerMin = groups.Select(g => g.Max(id => outer(min(id)))).ToArray();
            var outerWeight = groups.Select(g => g.Max(id => outer(Preferred(id)))).ToArray();
            overflow = Math.Max(0, outerMin.Sum() + Gap * (groups.Count - 1) - outerLength);
            foreach (var g in groups) overflow += Math.Max(0, g.Sum(id => inner(min(id))) + Gap * (g.Count - 1) - innerLength);
            var result = new Dictionary<string, Rect>();
            var outerEdges = Edges(0, outerLength, Distribute(outerLength - Gap * (groups.Count - 1), outerWeight, outerMin));
            for (int i = 0; i < groups.Count; i++)
            {
                var g = groups[i];
                var innerEdges = Edges(0, innerLength, Distribute(innerLength - Gap * (g.Count - 1), g.Select(id => inner(Preferred(id))).ToArray(), g.Select(id => inner(min(id))).ToArray()));
                for (int j = 0; j < g.Count; j++)
                {
                    double o = outerEdges[i].Item1, os = outerEdges[i].Item2 - o, n = innerEdges[j].Item1, ns = innerEdges[j].Item2 - n;
                    result[g[j]] = across ? new Rect(n, o, ns, os) : new Rect(o, n, os, ns);
                }
            }
            return result;
        }
        // Whole-unit edges for sizes laid one after another with gaps; the last ends at the end unless it overflows.
        private static Tuple<double, double>[] Edges(double start, double end, double[] sizes)
        {
            var edges = new Tuple<double, double>[sizes.Length]; double position = start;
            for (int i = 0; i < sizes.Length; i++)
            {
                double from = Math.Round(position), to = Math.Round(position + sizes[i]);
                if (i == sizes.Length - 1 && to <= end + 0.5) to = end;
                edges[i] = Tuple.Create(from, to); position += sizes[i] + Gap;
            }
            return edges;
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

        // Shown widgets in the order Arrange keeps them (docs/design/organize-widgets.md): on a wide canvas column by
        // column, left to right and each column top to bottom; on an upright one row by row. A widget whose centre falls
        // inside the span of the first widget of the current column (or row) joins it.
        public static string[] ArrangeOrder(IEnumerable<WidgetPlacement> shown, Size canvas)
        {
            bool rows = canvas.Height > canvas.Width;
            Func<Rect, double> low = r => rows ? r.Top : r.Left, high = r => rows ? r.Bottom : r.Right, along = r => rows ? r.Left : r.Top;
            var groups = new List<List<WidgetPlacement>>(); double start = 0, end = 0;
            foreach (var p in shown.Where(p => p != null).OrderBy(p => low(p.Bounds) + high(p.Bounds)))
            {
                double middle = (low(p.Bounds) + high(p.Bounds)) / 2;
                if (groups.Count > 0 && middle >= start && middle <= end) groups[groups.Count - 1].Add(p);
                else { groups.Add(new List<WidgetPlacement> { p }); start = low(p.Bounds); end = high(p.Bounds); }
            }
            return groups.SelectMany(g => g.OrderBy(p => along(p.Bounds))).Select(p => p.Id).ToArray();
        }
        // The layout with its shown widgets arranged neatly in their current order (Arrange). Hidden widgets keep their
        // places, and layers reset because nothing overlaps any more. Widgets too many to fit at their minimums keep
        // them and stay on the canvas, overlapping.
        public static WidgetPlacement[] Tidy(WidgetPlacement[] layout, IList<string> shown, Size canvas)
        {
            var arranged = Arrange(canvas, shown);
            return layout.Select(p => {
                var q = p.Copy();
                if (!shown.Contains(p.Id)) return q;
                var r = arranged.First(a => a.Id == p.Id).Bounds;
                q.Width = Math.Min(canvas.Width, r.Width); q.Height = Math.Min(canvas.Height, r.Height);
                q.X = Math.Max(0, Math.Min(canvas.Width - q.Width, r.X)); q.Y = Math.Max(0, Math.Min(canvas.Height - q.Height, r.Y));
                q.Layer = 0;
                return q;
            }).ToArray();
        }

        // ----- Snapping to other widgets (docs/design/organize-widgets.md) -----

        // How close, in canvas units, an edge has to come to a target to snap to it.
        public const double AlignDistance = 12;
        // A guide line drawn while an edge sits on a target: vertical lines at X = At from Y = From to To, horizontal
        // lines at Y = At from X = From to To.
        public struct Guide
        {
            public bool Vertical; public double At, From, To;
            public Guide(bool vertical, double at, double from, double to) { Vertical = vertical; At = at; From = from; To = to; }
        }
        // Where an edge can snap along one axis: a leading edge (left or top) to the canvas edge, another widget's leading
        // edge or one gap after its trailing edge; a trailing edge likewise; a centre to another widget's centre.
        private static IEnumerable<double> Targets(int kind, IEnumerable<Tuple<double, double>> others, double length)
        {
            if (kind == 0) yield return 0;
            if (kind == 1) yield return length;
            foreach (var o in others)
            {
                if (kind == 0) { yield return o.Item1; yield return o.Item2 + Gap; }
                else if (kind == 1) { yield return o.Item2; yield return o.Item1 - Gap; }
                else yield return (o.Item1 + o.Item2) / 2;
            }
        }
        // The shift that puts the nearest of the given edges (kinds 0 leading, 1 trailing, 2 centre) on a target within
        // AlignDistance, or null.
        private static double? Shift(double[] edges, int[] kinds, IEnumerable<Tuple<double, double>> others, double length)
        {
            double? best = null;
            for (int i = 0; i < edges.Length; i++)
                foreach (double target in Targets(kinds[i], others, length))
                {
                    double shift = target - edges[i];
                    if (Math.Abs(shift) <= AlignDistance && (best == null || Math.Abs(shift) < Math.Abs(best.Value))) best = shift;
                }
            return best;
        }
        // Transform with snapping on: the edges that move snap to other shown widgets (lined up with an edge, one gap
        // beside it, or centred on it, for moves) and to the canvas edges when they come within AlignDistance; an axis
        // that finds no target follows the 8-unit grid. Guides lists the lines to draw for the result.
        public static Rect Align(string id, Rect start, string corner, double dx, double dy, IList<Rect> others, Size canvas, List<Guide> guides)
        {
            guides.Clear();
            Rect free = Transform(id, start, corner, dx, dy, false, canvas), grid = Transform(id, start, corner, dx, dy, true, canvas);
            if (free == start) return start;
            var xs = others.Select(o => Tuple.Create(o.Left, o.Right)).ToArray(); var ys = others.Select(o => Tuple.Create(o.Top, o.Bottom)).ToArray();
            Rect result;
            if (String.IsNullOrEmpty(corner))
            {
                double? sx = Shift(new[] { free.Left, free.Right, free.Left + free.Width / 2 }, new[] { 0, 1, 2 }, xs, canvas.Width);
                double? sy = Shift(new[] { free.Top, free.Bottom, free.Top + free.Height / 2 }, new[] { 0, 1, 2 }, ys, canvas.Height);
                double x = sx == null ? grid.X : free.X + sx.Value, y = sy == null ? grid.Y : free.Y + sy.Value;
                result = new Rect(Math.Max(0, Math.Min(canvas.Width - free.Width, x)), Math.Max(0, Math.Min(canvas.Height - free.Height, y)), free.Width, free.Height);
            }
            else
            {
                var minimum = Minimum(id, canvas);
                double left = grid.Left, right = grid.Right, top = grid.Top, bottom = grid.Bottom; double? s;
                if (corner.Contains("W") && (s = Shift(new[] { free.Left }, new[] { 0 }, xs, canvas.Width)) != null) left = free.Left + s.Value;
                if (corner.Contains("E") && (s = Shift(new[] { free.Right }, new[] { 1 }, xs, canvas.Width)) != null) right = free.Right + s.Value;
                if (corner.Contains("N") && (s = Shift(new[] { free.Top }, new[] { 0 }, ys, canvas.Height)) != null) top = free.Top + s.Value;
                if (corner.Contains("S") && (s = Shift(new[] { free.Bottom }, new[] { 1 }, ys, canvas.Height)) != null) bottom = free.Bottom + s.Value;
                if (corner.Contains("W")) left = Math.Max(0, Math.Min(right - minimum.Width, left));
                if (corner.Contains("E")) right = Math.Min(canvas.Width, Math.Max(left + minimum.Width, right));
                if (corner.Contains("N")) top = Math.Max(0, Math.Min(bottom - minimum.Height, top));
                if (corner.Contains("S")) bottom = Math.Min(canvas.Height, Math.Max(top + minimum.Height, bottom));
                result = new Rect(left, top, right - left, bottom - top);
            }
            bool move = String.IsNullOrEmpty(corner);
            Func<double, double, bool> on = (a, b) => Math.Abs(a - b) < 0.5;
            foreach (var o in others)
            {
                double fromY = Math.Min(o.Top, result.Top), toY = Math.Max(o.Bottom, result.Bottom), fromX = Math.Min(o.Left, result.Left), toX = Math.Max(o.Right, result.Right);
                if (move || corner.Contains("W"))
                {
                    if (on(result.Left, o.Left) || on(result.Left, o.Right)) AddGuide(guides, new Guide(true, result.Left, fromY, toY));
                    if (on(result.Left, o.Right + Gap)) AddGuide(guides, new Guide(true, o.Right + Gap / 2, fromY, toY));
                }
                if (move || corner.Contains("E"))
                {
                    if (on(result.Right, o.Right) || on(result.Right, o.Left)) AddGuide(guides, new Guide(true, result.Right, fromY, toY));
                    if (on(result.Right, o.Left - Gap)) AddGuide(guides, new Guide(true, o.Left - Gap / 2, fromY, toY));
                }
                if (move || corner.Contains("N"))
                {
                    if (on(result.Top, o.Top) || on(result.Top, o.Bottom)) AddGuide(guides, new Guide(false, result.Top, fromX, toX));
                    if (on(result.Top, o.Bottom + Gap)) AddGuide(guides, new Guide(false, o.Bottom + Gap / 2, fromX, toX));
                }
                if (move || corner.Contains("S"))
                {
                    if (on(result.Bottom, o.Bottom) || on(result.Bottom, o.Top)) AddGuide(guides, new Guide(false, result.Bottom, fromX, toX));
                    if (on(result.Bottom, o.Top - Gap)) AddGuide(guides, new Guide(false, o.Top - Gap / 2, fromX, toX));
                }
                if (move && on(result.Left + result.Width / 2, o.Left + o.Width / 2)) AddGuide(guides, new Guide(true, result.Left + result.Width / 2, fromY, toY));
                if (move && on(result.Top + result.Height / 2, o.Top + o.Height / 2)) AddGuide(guides, new Guide(false, result.Top + result.Height / 2, fromX, toX));
            }
            return result;
        }
        // One line per position: guides that share a line merge their spans.
        private static void AddGuide(List<Guide> guides, Guide guide)
        {
            int i = guides.FindIndex(g => g.Vertical == guide.Vertical && Math.Abs(g.At - guide.At) < 0.5);
            if (i < 0) { guides.Add(guide); return; }
            guides[i] = new Guide(guide.Vertical, guides[i].At, Math.Min(guides[i].From, guide.From), Math.Max(guides[i].To, guide.To));
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
