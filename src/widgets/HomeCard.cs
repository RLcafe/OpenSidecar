using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WidgetScreen
{
    // Smart home in three designs that follow the card's size (CardSizing.Home; docs/design/smart-home.md). Small lists
    // the devices as rows; Medium makes each a tile; Large adds a dimmer slider to lights that dim. A tap on a device
    // turns it on or off; a thermostat has − and + for its target. The phone's HomeWidget.java draws the same.
    public sealed class HomeCard : CardParts
    {
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private readonly Window window;
        private Rect bounds = Rect.Empty;
        private string built = "";
        private CardSizing.Design design;
        private List<HomeEntity> entities;
        private string problem;
        private bool connected, chosen;
        private int page;
        // A slider being dragged keeps its place until it's let go.
        private bool dragging;
        private double textScale = 1;

        public event Action<HomeEntity> Toggled;
        public event Action<HomeEntity, int> Dimmed;
        public event Action<HomeEntity, double> Heated;

        public HomeCard(Window window, Grid host, TextBlock status) { this.window = window; this.host = host; this.status = status; names = new PartNames(window); }
        public CardSizing.Design Current { get { return design; } }

        public void Fit(Rect card, double textScale) { bounds = card; this.textScale = textScale; built = ""; Render(); }
        public void Show(bool connected, bool chosen, List<HomeEntity> entities, string problem)
        {
            this.connected = connected; this.chosen = chosen; this.entities = entities; this.problem = problem;
            if (!dragging) Render();
        }

        private void Render()
        {
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + connected + "|" + chosen + "|" + problem + "|" + page + "|"
                + (entities == null ? "-" : String.Join(";", entities.Select(e => e.Id + e.State + e.Brightness + e.Target + e.Current)));
            if (key == built) return;
            built = key;
            host.Children.Clear(); names.Clear();
            design = CardSizing.Home(bounds.Width, bounds.Height);
            status.Text = problem != null && connected ? (entities == null ? "OFFLINE" : "NOT UPDATING") : "";
            status.ToolTip = problem;
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36);
            FrameworkElement view;
            if (!connected) view = Note("Connect Home Assistant in Settings → Home.");
            else if (!chosen) view = Note("Choose the lights, switches and thermostats to show in Settings → Home.");
            else if (entities == null) view = Note(problem ?? "Reading your devices…");
            else view = Devices(width, height);
            host.Children.Add(names.Add(view, "HomeView"));
        }
        private TextBlock Note(string text) { var note = Caption(text); note.TextWrapping = TextWrapping.Wrap; note.VerticalAlignment = VerticalAlignment.Center; return note; }

        private FrameworkElement Devices(double width, double height)
        {
            // Small: every device as a row, in a list that scrolls.
            if (design == CardSizing.Design.Small)
            {
                var list = new StackPanel();
                for (int i = 0; i < entities.Count; i++) list.Children.Add(Tile(entities[i], i, true, false));
                return new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            }
            int[] shape = CardSizing.HomeGrid(width, height, textScale, entities.Count);
            int pageSize = shape[0] * shape[1], pages = Math.Max(1, (entities.Count + pageSize - 1) / pageSize);
            page = Math.Max(0, Math.Min(pages - 1, page));
            var outer = new Grid();
            outer.ColumnDefinitions.Add(new ColumnDefinition());
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(shape[2] == 1 ? CardSizing.HomePager : 0) });
            var grid = new System.Windows.Controls.Primitives.UniformGrid { Columns = shape[0], Rows = shape[1] };
            // A tile tall enough puts a thermostat's - and + (or a dimmer) under its name; a shorter one stacks them beside it.
            bool under = height / shape[1] >= CardSizing.HomeTile(textScale);
            for (int i = page * pageSize; i < Math.Min(entities.Count, (page + 1) * pageSize); i++) grid.Children.Add(Tile(entities[i], i, false, under));
            outer.Children.Add(grid);
            if (shape[2] == 1)
            {
                var pager = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                var previous = new Button { Content = "\uE70E", Style = (Style)window.Resources["IconButton"], FontSize = 16, IsEnabled = page > 0, Margin = new Thickness(0, 0, 0, 4) }; AutomationProperties.SetName(previous, "Previous devices");
                previous.Click += (s, e) => { page--; built = ""; Render(); };
                var label = Caption((page + 1) + "/" + pages); label.TextAlignment = TextAlignment.Center; label.Margin = new Thickness(0, 0, 0, 4);
                var next = new Button { Content = "\uE70D", Style = (Style)window.Resources["IconButton"], FontSize = 16, IsEnabled = page < pages - 1 }; AutomationProperties.SetName(next, "Next devices");
                next.Click += (s, e) => { page++; built = ""; Render(); };
                pager.Children.Add(previous); pager.Children.Add(label); pager.Children.Add(next);
                Grid.SetColumn(pager, 1); outer.Children.Add(pager);
            }
            return outer;
        }

        // A device: the tap target is its glyph, name and state together. A row (Small) is one line; a tile puts the
        // name over the state. Thermostats add - and +, and dimmable lights on a Large card a slider.
        private FrameworkElement Tile(HomeEntity e, int index, bool row, bool under)
        {
            var panel = new DockPanel { Margin = new Thickness(4) };
            var toggle = names.Add(new Button { Padding = new Thickness(row ? 12 : 14, 8, 10, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = e.Available, MinHeight = 48 }, "HomeToggle" + index);
            toggle.SetResourceReference(Control.BackgroundProperty, e.On ? "Selected" : "Raised");
            toggle.SetResourceReference(Control.BorderBrushProperty, e.On ? "Accent" : "ControlBorder");
            toggle.BorderThickness = new Thickness(e.On ? 2 : 1);
            AutomationProperties.SetName(toggle, e.Name + ", " + HomeAssistantService.Describe(e) + ". Tap to turn " + (e.On ? "off" : "on") + ".");
            var content = new DockPanel();
            var glyph = new TextBlock { Text = e.Domain == "light" ? "\uE82F" : e.Domain == "climate" ? "\uE9CA" : "\uE7E8", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = row ? 18 : 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, row ? 10 : 12, 0) };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, e.On ? "Accent" : "Muted");
            DockPanel.SetDock(glyph, Dock.Left); content.Children.Add(glyph);
            var name = Text(e.Name, row ? "SmallSize" : "BodySize", "Text"); name.VerticalAlignment = VerticalAlignment.Center; name.ToolTip = e.Name;
            var state = names.Add(Text(HomeAssistantService.Describe(e), "SmallSize", e.On ? "Text" : "Muted"), "HomeState" + index); state.VerticalAlignment = VerticalAlignment.Center;
            if (row)
            {
                state.Margin = new Thickness(10, 0, 0, 0); DockPanel.SetDock(state, Dock.Right); content.Children.Add(state);
                content.Children.Add(name);
            }
            else
            {
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                name.Margin = new Thickness(0, 0, 0, 2); words.Children.Add(name); words.Children.Add(state);
                content.Children.Add(words);
            }
            toggle.Content = content;
            var entity = e; toggle.Click += (s, a) => { if (Toggled != null) Toggled(entity); };
            if (e.Domain == "climate" && e.Available && e.Target.HasValue && !row)
            {
                if (!under)
                {
                    var stack = new StackPanel { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 52 };
                    var up = Step(e, 1, index); up.Margin = new Thickness(0, 0, 0, 3); stack.Children.Add(up);
                    var down = Step(e, -1, index); down.Margin = new Thickness(0); stack.Children.Add(down);
                    DockPanel.SetDock(stack, Dock.Right); panel.Children.Add(stack);
                }
                else
                {
                    var controls = new UniformGrid2(); controls.Panel.Margin = new Thickness(0, 6, 0, 0);
                    controls.Add(Step(e, -1, index)); controls.Add(Step(e, 1, index));
                    DockPanel.SetDock(controls.Panel, Dock.Bottom); panel.Children.Add(controls.Panel);
                }
            }
            if (e.Domain == "light" && e.Dimmable && e.Available && design == CardSizing.Design.Large && under)
            {
                var slider = names.Add(new Slider { Minimum = 1, Maximum = 100, Value = e.Brightness ?? 1, Margin = new Thickness(0, 4, 0, 0) }, "HomeDimmer" + index);
                AutomationProperties.SetName(slider, e.Name + " brightness");
                TouchSlider.Attach(slider);
                slider.PreviewMouseDown += (s, a) => dragging = true; slider.PreviewTouchDown += (s, a) => dragging = true;
                Action done = () => { if (!dragging) return; dragging = false; if (Dimmed != null) Dimmed(entity, (int)Math.Round(slider.Value)); };
                slider.PreviewMouseUp += (s, a) => done(); slider.PreviewTouchUp += (s, a) => done();
                slider.LostMouseCapture += (s, a) => done();
                DockPanel.SetDock(slider, Dock.Bottom); panel.Children.Add(slider);
            }
            panel.Children.Add(toggle);
            return panel;
        }
        private Button Step(HomeEntity e, int direction, int index)
        {
            var button = names.Add(new Button { Content = direction < 0 ? "−" : "+", FontSize = 20, MinHeight = 48, Margin = new Thickness(direction < 0 ? 0 : 3, 0, direction < 0 ? 3 : 0, 0) }, (direction < 0 ? "HomeDown" : "HomeUp") + index);
            AutomationProperties.SetName(button, (direction < 0 ? "Lower " : "Raise ") + e.Name + " to " + (e.Target.Value + direction * e.Step).ToString("0.#", CultureInfo.InvariantCulture) + "°");
            button.IsEnabled = direction < 0 ? e.Target.Value > e.Min : e.Target.Value < e.Max;
            var entity = e; button.Click += (s, a) => { if (Heated != null) Heated(entity, entity.Target.Value + direction * entity.Step); };
            return button;
        }
        private sealed class UniformGrid2
        {
            public readonly Grid Panel = new Grid();
            public void Add(UIElement element) { Panel.ColumnDefinitions.Add(new ColumnDefinition()); Grid.SetColumn(element, Panel.Children.Count); Panel.Children.Add(element); }
        }
    }
}
