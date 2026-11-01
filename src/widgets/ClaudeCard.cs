using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

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
        // The flag at the heading's right: "SAMPLE · 3 RUNNING", "AS OF 10:42" when the meters are old or the last check failed.
        public static string Flag(ClaudeView view, DateTime now, bool use24, bool withAge)
        {
            var parts = new List<string>();
            if (view.Sample) parts.Add("SAMPLE");
            if (view.Sessions.Count > 0) parts.Add(view.Sessions.Count + " RUNNING");
            if (withAge && Old(view, now)) parts.Add("AS OF " + Clock(view.Limits.UpdatedAt, use24).ToUpperInvariant());
            return String.Join(" · ", parts);
        }
        // The meters show numbers that may have moved since: older than 15 minutes, or the last check didn't go through.
        public static bool Old(ClaudeView view, DateTime now)
        {
            return view.Limits != null && view.Limits.Known && (now - view.Limits.UpdatedAt > ClaudeService.Stale || view.Plan == ClaudePlanState.Failed || view.Plan == ClaudePlanState.Expired);
        }
        public static string PlanName(ClaudePlanState plan)
        {
            return plan == ClaudePlanState.Current ? "current" : plan == ClaudePlanState.Reading ? "reading" : plan == ClaudePlanState.Failed ? "failed" : plan == ClaudePlanState.Expired ? "expired" : "signedOut";
        }
        public static string Clock(DateTime time, bool use24) { return time.ToLocalTime().ToString(use24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant(); }
        // The meters' empty states: the widget's own sign-in is what fills them (decision 015).
        public static string LimitsMissing(ClaudeView view)
        {
            switch (view.Plan)
            {
                case ClaudePlanState.Expired: return SignInEnded;
                case ClaudePlanState.Reading: return "Reading your plan's limits…";
                case ClaudePlanState.Failed: return "Couldn't read your plan's limits. Trying again soon.";
                case ClaudePlanState.Current: return view.NoPlan ? "Plan limits need a Claude Pro or Max login." : "Plan limits show after Claude Code's next reply.";
                default: return SignIn;
            }
        }
        public const string SignIn = "Sign in to Claude in Settings → Claude to see plan limits.", SignInEnded = "Your Claude sign-in has ended. Sign in again in Settings → Claude.";
        public const string NotConnected = "Connect Claude Code in Settings → Claude.", NoSessions = "No Claude Code sessions running.", NoLogs = "Can't read Claude Code's logs.";
        public static string PeriodName(ClaudePeriod period) { return period == ClaudePeriod.FiveHour ? "5 h" : period == ClaudePeriod.Today ? "Today" : "Week"; }
        // "as of 10:42 · checked online" under the Large panel; "Plan limits as of 10:42" when a check failed, and the
        // sign-in's end when it has.
        public static string Source(ClaudeView view, bool use24)
        {
            var limits = view.Limits;
            if (limits == null || !limits.Known) return "";
            if (view.Plan == ClaudePlanState.Expired) return "as of " + Clock(limits.UpdatedAt, use24) + " · sign in again in Settings → Claude";
            if (view.Plan == ClaudePlanState.Failed) return "Plan limits as of " + Clock(limits.UpdatedAt, use24);
            return "as of " + Clock(limits.UpdatedAt, use24) + " · " + (limits.Source == "online" ? "checked online" : "from Claude Code");
        }
        // A project row's or session's usage: "1.8M · ≈ $2.01".
        public static string Usage(long tokens, double cost) { return ClaudeUsage.Format(tokens) + " · " + ClaudeUsage.Money(cost); }
        // A wait: "30 s", "5 min".
        public static string Wait(TimeSpan time) { return time < TimeSpan.FromMinutes(1) ? (int)Math.Ceiling(time.TotalSeconds) + " s" : Duration(time); }
        public static string Threads(int count) { return count == 1 ? "1 thread" : count + " threads"; }
        // Under a project critter: "3 threads · 1.8M · ≈ $2.01".
        public static string GroupDetail(ClaudeProjectGroup group, bool counted) { return Threads(group.Threads.Count) + (counted ? " · " + Usage(group.Tokens, group.Cost) : ""); }
        // "3:40 pm" today, otherwise the day ("Thu").
        public static string Ago(DateTime at, DateTime now, bool use24)
        {
            return at.ToLocalTime().Date == now.ToLocalTime().Date ? Clock(at, use24) : at.ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture);
        }
        // A baby's label: the title the Claude app (or the user) gave the thread; never its prompts.
        public static string ThreadTitle(ClaudeSession thread, DateTime now, bool use24)
        {
            return !String.IsNullOrWhiteSpace(thread.Title) ? thread.Title.Trim() : "Untitled · " + Ago(thread.LastActive, now, use24);
        }
        // A baby's tooltip: the full title, its state or when it was last active, its usage and its model.
        public static string ThreadTip(ClaudeSession thread, ClaudePeriod period, DateTime now, bool use24, bool counted)
        {
            var lines = new List<string> { ThreadTitle(thread, now, use24) };
            lines.Add(thread.Running ? StateWords(thread.State, thread.Since, now, true) + (thread.State == ClaudeState.NeedsYou && !String.IsNullOrWhiteSpace(thread.Message) ? " · " + Ask(thread.Message) : "") : "Ended · last active " + Ago(thread.LastActive, now, use24));
            var detail = new List<string>();
            if (counted) detail.Add(Usage(thread.TokensFor(period), thread.CostFor(period)) + " " + PeriodName(period).ToLowerInvariant());
            if (!String.IsNullOrWhiteSpace(thread.Model)) detail.Add(thread.Model);
            if (!String.IsNullOrWhiteSpace(thread.Where)) detail.Add(thread.Where);
            if (detail.Count > 0) lines.Add(String.Join(" · ", detail));
            if (thread.Running && thread.CanFocus) lines.Add("Tap to show its window.");
            return String.Join("\n", lines);
        }
        public static string NoThreads(ClaudePeriod period) { return "No Claude Code threads " + (period == ClaudePeriod.FiveHour ? "in the last 5 hours." : period == ClaudePeriod.Today ? "today." : "this week."); }
    }

    // Claude Code in three designs that follow the card's size (CardSizing.Claude; docs/design/claude-watcher.md).
    // Small is the loudest running session's critter with its project and state. Medium and Large show each project
    // as a big critter with its threads as baby critters (Ali, 1 Nov 2026): Medium a row of projects with up to three
    // babies each over the plan meters, Large a column per project (its babies in a grid that scrolls when there are
    // many) under a strip with the meters and the period switch. Tapping a project critter folds its babies into it
    // (they're eaten: they shrink into its mouth) or lets them out again (they pop back out); Quiet mode skips the
    // motion. A folded project still turns Amber when a thread inside needs you. Tapping a running baby brings its
    // window forward. When a session needs you the card's border turns Amber and the heading names it. The phone's
    // ClaudeWidget.java draws the same three.
    public sealed class ClaudeCard : CardParts
    {
        public static readonly TimeSpan Motion = TimeSpan.FromMilliseconds(350);
        private readonly Grid host;
        private readonly Border card;
        private readonly PartNames names;
        private readonly Action<string> focus;
        private readonly Action<string, bool> collapse;
        private readonly Action<ClaudePeriod> choosePeriod;
        private ClaudeView view;
        private Rect bounds = Rect.Empty;
        private double textScale = 1;
        private string built = "";
        private CardSizing.Design design;
        private ClaudePeriod period = ClaudePeriod.Today;
        private bool phase, quiet, use24, animating;
        private string spitFolder;
        private readonly HashSet<string> collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Part> parts = new List<Part>();
        private readonly List<Head> heads = new List<Head>();
        private Meter fiveHour, week;
        private TextBlock heading, flag, more, limitsMessage, smallPercent;
        private MeterBar smallMeter;
        private readonly List<Meter> extras = new List<Meter>();
        private readonly List<Button> periodButtons = new List<Button>();

        // A session critter (Small) or a thread's baby critter (Medium and Large).
        private sealed class Part { public ClaudeCritter Critter; public TextBlock Title, State, Tokens; public FrameworkElement Cell; public string Id, Folder; }
        // A project's big critter and the babies laid out under it.
        private sealed class Head { public string Folder; public ClaudeCritter Critter; public TextBlock Name, Detail; public FrameworkElement Column; public readonly List<Part> Babies = new List<Part>(); }
        private sealed class Meter { public TextBlock Percent, Reset; public SegmentBar Bar; public FrameworkElement Root; }

        public ClaudeCard(FrameworkElement scope, Grid host, Border card, Action<string> focus, Action<string, bool> collapse = null, Action<ClaudePeriod> choosePeriod = null)
        {
            this.host = host; this.card = card; this.focus = focus; this.collapse = collapse; this.choosePeriod = choosePeriod; names = new PartNames(scope);
        }

        public CardSizing.Design Current { get { return design; } }
        public ClaudeView View { get { return view; } }
        public ClaudePeriod Period { get { return period; } }
        public bool Animating { get { return animating; } }
        // For the checks: each shown project column and its babies, in card coordinates.
        public List<KeyValuePair<FrameworkElement, List<FrameworkElement>>> Columns() { return heads.Where(h => h.Column != null).Select(h => new KeyValuePair<FrameworkElement, List<FrameworkElement>>(h.Column, h.Babies.Select(b => b.Cell).ToList())).ToList(); }

        public void Fit(Rect next, double scale) { bounds = next; textScale = scale; Rebuild(); }
        public void Update(ClaudeView next)
        {
            view = next;
            if (!animating && next != null) { collapsed.Clear(); collapsed.UnionWith(next.Collapsed); }
            Rebuild();
        }
        public void Configure(bool use24Hour, ClaudePeriod chosen) { use24 = use24Hour; if (chosen != period) { period = chosen; Rebuild(); } else Render(); }
        // The heartbeat: critters' pupils and hops, and the "2 min" counts.
        public void Tick(bool nextPhase, bool quietMode)
        {
            phase = nextPhase && !quietMode; quiet = quietMode;
            foreach (var critter in Critters()) { critter.Quiet = quiet; critter.Phase = phase; }
            Render();
        }
        public void ShowPeriod(ClaudePeriod next) { period = next; if (choosePeriod != null) choosePeriod(next); Rebuild(); }
        private IEnumerable<ClaudeCritter> Critters() { return parts.Select(p => p.Critter).Concat(heads.Select(h => h.Critter)).Concat(heads.SelectMany(h => h.Babies.Select(b => b.Critter))); }

        // Folds a project's babies into it or lets them out (a tap on its big critter). Returns whether it's folded now.
        public bool Toggle(string folder)
        {
            if (animating || folder == null) return collapsed.Contains(folder ?? "");
            bool fold = !collapsed.Contains(folder);
            var head = heads.FirstOrDefault(h => String.Equals(h.Folder, folder, StringComparison.OrdinalIgnoreCase));
            if (fold && head != null && head.Babies.Count > 0 && !quiet && host.IsLoaded && !Program.Rendering) { Eat(head, () => SetCollapsed(folder, true)); return true; }
            if (!fold && !quiet && host.IsLoaded && !Program.Rendering) spitFolder = folder;
            SetCollapsed(folder, fold);
            return fold;
        }
        private void SetCollapsed(string folder, bool fold)
        {
            if (fold) collapsed.Add(folder); else collapsed.Remove(folder);
            if (view != null) { if (fold) view.Collapsed.Add(folder); else view.Collapsed.Remove(folder); }
            if (collapse != null) collapse(folder, fold);
            Rebuild();
        }

        private void Rebuild()
        {
            if (bounds.IsEmpty || animating) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + textScale + "|" + period + "|" + Shape();
            if (key != built) { built = key; Build(); }
            Render();
            if (spitFolder != null)
            {
                var head = heads.FirstOrDefault(h => String.Equals(h.Folder, spitFolder, StringComparison.OrdinalIgnoreCase));
                spitFolder = null;
                if (head != null && head.Babies.Count > 0) host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Spit(head)));
            }
        }
        // What decides the design's parts: which sessions, which projects and threads, and which areas have something.
        private string Shape()
        {
            if (view == null) return "-";
            var groups = String.Join(";", view.Groups(period).Select(g => g.Folder + (collapsed.Contains(g.Folder) ? "!" : "") + ":" + String.Join(",", g.Threads.Select(t => t.Id + (t.Running && t.CanFocus ? "*" : "")))));
            return view.Connected + "|" + view.Sample + "|" + String.Join(",", view.Sessions.Select(s => s.Id + ":" + s.CanFocus)) + "|" + groups + "|" + (view.Limits != null && view.Limits.Known) + view.NoPlan + view.Plan + "|" + (view.Limits == null ? 0 : view.Limits.Extra.Count) + "|" + view.LogsFailed;
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); parts.Clear(); heads.Clear(); periodButtons.Clear(); extras.Clear();
            fiveHour = week = null; heading = flag = more = limitsMessage = smallPercent = null; smallMeter = null;
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
        private ClaudeCritter Critter(double size, string name)
        {
            var critter = new ClaudeCritter { Width = size, Height = size, Quiet = quiet, Phase = phase };
            return names.Add(critter, name);
        }
        // A tappable session: its whole area brings the session's window forward, when one is known.
        private FrameworkElement Tappable(FrameworkElement child, ClaudeSession session)
        {
            var area = new Border { Child = child, Background = Brushes.Transparent };
            if (session != null && session.Running && session.CanFocus)
            {
                string id = session.Id; area.Cursor = Cursors.Hand;
                AutomationProperties.SetName(area, session.Shown + ". Tap to show its window.");
                area.MouseLeftButtonUp += (sender, e) => { e.Handled = true; focus(id); };
            }
            return area;
        }
        // A project critter: a tap folds its babies in or lets them out.
        private FrameworkElement Folding(FrameworkElement child, ClaudeProjectGroup group)
        {
            var area = new Border { Child = child, Background = Brushes.Transparent, Cursor = Cursors.Hand, MinHeight = 48, MinWidth = 48 };
            string folder = group.Folder;
            bool folded = collapsed.Contains(folder);
            area.ToolTip = folded ? "Let " + group.Name + "'s threads out" : "Fold " + group.Name + "'s threads in";
            AutomationProperties.SetName(area, group.Name + ", " + ClaudeText.Threads(group.Threads.Count) + (folded ? ", folded. Tap to show them." : ". Tap to fold them in."));
            area.MouseLeftButtonUp += (sender, e) => { e.Handled = true; Toggle(folder); };
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
            bool meter = view.Limits != null && view.Limits.Known;
            var root = Rows(Star, Auto);
            // The meter's percentage needs a caption line; the shortest cards keep the bar alone.
            bool percent = meter && CardSizing.ClaudeSmallPercent(height, textScale);
            bool caption = CardSizing.ClaudeSmallPercent(height, textScale);
            double room = height - (percent ? 8 + Math.Ceiling(18 * textScale) : meter ? 16 : caption ? Math.Ceiling(18 * textScale) + 6 : 0), size = CardSizing.ClaudeSmallCritter(width, room);
            var row = Columns(Auto, Star);
            var first = view.Sessions.FirstOrDefault();
            var part = new Part { Id = first == null ? null : first.Id, Critter = Critter(size, "ClaudeCritter0") };
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
                // The 5-hour meter along the bottom, with its percentage at the right.
                var line = Columns(Star, Auto); line.Margin = new Thickness(0, percent ? 8 : 10, 0, 0);
                smallMeter = names.Add(new MeterBar { Height = 6, VerticalAlignment = VerticalAlignment.Center }, "ClaudeSmallMeter");
                smallMeter.SetResourceReference(MeterBar.TrackProperty, "Track");
                line.Children.Add(smallMeter);
                if (percent)
                {
                    smallPercent = names.Add(Caption(""), "ClaudeSmallPercent"); smallPercent.Margin = new Thickness(8, 0, 0, 0); smallPercent.VerticalAlignment = VerticalAlignment.Center;
                    line.Children.Add(At(smallPercent, 0, 1));
                }
                root.Children.Add(At(line, 1));
            }
            else if (caption)
            {
                limitsMessage = names.Add(Caption(view.Plan == ClaudePlanState.Expired ? "Claude sign-in ended" : view.Plan == ClaudePlanState.SignedOut ? "Sign in for plan limits" : ""), "ClaudeLimits");
                limitsMessage.Margin = new Thickness(0, 6, 0, 0);
                root.Children.Add(At(limitsMessage, 1));
            }
            return root;
        }

        // Medium: the heading, a row of project critters with up to three babies each, and the plan meters in a strip.
        private FrameworkElement BuildMedium(double width, double height)
        {
            double stripHeight = CardSizing.ClaudeStrip(textScale) + 12;
            var root = Rows(new GridLength(28), new GridLength(8), Star, Auto);
            root.Children.Add(HeadingRow());
            double room = height - 36 - stripHeight;
            var groups = view.Groups(period);
            if (groups.Count == 0) root.Children.Add(At(Empty(Math.Min(96, Math.Max(40, room - 8)), room), 2));
            else
            {
                int columns = CardSizing.ClaudeMediumColumns(width, groups.Count);
                bool overflow = groups.Count > columns && columns > 1;
                int shown = overflow ? columns - 1 : Math.Min(columns, groups.Count);
                double columnWidth = width / columns;
                bool babies = CardSizing.ClaudeMediumBabies(room, textScale);
                double parent = CardSizing.ClaudeMediumParent(room, columnWidth, textScale, babies), baby = Math.Max(16, Math.Round(parent * 0.4));
                var row = new Grid { VerticalAlignment = VerticalAlignment.Center };
                for (int c = 0; c < columns; c++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                for (int i = 0; i < shown; i++)
                {
                    var group = groups[i];
                    var head = new Head { Folder = group.Folder, Critter = Critter(parent, "ClaudeCritter" + i) };
                    var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    head.Critter.HorizontalAlignment = HorizontalAlignment.Center;
                    stack.Children.Add(Folding(head.Critter, group));
                    head.Name = names.Add(Text("", "SmallSize", "Text"), "ClaudeProject" + i); head.Name.HorizontalAlignment = HorizontalAlignment.Center; head.Name.TextTrimming = TextTrimming.CharacterEllipsis; head.Name.MaxWidth = Math.Max(40, columnWidth - 8); head.Name.Margin = new Thickness(0, 6, 0, 0);
                    stack.Children.Add(head.Name);
                    if (babies && !collapsed.Contains(group.Folder) && group.Threads.Count > 0)
                    {
                        int count = CardSizing.ClaudeMediumBabyCount(columnWidth - 8, baby, group.Threads.Count);
                        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
                        for (int j = 0; j < count; j++) line.Children.Add(Baby(head, group.Threads[j], i, j, baby, 0, false));
                        if (count < group.Threads.Count) { var extra = names.Add(Caption("+" + (group.Threads.Count - count)), "ClaudeBabyMore" + i); extra.VerticalAlignment = VerticalAlignment.Center; extra.Margin = new Thickness(4, 0, 0, 0); line.Children.Add(extra); }
                        stack.Children.Add(line);
                    }
                    head.Column = names.Add(stack, "ClaudeColumn" + i);
                    heads.Add(head);
                    row.Children.Add(At(stack, 0, i));
                }
                if (overflow) row.Children.Add(At(MoreProjects(groups.Skip(shown).ToList(), false), 0, shown));
                root.Children.Add(At(row, 2));
            }
            var tile = Tile(Meters(), 14, new Thickness(14, 10, 14, 10)); tile.Margin = new Thickness(0, 12, 0, 0);
            root.Children.Add(At(names.Add(tile, "ClaudeStrip"), 3));
            return root;
        }

        // A thread's baby critter: Large gives it a cell with its title and tokens, Medium the critter alone (its title
        // in the tooltip). A running baby's tap brings its window forward.
        private FrameworkElement Baby(Head head, ClaudeSession thread, int column, int index, double size, double cellWidth, bool labelled)
        {
            var part = new Part { Id = thread.Id, Folder = head.Folder, Critter = Critter(size, "ClaudeBabyCritter" + column + "_" + index) };
            part.Critter.HorizontalAlignment = HorizontalAlignment.Center;
            FrameworkElement content = part.Critter;
            if (labelled)
            {
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
                stack.Children.Add(part.Critter);
                part.Title = names.Add(Text("", "SmallSize", "Text"), "ClaudeBabyTitle" + column + "_" + index); part.Title.HorizontalAlignment = HorizontalAlignment.Center; part.Title.TextWrapping = TextWrapping.Wrap; part.Title.TextTrimming = TextTrimming.WordEllipsis; part.Title.MaxHeight = 2 * Math.Ceiling(20 * textScale); part.Title.TextAlignment = TextAlignment.Center; part.Title.MaxWidth = Math.Max(30, cellWidth - 6); part.Title.Margin = new Thickness(0, 4, 0, 0);
                stack.Children.Add(part.Title);
                part.Tokens = names.Add(Caption(""), "ClaudeBabyTokens" + column + "_" + index); part.Tokens.HorizontalAlignment = HorizontalAlignment.Center; part.Tokens.TextTrimming = TextTrimming.CharacterEllipsis; part.Tokens.MaxWidth = Math.Max(30, cellWidth - 6);
                stack.Children.Add(part.Tokens);
                content = stack;
            }
            else part.Critter.Margin = new Thickness(2, 0, 2, 0);
            var cell = Tappable(content, thread);
            cell.ToolTip = ClaudeText.ThreadTip(thread, period, DateTime.UtcNow, use24, view.LogsRead && !view.LogsFailed);
            cell.RenderTransformOrigin = new Point(0.5, 0.5);
            part.Cell = names.Add(cell, "ClaudeBaby" + column + "_" + index);
            head.Babies.Add(part);
            return cell;
        }

        // The projects past the last column: how many, and the first few names.
        private FrameworkElement MoreProjects(List<ClaudeProjectGroup> rest, bool names3)
        {
            var others = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = names3 ? VerticalAlignment.Top : VerticalAlignment.Center };
            more = names.Add(Reading("+" + rest.Count, 28), "ClaudeMore"); more.SetResourceReference(TextBlock.ForegroundProperty, rest.Any(g => g.NeedsYou) ? "Warning" : "Muted"); more.HorizontalAlignment = HorizontalAlignment.Center; others.Children.Add(more);
            var word = Caption(rest.Count == 1 ? "more project" : "more projects"); word.HorizontalAlignment = HorizontalAlignment.Center; others.Children.Add(word);
            if (names3) foreach (var g in rest.Take(4)) { var name = Caption(g.Name); name.HorizontalAlignment = HorizontalAlignment.Center; name.TextTrimming = TextTrimming.CharacterEllipsis; name.Margin = new Thickness(0, 4, 0, 0); others.Children.Add(name); }
            return others;
        }

        // A dozing critter and what to do next, for no threads or no connection.
        private FrameworkElement Empty(double size, double room)
        {
            var row = Columns(Auto, Star); row.VerticalAlignment = VerticalAlignment.Center;
            var part = new Part { Critter = Critter(size, "ClaudeCritter0") }; parts.Add(part);
            row.Children.Add(part.Critter);
            string words = !view.Connected && view.Threads.Count == 0 ? ClaudeText.NotConnected : ClaudeText.NoThreads(period);
            var text = names.Add(Wrapped(words, "SmallSize", room), "ClaudeMessage"); text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new Thickness(16, 0, 0, 0);
            row.Children.Add(At(text, 0, 1));
            return row;
        }

        // The two plan meters side by side, or what they need.
        private FrameworkElement Meters()
        {
            if (view.Limits == null || !view.Limits.Known)
            {
                limitsMessage = names.Add(Wrapped(ClaudeText.LimitsMissing(view), "SmallSize", CardSizing.ClaudeStrip(textScale) - 20), "ClaudeLimits");
                limitsMessage.VerticalAlignment = VerticalAlignment.Center;
                return limitsMessage;
            }
            fiveHour = BuildMeter("5-HOUR", "ClaudeFiveHour"); week = BuildMeter("WEEK", "ClaudeWeek");
            var grid = Columns(Star, new GridLength(16), Star);
            grid.Children.Add(fiveHour.Root); grid.Children.Add(At(week.Root, 0, 2));
            return grid;
        }
        private Meter BuildMeter(string label, string name)
        {
            var meter = new Meter();
            var top = new DockPanel();
            meter.Percent = names.Add(Text("—", "BodySize", "Text"), name + "Percent"); meter.Percent.VerticalAlignment = VerticalAlignment.Bottom;
            DockPanel.SetDock(meter.Percent, Dock.Right); top.Children.Add(meter.Percent);
            var title = Label(label); title.VerticalAlignment = VerticalAlignment.Center; top.Children.Add(title);
            meter.Bar = names.Add(new SegmentBar { Height = 10, Margin = new Thickness(0, 6, 0, 0) }, name + "Bar"); meter.Bar.SetResourceReference(SegmentBar.TrackProperty, "Track");
            meter.Reset = names.Add(Caption(""), name + "Reset"); meter.Reset.Margin = new Thickness(0, 4, 0, 0);
            var stack = new StackPanel(); stack.Children.Add(top); stack.Children.Add(meter.Bar); stack.Children.Add(meter.Reset);
            meter.Root = stack;
            return meter;
        }

        // Large: the meter strip (with the narrower weekly windows when there's room, and the period switch), then a
        // column per project: its big critter, name and usage, and its threads as babies in a grid that scrolls.
        private FrameworkElement BuildLarge(double width, double height)
        {
            var root = Rows(new GridLength(28), new GridLength(12), Auto, new GridLength(12), Star);
            root.Children.Add(HeadingRow());
            double stripHeight = CardSizing.ClaudeStrip(textScale) + 20, area = height - 28 - 12 - stripHeight - 12;
            root.Children.Add(At(names.Add(Tile(Strip(width - 28), 14, new Thickness(14, 10, 14, 10)), "ClaudeStrip"), 2));
            var groups = view.Groups(period);
            if (groups.Count == 0) { root.Children.Add(At(Empty(Math.Min(96, Math.Max(40, area * 0.5)), area), 4)); return root; }
            int columns = CardSizing.ClaudeColumns(width, groups.Count);
            bool overflow = groups.Count > columns && columns > 1;
            int shown = overflow ? columns - 1 : Math.Min(columns, groups.Count);
            double columnWidth = (width - 16 * (columns - 1)) / columns;
            var body = new Grid();
            for (int c = 0; c < columns; c++) { if (c > 0) body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); body.ColumnDefinitions.Add(new ColumnDefinition { Width = Star }); }
            for (int i = 0; i < shown; i++) body.Children.Add(At(ProjectColumn(groups[i], i, columnWidth, area), 0, i * 2));
            if (overflow) body.Children.Add(At(MoreProjects(groups.Skip(shown).ToList(), true), 0, shown * 2));
            root.Children.Add(At(body, 4));
            return root;
        }

        private FrameworkElement Strip(double width)
        {
            var grid = Columns(Star, new GridLength(16), Auto, new GridLength(0), Auto);
            grid.Children.Add(Meters());
            // Narrower weekly windows (Opus, Sonnet, a model's own) as one line each on a wide strip.
            int extraRows = view.Limits == null || !view.Limits.Known || width < 1000 ? 0 : Math.Min(3, view.Limits.Extra.Count);
            if (extraRows > 0)
            {
                var list = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 170 };
                for (int i = 0; i < extraRows; i++)
                {
                    var extra = new Meter();
                    var line = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : 2, 0, 0) };
                    extra.Percent = names.Add(Caption(""), "ClaudeExtraPercent" + i); DockPanel.SetDock(extra.Percent, Dock.Right); line.Children.Add(extra.Percent);
                    extra.Reset = names.Add(Caption(view.Limits.Extra[i].Key.ToUpperInvariant()), "ClaudeExtra" + i); extra.Reset.TextTrimming = TextTrimming.CharacterEllipsis; line.Children.Add(extra.Reset);
                    extra.Root = line; list.Children.Add(line); extras.Add(extra);
                }
                grid.ColumnDefinitions[3].Width = new GridLength(16);
                grid.Children.Add(At(list, 0, 2));
            }
            var periods = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            int index = 0;
            foreach (ClaudePeriod choice in new[] { ClaudePeriod.FiveHour, ClaudePeriod.Today, ClaudePeriod.Week })
            {
                var button = new Button { Content = ClaudeText.PeriodName(choice), Padding = new Thickness(4), Margin = new Thickness(index == 0 ? 0 : 6, 0, 0, 0), MinWidth = 64 };
                button.SetResourceReference(Control.FontSizeProperty, "SmallSize");
                bool selected = choice == period;
                button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "ControlBorder");
                button.SetResourceReference(Control.BackgroundProperty, selected ? "Selected" : "Page");
                button.BorderThickness = new Thickness(selected ? 2 : 1);
                AutomationProperties.SetName(button, "Threads and usage for " + (choice == ClaudePeriod.FiveHour ? "the 5-hour window" : choice == ClaudePeriod.Today ? "today" : "the week") + (selected ? ", selected" : ""));
                var picked = choice; button.Click += (sender, e) => ShowPeriod(picked);
                periodButtons.Add(names.Add(button, "ClaudePeriod" + index));
                periods.Children.Add(button); index++;
            }
            grid.Children.Add(At(periods, 0, 4));
            return grid;
        }

        // One project's column: the big critter (a tap folds the babies in or out), its name and usage, then its
        // babies, a row per few, scrolling inside the column when they don't all fit.
        private FrameworkElement ProjectColumn(ClaudeProjectGroup group, int index, double width, double area)
        {
            double parent = CardSizing.ClaudeParent(width, area, textScale), baby = CritterPose.Baby(parent);
            var column = Rows(Auto, new GridLength(10), Star);
            var head = new Head { Folder = group.Folder, Critter = Critter(parent, "ClaudeCritter" + index) };
            var top = new StackPanel();
            head.Critter.HorizontalAlignment = HorizontalAlignment.Center;
            var folding = Folding(head.Critter, group); folding.HorizontalAlignment = HorizontalAlignment.Center;
            top.Children.Add(folding);
            head.Name = names.Add(Text("", "BodySize", "Text"), "ClaudeProject" + index); head.Name.HorizontalAlignment = HorizontalAlignment.Center; head.Name.TextTrimming = TextTrimming.CharacterEllipsis; head.Name.MaxWidth = width; head.Name.Margin = new Thickness(0, 6, 0, 0);
            top.Children.Add(head.Name);
            head.Detail = names.Add(Caption(""), "ClaudeProjectDetail" + index); head.Detail.HorizontalAlignment = HorizontalAlignment.Center; head.Detail.TextTrimming = TextTrimming.CharacterEllipsis; head.Detail.MaxWidth = width;
            top.Children.Add(head.Detail);
            column.Children.Add(top);
            if (!collapsed.Contains(group.Folder) && group.Threads.Count > 0)
            {
                int perRow = CardSizing.ClaudeBabiesPerRow(width, textScale), rows = (group.Threads.Count + perRow - 1) / perRow;
                double cellWidth = width / perRow, cellHeight = CardSizing.ClaudeBabyHeight(baby, textScale);
                // A few babies sit centred under their project.
                if (group.Threads.Count < perRow) perRow = group.Threads.Count;
                var grid = new Grid { Width = cellWidth * perRow, HorizontalAlignment = HorizontalAlignment.Center };
                for (int c = 0; c < perRow; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(cellHeight + (r == rows - 1 ? 0 : 6)) });
                for (int j = 0; j < group.Threads.Count; j++)
                {
                    var cell = Baby(head, group.Threads[j], index, j, baby, cellWidth, true);
                    cell.VerticalAlignment = VerticalAlignment.Top;
                    grid.Children.Add(At(cell, j / perRow, j % perRow));
                }
                double room = area - parent - CardSizing.ClaudeHeadText(textScale) - 10;
                FrameworkElement list = grid;
                if (rows * (cellHeight + 6) - 6 > room)
                {
                    // More babies than fit: the column's grid scrolls (inner lists may scroll; the dashboard never does).
                    list = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
                }
                column.Children.Add(At(names.Add(list, "ClaudeBabies" + index), 2));
            }
            head.Column = names.Add(column, "ClaudeColumn" + index);
            heads.Add(head);
            return column;
        }

        // ----- Eating and spitting (Ali, 1 Nov 2026): ~350 ms, instant in Quiet mode -----

        private static Vector ToMouth(Head head, FrameworkElement baby)
        {
            var mouth = head.Critter.TranslatePoint(head.Critter.MouthPoint(), baby);
            return mouth - new Point(baby.ActualWidth / 2, baby.ActualHeight / 2);
        }
        private static TransformGroup Movable(FrameworkElement baby, out ScaleTransform scale, out TranslateTransform move)
        {
            scale = new ScaleTransform(1, 1); move = new TranslateTransform();
            var group = new TransformGroup(); group.Children.Add(scale); group.Children.Add(move);
            baby.RenderTransformOrigin = new Point(0.5, 0.5); baby.RenderTransform = group;
            return group;
        }
        private static void Animate(Animatable target, DependencyProperty property, double from, double to, IEasingFunction ease, TimeSpan begin, TimeSpan length)
        {
            target.BeginAnimation(property, new DoubleAnimation(from, to, new Duration(length)) { EasingFunction = ease, BeginTime = begin, FillBehavior = FillBehavior.HoldEnd });
        }
        // The mouth opens wide, holds, then closes: a gulp when eating, a puff when spitting.
        private static DoubleAnimationUsingKeyFrames Mouth(bool eat)
        {
            var frames = new DoubleAnimationUsingKeyFrames { Duration = Motion, FillBehavior = FillBehavior.Stop };
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(eat ? 120 : 60))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(eat ? 270 : 220))));
            frames.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(Motion)));
            return frames;
        }

        private void Eat(Head head, Action done)
        {
            animating = true;
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            foreach (var baby in head.Babies)
            {
                var cell = baby.Cell; ScaleTransform scale; TranslateTransform move;
                Movable(cell, out scale, out move);
                var to = ToMouth(head, cell);
                Animate(scale, ScaleTransform.ScaleXProperty, 1, 0.1, ease, TimeSpan.Zero, Motion); Animate(scale, ScaleTransform.ScaleYProperty, 1, 0.1, ease, TimeSpan.Zero, Motion);
                Animate(move, TranslateTransform.XProperty, 0, to.X, ease, TimeSpan.Zero, Motion); Animate(move, TranslateTransform.YProperty, 0, to.Y, ease, TimeSpan.Zero, Motion);
                cell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(100))) { BeginTime = TimeSpan.FromMilliseconds(250), FillBehavior = FillBehavior.HoldEnd });
            }
            var mouth = Mouth(true);
            mouth.Completed += (sender, e) => { animating = false; done(); };
            head.Critter.BeginAnimation(ClaudeCritter.MouthProperty, mouth);
        }

        private void Spit(Head head)
        {
            if (head.Critter == null || !head.Critter.IsLoaded) return;
            var ease = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
            foreach (var baby in head.Babies)
            {
                var cell = baby.Cell; ScaleTransform scale; TranslateTransform move;
                Movable(cell, out scale, out move);
                var from = ToMouth(head, cell);
                Animate(scale, ScaleTransform.ScaleXProperty, 0.1, 1, ease, TimeSpan.Zero, Motion); Animate(scale, ScaleTransform.ScaleYProperty, 0.1, 1, ease, TimeSpan.Zero, Motion);
                Animate(move, TranslateTransform.XProperty, from.X, 0, ease, TimeSpan.Zero, Motion); Animate(move, TranslateTransform.YProperty, from.Y, 0, ease, TimeSpan.Zero, Motion);
                cell.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(90))) { FillBehavior = FillBehavior.Stop });
            }
            head.Critter.BeginAnimation(ClaudeCritter.MouthProperty, Mouth(false));
        }

        // Fills the current design from the last view.
        private void Render()
        {
            if (view == null) return;
            DateTime now = DateTime.UtcNow;
            bool needs = view.NeedsYou > 0, counted = view.LogsRead && !view.LogsFailed;
            // The one state allowed to shout: an Amber border around the whole card.
            if (needs) { card.SetResourceReference(Border.BorderBrushProperty, "Warning"); card.BorderThickness = new Thickness(2); }
            else { card.ClearValue(Border.BorderBrushProperty); card.ClearValue(Border.BorderThicknessProperty); }
            if (heading != null) { heading.Text = ClaudeText.Heading(view); heading.SetResourceReference(TextBlock.ForegroundProperty, needs ? "Warning" : "Muted"); }
            if (flag != null && design != CardSizing.Design.Small) flag.Text = ClaudeText.Flag(view, now, use24, true);
            foreach (var part in parts)
            {
                var session = part.Id == null ? null : view.Sessions.FirstOrDefault(s => s.Id == part.Id);
                if (session == null) { part.Critter.State = ClaudeState.Ready; part.Critter.Project = view.Connected ? "dozing" : ""; continue; }
                part.Critter.State = session.State; part.Critter.Project = session.ProjectName;
                if (part.Title != null) part.Title.Text = session.ProjectName;
                if (part.State != null) { part.State.Text = ClaudeText.StateWords(session.State, session.Since, now, false); part.State.SetResourceReference(TextBlock.ForegroundProperty, session.State == ClaudeState.NeedsYou ? "Warning" : "Muted"); }
            }
            if (more != null && design == CardSizing.Design.Small) more.Text = "+" + (view.Sessions.Count - 1) + " more" + (view.Sessions.Skip(1).Any(s => s.State == ClaudeState.NeedsYou) ? " · one needs you" : "");
            if (heads.Count > 0)
            {
                var groups = view.Groups(period).ToDictionary(g => g.Folder, StringComparer.OrdinalIgnoreCase);
                var threads = view.Threads.ToDictionary(t => t.Id);
                foreach (var head in heads)
                {
                    ClaudeProjectGroup group; if (!groups.TryGetValue(head.Folder, out group)) continue;
                    head.Critter.State = group.Asleep ? ClaudeState.Ready : group.State; head.Critter.Project = group.Name;
                    head.Critter.Collapsed = collapsed.Contains(group.Folder); head.Critter.Count = group.Threads.Count;
                    if (head.Name != null) { head.Name.Text = group.Name; head.Name.SetResourceReference(TextBlock.ForegroundProperty, group.NeedsYou ? "Warning" : "Text"); }
                    if (head.Detail != null) head.Detail.Text = ClaudeText.GroupDetail(group, counted);
                    foreach (var baby in head.Babies)
                    {
                        ClaudeSession thread; if (!threads.TryGetValue(baby.Id, out thread)) continue;
                        baby.Critter.State = thread.Running ? thread.State : ClaudeState.Ready; baby.Critter.Project = group.Name;
                        // An ended thread sleeps, dimmed.
                        baby.Critter.Opacity = thread.Running ? 1 : 0.5;
                        if (baby.Title != null) { baby.Title.Text = ClaudeText.ThreadTitle(thread, now, use24); baby.Title.SetResourceReference(TextBlock.ForegroundProperty, thread.Running && thread.State == ClaudeState.NeedsYou ? "Warning" : thread.Running ? "Text" : "Muted"); }
                        if (baby.Tokens != null) baby.Tokens.Text = thread.Running && thread.State == ClaudeState.NeedsYou ? ClaudeText.StateWords(thread.State, thread.Since, now, false) : counted ? ClaudeUsage.Format(thread.TokensFor(period)) + " tokens" : "—";
                    }
                }
            }
            var limits = view.Limits;
            if (smallMeter != null)
            {
                double pct = ClaudeText.Percent(limits == null ? null : limits.FiveHour, now);
                smallMeter.Value = pct; smallMeter.SetResourceReference(MeterBar.FillProperty, ClaudeText.Warns(pct) ? "Warning" : "Accent");
                if (smallPercent != null) { smallPercent.Text = "5h " + Math.Round(pct) + "%" + (ClaudeText.Old(view, now) && bounds.Width >= 340 ? " · as of " + ClaudeText.Clock(limits.UpdatedAt, use24) : ""); smallPercent.SetResourceReference(TextBlock.ForegroundProperty, ClaudeText.Warns(pct) ? "Warning" : "Muted"); }
            }
            for (int i = 0; i < extras.Count && limits != null && i < limits.Extra.Count; i++)
            {
                double pct = ClaudeText.Percent(limits.Extra[i].Value, now);
                extras[i].Percent.Text = Math.Round(pct) + "%";
                extras[i].Percent.SetResourceReference(TextBlock.ForegroundProperty, ClaudeText.Warns(pct) ? "Warning" : "Text");
            }
            RenderMeter(fiveHour, limits == null ? null : limits.FiveHour, now);
            RenderMeter(week, limits == null ? null : limits.Week, now);
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
