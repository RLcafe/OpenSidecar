using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WidgetScreen
{
    // What the Claude card shows (decisions 027 and 028): the plan's two limits and tokens per minute.
    public sealed class ClaudeReading
    {
        // A labelled fixture (renders and checks): the card's sticker says SAMPLE.
        public bool Sample;
        public ClaudePlanState Plan = ClaudePlanState.SignedOut;
        public ClaudeLimits Limits;
        public bool LogsFound, LogsScanned;
        public double[] History = new double[ClaudeTokens.Minutes];
        public double PerMinute { get { return ClaudeTokens.Rate(History); } }
        public DateTime Now = DateTime.UtcNow;

        public static ClaudeReading Fixture(DateTime now)
        {
            var history = new double[ClaudeTokens.Minutes];
            for (int i = 0; i < history.Length; i++) history[i] = 4200 + 3100 * Math.Sin(i / 3.0) + (i % 7) * 900 + (i > 24 ? 6000 : 0);
            return new ClaudeReading {
                Sample = true, Plan = ClaudePlanState.Current, LogsFound = true, LogsScanned = true, History = history, Now = now,
                Limits = new ClaudeLimits { UpdatedAt = now, FiveHour = new ClaudeLimitWindow { Percent = 64, ResetsAt = now.AddMinutes(102) }, Week = new ClaudeLimitWindow { Percent = 31, ResetsAt = now.AddDays(3).AddHours(5) } } };
        }
    }

    public static class ClaudeText
    {
        // The big reading and the line under it for a plan tile.
        public static string Percent(ClaudeReading r, bool week)
        {
            if (r.Limits == null) return r.Plan == ClaudePlanState.Reading ? "…" : "—";
            var window = week ? r.Limits.Week : r.Limits.FiveHour;
            double pct = ClaudePlan.Percent(window, r.Now);
            return Double.IsNaN(pct) ? "—" : Math.Round(pct).ToString(CultureInfo.InvariantCulture) + "%";
        }
        public static string PlanNote(ClaudeReading r, bool week, bool use24)
        {
            switch (r.Plan)
            {
                case ClaudePlanState.SignedOut: return "Sign in: Settings → Claude";
                case ClaudePlanState.Expired: return "Sign in again: Settings → Claude";
                case ClaudePlanState.NoMeters: return "No limits on this plan";
                case ClaudePlanState.Reading: return "Checking…";
            }
            if (r.Limits == null) return "Couldn't check";
            if (r.Plan == ClaudePlanState.Failed) return "As of " + Clock(r.Limits.UpdatedAt, use24);
            var window = week ? r.Limits.Week : r.Limits.FiveHour;
            if (window == null) return week ? "No weekly limit" : "No 5-hour limit";
            if (!window.ResetsAt.HasValue) return "";
            if (window.ResetsAt.Value <= r.Now) return "Reset";
            return "Resets " + When(window.ResetsAt.Value, r.Now, use24);
        }
        // "on pace for 92%", or nothing early in a window.
        public static string Pace(double projection) { return Double.IsNaN(projection) ? "" : "on pace for " + Math.Round(projection).ToString(CultureInfo.InvariantCulture) + "%"; }
        // The week's lap counter: "LAP 5 OF 7".
        public static string Lap(int lap) { return lap < 0 ? "" : "LAP " + (lap + 1) + " OF 7"; }
        // The day names under the laps, from the window's start (the week before the reset), or the week to today.
        public static string[] LapDays(ClaudeLimitWindow week, DateTime nowUtc)
        {
            var days = new string[ClaudePace.Laps];
            DateTime start = week != null && week.ResetsAt.HasValue ? week.ResetsAt.Value - ClaudePace.Week : nowUtc.AddDays(-6);
            for (int i = 0; i < days.Length; i++) days[i] = start.AddDays(i).ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture);
            return days;
        }
        public static string TokensValue(ClaudeReading r) { return !r.LogsScanned ? "…" : !r.LogsFound ? "—" : ClaudeTokens.Format(r.PerMinute); }
        public static string TokensNote(ClaudeReading r) { return !r.LogsScanned ? "Reading Claude Code's logs…" : !r.LogsFound ? "No Claude Code logs on this PC" : "Last " + ClaudeTokens.RateMinutes + " minutes"; }

        // "3:40 PM" today, "Thu 9 AM" later (24-hour: "15:40", "Thu 09:00").
        public static string When(DateTime utc, DateTime nowUtc, bool use24)
        {
            var local = utc.ToLocalTime(); var today = nowUtc.ToLocalTime().Date;
            return (local.Date == today ? "" : local.ToString("ddd", CultureInfo.InvariantCulture) + " ") + Clock(utc, use24);
        }
        public static string Clock(DateTime utc, bool use24)
        {
            var local = utc.ToLocalTime();
            if (use24) return local.ToString("HH:mm", CultureInfo.InvariantCulture);
            return local.ToString(local.Minute == 0 ? "h tt" : "h:mm tt", CultureInfo.InvariantCulture);
        }
    }

    // The Claude card (decisions 027 and 028, docs/design/claude-card.md): three tiles, the 5-hour use as a needle
    // gauge, the week as seven laps, and tokens per minute as a needle gauge over its burn line, laid out by
    // CardSizing.ClaudeArrange (one row, the two plan tiles over the tokens, or one column) and rebuilt only when the
    // card's size or text size changes. The phone's ClaudeWidget draws the same.
    public sealed class ClaudeCard : CardParts
    {
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private Rect bounds = Rect.Empty;
        private string built = "";
        private bool use24;
        private ClaudeReading reading = new ClaudeReading { Plan = ClaudePlanState.Reading };
        private TextBlock fiveValue, fiveNote, fivePace, weekValue, weekLap, weekNote, weekPace, tokensValue, tokensNote;
        private ClaudeGauge fiveGauge, tokensGauge;
        private ClaudeLaps laps;
        private ClaudeBurn burn;
        private double labelHeight;

        public ClaudeCard(FrameworkElement scope, Grid host, TextBlock status) { this.host = host; this.status = status; names = new PartNames(scope); }

        public ClaudeLayout Arrangement { get; private set; }
        public ClaudeReading Current { get { return reading; } }

        public void Fit(Rect card) { bounds = card; Rebuild(); }
        public void Configure(bool use24Hour) { use24 = use24Hour; Rebuild(); }
        public void Update(ClaudeReading next) { reading = next ?? new ClaudeReading(); Render(); }

        private double SmallSize { get { object size = host.TryFindResource("SmallSize"); return size is double ? (double)size : 15; } }

        private void Rebuild()
        {
            if (bounds.IsEmpty) { Render(); return; }
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "@" + SmallSize.ToString(CultureInfo.InvariantCulture);
            if (key != built) { built = key; Build(); }
            Render();
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear();
            labelHeight = Math.Ceiling(SmallSize * CardSizing.ClaudeLineHeight) + 2;
            // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36), gap = CardSizing.ClaudeGap;
            Arrangement = CardSizing.ClaudeArrange(width, height);
            int columns = CardSizing.ClaudeGridColumns(Arrangement), rows = CardSizing.ClaudeGridRows(Arrangement);
            double tileWidth = (width - gap * (columns - 1)) / columns, tileHeight = (height - gap * (rows - 1)) / rows;
            var grid = new Grid();
            for (int c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
            for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = Star });
            // Over the two plan tiles, the tokens tile takes the whole width.
            bool wide = Arrangement == ClaudeLayout.TwoOverOne;
            var tiles = new FrameworkElement[] { FiveHourTile(tileWidth, tileHeight), WeekTile(tileWidth, tileHeight), TokensTile(wide ? width : tileWidth, tileHeight) };
            for (int i = 0; i < tiles.Length; i++)
            {
                int row = i / columns, column = i % columns, span = wide && i == 2 ? 2 : 1;
                tiles[i].Margin = new Thickness(column == 0 ? 0 : gap / 2, row == 0 ? 0 : gap / 2, column + span == columns ? 0 : gap / 2, row == rows - 1 ? 0 : gap / 2);
                grid.Children.Add(At(tiles[i], row, column, span));
            }
            host.Children.Add(names.Add(grid, "ClaudeTiles"));
        }

        private static TextBlock TileLabel(string text) { var label = Label(text); label.TextTrimming = TextTrimming.CharacterEllipsis; label.Margin = new Thickness(0, 0, 0, 2); return label; }
        private static TextBlock Line(string id, PartNames names)
        {
            var line = names.Add(Caption(""), id); line.TextTrimming = TextTrimming.CharacterEllipsis;
            return line;
        }

        // A gauge tile: its name over the gauge, with the reading and the lines under it beside the gauge in a wide tile
        // and under it otherwise. The reading shrinks, and lines step aside, to fit (CardSizing.ClaudeFit).
        private Border GaugeTile(string name, double w, double h, ClaudeGauge gauge, TextBlock value, FrameworkElement[] lines, double[] heights, string id)
        {
            double pad = CardSizing.ClaudePadding(w, h), inner = Math.Max(1, w - 2 * pad), body = Math.Max(1, h - 2 * pad - labelHeight);
            bool beside = CardSizing.ClaudeGaugeBeside(inner, body);
            double gaugeWidth = CardSizing.ClaudeGaugeWidth(inner, body), textWidth = inner - gaugeWidth - CardSizing.ClaudeGaugeGap;
            double size = beside ? Math.Min(CardSizing.ClaudeValueSize(w, h), Math.Floor(textWidth / 2.3)) : CardSizing.ClaudeValueSize(w, h);
            int shown = CardSizing.ClaudeFit(beside ? body : body * CardSizing.ClaudeTextShare, heights, ref size);
            value.FontSize = size;
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(value);
            for (int i = 0; i < lines.Length; i++)
            {
                if (i >= shown) continue;
                if (!beside && lines[i] is TextBlock) ((TextBlock)lines[i]).TextAlignment = TextAlignment.Center;
                text.Children.Add(lines[i]);
            }
            if (!beside) value.TextAlignment = TextAlignment.Center;
            var layout = Rows(Auto, Star);
            layout.Children.Add(TileLabel(name));
            Grid content;
            if (beside)
            {
                content = Columns(new GridLength(gaugeWidth), new GridLength(CardSizing.ClaudeGaugeGap), Star);
                content.Children.Add(gauge); content.Children.Add(At(text, 0, 2));
            }
            else { content = Rows(Star, Auto); content.Children.Add(gauge); content.Children.Add(At(text, 1)); }
            layout.Children.Add(At(content, 1));
            return names.Add(Tile(layout, 14, new Thickness(pad)), id + "Tile");
        }

        // The 5-hour use: a needle gauge with the pace tick and the projection, the reading, when it resets and the pace.
        private Border FiveHourTile(double w, double h)
        {
            fiveGauge = names.Add(new ClaudeGauge(Stickers.InkKey("Claude")), "ClaudeFiveGauge");
            fiveValue = names.Add(Reading("—", 32), "ClaudeFiveValue");
            fiveNote = Line("ClaudeFiveNote", names); fivePace = Line("ClaudeFivePace", names);
            return GaugeTile("5-HOUR", w, h, fiveGauge, fiveValue, new FrameworkElement[] { fiveNote, fivePace }, new[] { CardSizing.ClaudeLine, CardSizing.ClaudeLine }, "ClaudeFive");
        }

        // Tokens per minute: a needle gauge with a redline, the last five minutes' rate, and the half hour's burn line.
        private Border TokensTile(double w, double h)
        {
            tokensGauge = names.Add(new ClaudeGauge(Stickers.InkKey("Claude")) { Red = ClaudePace.Redline }, "ClaudeTokensGauge");
            tokensValue = names.Add(Reading("—", 32), "ClaudeTokensValue");
            burn = names.Add(new ClaudeBurn(Stickers.InkKey("Claude")) { Height = CardSizing.ClaudeBurnHeight, Margin = new Thickness(0, 2, 0, 4) }, "ClaudeBurn");
            burn.ToolTip = "Tokens a minute, the last " + ClaudeTokens.Minutes + " minutes";
            tokensNote = Line("ClaudeTokensNote", names);
            return GaugeTile("TOKENS / MIN", w, h, tokensGauge, tokensValue, new FrameworkElement[] { burn, tokensNote }, new[] { CardSizing.ClaudeBurnRoom, CardSizing.ClaudeLine }, "ClaudeTokens");
        }

        // The week as a race: the lap it's on, the reading with the pace and the reset beside it, and seven laps, one a
        // day, with the projection hatched and a flag at the reset.
        private Border WeekTile(double w, double h)
        {
            double pad = CardSizing.ClaudePadding(w, h), inner = Math.Max(1, w - 2 * pad), body = Math.Max(1, h - 2 * pad - labelHeight);
            double lapsHeight = CardSizing.ClaudeLapsHeight(body), room = Math.Max(1, body - lapsHeight - CardSizing.ClaudeLapsGap);
            double size = Math.Max(18, Math.Min(CardSizing.ClaudeValueSize(w, h), Math.Floor(room / CardSizing.ClaudeLineHeight)));
            int notes = CardSizing.ClaudeWeekNotes(inner, room, size);
            var top = new DockPanel { LastChildFill = true };
            weekLap = names.Add(Caption(""), "ClaudeWeekLap"); weekLap.Margin = new Thickness(8, 0, 0, 2); weekLap.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(weekLap, Dock.Right); top.Children.Add(weekLap);
            top.Children.Add(TileLabel("WEEKLY"));
            var middle = Columns(Auto, Star); middle.VerticalAlignment = VerticalAlignment.Center;
            weekValue = names.Add(Reading("—", size), "ClaudeWeekValue"); weekValue.VerticalAlignment = VerticalAlignment.Center;
            middle.Children.Add(weekValue);
            var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            weekNote = Line("ClaudeWeekNote", names); weekPace = Line("ClaudeWeekPace", names);
            weekNote.TextAlignment = TextAlignment.Right; weekPace.TextAlignment = TextAlignment.Right;
            if (notes >= 1) lines.Children.Add(weekPace);
            if (notes >= 2) lines.Children.Add(weekNote);
            middle.Children.Add(At(lines, 0, 1));
            laps = names.Add(new ClaudeLaps(Stickers.InkKey("Claude")) { Height = lapsHeight, Margin = new Thickness(0, CardSizing.ClaudeLapsGap, 0, 0) }, "ClaudeLaps");
            var layout = Rows(Auto, Star, Auto);
            layout.Children.Add(top); layout.Children.Add(At(middle, 1)); layout.Children.Add(At(laps, 2));
            return names.Add(Tile(layout, 14, new Thickness(pad)), "ClaudeWeekTile");
        }

        private void Render()
        {
            var r = reading;
            status.Text = r.Sample ? "SAMPLE" : "";
            if (fiveValue == null) return;
            bool stale = r.Plan == ClaudePlanState.Failed || r.Plan == ClaudePlanState.Expired;
            var five = r.Limits == null ? null : r.Limits.FiveHour;
            var week = r.Limits == null ? null : r.Limits.Week;

            double pct = ClaudePlan.Percent(five, r.Now), elapsed = ClaudePace.Elapsed(five, ClaudePace.FiveHours, r.Now), projection = ClaudePace.Projection(pct, elapsed);
            fiveGauge.Value = Double.IsNaN(pct) ? 0 : pct / 100; fiveGauge.Projection = Double.IsNaN(projection) ? Double.NaN : projection / 100;
            fiveGauge.Pace = elapsed; fiveGauge.Warn = pct >= 90; fiveGauge.InvalidateVisual();
            fiveValue.Text = ClaudeText.Percent(r, false); fiveValue.SetResourceReference(TextBlock.ForegroundProperty, pct >= 90 ? "Warning" : "Text");
            fiveNote.Text = ClaudeText.PlanNote(r, false, use24);
            Pace(fivePace, projection);
            Stickers.Stale(fiveGauge, stale); Stickers.Stale(fiveValue, stale);

            pct = ClaudePlan.Percent(week, r.Now); elapsed = ClaudePace.Elapsed(week, ClaudePace.Week, r.Now); projection = ClaudePace.Projection(pct, elapsed);
            laps.Percent = pct; laps.Projection = projection; laps.Now = ClaudePace.LapNow(elapsed); laps.Warn = pct >= 90; laps.Days = ClaudeText.LapDays(week, r.Now); laps.InvalidateVisual();
            weekLap.Text = ClaudeText.Lap(laps.Now);
            weekValue.Text = ClaudeText.Percent(r, true); weekValue.SetResourceReference(TextBlock.ForegroundProperty, pct >= 90 ? "Warning" : "Text");
            weekNote.Text = ClaudeText.PlanNote(r, true, use24);
            Pace(weekPace, projection);
            // Signed out, or with nothing to pace, the note says why where the pace would be.
            if (weekPace.Text.Length == 0) { weekPace.Text = weekNote.Text; weekNote.Text = ""; }
            Stickers.Stale(laps, stale); Stickers.Stale(weekValue, stale);

            double scale = ClaudePace.Scale(r.History == null || r.History.Length == 0 ? 0 : r.History.Max()), rate = r.PerMinute;
            bool counted = r.LogsScanned && r.LogsFound;
            tokensGauge.Value = counted ? Math.Min(1, rate / scale) : 0; tokensGauge.High = ClaudeTokens.Format(scale); tokensGauge.InvalidateVisual();
            tokensValue.Text = ClaudeText.TokensValue(r); tokensValue.SetResourceReference(TextBlock.ForegroundProperty, counted && rate >= scale * ClaudePace.Redline ? "Warning" : "Text");
            tokensNote.Text = ClaudeText.TokensNote(r);
            burn.Values = counted ? r.History : new double[0]; burn.Maximum = scale; burn.InvalidateVisual();

        }

        private static void Pace(TextBlock line, double projection)
        {
            line.Text = ClaudeText.Pace(projection);
            line.SetResourceReference(TextBlock.ForegroundProperty, ClaudePace.Warns(projection) ? "Warning" : "Muted");
        }
    }
}
