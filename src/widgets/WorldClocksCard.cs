using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // World clocks in three designs that follow the card's size (CardSizing.Clocks; docs/design/world-clocks.md).
    // Small lists each city with its time at the right; Medium puts the cities side by side with a large time, the day
    // and the hours against this PC; Large gives each city a tile with a larger time and the date. Clocks that don't fit
    // a small card are counted in the heading. The phone's ClocksWidget.java draws the same three.
    public sealed class WorldClocksCard : CardParts
    {
        private const double Gap = 12;
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private WorldClock[] clocks = new WorldClock[0];
        private bool use24;
        private Rect bounds = Rect.Empty;
        private string built = "", rendered = "";
        private CardSizing.Design design;
        private readonly List<Part> parts = new List<Part>();

        // What one clock is drawn with in the current design; Day and Difference may be missing.
        private sealed class Part { public TextBlock Label, Time, Period, Day, Difference; }

        public WorldClocksCard(FrameworkElement scope, Grid host, TextBlock status) { this.host = host; this.status = status; names = new PartNames(scope); }

        public CardSizing.Design Current { get { return design; } }
        public int Shown { get { return parts.Count; } }
        public bool Empty { get { return clocks.Length == 0; } }

        public void Fit(Rect card) { bounds = card; Rebuild(); }
        public void Configure(IList<WorldClock> next, bool use24)
        {
            clocks = WorldClockService.Clean(next); this.use24 = use24; rendered = "";
            Rebuild();
        }
        // The one-second heartbeat; the text only changes when a minute turns.
        public void Tick() { Render(); }

        private void Rebuild()
        {
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + String.Join(",", clocks.Select(c => c.Zone + "=" + c.Label)) + "|" + use24;
            if (key != built) { built = key; Build(); rendered = ""; }
            Render();
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); parts.Clear();
            // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36);
            design = CardSizing.Clocks(bounds.Width, bounds.Height);
            FrameworkElement view;
            if (clocks.Length == 0)
            {
                var empty = Caption("Choose up to three time zones in Settings → Widgets.");
                empty.TextWrapping = TextWrapping.Wrap; empty.VerticalAlignment = VerticalAlignment.Center;
                view = empty;
            }
            else
            {
                int shown = design == CardSizing.Design.Small ? Math.Min(clocks.Length, CardSizing.ClockRows(height)) : clocks.Length;
                view = design == CardSizing.Design.Small ? BuildList(shown, height) : design == CardSizing.Design.Medium ? BuildColumns(width, height) : BuildTiles(width, height);
            }
            host.Children.Add(names.Add(view, "ClocksView"));
        }

        private Part NewPart(int index, double timeSize)
        {
            var part = new Part(); parts.Add(part);
            part.Time = names.Add(Reading("", timeSize), "ClockTime" + index);
            part.Period = names.Add(Text("", "SmallSize", "Muted"), "ClockPeriod" + index);
            part.Period.Margin = new Thickness(5, 0, 0, Math.Round(timeSize * 0.12)); part.Period.VerticalAlignment = VerticalAlignment.Bottom;
            return part;
        }
        private static StackPanel TimeRow(Part part, HorizontalAlignment align)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = align };
            row.Children.Add(part.Time); row.Children.Add(part.Period);
            return row;
        }

        // Small: one row per city, the name and the day over the time at the right.
        private FrameworkElement BuildList(int shown, double height)
        {
            var view = new Grid();
            double rowHeight = (height - 8 * (shown - 1)) / shown;
            bool detail = rowHeight >= CardSizing.ClockDetailRow;
            double size = CardSizing.ClockListTime(rowHeight);
            for (int i = 0; i < shown; i++)
            {
                view.RowDefinitions.Add(new RowDefinition { Height = Star });
                var part = NewPart(i, size);
                var row = Columns(Star, Auto); row.Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0);
                var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                part.Label = names.Add(Text("", "BodySize", "Text"), "ClockLabel" + i); left.Children.Add(part.Label);
                if (detail) { part.Day = names.Add(Caption(""), "ClockDay" + i); part.Day.Margin = new Thickness(0, 2, 0, 0); left.Children.Add(part.Day); }
                row.Children.Add(left);
                var time = TimeRow(part, HorizontalAlignment.Right); time.VerticalAlignment = VerticalAlignment.Center; time.Margin = new Thickness(12, 0, 0, 0);
                row.Children.Add(At(time, 0, 1));
                view.Children.Add(At(row, i));
            }
            return view;
        }

        // Medium: the cities side by side, each with its name, a large time, the day and the hours against this PC.
        private FrameworkElement BuildColumns(double width, double height)
        {
            int n = clocks.Length;
            double column = (width - Gap * (n - 1)) / n, size = CardSizing.ClockColumnTime(column, height, use24);
            var view = new Grid { VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < n; i++)
            {
                view.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                var part = NewPart(i, size);
                var content = new StackPanel { Margin = new Thickness(i == 0 ? 0 : Gap / 2, 0, i == n - 1 ? 0 : Gap / 2, 0) };
                part.Label = names.Add(Text("", "BodySize", "Text"), "ClockLabel" + i); content.Children.Add(part.Label);
                var time = TimeRow(part, HorizontalAlignment.Left); time.Margin = new Thickness(0, 6, 0, 4); content.Children.Add(time);
                part.Day = names.Add(Caption(""), "ClockDay" + i); content.Children.Add(part.Day);
                part.Difference = names.Add(Caption(""), "ClockDifference" + i); part.Difference.Margin = new Thickness(0, 2, 0, 0); content.Children.Add(part.Difference);
                view.Children.Add(At(content, 0, i));
            }
            return view;
        }

        // Large: a tile per city with a larger time and the date; tiles sit side by side, or stack on a narrow card.
        private FrameworkElement BuildTiles(double width, double height)
        {
            int n = clocks.Length;
            bool across = CardSizing.ClockTilesAcross(width, n);
            double tileWidth = across ? (width - Gap * (n - 1)) / n : width, tileHeight = across ? height : (height - Gap * (n - 1)) / n;
            double size = across ? CardSizing.ClockTileTime(tileWidth, tileHeight, use24) : CardSizing.ClockStackedTime(tileWidth, tileHeight);
            var view = new Grid();
            for (int i = 0; i < n; i++)
            {
                if (across) view.ColumnDefinitions.Add(new ColumnDefinition { Width = Star }); else view.RowDefinitions.Add(new RowDefinition { Height = Star });
                var part = NewPart(i, size);
                Border tile;
                if (across)
                {
                    var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    part.Label = names.Add(Text("", "BodySize", "Text"), "ClockLabel" + i); part.Label.FontWeight = FontWeights.SemiBold; content.Children.Add(part.Label);
                    var time = TimeRow(part, HorizontalAlignment.Left); time.Margin = new Thickness(0, 8, 0, 6); content.Children.Add(time);
                    part.Day = names.Add(Text("", "SmallSize", "Text"), "ClockDay" + i); content.Children.Add(part.Day);
                    part.Difference = names.Add(Caption(""), "ClockDifference" + i); part.Difference.Margin = new Thickness(0, 2, 0, 0); content.Children.Add(part.Difference);
                    tile = Tile(content, 16, new Thickness(16));
                }
                else
                {
                    // Stacked tiles put the time at the right of the name, with the date and hours on one line.
                    var row = Columns(Star, Auto);
                    var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    part.Label = names.Add(Text("", "BodySize", "Text"), "ClockLabel" + i); part.Label.FontWeight = FontWeights.SemiBold; left.Children.Add(part.Label);
                    part.Day = names.Add(Caption(""), "ClockDay" + i); part.Day.Margin = new Thickness(0, 2, 0, 0); left.Children.Add(part.Day);
                    row.Children.Add(left);
                    var time = TimeRow(part, HorizontalAlignment.Right); time.VerticalAlignment = VerticalAlignment.Center; time.Margin = new Thickness(12, 0, 0, 0);
                    row.Children.Add(At(time, 0, 1));
                    tile = Tile(row, 16, new Thickness(16, 10, 16, 10));
                }
                tile.Margin = across ? new Thickness(i == 0 ? 0 : Gap / 2, 0, i == n - 1 ? 0 : Gap / 2, 0) : new Thickness(0, i == 0 ? 0 : Gap / 2, 0, i == n - 1 ? 0 : Gap / 2);
                view.Children.Add(across ? At(tile, 0, i) : At(tile, i));
            }
            return view;
        }

        // Fills the current design with the time now; nothing is set again until a minute turns.
        private void Render()
        {
            var readings = WorldClockService.Read(clocks, DateTime.UtcNow, TimeZoneInfo.Local);
            int hidden = clocks.Length - parts.Count;
            status.Text = hidden > 0 ? "+" + hidden + " MORE" : "";
            string key = String.Join("|", readings.Select(r => r.Time.ToString("yyyyMMddHHmm") + r.DifferenceMinutes));
            if (key == rendered) return;
            rendered = key;
            for (int i = 0; i < parts.Count && i < readings.Count; i++)
            {
                var reading = readings[i]; var part = parts[i];
                part.Label.Text = reading.Label;
                part.Time.Text = WorldClockService.Time(reading.Time, use24);
                part.Period.Text = use24 ? "" : WorldClockService.Period(reading.Time);
                part.Period.Visibility = use24 ? Visibility.Collapsed : Visibility.Visible;
                string day = WorldClockService.Day(reading.DayOffset), difference = WorldClockService.Difference(reading.DifferenceMinutes);
                if (design == CardSizing.Design.Small) { if (part.Day != null) part.Day.Text = day + "  ·  " + difference; }
                else if (design == CardSizing.Design.Medium) { part.Day.Text = day; part.Difference.Text = difference; }
                else if (part.Difference == null) part.Day.Text = WorldClockService.Date(reading) + "  ·  " + difference;
                else { part.Day.Text = WorldClockService.Date(reading); part.Difference.Text = reading.DifferenceMinutes == 0 ? "Same time as this PC" : difference + " from this PC"; }
            }
        }
    }
}
