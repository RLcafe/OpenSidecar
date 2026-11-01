using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Claude Code watcher (docs/design/claude-watcher.md, decision 014): Settings → Claude, the card, the header
    // pill and the phone's claude section. While the card is on any page the heartbeat looks at the bridge's session
    // files (a directory listing; only changed files are read) so the pill can light up from another page; the logs
    // are read every 10 s (30 in Quiet mode) only while the card is on the page shown, and the plan check runs every
    // 5 minutes while the card is on a page and Settings → Claude allows it.
    public sealed partial class DashboardController
    {
        private ClaudeService claude;
        private ClaudeCard claudeCard;
        private ClaudeView claudeView;
        private ClaudeCritter pillCritter;
        private Task claudeScan;
        private DateTime lastClaudeScan = DateTime.MinValue;
        private bool claudeSample, claudeOnlineBusy, claudeDirty = true;
        private readonly HashSet<string> claudeNeeding = new HashSet<string>();
        private string claudePagesKey; private bool claudeOnPages;
        // The card and service, for the checks (tests/Claude/ClaudeTests.cs).
        public ClaudeCard ClaudeCardView { get { return claudeCard; } }
        public ClaudeService Claude { get { return claude; } }

        // The ClaudeHook.exe that came with this copy of Widget Screen; Connect copies it beside settings.xml.
        private static string BundledHook { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ClaudeBridge.HookExe); } }

        private void InitializeClaude()
        {
            claude = new ClaudeService(SettingsStore.ClaudePath(settingsPath), ClaudeConnector.DefaultConfigFolder);
            claudeCard = new ClaudeCard(window, Control<Grid>("ClaudeBody"), Control<Border>("ClaudeCard"), id => FocusClaudeSession(id));
            pillCritter = new ClaudeCritter { State = ClaudeState.NeedsYou, Project = "pill" };
            Control<Grid>("ClaudePillCritter").Children.Add(pillCritter);
            Button("ClaudePill", ShowClaudePage);
            Button("ClaudeConnectButton", ConnectClaude);
            Button("ClaudeDisconnectButton", DisconnectClaude);
            if (Program.Rendering) return;
            claude.CheckConnected();
            // After an update, Claude Code runs the bridge that came with it.
            if (claude.Connected) claude.Connector.RefreshHook(BundledHook);
        }

        // A labelled fixture instead of this PC's sessions, for renders (--render-claude-sample) and checks.
        public void UseClaudeSample(bool needsYou)
        {
            claudeSample = true;
            claudeView = ClaudeView.Fixture(DateTime.UtcNow, needsYou);
            claudeCard.Update(claudeView);
            UpdateClaudePill();
        }

        // Whether the Claude card is shown on any page, for the pill and the phone. Worked out again only after a save
        // or when the screen changes.
        private bool ClaudeOnAnyPage
        {
            get
            {
                string key = settingsVersion + "|" + Editor.CanvasSize + "|" + WidgetLayout.PageCount(settings);
                if (key == claudePagesKey) return claudeOnPages;
                claudePagesKey = key;
                claudeOnPages = Enumerable.Range(0, WidgetLayout.PageCount(settings)).Any(i => WidgetLayout.ForCanvas(settings, i, Editor.CanvasSize).Any(p => p.Id == "Claude" && p.Visible));
                return claudeOnPages;
            }
        }

        private void TickClaude()
        {
            bool shown = Editor.IsVisible("Claude") && !Editor.Editing;
            if (claudeSample) { if (shown) claudeCard.Tick(ticks % 2 == 0, settings.QuietMode); pillCritter.Phase = ticks % 2 == 0 && !settings.QuietMode; return; }
            if (!Editor.IsVisible("Claude") && !ClaudeOnAnyPage) { if (claudeView != null && claudeView.NeedsYou > 0) { claudeView = null; UpdateClaudePill(); } return; }
            DateTime now = DateTime.UtcNow;
            bool changed = claude.ReadSessions(now, ticks % 5 == 0);
            changed |= claude.ReadLimits();
            if (ticks % 10 == 0) { bool was = claude.Connected; claude.CheckConnected(); changed |= was != claude.Connected; }
            if (Editor.IsVisible("Claude") && (claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= TimeSpan.FromSeconds(settings.QuietMode ? 30 : 10)) ScanClaudeLogs();
            if (changed || claudeDirty) RefreshClaudeView();
            if (shown) claudeCard.Tick(ticks % 2 == 0, settings.QuietMode);
            pillCritter.Phase = ticks % 2 == 0 && !settings.QuietMode;
        }

        // The plan check runs while the app runs, in the tray too, as long as the card is on a page.
        private void TickClaudeOnline()
        {
            if (Program.Rendering || claudeSample || !settings.ClaudeOnlineLimits || claudeOnlineBusy || !claude.OnlineDue(DateTime.UtcNow)) return;
            if (Editor.IsVisible("Claude") || ClaudeOnAnyPage) CheckClaudeOnline();
        }

        private async void ScanClaudeLogs()
        {
            lastClaudeScan = DateTime.UtcNow;
            var scan = Task.Run(() => claude.Usage.Scan(DateTime.UtcNow));
            claudeScan = scan;
            try { await scan; } catch (Exception) { }
            if (!disposed) claudeDirty = true;
        }

        private async void CheckClaudeOnline()
        {
            claudeOnlineBusy = true;
            try { if (await claude.CheckOnlineAsync(DateTime.UtcNow) && !disposed) claudeDirty = true; }
            catch (Exception) { }
            finally { claudeOnlineBusy = false; }
        }

        private void RefreshClaudeView()
        {
            claudeDirty = false;
            if (claudeSample) return;
            claudeView = claude.View(DateTime.UtcNow, claude.Usage.Scanned ? claude.Usage.Entries : null);
            claudeCard.Update(claudeView);
            // A chime for each session that newly needs you, if Settings → Claude asks for one.
            var needing = claudeView.Sessions.Where(s => s.State == ClaudeState.NeedsYou).Select(s => s.Id).ToList();
            bool fresh = needing.Any(id => !claudeNeeding.Contains(id));
            claudeNeeding.Clear(); foreach (var id in needing) claudeNeeding.Add(id);
            if (fresh && settings.ClaudeChime && !settings.QuietMode) Chime();
            UpdateClaudePill();
        }

        // The header pill: a session needs you while the Claude card is on another page.
        private void UpdateClaudePill()
        {
            bool show = claudeView != null && claudeView.NeedsYou > 0 && !Editor.IsVisible("Claude") && !Editor.Editing && (claudeSample || ClaudeOnAnyPage);
            Control<Button>("ClaudePill").Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowClaudePage()
        {
            for (int i = 0; i < WidgetLayout.PageCount(settings); i++)
                if (WidgetLayout.ForCanvas(settings, i, Editor.CanvasSize).Any(p => p.Id == "Claude" && p.Visible)) { ShowPage(i); UpdateClaudePill(); return; }
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

        private void UpdateClaudeSettings()
        {
            claude.CheckConnected();
            Text("ClaudeConnectionStatus", claude.Connected ? "Connected. Sessions you start from now on show on the card." : "Not connected");
            Text("ClaudeFolderText", "Claude Code folder: " + claude.ConfigFolder);
            Control<Button>("ClaudeConnectButton").Visibility = claude.Connected ? Visibility.Collapsed : Visibility.Visible;
            Control<Button>("ClaudeDisconnectButton").Visibility = claude.Connected ? Visibility.Visible : Visibility.Collapsed;
            Control<CheckBox>("ClaudeChimeChoice").IsChecked = settings.ClaudeChime;
            Control<CheckBox>("ClaudeOnlineChoice").IsChecked = settings.ClaudeOnlineLimits;
        }

        private async void ConnectClaude()
        {
            bool yes = await confirm.AskAsync("CLAUDE CODE", "Connect Claude Code?", "This adds Widget Screen's hooks and status line to your Claude Code settings (settings.json). A backup is saved next to it. Your current status line keeps showing.", "Connect");
            if (!yes || disposed) return;
            try { claude.Connector.Connect(BundledHook); Text("SettingsStatus", "Claude Code is connected. New sessions show on the Claude Code widget; turn it on in Edit layout."); }
            catch (Exception e) { Text("SettingsStatus", e is InvalidOperationException ? e.Message : "Couldn't connect Claude Code: " + e.Message); }
            UpdateClaudeSettings(); claudeDirty = true;
        }

        private async void DisconnectClaude()
        {
            bool yes = await confirm.AskAsync("CLAUDE CODE", "Disconnect Claude Code?", "This takes Widget Screen's hooks and status line out of your Claude Code settings (settings.json) and puts your earlier status line back. A backup is saved next to it.", "Disconnect");
            if (!yes || disposed) return;
            try { claude.Connector.Disconnect(); Text("SettingsStatus", "Claude Code is disconnected."); }
            catch (Exception e) { Text("SettingsStatus", e is InvalidOperationException ? e.Message : "Couldn't disconnect Claude Code: " + e.Message); }
            UpdateClaudeSettings(); claudeDirty = true;
        }

        // ----- The phone link -----

        // The phone's claude section: sessions, plan limits and usage. Never the sign-in token.
        private Dictionary<string, object> RemoteClaude()
        {
            // The dashboard may be in the tray, so the phone's question reads the bridge's files itself.
            if (!claudeSample)
            {
                DateTime now = DateTime.UtcNow;
                bool changed = claude.ReadSessions(now, false); changed |= claude.ReadLimits();
                if ((claudeScan == null || claudeScan.IsCompleted) && now - lastClaudeScan >= TimeSpan.FromSeconds(settings.QuietMode ? 30 : 10)) ScanClaudeLogs();
                if (changed || claudeDirty || claudeView == null) RefreshClaudeView();
            }
            var view = claudeView ?? claude.View(DateTime.UtcNow, null);
            Func<ClaudeLimitWindow, object> window = w => w == null ? null : D("pct", w.Percent, "resetsAt", w.ResetsAt.HasValue ? (object)(ClaudeBridge.Epoch(w.ResetsAt.Value) / 1000.0) : null);
            var limits = view.Limits;
            return D("connected", view.Connected, "sample", view.Sample, "logsFailed", view.LogsFailed, "logsRead", view.LogsRead, "noPlan", view.NoPlan,
                "sessions", view.Sessions.Select(s => (object)D("id", s.Id, "project", s.ProjectName, "title", s.Title, "state", ClaudeSessionFile.StateName(s.State),
                    "since", ClaudeBridge.Epoch(s.Since) / 1000.0, "message", s.Message, "model", s.Model, "canFocus", s.CanFocus,
                    "tokens", D("fiveHour", s.TokensFor(ClaudePeriod.FiveHour), "today", s.TokensFor(ClaudePeriod.Today), "week", s.TokensFor(ClaudePeriod.Week)))).ToArray(),
                "limits", limits == null ? null : D("fiveHour", window(limits.FiveHour), "week", window(limits.Week), "updatedAt", ClaudeBridge.Epoch(limits.UpdatedAt) / 1000.0, "source", limits.Source),
                "usage", D("fiveHour", Projects(view, ClaudePeriod.FiveHour), "today", Projects(view, ClaudePeriod.Today), "week", Projects(view, ClaudePeriod.Week)));
        }
        private static object[] Projects(ClaudeView view, ClaudePeriod period) { return view.UsageFor(period).Select(p => (object)D("project", p.Key, "tokens", p.Value)).ToArray(); }

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
