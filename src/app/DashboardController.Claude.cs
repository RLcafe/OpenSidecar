using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Claude Code watcher (docs/design/claude-watcher.md, decisions 014–016): Settings → Claude, the card, the
    // header pill and the phone's claude section. While the card is on any page the heartbeat looks at the bridge's
    // session files, Claude Code's own session list and the Claude app's thread records (directory listings; only
    // changed files are read) every Settings → Claude "Update the card every" seconds (2 by default, Quiet mode or
    // not), so the pill can light up from another page; the logs are read on the same interval only while the card is
    // on the page shown. The telemetry receiver listens on loopback while Claude Code is connected, and the plan check
    // runs on its own interval (300 s by default, longer while Anthropic turns checks away) while signed in.
    // Package 3 (decision 025): while the card is on a page, Windows tells the dashboard when one of those folders
    // changes (ClaudeWatch), so the card follows at once and the interval above is only a 30-second fallback; a shown card
    // with sessions running is still worked out at the interval, since turns and revving go by the clock. New alerts
    // become a Windows notification and ride to the phone in every /state.
    public sealed partial class DashboardController
    {
        private ClaudeService claude;
        private ClaudeCard claudeCard;
        private ClaudeView claudeView;
        private PitSprite pillCar;
        private Task claudeScan;
        private DateTime lastClaudeScan = DateTime.MinValue, lastClaudeRead = DateTime.MinValue, lastClaudeAlive = DateTime.MinValue;
        private bool claudeSample, claudeOnlineBusy, claudeDirty = true, claudeLogsWaiting, claudeWatchTried;
        // Package 3: what newly happened to the running sessions (Windows notifications and the phone's alerts), and when
        // the card was last worked out.
        private readonly ClaudeAlerts claudeAlerts = new ClaudeAlerts();
        private ClaudeToast claudeToast;
        private DateTime lastClaudeRefresh = DateTime.MinValue;
        private readonly HashSet<string> claudeNeeding = new HashSet<string>();
        private string claudePagesKey; private int claudeFirstPage = -1;
        // The project ticks shown in Settings → Claude, by folder, until Save.
        private readonly Dictionary<string, CheckBox> claudeProjectBoxes = new Dictionary<string, CheckBox>(StringComparer.OrdinalIgnoreCase);
        // The card and service, for the checks (tests/Claude/ClaudeTests.cs).
        public ClaudeCard ClaudeCardView { get { return claudeCard; } }
        public ClaudeService Claude { get { return claude; } }

        // The ClaudeHook.exe that came with this copy of Widget Screen; Connect copies it beside settings.xml.
        private static string BundledHook { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ClaudeBridge.HookExe); } }

        private void InitializeClaude()
        {
            claude = new ClaudeService(SettingsStore.ClaudePath(settingsPath), ClaudeConnector.DefaultConfigFolder, SettingsStore.ClaudeSignInPath(settingsPath));
            claudeCard = new ClaudeCard(window, Control<Grid>("ClaudeBody"), Control<Border>("ClaudeCard"), id => FocusClaudeSession(id), SetClaudeCollapsed, ChooseClaudePeriod);
            claude.PlanEvery = TimeSpan.FromSeconds(settings.ClaudePlanSeconds);
            // The pill's car waits with its hazards on (one still frame: the header never animates).
            pillCar = new PitSprite { Width = 40, Height = 17, VerticalAlignment = VerticalAlignment.Center };
            string pillKey; pillCar.Show("pill", PitSprite.CarFrames(PitArt.Amber, PitState.Need, new PitArt.CarLook { Car = PitCar.Sports }, 40, out pillKey), PitSprite.CarOverflow, 0, false);
            Control<Grid>("ClaudePillCar").Children.Add(pillCar);
            // Minimising doesn't hide the card's elements, and the heartbeat skips the card while minimised, so the frame
            // clock is told here (review fix 5).
            window.StateChanged += (sender, e) => UpdateClaudeLive();
            Button("ClaudePill", ShowClaudePage);
            Button("ClaudeConnectButton", ConnectClaude);
            Button("ClaudeDisconnectButton", DisconnectClaude);
            Button("ClaudeSignInButton", SignInClaude);
            Button("ClaudeSignOutButton", SignOutClaude);
            Button("ClaudeCardSecondsLess", () => StepClaudeSeconds("ClaudeCardSecondsBox", -1, ClaudeIntervals.Card, settings.ClaudeCardSeconds));
            Button("ClaudeCardSecondsMore", () => StepClaudeSeconds("ClaudeCardSecondsBox", 1, ClaudeIntervals.Card, settings.ClaudeCardSeconds));
            Button("ClaudePlanSecondsLess", () => StepClaudeSeconds("ClaudePlanSecondsBox", -10, ClaudeIntervals.Plan, settings.ClaudePlanSeconds));
            Button("ClaudePlanSecondsMore", () => StepClaudeSeconds("ClaudePlanSecondsBox", 10, ClaudeIntervals.Plan, settings.ClaudePlanSeconds));
            Control<TextBox>("ClaudePlanSecondsBox").TextChanged += (sender, e) => UpdateClaudePlanNote();
            claudeToast = new ClaudeToast(id => { if (!disposed) FocusClaudeSession(id); });
            if (Program.Rendering) return;
            // A watched folder changed or telemetry arrived (package 3): the card looks at once, on its own thread.
            claude.Changed = () => { try { window.Dispatcher.BeginInvoke(new Action(ClaudeWatchFired)); } catch (Exception) { } };
            claude.CheckConnected();
            // After an update, Claude Code runs the bridge that came with it.
            if (claude.Connected) { claude.Connector.RefreshHook(BundledHook); claude.StartTelemetry(); }
            if (claude.Account.StartupWarning != null && pendingMessage == null) pendingMessage = claude.Account.StartupWarning;
        }

        // A labelled fixture instead of this PC's sessions, for renders (--render-claude-sample) and checks.
        public void UseClaudeSample(bool needsYou)
        {
            claudeSample = true;
            claudeView = ClaudeView.Fixture(DateTime.UtcNow, needsYou);
            ClaudeChoices(claudeView);
            claudeCard.Update(claudeView);
            UpdateClaudePill();
        }

        // Whether the Claude card is shown on any page, for the pill and the phone. Worked out again only after a save
        // or when the screen changes.
        private bool ClaudeOnAnyPage { get { return ClaudePage >= 0; } }
        // The first page that shows the Claude card, or -1.
        private int ClaudePage
        {
            get
            {
                string key = settingsVersion + "|" + Editor.CanvasSize + "|" + WidgetLayout.PageCount(settings);
                if (key == claudePagesKey) return claudeFirstPage;
                claudePagesKey = key; claudeFirstPage = -1;
                for (int i = 0; i < WidgetLayout.PageCount(settings) && claudeFirstPage < 0; i++)
                    if (WidgetLayout.ForCanvas(settings, i, Editor.CanvasSize).Any(p => p.Id == "Claude" && p.Visible)) claudeFirstPage = i;
                return claudeFirstPage;
            }
        }

        // Which projects the card shows (Settings → Claude → Projects).
        private Func<string, bool> ClaudeFilter { get { return ClaudeService.Filter(settings.ClaudeProjects, settings.ClaudeShowNewProjects); } }
        private ClaudePeriod ClaudePeriodChosen { get { ClaudePeriod p; return Enum.TryParse(settings.ClaudePeriod, out p) ? p : ClaudePeriod.Today; } }
        // The card's own refresh, in seconds (Settings → Claude): the user's number wins over Quiet mode.
        private TimeSpan ClaudeEvery { get { return TimeSpan.FromSeconds(ClaudeIntervals.Card(settings.ClaudeCardSeconds)); } }

        // A project folded into its critter or let out (the card, or the phone's claude.collapse): kept in settings.xml
        // at once, outside the Settings panel, so the phone and the next start see it.
        public void SetClaudeCollapsed(string folder, bool fold)
        {
            string key = ClaudeUsage.Key(folder);
            if (key.Length == 0) return;
            var set = new List<string>((settings.ClaudeCollapsed ?? new string[0]).Where(f => !String.Equals(f, key, StringComparison.OrdinalIgnoreCase)));
            if (fold) set.Add(key);
            settings.ClaudeCollapsed = set.ToArray();
            Persist();
            if (claudeView != null) { if (fold) claudeView.Collapsed.Add(key); else claudeView.Collapsed.Remove(key); }
        }
        private void ChooseClaudePeriod(ClaudePeriod period) { settings.ClaudePeriod = period.ToString(); Persist(); }

        private void TickClaude()
        {
            bool shown = Editor.IsVisible("Claude") && !Editor.Editing;
            claudeCard.Tick(ClaudeStill, shown);
            UpdateClaudeLive();
            if (claudeSample) return;
            // Renders never read this PC's sessions or logs, or ask for the plan's usage; they use the labelled sample.
            if (Program.Rendering) return;
            if (!Editor.IsVisible("Claude") && !ClaudeOnAnyPage)
            {
                if (claudeView != null && claudeView.NeedsYou > 0) { claudeView = null; UpdateClaudePill(); }
                // Nothing shows the card: no folder is watched (package 3).
                if (claudeWatchTried) { claude.Watch.Stop(); claudeWatchTried = false; }
                return;
            }
            DateTime now = DateTime.UtcNow;
            bool changed = false;
            // The folders are watched while the card is on a page (package 3); folders that appear later join within 10 s.
            if (ticks % 10 == 0 || !claudeWatchTried) { claudeWatchTried = true; claude.Watch.Watch(claude.WatchFolders()); }
            // With the folders watched, reading every source is only a fallback every 30 s (or the chosen interval, if
            // longer); without them, the chosen interval as before. Within a second of it, so 2 s doesn't drift to 3.
            var every = claude.Watch.Running && ClaudeWatch.Fallback > ClaudeEvery ? ClaudeWatch.Fallback : ClaudeEvery;
            if (now - lastClaudeRead >= every - TimeSpan.FromMilliseconds(500))
            {
                lastClaudeRead = now;
                bool alive = now - lastClaudeAlive >= TimeSpan.FromSeconds(5);
                if (alive) lastClaudeAlive = now;
                changed |= claude.ReadSessions(now, alive);
                changed |= claude.ReadLimits();
                if (claude.TelemetryChanged) { claude.TelemetryChanged = false; changed = true; }
                if (Editor.IsVisible("Claude") && (claudeScan == null || claudeScan.IsCompleted)) ScanClaudeLogs();
            }
            // A log changed while the card was hidden, a scan was running or one ran moments ago: it's read now.
            if (claudeLogsWaiting && Editor.IsVisible("Claude") && (claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) ScanClaudeLogs();
            // Turns, revving and idling go by the clock, so a shown card with something running is worked out again at the
            // chosen interval even when no file changed.
            if (claude.Watch.Running && Editor.IsVisible("Claude") && claudeView != null && claudeView.Sessions.Count > 0 && now - lastClaudeRefresh >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) changed = true;
            if (ticks % 10 == 0)
            {
                bool was = claude.Connected; claude.CheckConnected(); changed |= was != claude.Connected;
                if (claude.Connected) claude.StartTelemetry(); else claude.Telemetry.Stop();
                changed |= claudeView != null && claudeView.TelemetryWarning != claude.TelemetryWarning();
            }
            if (changed || claudeDirty) RefreshClaudeView();
        }
        // The card's frame clock runs only while it's on the page, not being edited, and the window isn't minimised.
        private void UpdateClaudeLive() { claudeCard.SetLive(Editor.IsVisible("Claude") && !Editor.Editing && window.WindowState != WindowState.Minimized); }
        // Quiet mode keeps the cars still only when Settings → Claude says so (decision 019).
        private bool ClaudeStill { get { return settings.QuietMode && settings.ClaudeStillInQuiet; } }

        // The plan check runs on its interval while the widget is signed in to Claude, in the tray too (Ali, 1 Nov 2026).
        private void TickClaudeOnline()
        {
            if (Program.Rendering || claudeSample || claudeOnlineBusy || !claude.OnlineDue(DateTime.UtcNow)) return;
            CheckClaudeOnline();
        }

        private async void ScanClaudeLogs()
        {
            lastClaudeScan = DateTime.UtcNow; claudeLogsWaiting = false;
            var scan = Task.Run(() => claude.Usage.Scan(DateTime.UtcNow));
            claudeScan = scan;
            try { await scan; } catch (Exception) { }
            if (disposed) return;
            claudeDirty = true;
            // Watched, the new replies show as soon as they're read (package 3), not on the next heartbeat.
            if (claude.Watch.Running && Editor.IsVisible("Claude")) RefreshClaudeView();
        }

        // A watched folder changed or telemetry arrived (package 3): only what changed is read, and the card follows.
        // Log changes come in bursts while Claude writes, so they're read at most once per chosen interval (2 s by
        // default), as before; the heartbeat picks up one that has to wait.
        private void ClaudeWatchFired()
        {
            if (disposed || claudeSample || Program.Rendering) return;
            var kinds = claude.Watch.Take();
            if (kinds == ClaudeWatch.Kinds.None || (!Editor.IsVisible("Claude") && !ClaudeOnAnyPage)) return;
            DateTime now = DateTime.UtcNow;
            bool changed = false;
            if ((kinds & ClaudeWatch.Kinds.Sessions) != 0)
            {
                bool alive = now - lastClaudeAlive >= TimeSpan.FromSeconds(5);
                if (alive) lastClaudeAlive = now;
                changed |= claude.ReadSessions(now, alive);
                changed |= claude.ReadLimits();
            }
            if (claude.TelemetryChanged) { claude.TelemetryChanged = false; changed = true; }
            if ((kinds & ClaudeWatch.Kinds.Logs) != 0) claudeLogsWaiting = true;
            if (claudeLogsWaiting && Editor.IsVisible("Claude") && (claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) ScanClaudeLogs();
            if (changed) RefreshClaudeView();
        }

        private async void CheckClaudeOnline()
        {
            claudeOnlineBusy = true;
            try { if (await claude.CheckOnlineAsync(DateTime.UtcNow) && !disposed) { claudeDirty = true; if (SettingsOpen) UpdateClaudePlanStatus(); } }
            catch (Exception) { }
            finally { claudeOnlineBusy = false; }
        }

        // The folded projects and which threads Settings → Claude hides.
        private void ClaudeChoices(ClaudeView view)
        {
            view.Collapsed.UnionWith(settings.ClaudeCollapsed ?? new string[0]);
            view.ShowArchived = settings.ClaudeShowArchived; view.HideIdle = settings.ClaudeHideIdle;
        }
        private void RefreshClaudeView()
        {
            claudeDirty = false;
            if (claudeSample) return;
            lastClaudeRefresh = DateTime.UtcNow;
            claudeView = claude.View(DateTime.UtcNow, claude.Usage.Scanned ? claude.Usage.Entries : null, ClaudeFilter);
            ClaudeChoices(claudeView);
            claudeCard.Update(claudeView);
            NotifyClaude(claudeAlerts.Update(claudeView.Sessions, DateTime.UtcNow));
            // A chime for each session that newly needs you, if Settings → Claude asks for one.
            var needing = claudeView.Sessions.Where(s => s.State == ClaudeState.NeedsYou).Select(s => s.Id).ToList();
            bool fresh = needing.Any(id => !claudeNeeding.Contains(id));
            claudeNeeding.Clear(); foreach (var id in needing) claudeNeeding.Add(id);
            if (fresh && settings.ClaudeChime && !settings.QuietMode) Chime();
            UpdateClaudePill();
        }

        // Package 3: a Windows notification for a session that newly needs you, got stuck or finished a long turn, unless
        // Settings → Claude says not to or Quiet mode is on. The newest one shows (each replaces the last); the icon goes
        // once nothing needs you.
        private void NotifyClaude(List<ClaudeAlert> fresh)
        {
            if (claudeView == null || claudeToast == null) return;
            if (claudeView.NeedsYou == 0 && claudeView.Stuck == 0) claudeToast.Clear();
            if (fresh.Count == 0 || !settings.ClaudeToast || settings.QuietMode || Program.Rendering) return;
            var a = fresh.OrderBy(x => x.Kind == "need" ? 0 : x.Kind == "stuck" ? 1 : 2).First();
            string title = (a.Kind == "need" ? "Claude needs you" : a.Kind == "stuck" ? "Claude is stuck" : "Claude finished") + " · " + a.Project;
            string what = a.Kind == "need" ? ClaudeText.Ask(a.Text) : a.Text;
            if (what.Length == 0) what = a.Kind == "need" ? "Waiting for you" : a.Kind == "stuck" ? "Stopped on an error" : "Done";
            if (fresh.Count > 1) what += " (and " + (fresh.Count - 1) + " more)";
            claudeToast.Show(title, a.Title + ": " + what, a.Session, a.Kind != "done");
        }

        // The header pill: a session needs you while the Claude card is on another page.
        private void UpdateClaudePill()
        {
            bool show = claudeView != null && claudeView.NeedsYou > 0 && !Editor.IsVisible("Claude") && !Editor.Editing && (claudeSample || ClaudeOnAnyPage);
            Control<Button>("ClaudePill").Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowClaudePage()
        {
            if (ClaudePage >= 0) ShowPage(ClaudePage);
            UpdateClaudePill();
        }

        // Brings a session's terminal or editor forward (the one place the dashboard activates another window, because
        // the user tapped for it). Returns null when it worked, or what went wrong.
        private string FocusClaudeSession(string id)
        {
            var session = claudeView == null ? null : claudeView.Sessions.FirstOrDefault(s => s.Id == id);
            if (session == null || claudeSample) return null;
            var handle = ClaudeProcesses.Window(session.Window, session.WindowPid);
            string problem = handle == IntPtr.Zero ? "Couldn't find the window for " + session.Shown + "." : !ClaudeProcesses.Focus(handle) ? "Windows didn't let Widget Screen bring " + session.Shown + " forward. It's flashing on the taskbar." : null;
            if (problem != null) Notify(problem);
            return problem;
        }

        // ----- Settings → Claude -----

        private bool SettingsOpen { get { var overlay = window.FindName("SettingsOverlay") as UIElement; return overlay != null && overlay.Visibility == Visibility.Visible; } }

        private void UpdateClaudeSettings()
        {
            claude.CheckConnected();
            bool update = claude.Connector.NeedsUpdate();
            string portWarning = claude.TelemetryWarning();
            Text("ClaudeConnectionStatus", !claude.Connected ? "Not connected" : update ? "Connected by an earlier Widget Screen. Update the connection to add the latest hooks (API errors, subagents, tasks, model switches, usage-limit waits) and telemetry."
                : portWarning.Length > 0 ? portWarning + ". Update the connection to send telemetry to a free port." : "Connected. Every Claude Code session on this PC shows on the card.");
            update |= claude.Connected && portWarning.Length > 0;
            Text("ClaudeFolderText", "Claude Code folder: " + claude.ConfigFolder);
            var connect = Control<Button>("ClaudeConnectButton");
            connect.Content = update ? "Update connection" : "Connect Claude Code";
            connect.Visibility = !claude.Connected || update ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("ClaudeDisconnectButton").Visibility = claude.Connected ? Visibility.Visible : Visibility.Collapsed;
            var conflicts = claude.Connector.TelemetryConflicts();
            Text("ClaudeTelemetryText", conflicts.Count > 0 ? "Telemetry is left out: your settings.json already sets " + String.Join(", ", conflicts) + ". Remove those to let Widget Screen show live usage per session; until then usage comes from Claude Code's logs."
                : !claude.Connected ? "" : claude.Telemetry.Problem ?? (claude.Telemetry.LastReceived > DateTime.MinValue ? "Telemetry last arrived at " + ClaudeText.Clock(claude.Telemetry.LastReceived, settings.Use24HourClock) + "." : "Telemetry arrives from Claude Code sessions started after connecting."));
            Control<CheckBox>("ClaudeChimeChoice").IsChecked = settings.ClaudeChime;
            Control<CheckBox>("ClaudeToastChoice").IsChecked = settings.ClaudeToast;
            // Package 3: what the organisation's managed settings keep Widget Screen's connection from doing.
            var managed = claude.Connector.ManagedOverrides();
            Text("ClaudeManagedText", managed.Count == 0 ? "" : "Your organisation's managed Claude Code settings (" + claude.Connector.ManagedPath + ") take precedence over yours, so Widget Screen's " + String.Join(", ", managed) + (managed.Count == 1 ? " doesn't" : " don't") + " take effect. Ask whoever manages Claude Code on this PC to allow them.");
            Control<TextBlock>("ClaudeManagedText").Visibility = managed.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            Control<CheckBox>("ClaudeStillChoice").IsChecked = settings.ClaudeStillInQuiet;
            Control<CheckBox>("ClaudeApiValueChoice").IsChecked = settings.ClaudeShowApiValue;
            Control<CheckBox>("ClaudeHideArchivedChoice").IsChecked = !settings.ClaudeShowArchived;
            Control<CheckBox>("ClaudeHideIdleChoice").IsChecked = settings.ClaudeHideIdle;
            // The side panel's four facts (package 2).
            var facts = ClaudeFactKinds.Clean(settings.ClaudeFacts);
            for (int i = 0; i < 4; i++)
            {
                var box = Control<ComboBox>("ClaudeFact" + i + "Choice");
                box.ItemsSource = ClaudeFactKinds.All.Select(k => ClaudeFactKinds.Name(k)).ToList();
                box.SelectedIndex = Array.IndexOf(ClaudeFactKinds.All, facts[i]);
            }
            Control<CheckBox>("ClaudeShowNewChoice").IsChecked = settings.ClaudeShowNewProjects;
            Control<TextBox>("ClaudeCardSecondsBox").Text = settings.ClaudeCardSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Control<TextBox>("ClaudePlanSecondsBox").Text = settings.ClaudePlanSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateClaudePlanNote();
            UpdateClaudePlanStatus();
            FillClaudeProjects();
        }

        private void UpdateClaudePlanStatus()
        {
            var plan = claude.Plan; var limits = claude.Limits;
            string asOf = limits != null && limits.Known && limits.Source == "online" ? " Checked at " + ClaudeText.Clock(limits.UpdatedAt, settings.Use24HourClock) + "." : "";
            Text("ClaudePlanStatus", claude.Account.SigningIn ? "Waiting for you to sign in in your browser…"
                : plan == ClaudePlanState.Expired ? "Your Claude sign-in has ended. Sign in again to keep the plan meters current."
                : plan == ClaudePlanState.SignedOut ? "Not signed in. The card's plan meters need this."
                : plan == ClaudePlanState.NoMeters ? "Signed in. Anthropic says your plan has no usage meters, so the card shows none."
                : plan == ClaudePlanState.Failed ? (claude.BackingOff ? "Signed in. Anthropic asked Widget Screen to slow down; it tries again in " + ClaudeText.Wait(claude.NextWait) + "." : "Signed in. The last check didn't go through; it tries again in " + ClaudeText.Wait(claude.NextWait) + ".") + asOf
                : plan == ClaudePlanState.Reading ? "Signed in. Reading your plan's limits…" : "Signed in." + asOf);
            var signIn = Control<Button>("ClaudeSignInButton");
            signIn.Content = claude.Account.SigningIn ? "Cancel sign-in" : plan == ClaudePlanState.Expired ? "Sign in again" : "Sign in with Claude";
            signIn.Visibility = plan == ClaudePlanState.SignedOut || plan == ClaudePlanState.Expired || claude.Account.SigningIn ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("ClaudeSignOutButton").Visibility = plan != ClaudePlanState.SignedOut && !claude.Account.SigningIn ? Visibility.Visible : Visibility.Collapsed;
        }

        // One tick per project the sources know, newest first: its name, when it was last used and this week's usage.
        private void FillClaudeProjects()
        {
            var host = Control<StackPanel>("ClaudeProjectsList");
            host.Children.Clear(); claudeProjectBoxes.Clear();
            if (!claude.Usage.Scanned && !Program.Rendering) { try { claude.Usage.Scan(DateTime.UtcNow); } catch (Exception) { } }
            if (!Program.Rendering) claude.ReadSessions(DateTime.UtcNow, false);
            var projects = Program.Rendering || claudeSample ? new List<ClaudeProjectInfo>() : claude.Projects(DateTime.UtcNow, ClaudeFilter);
            Control<TextBlock>("ClaudeProjectsEmpty").Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var project in projects.Take(40))
            {
                var name = new TextBlock { Text = project.Name, TextTrimming = TextTrimming.CharacterEllipsis };
                var detail = new TextBlock { Text = "Last used " + project.LastUsed.ToLocalTime().ToString("MMM d", culture) + " · " + ClaudeText.Threads(project.Threads) + " this week · " + ClaudeUsage.Format(project.WeekTokens) + " tokens · " + ClaudeUsage.Money(project.WeekCost) + " at API prices", TextTrimming = TextTrimming.CharacterEllipsis };
                detail.SetResourceReference(FrameworkElement.StyleProperty, "Small");
                var text = new StackPanel(); text.Children.Add(name); text.Children.Add(detail);
                var box = new CheckBox { Content = text, IsChecked = project.Shown, ToolTip = project.Folder, Margin = new Thickness(0, 0, 0, 6) };
                System.Windows.Automation.AutomationProperties.SetName(box, "Show " + project.Name + " on the Claude card");
                claudeProjectBoxes[project.Folder] = box;
                host.Children.Add(box);
            }
        }

        // What Save keeps: a choice for every project listed (so a project ticked off stays off), plus earlier choices
        // for projects that aren't listed now.
        private void SaveClaudeChoices(AppSettings candidate)
        {
            candidate.ClaudeChime = Control<CheckBox>("ClaudeChimeChoice").IsChecked == true;
            candidate.ClaudeToast = Control<CheckBox>("ClaudeToastChoice").IsChecked == true;
            candidate.ClaudeStillInQuiet = Control<CheckBox>("ClaudeStillChoice").IsChecked == true;
            candidate.ClaudeShowApiValue = Control<CheckBox>("ClaudeApiValueChoice").IsChecked == true;
            candidate.ClaudeShowArchived = Control<CheckBox>("ClaudeHideArchivedChoice").IsChecked != true;
            candidate.ClaudeHideIdle = Control<CheckBox>("ClaudeHideIdleChoice").IsChecked == true;
            candidate.ClaudeFacts = ClaudeFactKinds.Clean(Enumerable.Range(0, 4).Select(i => { int at = Control<ComboBox>("ClaudeFact" + i + "Choice").SelectedIndex; return at >= 0 && at < ClaudeFactKinds.All.Length ? ClaudeFactKinds.All[at] : ""; }).ToArray());
            candidate.ClaudeShowNewProjects = Control<CheckBox>("ClaudeShowNewChoice").IsChecked == true;
            candidate.ClaudeCardSeconds = ClaudeIntervals.Parse(Control<TextBox>("ClaudeCardSecondsBox").Text, settings.ClaudeCardSeconds, ClaudeIntervals.Card);
            candidate.ClaudePlanSeconds = ClaudeIntervals.Parse(Control<TextBox>("ClaudePlanSecondsBox").Text, settings.ClaudePlanSeconds, ClaudeIntervals.Plan);
            if (candidate.ClaudePlanSeconds != settings.ClaudePlanSeconds) { claude.PlanEvery = TimeSpan.FromSeconds(candidate.ClaudePlanSeconds); claude.CheckSoon(); }
            var choices = (settings.ClaudeProjects ?? new ClaudeProjectChoice[0]).Where(c => c != null && c.Folder != null && !claudeProjectBoxes.ContainsKey(ClaudeUsage.Key(c.Folder))).ToList();
            foreach (var pair in claudeProjectBoxes) choices.Add(new ClaudeProjectChoice { Folder = pair.Key, Shown = pair.Value.IsChecked == true });
            candidate.ClaudeProjects = choices.ToArray();
            claudeDirty = true;
        }

        // The −/+ buttons beside the two interval boxes, and the note under the plan's when it's under a minute.
        private void StepClaudeSeconds(string box, int step, Func<int, int> clamp, int fallback)
        {
            var text = Control<TextBox>(box);
            int value = ClaudeIntervals.Parse(text.Text, fallback, clamp);
            text.Text = clamp(value + step).ToString(System.Globalization.CultureInfo.InvariantCulture);
            UpdateClaudePlanNote();
        }
        private void UpdateClaudePlanNote()
        {
            int plan = ClaudeIntervals.Parse(Control<TextBox>("ClaudePlanSecondsBox").Text, settings.ClaudePlanSeconds, ClaudeIntervals.Plan);
            Control<TextBlock>("ClaudePlanSecondsNote").Visibility = plan < ClaudeIntervals.PlanWarnBelow ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ConnectClaude()
        {
            bool update = claude.Connected;
            // Package 3: say up front what the organisation's managed settings will override.
            var managed = claude.Connector.ManagedOverrides();
            bool yes = await confirm.AskAsync("CLAUDE CODE", update ? "Update the connection?" : "Connect Claude Code?",
                "This adds Widget Screen's hooks, status line and telemetry settings to your Claude Code settings (settings.json). Telemetry goes only to Widget Screen on this PC and never includes your prompts. A backup is saved next to it. Your current status line keeps showing."
                + (managed.Count == 0 ? "" : " Your organisation's managed settings override Widget Screen's " + String.Join(", ", managed) + ", so " + (managed.Count == 1 ? "that part" : "those parts") + " won't work until they're allowed."), update ? "Update" : "Connect");
            if (!yes || disposed) return;
            try
            {
                // Binds the telemetry port first and writes the one it got (decision 023), then reads every source at
                // once, so sessions already running show without waiting for their next hook.
                claude.Connect(BundledHook);
                claude.CheckConnected();
                claude.ReadSessions(DateTime.UtcNow, true);
                if (claudeScan == null || claudeScan.IsCompleted) ScanClaudeLogs();
                Text("SettingsStatus", "Claude Code is connected. Sessions show on the Claude Code widget; turn it on in Edit layout. Sessions already open pick up telemetry when they restart.");
            }
            catch (Exception e) { Text("SettingsStatus", e is InvalidOperationException ? e.Message : "Couldn't connect Claude Code: " + e.Message); }
            UpdateClaudeSettings(); claudeDirty = true;
        }

        private async void DisconnectClaude()
        {
            bool yes = await confirm.AskAsync("CLAUDE CODE", "Disconnect Claude Code?", "This takes Widget Screen's hooks, status line and telemetry settings out of your Claude Code settings (settings.json) and puts your earlier status line back. A backup is saved next to it.", "Disconnect");
            if (!yes || disposed) return;
            try { claude.Connector.Disconnect(); claude.Telemetry.Stop(); Text("SettingsStatus", "Claude Code is disconnected."); }
            catch (Exception e) { Text("SettingsStatus", e is InvalidOperationException ? e.Message : "Couldn't disconnect Claude Code: " + e.Message); }
            UpdateClaudeSettings(); claudeDirty = true;
        }

        private async void SignInClaude()
        {
            if (claude.Account.SigningIn) { claude.Account.CancelSignIn(); return; }
            Text("SettingsStatus", "Sign in to Claude in your browser, then come back here.");
            var signing = claude.Account.SignInAsync(message => window.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) Text("SettingsStatus", message); })));
            UpdateClaudePlanStatus();
            try
            {
                await signing;
                if (disposed) return;
                Text("SettingsStatus", "Widget Screen is signed in to Claude. The plan meters fill in a moment.");
                claude.CheckSoon(); TickClaudeOnline();
            }
            catch (Exception e)
            {
                if (disposed) return;
                Text("SettingsStatus", e is OperationCanceledException ? "The Claude sign-in was cancelled." : e is UnauthorizedAccessException ? "Claude didn't accept the sign-in. Try again." : "Couldn't sign in to Claude: " + e.Message);
            }
            UpdateClaudePlanStatus(); claudeDirty = true;
        }

        private async void SignOutClaude()
        {
            bool yes = await confirm.AskAsync("CLAUDE", "Sign out of Claude?", "Widget Screen forgets its Claude sign-in on this PC. The plan meters stop updating until you sign in again. Claude Code's own sign-in isn't affected.", "Sign out");
            if (!yes || disposed) return;
            claude.Account.SignOut();
            Text("SettingsStatus", "Signed out of Claude.");
            UpdateClaudePlanStatus(); claudeDirty = true;
        }

        // ----- The phone link -----

        // In every /state: how many sessions need you while the card is on some page, and that page, so the phone's
        // header pill can light up from another page.
        private void RemoteClaudeAlert(Dictionary<string, object> state)
        {
            int page = ClaudePage;
            if (page >= 0 && !claudeSample)
            {
                bool changed = claude.ReadSessions(DateTime.UtcNow, false);
                if (changed || claudeDirty || claudeView == null) RefreshClaudeView();
            }
            state["claudeNeeds"] = page < 0 || claudeView == null ? 0 : claudeView.NeedsYou;
            state["claudePage"] = page;
            // Package 3: what newly happened in the last ten minutes, numbered, so the phone raises a notification for
            // each one it hasn't seen (its screen still polls the claude section). Quiet mode on the PC keeps them silent.
            state["claudeAlerts"] = page < 0 || claudeSample ? new object[0] : claudeAlerts.Recent(DateTime.UtcNow).Select(a => (object)D("seq", a.Seq, "kind", a.Kind, "session", a.Session,
                "project", a.Project, "title", a.Title, "text", a.Text, "at", ClaudeBridge.Epoch(a.At) / 1000.0)).ToArray();
            state["claudeAlertSeq"] = claudeAlerts.Seq;
            state["claudeQuiet"] = settings.QuietMode;
        }

        // The phone's claude section: sessions, plan limits and usage, filtered like the card. Never a token.
        private Dictionary<string, object> RemoteClaude()
        {
            // The dashboard may be in the tray, so the phone's question reads the files itself.
            if (!claudeSample)
            {
                DateTime now = DateTime.UtcNow;
                bool changed = claude.ReadSessions(now, false); changed |= claude.ReadLimits();
                if (claude.TelemetryChanged) { claude.TelemetryChanged = false; changed = true; }
                if ((claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= ClaudeEvery - TimeSpan.FromMilliseconds(500)) ScanClaudeLogs();
                if (changed || claudeDirty || claudeView == null) RefreshClaudeView();
            }
            var view = claudeView ?? claude.View(DateTime.UtcNow, null, ClaudeFilter);
            Func<ClaudeLimitWindow, object> window = w => w == null ? null : D("pct", w.Percent, "resetsAt", w.ResetsAt.HasValue ? (object)(ClaudeBridge.Epoch(w.ResetsAt.Value) / 1000.0) : null);
            var limits = view.Limits;
            // Every thread of the week once, and the projects of each period as their folders' thread ids, so the phone
            // groups babies under their project critters the same way. Titles are the Claude app's; never prompts.
            Func<ClaudePeriod, object[]> groups = period => view.Groups(period).Select(g => (object)D("key", g.Folder, "project", g.Name, "collapsed", g.Collapsed,
                "tokens", g.Tokens, "cost", Math.Round(g.Cost, 4), "threads", g.Threads.Select(t => (object)t.Id).ToArray(),
                "mix", Shares(g.Mix), "added", g.Added, "removed", g.Removed, "replies", g.Replies, "cache", g.CacheReads, "models", Names(g.Models), "hidden", g.Hidden, "lastActive", ClaudeBridge.Epoch(g.LastActive == DateTime.MinValue ? new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) : g.LastActive) / 1000.0)).ToArray();
            // The pit wall (decision 017): each thread's car and what it does, its burn, mix and lines per period.
            Func<ClaudeSession, Func<ClaudePeriod, object>, object> per = (t, value) => D("fiveHour", value(ClaudePeriod.FiveHour), "today", value(ClaudePeriod.Today), "week", value(ClaudePeriod.Week));
            return D("connected", view.Connected, "sample", view.Sample, "logsFailed", view.LogsFailed, "logsRead", view.LogsRead, "noPlan", view.NoPlan, "plan", ClaudeText.PlanName(view.Plan),
                "every", ClaudeIntervals.Card(settings.ClaudeCardSeconds), "period", settings.ClaudePeriod, "still", ClaudeStill, "apiValue", settings.ClaudeShowApiValue, "warning", view.TelemetryWarning ?? "",
                "threads", view.Threads.Select(t => (object)D("id", t.Id, "project", t.ProjectName, "title", t.Title, "running", t.Running, "state", ClaudeSessionFile.StateName(t.Running ? t.State : ClaudeState.Ready),
                    "since", ClaudeBridge.Epoch(t.Since) / 1000.0, "lastActive", ClaudeBridge.Epoch(t.LastActive) / 1000.0, "message", t.Message, "model", t.Model, "where", t.Where, "canFocus", t.Running && t.CanFocus, "archived", t.Archived, "resolved", t.Pit == PitState.Resolved,
                    "reason", t.Running && t.State == ClaudeState.Stuck ? t.Reason : "", "reasonText", t.Running && t.State == ClaudeState.Stuck ? ClaudeText.StuckReason(t, view, DateTime.UtcNow, settings.Use24HourClock) : "",
                    "context", t.Running && !Double.IsNaN(t.Context) ? (object)Math.Round(t.Context, 1) : null, "workers", t.Workers, "tasks", t.Tasks, "failed", t.Failed,
                    "tasksDone", t.TasksDone, "tasksTotal", t.TasksTotal, "endReason", t.EndReason,
                    "tokens", D("fiveHour", t.TokensFor(ClaudePeriod.FiveHour), "today", t.TokensFor(ClaudePeriod.Today), "week", t.TokensFor(ClaudePeriod.Week)),
                    "cost", D("fiveHour", Math.Round(t.CostFor(ClaudePeriod.FiveHour), 4), "today", Math.Round(t.CostFor(ClaudePeriod.Today), 4), "week", Math.Round(t.CostFor(ClaudePeriod.Week), 4)),
                    "pit", ClaudePace.StateName(t.Pit), "car", ClaudePace.CarName(t.Car), "rate", Math.Round(t.Rate),
                    "mix", per(t, p => Shares(t.MixFor(p))), "lines", per(t, p => t.LinesFor(p).Select(n => (object)n).ToArray()), "replies", per(t, p => t.RepliesFor(p)), "models", per(t, p => Names(t.ModelsFor(p))), "cache", per(t, p => t.CacheReadsFor(p)),
                    // Package 2 (the phone reads these in a later Android pass): helpers' tokens, Claude Code's own cost,
                    // its mode and the last API error while it retries.
                    "helperTokens", per(t, p => t.HelperTokensFor(p)), "costUsd", OrNull(t.CostUsd), "mode", t.Running ? t.Mode : "", "revving", t.Revving,
                    "errorStatus", t.ErrorAt == DateTime.MinValue ? null : (object)t.ErrorStatus, "errorAttempt", t.ErrorAt == DateTime.MinValue ? null : (object)t.ErrorAttempt)).ToArray(),
                "burn", D("rate", Math.Round(view.Rate), "scale", view.Scale, "minutes", view.Burn.Select(v => (object)Math.Round(v)).ToArray()),
                "lanes", view.Lanes.Select(l => (object)D("key", l.Folder, "project", l.Name, "thread", l.Session.Length > 0, "color", ClaudeBridge.ProjectName(l.Folder), "bins", l.Bins.Select(v => (object)Math.Round(v)).ToArray())).ToArray(),
                "facts", D("tokens", view.Facts.Tokens, "cost", Math.Round(view.Facts.Cost, 4), "prompt", view.Facts.Prompt, "cacheRead", view.Facts.CacheRead, "tools", view.Facts.Tools, "asked", view.Facts.Asked,
                    "denied", view.Facts.Denied, "replies", view.Facts.Replies, "added", view.Facts.Added, "removed", view.Facts.Removed, "linesThreads", view.Facts.LinesThreads, "telemetry", view.Facts.Telemetry, "askedPartial", view.Facts.AskedPartial),
                // Package 2: every period's facts, the chosen four, where the costs came from, the unpriced models, when
                // the 5-hour limit fills at this pace, and Claude Code's own cost for the running sessions.
                "factsBy", D("fiveHour", Facts(view.FactsFor(ClaudePeriod.FiveHour)), "today", Facts(view.FactsFor(ClaudePeriod.Today)), "week", Facts(view.FactsFor(ClaudePeriod.Week))),
                "factKinds", ClaudeFactKinds.Clean(settings.ClaudeFacts).Cast<object>().ToArray(),
                "costSource", D("fiveHour", view.CostSourceFor(ClaudePeriod.FiveHour), "today", view.CostSourceFor(ClaudePeriod.Today), "week", view.CostSourceFor(ClaudePeriod.Week)),
                "unpriced", D("fiveHour", view.UnpricedFor(ClaudePeriod.FiveHour).Cast<object>().ToArray(), "today", view.UnpricedFor(ClaudePeriod.Today).Cast<object>().ToArray(), "week", view.UnpricedFor(ClaudePeriod.Week).Cast<object>().ToArray()),
                "limitEta", Double.IsNaN(view.LimitEtaMinutes) ? null : (object)Math.Round(view.LimitEtaMinutes, 1),
                "claudeCodeCost", OrNull(view.ClaudeCodeCost), "estimateCost", OrNull(view.EstimateCost),
                "groups", D("fiveHour", groups(ClaudePeriod.FiveHour), "today", groups(ClaudePeriod.Today), "week", groups(ClaudePeriod.Week)),
                "sessions", view.Sessions.Select(s => (object)D("id", s.Id, "project", s.ProjectName, "title", s.Title, "state", ClaudeSessionFile.StateName(s.State),
                    "since", ClaudeBridge.Epoch(s.Since) / 1000.0, "message", s.Message, "model", s.Model, "where", s.Where, "canFocus", s.CanFocus,
                    "tokens", D("fiveHour", s.TokensFor(ClaudePeriod.FiveHour), "today", s.TokensFor(ClaudePeriod.Today), "week", s.TokensFor(ClaudePeriod.Week)),
                    "cost", D("fiveHour", Math.Round(s.CostFor(ClaudePeriod.FiveHour), 4), "today", Math.Round(s.CostFor(ClaudePeriod.Today), 4), "week", Math.Round(s.CostFor(ClaudePeriod.Week), 4)))).ToArray(),
                "limits", limits == null ? null : D("fiveHour", window(limits.FiveHour), "week", window(limits.Week), "updatedAt", ClaudeBridge.Epoch(limits.UpdatedAt) / 1000.0, "source", limits.Source,
                    "extra", limits.Extra.Select(p => (object)D("name", p.Key, "pct", p.Value.Percent, "resetsAt", p.Value.ResetsAt.HasValue ? (object)(ClaudeBridge.Epoch(p.Value.ResetsAt.Value) / 1000.0) : null)).ToArray(),
                    "extraUsage", limits.ExtraUsage == null ? null : D("enabled", limits.ExtraUsage.Enabled, "limit", OrNull(limits.ExtraUsage.Limit), "used", OrNull(limits.ExtraUsage.Used), "pct", OrNull(limits.ExtraUsage.Percent)),
                    "spend", limits.Spend == null ? null : D("pct", Math.Round(limits.Spend.Percent, 1), "resetsAt", limits.Spend.ResetsAt.HasValue ? (object)(ClaudeBridge.Epoch(limits.Spend.ResetsAt.Value) / 1000.0) : null, "usedUsd", OrNull(limits.Spend.UsedUsd), "limitUsd", OrNull(limits.Spend.LimitUsd), "period", limits.Spend.Period)),
                "usage", D("fiveHour", Projects(view, ClaudePeriod.FiveHour), "today", Projects(view, ClaudePeriod.Today), "week", Projects(view, ClaudePeriod.Week)));
        }
        private static object OrNull(double value) { return Double.IsNaN(value) ? null : (object)Math.Round(value, 2); }
        private static object Facts(ClaudeFacts f)
        {
            return D("tokens", f.Tokens, "cost", Math.Round(f.Cost, 4), "cacheHit", OrNull(f.CacheHit), "tools", f.Tools, "slowTool", f.SlowTool, "slowMs", OrNull(f.SlowMs),
                "runs", f.Runs, "runFailures", f.RunFailures, "failuresFromHooks", f.FailuresFromHooks, "errors", f.Errors, "helperShare", OrNull(f.HelperShare), "helpers", f.Helpers,
                "editsAccepted", f.EditsAccepted, "editsRejected", f.EditsRejected, "commits", f.Commits, "pullRequests", f.PullRequests,
                "activeUser", Math.Round(f.ActiveUser), "activeCli", Math.Round(f.ActiveCli), "metrics", f.Metrics, "added", f.Added, "removed", f.Removed);
        }
        private static object[] Names(List<KeyValuePair<string, double>> models) { return models.Take(4).Select(m => (object)D("name", m.Key, "share", Math.Round(m.Value, 3))).ToArray(); }
        private static object[] Shares(double[] mix) { return mix.Select(v => (object)Math.Round(v, 3)).ToArray(); }
        private static object[] Projects(ClaudeView view, ClaudePeriod period) { return view.UsageFor(period).Select(p => (object)D("project", p.Name, "tokens", p.Tokens, "cost", Math.Round(p.Cost, 4))).ToArray(); }

        // The phone folds a project into its critter or lets it out; the card follows (with its motion).
        private RemoteResponse RemoteClaudeCollapse(Dictionary<string, object> body)
        {
            string folder = ClaudeUsage.Key(Str(body, "project"));
            if (folder.Length == 0) throw new InvalidOperationException("Which project?");
            object value; bool fold = body.TryGetValue("collapsed", out value) && value is bool && (bool)value;
            bool folded = (settings.ClaudeCollapsed ?? new string[0]).Any(f => String.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
            if (folded != fold)
            {
                if (claudeCard.View != null && claudeCard.View.Groups(claudeCard.Period).Any(g => String.Equals(g.Folder, folder, StringComparison.OrdinalIgnoreCase))) claudeCard.Toggle(folder);
                else SetClaudeCollapsed(folder, fold);
            }
            return Done(null);
        }

        private RemoteResponse RemoteClaudeFocus(Dictionary<string, object> body)
        {
            string id = Str(body, "id");
            if (claudeView == null || !claudeView.Sessions.Any(s => s.Id == id)) throw new InvalidOperationException("That Claude Code session isn't running anymore.");
            string problem = FocusClaudeSession(id);
            if (problem != null) throw new InvalidOperationException(problem);
            return Done(null);
        }
    }
}
