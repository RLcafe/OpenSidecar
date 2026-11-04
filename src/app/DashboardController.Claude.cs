using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Claude Code card (decision 027, docs/design/claude-card.md): four tiles, the plan's 5-hour and weekly use,
    // tokens per minute and the mascot with the session counts. While the card is on the page shown, the heartbeat
    // reads Claude Code's session list and the Claude app's thread records every 2 s (only changed files are parsed) and
    // Claude Code's logs every 10 s off the UI thread; the plan check runs every 5 minutes while signed in and the card
    // is on any page, so the phone gets it too. Nothing else runs for Claude.
    public sealed partial class DashboardController
    {
        private const int ClaudeSessionsEvery = 2, ClaudeLogsEvery = 10;
        private ClaudeService claude;
        private ClaudeCard claudeCard;
        private Task claudeScan;
        private DateTime lastClaudeScan = DateTime.MinValue;
        private bool claudeSample, claudePlanBusy;
        private ClaudeReading claudeSampleReading;
        private volatile string claudeCleanupProblem;
        private string claudePagesKey; private bool claudeOnAnyPage;
        // The card and service, for the checks (tests/Claude/ClaudeTests.cs).
        public ClaudeCard ClaudeCardView { get { return claudeCard; } }
        public ClaudeService Claude { get { return claude; } }

        private void InitializeClaude()
        {
            claude = new ClaudeService(ClaudeCleanup.DefaultConfigFolder, SettingsStore.ClaudeSignInPath(settingsPath), SettingsStore.ClaudePath(settingsPath));
            claudeCard = new ClaudeCard(window, Control<Grid>("ClaudeBody"), Control<TextBlock>("ClaudeStatus"));
            Button("ClaudeSignInButton", SignInClaude);
            Button("ClaudeSignOutButton", SignOutClaude);
            Button("ClaudeCleanupButton", CleanUpClaude);
            // A tap on the card while it isn't signed in opens Settings → Claude.
            Action tapped = () => {
                if (Editor.Editing || claudeSample || (claude.Plan != ClaudePlanState.SignedOut && claude.Plan != ClaudePlanState.Expired)) return;
                OpenSettingsTab("Claude");
            };
            Control<Border>("ClaudeCard").PreviewMouseLeftButtonUp += (sender, e) => tapped();
            Control<Border>("ClaudeCard").PreviewTouchUp += (sender, e) => tapped();
            if (Program.Rendering) return;
            if (claude.Account.StartupWarning != null && pendingMessage == null) pendingMessage = claude.Account.StartupWarning;
            // Earlier versions hooked into Claude Code; that connection comes out once, off the UI thread.
            Task.Run(() => claude.Cleanup.Run()).ContinueWith(t => { claudeCleanupProblem = t.IsFaulted ? "Couldn't remove the old Claude Code hooks." : t.Result; });
        }

        // A labelled fixture instead of this PC's sessions, for renders (--render-claude-sample) and checks.
        public void UseClaudeSample(bool needsYou)
        {
            claudeSample = true;
            claudeSampleReading = ClaudeReading.Fixture(DateTime.UtcNow, needsYou);
            claudeCard.Update(claudeSampleReading);
        }

        // Whether the card is on any page (the plan check keeps going for the phone while another page shows).
        private bool ClaudeOnAnyPage
        {
            get
            {
                string key = settingsVersion + "|" + Editor.CanvasSize + "|" + WidgetLayout.PageCount(settings);
                if (key == claudePagesKey) return claudeOnAnyPage;
                claudePagesKey = key; claudeOnAnyPage = false;
                for (int i = 0; i < WidgetLayout.PageCount(settings) && !claudeOnAnyPage; i++)
                    if (WidgetLayout.ForCanvas(settings, i, Editor.CanvasSize).Any(p => p.Id == "Claude" && p.Visible)) claudeOnAnyPage = true;
                return claudeOnAnyPage;
            }
        }

        // The heartbeat while the dashboard shows.
        private void TickClaude()
        {
            if (claudeSample || Program.Rendering || !Editor.IsVisible("Claude") || Editor.Editing) return;
            if (ticks % ClaudeSessionsEvery == 0) ReadClaudeSessions();
            if (ticks % ClaudeLogsEvery == 0) ScanClaudeLogs();
        }

        // The plan check, in the tray too (a phone may show the card).
        private void TickClaudeOnline()
        {
            if (Program.Rendering || claudeSample || claudePlanBusy || !ClaudeOnAnyPage || !claude.PlanDue(DateTime.UtcNow)) return;
            CheckClaudePlan();
        }

        private void ReadClaudeSessions()
        {
            claude.Sessions.Read(DateTime.UtcNow, claude.Tokens.Errored);
            RefreshClaude();
        }

        private async void ScanClaudeLogs()
        {
            if (claudeScan != null && !claudeScan.IsCompleted) return;
            lastClaudeScan = DateTime.UtcNow;
            var scan = Task.Run(() => claude.Tokens.Scan(DateTime.UtcNow));
            claudeScan = scan;
            try { await scan; } catch (Exception) { }
            if (!disposed) RefreshClaude();
        }

        private async void CheckClaudePlan()
        {
            claudePlanBusy = true;
            try { if (await claude.CheckPlanAsync(DateTime.UtcNow) && !disposed) { RefreshClaude(); if (SettingsOpen) UpdateClaudeSettings(); } }
            catch (Exception) { }
            finally { claudePlanBusy = false; }
        }

        private ClaudeReading ClaudeNow()
        {
            if (claudeSample) return claudeSampleReading;
            return new ClaudeReading { Plan = claude.Plan, Limits = claude.Limits, LogsFound = claude.Tokens.Found, LogsScanned = claude.Tokens.Scanned,
                History = claude.Tokens.History, Counts = claude.Sessions.Counts ?? new ClaudeCounts(), Now = DateTime.UtcNow };
        }
        private void RefreshClaude() { claudeCard.Update(ClaudeNow()); }

        // ----- Settings → Claude -----

        private bool SettingsOpen { get { var overlay = window.FindName("SettingsOverlay") as UIElement; return overlay != null && overlay.Visibility == Visibility.Visible; } }

        private void UpdateClaudeSettings()
        {
            var plan = claude.Plan; var limits = claude.Limits;
            string asOf = limits != null ? " Checked at " + ClaudeText.Clock(limits.UpdatedAt, settings.Use24HourClock) + "." : "";
            Text("ClaudePlanStatus", claude.Account.SigningIn ? "Waiting for you to sign in in your browser…"
                : plan == ClaudePlanState.Expired ? "Your Claude sign-in has ended. Sign in again to keep the 5-hour and weekly tiles current."
                : plan == ClaudePlanState.SignedOut ? "Not signed in. The 5-hour and weekly tiles need this."
                : plan == ClaudePlanState.NoMeters ? "Signed in. Anthropic says your plan has no usage limits, so those tiles stay empty."
                : plan == ClaudePlanState.Failed ? (claude.BackingOff ? "Signed in. Anthropic asked Widget Screen to slow down; it tries again in " + Math.Round(claude.NextWait.TotalMinutes) + " minutes." : "Signed in. The last check didn't go through; it tries again in 5 minutes.") + asOf
                : plan == ClaudePlanState.Reading ? "Signed in. Reading your plan's limits…" : "Signed in." + asOf);
            var signIn = Control<Button>("ClaudeSignInButton");
            signIn.Content = claude.Account.SigningIn ? "Cancel sign-in" : plan == ClaudePlanState.Expired ? "Sign in again" : "Sign in with Claude";
            signIn.Visibility = plan == ClaudePlanState.SignedOut || plan == ClaudePlanState.Expired || claude.Account.SigningIn ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("ClaudeSignOutButton").Visibility = plan != ClaudePlanState.SignedOut && !claude.Account.SigningIn ? Visibility.Visible : Visibility.Collapsed;
            Text("ClaudeFolderText", "Claude Code folder: " + claude.ConfigFolder);
            // The old connection, only while it couldn't be taken out.
            bool left = claudeCleanupProblem != null;
            Text("ClaudeCleanupText", left ? claudeCleanupProblem : "");
            Control<StackPanel>("ClaudeCleanupRow").Visibility = left ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CleanUpClaude()
        {
            claudeCleanupProblem = claude.Cleanup.Run();
            Text("SettingsStatus", claudeCleanupProblem == null ? "Widget Screen's old Claude Code hooks are gone." : claudeCleanupProblem);
            UpdateClaudeSettings();
        }

        private async void SignInClaude()
        {
            if (claude.Account.SigningIn) { claude.Account.CancelSignIn(); return; }
            Text("SettingsStatus", "Sign in to Claude in your browser, then come back here.");
            var signing = claude.Account.SignInAsync(message => window.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) Text("SettingsStatus", message); })));
            UpdateClaudeSettings();
            try
            {
                await signing;
                if (disposed) return;
                Text("SettingsStatus", "Widget Screen is signed in to Claude. The 5-hour and weekly tiles fill in a moment.");
                claude.CheckSoon(); TickClaudeOnline();
            }
            catch (Exception e)
            {
                if (disposed) return;
                Text("SettingsStatus", e is OperationCanceledException ? "The Claude sign-in was cancelled." : e is UnauthorizedAccessException ? "Claude didn't accept the sign-in. Try again." : "Couldn't sign in to Claude: " + e.Message);
            }
            UpdateClaudeSettings(); RefreshClaude();
        }

        private async void SignOutClaude()
        {
            bool yes = await confirm.AskAsync("CLAUDE", "Sign out of Claude?", "Widget Screen forgets its Claude sign-in on this PC. The 5-hour and weekly tiles stop updating until you sign in again. Claude Code's own sign-in isn't affected.", "Sign out");
            if (!yes || disposed) return;
            claude.Account.SignOut(); claude.SignedOut();
            Text("SettingsStatus", "Signed out of Claude.");
            UpdateClaudeSettings(); RefreshClaude();
        }

        // ----- The phone link -----

        // The phone's claude section: the same four tiles' readings. Never a token, a path or a session's name. The
        // dashboard may be in the tray, so the phone's question reads the sources itself (the logs at most every 10 s).
        private Dictionary<string, object> RemoteClaude()
        {
            if (!claudeSample && !Program.Rendering)
            {
                claude.Sessions.Read(DateTime.UtcNow, claude.Tokens.Errored);
                if (DateTime.UtcNow - lastClaudeScan >= TimeSpan.FromSeconds(ClaudeLogsEvery)) ScanClaudeLogs();
            }
            var r = ClaudeNow();
            Func<ClaudeLimitWindow, object> window = w => w == null ? null : D("pct", Math.Round(ClaudePlan.Percent(w, r.Now), 1), "resetsAt", w.ResetsAt.HasValue ? (object)(ClaudeTime.Epoch(w.ResetsAt.Value) / 1000.0) : null);
            var c = r.Counts ?? new ClaudeCounts();
            return D("sample", r.Sample, "plan", PlanName(r.Plan), "updatedAt", r.Limits == null ? null : (object)(ClaudeTime.Epoch(r.Limits.UpdatedAt) / 1000.0),
                "fiveHour", r.Limits == null ? null : window(r.Limits.FiveHour), "week", r.Limits == null ? null : window(r.Limits.Week),
                "logs", r.LogsFound, "logsRead", r.LogsScanned, "minutes", r.History.Select(v => (object)Math.Round(v)).ToArray(),
                "done", c.Done, "needsYou", c.NeedsYou, "blocked", c.Blocked, "working", c.Working);
        }
        private static string PlanName(ClaudePlanState plan)
        {
            switch (plan)
            {
                case ClaudePlanState.Reading: return "reading";
                case ClaudePlanState.Current: return "current";
                case ClaudePlanState.Failed: return "failed";
                case ClaudePlanState.Expired: return "expired";
                case ClaudePlanState.NoMeters: return "noMeters";
                default: return "signedOut";
            }
        }
    }
}
