using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // Storage in three designs that follow the card's size (CardSizing.Storage; docs/design/storage.md). Small lists each
    // drive's letter and free space over a thin bar; Medium adds the drive's name, a thicker bar and used of total; Large
    // gives each drive a tile with a ring of used space and the free percentage inside. A drive under the low-space
    // threshold (Settings → Widgets) turns to the warning colour, and the heading names it. Drives that don't fit the
    // card are counted in a last line. The phone's StorageWidget.java draws the same three.
    public sealed class StorageCard : CardParts
    {
        private const double Gap = 12;
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private List<StorageDrive> drives;
        private bool failed;
        private int lowPercent = StorageService.DefaultLowPercent;
        private Rect bounds = Rect.Empty;
        private string built = "";
        private CardSizing.Design design;
        private readonly List<Part> parts = new List<Part>();
        private TextBlock more;

        // What one drive is drawn with in the current design; any of these may be missing.
        private sealed class Part
        {
            public TextBlock Title, Free, Detail;
            public MeterBar Bar;
            public RingGauge Ring;
        }

        public StorageCard(FrameworkElement scope, Grid host, TextBlock status) { this.host = host; this.status = status; names = new PartNames(scope); }

        public CardSizing.Design Current { get { return design; } }
        public IList<StorageDrive> Drives { get { return drives; } }

        public void Fit(Rect card)
        {
            bounds = card;
            Rebuild();
        }
        public void Configure(int lowPercent) { this.lowPercent = StorageService.ClampPercent(lowPercent); Render(); }
        public void Update(List<StorageDrive> next) { failed = false; drives = next; Rebuild(); }
        public void Unavailable() { failed = true; Render(); }

        // The design is rebuilt only when the card's size or the number of drives changes.
        private void Rebuild()
        {
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + (drives == null ? -1 : drives.Count);
            if (key != built) { built = key; Build(); }
            Render();
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); parts.Clear(); more = null;
            // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36);
            design = CardSizing.Storage(bounds.Width, bounds.Height);
            FrameworkElement view;
            if (drives == null) view = Caption("Reading drives…");
            else if (drives.Count == 0) view = Caption("No fixed drives found.");
            else
            {
                int capacity = CardSizing.StorageCapacity(design, width, height, drives.Count);
                int shown = capacity >= drives.Count ? drives.Count : Math.Max(1, capacity - 1);
                var list = drives.Take(shown).ToList();
                view = design == CardSizing.Design.Small ? BuildList(list, height, shown < drives.Count)
                    : design == CardSizing.Design.Medium ? BuildRows(list, shown < drives.Count, CardSizing.StorageDetailRows(height, drives.Count))
                    : BuildTiles(list, width, height, shown < drives.Count);
            }
            host.Children.Add(names.Add(view, "StorageView"));
        }
        private TextBlock More()
        {
            more = names.Add(Caption(""), "StorageMore"); more.Margin = new Thickness(0, 6, 0, 0); more.VerticalAlignment = VerticalAlignment.Bottom;
            return more;
        }
        private MeterBar Bar(double height) { var bar = new MeterBar { Height = height }; bar.SetResourceReference(MeterBar.TrackProperty, "Track"); return bar; }

        // Small: one row per drive, the letter at the left and the free space at the right, a thin bar under them.
        private FrameworkElement BuildList(List<StorageDrive> list, double height, bool overflow)
        {
            var view = new Grid();
            double rows = height - (overflow ? 26 : 0), rowHeight = (rows - 8 * (list.Count - 1)) / list.Count, size = CardSizing.StorageListValue(rowHeight);
            for (int i = 0; i < list.Count; i++)
            {
                var part = new Part(); parts.Add(part);
                view.RowDefinitions.Add(new RowDefinition { Height = Star });
                var row = Rows(Star, Auto); row.Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0);
                var top = Columns(Auto, Star);
                part.Title = names.Add(Label(list[i].Name).Centered(), "StorageTitle" + i); top.Children.Add(part.Title);
                part.Free = names.Add(Reading("—", size), "StorageFree" + i); part.Free.HorizontalAlignment = HorizontalAlignment.Right; part.Free.VerticalAlignment = VerticalAlignment.Center; part.Free.Margin = new Thickness(12, 0, 0, 0);
                top.Children.Add(At(part.Free, 0, 1));
                row.Children.Add(top);
                part.Bar = Bar(4); part.Bar.Margin = new Thickness(0, 4, 0, 0); row.Children.Add(At(part.Bar, 1));
                view.Children.Add(At(row, i));
            }
            if (overflow) { view.RowDefinitions.Add(new RowDefinition { Height = Auto }); view.Children.Add(At(More(), list.Count)); }
            return view;
        }

        // Medium: a row per drive with its name and free space, a bar, and used of total under it.
        private FrameworkElement BuildRows(List<StorageDrive> list, bool overflow, bool detail)
        {
            var view = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < list.Count; i++)
            {
                var part = new Part(); parts.Add(part);
                var row = Rows(Auto, Auto, Auto); row.Margin = new Thickness(0, i == 0 ? 0 : detail ? 8 : 4, 0, 0);
                var top = Columns(Star, Auto);
                part.Title = names.Add(Text("", detail ? "BodySize" : "SmallSize", "Text"), "StorageTitle" + i); part.Title.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(part.Title);
                part.Free = names.Add(Text("—", detail ? "BodySize" : "SmallSize", "Text"), "StorageFree" + i); part.Free.Margin = new Thickness(12, 0, 0, 0); part.Free.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(At(part.Free, 0, 1));
                row.Children.Add(top);
                part.Bar = Bar(detail ? 10 : 4); part.Bar.Margin = new Thickness(0, detail ? 6 : 3, 0, 0); row.Children.Add(At(part.Bar, 1));
                if (detail) { part.Detail = names.Add(Caption(""), "StorageDetail" + i); part.Detail.Margin = new Thickness(0, 5, 0, 0); row.Children.Add(At(part.Detail, 2)); }
                view.Children.Add(row);
            }
            if (overflow) view.Children.Add(More());
            return view;
        }

        // Large: a tile per drive with a ring of used space, the free percentage inside, and the drive and sizes under it.
        private FrameworkElement BuildTiles(List<StorageDrive> list, double width, double height, bool overflow)
        {
            int columns = CardSizing.StorageTileColumns(width, list.Count), rows = (list.Count + columns - 1) / columns;
            double room = height - (overflow ? 26 : 0), tileHeight = (room - Gap * (rows - 1)) / rows, tileWidth = (width - Gap * (columns - 1)) / columns;
            double diameter = CardSizing.StorageRing(tileWidth, tileHeight);
            var view = new Grid();
            for (int r = 0; r < rows; r++)
            {
                view.RowDefinitions.Add(new RowDefinition { Height = Star });
                var line = new Grid { Margin = new Thickness(0, r == 0 ? 0 : Gap, 0, 0) };
                for (int c = 0; c < columns; c++) line.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                var ids = list.Skip(r * columns).Take(columns).ToList();
                for (int c = 0; c < ids.Count; c++)
                {
                    int index = r * columns + c; var part = new Part(); parts.Add(part);
                    var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    var dial = new Grid { Width = diameter, Height = diameter, HorizontalAlignment = HorizontalAlignment.Center };
                    part.Ring = new RingGauge { Thickness = Math.Max(6, Math.Min(14, Math.Round(diameter * 0.08))) };
                    part.Ring.SetResourceReference(RingGauge.TrackProperty, "Track");
                    dial.Children.Add(part.Ring);
                    var inside = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    part.Free = names.Add(Reading("—", Math.Round(diameter * 0.24)), "StorageFree" + index); part.Free.HorizontalAlignment = HorizontalAlignment.Center; inside.Children.Add(part.Free);
                    var free = Caption("free"); free.HorizontalAlignment = HorizontalAlignment.Center; inside.Children.Add(free);
                    dial.Children.Add(inside);
                    content.Children.Add(dial);
                    part.Title = names.Add(Text("", "BodySize", "Text"), "StorageTitle" + index); part.Title.HorizontalAlignment = HorizontalAlignment.Center; part.Title.MaxWidth = Math.Max(60, tileWidth - 24); part.Title.Margin = new Thickness(0, 10, 0, 0);
                    content.Children.Add(part.Title);
                    part.Detail = names.Add(Caption(""), "StorageDetail" + index); part.Detail.HorizontalAlignment = HorizontalAlignment.Center; part.Detail.MaxWidth = Math.Max(60, tileWidth - 24); part.Detail.Margin = new Thickness(0, 2, 0, 0);
                    content.Children.Add(part.Detail);
                    var tile = Tile(content, 16, new Thickness(12)); tile.Margin = new Thickness(c == 0 ? 0 : Gap / 2, 0, c == columns - 1 ? 0 : Gap / 2, 0);
                    line.Children.Add(At(tile, 0, c));
                }
                view.Children.Add(At(line, r));
            }
            if (overflow) { view.RowDefinitions.Add(new RowDefinition { Height = Auto }); view.Children.Add(At(More(), rows)); }
            return view;
        }

        // Fills the current design from the last reading.
        private void Render()
        {
            status.Text = failed ? "UNAVAILABLE" : drives == null ? "" : StorageService.LowFlag(drives, lowPercent);
            if (drives == null) return;
            for (int i = 0; i < parts.Count && i < drives.Count; i++)
            {
                var drive = drives[i]; var part = parts[i];
                bool low = StorageService.Low(drive, lowPercent);
                string fill = low ? "Warning" : "Accent", free = StorageService.FormatBytes(drive.FreeBytes);
                if (design == CardSizing.Design.Small) part.Free.Text = free;
                else if (design == CardSizing.Design.Medium)
                {
                    part.Title.Text = StorageService.Title(drive); part.Free.Text = free + " free";
                    if (part.Detail != null) part.Detail.Text = StorageService.FormatBytes(drive.TotalBytes - drive.FreeBytes) + " used of " + StorageService.FormatBytes(drive.TotalBytes) + "  ·  " + Math.Round(drive.FreePercent) + "% free";
                }
                else
                {
                    part.Free.Text = Math.Round(drive.FreePercent) + "%"; part.Title.Text = StorageService.Title(drive);
                    part.Detail.Text = free + " free of " + StorageService.FormatBytes(drive.TotalBytes);
                }
                part.Free.SetResourceReference(TextBlock.ForegroundProperty, low ? "Warning" : "Text");
                if (part.Bar != null) { part.Bar.Value = drive.UsedPercent; part.Bar.SetResourceReference(MeterBar.FillProperty, fill); }
                if (part.Ring != null) { part.Ring.Value = drive.UsedPercent; part.Ring.SetResourceReference(RingGauge.FillProperty, fill); }
            }
            if (more != null)
            {
                int hidden = drives.Count - parts.Count, hiddenLow = drives.Skip(parts.Count).Count(d => StorageService.Low(d, lowPercent));
                more.Text = "+" + hidden + (hidden == 1 ? " more drive" : " more drives") + (hiddenLow > 0 ? "  ·  " + hiddenLow + " low" : "");
                more.SetResourceReference(TextBlock.ForegroundProperty, hiddenLow > 0 ? "Warning" : "Muted");
            }
        }
    }
}
