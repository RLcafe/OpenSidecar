using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // Prayer times in three designs that follow the card's size (CardSizing.Prayer; docs/design/prayer-times.md),
    // with the day's sun path (Sticker Sheet phase 3, docs/design/sticker-sheet.md): an arc from Fajr to Isha with each
    // prayer placed by its time and the sun at now. Small is the next prayer with its countdown; Medium puts it (over the
    // sun path when there's room, else a progress line) beside today's five times; Large sets the sun path beside the
    // next prayer, Hijri, Method and Alert chips under them, and today's times (with sunrise when there's room) as rows,
    // or as tiles across a wide card. The phone's PrayerWidget.java draws the same three.
    public sealed class PrayerCard : CardParts
    {
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private Rect bounds = Rect.Empty;
        private string built = "", rendered = "";
        private CardSizing.Design design;
        private bool use24, located;
        private string alert = "Off";
        private TextBlock nextName, nextTime, countdown, hijri, method, alertChip;
        private MeterBar progress;
        private PrayerPath path;
        private readonly List<TextBlock[]> rows = new List<TextBlock[]>();

        public PrayerCard(FrameworkElement scope, Grid host, TextBlock status) { this.host = host; this.status = status; names = new PartNames(scope); }

        public CardSizing.Design Current { get { return design; } }

        public void Fit(Rect card) { bounds = card; Rebuild(); }
        // Whether there's a city, the clock's choice, and the alert chip's words (PrayerService.AlertText).
        public void Configure(bool located, bool use24, string alert = "Off") { this.located = located; this.use24 = use24; this.alert = alert ?? "Off"; rendered = ""; Rebuild(); }

        private void Rebuild()
        {
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + located + "|" + use24;
            if (key != built) { built = key; Build(); rendered = ""; }
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); rows.Clear();
            nextName = nextTime = countdown = hijri = method = alertChip = null; progress = null; path = null;
            // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36);
            design = CardSizing.Prayer(bounds.Width, bounds.Height);
            FrameworkElement view;
            if (!located)
            {
                var empty = Caption("Choose a city in Settings → Prayer, or set the weather city.");
                empty.TextWrapping = TextWrapping.Wrap; empty.VerticalAlignment = VerticalAlignment.Center;
                view = empty;
            }
            else if (design == CardSizing.Design.Small) view = Next(width, height, CardSizing.PrayerTime(width, height), false);
            else if (design == CardSizing.Design.Medium)
            {
                var grid = Columns(new GridLength(CardSizing.PrayerNextColumn(width)), new GridLength(20), Star);
                grid.Children.Add(Next(CardSizing.PrayerNextColumn(width), height, CardSizing.PrayerTime(CardSizing.PrayerNextColumn(width), height), true));
                grid.Children.Add(At(List(height, false), 0, 2));
                view = grid;
            }
            else
            {
                bool across = CardSizing.PrayerTilesAcross(width);
                bool rail = CardSizing.PrayerChips(height);
                double chips = rail ? 57 + 12 : 0, top = CardSizing.PrayerPathTop(height - chips, across);
                var grid = Rows(new GridLength(top), new GridLength(12), Auto, new GridLength(12), Star);
                var upper = Columns(Star, Star);
                upper.Children.Add(Next(width / 2, top, CardSizing.PrayerTime(width * 0.46, top), false));
                path = names.Add(new PrayerPath { Height = Math.Min(top, Math.Round(width / 4 + 12)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }, "PrayerPath");
                upper.Children.Add(At(path, 0, 1));
                grid.Children.Add(upper);
                if (rail) grid.Children.Add(At(Chips(), 2));
                double rest = height - top - chips - 12;
                grid.Children.Add(At(across ? Tiles(width) : List(rest, CardSizing.PrayerSunriseRow(rest)), 4));
                view = grid;
            }
            host.Children.Add(names.Add(view, "PrayerView"));
        }

        // The next prayer: its name, time and countdown, and a progress line on the larger designs.
        private FrameworkElement Next(double width, double height, double size, bool line)
        {
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nextName = names.Add(Text("", "BodySize", "Accent"), "PrayerNextName"); nextName.FontWeight = FontWeights.SemiBold;
            nextTime = names.Add(Reading("", size), "PrayerNextTime"); nextTime.Margin = new Thickness(0, 2, 0, 2);
            countdown = names.Add(Text("", "SmallSize", "Muted"), "PrayerCountdown");
            if (design == CardSizing.Design.Small && height < 100)
            {
                // A short card puts the countdown beside the name, over the time.
                var top = Columns(Auto, Star); top.Children.Add(nextName);
                countdown.HorizontalAlignment = HorizontalAlignment.Right; countdown.VerticalAlignment = VerticalAlignment.Center; countdown.Margin = new Thickness(10, 0, 0, 0);
                top.Children.Add(At(countdown, 0, 1));
                panel.Children.Add(top); panel.Children.Add(nextTime);
                return panel;
            }
            // Medium: the sun path under the countdown when it has room (taking what the text leaves, so larger text
            // shrinks it), or else a progress line.
            double room = CardSizing.PrayerPathRoom(height, size);
            if (line && room >= CardSizing.PrayerPathMinimum)
            {
                var stack = Rows(Auto, Auto, Auto, Star); stack.MaxHeight = height;
                stack.Children.Add(nextName); stack.Children.Add(At(nextTime, 1)); stack.Children.Add(At(countdown, 2));
                path = names.Add(new PrayerPath { MaxHeight = 80, Margin = new Thickness(0, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top }, "PrayerPath");
                stack.Children.Add(At(path, 3));
                return stack;
            }
            panel.Children.Add(nextName); panel.Children.Add(nextTime); panel.Children.Add(countdown);
            if (line && height >= 150) { progress = Bar(); progress.Margin = new Thickness(0, 12, 0, 0); progress.Height = 6; panel.Children.Add(progress); }
            return panel;
        }

        // Large: Hijri, Method and Alert as fact-rail chips.
        private FrameworkElement Chips()
        {
            var rail = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 3 };
            string[] captions = { "Hijri", "Method", "Alert" };
            for (int i = 0; i < 3; i++)
            {
                TextBlock value;
                var chip = Stickers.FactChip(captions[i], "", out value);
                chip.Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 2 ? 0 : 4, 0);
                if (i == 0) hijri = names.Add(value, "PrayerHijri"); else if (i == 1) method = names.Add(value, "PrayerMethod"); else alertChip = names.Add(value, "PrayerAlertChip");
                rail.Children.Add(chip);
            }
            return names.Add(rail, "PrayerChips");
        }
        private static MeterBar Bar() { var bar = new MeterBar(); bar.SetResourceReference(MeterBar.TrackProperty, "Track"); bar.SetResourceReference(MeterBar.FillProperty, "Accent"); return bar; }

        // Today's times as rows: the name at the left and the time at the right; Large adds sunrise.
        private FrameworkElement List(double height, bool sunrise)
        {
            // Large spreads the rows over its room; Medium centres them beside the next prayer.
            var list = new Grid { VerticalAlignment = sunrise ? VerticalAlignment.Stretch : VerticalAlignment.Center };
            int count = sunrise ? 6 : 5;
            // Rows keep their text whole: body text from 34 units a row, then the small size, then the 13-unit caption.
            double room = height / count;
            Func<string, TextBlock> text = name => room >= 34 ? Text("", "BodySize", "Text") : room >= 30 ? Text("", "SmallSize", "Text") : Text("", 13.0, "Text");
            for (int i = 0; i < count; i++)
            {
                list.RowDefinitions.Add(new RowDefinition { Height = Star });
                var row = Columns(Star, Auto);
                var name = names.Add(text("name"), "PrayerName" + i); name.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(name);
                var time = names.Add(text("time"), "PrayerTime" + i); time.VerticalAlignment = VerticalAlignment.Center; time.Margin = new Thickness(12, 0, 0, 0);
                System.Windows.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
                row.Children.Add(At(time, 0, 1));
                list.Children.Add(At(row, i));
                rows.Add(new[] { name, time });
            }
            return list;
        }

        // A wide Large card: the five prayers and sunrise as tiles across.
        private FrameworkElement Tiles(double width)
        {
            var grid = new Grid(); double size = CardSizing.PrayerTileTime((width - 50) / 6, use24);
            for (int i = 0; i < 6; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var name = names.Add(Text("", "SmallSize", "Muted"), "PrayerName" + i); content.Children.Add(name);
                var time = names.Add(Reading("", size), "PrayerTime" + i); time.Margin = new Thickness(0, 4, 0, 0); content.Children.Add(time);
                var tile = Tile(content, 14, new Thickness(14, 10, 10, 10)); tile.Margin = new Thickness(i == 0 ? 0 : 5, 0, i == 5 ? 0 : 5, 0);
                grid.Children.Add(At(tile, 0, i));
                rows.Add(new[] { name, time });
            }
            return grid;
        }

        // Fills the design from the schedule now; nothing is set again until something on it changes.
        public void Render(PrayerSchedule schedule, DateTime utcNow, bool alerting)
        {
            if (schedule == null || bounds.IsEmpty || nextName == null) { status.Text = ""; return; }
            var next = schedule.Next(utcNow, TimeZoneInfo.Local);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), TimeZoneInfo.Local);
            var today = schedule.Day(localNow.Date);
            string left = PrayerService.Countdown(next.At - utcNow);
            string key = left + "|" + next.Index + "|" + next.JustStarted + "|" + alerting + "|" + localNow.Date.ToString("yyyyMMdd");
            if (key == rendered) return;
            rendered = key;
            status.Text = alerting || next.JustStarted ? (PrayerService.Names[next.CurrentIndex] + " now").ToUpperInvariant() : schedule.City.Split(',')[0].Trim();
            // A prayer starting now fills the status sticker (docs/design/sticker-sheet.md).
            var sticker = StatusSticker.For(status);
            if (sticker != null) sticker.Filled = alerting || next.JustStarted;
            else status.SetResourceReference(TextBlock.ForegroundProperty, alerting || next.JustStarted ? "Accent" : "Muted");
            nextName.Text = PrayerService.Names[next.Index];
            nextTime.Text = PrayerService.Time(next.At.ToLocalTime(), use24);
            countdown.Text = left;
            if (progress != null) progress.Value = 100 * Math.Max(0, Math.Min(1, (utcNow - next.Previous).TotalMinutes / Math.Max(1, (next.At - next.Previous).TotalMinutes)));
            if (hijri != null) { hijri.Text = PrayerService.Hijri(localNow.Date); method.Text = PrayerService.MethodShort(PrayerService.Method(schedule.MethodId).Name); alertChip.Text = alert; }
            if (path != null)
            {
                DateTime fajr = today.Prayers[0], isha = today.Prayers[4];
                var parts = today.Prayers.Select(t => PrayerService.PathPart(t, fajr, isha)).ToArray();
                int nextToday = Array.IndexOf(today.Prayers, next.At);
                path.Show(parts, PrayerService.PathPart(today.Sunrise, fajr, isha), PrayerService.PathPart(utcNow, fajr, isha), nextToday);
            }
            // Rows: Fajr, (Sunrise), Dhuhr, Asr, Maghrib, Isha; the next one in the accent, past ones muted.
            var entries = new List<Tuple<string, DateTime, int>>();
            for (int i = 0; i < 5; i++)
            {
                entries.Add(Tuple.Create(PrayerService.Names[i], today.Prayers[i], i));
                if (i == 0 && rows.Count == 6) entries.Add(Tuple.Create("Sunrise", today.Sunrise, -1));
            }
            for (int i = 0; i < rows.Count && i < entries.Count; i++)
            {
                var entry = entries[i]; var row = rows[i];
                bool isNext = entry.Item3 == next.Index && entry.Item2 == next.At, isNow = next.JustStarted && entry.Item3 == next.CurrentIndex && entry.Item2 == next.Previous;
                row[0].Text = entry.Item1;
                row[1].Text = entry.Item2 == DateTime.MinValue ? "—" : PrayerService.Time(entry.Item2.ToLocalTime(), use24);
                string brush = isNext || isNow ? "Accent" : entry.Item2 != DateTime.MinValue && entry.Item2 < utcNow || entry.Item3 < 0 ? "Muted" : "Text";
                foreach (var block in row) { block.SetResourceReference(TextBlock.ForegroundProperty, brush); block.FontWeight = isNext ? FontWeights.SemiBold : FontWeights.Normal; }
            }
        }
    }
}
