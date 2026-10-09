using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WidgetScreen
{
    public sealed class LayoutEditor
    {
        private readonly Canvas canvas;
        private readonly Dictionary<string, Grid> hosts = new Dictionary<string, Grid>();
        private readonly Dictionary<string, Border> cards = new Dictionary<string, Border>();
        private readonly Dictionary<string, Grid> chrome = new Dictionary<string, Grid>();
        private readonly Dictionary<string, Border> outlines = new Dictionary<string, Border>();
        private readonly List<WidgetPlacement[]> undo = new List<WidgetPlacement[]>(), redo = new List<WidgetPlacement[]>();
        private WidgetPlacement[] saved, current, gestureBefore;
        private AppSettings preferences;
        private string gestureId, gestureCorner;
        private Rect gestureBounds;
        private Point gesturePoint;
        private TouchDevice gestureTouch;
        private int gestureLayer;
        private bool endingGesture, gestureMoved;
        public bool Editing { get; private set; }
        public bool Snap { get; set; }
        public string SelectedId { get; private set; }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public bool GestureActive { get { return gestureId != null; } }
        public event Action Changed;

        public LayoutEditor(Window window)
        {
            var parent = (Grid)window.FindName("WidgetColumns");
            canvas = new Canvas { Width = WidgetLayout.Width, Height = WidgetLayout.Height, ClipToBounds = true, Background = Brushes.Transparent };
            window.RegisterName("WidgetCanvas", canvas);
            foreach (string id in WidgetLayout.Ids)
            {
                var card = (Border)window.FindName(id + "Card");
                ((Panel)card.Parent).Children.Remove(card);
                card.Margin = new Thickness(0); card.Width = card.Height = Double.NaN;
                card.VerticalAlignment = VerticalAlignment.Stretch; card.HorizontalAlignment = HorizontalAlignment.Stretch;
                card.ClipToBounds = true;
                cards[id] = card;
                var host = new Grid(); hosts[id] = host; host.Children.Add(card); canvas.Children.Add(host);
                var overlay = CreateChrome(id); chrome[id] = overlay; host.Children.Add(overlay);
            }
            parent.Children.Clear(); parent.ColumnDefinitions.Clear(); parent.RowDefinitions.Clear(); parent.Children.Add(canvas);
        }

        private Grid CreateChrome(string id)
        {
            var overlay = new Grid { Background = Brushes.Transparent, Visibility = Visibility.Collapsed, Cursor = Cursors.SizeAll, Focusable = true };
            AutomationProperties.SetName(overlay, "Move or resize " + WidgetLayout.Title(id));
            var outline = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
            outline.SetResourceReference(Border.BorderBrushProperty, "Muted"); outline.SetResourceReference(Border.CornerRadiusProperty, "CardRadius");
            outlines[id] = outline; overlay.Children.Add(outline);
            foreach (string corner in new[] { "NW", "NE", "SW", "SE" })
            {
                var hit = new Border { Width = 48, Height = 48, Background = Brushes.Transparent, Tag = corner,
                    HorizontalAlignment = corner.Contains("W") ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = corner.Contains("N") ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                    Cursor = corner == "NW" || corner == "SE" ? Cursors.SizeNWSE : Cursors.SizeNESW,
                    ToolTip = "Drag to resize " + WidgetLayout.Title(id) };
                var grip = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2) };
                grip.SetResourceReference(Border.BackgroundProperty, "Accent"); grip.SetResourceReference(Border.BorderBrushProperty, "Page");
                hit.Child = grip; overlay.Children.Add(hit);
                AutomationProperties.SetName(hit, "Resize " + WidgetLayout.Title(id) + " " + corner + " corner");
            }
            // Coordinates come from the fixed design canvas, so pointer motion remains correct inside the scaled Viewbox.
            overlay.PreviewMouseLeftButtonDown += (sender, e) => {
                if (!Editing || GestureActive || gestureTouch != null) return;
                StartGesture(id, FindCorner(e.OriginalSource), e.GetPosition(canvas));
                if (!overlay.CaptureMouse()) EndGesture(); e.Handled = true;
            };
            overlay.PreviewMouseMove += (sender, e) => {
                if (GestureActive && gestureTouch == null && gestureId == id) { ContinueGesture(e.GetPosition(canvas)); e.Handled = true; }
            };
            overlay.PreviewMouseLeftButtonUp += (sender, e) => {
                if (GestureActive && gestureTouch == null && gestureId == id) { ContinueGesture(e.GetPosition(canvas)); EndGesture(); e.Handled = true; }
            };
            overlay.LostMouseCapture += (sender, e) => { if (!endingGesture && gestureId == id && gestureTouch == null) EndGesture(); };
            overlay.PreviewTouchDown += (sender, e) => {
                e.Handled = true;
                if (!Editing || GestureActive) return;
                gestureTouch = e.TouchDevice;
                StartGesture(id, FindCorner(e.OriginalSource), e.GetTouchPoint(canvas).Position);
                if (!gestureTouch.Capture(overlay)) EndGesture();
            };
            overlay.PreviewTouchMove += (sender, e) => {
                e.Handled = true;
                if (GestureActive && gestureTouch == e.TouchDevice) ContinueGesture(e.GetTouchPoint(canvas).Position);
            };
            overlay.PreviewTouchUp += (sender, e) => {
                e.Handled = true;
                if (GestureActive && gestureTouch == e.TouchDevice) { ContinueGesture(e.GetTouchPoint(canvas).Position); EndGesture(); }
            };
            overlay.LostTouchCapture += (sender, e) => { if (!endingGesture && gestureTouch == e.TouchDevice) EndGesture(); };
            return overlay;
        }

        private static string FindCorner(object source)
        {
            var element = source as DependencyObject;
            while (element != null)
            {
                var border = element as Border;
                if (border != null && border.Tag is string) return (string)border.Tag;
                element = element is Visual ? VisualTreeHelper.GetParent(element) : null;
            }
            return "";
        }
        public void Apply(AppSettings settings)
        {
            if (Editing) return;
            preferences = settings; Snap = settings.SnapWidgets;
            current = WidgetLayout.Normalize(settings.WidgetLayout, settings); saved = WidgetLayout.Copy(current);
            Draw();
        }
        public void Begin()
        {
            if (Editing) return;
            Editing = true; saved = WidgetLayout.Copy(current); undo.Clear(); redo.Clear();
            Select(current.FirstOrDefault(p => IsVisible(p.Id)) == null ? "Clock" : current.First(p => IsVisible(p.Id)).Id);
            Draw();
        }
        public void Cancel()
        {
            EndGesture(); current = WidgetLayout.Copy(saved); Editing = false; Snap = preferences.SnapWidgets; undo.Clear(); redo.Clear(); Draw();
        }
        public WidgetPlacement[] Export() { return WidgetLayout.Copy(current); }
        public void Accept()
        {
            EndGesture(); saved = WidgetLayout.Copy(current); Editing = false; undo.Clear(); redo.Clear(); Draw();
        }
        public void Select(string id)
        {
            if (!WidgetLayout.Ids.Contains(id)) return;
            SelectedId = id; Draw();
        }
        public bool IsVisible(string id)
        {
            if (current == null) return true;
            var p = current.First(w => w.Id == id);
            bool hasStats = preferences.ShowCpu || preferences.ShowGpu || preferences.ShowRam || preferences.ShowNetwork || preferences.ShowUptime;
            return p.Visible && (id != "System" || hasStats);
        }
        public bool IsEnabled(string id) { return current.First(p => p.Id == id).Visible; }
        public Rect Bounds(string id) { return current.First(p => p.Id == id).Bounds; }
        public void Refresh() { Draw(); }
        public bool HasOverlaps()
        {
            var visible = current.Where(p => IsVisible(p.Id)).ToArray();
            for (int i = 0; i < visible.Length; i++)
                for (int j = i + 1; j < visible.Length; j++)
                {
                    Rect intersection = Rect.Intersect(visible[i].Bounds, visible[j].Bounds);
                    if (!intersection.IsEmpty && intersection.Width > 1 && intersection.Height > 1) return true;
                }
            return false;
        }
        private void Remember(WidgetPlacement[] before)
        {
            undo.Add(before); if (undo.Count > 50) undo.RemoveAt(0); redo.Clear();
        }
        public void SetVisible(string id, bool visible)
        {
            if (!Editing || GestureActive) return;
            var p = current.First(w => w.Id == id); if (p.Visible == visible) return;
            Remember(Export()); p.Visible = visible; Draw();
        }
        public void ArrangeSpotify()
        {
            if (!Editing || GestureActive) return;
            Remember(Export()); current = WidgetLayout.SpotifyDefault(preferences); Select("Spotify"); Draw();
        }
        public void Reset()
        {
            if (!Editing || GestureActive) return;
            Remember(Export()); current = WidgetLayout.SpotifyDefault(new AppSettings {
                ShowCpu = preferences.ShowCpu, ShowGpu = preferences.ShowGpu, ShowRam = preferences.ShowRam,
                ShowNetwork = preferences.ShowNetwork, ShowUptime = preferences.ShowUptime }); Draw();
        }
        public void Undo()
        {
            if (!Editing || !CanUndo || GestureActive) return;
            redo.Add(Export()); current = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1); Draw();
        }
        public void Redo()
        {
            if (!Editing || !CanRedo || GestureActive) return;
            undo.Add(Export()); current = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1); Draw();
        }
        public void TransformSelected(string corner, double dx, double dy, bool snap)
        {
            if (!Editing || GestureActive || SelectedId == null) return;
            var p = current.First(w => w.Id == SelectedId);
            Rect next = WidgetLayout.Transform(p.Id, p.Bounds, corner, dx, dy, snap);
            if (next == p.Bounds) return;
            Remember(Export()); p.Layer = current.Max(w => w.Layer) + 1; WidgetLayout.SetBounds(p, next); Draw();
        }
        private void StartGesture(string id, string corner, Point point)
        {
            gestureBefore = Export(); gestureId = id; gestureCorner = corner;
            gestureMoved = false;
            gestureBounds = Bounds(id); gesturePoint = point; Select(id); chrome[id].Focus();
            gestureLayer = current.Max(w => w.Layer) + 1;
        }
        private void ContinueGesture(Point point)
        {
            double dx = point.X - gesturePoint.X, dy = point.Y - gesturePoint.Y;
            // A tap selects a widget without snapping or changing its saved stacking order.
            if (!gestureMoved && Math.Abs(dx) < 2 && Math.Abs(dy) < 2) return;
            gestureMoved = true;
            var p = current.First(w => w.Id == gestureId);
            var next = WidgetLayout.Transform(p.Id, gestureBounds, gestureCorner, dx, dy, Snap);
            p.Layer = next == gestureBounds ? gestureBefore.First(w => w.Id == p.Id).Layer : gestureLayer;
            WidgetLayout.SetBounds(p, next);
            Draw();
        }
        private void EndGesture()
        {
            if (!GestureActive || endingGesture) return;
            endingGesture = true;
            string id = gestureId;
            if (Bounds(id) != gestureBounds) Remember(gestureBefore);
            gestureId = null;
            if (gestureTouch != null) gestureTouch.Capture(null); else chrome[id].ReleaseMouseCapture();
            gestureTouch = null; gestureBefore = null; endingGesture = false; Draw();
        }
        private void Draw()
        {
            if (current == null) return;
            foreach (var p in current)
            {
                var host = hosts[p.Id]; host.Width = p.Width; host.Height = p.Height;
                Canvas.SetLeft(host, p.X); Canvas.SetTop(host, p.Y);
                host.Visibility = IsVisible(p.Id) ? Visibility.Visible : Visibility.Collapsed;
                cards[p.Id].Visibility = host.Visibility;
                // Raise the selected widget while editing so all four handles remain reachable through overlaps.
                Panel.SetZIndex(host, Editing && p.Id == SelectedId ? Int32.MaxValue : p.Layer);
                cards[p.Id].IsHitTestVisible = !Editing;
                chrome[p.Id].Visibility = Editing ? Visibility.Visible : Visibility.Collapsed;
                outlines[p.Id].BorderThickness = new Thickness(p.Id == SelectedId ? 2 : 1);
                outlines[p.Id].SetResourceReference(Border.BorderBrushProperty, p.Id == SelectedId ? "Accent" : "Muted");
            }
            if (Changed != null) Changed();
        }
    }
}
