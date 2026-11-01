using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WidgetScreen
{
    // Calendar and agenda in three designs that follow the card's size (CardSizing.Calendar; docs/design/calendar.md).
    // Small is the next event with its countdown; Medium adds the events after it; Large is the agenda for two weeks,
    // grouped by day, with a dot per feed when there's more than one. The phone's CalendarWidget.java draws the same.
    public sealed class CalendarCard : CardParts
    {
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private Rect bounds = Rect.Empty;
        private string built = "", rendered = "";
        private CardSizing.Design design;
        private List<CalendarEvent> events;
        private List<FeedStatus> feeds = new List<FeedStatus>();
        private int feedCount;
        private bool use24, failed;
        private double textScale = 1;
        // The feeds' colours: the accent, then the other accents in turn.
        private static readonly string[] FeedColors = { null, "#8ED8F8", "#C5AEFF", "#F5C47D", "#BAF28B" };

        public CalendarCard(FrameworkElement scope, Grid host, TextBlock status) { this.host = host; this.status = status; names = new PartNames(scope); }
        public CardSizing.Design Current { get { return design; } }

        public void Fit(Rect card) { bounds = card; rendered = ""; Render(DateTime.UtcNow); }
        public void Configure(int feedCount, bool use24, double textScale) { if (this.feedCount != feedCount || this.use24 != use24 || this.textScale != textScale) { this.feedCount = feedCount; this.use24 = use24; this.textScale = textScale; if (feedCount == 0) { events = null; feeds.Clear(); } rendered = ""; Render(DateTime.UtcNow); } }
        public void Update(List<CalendarEvent> next, List<FeedStatus> statuses) { events = next; feeds = statuses ?? new List<FeedStatus>(); failed = false; rendered = ""; Render(DateTime.UtcNow); }
        public void Failed() { failed = true; rendered = ""; Render(DateTime.UtcNow); }

        // Rebuilt when the minute, the events or the size change; nothing moves in between.
        public void Render(DateTime utcNow)
        {
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + feedCount + "|" + use24 + "|" + failed + "|" + (utcNow.Ticks / TimeSpan.TicksPerMinute) + "|" + (events == null ? -1 : events.GetHashCode());
            if (key == rendered) return;
            rendered = key;
            var current = events == null ? null : CalendarService.Merge(events, utcNow);
            int problems = feeds.Count(f => !f.Ok);
            status.Text = feedCount > 0 && (failed || problems > 0) ? (failed || problems == feeds.Count ? "OFFLINE" : problems == 1 ? "1 FEED FAILED" : problems + " FEEDS FAILED") : "";
            status.ToolTip = String.Join("\n", feeds.Where(f => !f.Ok).Select(f => f.Name + ": " + f.Problem));
            host.Children.Clear(); names.Clear();
            design = CardSizing.Calendar(bounds.Width, bounds.Height);
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36);
            FrameworkElement view;
            if (feedCount == 0) view = Note("Add a calendar in Settings → Calendar: Google's secret iCal address or an Outlook published calendar.");
            else if (current == null) view = Note(failed ? "The calendars couldn't be read. Tap to try again." : "Reading your calendars…");
            else if (current.Count == 0) view = Note("Nothing in the next two weeks.");
            else if (design == CardSizing.Design.Large) view = Agenda(current, utcNow, height);
            else view = NextAndAfter(current, utcNow, width, height);
            host.Children.Add(names.Add(view, "CalendarView"));
        }
        private TextBlock Note(string text) { var note = Caption(text); note.TextWrapping = TextWrapping.Wrap; note.VerticalAlignment = VerticalAlignment.Center; return note; }

        private FrameworkElement Dot(int feed)
        {
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            string hex = FeedColors[feed % FeedColors.Length];
            if (hex == null) dot.SetResourceReference(Shape.FillProperty, "Accent"); else dot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            return dot;
        }

        // Small and Medium: the next event large, then (Medium) the ones after it, one line each.
        private FrameworkElement NextAndAfter(List<CalendarEvent> current, DateTime utcNow, double width, double height)
        {
            var next = CalendarText.Next(current, utcNow);
            var panel = new StackPanel { VerticalAlignment = design == CardSizing.Design.Small ? VerticalAlignment.Center : VerticalAlignment.Top };
            if (design == CardSizing.Design.Small && height < CardSizing.CalendarNextHeight(textScale) - 21)
            {
                // A short card: the countdown and start on one line, the title under it.
                var line = names.Add(Text(CalendarText.Countdown(next, utcNow) + "  \u00b7  " + CalendarText.Start(next, TimeZoneInfo.Local, use24), "SmallSize", "Accent"), "CalendarCountdown");
                line.FontWeight = FontWeights.SemiBold; panel.Children.Add(line);
                var name = names.Add(Text(next.Title, "BodySize", "Text"), "CalendarNextTitle"); name.Margin = new Thickness(0, 4, 0, 0); name.ToolTip = next.Title; panel.Children.Add(name);
                return panel;
            }
            var day = names.Add(Text(CalendarText.Day(next, TimeZoneInfo.Local, utcNow).ToUpperInvariant() + "  ·  " + CalendarText.When(next, TimeZoneInfo.Local, use24), "SmallSize", "Accent"), "CalendarNextWhen");
            day.FontWeight = FontWeights.SemiBold; panel.Children.Add(day);
            var title = names.Add(Text(next.Title, "TitleSize", "Text"), "CalendarNextTitle"); title.Margin = new Thickness(0, 4, 0, 2); title.ToolTip = next.Title + (next.Location.Length > 0 ? "\n" + next.Location : "");
            panel.Children.Add(title);
            var countdown = names.Add(Text(CalendarText.Countdown(next, utcNow) + (next.Location.Length > 0 ? "  ·  " + next.Location : ""), "SmallSize", "Muted"), "CalendarCountdown");
            panel.Children.Add(countdown);
            if (design == CardSizing.Design.Small) return panel;
            // The events after the next one, as many as fit at 30 units a row.
            var rest = current.Where(e => e != next).ToList();
            int rows = Math.Max(0, Math.Min(rest.Count, (int)Math.Floor((height - CardSizing.CalendarNextHeight(textScale)) / CardSizing.CalendarRow)));
            if (rows > 0)
            {
                var line = new Border { Height = 1, Margin = new Thickness(0, 12, 0, 8) }; line.SetResourceReference(Border.BackgroundProperty, "ControlBorder"); panel.Children.Add(line);
                for (int i = 0; i < rows; i++)
                {
                    var e = rest[i];
                    var row = Columns(Auto, new GridLength(CardSizing.CalendarTimeColumn(use24)), Star); row.Height = CardSizing.CalendarRow - 4; row.Margin = new Thickness(0, 0, 0, 4);
                    row.Children.Add(feedCount > 1 ? Dot(e.Feed) : new Border());
                    var when = Text(ShortWhen(e, utcNow), "SmallSize", "Muted"); when.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(At(when, 0, 1));
                    var name = names.Add(Text(e.Title, "SmallSize", "Text"), "CalendarRow" + i); name.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(At(name, 0, 2));
                    panel.Children.Add(row);
                }
            }
            return panel;
        }
        // "2:30 PM" today, "Tue 2:30 PM" later this week, "Nov 12" further on, "All day" for all-day events today.
        private string ShortWhen(CalendarEvent e, DateTime utcNow)
        {
            string day = CalendarText.Day(e, TimeZoneInfo.Local, utcNow);
            var local = TimeZoneInfo.ConvertTimeFromUtc(e.StartUtc, TimeZoneInfo.Local);
            if (day == "Today") return CalendarText.Start(e, TimeZoneInfo.Local, use24);
            if (day == "Tomorrow") return "Tmrw " + (e.AllDay ? "" : CalendarText.Start(e, TimeZoneInfo.Local, use24));
            return (e.AllDay ? e.Date : local).ToString((e.AllDay ? e.Date : local) - DateTime.Now < TimeSpan.FromDays(6) ? "ddd" : "MMM d", System.Globalization.CultureInfo.InvariantCulture) + (e.AllDay ? "" : " " + CalendarText.Start(e, TimeZoneInfo.Local, use24));
        }

        // Large: the agenda for the next two weeks, grouped by day, scrolling inside the card.
        private FrameworkElement Agenda(List<CalendarEvent> current, DateTime utcNow, double height)
        {
            var panel = new StackPanel();
            var next = CalendarText.Next(current, utcNow);
            var head = names.Add(Text(next.Title + "  ·  " + CalendarText.Countdown(next, utcNow), "BodySize", "Accent"), "CalendarCountdown"); head.FontWeight = FontWeights.SemiBold; head.Margin = new Thickness(0, 0, 0, 8);
            panel.Children.Add(head);
            string lastDay = null; int index = 0;
            foreach (var e in current)
            {
                string day = CalendarText.Day(e, TimeZoneInfo.Local, utcNow);
                if (day != lastDay)
                {
                    var label = Label(day.ToUpperInvariant()); label.Margin = new Thickness(0, lastDay == null ? 0 : 12, 0, 6); panel.Children.Add(label);
                    lastDay = day;
                }
                var row = Columns(Auto, new GridLength(CardSizing.CalendarTimeColumn(use24) + 8), Star); row.Margin = new Thickness(0, 0, 0, 8);
                row.Children.Add(feedCount > 1 ? Dot(e.Feed) : new Border());
                var when = Text(CalendarText.Start(e, TimeZoneInfo.Local, use24), "SmallSize", e == next ? "Accent" : "Muted"); when.VerticalAlignment = VerticalAlignment.Top; row.Children.Add(At(when, 0, 1));
                var text = new StackPanel();
                var name = names.Add(Text(e.Title, "BodySize", "Text"), "CalendarRow" + index); name.ToolTip = e.Title; text.Children.Add(name);
                if (e.Location.Length > 0) text.Children.Add(Caption(e.Location));
                row.Children.Add(At(text, 0, 2));
                panel.Children.Add(row);
                index++;
            }
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
        }
    }
}
