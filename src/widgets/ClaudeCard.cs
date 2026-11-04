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
    // What the Claude card shows (decision 027): the plan's two limits, tokens per minute, and the session counts.
    public sealed class ClaudeReading
    {
        // A labelled fixture (renders and checks): the card's sticker says SAMPLE.
        public bool Sample;
        public ClaudePlanState Plan = ClaudePlanState.SignedOut;
        public ClaudeLimits Limits;
        public bool LogsFound, LogsScanned;
        public double[] History = new double[ClaudeTokens.Minutes];
        public double PerMinute { get { return ClaudeTokens.Rate(History); } }
        public ClaudeCounts Counts = new ClaudeCounts();
        public DateTime Now = DateTime.UtcNow;

        public static ClaudeReading Fixture(DateTime now, bool needsYou)
        {
            var history = new double[ClaudeTokens.Minutes];
            for (int i = 0; i < history.Length; i++) history[i] = 4200 + 3100 * Math.Sin(i / 3.0) + (i % 7) * 900 + (i > 24 ? 6000 : 0);
            return new ClaudeReading {
                Sample = true, Plan = ClaudePlanState.Current, LogsFound = true, LogsScanned = true, History = history, Now = now,
                Limits = new ClaudeLimits { UpdatedAt = now, FiveHour = new ClaudeLimitWindow { Percent = 64, ResetsAt = now.AddMinutes(102) }, Week = new ClaudeLimitWindow { Percent = 31, ResetsAt = now.AddDays(3).AddHours(5) } },
                Counts = needsYou ? new ClaudeCounts { Done = 2, NeedsYou = 3, Blocked = 1, Working = 2 } : new ClaudeCounts { Done = 2, Working = 1 } };
        }
    }

    // The mascot's face: the most urgent count wins.
    public enum ClaudeMood { Sleeping, Done, Working, Blocked, NeedsYou }

    public static class ClaudeText
    {
        public static readonly ClaudeStatus[] Order = { ClaudeStatus.NeedsYou, ClaudeStatus.Blocked, ClaudeStatus.Done, ClaudeStatus.Working };

        public static ClaudeMood Mood(ClaudeCounts c)
        {
            if (c == null) return ClaudeMood.Sleeping;
            return c.NeedsYou > 0 ? ClaudeMood.NeedsYou : c.Blocked > 0 ? ClaudeMood.Blocked : c.Working > 0 ? ClaudeMood.Working : c.Done > 0 ? ClaudeMood.Done : ClaudeMood.Sleeping;
        }
        public static string Words(ClaudeStatus status, int n)
        {
            switch (status)
            {
                case ClaudeStatus.NeedsYou: return n == 1 ? "needs you" : "need you";
                case ClaudeStatus.Blocked: return "blocked";
                case ClaudeStatus.Working: return "working";
                default: return "done";
            }
        }
        // The ink each count wears: green done, orange waiting on you, coral blocked, blue working.
        public static string Brush(ClaudeStatus status)
        {
            return status == ClaudeStatus.NeedsYou ? "Warning" : status == ClaudeStatus.Blocked ? "InkCoral" : status == ClaudeStatus.Working ? "InkGlacier" : "InkMint";
        }
        public static string MoodBrush(ClaudeMood mood)
        {
            return mood == ClaudeMood.NeedsYou ? "Warning" : mood == ClaudeMood.Blocked ? "InkCoral" : mood == ClaudeMood.Working ? "InkGlacier" : mood == ClaudeMood.Done ? "InkMint" : "Muted";
        }
        public static string Summary(ClaudeCounts c)
        {
            if (c == null || c.Total == 0) return "No Claude Code sessions";
            return String.Join(", ", Order.Where(s => c.Of(s) > 0).Select(s => c.Of(s) + " " + Words(s, c.Of(s))));
        }

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

    // The Claude mascot: an original pixel robot (16 × 16, drawn in code, no image) in the card's ink, with its antenna
    // light and eyes in the colour of the most urgent count. Sleeping (nothing open) it closes its eyes and dims; it
    // never animates.
    public sealed class ClaudeMascot : FrameworkElement
    {
        // '#' body (the ink), 'v' visor, 'L' the antenna light. The phone's ClaudeMascot draws the same rows.
        public static readonly string[] Sprite = {
            "......LLLL......",
            ".......##.......",
            "..############..",
            ".##############.",
            ".##vvvvvvvvvv##.",
            "###vvvvvvvvvv###",
            "###vvvvvvvvvv###",
            ".##vvvvvvvvvv##.",
            ".##############.",
            "..############..",
            ".....######.....",
            "..############..",
            ".#.####vv####.#.",
            ".#.####vv####.#.",
            "...##########...",
            "...###....###..." };
        // Each eye, three pixels wide and four tall, set into the visor at columns 4 and 9, rows 4 to 7.
        public static string[] Eye(ClaudeMood mood)
        {
            switch (mood)
            {
                case ClaudeMood.NeedsYou: return new[] { "eee", "e.e", "eee", "..." };
                case ClaudeMood.Blocked: return new[] { "e.e", ".e.", "e.e", "..." };
                case ClaudeMood.Working: return new[] { "...", ".ee", ".ee", "..." };
                case ClaudeMood.Done: return new[] { "...", ".e.", "e.e", "..." };
                default: return new[] { "...", "...", "eee", "..." };
            }
        }
        public static readonly int[] EyeColumns = { 4, 9 };
        public const int EyeRow = 4, Size = 16;

        public static readonly DependencyProperty MoodProperty = DependencyProperty.Register("Mood", typeof(ClaudeMood), typeof(ClaudeMascot), new FrameworkPropertyMetadata(ClaudeMood.Sleeping, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty BodyProperty = DependencyProperty.Register("Body", typeof(Brush), typeof(ClaudeMascot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty VisorProperty = DependencyProperty.Register("Visor", typeof(Brush), typeof(ClaudeMascot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LightProperty = DependencyProperty.Register("Light", typeof(Brush), typeof(ClaudeMascot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public ClaudeMood Mood { get { return (ClaudeMood)GetValue(MoodProperty); } set { SetValue(MoodProperty, value); } }
        public Brush Body { get { return (Brush)GetValue(BodyProperty); } set { SetValue(BodyProperty, value); } }
        public Brush Visor { get { return (Brush)GetValue(VisorProperty); } set { SetValue(VisorProperty, value); } }
        public Brush Light { get { return (Brush)GetValue(LightProperty); } set { SetValue(LightProperty, value); } }

        public ClaudeMascot() { SnapsToDevicePixels = true; RenderOptions.SetEdgeMode(this, EdgeMode.Aliased); }

        protected override void OnRender(DrawingContext context)
        {
            double cell = Math.Floor(Math.Min(ActualWidth, ActualHeight) / Size);
            if (cell < 1) cell = Math.Min(ActualWidth, ActualHeight) / Size;
            if (cell <= 0) return;
            double left = Math.Round((ActualWidth - cell * Size) / 2), top = Math.Round((ActualHeight - cell * Size) / 2);
            bool asleep = Mood == ClaudeMood.Sleeping;
            if (asleep) context.PushOpacity(0.55);
            var eye = Eye(Mood);
            for (int row = 0; row < Size; row++)
                for (int col = 0; col < Size; col++)
                {
                    char c = Sprite[row][col];
                    Brush brush = c == '#' ? Body : c == 'v' ? Visor : c == 'L' ? Light : null;
                    if (c == 'v')
                    {
                        foreach (int at in EyeColumns)
                            if (col >= at && col < at + 3 && row >= EyeRow && row < EyeRow + 4 && eye[row - EyeRow][col - at] == 'e') brush = Light;
                        // The chest panel lights up too.
                        if (row >= 12) brush = Light;
                    }
                    if (brush != null) context.DrawRectangle(brush, null, new Rect(left + col * cell, top + row * cell, cell, cell));
                }
            if (asleep) context.Pop();
        }
    }

    // The Claude card (decision 027, docs/design/claude-card.md): four tiles, 5-hour and weekly use of the plan, tokens
    // per minute, and the mascot with the session counts, laid out by CardSizing.ClaudeColumns (one row, a 2 × 2 square
    // or a column) and rebuilt only when the card's size changes. The phone's ClaudeWidget draws the same.
    public sealed class ClaudeCard : CardParts
    {
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private Rect bounds = Rect.Empty;
        private string built = "";
        private bool use24;
        private ClaudeReading reading = new ClaudeReading { Plan = ClaudePlanState.Reading };
        private TextBlock fiveValue, fiveNote, weekValue, weekNote, tokensValue, tokensNote;
        private MeterBar fiveBar, weekBar;
        private HistoryBars history;
        private ClaudeMascot mascot;
        private Panel counts;
        private bool compactCounts;

        public ClaudeCard(FrameworkElement scope, Grid host, TextBlock status) { this.host = host; this.status = status; names = new PartNames(scope); }

        public int TileColumns { get; private set; }
        public ClaudeReading Current { get { return reading; } }
        public ClaudeMascot Mascot { get { return mascot; } }

        public void Fit(Rect card) { bounds = card; Rebuild(); }
        public void Configure(bool use24Hour) { use24 = use24Hour; Render(); }
        public void Update(ClaudeReading next) { reading = next ?? new ClaudeReading(); Render(); }

        private void Rebuild()
        {
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height);
            if (key != built) { built = key; Build(); }
            Render();
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear();
            // The body: the card less its 24-unit padding, the 28-unit heading and the 8-unit gap under it.
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48 - 36), gap = CardSizing.ClaudeGap;
            TileColumns = CardSizing.ClaudeColumns(width, height);
            int rows = 4 / TileColumns;
            double tileWidth = (width - gap * (TileColumns - 1)) / TileColumns, tileHeight = (height - gap * (rows - 1)) / rows;
            var grid = new Grid();
            for (int c = 0; c < TileColumns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
            for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = Star });
            var tiles = new FrameworkElement[] {
                PlanTile("5-HOUR", tileWidth, tileHeight, out fiveValue, out fiveBar, out fiveNote, "ClaudeFive"),
                PlanTile("WEEKLY", tileWidth, tileHeight, out weekValue, out weekBar, out weekNote, "ClaudeWeek"),
                TokensTile(tileWidth, tileHeight),
                MascotTile(tileWidth, tileHeight) };
            for (int i = 0; i < tiles.Length; i++)
            {
                int row = i / TileColumns, column = i % TileColumns;
                tiles[i].Margin = new Thickness(column == 0 ? 0 : gap / 2, row == 0 ? 0 : gap / 2, column == TileColumns - 1 ? 0 : gap / 2, row == rows - 1 ? 0 : gap / 2);
                grid.Children.Add(At(tiles[i], row, column));
            }
            host.Children.Add(names.Add(grid, "ClaudeTiles"));
        }

        private static TextBlock TileLabel(string text) { var label = Label(text); label.TextTrimming = TextTrimming.CharacterEllipsis; return label; }

        // A plan tile: its name, the percentage used, a bar, and when it resets.
        private Border PlanTile(string name, double w, double h, out TextBlock value, out MeterBar bar, out TextBlock note, string id)
        {
            double pad = CardSizing.ClaudePadding(w, h);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(TileLabel(name));
            value = names.Add(Reading("—", CardSizing.ClaudeValueSize(w, h)), id + "Value"); value.Margin = new Thickness(0, 2, 0, 0);
            stack.Children.Add(value);
            bar = names.Add(new MeterBar { Height = h >= 120 ? 8 : 6, Margin = new Thickness(0, 6, 0, 0) }, id + "Bar");
            bar.SetResourceReference(MeterBar.TrackProperty, "Track");
            stack.Children.Add(bar);
            note = names.Add(Caption(""), id + "Note"); note.Margin = new Thickness(0, 6, 0, 0); note.TextTrimming = TextTrimming.CharacterEllipsis;
            if (!CardSizing.ClaudeNote(h)) note.Visibility = Visibility.Collapsed;
            stack.Children.Add(note);
            return Tile(stack, 14, new Thickness(pad));
        }

        // Tokens per minute: the rate over the last five minutes and, in a tall tile, a bar a minute for half an hour.
        private Border TokensTile(double w, double h)
        {
            double pad = CardSizing.ClaudePadding(w, h);
            var layout = Rows(Auto, Auto, Star, Auto);
            layout.Children.Add(TileLabel("TOKENS / MIN"));
            tokensValue = names.Add(Reading("—", CardSizing.ClaudeValueSize(w, h)), "ClaudeTokensValue"); tokensValue.Margin = new Thickness(0, 2, 0, 0);
            layout.Children.Add(At(tokensValue, 1));
            history = null;
            if (CardSizing.ClaudeHistory(h))
            {
                history = names.Add(new HistoryBars { Capacity = ClaudeTokens.Minutes, Maximum = 0, Margin = new Thickness(0, 8, 0, 0), MinHeight = 16 }, "ClaudeHistory");
                history.SetResourceReference(HistoryBars.FillProperty, Stickers.InkKey("Claude")); history.SetResourceReference(HistoryBars.TrackProperty, "Track");
                layout.Children.Add(At(history, 2));
            }
            else layout.VerticalAlignment = VerticalAlignment.Center;
            tokensNote = names.Add(Caption(""), "ClaudeTokensNote"); tokensNote.Margin = new Thickness(0, 6, 0, 0); tokensNote.TextTrimming = TextTrimming.CharacterEllipsis;
            if (!CardSizing.ClaudeNote(h)) tokensNote.Visibility = Visibility.Collapsed;
            layout.Children.Add(At(tokensNote, 3));
            return Tile(layout, 14, new Thickness(pad));
        }

        // The mascot with the counts: beside it in a wide tile (a line each, with words), under it otherwise (a dot and
        // a number each; the words are in the tooltip).
        private Border MascotTile(double w, double h)
        {
            double pad = CardSizing.ClaudePadding(w, h), size = CardSizing.ClaudeMascotSize(w, h);
            bool beside = CardSizing.ClaudeMascotBeside(w, h);
            mascot = names.Add(new ClaudeMascot { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "ClaudeMascot");
            mascot.SetResourceReference(ClaudeMascot.BodyProperty, Stickers.InkKey("Claude")); mascot.SetResourceReference(ClaudeMascot.VisorProperty, "Page");
            compactCounts = !beside;
            counts = names.Add(beside ? (Panel)new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }
                : new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) }, "ClaudeCounts");
            Panel layout;
            if (beside) { var row = Columns(Auto, Star); row.Children.Add(mascot); row.Children.Add(At(counts, 0, 1)); row.HorizontalAlignment = HorizontalAlignment.Center; layout = row; }
            else { var column = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; column.Children.Add(mascot); column.Children.Add(counts); layout = column; }
            return names.Add(Tile(layout, 14, new Thickness(pad)), "ClaudeMascotTile");
        }

        // One count: a dot in its colour, the number, and its words when there's room.
        private FrameworkElement Count(ClaudeStatus kind, int n)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = compactCounts ? new Thickness(5, 2, 5, 2) : new Thickness(0, 2, 0, 2) };
            var dot = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ClaudeText.Brush(kind));
            row.Children.Add(dot);
            var number = Text(n.ToString(CultureInfo.InvariantCulture), "BodySize", ClaudeText.Brush(kind)); number.FontWeight = FontWeights.SemiBold; number.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(number);
            if (!compactCounts) { var words = Text(" " + ClaudeText.Words(kind, n), "SmallSize", "Text"); words.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(words); }
            row.ToolTip = n + " " + ClaudeText.Words(kind, n);
            AutomationProperties.SetName(row, n + " " + ClaudeText.Words(kind, n));
            return row;
        }

        private void Render()
        {
            var r = reading;
            status.Text = r.Sample ? "SAMPLE" : "";
            if (fiveValue == null) return;
            FillPlan(r, false, fiveValue, fiveBar, fiveNote);
            FillPlan(r, true, weekValue, weekBar, weekNote);
            tokensValue.Text = ClaudeText.TokensValue(r); tokensNote.Text = ClaudeText.TokensNote(r);
            if (history != null) history.SetValues(r.History);
            var mood = ClaudeText.Mood(r.Counts);
            mascot.Mood = mood; mascot.SetResourceReference(ClaudeMascot.LightProperty, ClaudeText.MoodBrush(mood));
            AutomationProperties.SetName(mascot, ClaudeText.Summary(r.Counts));
            mascot.ToolTip = ClaudeText.Summary(r.Counts);
            counts.Children.Clear();
            foreach (var kind in ClaudeText.Order) { int n = r.Counts.Of(kind); if (n > 0) counts.Children.Add(Count(kind, n)); }
            if (counts.Children.Count == 0) { var quiet = Caption(r.Counts.Total == 0 ? "All quiet" : ""); quiet.HorizontalAlignment = HorizontalAlignment.Center; counts.Children.Add(quiet); }
        }

        private void FillPlan(ClaudeReading r, bool week, TextBlock value, MeterBar bar, TextBlock note)
        {
            value.Text = ClaudeText.Percent(r, week);
            note.Text = ClaudeText.PlanNote(r, week, use24);
            double pct = r.Limits == null ? Double.NaN : ClaudePlan.Percent(week ? r.Limits.Week : r.Limits.FiveHour, r.Now);
            bar.Value = Double.IsNaN(pct) ? 0 : pct;
            bar.SetResourceReference(MeterBar.FillProperty, !Double.IsNaN(pct) && pct >= 90 ? "Warning" : Stickers.InkKey("Claude"));
            bool stale = r.Plan == ClaudePlanState.Failed || r.Plan == ClaudePlanState.Expired;
            Stickers.Stale(value, stale); Stickers.Stale(bar, stale);
        }
    }
}
