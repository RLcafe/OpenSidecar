using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WidgetScreen;

// Organizing widgets (docs/design/organize-widgets.md): snapping to other widgets' edges with alignment guides, and
// Arrange, which lines the shown widgets up in their current order.
internal static class OrganizeTests
{
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static bool Overlaps(IEnumerable<WidgetPlacement> layout)
    {
        var shown = layout.ToArray();
        for (int i = 0; i < shown.Length; i++)
            for (int j = i + 1; j < shown.Length; j++)
            {
                Rect overlap = Rect.Intersect(shown[i].Bounds, shown[j].Bounds);
                if (!overlap.IsEmpty && overlap.Width > 1 && overlap.Height > 1) return true;
            }
        return false;
    }
    private static string Describe(IEnumerable<WidgetScreen.WidgetLayout.Guide> guides) { return String.Join("; ", guides.Select(g => (g.Vertical ? "x " : "y ") + g.At + " " + g.From + "-" + g.To)); }

    public static void Run(string directory, Action<string, Action> check)
    {
        var canvas = WidgetLayout.Reference;
        check("Moving snaps to another widget's gap and edges, with guides", () => {
            var guides = new List<WidgetLayout.Guide>();
            var other = new Rect(705, 0, 400, 300);
            var moved = WidgetLayout.Align("Focus", new Rect(0, 0, 300, 300), "", 385, 3, new[] { other }, canvas, guides);
            Assert(moved == new Rect(389, 0, 300, 300), "Move did not sit one gap from the neighbour: " + moved);
            Assert(guides.Any(g => g.Vertical && g.At == 697 && g.From == 0 && g.To == 300), "No gap guide: " + Describe(guides));
            Assert(guides.Any(g => !g.Vertical && g.At == 0) && guides.Any(g => !g.Vertical && g.At == 300), "No top or bottom guide: " + Describe(guides));
            var far = WidgetLayout.Align("Focus", new Rect(0, 0, 300, 300), "", 357, 3, new[] { other }, canvas, guides);
            Assert(far.X == 360 && !guides.Any(g => g.Vertical), "Out of reach, a move must follow the grid: " + far + " " + Describe(guides));
            var centred = WidgetLayout.Align("Focus", new Rect(0, 400, 300, 176), "", 645, 0, new[] { new Rect(600, 0, 400, 300) }, canvas, guides);
            Assert(centred.X == 650 && guides.Any(g => g.Vertical && g.At == 800), "Centres did not line up: " + centred + " " + Describe(guides));
            var still = WidgetLayout.Align("Focus", new Rect(0, 0, 300, 300), "", 0, 0, new[] { other }, canvas, guides);
            Assert(still == new Rect(0, 0, 300, 300) && guides.Count == 0, "A drag that goes nowhere must not move or show guides.");
        });
        check("Resizing snaps the moving edges and keeps minimums", () => {
            var guides = new List<WidgetLayout.Guide>();
            var resized = WidgetLayout.Align("Focus", new Rect(0, 0, 300, 300), "SE", 95, 30, new[] { new Rect(0, 341, 400, 200) }, canvas, guides);
            Assert(resized == new Rect(0, 0, 400, 325), "Resize did not snap to the edge and the gap: " + resized);
            Assert(guides.Any(g => g.Vertical && g.At == 400) && guides.Any(g => !g.Vertical && g.At == 333), "Resize guides: " + Describe(guides));
            var squeezed = WidgetLayout.Align("Focus", new Rect(0, 0, 300, 300), "SE", -30, 0, new[] { new Rect(0, 420, 270, 100) }, canvas, guides);
            Assert(squeezed.Width == WidgetLayout.Minimum("Focus").Width, "Snapping must never squeeze under the minimum: " + squeezed);
            var edge = WidgetLayout.Align("Focus", new Rect(1500, 0, 300, 300), "NE", 50, 0, new Rect[0], canvas, guides);
            Assert(edge.Right == canvas.Width && edge.Left == 1500 && guides.Count == 0, "Resize must snap to the canvas edge without a guide: " + edge);
        });
        check("Edge grips resize one side only, snap that side and keep the smaller minimums", () => {
            var start = new Rect(400, 100, 400, 360);
            Assert(WidgetLayout.Transform("Media", start, "E", 57, 80, false, canvas) == new Rect(400, 100, 457, 360), "The right edge moved more than the right side.");
            Assert(WidgetLayout.Transform("Media", start, "N", 40, -60, false, canvas) == new Rect(400, 40, 400, 420), "The top edge moved more than the top.");
            Assert(WidgetLayout.Transform("Media", start, "W", 1000, 0, false, canvas) == new Rect(488, 100, 312, 360), "The left edge squeezed under the minimum width.");
            Assert(WidgetLayout.Transform("Media", start, "S", 0, -1000, false, canvas) == new Rect(400, 100, 400, 200), "The bottom edge squeezed under the minimum height.");
            var guides = new List<WidgetLayout.Guide>();
            var snapped = WidgetLayout.Align("Media", start, "S", 0, 30, new[] { new Rect(900, 0, 300, 486) }, canvas, guides);
            Assert(snapped == new Rect(400, 100, 400, 386) && guides.All(g => !g.Vertical), "The bottom edge did not snap alone: " + snapped);
            foreach (string id in WidgetLayout.Ids)
            {
                var minimum = WidgetLayout.Minimum(id);
                Assert(minimum.Width <= 360 && minimum.Height <= 360, id + " is still too large at its smallest: " + minimum);
                Assert(minimum.Width >= 200 && minimum.Height >= 152, id + " would drop under its touch-size floor: " + minimum);
            }
            Assert(CardSizing.FocusShort(299) && !CardSizing.FocusShort(300) && CardSizing.MediaShort(289) && !CardSizing.MediaShort(290), "Focus or Now Playing folds at the wrong height.");
            Assert(CardSizing.Mixer(420) == CardSizing.MixerMode.Full && CardSizing.Mixer(300) == CardSizing.MixerMode.Compact && CardSizing.Mixer(299) == CardSizing.MixerMode.Master, "Mixer arrangements.");
            Assert(CardSizing.EmptyDetail(478) == 3 && CardSizing.EmptyDetail(262) == 2 && CardSizing.EmptyDetail(120) == 1 && CardSizing.EmptyDetail(62) == 0, "Empty states.");
            Assert(CardSizing.FocusTime(320, 576) == 84 && CardSizing.MediaArt(552, 310) == 104 && CardSizing.FocusTime(280, 192) == 40 && CardSizing.MediaArt(312, 200) == 64, "Reading sizes changed: " + CardSizing.FocusTime(280, 192) + ", " + CardSizing.MediaArt(312, 200));
        });
        check("Arrange keeps the reading order by columns on wide canvases and rows on upright ones", () => {
            var layout = new[] {
                new WidgetPlacement { Id = "Focus", X = 0, Y = 0, Width = 320, Height = 576 },
                new WidgetPlacement { Id = "Shortcuts", X = 344, Y = 330, Width = 460, Height = 246 },
                new WidgetPlacement { Id = "Media", X = 336, Y = 0, Width = 472, Height = 310 },
                new WidgetPlacement { Id = "Mixer", X = 830, Y = 20, Width = 456, Height = 500 } };
            var order = WidgetLayout.ArrangeOrder(layout, canvas);
            Assert(order.SequenceEqual(new[] { "Focus", "Media", "Shortcuts", "Mixer" }), "Column order: " + String.Join(",", order));
            var upright = new[] {
                new WidgetPlacement { Id = "Mixer", X = 0, Y = 700, Width = 600, Height = 500 },
                new WidgetPlacement { Id = "Media", X = 340, Y = 10, Width = 300, Height = 320 },
                new WidgetPlacement { Id = "Focus", X = 0, Y = 0, Width = 320, Height = 320 } };
            order = WidgetLayout.ArrangeOrder(upright, new Size(656, 1712));
            Assert(order.SequenceEqual(new[] { "Focus", "Media", "Mixer" }), "Row order: " + String.Join(",", order));
        });
        check("Arrange tidies every screen shape without overlaps and keeps hidden widgets", () => {
            foreach (var size in new[] { canvas, new Size(2496, 576), new Size(1856, 936), new Size(1216, 680), new Size(656, 1712) })
                foreach (var shown in new[] { WidgetLayout.DefaultShown, new[] { "Mixer", "Focus", "Weather" }, new[] { "Focus", "Media", "Shortcuts", "Mixer", "System", "Weather" } })
                {
                    var start = WidgetLayout.Normalize(null, new AppSettings(), size);
                    var tidy = WidgetLayout.Tidy(start, shown, size);
                    var visible = tidy.Where(p => shown.Contains(p.Id)).ToArray();
                    Assert(!Overlaps(visible) && visible.All(p => p.X >= 0 && p.Y >= 0 && p.Bounds.Right <= size.Width && p.Bounds.Bottom <= size.Height), "Arrange overlaps or escapes on " + size + " with " + String.Join(",", shown));
                    Assert(tidy.Where(p => !shown.Contains(p.Id)).All(p => p.Bounds == start.First(s => s.Id == p.Id).Bounds), "Arrange moved a hidden widget on " + size);
                    Assert(tidy.All(p => p.Visible == start.First(s => s.Id == p.Id).Visible), "Arrange changed which widgets are shown.");
                }
            // Eight widgets don't fit side by side at their minimums; they may overlap, but stay on the canvas.
            var crowded = WidgetLayout.Tidy(WidgetLayout.Normalize(null, new AppSettings(), canvas), WidgetLayout.Ids, canvas);
            Assert(crowded.All(p => p.X >= 0 && p.Y >= 0 && p.Bounds.Right <= canvas.Width && p.Bounds.Bottom <= canvas.Height), "A crowded Arrange escaped the canvas.");
        });
        check("Editor drags show guides while held, Arrange is one undo step", () => {
            string path = Path.Combine(directory, "organize-editor.xml"); SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    Action<string> click = name => ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    var editor = controller.Editor; var shortcuts = editor.Bounds("Shortcuts");
                    click("EditLayoutButton");
                    Assert(editor.Snap, "Snap should be on by default.");
                    editor.Drag("Shortcuts", "", 5, -4, false);
                    Assert(editor.Bounds("Shortcuts") == shortcuts && editor.Guides.Count > 0, "Held drag did not snap back under Now playing with guides: " + editor.Bounds("Shortcuts"));
                    var lines = ((System.Windows.Controls.Canvas)window.FindName("WidgetCanvas")).Children.OfType<System.Windows.Controls.Border>().Count(b => b.Visibility == Visibility.Visible && !b.IsHitTestVisible);
                    Assert(lines == editor.Guides.Count, "Guide lines drawn: " + lines + " of " + editor.Guides.Count);
                    editor.Drag("Shortcuts", "", 5, -4, true);
                    Assert(editor.Guides.Count == 0 && !editor.CanUndo, "Letting go must clear guides, and a drag back to the start is no undo step.");
                    editor.Drag("Media", "", 500, 140, true); editor.Drag("Focus", "SE", 200, -100, true);
                    Assert(editor.HasOverlaps(), "The messy layout should overlap before Arrange.");
                    var before = editor.Export(); int steps = 0;
                    click("ArrangeLayoutButton");
                    Assert(!editor.HasOverlaps() && WidgetLayout.Ids.All(id => editor.IsVisible(id) == before.First(p => p.Id == id).Visible), "Arrange left overlaps or changed the shown widgets.");
                    Assert(!editor.Arrange(), "A second Arrange should find nothing to do.");
                    while (editor.CanUndo) { editor.Undo(); steps++; }
                    editor.Redo(); editor.Redo(); editor.Redo();
                    Assert(steps == 3 && !editor.HasOverlaps(), "Arrange should be one undo step after the two drags: " + steps);
                    var arranged = editor.Bounds("Shortcuts"); editor.Snap = false; editor.Drag("Shortcuts", "", 5, -4, false);
                    Assert(editor.Guides.Count == 0 && editor.Bounds("Shortcuts").X == Math.Min(arranged.X + 5, canvas.Width - arranged.Width), "Snap off must move freely without guides: " + editor.Bounds("Shortcuts"));
                    editor.Drag("Shortcuts", "", 5, -4, true);
                    click("CancelLayoutButton");
                }
                window.Close();
            }
        });
    }
}
