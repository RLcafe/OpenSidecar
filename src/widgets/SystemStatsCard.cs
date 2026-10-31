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
    // System stats in three designs that follow the card's size (CardSizing.Stats; docs/design/widget-size-views.md).
    // Small lists the readings with a thin bar under each percentage; Medium shows CPU, GPU and RAM as rings over a
    // network and uptime strip; Large gives each reading a tile with its recent history, and uptime moves to the
    // heading. The readings and their history live here, so a new design starts full. The phone's SystemWidget.java
    // draws the same three.
    public sealed class SystemStatsCard : CardParts
    {
        public const int HistoryLength = 48;
        private const double Gap = 12;
        private readonly Grid host;
        private readonly TextBlock headerUptime;
        private readonly PartNames names;
        private bool cpu = true, gpu = true, ram = true, network = true, uptime = true;
        private SystemSnapshot last;
        private readonly List<double> cpuHistory = new List<double>(), gpuHistory = new List<double>(), ramHistory = new List<double>(), downHistory = new List<double>(), upHistory = new List<double>();
        private Rect bounds = Rect.Empty;
        private string built = "";
        private CardSizing.Design design;
        private readonly Dictionary<string, Part> parts = new Dictionary<string, Part>();

        // What one reading is drawn with in the current design; any of these may be missing.
        private sealed class Part
        {
            public TextBlock Value, Detail, Second;
            public RingGauge Ring;
            public MeterBar Bar;
            public HistoryGraph Graph;
        }

        public SystemStatsCard(FrameworkElement scope, Grid host, TextBlock headerUptime)
        {
            this.host = host; this.headerUptime = headerUptime; names = new PartNames(scope);
        }

        public CardSizing.Design Current { get { return design; } }

        // The readings chosen in Settings → Widgets.
        public void Choose(bool cpu, bool gpu, bool ram, bool network, bool uptime)
        {
            this.cpu = cpu; this.gpu = gpu; this.ram = ram; this.network = network; this.uptime = uptime;
            if (!bounds.IsEmpty) Fit(bounds);
        }

        // The card's bounds on the canvas; the design is rebuilt only when its size or readings change.
        public void Fit(Rect card)
        {
            bounds = card;
            string key = Math.Round(card.Width) + "x" + Math.Round(card.Height) + (cpu ? "c" : "") + (gpu ? "g" : "") + (ram ? "r" : "") + (network ? "n" : "") + (uptime ? "u" : "");
            if (key == built) return;
            built = key; Build(); Render();
        }

        public void Update(SystemSnapshot snapshot)
        {
            last = snapshot;
            Push(cpuHistory, snapshot.CpuAvailable ? (double?)snapshot.Cpu : null);
            Push(gpuHistory, snapshot.Gpu);
            Push(ramHistory, snapshot.MemoryAvailable ? (double?)snapshot.MemoryPercent : null);
            Push(downHistory, snapshot.NetworkAvailable ? (double?)snapshot.DownloadBytes : null);
            Push(upHistory, snapshot.NetworkAvailable ? (double?)snapshot.UploadBytes : null);
            Render();
        }
        public void Unavailable()
        {
            last = null;
            foreach (var history in new[] { cpuHistory, gpuHistory, ramHistory, downHistory, upHistory }) history.Clear();
            Render();
        }
        private static void Push(List<double> history, double? value)
        {
            if (!value.HasValue) { history.Clear(); return; }
            history.Add(value.Value); if (history.Count > HistoryLength) history.RemoveAt(0);
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); parts.Clear();
            var shown = new List<string>();
            if (cpu) shown.Add("Cpu"); if (gpu) shown.Add("Gpu"); if (ram) shown.Add("Ram"); if (network) shown.Add("Network"); if (uptime) shown.Add("Uptime");
            // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36);
            design = CardSizing.Stats(bounds.Width, bounds.Height);
            // Rings need a percentage to show and tiles a reading to graph; otherwise the list says it best.
            if (design == CardSizing.Design.Medium && !shown.Any(IsPercent)) design = CardSizing.Design.Small;
            if (design == CardSizing.Design.Large && !shown.Any(id => id != "Uptime")) design = CardSizing.Design.Small;
            headerUptime.Visibility = design == CardSizing.Design.Large && uptime ? Visibility.Visible : Visibility.Collapsed;
            FrameworkElement view;
            if (shown.Count == 0) view = Caption("No readings chosen. Choose them in Settings → Widgets.");
            else if (design == CardSizing.Design.Small) view = BuildList(shown, height);
            else if (design == CardSizing.Design.Medium) view = BuildRings(shown, width, height);
            else view = BuildTiles(shown.Where(id => id != "Uptime").ToList(), width, height);
            host.Children.Add(names.Add(view, "SystemView"));
        }
        private static bool IsPercent(string id) { return id == "Cpu" || id == "Gpu" || id == "Ram"; }
        private static string Title(string id) { return id == "Cpu" ? "CPU" : id == "Gpu" ? "GPU" : id == "Ram" ? "RAM" : id == "Network" ? "NET" : "UPTIME"; }
        private T Named<T>(T element, string id, string role) where T : FrameworkElement
        {
            if (id == "Ram" && role == "Value") return names.Add(element, "MemoryPercent");
            if (id == "Network" && role == "Value") return names.Add(element, "DownloadValue");
            if (id == "Network" && role == "Second") return names.Add(element, "UploadValue");
            return names.Add(element, id + role);
        }

        // Small: one row per reading, the name at the left and the number at the right, a thin bar under percentages.
        private FrameworkElement BuildList(List<string> shown, double height)
        {
            var list = new Grid();
            double rowHeight = (height - 8 * (shown.Count - 1)) / shown.Count, size = CardSizing.StatsListValue(rowHeight);
            for (int i = 0; i < shown.Count; i++)
            {
                string id = shown[i]; var part = parts[id] = new Part();
                list.RowDefinitions.Add(new RowDefinition { Height = Star });
                var row = Rows(Star, Auto); row.Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0);
                var top = Columns(Auto, Star); top.Children.Add(Label(Title(id)).Centered());
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
                    top.Children.Add(At(part.Value, 0, 1));
                }
                row.Children.Add(top);
                if (IsPercent(id)) { part.Bar = Bar(); part.Bar.Height = 4; part.Bar.Margin = new Thickness(0, 4, 0, 0); row.Children.Add(At(part.Bar, 1)); }
                list.Children.Add(At(Named(row, id, "Section"), i));
            }
            return list;
        }

        // Medium: CPU, GPU and RAM as rings side by side, with the network rates and uptime in a strip under them.
        private FrameworkElement BuildRings(List<string> shown, double width, double height)
        {
            var rings = shown.Where(IsPercent).ToList(); bool strip = network || uptime;
            var view = Rows(Star, Auto);
            double ringRoom = height - (strip ? 56 + Gap : 0), cell = width / rings.Count;
            double diameter = CardSizing.StatsRing(cell, ringRoom - 48);
            var row = new UniformGrid { Rows = 1, Columns = rings.Count, VerticalAlignment = VerticalAlignment.Center };
            foreach (string id in rings)
            {
                var part = parts[id] = new Part();
                var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var dial = new Grid { Width = diameter, Height = diameter, HorizontalAlignment = HorizontalAlignment.Center };
                part.Ring = new RingGauge { Thickness = Math.Max(5, Math.Min(12, Math.Round(diameter * 0.075))) };
                part.Ring.SetResourceReference(RingGauge.TrackProperty, "Track");
                dial.Children.Add(part.Ring);
                part.Value = Named(Reading("—", Math.Round(diameter * 0.27)), id, "Value");
                part.Value.HorizontalAlignment = HorizontalAlignment.Center; part.Value.VerticalAlignment = VerticalAlignment.Center;
                dial.Children.Add(part.Value);
                column.Children.Add(dial);
                var label = Label(Title(id)); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 8, 0, 0); column.Children.Add(label);
                part.Detail = Caption(" "); part.Detail.HorizontalAlignment = HorizontalAlignment.Center; part.Detail.MaxWidth = Math.Max(40, cell - 8); part.Detail.Margin = new Thickness(0, 2, 0, 0);
                column.Children.Add(part.Detail);
                row.Children.Add(Named(column, id, "Section"));
            }
            view.Children.Add(row);
            if (strip)
            {
                var line = Columns(Star, Star, Auto);
                if (network)
                {
                    var part = parts["Network"] = new Part();
                    part.Value = Named(Tabular(Text("↓ —", "BodySize", "Text")), "Network", "Value"); part.Value.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(part.Value);
                    part.Second = Named(Tabular(Text("↑ —", "BodySize", "Text")), "Network", "Second"); part.Second.VerticalAlignment = VerticalAlignment.Center; part.Second.Margin = new Thickness(12, 0, 0, 0);
                    line.Children.Add(At(part.Second, 0, 1));
                }
                if (uptime)
                {
                    var part = parts["Uptime"] = new Part();
                    part.Value = Named(Text("—", "SmallSize", "Muted"), "Uptime", "Value"); part.Value.VerticalAlignment = VerticalAlignment.Center; part.Value.Margin = new Thickness(12, 0, 0, 0);
                    if (!network) { part.Value.Margin = new Thickness(0); line.Children.Add(At(part.Value, 0, 0, 3)); } else line.Children.Add(At(part.Value, 0, 2));
                }
                var tile = Tile(line, 14, new Thickness(16, 0, 16, 0)); tile.Height = 56; tile.Margin = new Thickness(0, Gap, 0, 0);
                view.Children.Add(At(Named(tile, network ? "Network" : "Uptime", "Section"), 1));
            }
            return view;
        }

        // Large: a tile per reading with its name, a detail, the number and its last 48 readings as a graph.
        private FrameworkElement BuildTiles(List<string> shown, double width, double height)
        {
            int columns = CardSizing.StatsTileColumns(width, shown.Count), rows = (shown.Count + columns - 1) / columns;
            double tileHeight = (height - Gap * (rows - 1)) / rows, size = CardSizing.StatsTileValue(tileHeight);
            var view = new Grid();
            for (int r = 0; r < rows; r++)
            {
                view.RowDefinitions.Add(new RowDefinition { Height = Star });
                var line = new Grid { Margin = new Thickness(0, r == 0 ? 0 : Gap, 0, 0) };
                var ids = shown.Skip(r * columns).Take(columns).ToList();
                for (int c = 0; c < ids.Count; c++)
                {
                    line.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                    string id = ids[c]; var part = parts[id] = new Part();
                    var tile = Rows(Auto, Auto, Star);
                    var top = Columns(Auto, Star); top.Children.Add(Label(Title(id)));
                    part.Detail = Caption(""); part.Detail.HorizontalAlignment = HorizontalAlignment.Right; part.Detail.VerticalAlignment = VerticalAlignment.Center; part.Detail.Margin = new Thickness(8, 0, 0, 0);
                    top.Children.Add(At(part.Detail, 0, 1));
                    tile.Children.Add(top);
                    part.Value = Named(Reading("—", id == "Network" ? Math.Round(size * 0.66) : size), id, "Value"); part.Value.Margin = new Thickness(0, 4, 0, 0);
                    tile.Children.Add(At(part.Value, 1));
                    part.Graph = new HistoryGraph { Maximum = id == "Network" ? 0 : 100, Capacity = HistoryLength, Margin = new Thickness(0, 8, 0, 0) };
                    part.Graph.SetResourceReference(HistoryGraph.GuideProperty, "Track");
                    if (id == "Network") part.Graph.SetResourceReference(HistoryGraph.SecondStrokeProperty, "Muted");
                    tile.Children.Add(At(part.Graph, 2));
                    var border = Tile(tile, 16, new Thickness(16, 14, 16, 12)); border.Margin = new Thickness(c == 0 ? 0 : Gap / 2, 0, c == ids.Count - 1 ? 0 : Gap / 2, 0);
                    line.Children.Add(At(Named(border, id, "Section"), 0, c));
                }
                view.Children.Add(At(line, r));
            }
            if (uptime) { var part = parts["Uptime"] = new Part(); part.Value = names.Add(headerUptime, "UptimeValue"); }
            return view;
        }

        private static MeterBar Bar() { var bar = new MeterBar(); bar.SetResourceReference(MeterBar.TrackProperty, "Track"); return bar; }
        private static TextBlock Tabular(TextBlock block) { Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular); return block; }

        // Fills the current design from the last reading. A reading at 90% or more turns to the warning colour.
        private void Render()
        {
            var s = last;
            Percent("Cpu", s != null && s.CpuAvailable ? (double?)s.Cpu : null, null, cpuHistory);
            Percent("Gpu", s == null ? null : s.Gpu, s == null ? "Unavailable" : s.GpuStatus ?? "Unavailable", gpuHistory);
            Percent("Ram", s != null && s.MemoryAvailable ? (double?)s.MemoryPercent : null, s == null || !s.MemoryAvailable ? "Unavailable" : s.UsedMemoryGB.ToString("0.0") + " / " + s.TotalMemoryGB.ToString("0.0") + " GB", ramHistory);
            Part part;
            if (parts.TryGetValue("Network", out part))
            {
                bool ok = s != null && s.NetworkAvailable;
                string down = ok ? SystemService.FormatRate(s.DownloadBytes) : "— KB/s", up = ok ? SystemService.FormatRate(s.UploadBytes) : "— KB/s";
                part.Value.Text = "↓ " + down;
                if (part.Second != null) part.Second.Text = "↑ " + up;
                if (part.Detail != null) part.Detail.Text = "↑ " + up;
                if (part.Graph != null) part.Graph.SetValues(downHistory, upHistory);
                if (part.Graph != null) part.Graph.SetResourceReference(HistoryGraph.StrokeProperty, "Accent");
            }
            if (parts.TryGetValue("Uptime", out part))
                part.Value.Text = s == null ? "—" : Uptime(s.Uptime, design == CardSizing.Design.Medium && bounds.Width < 440 ? 2 : 3, design == CardSizing.Design.Large);
        }
        private void Percent(string id, double? value, string detail, List<double> history)
        {
            Part part;
            if (!parts.TryGetValue(id, out part)) return;
            double reading = value ?? 0; string brush = reading >= 90 ? "Warning" : "Accent";
            part.Value.Text = value.HasValue ? Math.Round(reading) + "%" : "—";
            part.Value.SetResourceReference(TextBlock.ForegroundProperty, reading >= 90 ? "Warning" : "Text");
            if (part.Detail != null) part.Detail.Text = detail ?? "";
            if (part.Ring != null) { part.Ring.Value = reading; part.Ring.SetResourceReference(RingGauge.FillProperty, brush); }
            if (part.Bar != null) { part.Bar.Value = reading; part.Bar.SetResourceReference(MeterBar.FillProperty, brush); }
            if (part.Graph != null) { part.Graph.SetResourceReference(HistoryGraph.StrokeProperty, brush); part.Graph.SetValues(history); }
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
