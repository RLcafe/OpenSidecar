using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WidgetScreen
{
    // The Claude card's words (docs/design/claude-watcher.md and claude-watcher-v2.html), the same as ClaudeText.java on
    // the phone.
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
        // "Needs you · 2 min" always says how long; the others only when asked.
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
        // Under a meter: "resets 3:40 pm", or "Limit reached · back 3:40 pm" at 100%.
        public static string Reset(ClaudeLimitWindow window, DateTime now, bool use24)
        {
            if (window == null) return "";
            string when = window.ResetsAt.HasValue ? When(window.ResetsAt.Value, now, use24) : "";
            if (window.Percent >= 100) return when.Length == 0 ? "Limit reached" : "Limit reached · back " + when;
            return when.Length == 0 ? "" : "resets " + when;
        }
        // The week's reset with its time: "resets Wed 9:00 am".
        public static string WeekReset(ClaudeLimitWindow window, bool use24)
        {
            if (window == null || !window.ResetsAt.HasValue) return "";
            var local = window.ResetsAt.Value.ToLocalTime();
            return "resets " + local.ToString("ddd", CultureInfo.InvariantCulture) + " " + local.ToString(use24 ? "HH:mm" : "h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant();
        }
        // "resets 4:18 pm · in 1 h 38 m".
        public static string FiveHourReset(ClaudeLimitWindow window, DateTime now, bool use24)
        {
            if (window == null || !window.ResetsAt.HasValue || !window.Current(now)) return window != null && window.ResetsAt.HasValue ? "reset" : "";
            if (window.Percent >= 100) return Reset(window, now, use24);
            var left = window.ResetsAt.Value - now;
            string inText = left.TotalHours >= 1 ? (int)left.TotalHours + " h " + left.Minutes + " m" : Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + " m";
            return "resets " + When(window.ResetsAt.Value, now, use24) + " · in " + inText;
        }
        // "on pace for 92%", or nothing early in a window.
        public static string Pace(double projection) { return Double.IsNaN(projection) ? "" : "on pace for " + Math.Round(projection) + "%"; }
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
        // The heading's live summary: "1 needs you · 2 running · 6 threads today".
        public static string Summary(ClaudeView view, ClaudePeriod period, bool threads)
        {
            if (!view.Connected && view.Threads.Count == 0) return "Not connected";
            var parts = new List<string>();
            if (view.NeedsYou > 0) parts.Add(view.NeedsYou + " needs you");
            if (view.Working > 0) parts.Add(view.Working + " running");
            if (threads)
            {
                int count = view.Groups(period).Sum(g => g.Threads.Count);
                parts.Add(Threads(count) + (period == ClaudePeriod.FiveHour ? " in 5 h" : period == ClaudePeriod.Today ? " today" : " this week"));
            }
            if (parts.Count == 0) parts.Add(view.Sessions.Count == 0 ? "Nothing running" : "All quiet");
            return String.Join(" · ", parts);
        }
        // The flag at the heading's right: "SAMPLE", and "AS OF 10:42" when the meters are old or the last check failed.
        public static string Flag(ClaudeView view, DateTime now, bool use24)
        {
            var parts = new List<string>();
            if (view.Sample) parts.Add("SAMPLE");
            if (Old(view, now)) parts.Add("AS OF " + Clock(view.Limits.UpdatedAt, use24).ToUpperInvariant());
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
        // "Plan limits as of 10:42" when a check failed, and the sign-in's end when it has.
        public static string Source(ClaudeView view, bool use24)
        {
            var limits = view.Limits;
            if (limits == null || !limits.Known) return "";
            if (view.Plan == ClaudePlanState.Expired) return "as of " + Clock(limits.UpdatedAt, use24) + " · sign in again in Settings → Claude";
            if (view.Plan == ClaudePlanState.Failed) return "Plan limits as of " + Clock(limits.UpdatedAt, use24);
            return "as of " + Clock(limits.UpdatedAt, use24) + " · " + (limits.Source == "online" ? "checked online" : "from Claude Code");
        }
        // A project row's or thread's usage: "1.8M · ≈ $2.01".
        public static string Usage(long tokens, double cost) { return ClaudeUsage.Format(tokens) + " · " + ClaudeUsage.Money(cost); }
        // A wait: "30 s", "5 min".
        public static string Wait(TimeSpan time) { return time < TimeSpan.FromMinutes(1) ? (int)Math.Ceiling(time.TotalSeconds) + " s" : Duration(time); }
        public static string Threads(int count) { return count == 1 ? "1 thread" : count + " threads"; }
        public static string Cars(int count) { return count == 1 ? "1 car" : count + " cars"; }
        // Under a project's name: "3 threads · 2 running · Claude app".
        public static string GroupDetail(ClaudeProjectGroup group)
        {
            var parts = new List<string> { Threads(group.Threads.Count) };
            if (group.Running > 0) parts.Add(group.Running + " running");
            var where = group.Threads.Where(t => t.Running && t.Where.Length > 0).Select(t => t.Where).Distinct().ToList();
            if (where.Count == 1) parts.Add(where[0] == "Desktop" ? "Claude app" : where[0]);
            else if (group.Asleep) parts.Add("ended " + Duration(DateTime.UtcNow - group.LastActive) + " ago");
            return String.Join(" · ", parts);
        }
        // "3:40 pm" today, otherwise the day ("Thu").
        public static string Ago(DateTime at, DateTime now, bool use24)
        {
            return at.ToLocalTime().Date == now.ToLocalTime().Date ? Clock(at, use24) : at.ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture);
        }
        // A thread's title: the one the Claude app (or the user) gave it; never its prompts.
        public static string ThreadTitle(ClaudeSession thread, DateTime now, bool use24)
        {
            return !String.IsNullOrWhiteSpace(thread.Title) ? thread.Title.Trim() : "Untitled · " + Ago(thread.LastActive, now, use24);
        }
        // Where a thread runs and for how long in its state ("Desktop · 6 min"), or when it ended ("ended 1 h ago").
        public static string Where(ClaudeSession thread, DateTime now)
        {
            if (!thread.Running) return "ended " + Duration(now - thread.LastActive) + " ago";
            string age = Duration(now - thread.Since);
            return thread.Where.Length > 0 ? thread.Where + " · " + age : age;
        }
        // A thread's chip: what its car is doing.
        public static string Chip(PitState state)
        {
            switch (state)
            {
                case PitState.Need: return "Needs you";
                case PitState.Work: return "Working";
                case PitState.Rev: return "Tool run";
                case PitState.Pit: return "Compacting";
                case PitState.Stuck: return "Retrying";
                case PitState.Done: return "Done";
                case PitState.End: return "Ended";
                default: return "Ready";
            }
        }
        // A thread's tooltip: the full title, its state or when it was last active, its usage and its model.
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
        // "+1.3k" and "−216": lines added and removed.
        public static string Added(long lines) { return "+" + ClaudeUsage.Format(lines); }
        public static string Removed(long lines) { return "−" + ClaudeUsage.Format(lines); }
        // A ledger's dollars without the "≈" (the column says it): "7.10", "12", "1.2k".
        public static string Dollars(double dollars) { string money = ClaudeUsage.Money(dollars); return money.Substring(money.IndexOf('$') + 1); }
        // The model mix in words, for its tooltip: "Opus 80% · Sonnet 20%".
        public static string Mix(double[] mix)
        {
            var names = new[] { "Opus", "Sonnet", "Haiku" };
            var parts = Enumerable.Range(0, 3).Where(i => mix[i] > 0.005).Select(i => names[i] + " " + Math.Round(mix[i] * 100) + "%").ToList();
            return parts.Count == 0 ? "No replies in this period" : String.Join(" · ", parts);
        }
        // The narrower weekly windows in one line: "Opus only 18% · Sonnet 9%".
        public static string Extras(ClaudeLimits limits, DateTime now)
        {
            if (limits == null || limits.Extra.Count == 0) return "";
            var parts = limits.Extra.Take(2).Select((p, i) => p.Key.Replace(" week", "") + (i == 0 ? " only " : " ") + Math.Round(Percent(p.Value, now)) + "%");
            return String.Join(" · ", parts);
        }
        // The lap counter: "Week · lap 5 of 7".
        public static string Lap(int lap) { return lap < 0 ? "WEEK" : "WEEK · LAP " + (lap + 1) + " OF 7"; }
        // The day names under the laps, from the window's start.
        public static string[] LapDays(ClaudeLimitWindow week)
        {
            var days = new string[7];
            DateTime start = week != null && week.ResetsAt.HasValue ? week.ResetsAt.Value - ClaudePace.Week : DateTime.UtcNow.AddDays(-6);
            for (int i = 0; i < 7; i++) days[i] = start.AddDays(i).ToLocalTime().ToString("ddd", CultureInfo.InvariantCulture);
            return days;
        }
        // The cache hit rate: "91%", or "—" with nothing read today.
        public static string Percent(double share) { return Double.IsNaN(share) ? "—" : Math.Round(share * 100) + "%"; }
    }

    // Claude Code as a pit wall (docs/design/claude-watcher-v2.html; decision 017), in three designs that follow the
    // card's size (CardSizing.Claude). Each project is a pit garage and each thread a pixel-art race car drawn in code
    // (PitArt): its class follows the model it used most, its colour the project's, and its status shows in what it
    // does. Large: the heading with a live summary and the period switch; the plan row (the 5-hour tank gauge with
    // pace, the week as seven laps, the rev counter with the hour's burn); a pit stop strip for the thread waiting on
    // you; and the ledger of garages and cars beside the 5-hour lanes and today's facts. Medium: the two plan meters
    // with pace and the top garages with their cars. Small: the car that needs you (or the busiest) and the 5-hour
    // meter. Tapping a garage rolls its cars in (they back out of their rows and drive into the bay, tail lights toward
    // you) or out (headlights toward you, then into their rows); Quiet mode swaps states without motion. The phone's
    // ClaudeWidget.java draws the same.
    public sealed class ClaudeCard : CardParts
    {
        public static readonly TimeSpan Roll = TimeSpan.FromMilliseconds(1200);
        private readonly Grid host;
        private readonly Border card;
        private readonly PartNames names;
        private readonly Func<string, string> focus;
        private readonly Action<string, bool> collapse;
        private readonly Action<ClaudePeriod> choosePeriod;
        private readonly PitClock clock;
        private ClaudeView view;
        private Rect bounds = Rect.Empty;
        private double textScale = 1;
        private string built = "";
        private CardSizing.Design design;
        private ClaudePeriod period = ClaudePeriod.Today;
        private bool quiet, use24, live;
        private RollState rolling;
        private string rollOut;
        private readonly HashSet<string> collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Each thread's last car state, so Done and Ended play their once-only frames when a car changes into them.
        private readonly Dictionary<string, PitState> seen = new Dictionary<string, PitState>();
        private readonly List<CarPart> cars = new List<CarPart>();
        private readonly List<GaragePart> garages = new List<GaragePart>();
        private readonly List<Action<DateTime>> fills = new List<Action<DateTime>>();
        private Canvas layer;
        private Border askStrip;

        // A thread's car (in the ledger, inline on Medium, Small's or the pit stop strip's) and its row.
        private sealed class CarPart { public string Id, Folder; public PitSprite Sprite; public FrameworkElement Row; public double Width; public bool Dim; }
        // A project's garage and its cars.
        private sealed class GaragePart { public string Folder; public PitSprite Sprite; public FrameworkElement Tap; public readonly List<CarPart> Cars = new List<CarPart>(); }

        public ClaudeCard(FrameworkElement scope, Grid host, Border card, Func<string, string> focus, Action<string, bool> collapse = null, Action<ClaudePeriod> choosePeriod = null)
        {
            this.host = host; this.card = card; this.focus = focus; this.collapse = collapse; this.choosePeriod = choosePeriod; names = new PartNames(scope);
            clock = new PitClock(host.Dispatcher);
            host.IsVisibleChanged += (sender, e) => clock.SetLive(live && host.IsVisible);
        }

        public CardSizing.Design Current { get { return design; } }
        public ClaudeView View { get { return view; } }
        public ClaudePeriod Period { get { return period; } }
        public bool Animating { get { return rolling != null; } }
        public PitClock Clock { get { return clock; } }
        // For the checks: each shown garage and its cars, in card coordinates.
        public List<KeyValuePair<FrameworkElement, List<FrameworkElement>>> Garages() { return garages.Select(g => new KeyValuePair<FrameworkElement, List<FrameworkElement>>(g.Tap, g.Cars.Select(c => (FrameworkElement)c.Sprite).ToList())).ToList(); }
        public IEnumerable<PitSprite> Sprites { get { return cars.Select(c => c.Sprite).Concat(garages.Select(g => g.Sprite)); } }

        public void Fit(Rect next, double scale) { bounds = next; textScale = scale; Rebuild(); }
        public void Update(ClaudeView next)
        {
            view = next;
            if (rolling == null && next != null) { collapsed.Clear(); collapsed.UnionWith(next.Collapsed); }
            Rebuild();
        }
        public void Configure(bool use24Hour, ClaudePeriod chosen) { use24 = use24Hour; if (chosen != period) { period = chosen; Rebuild(); } else Render(); }
        // The heartbeat: the "2 min" counts and Quiet mode. The cars move on the card's own frame clock.
        public void Tick(bool quietMode)
        {
            if (quietMode != quiet) { quiet = quietMode; Render(); } else Render();
        }
        // Whether the card may move: shown on the page, the window not minimised, and not in Quiet mode.
        public void SetLive(bool shown)
        {
            live = shown && !quiet;
            clock.SetLive(live && host.IsVisible);
        }
        public void ShowPeriod(ClaudePeriod next) { period = next; if (choosePeriod != null) choosePeriod(next); Rebuild(); }

        // Folds a project's cars into its garage or rolls them out (a tap on the garage). Returns whether it's folded now.
        public bool Toggle(string folder)
        {
            if (rolling != null || folder == null) return collapsed.Contains(folder ?? "");
            bool fold = !collapsed.Contains(folder);
            var garage = garages.FirstOrDefault(g => String.Equals(g.Folder, folder, StringComparison.OrdinalIgnoreCase));
            bool motion = !quiet && live && host.IsLoaded && !Program.Rendering;
            if (fold && motion && garage != null && garage.Cars.Count > 0) { StartRoll(garage, false, () => SetCollapsed(folder, true)); return true; }
            if (!fold && motion) rollOut = folder;
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
            if (bounds.IsEmpty || rolling != null) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + textScale + "|" + period + "|" + Layout();
            if (key != built) { built = key; Build(); }
            Render();
            if (rollOut != null)
            {
                string folder = rollOut; rollOut = null;
                var garage = garages.FirstOrDefault(g => String.Equals(g.Folder, folder, StringComparison.OrdinalIgnoreCase));
                if (garage != null && garage.Cars.Count > 0)
                {
                    foreach (var c in garage.Cars) c.Sprite.Visibility = Visibility.Hidden;
                    garage.Sprite.Visibility = Visibility.Hidden;
                    host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => StartRoll(garage, true, null)));
                }
            }
        }
        // What decides the design's parts: which threads and projects, which areas have something.
        private string Layout()
        {
            if (view == null) return "-";
            var groups = String.Join(";", view.Groups(period).Select(g => g.Folder + (collapsed.Contains(g.Folder) ? "!" : "") + ":" + String.Join(",", g.Threads.Select(t => t.Id + (t.Running && t.CanFocus ? "*" : "")))));
            var asking = view.Sessions.FirstOrDefault(s => s.State == ClaudeState.NeedsYou);
            return view.Connected + "|" + view.Sample + "|" + String.Join(",", view.Sessions.Select(s => s.Id + ":" + s.CanFocus)) + "|" + groups + "|" + (asking == null ? "" : asking.Id + asking.CanFocus)
                + "|" + (view.Limits != null && view.Limits.Known) + view.NoPlan + view.Plan + "|" + (view.Limits == null ? 0 : view.Limits.Extra.Count) + "|" + view.LogsFailed + "|" + String.Join(",", view.Lanes.Select(l => l.Folder)) + "|" + view.Facts.Telemetry;
        }

        private void Build()
        {
            EndRoll(false);
            host.Children.Clear(); names.Clear(); cars.Clear(); garages.Clear(); fills.Clear(); clock.Clear(); askStrip = null;
            double width = Math.Max(1, bounds.Width - 48), height = Math.Max(1, bounds.Height - 48);
            design = CardSizing.Claude(bounds.Width, bounds.Height);
            FrameworkElement content;
            if (view == null) content = Caption("Reading Claude Code…");
            else content = design == CardSizing.Design.Small ? BuildSmall(width, height) : design == CardSizing.Design.Medium ? BuildMedium(width, height) : BuildLarge(width, height);
            host.Children.Add(names.Add(content, "ClaudeView"));
            layer = new Canvas { IsHitTestVisible = false, ClipToBounds = false };
            host.Children.Add(layer);
        }

        // ----- Parts -----

        private double Scaled(double size) { return Math.Round(size * textScale); }
        private static Run Colored(string text, string brush) { var run = new Run(text); run.SetResourceReference(TextElement.ForegroundProperty, brush); return run; }
        private TextBlock Trimmed(TextBlock block) { block.TextTrimming = TextTrimming.CharacterEllipsis; return block; }
        private static TextBlock Right(TextBlock block) { block.TextAlignment = TextAlignment.Right; block.HorizontalAlignment = HorizontalAlignment.Stretch; block.VerticalAlignment = VerticalAlignment.Center; return block; }
        private static TextBlock Upper(string text, double size) { var block = Text(text.ToUpperInvariant(), size, "Muted"); block.TextTrimming = TextTrimming.CharacterEllipsis; return block; }

        // A car sprite for a thread, registered for its frames.
        private CarPart Car(ClaudeSession thread, string folder, double width, string name, bool dim)
        {
            var sprite = names.Add(new PitSprite { Width = width, Height = Math.Round(width * 60 / 140), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, name);
            var part = new CarPart { Id = thread == null ? null : thread.Id, Folder = folder, Sprite = sprite, Width = width, Dim = dim };
            cars.Add(part); clock.Add(sprite);
            return part;
        }
        // A tappable thread: its whole area brings the thread's window forward, when one is known.
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
        // A project's garage: a tap rolls its cars in or out.
        private GaragePart Garage(ClaudeProjectGroup group, double size, string name)
        {
            var sprite = names.Add(new PitSprite { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, name);
            var tap = new Border { Child = sprite, Background = Brushes.Transparent, Cursor = Cursors.Hand, MinWidth = 48, MinHeight = 48 };
            string folder = group.Folder;
            bool folded = collapsed.Contains(folder);
            tap.ToolTip = folded ? "Roll " + group.Name + "'s cars out" : "Roll " + group.Name + "'s cars into the garage";
            AutomationProperties.SetName(tap, group.Name + ", " + ClaudeText.Threads(group.Threads.Count) + (folded ? ", in the garage. Tap to roll them out." : ". Tap to roll them into the garage."));
            tap.MouseLeftButtonUp += (sender, e) => { e.Handled = true; Toggle(folder); };
            var part = new GaragePart { Folder = folder, Sprite = sprite, Tap = names.Add(tap, name + "Tap") };
            garages.Add(part); clock.Add(sprite);
            return part;
        }
        private FrameworkElement Dot(bool warn)
        {
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            dot.SetResourceReference(Shape.FillProperty, warn ? "Warning" : "Accent");
            return dot;
        }
        // The heading row: the title, the live summary, the flag and (Large) the period switch.
        private FrameworkElement Header(double width, bool large)
        {
            var row = new DockPanel { LastChildFill = true };
            if (large) { var periods = Periods(); DockPanel.SetDock(periods, Dock.Right); row.Children.Add(periods); }
            var flag = names.Add(Caption(""), "ClaudeFlag"); flag.VerticalAlignment = VerticalAlignment.Center; flag.Margin = new Thickness(12, 0, 12, 0);
            DockPanel.SetDock(flag, Dock.Right); row.Children.Add(flag);
            var title = names.Add(Label(large && width >= 900 ? "CLAUDE CODE · PIT WALL" : "CLAUDE CODE"), "ClaudeHeading"); title.VerticalAlignment = VerticalAlignment.Center; title.Margin = new Thickness(0, 0, 14, 0);
            DockPanel.SetDock(title, Dock.Left); row.Children.Add(title);
            var live = new DockPanel { LastChildFill = true, VerticalAlignment = VerticalAlignment.Center };
            var dot = (Ellipse)Dot(false); DockPanel.SetDock(dot, Dock.Left); live.Children.Add(dot);
            var summary = names.Add(Trimmed(Text("", Scaled(14), "Text")), "ClaudeSummary"); summary.VerticalAlignment = VerticalAlignment.Center; live.Children.Add(summary);
            row.Children.Add(live);
            fills.Add(now => {
                bool needs = view.NeedsYou > 0;
                dot.SetResourceReference(Shape.FillProperty, needs ? "Warning" : view.Working > 0 ? "Accent" : "Muted");
                summary.Text = ClaudeText.Summary(view, period, large);
                summary.SetResourceReference(TextBlock.ForegroundProperty, needs ? "Warning" : "Text");
                flag.Text = ClaudeText.Flag(view, now, use24);
            });
            return row;
        }
        private FrameworkElement Periods()
        {
            var periods = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            int index = 0;
            foreach (ClaudePeriod choice in new[] { ClaudePeriod.FiveHour, ClaudePeriod.Today, ClaudePeriod.Week })
            {
                var button = new Button { Content = ClaudeText.PeriodName(choice), Padding = new Thickness(4), Margin = new Thickness(index == 0 ? 0 : 4, 0, 0, 0), MinWidth = 64, MinHeight = 48 };
                button.SetResourceReference(Control.FontSizeProperty, "SmallSize");
                bool selected = choice == period;
                button.SetResourceReference(Control.BorderBrushProperty, selected ? "Accent" : "ControlBorder");
                button.SetResourceReference(Control.BackgroundProperty, selected ? "Selected" : "Input");
                button.BorderThickness = new Thickness(selected ? 2 : 1);
                AutomationProperties.SetName(button, "Threads and usage for " + (choice == ClaudePeriod.FiveHour ? "the 5-hour window" : choice == ClaudePeriod.Today ? "today" : "the week") + (selected ? ", selected" : ""));
                var picked = choice; button.Click += (sender, e) => ShowPeriod(picked);
                periods.Children.Add(names.Add(button, "ClaudePeriod" + index)); index++;
            }
            return periods;
        }

        // ----- Large -----

        private FrameworkElement BuildLarge(double width, double height)
        {
            bool asking = view.Sessions.Any(s => s.State == ClaudeState.NeedsYou);
            var root = Rows(new GridLength(48), new GridLength(12), Auto, Auto, new GridLength(12), Star);
            root.Children.Add(Header(width, true));
            root.Children.Add(At(PlanRow(width), 2));
            if (asking) { askStrip = AskStrip(); askStrip.Margin = new Thickness(0, 12, 0, 0); root.Children.Add(At(askStrip, 3)); }
            bool side = CardSizing.ClaudeSide(width);
            var ledger = side ? Columns(Star, new GridLength(16), new GridLength(CardSizing.ClaudeSideWidth)) : Columns(Star);
            ledger.Children.Add(Ledger(side ? width - 16 - CardSizing.ClaudeSideWidth : width));
            if (side) ledger.Children.Add(At(SidePanel(), 0, 2));
            root.Children.Add(At(names.Add(ledger, "ClaudeLedgerArea"), 5));
            return root;
        }

        // The plan row: the 5-hour tank, the week's laps and the rev counter (from 900 wide).
        private FrameworkElement PlanRow(double width)
        {
            bool revs = CardSizing.ClaudeRevs(width), known = view.Limits != null && view.Limits.Known;
            var row = revs ? Columns(new GridLength(1.1, GridUnitType.Star), new GridLength(12), new GridLength(1.3, GridUnitType.Star), new GridLength(12), new GridLength(1.1, GridUnitType.Star)) : Columns(Star, new GridLength(12), Star);
            row.Height = CardSizing.ClaudePlanHeight;
            double tile = revs ? (width - 24) / 3.5 : (width - 12) / 2;
            if (known) { row.Children.Add(names.Add(TankTile(tile), "ClaudeTank")); row.Children.Add(At(names.Add(WeekTile(), "ClaudeWeekTile"), 0, 2)); }
            else
            {
                var message = names.Add(Wrapped(ClaudeText.LimitsMissing(view), 15, CardSizing.ClaudePlanHeight - 24), "ClaudeLimits");
                message.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(At(Tile(message, 12, new Thickness(14, 12, 14, 12)), 0, 0, 3));
            }
            if (revs) row.Children.Add(At(names.Add(RevTile(tile * 1.1), "ClaudeRevs"), 0, 4));
            return row;
        }
        private TextBlock Wrapped(string text, double size, double maxHeight)
        {
            var block = Text(text, size, "Muted"); block.TextWrapping = TextWrapping.Wrap; block.TextTrimming = TextTrimming.WordEllipsis; block.MaxHeight = Math.Max(18, maxHeight);
            return block;
        }
        private static TextBlock Big(string text) { var block = Reading(text, 40); block.SetResourceReference(TextBlock.ForegroundProperty, "Text"); return block; }
        private static void SetBig(TextBlock block, double percent, string brush)
        {
            block.Inlines.Clear(); block.Inlines.Add(Colored(Math.Round(percent).ToString(CultureInfo.InvariantCulture), brush));
            var unit = Colored("%", "Muted"); unit.FontSize = block.FontSize / 2; block.Inlines.Add(unit);
        }
        private Border TankTile(double width)
        {
            double size = width >= 330 ? 140 : 112;
            var gauge = names.Add(new PitGauge { Width = size, Height = size * 0.6, VerticalAlignment = VerticalAlignment.Center }, "ClaudeTankGauge");
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            text.Children.Add(Upper("5-hour tank", 13));
            var value = Big("—"); text.Children.Add(value);
            var pace = Trimmed(Text("", 13, "Muted")); text.Children.Add(pace);
            var reset = Trimmed(Text("", 13, "Muted")); text.Children.Add(reset);
            names.Add(value, "ClaudeFiveHourPercent"); names.Add(pace, "ClaudeFiveHourPace"); names.Add(reset, "ClaudeFiveHourReset");
            var row = Columns(Auto, Star); row.Children.Add(gauge); row.Children.Add(At(text, 0, 1));
            fills.Add(now => {
                var w = view.Limits.FiveHour;
                double pct = ClaudeText.Percent(w, now), elapsed = ClaudePace.Elapsed(w, ClaudePace.FiveHours, now), projection = ClaudePace.Projection(pct, elapsed);
                bool warn = ClaudePace.Warns(projection) || ClaudeText.Warns(pct);
                gauge.Value = pct / 100; gauge.Projection = Double.IsNaN(projection) ? Double.NaN : projection / 100; gauge.Pace = elapsed; gauge.Warn = pct >= 100; gauge.InvalidateVisual();
                if (w == null) value.Text = "—"; else SetBig(value, pct, ClaudeText.Warns(pct) ? "Warning" : "Text");
                pace.Text = ClaudeText.Pace(projection); pace.SetResourceReference(TextBlock.ForegroundProperty, warn ? "Warning" : "Muted");
                reset.Text = ClaudeText.FiveHourReset(w, now, use24); reset.SetResourceReference(TextBlock.ForegroundProperty, pct >= 100 ? "Warning" : "Muted");
            });
            return Tile(row, 12, new Thickness(12, 10, 14, 10));
        }
        private Border WeekTile()
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var top = new DockPanel();
            var resets = Text("", 13, "Muted"); DockPanel.SetDock(resets, Dock.Right); top.Children.Add(resets);
            var lap = Upper("Week", 13); top.Children.Add(lap);
            stack.Children.Add(top);
            var middle = Columns(Auto, Star); middle.Margin = new Thickness(0, 2, 0, 4);
            var value = Big("—"); middle.Children.Add(value);
            var notes = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var pace = Right(Trimmed(Text("", 13, "Muted"))); var extra = Right(Trimmed(Text("", 13, "Muted")));
            notes.Children.Add(pace); notes.Children.Add(extra); middle.Children.Add(At(notes, 0, 1));
            stack.Children.Add(middle);
            var laps = names.Add(new WeekLaps { Height = 30 }, "ClaudeLaps"); stack.Children.Add(laps);
            names.Add(value, "ClaudeWeekPercent"); names.Add(pace, "ClaudeWeekPace"); names.Add(resets, "ClaudeWeekReset");
            fills.Add(now => {
                var w = view.Limits.Week;
                double pct = ClaudeText.Percent(w, now), elapsed = ClaudePace.Elapsed(w, ClaudePace.Week, now), projection = ClaudePace.Projection(pct, elapsed);
                lap.Text = ClaudeText.Lap(ClaudePace.LapNow(elapsed));
                resets.Text = w == null ? "" : w.Current(now) ? ClaudeText.WeekReset(w, use24) : "reset";
                if (w == null) value.Text = "—"; else SetBig(value, pct, ClaudeText.Warns(pct) ? "Warning" : "Text");
                pace.Text = ClaudeText.Pace(projection); pace.SetResourceReference(TextBlock.ForegroundProperty, ClaudePace.Warns(projection) ? "Warning" : "Muted");
                extra.Text = ClaudeText.Extras(view.Limits, now);
                laps.Percent = pct; laps.Projection = projection; laps.Now = ClaudePace.LapNow(elapsed); laps.Days = ClaudeText.LapDays(w); laps.InvalidateVisual();
            });
            return Tile(stack, 12, new Thickness(14, 10, 14, 10));
        }
        private Border RevTile(double width)
        {
            double size = width >= 330 ? 140 : 112;
            var gauge = names.Add(new PitGauge { Width = size, Height = size * 0.6, VerticalAlignment = VerticalAlignment.Center, Red = ClaudePace.Redline }, "ClaudeRevGauge");
            var text = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            foreach (var h in new[] { Auto, Auto, Auto, Auto }) text.RowDefinitions.Add(new RowDefinition { Height = h });
            text.Children.Add(Upper("Revs · tok/min", 13));
            var value = Big("—"); text.Children.Add(At(value, 1));
            var burn = names.Add(new BurnLine { Height = 24, Margin = new Thickness(0, 2, 0, 2) }, "ClaudeBurn"); text.Children.Add(At(burn, 2));
            text.Children.Add(At(Text("last 60 min", 13, "Muted"), 3));
            var row = Columns(Auto, Star); row.Children.Add(gauge); row.Children.Add(At(text, 0, 1));
            fills.Add(now => {
                gauge.Value = Math.Min(1, view.Rate / view.Scale); gauge.Low = "0"; gauge.High = ClaudeUsage.Format((long)view.Scale); gauge.InvalidateVisual();
                string rate = ClaudeUsage.Format((long)view.Rate);
                value.Inlines.Clear();
                string digits = rate.TrimEnd('k', 'M', 'B'), unit = rate.Substring(digits.Length);
                value.Inlines.Add(Colored(digits, view.Rate >= view.Scale * ClaudePace.Redline ? "Warning" : "Text"));
                if (unit.Length > 0) { var small = Colored(unit, "Muted"); small.FontSize = 20; value.Inlines.Add(small); }
                burn.Values = view.Burn; burn.Maximum = view.Scale; burn.InvalidateVisual();
            });
            return Tile(row, 12, new Thickness(12, 10, 14, 10));
        }

        // The pit stop strip: the thread that waited on you longest, what it wants, and Go to it.
        private Border AskStrip()
        {
            var thread = view.Sessions.First(s => s.State == ClaudeState.NeedsYou);
            var strip = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 6, 8, 6), BorderThickness = new Thickness(1), Height = Math.Max(60, Scaled(16) * 1.35 + 13 * 1.35 + 14) };
            strip.Background = Tint("Warning", 0.12); strip.BorderBrush = Tint("Warning", 0.45);
            var row = Columns(Auto, Star, Auto);
            var car = Car(thread, thread.Project, 62, "ClaudeAskCar", false);
            car.Sprite.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(car.Sprite);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var title = names.Add(Trimmed(Text("", Scaled(16), "Text")), "ClaudeAskTitle"); title.FontWeight = FontWeights.SemiBold; words.Children.Add(title);
            var reason = names.Add(Trimmed(Text("", 13, "Warning")), "ClaudeAskReason"); words.Children.Add(reason);
            row.Children.Add(At(words, 0, 1));
            if (thread.CanFocus)
            {
                var go = new Button { Content = "Go to it", MinHeight = 48, MinWidth = 96, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0) };
                go.SetResourceReference(Control.BackgroundProperty, "Warning"); go.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x0E));
                go.SetResourceReference(Control.FontSizeProperty, "SmallSize");
                string id = thread.Id; go.Click += (sender, e) => focus(id);
                AutomationProperties.SetName(go, "Go to the thread that needs you");
                row.Children.Add(At(names.Add(go, "ClaudeAskGo"), 0, 2));
            }
            strip.Child = row;
            fills.Add(now => {
                var t = view.Sessions.FirstOrDefault(s => s.Id == thread.Id) ?? thread;
                title.Text = "Pit stop: " + ClaudeText.ThreadTitle(t, now, use24) + " · " + t.ProjectName;
                var parts = new List<string>();
                string ask = ClaudeText.Ask(t.Message); if (ask.Length > 0) parts.Add(ask);
                parts.Add("waiting " + ClaudeText.Duration(now - t.Since));
                if (t.Where.Length > 0) parts.Add(t.Where == "Desktop" ? "Claude app" : t.Where);
                reason.Text = String.Join(" · ", parts);
            });
            return names.Add(strip, "ClaudeAsk");
        }
        private Brush Tint(string key, double opacity)
        {
            var brush = host.TryFindResource(key) as SolidColorBrush;
            var color = brush == null ? Color.FromRgb(0xF5, 0xC4, 0x7D) : brush.Color;
            var tinted = new SolidColorBrush(Color.FromArgb((byte)Math.Round(opacity * 255), color.R, color.G, color.B)); tinted.Freeze();
            return tinted;
        }

        // The ledger's columns: car, project or thread, tokens, ≈ $, models and lines (the last two drop on narrow ones).
        private Grid LedgerRow(double width, double height)
        {
            var row = new Grid { Height = height };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(72) + 10) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(52) + 10) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardSizing.ClaudeModels(width) ? 74 : 0) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CardSizing.ClaudeLines(width) ? Scaled(84) + 10 : 0) });
            return row;
        }
        private FrameworkElement Ledger(double width)
        {
            var groups = view.Groups(period);
            var table = Rows(new GridLength(24), Star);
            var head = LedgerRow(width, 24);
            var labels = new[] { "", "Project · thread", "Tokens", "≈ $", "Models", "Lines" };
            for (int i = 1; i < labels.Length; i++)
            {
                if (head.ColumnDefinitions[i].Width.Value == 0 && !head.ColumnDefinitions[i].Width.IsStar) continue;
                var label = Upper(labels[i], 12); label.VerticalAlignment = VerticalAlignment.Center; if (i >= 2) { Right(label); label.Margin = new Thickness(10, 0, 0, 0); }
                head.Children.Add(At(label, 0, i));
            }
            var line = new Border { BorderThickness = new Thickness(0, 0, 0, 1) }; line.SetResourceReference(Border.BorderBrushProperty, "ControlBorder"); Grid.SetColumnSpan(line, 6); head.Children.Add(line);
            table.Children.Add(head);
            var list = new StackPanel();
            if (groups.Count == 0) list.Children.Add(Empty(width));
            for (int i = 0; i < groups.Count; i++) AddGarageRows(list, groups[i], i, width);
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            table.Children.Add(At(names.Add(scroll, "ClaudeLedger"), 1));
            return table;
        }
        private FrameworkElement Empty(double width)
        {
            var row = Columns(Auto, Star); row.Margin = new Thickness(0, 12, 0, 0);
            var car = Car(null, "", 62, "ClaudeIdleCar", true);
            row.Children.Add(car.Sprite);
            string words = !view.Connected && view.Threads.Count == 0 ? ClaudeText.NotConnected : ClaudeText.NoThreads(period);
            var text = names.Add(Wrapped(words, 15, 60), "ClaudeMessage"); text.VerticalAlignment = VerticalAlignment.Center; text.Margin = new Thickness(16, 0, 0, 0);
            row.Children.Add(At(text, 0, 1));
            return row;
        }
        private void AddGarageRows(StackPanel list, ClaudeProjectGroup group, int index, double width)
        {
            bool folded = collapsed.Contains(group.Folder);
            double projectHeight = Math.Max(52, Math.Ceiling(Scaled(17) * 1.33 + 12 * 1.33 + 10)), threadHeight = Math.Max(36, Math.Ceiling(Scaled(14) * 1.33 + 10));
            var row = LedgerRow(width, projectHeight);
            var top = new Border { BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0) }; top.SetResourceReference(Border.BorderBrushProperty, "ControlBorder"); Grid.SetColumnSpan(top, 6); row.Children.Add(top);
            var garage = Garage(group, 40, "ClaudeGarage" + index);
            row.Children.Add(garage.Tap);
            var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            var title = names.Add(Trimmed(Text(group.Name, Scaled(17), "Text")), "ClaudeProject" + index); title.FontWeight = FontWeights.SemiBold; name.Children.Add(title);
            TextBlock info;
            if (folded)
            {
                info = Trimmed(Text("", 12, "Muted"));
                info.Inlines.Add(new InlineUIContainer(Bubble(group.Threads.Count)) { BaselineAlignment = BaselineAlignment.Center });
                info.Inlines.Add(new Run(" " + ClaudeText.Cars(group.Threads.Count) + " in the garage · tap to roll them out"));
            }
            else info = Trimmed(Text(ClaudeText.GroupDetail(group), 12, "Muted"));
            name.Children.Add(names.Add(info, "ClaudeProjectDetail" + index));
            var share = new MeterBar { Height = 4, Margin = new Thickness(0, 4, 0, 0), Fill = Solid(PitArt.Accent(group.Name)) };
            share.SetResourceReference(MeterBar.TrackProperty, "Track");
            name.Children.Add(share);
            row.Children.Add(At(name, 0, 1));
            var tokens = Right(Text("", Scaled(17), "Text")); tokens.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(At(tokens, 0, 2));
            var dollars = Right(Text("", Scaled(15), "Muted")); dollars.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(At(dollars, 0, 3));
            var mix = new MixBar { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }; if (CardSizing.ClaudeModels(width)) row.Children.Add(At(mix, 0, 4));
            var lines = Right(Text("", 12, "Muted")); lines.Margin = new Thickness(10, 0, 0, 0); if (CardSizing.ClaudeLines(width)) row.Children.Add(At(lines, 0, 5));
            list.Children.Add(names.Add(row, "ClaudeProjectRow" + index));
            string folder = group.Folder;
            fills.Add(now => {
                var g = view.Groups(period).FirstOrDefault(x => String.Equals(x.Folder, folder, StringComparison.OrdinalIgnoreCase));
                if (g == null) return;
                title.SetResourceReference(TextBlock.ForegroundProperty, g.NeedsYou ? "Warning" : "Text");
                if (!folded) info.Text = ClaudeText.GroupDetail(g);
                long total = view.Groups(period).Sum(x => x.Tokens);
                share.Value = total <= 0 ? 0 : 100.0 * g.Tokens / total;
                tokens.Text = Counted ? ClaudeUsage.Format(g.Tokens) : "—"; dollars.Text = Counted ? ClaudeText.Dollars(g.Cost) : "";
                mix.Shares = g.Mix; mix.ToolTip = ClaudeText.Mix(g.Mix); mix.InvalidateVisual();
                SetLines(lines, g.Added, g.Removed);
                RenderGarage(garage, g);
            });
            if (folded) return;
            for (int j = 0; j < group.Threads.Count; j++)
            {
                var thread = group.Threads[j];
                var trow = LedgerRow(width, threadHeight);
                var car = Car(thread, group.Folder, 48, "ClaudeCar" + index + "_" + j, false);
                trow.Children.Add(car.Sprite);
                garage.Cars.Add(car);
                var what = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0), LastChildFill = true };
                var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var chipText = Text("", 11, "Text"); chip.Child = chipText; DockPanel.SetDock(chip, Dock.Left); what.Children.Add(chip);
                var where = Text("", 12, "Muted"); where.Margin = new Thickness(8, 0, 0, 0); where.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(where, Dock.Right); what.Children.Add(where);
                var name2 = names.Add(Trimmed(Text("", Scaled(14), "Text")), "ClaudeThreadTitle" + index + "_" + j); name2.VerticalAlignment = VerticalAlignment.Center; what.Children.Add(name2);
                trow.Children.Add(At(what, 0, 1));
                var ttokens = Right(Text("", Scaled(14), "Text")); ttokens.Margin = new Thickness(10, 0, 0, 0); trow.Children.Add(At(ttokens, 0, 2));
                var tdollars = Right(Text("", Scaled(14), "Muted")); tdollars.Margin = new Thickness(10, 0, 0, 0); trow.Children.Add(At(tdollars, 0, 3));
                var tmix = new MixBar { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) }; if (CardSizing.ClaudeModels(width)) trow.Children.Add(At(tmix, 0, 4));
                var tlines = Right(Text("", 12, "Muted")); tlines.Margin = new Thickness(10, 0, 0, 0); if (CardSizing.ClaudeLines(width)) trow.Children.Add(At(tlines, 0, 5));
                var tap = Tappable(trow, thread);
                tap.ToolTip = ClaudeText.ThreadTip(thread, period, DateTime.UtcNow, use24, Counted);
                car.Row = tap;
                list.Children.Add(names.Add(tap, "ClaudeThreadRow" + index + "_" + j));
                string id = thread.Id;
                fills.Add(now => {
                    var t = view.Threads.FirstOrDefault(x => x.Id == id);
                    if (t == null) return;
                    tap.Opacity = t.Running ? 1 : 0.62;
                    chipText.Text = ClaudeText.Chip(t.Pit).ToUpperInvariant();
                    SetChip(chip, chipText, t.Pit);
                    name2.Text = ClaudeText.ThreadTitle(t, now, use24);
                    name2.SetResourceReference(TextBlock.ForegroundProperty, t.Pit == PitState.Need ? "Warning" : "Text");
                    where.Text = ClaudeText.Where(t, now);
                    ttokens.Text = Counted ? ClaudeUsage.Format(t.TokensFor(period)) : "—"; tdollars.Text = Counted ? ClaudeText.Dollars(t.CostFor(period)) : "";
                    var m = t.MixFor(period); tmix.Shares = m; tmix.ToolTip = ClaudeText.Mix(m); tmix.InvalidateVisual();
                    var l = t.LinesFor(period); SetLines(tlines, l[0], l[1]);
                });
            }
        }
        private bool Counted { get { return view.LogsRead && !view.LogsFailed; } }
        private static Brush Solid(uint color) { var b = new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color)); b.Freeze(); return b; }
        private FrameworkElement Bubble(int count)
        {
            var bubble = new Border { MinWidth = 20, Height = 20, CornerRadius = new CornerRadius(10), Padding = new Thickness(5, 0, 5, 0) };
            bubble.SetResourceReference(Border.BackgroundProperty, "Text");
            var number = Text(count.ToString(CultureInfo.InvariantCulture), 12, "Page"); number.FontWeight = FontWeights.Bold; number.HorizontalAlignment = HorizontalAlignment.Center; number.VerticalAlignment = VerticalAlignment.Center;
            bubble.Child = number;
            return bubble;
        }
        private void SetChip(Border chip, TextBlock text, PitState state)
        {
            chip.BorderThickness = new Thickness(state == PitState.End || state == PitState.Idle ? 1 : 0);
            chip.SetResourceReference(Border.BorderBrushProperty, "ControlBorder");
            if (state == PitState.Need) { chip.SetResourceReference(Border.BackgroundProperty, "Warning"); text.Foreground = new SolidColorBrush(Color.FromRgb(0x11, 0x17, 0x0E)); text.FontWeight = FontWeights.Bold; return; }
            text.FontWeight = FontWeights.Normal;
            if (state == PitState.Done) { chip.Background = Solid(PitArt.Alpha(PitArt.Accents[1], 0.14)); text.Foreground = Solid(PitArt.Accents[1]); return; }
            if (state == PitState.Stuck) { chip.Background = Tint("Warning", 0.16); text.SetResourceReference(TextBlock.ForegroundProperty, "Warning"); return; }
            if (state == PitState.End || state == PitState.Idle) { chip.Background = Brushes.Transparent; text.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); return; }
            chip.Background = Tint("Accent", 0.16); text.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
        }
        private static void SetLines(TextBlock block, long added, long removed)
        {
            block.Inlines.Clear();
            if (added == 0 && removed == 0) { block.Inlines.Add(Colored("—", "Muted")); return; }
            block.Inlines.Add(new Run(ClaudeText.Added(added)) { Foreground = Solid(PitArt.Lime) });
            block.Inlines.Add(new Run(" " + ClaudeText.Removed(removed)) { Foreground = Solid(0xFFF09A9A) });
        }

        // The side panel: the 5-hour lanes and today's four facts.
        private FrameworkElement SidePanel()
        {
            var side = new StackPanel();
            var lanes = new StackPanel();
            lanes.Children.Add(Upper("Last 5 hours · laps of 10 min", 11));
            var bins = new List<LaneBins>();
            foreach (var lane in view.Lanes)
            {
                var row = Columns(new GridLength(72), Star); row.Margin = new Thickness(0, 6, 0, 0);
                row.Children.Add(Trimmed(Text(lane.Name, 12, "Muted")));
                var b = new LaneBins { Height = 16, Margin = new Thickness(8, 0, 0, 0), Color = PitArt.Accent(lane.Name), Bins = lane.Bins };
                bins.Add(b); row.Children.Add(At(b, 0, 1));
                lanes.Children.Add(row);
            }
            if (view.Lanes.Count == 0) { var none = Text("Nothing in the last 5 hours.", 12, "Muted"); none.Margin = new Thickness(0, 6, 0, 0); lanes.Children.Add(none); }
            var axis = Columns(new GridLength(72), Star); axis.Margin = new Thickness(0, 4, 0, 0);
            var times = new Grid { Margin = new Thickness(8, 0, 0, 0) };
            var start = Text("", 11, "Muted"); var middle = Text("", 11, "Muted"); middle.HorizontalAlignment = HorizontalAlignment.Center; var end = Text("now", 11, "Muted"); end.HorizontalAlignment = HorizontalAlignment.Right;
            times.Children.Add(start); times.Children.Add(middle); times.Children.Add(end);
            axis.Children.Add(At(times, 0, 1));
            lanes.Children.Add(axis);
            side.Children.Add(names.Add(Tile(lanes, 12, new Thickness(12, 10, 12, 10)), "ClaudeLanes"));
            var facts = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(4, 10, 4, 0) };
            var values = new TextBlock[4]; var keys = new TextBlock[4];
            for (int i = 0; i < 4; i++)
            {
                var fact = new WrapPanel { Margin = new Thickness(0, 0, 8, 4) };
                values[i] = Reading("", Scaled(20)); values[i].SetResourceReference(TextBlock.ForegroundProperty, "Text"); values[i].Margin = new Thickness(0, 0, 6, 0);
                keys[i] = Upper("", 11); keys[i].VerticalAlignment = VerticalAlignment.Bottom; keys[i].Margin = new Thickness(0, 0, 0, 3);
                fact.Children.Add(values[i]); fact.Children.Add(keys[i]);
                facts.Children.Add(names.Add(fact, "ClaudeFact" + i));
            }
            side.Children.Add(facts);
            fills.Add(now => {
                double max = Math.Max(1, view.Lanes.SelectMany(l => l.Bins).DefaultIfEmpty(0).Max());
                foreach (var b in bins) { b.Maximum = max; b.InvalidateVisual(); }
                start.Text = ClaudeText.Clock(now - ClaudePace.FiveHours, use24); middle.Text = ClaudeText.Clock(now - TimeSpan.FromMinutes(150), use24);
                var f = view.Facts;
                values[0].Text = Counted ? ClaudeUsage.Format(f.Tokens) : "—"; keys[0].Text = Counted ? "TODAY · " + ClaudeUsage.Money(f.Cost) : "TODAY";
                values[1].Text = ClaudeText.Percent(f.CacheHit); keys[1].Text = "CACHE HITS";
                values[2].Text = f.Telemetry ? f.Tools.ToString(CultureInfo.InvariantCulture) : "—"; keys[2].Text = f.Telemetry ? "TOOLS · " + f.Asked + " ASKED" : "TOOLS · NEEDS TELEMETRY";
                values[2].ToolTip = f.Telemetry ? f.Denied + " denied · " + f.Asked + " asked you" : "Tool calls come from Claude Code's telemetry (Settings → Claude → Update connection).";
                values[3].Inlines.Clear();
                if (f.Telemetry && (f.Added > 0 || f.Removed > 0)) { values[3].Inlines.Add(new Run(ClaudeText.Added(f.Added)) { Foreground = Solid(PitArt.Lime) }); keys[3].Text = ClaudeText.Removed(f.Removed) + " · " + ClaudeText.Threads(f.LinesThreads).ToUpperInvariant(); }
                else { values[3].Inlines.Add(new Run("—")); keys[3].Text = "LINES"; }
            });
            return names.Add(side, "ClaudeSide");
        }

        // ----- Medium -----

        // The two plan meters with pace, then the top garages with their cars inline (a waiting thread's first).
        private FrameworkElement BuildMedium(double width, double height)
        {
            double head = Math.Max(24, Math.Ceiling(15 * textScale * 1.34));
            var root = Rows(new GridLength(head), new GridLength(10), Auto, Star);
            root.Children.Add(Header(width, false));
            var tiles = Columns(Star, new GridLength(10), Star); tiles.Height = CardSizing.ClaudeMiniHeight;
            if (view.Limits != null && view.Limits.Known)
            {
                tiles.Children.Add(MiniTile(true));
                tiles.Children.Add(At(MiniTile(false), 0, 2));
            }
            else
            {
                var message = names.Add(Wrapped(ClaudeText.LimitsMissing(view), 13, CardSizing.ClaudeMiniHeight - 16), "ClaudeLimits"); message.VerticalAlignment = VerticalAlignment.Center;
                tiles.Children.Add(At(Tile(message, 12, new Thickness(10, 8, 10, 8)), 0, 0, 3));
            }
            root.Children.Add(At(tiles, 2));
            var groups = view.Groups(period);
            double rowHeight = CardSizing.ClaudeMediumRow(textScale), room = height - head - 10 - CardSizing.ClaudeMiniHeight - 6;
            int fit = Math.Max(1, (int)Math.Floor(room / rowHeight));
            var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (groups.Count == 0) list.Children.Add(Empty(width));
            bool money = width >= 400;
            for (int i = 0; i < Math.Min(fit, groups.Count); i++)
            {
                var group = groups[i];
                bool folded = collapsed.Contains(group.Folder);
                var row = new Grid { Height = rowHeight };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = Star });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Scaled(64) + 10) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(money ? Scaled(56) + 10 : 0) });
                var line = new Border { BorderThickness = new Thickness(0, 1, 0, 0) }; line.SetResourceReference(Border.BorderBrushProperty, "ControlBorder"); Grid.SetColumnSpan(line, 4); row.Children.Add(line);
                var garage = Garage(group, 36, "ClaudeGarage" + i);
                row.Children.Add(garage.Tap);
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
                var first = new DockPanel { LastChildFill = true };
                var inline = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(inline, Dock.Right);
                double nameRoom = width - 48 - 6 - (Scaled(64) + 10) - (money ? Scaled(56) + 10 : 0);
                int shown = folded ? 0 : CardSizing.ClaudeInlineCars(nameRoom, group.Threads.Count);
                if (folded) inline.Children.Add(Bubble(group.Threads.Count));
                for (int j = 0; j < shown; j++)
                {
                    var car = Car(group.Threads[j], group.Folder, 30, "ClaudeCar" + i + "_" + j, true);
                    car.Sprite.Margin = new Thickness(0, 0, 2, 0);
                    var tap = Tappable(car.Sprite, group.Threads[j]); tap.ToolTip = ClaudeText.ThreadTip(group.Threads[j], period, DateTime.UtcNow, use24, Counted);
                    car.Row = tap; inline.Children.Add(tap); garage.Cars.Add(car);
                }
                if (!folded && shown < group.Threads.Count) { var more = Caption("+" + (group.Threads.Count - shown)); more.VerticalAlignment = VerticalAlignment.Center; more.Margin = new Thickness(2, 0, 0, 0); inline.Children.Add(more); }
                first.Children.Add(inline);
                var name = names.Add(Trimmed(Text(group.Name, Scaled(15), "Text")), "ClaudeProject" + i); name.FontWeight = FontWeights.SemiBold; name.VerticalAlignment = VerticalAlignment.Center;
                first.Children.Add(name);
                text.Children.Add(first);
                var sub = names.Add(Trimmed(Text("", 12, "Muted")), "ClaudeProjectDetail" + i); text.Children.Add(sub);
                row.Children.Add(At(text, 0, 1));
                var tokens = Right(Text("", Scaled(15), "Text")); tokens.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(At(tokens, 0, 2));
                var dollars = Right(Text("", Scaled(14), "Muted")); dollars.Margin = new Thickness(10, 0, 0, 0); if (money) row.Children.Add(At(dollars, 0, 3));
                list.Children.Add(names.Add(row, "ClaudeProjectRow" + i));
                string folder = group.Folder;
                fills.Add(now => {
                    var g = view.Groups(period).FirstOrDefault(x => String.Equals(x.Folder, folder, StringComparison.OrdinalIgnoreCase));
                    if (g == null) return;
                    var asking = g.Threads.FirstOrDefault(t => t.Running && t.State == ClaudeState.NeedsYou);
                    name.SetResourceReference(TextBlock.ForegroundProperty, asking != null ? "Warning" : "Text");
                    sub.Text = asking != null ? "Needs you · " + ClaudeText.ThreadTitle(asking, now, use24) : folded ? ClaudeText.Cars(g.Threads.Count) + " in the garage" : ClaudeText.GroupDetail(g);
                    sub.SetResourceReference(TextBlock.ForegroundProperty, asking != null ? "Warning" : "Muted");
                    tokens.Text = Counted ? ClaudeUsage.Format(g.Tokens) : "—"; dollars.Text = Counted ? "$" + ClaudeText.Dollars(g.Cost) : "";
                    RenderGarage(garage, g);
                });
            }
            root.Children.Add(At(names.Add(list, "ClaudeGarages"), 3));
            return root;
        }
        // A mini plan tile: the 5-hour or the week's percentage, its reset and pace, over a pace meter.
        private Border MiniTile(bool fiveHour)
        {
            var stack = new StackPanel();
            var top = new DockPanel();
            var reset = Text("", 12, "Muted"); DockPanel.SetDock(reset, Dock.Right); top.Children.Add(reset);
            top.Children.Add(Upper(fiveHour ? "5-hour" : "Week", 12));
            stack.Children.Add(top);
            var middle = new DockPanel { Margin = new Thickness(0, 2, 0, 4) };
            var pace = Text("", 12, "Muted"); pace.VerticalAlignment = VerticalAlignment.Bottom; pace.Margin = new Thickness(0, 0, 0, 4); DockPanel.SetDock(pace, Dock.Right); middle.Children.Add(pace);
            var value = Reading("—", 28); value.SetResourceReference(TextBlock.ForegroundProperty, "Text"); middle.Children.Add(value);
            stack.Children.Add(middle);
            var bar = new PaceBar { Height = 8 }; stack.Children.Add(bar);
            string name = fiveHour ? "ClaudeFiveHour" : "ClaudeWeek";
            names.Add(value, name + "Percent"); names.Add(reset, name + "Reset"); names.Add(pace, name + "Pace"); names.Add(bar, name + "Bar");
            fills.Add(now => {
                var w = fiveHour ? view.Limits.FiveHour : view.Limits.Week;
                double pct = ClaudeText.Percent(w, now), elapsed = ClaudePace.Elapsed(w, fiveHour ? ClaudePace.FiveHours : ClaudePace.Week, now), projection = ClaudePace.Projection(pct, elapsed);
                if (w == null) value.Text = "—"; else SetBig(value, pct, ClaudeText.Warns(pct) ? "Warning" : "Text");
                reset.Text = w == null || !w.ResetsAt.HasValue ? "" : !w.Current(now) ? "reset" : pct >= 100 ? "back " + ClaudeText.When(w.ResetsAt.Value, now, use24) : ClaudeText.When(w.ResetsAt.Value, now, use24);
                reset.SetResourceReference(TextBlock.ForegroundProperty, pct >= 100 ? "Warning" : "Muted");
                pace.Text = Double.IsNaN(projection) ? "" : "pace " + Math.Round(projection) + "%"; pace.SetResourceReference(TextBlock.ForegroundProperty, ClaudePace.Warns(projection) ? "Warning" : "Muted");
                bar.Value = pct / 100; bar.Projection = Double.IsNaN(projection) ? Double.NaN : projection / 100; bar.Pace = elapsed; bar.Warn = ClaudePace.Warns(projection) || ClaudeText.Warns(pct); bar.InvalidateVisual();
            });
            return Tile(stack, 12, new Thickness(10, 8, 10, 8));
        }

        // ----- Small -----

        // The car that needs you (or the busiest running one), its project and state, then the 5-hour meter with pace.
        private FrameworkElement BuildSmall(double width, double height)
        {
            bool meter = view.Limits != null && view.Limits.Known, caption = CardSizing.ClaudeSmallMeter(height, textScale);
            var root = Rows(Star, Auto);
            var first = view.Sessions.FirstOrDefault();
            var top = Columns(Auto, Star); top.VerticalAlignment = VerticalAlignment.Center;
            double size = CardSizing.ClaudeSmallCar(width, height - (caption ? CardSizing.ClaudeSmallMeterHeight : 0));
            var car = Car(first, first == null ? "" : first.Project, size, "ClaudeSmallCar", true);
            var tap = Tappable(car.Sprite, first); car.Row = tap;
            top.Children.Add(tap);
            var side = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            if (first == null)
            {
                var message = names.Add(Wrapped(view.Connected ? ClaudeText.NoSessions : ClaudeText.NotConnected, width < 300 ? 13 : 15, height - (caption ? CardSizing.ClaudeSmallMeterHeight : 0)), "ClaudeMessage");
                side.Children.Add(message);
            }
            else
            {
                var project = names.Add(Trimmed(Text("", Scaled(17), "Text")), "ClaudeTitle0"); project.FontWeight = FontWeights.SemiBold; side.Children.Add(project);
                var state = names.Add(Trimmed(Text("", 13, "Muted")), "ClaudeState0"); side.Children.Add(state);
                var more = names.Add(Trimmed(Text("", 12, "Muted")), "ClaudeMore"); side.Children.Add(more);
                string id = first.Id;
                fills.Add(now => {
                    var s = view.Sessions.FirstOrDefault(x => x.Id == id); if (s == null) return;
                    project.Text = s.ProjectName;
                    state.Text = ClaudeText.StateWords(s.State, s.Since, now, true);
                    state.SetResourceReference(TextBlock.ForegroundProperty, s.State == ClaudeState.NeedsYou ? "Warning" : "Muted");
                    int working = view.Sessions.Count(x => x.Id != id && x.State == ClaudeState.Working), others = view.Groups(period).Sum(g => g.Threads.Count) - 1 - working;
                    var parts = new List<string>();
                    if (working > 0) parts.Add("+" + working + " working");
                    if (others > 0) parts.Add(others + " more " + (others == 1 ? "thread" : "threads"));
                    if (view.Sessions.Any(x => x.Id != id && x.State == ClaudeState.NeedsYou)) parts.Insert(0, "another needs you");
                    more.Text = String.Join(" · ", parts);
                });
            }
            if (view.Sample) side.Children.Add(names.Add(Caption("SAMPLE"), "ClaudeFlag"));
            top.Children.Add(At(side, 0, 1));
            root.Children.Add(top);
            if (meter && caption)
            {
                var bottom = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                var line = new DockPanel();
                var right = Text("", 12, "Muted"); right.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(right, Dock.Right); line.Children.Add(right);
                var label = Upper("5-hour", 12); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(0, 0, 8, 0); DockPanel.SetDock(label, Dock.Left); line.Children.Add(label);
                var value = Reading("—", 28); value.SetResourceReference(TextBlock.ForegroundProperty, "Text"); line.Children.Add(value);
                bottom.Children.Add(line);
                var bar = names.Add(new PaceBar { Height = 8, Margin = new Thickness(0, 4, 0, 0) }, "ClaudeSmallMeter"); bottom.Children.Add(bar);
                names.Add(value, "ClaudeSmallPercent"); names.Add(right, "ClaudeSmallPace");
                fills.Add(now => {
                    var w = view.Limits.FiveHour;
                    double pct = ClaudeText.Percent(w, now), elapsed = ClaudePace.Elapsed(w, ClaudePace.FiveHours, now), projection = ClaudePace.Projection(pct, elapsed);
                    value.Text = w == null ? "—" : Math.Round(pct) + "%"; value.SetResourceReference(TextBlock.ForegroundProperty, ClaudeText.Warns(pct) ? "Warning" : "Text");
                    var parts = new List<string>();
                    if (!Double.IsNaN(projection)) parts.Add("pace " + Math.Round(projection) + "%");
                    if (w != null && w.ResetsAt.HasValue && w.Current(now)) parts.Add(ClaudeText.When(w.ResetsAt.Value, now, use24));
                    if (ClaudeText.Old(view, now) && width >= 280) parts.Add("as of " + ClaudeText.Clock(view.Limits.UpdatedAt, use24));
                    right.Text = String.Join(" · ", parts); right.SetResourceReference(TextBlock.ForegroundProperty, ClaudePace.Warns(projection) ? "Warning" : "Muted");
                    bar.Value = pct / 100; bar.Projection = Double.IsNaN(projection) ? Double.NaN : projection / 100; bar.Pace = elapsed; bar.Warn = ClaudePace.Warns(projection) || ClaudeText.Warns(pct); bar.InvalidateVisual();
                });
                root.Children.Add(At(bottom, 1));
            }
            else if (caption)
            {
                var message = names.Add(Trimmed(Caption(view.Plan == ClaudePlanState.Expired ? "Claude sign-in ended" : view.Plan == ClaudePlanState.SignedOut ? "Sign in for plan limits" : ClaudeText.LimitsMissing(view))), "ClaudeLimits");
                message.Margin = new Thickness(0, 6, 0, 0);
                root.Children.Add(At(message, 1));
            }
            return root;
        }

        // ----- Filling in -----

        private void Render()
        {
            if (view == null || rolling != null) return;
            DateTime now = DateTime.UtcNow;
            // The one state allowed to shout: an Amber border around the whole card.
            if (view.NeedsYou > 0) { card.SetResourceReference(Border.BorderBrushProperty, "Warning"); card.BorderThickness = new Thickness(2); }
            else { card.ClearValue(Border.BorderBrushProperty); card.ClearValue(Border.BorderThicknessProperty); }
            foreach (var fill in fills) fill(now);
            var threads = view.Threads.ToDictionary(t => t.Id);
            double fiveHour = view.Limits == null ? 0 : ClaudeText.Percent(view.Limits.FiveHour, now);
            foreach (var part in cars)
            {
                ClaudeSession t = null; if (part.Id != null) threads.TryGetValue(part.Id, out t);
                RenderCar(part, t, fiveHour);
            }
            clock.Update();
        }
        private void RenderCar(CarPart part, ClaudeSession t, double fiveHour)
        {
            PitState state = t == null ? (view.Connected ? PitState.Idle : PitState.End) : t.Pit;
            uint color = t == null ? PitArt.Accents[0] : PitArt.Accent(t.ProjectName);
            double share = t == null ? 0 : ClaudePace.Share(t.Rate, view.Scale);
            bool fuel = t != null && t.Running && ClaudePace.Fuel(fiveHour);
            var look = new PitArt.CarLook { Car = t == null ? PitCar.Sports : t.Car, Fuel = fuel, Hot = state == PitState.Work && ClaudePace.Hot(share), Fast = ClaudePace.Heavy(share) };
            string key; var frames = PitSprite.CarFrames(color, state, look, part.Width, out key);
            PitState before = state; bool known = part.Id != null && seen.TryGetValue(part.Id, out before);
            bool restart = known && before != state && (state == PitState.Done || state == PitState.End) && !quiet && live;
            if (part.Id != null) seen[part.Id] = state;
            part.Sprite.Fps = quiet ? 0 : PitSprite.CarFps(state, share, fuel);
            part.Sprite.Once = state == PitState.Done || state == PitState.End;
            part.Sprite.Show(key, frames, PitSprite.CarOverflow, PitSprite.StillFrame(state), restart);
            if (quiet || !live) part.Sprite.Hold();
            part.Sprite.Opacity = part.Dim && state == PitState.End ? 0.6 : 1;
        }
        private void RenderGarage(GaragePart part, ClaudeProjectGroup group)
        {
            bool closed = collapsed.Contains(group.Folder);
            var state = group.Garage;
            string key; var frames = PitSprite.GarageFrames(PitArt.Accent(group.Name), state, closed, group.Threads.Count, part.Sprite.Width, out key);
            part.Sprite.Fps = quiet || !(state == PitState.Need || (state == PitState.Work && !closed)) ? 0 : 3;
            part.Sprite.Once = false;
            part.Sprite.Show(key, frames, 0, 0, false);
            if (quiet || !live) part.Sprite.Hold();
            part.Sprite.Opacity = state == PitState.End ? 0.6 : 1;
        }

        // ----- Rolling in and out (Ali, 1 Nov 2026): about 1.2 s, instant in Quiet mode -----

        private sealed class RollState
        {
            public GaragePart Garage; public bool Out; public Action Done; public double T;
            public PitSprite Back, Front; public uint Color; public int Count;
            public Point Bay; public double GarageSize; public Point GarageAt;
            public readonly List<RollCar> Cars = new List<RollCar>();
        }
        private sealed class RollCar { public CarPart Part; public PitSprite Moving; public Point Target, Apron; public double Width; public PitCar Car; }

        // Where a piece sits on the overlay: its top left in the card body's coordinates.
        private Point On(FrameworkElement element) { return element.TranslatePoint(new Point(0, 0), host); }

        private void StartRoll(GaragePart garage, bool outward, Action done)
        {
            if (!garage.Sprite.IsLoaded || garage.Sprite.ActualWidth <= 0) { if (done != null) done(); else ShowGarage(garage); return; }
            var group = view.Groups(period).FirstOrDefault(g => String.Equals(g.Folder, garage.Folder, StringComparison.OrdinalIgnoreCase));
            string name = group == null ? garage.Folder : group.Name;
            var roll = new RollState { Garage = garage, Out = outward, Done = done, Color = PitArt.Accent(name), Count = group == null ? garage.Cars.Count : group.Threads.Count };
            roll.GarageSize = garage.Sprite.ActualWidth; roll.GarageAt = On(garage.Sprite);
            roll.Bay = new Point(roll.GarageAt.X + roll.GarageSize * 0.5, roll.GarageAt.Y + roll.GarageSize * 0.93);
            layer.Children.Clear();
            roll.Back = Piece(roll.GarageAt, roll.GarageSize, roll.GarageSize, 0);
            roll.Back.Show("back|" + roll.Color, new[] { Overlay("back|" + roll.Color, PitArt.GarageBack(roll.Color), roll.GarageSize, 100, 100) }, 0, 0, false);
            roll.Front = Piece(roll.GarageAt, roll.GarageSize, roll.GarageSize, 2);
            foreach (var c in garage.Cars.Where(c => c.Sprite.IsLoaded && c.Sprite.ActualWidth > 0))
            {
                var at = On(c.Sprite);
                var target = new Point(at.X + c.Sprite.ActualWidth / 2, at.Y + c.Sprite.ActualHeight);
                ClaudeSession t = view.Threads.FirstOrDefault(x => x.Id == c.Id);
                var car = new RollCar { Part = c, Target = target, Apron = new Point(target.X + c.Sprite.ActualWidth * 1.4, target.Y), Width = c.Sprite.ActualWidth, Car = t == null ? PitCar.Sports : t.Car };
                car.Moving = Piece(new Point(0, 0), car.Width, car.Width * 60 / 140, 3);
                car.Moving.Visibility = Visibility.Hidden;
                roll.Cars.Add(car);
                if (outward) c.Sprite.Visibility = Visibility.Hidden;
            }
            garage.Sprite.Visibility = Visibility.Hidden;
            rolling = roll;
            clock.Extra = StepRoll;
            clock.Update();
            DrawRoll();
        }
        private PitSprite Piece(Point at, double width, double height, int z)
        {
            var sprite = new PitSprite { Width = width, Height = height };
            Canvas.SetLeft(sprite, at.X); Canvas.SetTop(sprite, at.Y); Panel.SetZIndex(sprite, z);
            layer.Children.Add(sprite);
            return sprite;
        }
        private static System.Windows.Media.Imaging.BitmapSource Overlay(string key, List<PitOp> ops, double width, double boxWidth, double boxHeight)
        {
            int px = PitRaster.PixelSize(width), w = Math.Max(4, (int)Math.Round(width / px)), h = Math.Max(4, (int)Math.Round(width * boxHeight / boxWidth / px));
            return PitRaster.Frame("roll|" + key + "|" + w, () => ops, new Rect(0, 0, boxWidth, boxHeight), w, h);
        }
        private void StepRoll(double seconds)
        {
            if (rolling == null) { clock.Extra = null; return; }
            rolling.T += seconds;
            if (rolling.T >= Roll.TotalSeconds) { EndRoll(true); return; }
            DrawRoll();
        }
        private static double Ease(double t) { return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2; }
        private static double Span(double t, double a, double b) { return Math.Max(0, Math.Min(1, (t - a) / (b - a))); }
        private static double Lerp(double a, double b, double t) { return a + (b - a) * t; }

        // One frame of the roll: the shutter, then each car's path, one after another.
        private void DrawRoll()
        {
            var r = rolling; double t = r.T;
            int n = r.Cars.Count, k;
            double drive = 0.6, stagger = n > 1 ? Math.Min(0.15, 0.3 / (n - 1)) : 0;
            bool sign = false;
            if (r.Out) k = t < 0.15 ? (int)Math.Round(8 * (1 - t / 0.15)) : t > 1.05 ? (int)Math.Round(4 * Span(t, 1.05, 1.2)) : 0;
            else { k = t < 0.15 ? (int)Math.Round(4 * (1 - t / 0.15)) : t > 1.05 ? (int)Math.Round(8 * Span(t, 1.05, 1.2)) : 0; sign = t > 1.15; }
            r.Front.Show("front|" + k + sign, new[] { Overlay("front|" + r.Color + "|" + k + "|" + (sign ? r.Count : 0), PitArt.GarageFront(r.Color, k, sign ? r.Count : 0, !sign), r.GarageSize, 100, 100) }, 0, 0, false);
            int wheel = (int)Math.Floor(t * 16) % 4 * 2;
            for (int i = 0; i < n; i++)
            {
                var car = r.Cars[i];
                int order = r.Out ? i : n - 1 - i;
                double start = 0.15 + order * stagger, u = Span(t, start, start + drive);
                if (t < start) { car.Moving.Visibility = Visibility.Hidden; if (!r.Out) car.Part.Sprite.Visibility = Visibility.Visible; continue; }
                if (!r.Out) car.Part.Sprite.Visibility = Visibility.Hidden;
                car.Moving.Visibility = u >= 1 && r.Out ? Visibility.Hidden : Visibility.Visible;
                if (u >= 1 && r.Out) { car.Part.Sprite.Visibility = Visibility.Visible; continue; }
                if (u >= 1) { car.Moving.Visibility = Visibility.Hidden; continue; }
                double bayScale = 0.4, frontWidth = car.Width * 0.55;
                if (r.Out)
                {
                    if (u < 0.45) { double v = Ease(u / 0.45); Place(car, "front", r.Color, Lerp(r.Bay.X, car.Apron.X, v), Lerp(r.Bay.Y, car.Apron.Y, v), frontWidth * Lerp(bayScale, 1, v), 1, v < 0.3 ? 1 : 3, 0); }
                    else if (u < 0.6) Place(car, "turn", r.Color, car.Apron.X, car.Apron.Y, car.Width, 0.45, 3, 0);
                    else { double v = Ease(Span(u, 0.6, 1)); Place(car, "side", r.Color, Lerp(car.Apron.X, car.Target.X, v), car.Target.Y, car.Width, 1, 3, wheel); }
                }
                else
                {
                    if (u < 0.4) { double v = Ease(u / 0.4); Place(car, "reverse", r.Color, Lerp(car.Target.X, car.Apron.X, v), car.Target.Y, car.Width, 1, 3, wheel); }
                    else if (u < 0.52) Place(car, "turnback", r.Color, car.Apron.X, car.Apron.Y, car.Width, 0.45, 3, 0);
                    else { double v = Ease(Span(u, 0.52, 1)); Place(car, v < 0.15 ? "brake" : "rear", r.Color, Lerp(car.Apron.X, r.Bay.X, v), Lerp(car.Apron.Y, r.Bay.Y, v), frontWidth * Lerp(1, bayScale, v), 1, v > 0.6 ? 1 : 3, 0); }
                }
            }
        }
        // Puts a moving car at a point (the middle of its bottom edge), in one of its views; z 1 is inside the bay,
        // behind the garage's walls.
        private void Place(RollCar car, string pose, uint color, double x, double bottom, double width, double squash, int z, int wheel)
        {
            System.Windows.Media.Imaging.BitmapSource bitmap; double height, over = 0;
            string look = ClaudePace.CarName(car.Car);
            if (pose == "front" || pose == "rear" || pose == "brake")
            {
                double full = car.Width * 0.55;
                bitmap = Overlay(pose + "|" + color, pose == "front" ? PitArt.Front(color, true) : PitArt.Rear(color, pose == "brake"), full, 80, 56);
                height = width * 56 / 80;
            }
            else
            {
                bool reverse = pose == "reverse" || pose == "turnback";
                var a = new PitArt.CarLook { Car = car.Car, Lights = !reverse, Reverse = reverse };
                string key; var frames = PitSprite.CarFrames(color, pose == "side" || pose == "reverse" ? PitState.Work : PitState.Idle, a, car.Width, out key);
                bitmap = frames[pose == "side" || pose == "reverse" ? wheel : 0];
                height = width * 60 / 140; over = PitSprite.CarOverflow;
            }
            double shown = width * squash;
            car.Moving.Width = shown; car.Moving.Height = height;
            Canvas.SetLeft(car.Moving, x - shown / 2); Canvas.SetTop(car.Moving, bottom - height);
            Panel.SetZIndex(car.Moving, z);
            car.Moving.Show(pose + "|" + look + "|" + wheel + "|" + Math.Round(width), new[] { bitmap }, over, 0, false);
        }
        private void EndRoll(bool finished)
        {
            var r = rolling;
            if (r == null) return;
            rolling = null; clock.Extra = null;
            if (layer != null) layer.Children.Clear();
            ShowGarage(r.Garage);
            foreach (var c in r.Cars) c.Part.Sprite.Visibility = Visibility.Visible;
            clock.Update();
            if (finished && r.Done != null) r.Done();
            else if (finished) Render();
        }
        private static void ShowGarage(GaragePart garage) { garage.Sprite.Visibility = Visibility.Visible; foreach (var c in garage.Cars) c.Sprite.Visibility = Visibility.Visible; }
    }
}
