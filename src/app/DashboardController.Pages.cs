using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    // Layout pages (docs/design/any-screen-size.md): the header's page switcher, swiping the dashboard sideways, and
    // the page and layout requests from a paired phone, which mirrors the PC's pages and shows the PC's current page.
    public sealed partial class DashboardController
    {
        private Point? swipeStart;
        private int swipeStartedAt;
        private bool swipeIgnored;
        // A sideways swipe of at least this many design units, mostly horizontal and quick, switches pages.
        private const double SwipeDistance = 160;

        private void InitializePages()
        {
            Button("PreviousPageButton", () => ShowPage(WidgetLayout.CurrentPage(settings) - 1));
            Button("NextPageButton", () => ShowPage(WidgetLayout.CurrentPage(settings) + 1));
            var area = Control<Grid>("WidgetColumns");
            area.PreviewTouchDown += (sender, e) => {
                if (Editor.Editing) return;
                swipeIgnored = OverControl(e.OriginalSource as DependencyObject);
                swipeStart = e.GetTouchPoint(area).Position; swipeStartedAt = Environment.TickCount;
            };
            area.PreviewTouchUp += (sender, e) => {
                if (swipeStart == null) return;
                Point start = swipeStart.Value, end = e.GetTouchPoint(area).Position; swipeStart = null;
                if (swipeIgnored || Editor.Editing) return;
                double dx = end.X - start.X, dy = end.Y - start.Y;
                if (Math.Abs(dx) >= SwipeDistance && Math.Abs(dx) > 2 * Math.Abs(dy) && Environment.TickCount - swipeStartedAt < 800)
                    ShowPage(WidgetLayout.CurrentPage(settings) + (dx < 0 ? 1 : -1));
            };
        }

        // Sliders, lists and text boxes keep their own sideways drags.
        private static bool OverControl(DependencyObject element)
        {
            for (; element != null; element = element is Visual ? VisualTreeHelper.GetParent(element) : null)
                if (element is Slider || element is ScrollBar || element is ScrollViewer || element is TextBox || element is Thumb) return true;
            return false;
        }

        // Shows another page. Widgets on other pages stop polling because the editor reports them hidden.
        public bool ShowPage(int index)
        {
            if (Editor.Editing) return false;
            if (index < 0 || index >= WidgetLayout.PageCount(settings) || index == WidgetLayout.CurrentPage(settings)) return false;
            int previous = settings.CurrentPage;
            settings.CurrentPage = index;
            try { SettingsStore.Save(settingsPath, settings); }
            catch (Exception e) { settings.CurrentPage = previous; Notify("Could not switch pages: " + e.Message); return false; }
            PagesChanged();
            return true;
        }

        private void PagesChanged()
        {
            ApplyLayout(settings); UpdatePages(); UpdateLevelTimer();
            if (window.IsLoaded) { RefreshSystem(); RefreshAudio(); RefreshMedia(); RefreshWeather(); }
        }

        private void UpdatePages()
        {
            int count = WidgetLayout.PageCount(settings), current = WidgetLayout.CurrentPage(settings);
            Control<StackPanel>("PageSwitcher").Visibility = count > 1 && !Editor.Editing ? Visibility.Visible : Visibility.Collapsed;
            Text("PageNameText", WidgetLayout.Page(settings, current).Name);
            Text("PageCountText", (current + 1) + " of " + count);
            Control<Button>("PreviousPageButton").IsEnabled = current > 0;
            Control<Button>("NextPageButton").IsEnabled = current < count - 1;
        }

        // Changes the pages and saves them, or puts everything back and returns the problem.
        private string ChangePages(Action change)
        {
            var before = WidgetLayout.Pages(settings).Select(p => p.Copy()).ToList(); int current = settings.CurrentPage;
            try { change(); SettingsStore.Save(settingsPath, settings); }
            catch (Exception e)
            {
                WidgetLayout.SetPages(settings, before); settings.CurrentPage = current;
                return e is ArgumentException || e is InvalidOperationException ? e.Message : "The layout couldn't be saved: " + e.Message;
            }
            PagesChanged();
            return null;
        }

        // ----- The phone link -----

        private static object[] RemoteWidgets(IEnumerable<WidgetPlacement> layout)
        {
            return layout.Select(p => (object)D("id", p.Id, "x", p.X, "y", p.Y, "width", p.Width, "height", p.Height, "visible", p.Visible, "layer", p.Layer)).ToArray();
        }

        // The current page's saved layouts and the layout on the PC's own screen, which the phone stretches to its canvas.
        private Dictionary<string, object> RemoteLayout()
        {
            int current = WidgetLayout.CurrentPage(settings); var page = WidgetLayout.Page(settings, current);
            Size canvas = Editor.CanvasSize;
            return D("page", current, "pages", WidgetLayout.Pages(settings).Select(p => (object)p.Name).ToArray(), "snap", settings.SnapWidgets, "editing", Editor.Editing,
                "canvas", D("width", canvas.Width, "height", canvas.Height), "shown", RemoteWidgets(WidgetLayout.ForCanvas(settings, current, canvas)),
                "layouts", WidgetLayout.SavedLayouts(page).Select(c => (object)D("width", c.Width, "height", c.Height, "widgets", RemoteWidgets(c.Widgets))).ToArray());
        }

        // Changes whenever anything in the layout section does, so the phone only asks for it then (FNV-1a).
        private string RemoteLayoutVersion(Dictionary<string, object> layout)
        {
            uint hash = 2166136261;
            foreach (char c in new JavaScriptSerializer().Serialize(layout)) { hash ^= c; hash *= 16777619; }
            return hash.ToString("x8");
        }

        private static WidgetPlacement[] ParseWidgets(object value)
        {
            var list = value as object[] ?? (value is System.Collections.ArrayList ? ((System.Collections.ArrayList)value).ToArray() : null);
            if (list == null) throw new ArgumentException("The layout is missing its widgets.");
            return list.OfType<Dictionary<string, object>>().Select(w => new WidgetPlacement {
                Id = Str(w, "id"), X = Num(w, "x"), Y = Num(w, "y"), Width = Num(w, "width"), Height = Num(w, "height"),
                Visible = Flag(w, "visible"), Layer = Double.IsNaN(Num(w, "layer")) ? 0 : (int)Num(w, "layer") }).Where(p => WidgetLayout.Ids.Contains(p.Id)).ToArray();
        }

        private RemoteResponse RemotePages(string action, Dictionary<string, object> body)
        {
            if (Editor.Editing) throw new InvalidOperationException("The layout is being edited on the PC. Try again once it's saved.");
            int index = Double.IsNaN(Num(body, "index")) ? WidgetLayout.CurrentPage(settings) : (int)Num(body, "index");
            string expected = Str(body, "page");
            if (expected != null && (index < 0 || index >= WidgetLayout.PageCount(settings) || WidgetLayout.Page(settings, index).Name != expected))
                throw new InvalidOperationException("The pages changed on the PC. Try again.");
            string problem = null, done = null;
            switch (action)
            {
                case "page.show":
                    if (index < 0 || index >= WidgetLayout.PageCount(settings)) throw new InvalidOperationException("That page isn't on the PC anymore.");
                    if (index != WidgetLayout.CurrentPage(settings) && !ShowPage(index)) throw new InvalidOperationException("The PC couldn't switch pages.");
                    return Done(null);
                case "layout.save":
                    {
                        double width = Num(body, "width"), height = Num(body, "height");
                        if (!(width >= 64 && width <= 100000 && height >= 64 && height <= 100000)) throw new ArgumentException("The layout's screen size is missing.");
                        var widgets = ParseWidgets(body.ContainsKey("widgets") ? body["widgets"] : null);
                        var canvas = new Size(width, height);
                        problem = ChangePages(() => WidgetLayout.Store(settings, index, canvas, WidgetLayout.Normalize(widgets, settings, canvas)));
                        done = "Layout saved on the PC.";
                        break;
                    }
                case "page.add":
                    problem = ChangePages(() => { if (WidgetLayout.AddPage(settings, Str(body, "name")) < 0) throw new InvalidOperationException("A dashboard can have up to " + WidgetLayout.MaxPages + " pages."); });
                    done = "Page added.";
                    break;
                case "page.rename": problem = ChangePages(() => WidgetLayout.RenamePage(settings, index, Str(body, "name"))); break;
                case "page.delete": problem = ChangePages(() => { if (!WidgetLayout.DeletePage(settings, index)) throw new InvalidOperationException("The only page can't be deleted."); }); done = "Page deleted."; break;
                case "page.move": problem = ChangePages(() => WidgetLayout.MovePage(settings, index, (int)Num(body, "to"))); break;
            }
            if (problem != null) throw new InvalidOperationException(problem);
            return Done(done);
        }
    }
}
