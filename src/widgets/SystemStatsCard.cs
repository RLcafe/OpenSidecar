using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;

namespace WidgetScreen
{
    // System stats in three designs that follow the card's size (CardSizing.Stats; docs/design/widget-size-views.md and
    // docs/design/system-sensors.md). Small lists the readings, in columns when there are many; Medium gives each reading
    // a cell drawn in its style (ring, gauge, line, bars or sparkline) over a network and uptime strip; Large gives each a
    // tile with its value and its style, and uptime moves to the heading. A design that can't fit its readings at a
    // readable size steps down to the next. The readings and their history live here, so a new design starts full. The
    // phone's SystemWidget.java draws the same three.
    public sealed class SystemStatsCard : CardParts
    {
        public const int HistoryLength = 48;
        private const double Gap = 12;
        private readonly Grid host;
        private readonly TextBlock headerUptime;
        private readonly PartNames names;
        private string[] shown = { "Cpu", "Gpu", "Ram", "Network", "Uptime" };
        private string styles = "";
        private bool fahrenheit;
        private double textScale = 1;
        private SystemSnapshot last;
        private readonly Dictionary<string, List<double>> history = new Dictionary<string, List<double>>();
        private readonly List<double> upHistory = new List<double>();
        private Rect bounds = Rect.Empty;
        private string built = "";
        private CardSizing.Design design;
        private readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();

        // What one reading is drawn with in the current design; any of these may be missing.
        private sealed class Part
        {
            public string Style;
            public TextBlock Value, Detail, Second;
            public RingGauge Ring;
            public ArcGauge Gauge;
            public MeterBar Bar;
            public HistoryGraph Graph;
            public HistoryBars Bars;
            // Sticker Sheet phase 3: a temperature badge on the CPU and GPU dials, and min and max on Large tiles.
            public Border Badge; public TextBlock BadgeText, Range;
        }

        public SystemStatsCard(FrameworkElement scope, Grid host, TextBlock headerUptime)
        {
            this.host = host; this.headerUptime = headerUptime; names = new PartNames(scope);
            foreach (string id in SystemStyles.Ids) history[id] = new List<double>();
        }

        public CardSizing.Design Current { get { return design; } }
        public string StyleOf(string id) { Part part; return parts.TryGetValue(id, out part) ? part.Style : null; }

        // The readings and styles chosen in Settings → Stats; temperatures follow Settings → Weather's units.
        public void Choose(AppSettings settings)
        {
            shown = SystemStyles.ShownIds(settings); styles = settings.StatStyles ?? ""; fahrenheit = settings.WeatherUnits == "imperial";
            built = "";
            if (!bounds.IsEmpty) Fit(bounds, textScale);
        }

        // The card's bounds on the canvas; the design is rebuilt only when its size, text size or readings change.
        public void Fit(Rect card, double textScale)
        {
            bounds = card; this.textScale = textScale;
            string key = Math.Round(card.Width) + "x" + Math.Round(card.Height) + "|" + textScale + "|" + String.Join(",", shown) + "|" + styles + "|" + fahrenheit;
            if (key == built) return;
            built = key; Build(); Render();
        }

        public void Update(SystemSnapshot snapshot)
        {
            last = snapshot;
            var readings = SystemReadings.From(snapshot, fahrenheit, SystemReadings.FpsScale(history["Fps"]));
            foreach (string id in SystemStyles.Ids) Push(history[id], readings[id].Value);
            Push(upHistory, readings["Network"].Second);
            Render();
        }
        public void Unavailable()
        {
            last = null;
            foreach (var list in history.Values) list.Clear();
            upHistory.Clear();
            Render();
        }
        private static void Push(List<double> list, double? value)
        {
            if (!value.HasValue) { list.Clear(); return; }
            list.Add(value.Value); if (list.Count > HistoryLength) list.RemoveAt(0);
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); parts.Clear();
            // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36);
            var visuals = shown.Where(id => id != "Network" && id != "Uptime").ToList();
            design = CardSizing.Stats(bounds.Width, bounds.Height);
            // Each design steps down when its readings wouldn't fit at a readable size.
            if (design == CardSizing.Design.Large && (!shown.Any(id => id != "Uptime") || CardSizing.StatsTileGrid(width, height, shown.Count(id => id != "Uptime"), textScale) == null)) design = CardSizing.Design.Medium;
            if (design == CardSizing.Design.Medium && (visuals.Count == 0 || CardSizing.StatsCellGrid(width, height - (shown.Contains("Network") || shown.Contains("Uptime") ? 56 + Gap : 0), visuals.Count, textScale) == null)) design = CardSizing.Design.Small;
            headerUptime.Visibility = design == CardSizing.Design.Large && shown.Contains("Uptime") ? Visibility.Visible : Visibility.Collapsed;
            FrameworkElement view;
            if (shown.Length == 0) view = Caption("No readings chosen. Choose them in Settings → Stats.");
            else if (design == CardSizing.Design.Small) view = BuildList(width, height);
            else if (design == CardSizing.Design.Medium) view = BuildCells(visuals, width, height);
            else view = BuildTiles(shown.Where(id => id != "Uptime").ToList(), width, height);
            host.Children.Add(names.Add(view, "SystemView"));
        }

        private T Named<T>(T element, string id, string role) where T : FrameworkElement
        {
            if (id == "Ram" && role == "Value") return names.Add(element, "MemoryPercent");
            if (id == "Network" && role == "Value") return names.Add(element, "DownloadValue");
            if (id == "Network" && role == "Second") return names.Add(element, "UploadValue");
            return names.Add(element, id + role);
        }
        private string Style(string id) { return SystemStyles.Resolve(SystemStyles.Get(styles, id), id, design); }

        // Small: one row per reading (the name at the left, the number at the right, a thin bar under dial readings), in
        // as many columns as it takes to keep rows readable.
        private FrameworkElement BuildList(double width, double height)
        {
            int[] grid = CardSizing.StatsListGrid(width, height, shown.Length, textScale);
            int columns = grid[0], rows = grid[1], fits = grid[2];
            double rowHeight = (height - 8 * (rows - 1)) / rows, size = CardSizing.StatsListValue(rowHeight);
            var list = new Grid();
            for (int c = 0; c < columns; c++) list.ColumnDefinitions.Add(new ColumnDefinition());
            for (int r = 0; r < rows; r++) list.RowDefinitions.Add(new RowDefinition());
            for (int i = 0; i < shown.Length; i++)
            {
                if (i >= fits)
                {
                    // A small card shows the readings in order until it's full; the last row counts the rest.
                    var more = Caption((shown.Length - fits) + " more: make the card bigger"); more.VerticalAlignment = VerticalAlignment.Center; more.TextWrapping = TextWrapping.Wrap;
                    more.ToolTip = String.Join(", ", shown.Skip(fits).Select(SystemStyles.Title)); more.Margin = new Thickness(i / rows == 0 ? 0 : 8, i % rows == 0 ? 0 : 8, 0, 0);
                    list.Children.Add(At(names.Add(more, "SystemMore"), i % rows, i / rows));
                    break;
                }
                string id = shown[i]; var part = parts[id] = new Part { Style = "List" };
                int column = i / rows, row = i % rows;
                var cell = Rows(Star, Auto); cell.Margin = new Thickness(column == 0 ? 0 : 8, row == 0 ? 0 : 8, column == columns - 1 ? 0 : 8, 0);
                var top = Columns(Auto, Star); top.Children.Add(Label(SystemStyles.Title(id)).Centered());
                if (id == "Network")
                {
                    double line = Math.Max(15, Math.Min(20, Math.Round(size * 0.55)));
                    var rates = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                    part.Value = Named(Tabular(Text("↓ —", line, "Text")), id, "Value"); part.Value.HorizontalAlignment = HorizontalAlignment.Right; rates.Children.Add(part.Value);
                    part.Second = Named(Tabular(Text("↑ —", line, "Text")), id, "Second"); part.Second.HorizontalAlignment = HorizontalAlignment.Right; rates.Children.Add(part.Second);
                    top.Children.Add(At(rates, 0, 1));
                }
                else
                {
                    part.Value = Named(Reading("—", id == "Uptime" ? Math.Round(size * 0.72) : size), id, "Value");
                    part.Value.HorizontalAlignment = HorizontalAlignment.Right; part.Value.VerticalAlignment = VerticalAlignment.Center; part.Value.Margin = new Thickness(12, 0, 0, 0);
                    part.Value.TextTrimming = TextTrimming.CharacterEllipsis;
                    top.Children.Add(At(part.Value, 0, 1));
                }
                cell.Children.Add(top);
                if (id != "Network" && id != "Uptime") { part.Bar = Bar(); part.Bar.Height = 4; part.Bar.Margin = new Thickness(0, 4, 0, 0); cell.Children.Add(At(part.Bar, 1)); }
                list.Children.Add(At(Named(cell, id, "Section"), row, column));
            }
            return list;
        }

        // Medium: a cell per reading in its style, with the network rates and uptime in a strip under them.
        private FrameworkElement BuildCells(List<string> visuals, double width, double height)
        {
            bool network = shown.Contains("Network"), uptime = shown.Contains("Uptime"), strip = network || uptime;
            var view = Rows(Star, Auto);
            double room = height - (strip ? 56 + Gap : 0);
            int[] grid = CardSizing.StatsCellGrid(width, room, visuals.Count, textScale);
            int columns = grid[0], rows = grid[1];
            double cellWidth = width / columns, cellHeight = (room - Gap * (rows - 1)) / rows;
            var cells = new UniformGrid { Rows = rows, Columns = columns };
            foreach (string id in visuals)
            {
                var part = parts[id] = new Part { Style = Style(id) };
                var cell = SystemStyles.IsDial(part.Style) ? DialCell(id, part, cellWidth, cellHeight) : GraphCell(id, part, cellHeight);
                cell.Margin = new Thickness(4, 0, 4, 0);
                cells.Children.Add(Named(cell, id, "Section"));
            }
            view.Children.Add(cells);
            if (strip)
            {
                var line = Columns(Star, Star, Auto);
                if (network)
                {
                    var part = parts["Network"] = new Part { Style = "Strip" };
                    part.Value = Named(Tabular(Text("↓ —", "BodySize", "Text")), "Network", "Value"); part.Value.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(part.Value);
                    part.Second = Named(Tabular(Text("↑ —", "BodySize", "Text")), "Network", "Second"); part.Second.VerticalAlignment = VerticalAlignment.Center; part.Second.Margin = new Thickness(12, 0, 0, 0);
                    line.Children.Add(At(part.Second, 0, 1));
                }
                if (uptime)
                {
                    var part = parts["Uptime"] = new Part { Style = "Strip" };
                    part.Value = Named(Text("—", "SmallSize", "Muted"), "Uptime", "Value"); part.Value.VerticalAlignment = VerticalAlignment.Center; part.Value.Margin = new Thickness(12, 0, 0, 0);
                    if (!network) { part.Value.Margin = new Thickness(0); line.Children.Add(At(part.Value, 0, 0, 3)); } else line.Children.Add(At(part.Value, 0, 2));
                }
                var tile = Tile(line, 14, new Thickness(16, 0, 16, 0)); tile.Height = 56; tile.Margin = new Thickness(0, Gap, 0, 0);
                view.Children.Add(At(Named(tile, network ? "Network" : "Uptime", "Section"), 1));
            }
            return view;
        }

        // A ring or gauge with the value inside, the name and a detail under it.
        private FrameworkElement DialCell(string id, Part part, double cellWidth, double cellHeight)
        {
            var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            double diameter = CardSizing.StatsRing(cellWidth, cellHeight - CardSizing.StatsDialText(textScale));
            column.Children.Add(Dial(id, part, diameter));
            var label = Label(SystemStyles.Title(id)); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 6, 0, 0); column.Children.Add(label);
            part.Detail = Named(Caption(" "), id, "Detail"); part.Detail.HorizontalAlignment = HorizontalAlignment.Center; part.Detail.MaxWidth = Math.Max(40, cellWidth - 8); part.Detail.Margin = new Thickness(0, 2, 0, 0);
            part.Detail.TextTrimming = TextTrimming.CharacterEllipsis;
            column.Children.Add(part.Detail);
            return column;
        }
        private Grid Dial(string id, Part part, double diameter)
        {
            var dial = new Grid { Width = diameter, Height = diameter, HorizontalAlignment = HorizontalAlignment.Center };
            double thickness = Math.Max(5, Math.Min(12, Math.Round(diameter * 0.075)));
            if (part.Style == "Gauge") { part.Gauge = new ArcGauge { Thickness = thickness }; part.Gauge.SetResourceReference(ArcGauge.TrackProperty, "Track"); dial.Children.Add(part.Gauge); }
            else { part.Ring = new RingGauge { Thickness = thickness }; part.Ring.SetResourceReference(RingGauge.TrackProperty, "Track"); dial.Children.Add(part.Ring); }
            part.Value = Named(Reading("—", Math.Round(diameter * (part.Style == "Gauge" ? 0.25 : 0.27))), id, "Value");
            part.Value.HorizontalAlignment = HorizontalAlignment.Center; part.Value.VerticalAlignment = VerticalAlignment.Center;
            dial.Children.Add(part.Value);
            // The CPU and GPU dials wear their temperature as a small badge at the top right (filled in the ink).
            if (id == "Cpu" || id == "Gpu")
            {
                part.BadgeText = Named(new TextBlock { FontSize = Math.Max(13, Math.Round(diameter * 0.1)), FontWeight = FontWeights.Bold }, id, "Badge");
                part.BadgeText.SetResourceReference(TextBlock.ForegroundProperty, "InkText");
                part.Badge = new Border { Child = part.BadgeText, CornerRadius = new CornerRadius(10), Padding = new Thickness(6, 1, 6, 1), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -10, 0), Visibility = Visibility.Collapsed };
                part.Badge.SetResourceReference(Border.BackgroundProperty, Stickers.InkKey("System")); part.Badge.SetResourceReference(Border.BorderBrushProperty, "Surface"); part.Badge.BorderThickness = new Thickness(2);
                dial.Children.Add(part.Badge);
            }
            return dial;
        }
        // The name and value on a line, the history under it (line, bars or sparkline), a detail at the foot.
        private FrameworkElement GraphCell(string id, Part part, double cellHeight)
        {
            var cell = Rows(Auto, Star, Auto);
            var top = Columns(Auto, Star); top.Children.Add(Label(SystemStyles.Title(id)).Centered());
            part.Value = Named(Reading("—", Math.Max(20, Math.Min(32, Math.Round(cellHeight * 0.22)))), id, "Value");
            part.Value.HorizontalAlignment = HorizontalAlignment.Right; part.Value.Margin = new Thickness(8, 0, 0, 0);
            top.Children.Add(At(part.Value, 0, 1));
            cell.Children.Add(top);
            var graph = Graph(id, part); graph.Margin = new Thickness(0, 6, 0, 4); cell.Children.Add(At(graph, 1));
            part.Detail = Named(Caption(" "), id, "Detail"); part.Detail.TextTrimming = TextTrimming.CharacterEllipsis; cell.Children.Add(At(part.Detail, 2));
            return cell;
        }
        private FrameworkElement Graph(string id, Part part)
        {
            double maximum = id == "Network" || id == "Fps" ? 0 : 100;
            if (part.Style == "Bars")
            {
                part.Bars = new HistoryBars { Maximum = maximum };
                part.Bars.SetResourceReference(HistoryBars.TrackProperty, "Track");
                if (id == "Network") part.Bars.SetResourceReference(HistoryBars.SecondFillProperty, "Muted");
                return part.Bars;
            }
            part.Graph = new HistoryGraph { Maximum = maximum, Capacity = HistoryLength, Sparkline = part.Style == "Sparkline" };
            part.Graph.SetResourceReference(HistoryGraph.GuideProperty, "Track");
            if (id == "Network") part.Graph.SetResourceReference(HistoryGraph.SecondStrokeProperty, "Muted");
            return part.Graph;
        }

        // Large: a tile per reading with its name, a detail and the value over its graph, or a big dial.
        private FrameworkElement BuildTiles(List<string> ids, double width, double height)
        {
            int[] grid = CardSizing.StatsTileGrid(width, height, ids.Count, textScale);
            int columns = grid[0], rows = grid[1];
            double tileWidth = (width - Gap * (columns - 1)) / columns, tileHeight = (height - Gap * (rows - 1)) / rows, size = CardSizing.StatsTileValue(tileHeight);
            var view = new Grid();
            for (int r = 0; r < rows; r++)
            {
                view.RowDefinitions.Add(new RowDefinition { Height = Star });
                var line = new Grid { Margin = new Thickness(0, r == 0 ? 0 : Gap, 0, 0) };
                var row = ids.Skip(r * columns).Take(columns).ToList();
                for (int c = 0; c < columns; c++) line.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                for (int c = 0; c < row.Count; c++)
                {
                    string id = row[c]; var part = parts[id] = new Part { Style = Style(id) };
                    var tile = Rows(Auto, Star, Auto);
                    var top = Columns(Auto, Star); top.Children.Add(Label(SystemStyles.Title(id)));
                    part.Detail = Named(Caption(""), id, "Detail"); part.Detail.HorizontalAlignment = HorizontalAlignment.Right; part.Detail.VerticalAlignment = VerticalAlignment.Center; part.Detail.Margin = new Thickness(8, 0, 0, 0);
                    part.Detail.TextTrimming = TextTrimming.CharacterEllipsis;
                    top.Children.Add(At(part.Detail, 0, 1));
                    tile.Children.Add(top);
                    if (SystemStyles.IsDial(part.Style))
                    {
                        // Less 20 for the range line under the dial.
                        double diameter = CardSizing.StatsRing(tileWidth - 32, tileHeight - 26 - 40 - 20);
                        var dial = Dial(id, part, diameter); dial.VerticalAlignment = VerticalAlignment.Center; dial.Margin = new Thickness(0, 6, 0, 0);
                        tile.Children.Add(At(dial, 1));
                    }
                    else
                    {
                        var body = Rows(Auto, Star);
                        part.Value = Named(Reading("—", id == "Network" ? Math.Round(size * 0.66) : size), id, "Value"); part.Value.Margin = new Thickness(0, 4, 0, 0);
                        body.Children.Add(part.Value);
                        var graph = Graph(id, part); graph.Margin = new Thickness(0, 8, 0, 0); body.Children.Add(At(graph, 1));
                        tile.Children.Add(At(body, 1));
                    }
                    // The range this card has seen (its 48-reading history), under the dial or graph.
                    if (id != "Network")
                    {
                        part.Range = Named(Caption(""), id, "Range"); part.Range.TextTrimming = TextTrimming.CharacterEllipsis; part.Range.Margin = new Thickness(0, 4, 0, 0);
                        if (SystemStyles.IsDial(part.Style)) part.Range.HorizontalAlignment = HorizontalAlignment.Center;
                        tile.Children.Add(At(part.Range, 2));
                    }
                    var border = Tile(tile, 16, new Thickness(16, 14, 16, 12)); border.Margin = new Thickness(c == 0 ? 0 : Gap / 2, 0, c == columns - 1 ? 0 : Gap / 2, 0);
                    line.Children.Add(At(Named(border, id, "Section"), 0, c));
                }
                view.Children.Add(At(line, r));
            }
            if (shown.Contains("Uptime")) { var part = parts["Uptime"] = new Part { Style = "Heading" }; part.Value = names.Add(headerUptime, "UptimeValue"); }
            return view;
        }

        private static MeterBar Bar() { var bar = new MeterBar(); bar.SetResourceReference(MeterBar.TrackProperty, "Track"); return bar; }
        private static TextBlock Tabular(TextBlock block) { Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular); return block; }

        // Fills the current design from the last reading. A busy or hot reading turns to the warning colour; a reading
        // that can't be had says why instead of a number.
        private void Render()
        {
            var readings = SystemReadings.From(last, fahrenheit, SystemReadings.FpsScale(history["Fps"]));
            foreach (var pair in parts)
            {
                string id = pair.Key; var part = pair.Value; var reading = readings[id];
                if (id == "Network") { RenderNetwork(part, reading); continue; }
                if (id == "Uptime") { part.Value.Text = last == null ? "—" : Uptime(last.Uptime, design == CardSizing.Design.Medium && bounds.Width < 440 ? 2 : 3, design == CardSizing.Design.Large); continue; }
                string brush = reading.Warning ? "Warning" : "Accent";
                if (part.Style == "List" && reading.Missing)
                {
                    part.Value.Text = reading.Short; part.Value.FontSize = 13; part.Value.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); part.Value.ToolTip = reading.Long;
                }
                else
                {
                    part.Value.Text = reading.Text; part.Value.ToolTip = reading.Long;
                    part.Value.SetResourceReference(TextBlock.ForegroundProperty, reading.Warning ? "Warning" : reading.Missing ? "Muted" : "Text");
                }
                if (part.Detail != null) { part.Detail.Text = reading.Detail; part.Detail.ToolTip = String.IsNullOrEmpty(reading.Detail) ? null : reading.Detail; }
                if (part.Ring != null) { part.Ring.Value = reading.Percent; part.Ring.SetResourceReference(RingGauge.FillProperty, brush); }
                if (part.Gauge != null) { part.Gauge.Value = reading.Percent; part.Gauge.SetResourceReference(ArcGauge.FillProperty, brush); }
                if (part.Bar != null) { part.Bar.Value = reading.Percent; part.Bar.SetResourceReference(MeterBar.FillProperty, brush); }
                List<double> values = history[id];
                if (id == "Fps") { double scale = SystemReadings.FpsScale(values); if (part.Graph != null) part.Graph.Maximum = scale; if (part.Bars != null) part.Bars.Maximum = scale; }
                if (part.Graph != null) { part.Graph.SetResourceReference(HistoryGraph.StrokeProperty, brush); part.Graph.SetValues(values); }
                if (part.Bars != null) { part.Bars.SetResourceReference(HistoryBars.FillProperty, brush); part.Bars.SetValues(values); }
                if (part.Badge != null)
                {
                    string badge = SystemReadings.Badge(readings[id == "Cpu" ? "CpuTemp" : "GpuTemp"]);
                    part.BadgeText.Text = badge; part.Badge.Visibility = badge.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                }
                if (part.Range != null) part.Range.Text = SystemReadings.Range(id, values, fahrenheit);
            }
            // The status sticker names the busiest reading, filled from 80%.
            if (Peak != null)
            {
                string peak = last == null ? null : SystemReadings.PeakId(readings, id => shown.Contains(id));
                Peak.Text = peak == null ? "" : SystemReadings.PeakText(peak, readings[peak]);
                var sticker = StatusSticker.For(Peak); if (sticker != null) sticker.Filled = peak != null && readings[peak].Percent >= SystemReadings.Strained;
            }
        }
        // The header's caption for the peak (Sticker Sheet phase 3), wrapped in a status sticker by the controller.
        public TextBlock Peak { get; set; }
        private void RenderNetwork(Part part, StatReading reading)
        {
            part.Value.Text = "↓ " + reading.Text;
            if (part.Second != null) part.Second.Text = "↑ " + reading.Detail;
            if (part.Detail != null) part.Detail.Text = "↑ " + reading.Detail;
            if (part.Graph != null) { part.Graph.SetResourceReference(HistoryGraph.StrokeProperty, "Accent"); part.Graph.SetValues(history["Network"], upHistory); }
            if (part.Bars != null) { part.Bars.SetResourceReference(HistoryBars.FillProperty, "Accent"); part.Bars.SetValues(history["Network"], upHistory); }
        }

        // "3d 4h 12m"; two parts on a narrow strip; "Up 3d 4h 12m" in the Large heading.
        public static string Uptime(TimeSpan time, int parts, bool prefixed)
        {
            var all = new[] { (int)time.TotalDays + "d", time.Hours + "h", time.Minutes + "m" };
            string text = String.Join(" ", all.Take(parts));
            return prefixed ? "Up " + text : text;
        }
    }

    internal static class TextBlockLayout
    {
        public static TextBlock Centered(this TextBlock block) { block.VerticalAlignment = VerticalAlignment.Center; return block; }
    }
}
