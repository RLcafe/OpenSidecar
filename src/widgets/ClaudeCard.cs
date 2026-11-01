using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace WidgetScreen
{
    // The Claude card's words (docs/design/claude-watcher.md), the same as ClaudeText.java on the phone.
    public static class ClaudeText
    {
        public static string State(ClaudeState state)
        {
            return state == ClaudeState.NeedsYou ? "Needs you" : state == ClaudeState.Working ? "Working" : state == ClaudeState.Done ? "Done" : "Ready";
        }
        // "now", "2 min", "3 h".
        public static string Duration(TimeSpan time)
        {
            if (time < TimeSpan.FromMinutes(1)) return "now";
            if (time < TimeSpan.FromHours(1)) return (int)time.TotalMinutes + " min";
            return (int)time.TotalHours + " h";
        }
        // "Needs you · 2 min" always says how long; the others only when asked (the Large rows).
        public static string StateWords(ClaudeState state, DateTime since, DateTime now, bool withTime)
        {
            return state == ClaudeState.NeedsYou || (withTime && state != ClaudeState.Ready) ? State(state) + " · " + Duration(now - since) : State(state);
        }
        // "3:40 pm" within a day, otherwise the day ("Thu").
        public static string When(DateTime resetsAt, DateTime now, bool use24)
        {
            var culture = CultureInfo.InvariantCulture; var local = resetsAt.ToLocalTime();
            if (resetsAt - now < TimeSpan.FromHours(24)) return use24 ? local.ToString("HH:mm", culture) : local.ToString("h:mm tt", culture).ToLowerInvariant();
            return local.ToString("ddd", culture);
        }
        // Under the meter: "resets 3:40 pm", or "Limit reached · back 3:40 pm" at 100%.
        public static string Reset(ClaudeLimitWindow window, DateTime now, bool use24)
        {
            if (window == null) return "";
            string when = window.ResetsAt.HasValue ? When(window.ResetsAt.Value, now, use24) : "";
            if (window.Percent >= 100) return when.Length == 0 ? "Limit reached" : "Limit reached · back " + when;
            return when.Length == 0 ? "" : "resets " + when;
        }
        // The hook's message, shorter: "Claude needs your permission to use Bash" reads "Wants to use Bash".
        public static string Ask(string message)
        {
            const string permission = "Claude needs your permission to use ";
            if (String.IsNullOrWhiteSpace(message)) return "";
            message = message.Trim();
            return message.StartsWith(permission, StringComparison.OrdinalIgnoreCase) && message.Length > permission.Length ? "Wants to use " + message.Substring(permission.Length) : message;
        }
        // A meter turns Amber from 80%.
        public static bool Warns(double percent) { return percent >= 80; }
        public static double Percent(ClaudeLimitWindow window, DateTime now) { return window == null || !window.Current(now) ? 0 : window.Percent; }
        // The heading: "Claude Code", "api-server needs you" or "2 sessions need you".
        public static string Heading(ClaudeView view)
        {
            var needs = view.Sessions.Where(s => s.State == ClaudeState.NeedsYou).ToList();
            return needs.Count == 0 ? "CLAUDE CODE" : needs.Count == 1 ? needs[0].ProjectName + " needs you" : needs.Count + " sessions need you";
        }
        // The flag at the heading's right: "SAMPLE · 3 RUNNING", "AS OF 10:42" when the meters are old.
        public static string Flag(ClaudeView view, DateTime now, bool use24, bool withAge)
        {
            var parts = new List<string>();
            if (view.Sample) parts.Add("SAMPLE");
            if (view.Sessions.Count > 0) parts.Add(view.Sessions.Count + " RUNNING");
            if (withAge && view.Limits != null && view.Limits.Known && now - view.Limits.UpdatedAt > ClaudeService.Stale) parts.Add("AS OF " + Clock(view.Limits.UpdatedAt, use24).ToUpperInvariant());
            return String.Join(" · ", parts);
        }
        public static string Clock(DateTime time, bool use24) { return time.ToLocalTime().ToString(use24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant(); }
        // The meters' empty states.
        public static string LimitsMissing(ClaudeView view)
        {
            return view.NoPlan ? "Plan limits need a Claude Pro or Max login." : "Plan limits show after Claude Code's next reply.";
        }
        public const string NotConnected = "Connect Claude Code in Settings → Claude.", NoSessions = "No Claude Code sessions running.", NoLogs = "Can't read Claude Code's logs.";
        public static string PeriodName(ClaudePeriod period) { return period == ClaudePeriod.FiveHour ? "5 h" : period == ClaudePeriod.Today ? "Today" : "Week"; }
        // "as of 10:42 · from Claude Code" under the Large panel.
        public static string Source(ClaudeLimits limits, bool use24)
        {
            if (limits == null || !limits.Known) return "";
            return "as of " + Clock(limits.UpdatedAt, use24) + " · " + (limits.Source == "online" ? "checked online" : "from Claude Code");
        }
    }

    // Claude Code in three designs that follow the card's size (CardSizing.Claude; docs/design/claude-watcher.md).
    // Small is the loudest critter with its project and state; Medium a lineup of critters over the two plan meters;
    // Large a row per session beside a panel with the meters, a period switch and usage by project. When a session
    // needs you the card's border turns Amber and the heading names it. Tapping a session brings its window forward.
    // The phone's ClaudeWidget.java draws the same three.
    public sealed class ClaudeCard : CardParts
    {
        private readonly Grid host;
        private readonly Border card;
        private readonly PartNames names;
        private readonly Action<string> focus;
        private ClaudeView view;
        private Rect bounds = Rect.Empty;
        private double textScale = 1;
        private string built = "";
        private CardSizing.Design design;
        private ClaudePeriod period = ClaudePeriod.FiveHour;
        private bool phase, quiet, use24;
        private readonly List<Part> parts = new List<Part>();
        private readonly List<ProjectRow> projects = new List<ProjectRow>();
        private Meter fiveHour, week;
        private TextBlock heading, flag, more, limitsMessage, source, logsMessage;
        private MeterBar smallMeter;
        private readonly List<Button> periodButtons = new List<Button>();

        private sealed class Part { public ClaudeCritter Critter; public TextBlock Title, State, Detail, Tokens; public string Id; }
        private sealed class Meter { public TextBlock Percent, Reset; public SegmentBar Bar; public FrameworkElement Root; }
        private sealed class ProjectRow { public TextBlock Name, Tokens; public MeterBar Share; }

        public ClaudeCard(FrameworkElement scope, Grid host, Border card, Action<string> focus) { this.host = host; this.card = card; this.focus = focus; names = new PartNames(scope); }

        public CardSizing.Design Current { get { return design; } }
        public ClaudeView View { get { return view; } }
        public ClaudePeriod Period { get { return period; } }

        public void Fit(Rect next, double scale) { bounds = next; textScale = scale; Rebuild(); }
        public void Update(ClaudeView next) { view = next; Rebuild(); }
        public void Configure(bool use24Hour) { use24 = use24Hour; Render(); }
        // The heartbeat: critters' pupils and hops, and the "2 min" counts.
        public void Tick(bool nextPhase, bool quietMode)
        {
            phase = nextPhase && !quietMode; quiet = quietMode;
            foreach (var part in parts) if (part.Critter != null) { part.Critter.Quiet = quiet; part.Critter.Phase = phase; }
            Render();
        }
        public void ShowPeriod(ClaudePeriod next) { period = next; Rebuild(); }

        private void Rebuild()
        {
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + textScale + "|" + period + "|" + Shape();
            if (key != built) { built = key; Build(); }
            Render();
        }
        // What decides the design's parts: which sessions, and which areas have something to show.
        private string Shape()
        {
            if (view == null) return "-";
            return view.Connected + "|" + view.Sample + "|" + String.Join(",", view.Sessions.Select(s => s.Id + ":" + s.CanFocus)) + "|" + (view.Limits != null && view.Limits.Known) + view.NoPlan + "|" + view.LogsFailed + "|" + view.UsageFor(period).Count;
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); parts.Clear(); projects.Clear(); periodButtons.Clear();
            fiveHour = week = null; heading = flag = more = limitsMessage = source = logsMessage = null; smallMeter = null;
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48);
            design = CardSizing.Claude(bounds.Width, bounds.Height);
            FrameworkElement content;
            if (view == null) content = Caption("Reading Claude Code…");
            else content = design == CardSizing.Design.Small ? BuildSmall(width, height) : design == CardSizing.Design.Medium ? BuildMedium(width, height) : BuildLarge(width, height);
            host.Children.Add(names.Add(content, "ClaudeView"));
        }

        private TextBlock Wrapped(string text, string size, double maxHeight)
        {
            var block = Text(text, size, "Muted"); block.TextWrapping = TextWrapping.Wrap; block.TextTrimming = TextTrimming.WordEllipsis; block.MaxHeight = Math.Max(18, maxHeight);
            return block;
        }
        private ClaudeCritter Critter(double size, int index)
        {
            var critter = new ClaudeCritter { Width = size, Height = size, Quiet = quiet, Phase = phase };
            return names.Add(critter, "ClaudeCritter" + index);
        }
        // A tappable session: its whole area brings the session's window forward, when one is known.
        private FrameworkElement Tappable(FrameworkElement child, ClaudeSession session)
        {
            var area = new Border { Child = child, Background = System.Windows.Media.Brushes.Transparent };
            if (session != null && session.CanFocus)
            {
                string id = session.Id; area.Cursor = Cursors.Hand;
                area.ToolTip = "Show this session's window";
                AutomationProperties.SetName(area, session.Shown + ". Tap to show its window.");
                area.MouseLeftButtonUp += (sender, e) => { e.Handled = true; focus(id); };
            }
            return area;
        }
        private TextBlock Heading()
        {
            heading = names.Add(Label("CLAUDE CODE"), "ClaudeHeading"); heading.VerticalAlignment = VerticalAlignment.Center;
            return heading;
        }
        private DockPanel HeadingRow()
        {
            var row = new DockPanel { LastChildFill = true };
            flag = names.Add(Caption(""), "ClaudeFlag"); flag.VerticalAlignment = VerticalAlignment.Center; flag.Margin = new Thickness(12, 0, 0, 0);
            DockPanel.SetDock(flag, Dock.Right); row.Children.Add(flag); row.Children.Add(Heading());
            return row;
        }

        // Small: the most urgent session's critter at the left, its project and state at the right, the 5-hour meter below.
        private FrameworkElement BuildSmall(double width, double height)
        {
            bool meter = view.Connected || (view.Limits != null && view.Limits.Known);
            var root = Rows(Star, Auto);
            double room = height - (meter ? 16 : 0), size = CardSizing.ClaudeSmallCritter(width, room);
            var row = Columns(Auto, Star);
            var first = view.Sessions.FirstOrDefault();
            var part = new Part { Id = first == null ? null : first.Id, Critter = Critter(size, 0) };
            parts.Add(part);
            row.Children.Add(Tappable(part.Critter, first));
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            if (first == null)
            {
                // The narrow side column keeps the message whole in the caption size.
                var message = names.Add(Wrapped(view.Connected ? ClaudeText.NoSessions : ClaudeText.NotConnected, "SmallSize", room - (view.Sample ? 22 : 0)), "ClaudeMessage");
                if (width < 300) message.FontSize = 13;
                side.Children.Add(message);
            }
            else
            {
                // A short card gives the project the body size, so its name, state and count stay inside.
                part.Title = names.Add(Text("", room < 150 ? "BodySize" : "TitleSize", "Text"), "ClaudeTitle0"); side.Children.Add(part.Title);
                part.State = names.Add(Text("", "SmallSize", "Muted"), "ClaudeState0"); part.State.Margin = new Thickness(0, 4, 0, 0); side.Children.Add(part.State);
                if (view.Sessions.Count > 1) { more = names.Add(Caption(""), "ClaudeMore"); more.Margin = new Thickness(0, 4, 0, 0); side.Children.Add(more); }
            }
            if (view.Sample) { var sample = names.Add(Caption("SAMPLE"), "ClaudeFlag"); sample.Margin = new Thickness(0, 4, 0, 0); side.Children.Add(sample); }
            row.Children.Add(At(side, 0, 1));
            root.Children.Add(row);
            if (meter)
            {
                smallMeter = names.Add(new MeterBar { Height = 6, Margin = new Thickness(0, 10, 0, 0) }, "ClaudeSmallMeter");
                smallMeter.SetResourceReference(MeterBar.TrackProperty, "Track");
                root.Children.Add(At(smallMeter, 1));
            }
            return root;
        }

        // Medium: the heading, a lineup of critters with their projects, and the two plan meters in a strip.
        private FrameworkElement BuildMedium(double width, double height)
        {
            bool strip = view.Connected || (view.Limits != null && view.Limits.Known);
            double stripHeight = strip ? CardSizing.ClaudeStrip(textScale) + 12 : 0;
            var root = Rows(new GridLength(28), new GridLength(8), Star, Auto);
            root.Children.Add(HeadingRow());
            double rowHeight = height - 36 - stripHeight;
            if (view.Sessions.Count == 0) root.Children.Add(At(Empty(Math.Min(96, Math.Max(40, rowHeight - 8)), rowHeight), 2));
            else
            {
                int columns = CardSizing.ClaudeLineup(width), count = view.Sessions.Count;
                int shown = count <= columns ? count : columns - 1;
                double columnWidth = width / Math.Max(columns, Math.Min(count, columns));
                bool state = CardSizing.ClaudeLineupState(rowHeight, textScale);
                double size = CardSizing.ClaudeLineupCritter(rowHeight, columnWidth, textScale, state);
                var lineup = new Grid { VerticalAlignment = VerticalAlignment.Center };
                for (int c = 0; c < Math.Min(count, columns); c++) lineup.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                for (int i = 0; i < shown; i++)
                {
                    var session = view.Sessions[i];
                    var part = new Part { Id = session.Id, Critter = Critter(size, i) }; parts.Add(part);
                    var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    part.Critter.HorizontalAlignment = HorizontalAlignment.Center; stack.Children.Add(part.Critter);
                    part.Title = names.Add(Text("", "SmallSize", "Text"), "ClaudeTitle" + i); part.Title.HorizontalAlignment = HorizontalAlignment.Center; part.Title.MaxWidth = Math.Max(40, columnWidth - 8); part.Title.Margin = new Thickness(0, 6, 0, 0);
                    stack.Children.Add(part.Title);
                    if (state) { part.State = names.Add(Caption(""), "ClaudeState" + i); part.State.HorizontalAlignment = HorizontalAlignment.Center; part.State.MaxWidth = Math.Max(40, columnWidth - 8); part.State.Margin = new Thickness(0, 2, 0, 0); stack.Children.Add(part.State); }
                    lineup.Children.Add(At(Tappable(stack, session), 0, i));
                }
                if (shown < count)
                {
                    var rest = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    more = names.Add(Reading("+" + (count - shown), 28), "ClaudeMore"); more.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); more.HorizontalAlignment = HorizontalAlignment.Center; rest.Children.Add(more);
                    var label = Caption("more"); label.HorizontalAlignment = HorizontalAlignment.Center; rest.Children.Add(label);
                    lineup.Children.Add(At(rest, 0, shown));
                }
                root.Children.Add(At(lineup, 2));
            }
            if (strip)
            {
                var tile = Tile(Meters(false), 14, new Thickness(14, 10, 14, 10)); tile.Margin = new Thickness(0, 12, 0, 0);
                root.Children.Add(At(names.Add(tile, "ClaudeStrip"), 3));
            }
            return root;
        }

        // A dozing critter and what to do next, for no sessions or no connection.
        private FrameworkElement Empty(double size, double room)
        {
            var row = Columns(Auto, Star); row.VerticalAlignment = VerticalAlignment.Center;
            var part = new Part { Critter = Critter(size, 0) }; parts.Add(part);
            row.Children.Add(part.Critter);
            var text = names.Add(Wrapped(view.Connected ? ClaudeText.NoSessions : ClaudeText.NotConnected, "SmallSize", room), "ClaudeMessage"); text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new Thickness(16, 0, 0, 0);
            row.Children.Add(At(text, 0, 1));
            return row;
        }

        // The two plan meters: side by side in the Medium strip, stacked and bigger in the Large panel.
        private FrameworkElement Meters(bool stacked)
        {
            if (view.Limits == null || !view.Limits.Known)
            {
                limitsMessage = names.Add(Wrapped(ClaudeText.LimitsMissing(view), "SmallSize", stacked ? 80 : CardSizing.ClaudeStrip(textScale) - 20), "ClaudeLimits");
                return limitsMessage;
            }
            fiveHour = BuildMeter("5-HOUR", stacked, "ClaudeFiveHour"); week = BuildMeter("WEEK", stacked, "ClaudeWeek");
            if (stacked)
            {
                var panel = new StackPanel(); week.Root.Margin = new Thickness(0, 14, 0, 0);
                panel.Children.Add(fiveHour.Root); panel.Children.Add(week.Root);
                return panel;
            }
            var grid = Columns(Star, new GridLength(16), Star);
            grid.Children.Add(fiveHour.Root); grid.Children.Add(At(week.Root, 0, 2));
            return grid;
        }
        private Meter BuildMeter(string label, bool big, string name)
        {
            var meter = new Meter();
            var top = new DockPanel();
            meter.Percent = names.Add(big ? Reading("—", 30) : Text("—", "BodySize", "Text"), name + "Percent"); meter.Percent.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(meter.Percent, Dock.Right); top.Children.Add(meter.Percent);
            var title = Label(label); title.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(title);
            meter.Bar = names.Add(new SegmentBar { Height = big ? 14 : 10, Margin = new Thickness(0, 6, 0, 0) }, name + "Bar"); meter.Bar.SetResourceReference(SegmentBar.TrackProperty, "Track");
            meter.Reset = names.Add(Caption(""), name + "Reset"); meter.Reset.Margin = new Thickness(0, 4, 0, 0);
            var stack = new StackPanel(); stack.Children.Add(top); stack.Children.Add(meter.Bar); stack.Children.Add(meter.Reset);
            meter.Root = stack;
            return meter;
        }

        // Large: a row per session at the left, and a panel with the meters, the period switch and usage by project.
        private FrameworkElement BuildLarge(double width, double height)
        {
            var root = Rows(new GridLength(28), new GridLength(12), Star);
            root.Children.Add(HeadingRow());
            double panelWidth = CardSizing.ClaudePanel(width), leftWidth = width - panelWidth - 16, bodyHeight = height - 40;
            var body = Columns(Star, new GridLength(16), new GridLength(panelWidth));
            if (view.Sessions.Count == 0) body.Children.Add(Empty(Math.Min(96, leftWidth * 0.3), bodyHeight));
            else
            {
                double rowHeight = CardSizing.ClaudeRowHeight(textScale);
                int capacity = CardSizing.ClaudeRows(bodyHeight, textScale), count = view.Sessions.Count;
                int shown = count <= capacity ? count : Math.Max(1, capacity - 1);
                var list = new StackPanel();
                for (int i = 0; i < shown; i++)
                {
                    var session = view.Sessions[i];
                    var part = new Part { Id = session.Id, Critter = Critter(56, i) }; parts.Add(part);
                    var row = Columns(new GridLength(68), Star, Auto); row.Height = rowHeight;
                    part.Critter.VerticalAlignment = VerticalAlignment.Center; part.Critter.HorizontalAlignment = HorizontalAlignment.Left;
                    row.Children.Add(part.Critter);
                    var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    part.Title = names.Add(Text("", "BodySize", "Text"), "ClaudeTitle" + i); text.Children.Add(part.Title);
                    var line = new DockPanel { Margin = new Thickness(0, 3, 0, 0) };
                    part.State = names.Add(Text("", "SmallSize", "Muted"), "ClaudeState" + i); DockPanel.SetDock(part.State, Dock.Left); line.Children.Add(part.State);
                    part.Detail = names.Add(Caption(""), "ClaudeDetail" + i); part.Detail.Margin = new Thickness(10, 0, 0, 0); part.Detail.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(part.Detail);
                    text.Children.Add(line);
                    row.Children.Add(At(text, 0, 1));
                    part.Tokens = names.Add(Reading("", 22), "ClaudeTokens" + i); part.Tokens.VerticalAlignment = VerticalAlignment.Center; part.Tokens.Margin = new Thickness(12, 0, 0, 0);
                    row.Children.Add(At(part.Tokens, 0, 2));
                    var tappable = Tappable(row, session); tappable.Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0);
                    list.Children.Add(tappable);
                }
                if (shown < count) { more = names.Add(Caption(""), "ClaudeMore"); more.Margin = new Thickness(0, 10, 0, 0); list.Children.Add(more); }
                body.Children.Add(list);
            }
            body.Children.Add(At(names.Add(Tile(Panel(panelWidth - 32, bodyHeight - 32), 16, new Thickness(16)), "ClaudePanel"), 0, 2));
            root.Children.Add(At(body, 2));
            return root;
        }

        private FrameworkElement Panel(double width, double height)
        {
            var panel = new StackPanel();
            panel.Children.Add(Meters(true));
            var periods = Columns(Star, Star, Star); periods.Margin = new Thickness(0, 14, 0, 0);
            int index = 0;
            foreach (ClaudePeriod choice in new[] { ClaudePeriod.FiveHour, ClaudePeriod.Today, ClaudePeriod.Week })
            {
                var button = new Button { Content = ClaudeText.PeriodName(choice), Padding = new Thickness(4), Margin = new Thickness(index == 0 ? 0 : 3, 0, index == 2 ? 0 : 3, 0), MinWidth = 0 };
                button.SetResourceReference(Control.FontSizeProperty, "SmallSize");
                bool selected = choice == period;
                button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "ControlBorder");
                button.SetResourceReference(Control.BackgroundProperty, selected ? "Selected" : "Raised");
                button.BorderThickness = new Thickness(selected ? 2 : 1);
                AutomationProperties.SetName(button, "Usage for " + (choice == ClaudePeriod.FiveHour ? "the 5-hour window" : choice == ClaudePeriod.Today ? "today" : "the week") + (selected ? ", selected" : ""));
                var picked = choice; button.Click += (sender, e) => ShowPeriod(picked);
                periodButtons.Add(names.Add(button, "ClaudePeriod" + index));
                periods.Children.Add(At(button, 0, index++));
            }
            panel.Children.Add(periods);
            int rows = CardSizing.ClaudeProjects(height, textScale);
            if (view.LogsFailed) { logsMessage = names.Add(Wrapped(ClaudeText.NoLogs, "SmallSize", 60), "ClaudeLogs"); logsMessage.Margin = new Thickness(0, 16, 0, 0); panel.Children.Add(logsMessage); }
            else if (rows > 0)
            {
                var label = Label("BY PROJECT"); label.Margin = new Thickness(0, 16, 0, 8); panel.Children.Add(label);
                var usage = view.UsageFor(period);
                if (usage.Count == 0) { var none = Caption(view.LogsRead ? "No usage in this period." : "Reading Claude Code's logs…"); panel.Children.Add(none); }
                for (int i = 0; i < Math.Min(rows, usage.Count); i++)
                {
                    var row = new ProjectRow();
                    var top = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : 8, 0, 0) };
                    row.Tokens = names.Add(Text("", "SmallSize", "Muted"), "ClaudeProjectTokens" + i); row.Tokens.Margin = new Thickness(8, 0, 0, 0); DockPanel.SetDock(row.Tokens, Dock.Right); top.Children.Add(row.Tokens);
                    row.Name = names.Add(Text("", "SmallSize", "Text"), "ClaudeProject" + i); top.Children.Add(row.Name);
                    row.Share = new MeterBar { Height = 4, Margin = new Thickness(0, 4, 0, 0) }; row.Share.SetResourceReference(MeterBar.TrackProperty, "Track"); row.Share.SetResourceReference(MeterBar.FillProperty, "Accent");
                    panel.Children.Add(top); panel.Children.Add(row.Share);
                    projects.Add(row);
                }
            }
            source = names.Add(Caption(""), "ClaudeSource"); source.Margin = new Thickness(0, 12, 0, 0); panel.Children.Add(source);
            return panel;
        }

        // Fills the current design from the last view.
        private void Render()
        {
            if (view == null) return;
            DateTime now = DateTime.UtcNow;
            bool needs = view.NeedsYou > 0;
            // The one state allowed to shout: an Amber border around the whole card.
            if (needs) { card.SetResourceReference(Border.BorderBrushProperty, "Warning"); card.BorderThickness = new Thickness(2); }
            else { card.ClearValue(Border.BorderBrushProperty); card.ClearValue(Border.BorderThicknessProperty); }
            if (heading != null) { heading.Text = ClaudeText.Heading(view); heading.SetResourceReference(TextBlock.ForegroundProperty, needs ? "Warning" : "Muted"); }
            if (flag != null && design != CardSizing.Design.Small) flag.Text = ClaudeText.Flag(view, now, use24, design == CardSizing.Design.Medium);
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                var session = part.Id == null ? null : view.Sessions.FirstOrDefault(s => s.Id == part.Id);
                if (session == null) { part.Critter.State = ClaudeState.Ready; part.Critter.Project = view.Connected ? "dozing" : ""; continue; }
                part.Critter.State = session.State; part.Critter.Project = session.ProjectName;
                bool asks = session.State == ClaudeState.NeedsYou;
                if (part.Title != null) part.Title.Text = design == CardSizing.Design.Large ? session.Shown : session.ProjectName;
                if (part.State != null)
                {
                    // The lineup's narrow columns say the state alone; Small and Large say how long it has needed you.
                    part.State.Text = design == CardSizing.Design.Medium ? ClaudeText.State(session.State) : ClaudeText.StateWords(session.State, session.Since, now, design == CardSizing.Design.Large);
                    part.State.SetResourceReference(TextBlock.ForegroundProperty, asks ? "Warning" : "Muted");
                }
                if (part.Detail != null)
                {
                    part.Detail.Text = asks && !String.IsNullOrWhiteSpace(session.Message) ? ClaudeText.Ask(session.Message) : String.Join(" · ", new[] { session.ProjectName, session.Model }.Where(t => !String.IsNullOrWhiteSpace(t)).Distinct());
                    part.Detail.SetResourceReference(TextBlock.ForegroundProperty, asks ? "Warning" : "Muted");
                }
                if (part.Tokens != null) part.Tokens.Text = view.LogsRead && !view.LogsFailed ? ClaudeUsage.Format(session.TokensFor(period)) : "—";
            }
            if (more != null)
            {
                int hidden = view.Sessions.Count - parts.Count(p => p.Id != null);
                if (design == CardSizing.Design.Medium) more.Text = "+" + hidden;
                else more.Text = "+" + hidden + " more" + (view.Sessions.Skip(parts.Count).Any(s => s.State == ClaudeState.NeedsYou) ? " · one needs you" : "");
            }
            var limits = view.Limits;
            if (smallMeter != null)
            {
                double pct = ClaudeText.Percent(limits == null ? null : limits.FiveHour, now);
                smallMeter.Value = pct; smallMeter.SetResourceReference(MeterBar.FillProperty, ClaudeText.Warns(pct) ? "Warning" : "Accent");
            }
            RenderMeter(fiveHour, limits == null ? null : limits.FiveHour, now);
            RenderMeter(week, limits == null ? null : limits.Week, now);
            var usage = view.UsageFor(period); long biggest = usage.Count == 0 ? 1 : Math.Max(1, usage.Max(p => p.Value));
            for (int i = 0; i < projects.Count && i < usage.Count; i++)
            {
                projects[i].Name.Text = usage[i].Key; projects[i].Tokens.Text = ClaudeUsage.Format(usage[i].Value);
                projects[i].Share.Value = 100.0 * usage[i].Value / biggest;
            }
            if (source != null) source.Text = ClaudeText.Source(limits, use24);
        }
        private void RenderMeter(Meter meter, ClaudeLimitWindow window, DateTime now)
        {
            if (meter == null) return;
            double pct = ClaudeText.Percent(window, now);
            bool full = pct >= 100, warns = ClaudeText.Warns(pct);
            meter.Percent.Text = window == null ? "—" : Math.Round(pct) + "%";
            meter.Percent.SetResourceReference(TextBlock.ForegroundProperty, warns ? "Warning" : "Text");
            meter.Bar.Value = pct; meter.Bar.SetResourceReference(SegmentBar.FillProperty, warns ? "Warning" : "Accent");
            meter.Reset.Text = window == null ? "" : window.Current(now) ? ClaudeText.Reset(window, now, use24) : "reset";
            meter.Reset.SetResourceReference(TextBlock.ForegroundProperty, full ? "Warning" : "Muted");
        }
    }
}
