using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using WidgetScreen;

// Projects with their threads (docs/design/claude-watcher.md, decision 016): the Claude app's thread records, grouping
// under the folder a thread started in (worktrees under their parent), the period filter, folding a project into its
// critter and back, the two refresh intervals and the plan check's backoff. Everything runs on fixtures in temporary
// folders; the real Claude folders and Anthropic's servers are never touched.
internal static class ClaudeTreeTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static readonly DateTime Now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-tree", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static string Iso(DateTime time) { return time.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture); }
    private static string Json(string text) { return text.Replace("\\", "\\\\").Replace("\"", "\\\""); }
    // A Claude app thread record with only the fields the reader uses, plus one it ignores.
    private static void Record(string root, string session, string folder, string title, DateTime active, bool archived = false)
    {
        string path = Path.Combine(root, "org", "account", "local_" + session + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "{\"sessionId\":\"local_" + session + "\",\"cliSessionId\":\"" + session + "\",\"cwd\":\"" + Json(folder) + "\",\"originCwd\":\"" + Json(folder) + "\",\"createdAt\":" + ClaudeBridge.Epoch(active.AddHours(-1))
            + ",\"lastActivityAt\":" + ClaudeBridge.Epoch(active) + ",\"isArchived\":" + (archived ? "true" : "false") + ",\"title\":\"" + Json(title) + "\",\"promptAppendSnapshot\":{\"append\":\"not read\"}}");
    }
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage> Send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) { return Task.FromResult(Send(request)); }
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude reads the Claude app's thread records and the titles typed in Claude Code", () => {
            var thread = ClaudeAppThreads.Parse("{\"cliSessionId\":\"abc\",\"cwd\":\"C:\\\\Code\\\\moved\",\"originCwd\":\"C:\\\\Code\\\\start\",\"title\":\" Fix the build \",\"isArchived\":false,\"createdAt\":1793537932958,\"lastActivityAt\":1793537990000}");
            Assert(thread != null && thread.Session == "abc" && thread.Folder == "C:\\Code\\start" && thread.Title == "Fix the build" && !thread.Archived && thread.LastActive > thread.Created, "A thread record was read wrongly.");
            Assert(ClaudeAppThreads.Parse("{\"sessionId\":\"local_x\"}") == null && ClaudeAppThreads.Parse("not json") == null, "A record without a Claude Code session was kept.");
            Assert(ClaudeAppThreads.Parse("{\"cliSessionId\":\"a\",\"cwd\":\"C:\\\\Only\"}").Folder == "C:\\Only", "A record without originCwd lost its folder.");
            var titles = new Dictionary<string, string>();
            ClaudeUsage.Parse("{\"type\":\"custom-title\",\"customTitle\":\"First\",\"sessionId\":\"s1\"}\n{\"type\":\"custom-title\",\"customTitle\":\"Renamed\",\"sessionId\":\"s1\"}\n", new Dictionary<string, ClaudeUsageEntry>(), "s1", null, titles);
            Assert(titles["s1"] == "Renamed", "The latest typed title doesn't win.");
            Assert(ClaudeService.ProjectOf("C:\\Code\\alpha\\.claude\\worktrees\\zen") == "C:\\Code\\alpha" && ClaudeService.ProjectOf("C:\\Code\\alpha\\.worktrees\\feat\\") == "C:\\Code\\alpha" && ClaudeService.ProjectOf("C:\\Code\\alpha\\") == "C:\\Code\\alpha", "Worktrees aren't counted under their project.");
        });

        check("Claude groups threads under the folder they started in, by period", () => {
            string config = Fresh(directory, "config-group"), app = Fresh(directory, "app-group");
            // A running Code-tab thread whose shell moved into a worktree; the app knows where it started.
            Directory.CreateDirectory(Path.Combine(config, "sessions"));
            File.WriteAllText(Path.Combine(config, "sessions", "4242.json"), "{\"pid\":4242,\"sessionId\":\"s-run\",\"cwd\":\"C:\\\\Code\\\\alpha\\\\.worktrees\\\\feat\",\"status\":\"busy\",\"startedAt\":" + ClaudeBridge.Epoch(Now.AddMinutes(-30)) + ",\"statusUpdatedAt\":" + ClaudeBridge.Epoch(Now.AddMinutes(-1)) + ",\"entrypoint\":\"claude-desktop\"}");
            Record(app, "s-run", "C:\\Code\\alpha", "Running thread", Now.AddMinutes(-1));
            Record(app, "s-wt", "C:\\Code\\alpha\\.claude\\worktrees\\zen", "Worktree thread", Now.AddHours(-6));
            Record(app, "s-arch", "C:\\Code\\alpha", "Archived thread", Now.AddHours(-2), archived: true);
            Record(app, "s-beta", "C:\\Code\\beta", "Beta work", Now.AddDays(-2));
            Record(app, "s-old", "C:\\Code\\beta", "Too old", Now.AddDays(-10));
            // A terminal thread only the logs know, with a typed title and some usage an hour ago.
            string log = Path.Combine(config, "projects", "C--Code-alpha", "s-log.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            File.WriteAllText(log, "{\"type\":\"user\",\"cwd\":\"C:\\\\Code\\\\alpha\",\"sessionId\":\"s-log\",\"timestamp\":\"" + Iso(Now.AddHours(-1)) + "\"}\n"
                + "{\"type\":\"custom-title\",\"customTitle\":\"Typed title\",\"sessionId\":\"s-log\"}\n"
                + "{\"type\":\"assistant\",\"cwd\":\"C:\\\\Code\\\\alpha\",\"sessionId\":\"s-log\",\"requestId\":\"req-1\",\"timestamp\":\"" + Iso(Now.AddHours(-1)) + "\",\"message\":{\"id\":\"m1\",\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":100,\"output_tokens\":50}}}\n");
            File.SetLastWriteTimeUtc(log, Now.AddHours(-1));
            var service = new ClaudeService(Fresh(directory, "bridge-group"), config, appThreads: new ClaudeAppThreads(() => new[] { app }));
            service.RegistryAlive = pid => pid == 4242;
            service.Usage.Scan(Now);
            service.ReadSessions(Now, true);
            var view = service.View(Now, service.Usage.Entries);
            Assert(view.Sessions.Count == 1 && view.Sessions[0].Id == "s-run" && view.Sessions[0].State == ClaudeState.Working, "The running thread isn't the one session.");
            Assert(!view.Threads.Any(t => t.Id == "s-arch" || t.Id == "s-old"), "Archived or old threads show.");
            var five = view.Groups(ClaudePeriod.FiveHour);
            Assert(five.Count == 1 && five[0].Folder == "C:\\Code\\alpha" && five[0].Threads.Select(t => t.Id).SequenceEqual(new[] { "s-run", "s-log" }), "5 h groups wrong: " + String.Join(";", five.Select(g => g.Folder + ":" + String.Join(",", g.Threads.Select(t => t.Id)))));
            Assert(five[0].Threads[0].Title == "Running thread" && five[0].Threads[1].Title == "Typed title" && five[0].Tokens == 150 && five[0].Running == 1 && five[0].State == ClaudeState.Working, "Titles, tokens or the project's face are wrong.");
            var week = view.Groups(ClaudePeriod.Week);
            Assert(week.Count == 2 && week[0].Folder == "C:\\Code\\alpha" && week[1].Folder == "C:\\Code\\beta" && week[1].Asleep && week[0].Threads.Select(t => t.Id).SequenceEqual(new[] { "s-run", "s-log", "s-wt" }), "Week groups wrong: " + String.Join(";", week.Select(g => g.Folder + ":" + String.Join(",", g.Threads.Select(t => t.Id)))));
            DateTime today = view.Starts[ClaudePeriod.Today];
            var expected = new[] { "s-run", "s-log", "s-wt" }.Where(id => id == "s-run" || view.Threads.First(t => t.Id == id).LastActive >= today).ToList();
            Assert(view.Groups(ClaudePeriod.Today)[0].Threads.Select(t => t.Id).SequenceEqual(expected), "Today's threads are filtered wrongly.");
            var projects = service.Projects(Now, null);
            Assert(projects.Any(p => p.Folder == "C:\\Code\\alpha" && p.Threads == 3) && projects.Any(p => p.Folder == "C:\\Code\\beta" && p.Threads == 1) && !projects.Any(p => p.Folder.Contains("worktrees")), "Settings → Claude counts threads wrongly: " + String.Join(";", projects.Select(p => p.Folder + "=" + p.Threads)));
            // Unticked projects leave the card's threads too.
            var filtered = service.View(Now, service.Usage.Entries, folder => folder != "C:\\Code\\beta");
            Assert(filtered.Groups(ClaudePeriod.Week).Count == 1, "An unticked project still shows its threads.");
            Assert(ClaudeText.ThreadTitle(new ClaudeSession { LastActive = Now }, Now, true).StartsWith("Untitled · ") && ClaudeText.GroupDetail(five[0]).StartsWith("2 threads") && ClaudeText.Threads(1) == "1 thread", "Thread words wrong: " + ClaudeText.GroupDetail(five[0]));
            service.Dispose();
        });

        check("Claude refresh intervals keep their minimums and round-trip", () => {
            var defaults = new AppSettings();
            Assert(defaults.ClaudeCardSeconds == 2 && defaults.ClaudePlanSeconds == 300 && defaults.ClaudePeriod == "Today" && defaults.ClaudeCollapsed == null, "Claude refresh defaults wrong.");
            var odd = new AppSettings { ClaudeCardSeconds = -4, ClaudePlanSeconds = 3, ClaudePeriod = "Month", ClaudeCollapsed = new[] { "C:\\Code\\alpha\\", "c:\\code\\ALPHA", "" } };
            odd.Normalize();
            Assert(odd.ClaudeCardSeconds == 1 && odd.ClaudePlanSeconds == 10 && odd.ClaudePeriod == "Today" && odd.ClaudeCollapsed.Length == 1 && odd.ClaudeCollapsed[0] == "C:\\Code\\alpha", "Claude settings aren't clamped: " + odd.ClaudeCardSeconds + "/" + odd.ClaudePlanSeconds + "/" + String.Join("|", odd.ClaudeCollapsed));
            var zero = new AppSettings { ClaudeCardSeconds = 0, ClaudePlanSeconds = 0 }; zero.Normalize();
            Assert(zero.ClaudeCardSeconds == 2 && zero.ClaudePlanSeconds == 300, "An older settings.xml doesn't get the default intervals.");
            string path = Path.Combine(Fresh(directory, "intervals"), "settings.xml"), warning;
            SettingsStore.Save(path, new AppSettings { ClaudeCardSeconds = 7, ClaudePlanSeconds = 45, ClaudePeriod = "Week", ClaudeCollapsed = new[] { "C:\\Code\\alpha" } });
            var back = SettingsStore.Load(path, out warning);
            Assert(back.ClaudeCardSeconds == 7 && back.ClaudePlanSeconds == 45 && back.ClaudePeriod == "Week" && back.ClaudeCollapsed.SequenceEqual(new[] { "C:\\Code\\alpha" }), "Claude settings didn't round-trip.");
            Assert(ClaudeIntervals.Parse(" 12 ", 2, ClaudeIntervals.Card) == 12 && ClaudeIntervals.Parse("0", 2, ClaudeIntervals.Card) == 1 && ClaudeIntervals.Parse("abc", 5, ClaudeIntervals.Card) == 5 && ClaudeIntervals.Parse("-3", 300, ClaudeIntervals.Plan) == 300
                && ClaudeIntervals.Parse("4", 300, ClaudeIntervals.Plan) == 10 && ClaudeIntervals.Parse("99999999999", 2, ClaudeIntervals.Card) == ClaudeIntervals.Maximum, "Typed intervals are read wrongly.");
        });

        check("Claude plan check backs off when Anthropic turns it away and recovers", () => {
            Assert(ClaudeIntervals.Backoff(TimeSpan.FromSeconds(300), TimeSpan.Zero) == TimeSpan.FromSeconds(600) && ClaudeIntervals.Backoff(TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(600)) == TimeSpan.FromMinutes(15)
                && ClaudeIntervals.Backoff(TimeSpan.FromSeconds(10), TimeSpan.Zero) == TimeSpan.FromSeconds(20) && ClaudeIntervals.Backoff(TimeSpan.FromMinutes(20), TimeSpan.Zero) == TimeSpan.FromMinutes(20), "Backoff steps wrong.");
            string folder = Fresh(directory, "backoff"), path = Path.Combine(folder, "claude-session.dat");
            new ClaudeSignIn { AccessToken = "ok", RefreshToken = "r", ExpiresUtc = DateTime.UtcNow.AddHours(1) }.Save(path);
            var status = HttpStatusCode.OK;
            var handler = new Handler { Send = request => status == HttpStatusCode.OK
                ? new HttpResponseMessage(status) { Content = new StringContent("{\"five_hour\":{\"utilization\":10,\"resets_at\":\"2026-11-01T15:00:00Z\"},\"seven_day\":{\"utilization\":20,\"resets_at\":\"2026-11-06T04:00:00Z\"}}") }
                : new HttpResponseMessage(status) { Content = new StringContent("{}") } };
            using (var service = new ClaudeService(Fresh(directory, "bridge-backoff"), Fresh(directory, "config-backoff"), account: new ClaudeAccount(path, handler, url => { })))
            {
                service.PlanEvery = TimeSpan.FromSeconds(5);
                Assert(service.PlanEvery == TimeSpan.FromSeconds(10), "The plan interval goes under 10 seconds.");
                service.PlanEvery = TimeSpan.FromSeconds(300);
                status = (HttpStatusCode)429; service.CheckOnlineAsync(Now).GetAwaiter().GetResult();
                Assert(service.Plan == ClaudePlanState.Failed && service.BackingOff && service.NextWait == TimeSpan.FromSeconds(600) && !service.OnlineDue(Now.AddSeconds(599)) && service.OnlineDue(Now.AddSeconds(600)), "A 429 didn't double the wait.");
                status = HttpStatusCode.ServiceUnavailable; service.CheckOnlineAsync(Now).GetAwaiter().GetResult();
                Assert(service.NextWait == TimeSpan.FromMinutes(15), "A 5xx didn't back off further (to 15 minutes).");
                status = HttpStatusCode.ServiceUnavailable; service.CheckOnlineAsync(Now).GetAwaiter().GetResult();
                Assert(service.NextWait == TimeSpan.FromMinutes(15), "The backoff went past 15 minutes.");
                status = HttpStatusCode.OK; service.CheckOnlineAsync(Now).GetAwaiter().GetResult();
                Assert(service.Plan == ClaudePlanState.Current && !service.BackingOff && service.NextWait == TimeSpan.FromSeconds(300) && service.Limits.FiveHour.Percent == 10, "A success didn't go back to the chosen interval.");
                status = HttpStatusCode.NotFound; service.CheckOnlineAsync(Now).GetAwaiter().GetResult();
                Assert(service.Plan == ClaudePlanState.Failed && !service.BackingOff && service.NextWait == TimeSpan.FromSeconds(300), "A failure that isn't throttling backed off.");
                var view = new ClaudeView { Limits = service.Limits, Plan = service.Plan };
                Assert(ClaudeText.Source(view, true).StartsWith("Plan limits as of "), "A failed check doesn't say when the limits are from.");
            }
        });

        check("Claude garages roll their cars in and out, and cars stay in the card", () => {
            string folder = Fresh(directory, "fold");
            foreach (var size in new[] { new Size(1856, 576), new Size(1216, 576), new Size(900, 576), new Size(640, 420), new Size(472, 310), new Size(639, 576), new Size(360, 280) })
            {
                string path = Path.Combine(folder, "claude-" + size.Width + "x" + size.Height + ".xml");
                var settings = new AppSettings { TextScale = size.Width == 640 ? 1.4 : 1, ClaudePeriod = "Week" };
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Claude"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                SettingsStore.Save(path, settings);
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            controller.UseClaudeSample(false);
                            var card = (FrameworkElement)window.FindName("ClaudeCard"); var dashboard = (FrameworkElement)window.FindName("Dashboard");
                            Action layout = () => { dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout(); };
                            layout();
                            var cardView = controller.ClaudeCardView;
                            var garages = cardView.Garages();
                            Assert(garages.Count > 0, "No garages at " + size + ".");
                            Assert(cardView.Sprites.All(s => s.FrameCount > 0), "A car or garage has no frames at " + size + ".");
                            foreach (var pair in garages)
                            {
                                var garage = pair.Key.TransformToAncestor(card).TransformBounds(new Rect(0, 0, pair.Key.ActualWidth, pair.Key.ActualHeight));
                                Assert(pair.Key.ActualWidth >= 48 && pair.Key.ActualHeight >= 48, "A garage is smaller than a finger at " + size + ".");
                                Assert(garage.Left >= 22.5 && garage.Top >= 22.5, "A garage leaves its " + size + " card: " + garage);
                            }
                            if (CardSizing.Claude(size.Width, size.Height) == CardSizing.Design.Small) continue;
                            // Folding widget-screen and letting its cars out again; the choice is kept in settings.xml.
                            const string project = "C:\\Code\\widget-screen";
                            int index = cardView.View.Groups(cardView.Period).FindIndex(g => g.Folder == project);
                            if (index < 0 || index >= garages.Count) continue;
                            int before = garages[index].Value.Count;
                            Assert(before > 0, "widget-screen shows no cars at " + size + ".");
                            Assert(cardView.Toggle(project) && !cardView.Animating, "Folding didn't happen at once without the motion (not on screen).");
                            layout();
                            string warning;
                            Assert(cardView.Garages()[index].Value.Count == 0, "The folded garage still shows cars at " + size + ".");
                            Assert(SettingsStore.Load(path, out warning).ClaudeCollapsed.Contains(project), "The fold wasn't kept in settings.xml.");
                            if (CardSizing.Claude(size.Width, size.Height) == CardSizing.Design.Large)
                                Assert(Descendants(card).OfType<TextBlock>().Any(t => t.Inlines.OfType<Run>().Any(r => r.Text.Contains("in the garage · tap to roll them out"))), "A folded garage doesn't say its cars are inside.");
                            Assert(!cardView.Toggle(project), "Rolling the cars out didn't happen.");
                            layout();
                            Assert(cardView.Garages()[index].Value.Count == before && !SettingsStore.Load(path, out warning).ClaudeCollapsed.Contains(project), "The cars didn't come back, or the fold stayed in settings.xml.");
                            // A folded garage with a thread that needs you still lights its beacon.
                            var needs = ClaudeView.Fixture(DateTime.UtcNow, true); needs.Collapsed.Add("C:\\Code\\api-server");
                            cardView.Update(needs); layout();
                            int api = cardView.View.Groups(cardView.Period).FindIndex(g => g.Folder == "C:\\Code\\api-server");
                            var apiGarage = (PitSprite)window.FindName("ClaudeGarage" + api);
                            Assert(apiGarage != null && apiGarage.Key.Contains("|Need|True|") && cardView.Garages()[api].Value.Count == 0, "A folded garage hides that a thread needs you: " + (apiGarage == null ? "none" : apiGarage.Key));
                        }
                    }
                    finally { window.Close(); }
                }
            }
        });

        check("Claude cars follow their threads: class by model, behaviour by state and telemetry", () => {
            var now = Now;
            Func<ClaudeState, bool, ClaudeSession> thread = (state, running) => new ClaudeSession { State = state, Running = running };
            Assert(ClaudePace.State(thread(ClaudeState.Working, false), now, now, now, now, now) == PitState.End
                && ClaudePace.State(thread(ClaudeState.NeedsYou, true), now, now, now, now, now) == PitState.Need
                && ClaudePace.State(thread(ClaudeState.Done, true), now, now, now, now, now) == PitState.Done
                && ClaudePace.State(thread(ClaudeState.Ready, true), now, now, now, now, now) == PitState.Idle, "Car states from thread states wrong.");
            var compacting = thread(ClaudeState.Working, true); compacting.Compacting = true;
            Assert(ClaudePace.State(compacting, now, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue, DateTime.MinValue) == PitState.Pit, "Compacting isn't on the jack.");
            var working = thread(ClaudeState.Working, true);
            Assert(ClaudePace.State(working, now, now.AddSeconds(-20), now.AddSeconds(-30), DateTime.MinValue, DateTime.MinValue) == PitState.Rev, "A long tool run doesn't rev.");
            Assert(ClaudePace.State(working, now, now.AddSeconds(-5), now.AddSeconds(-30), DateTime.MinValue, DateTime.MinValue) == PitState.Work, "A short tool run revs.");
            Assert(ClaudePace.State(working, now, DateTime.MinValue, DateTime.MinValue, now.AddSeconds(-10), now.AddSeconds(-20)) == PitState.Stuck, "An API error doesn't spin the wheels.");
            Assert(ClaudePace.State(working, now, DateTime.MinValue, DateTime.MinValue, now.AddSeconds(-10), now.AddSeconds(-2)) == PitState.Work, "A request after the error keeps spinning.");
            Assert(ClaudePace.State(working, now, DateTime.MinValue, DateTime.MinValue, now.AddSeconds(-90), now.AddSeconds(-120)) == PitState.Work, "An old error keeps spinning.");
            Func<string, long, ClaudeUsageEntry> entry = (model, tokens) => new ClaudeUsageEntry { Model = model, Tokens = tokens, At = now };
            var mix = ClaudePace.Mix(new[] { entry("claude-opus-5-5", 300), entry("claude-sonnet-5-5", 100), entry("claude-haiku-5-5", 100), entry("gpt-x", 900) });
            Assert(Math.Abs(mix[0] - 0.6) < 1e-9 && Math.Abs(mix[1] - 0.2) < 1e-9 && ClaudePace.Car(mix, "") == PitCar.GrandTourer && ClaudePace.Car(new double[3], "Haiku 5.5") == PitCar.Hatch
                && ClaudePace.Car(new double[3], "") == PitCar.Sports && ClaudePace.Family("claude-fable-5-1") == 0 && ClaudePace.Family("x") == -1, "Model mix or car class wrong.");
            // Pace: 62% with two thirds of the window gone is on pace for 93%; the first twentieth says nothing.
            var five = new ClaudeLimitWindow { Percent = 62, ResetsAt = now.AddMinutes(100) };
            double elapsed = ClaudePace.Elapsed(five, ClaudePace.FiveHours, now);
            Assert(Math.Abs(elapsed - 2.0 / 3) < 1e-9 && Math.Round(ClaudePace.Projection(62, elapsed)) == 93 && ClaudePace.Warns(93) && !ClaudePace.Warns(89) && Double.IsNaN(ClaudePace.Projection(10, 0.04))
                && Double.IsNaN(ClaudePace.Elapsed(new ClaudeLimitWindow { Percent = 5 }, ClaudePace.FiveHours, now)) && ClaudeText.Pace(92.4) == "on pace for 92%", "Pace wrong.");
            Assert(ClaudePace.Lap(41, 1) == 1 && Math.Abs(ClaudePace.Lap(41, 2) - (0.41 - 2 / 7.0) * 7) < 1e-9 && ClaudePace.Lap(41, 3) == 0 && ClaudePace.LapNow(0.62) == 4 && ClaudePace.LapNow(1) == 6, "Laps wrong.");
            Assert(ClaudePace.Scale(10000) == 60000 && ClaudePace.Scale(70000) == 80000 && ClaudePace.Scale(250000) == 300000 && ClaudePace.Scale(1000000) == 1000000 && ClaudePace.WheelFps(0.5) == 14 && ClaudePace.Hot(0.75) && !ClaudePace.Heavy(0.4), "Rev counter wrong.");
            var bins = ClaudePace.Bins(new[] { new ClaudeUsageEntry { At = now.AddSeconds(-30), Tokens = 5 }, new ClaudeUsageEntry { At = now.AddMinutes(-59.5), Tokens = 7 }, new ClaudeUsageEntry { At = now.AddMinutes(-61), Tokens = 9 } }, now, TimeSpan.FromMinutes(1), 60);
            Assert(bins[59] == 5 && bins[0] == 7 && bins.Sum() == 12 && ClaudePace.Rate(new[] { new ClaudeUsageEntry { At = now.AddSeconds(-30), Tokens = 600 }, new ClaudeUsageEntry { At = now.AddMinutes(-3), Tokens = 999 } }, now) == 300, "Burn bins wrong.");
            Assert(ClaudeText.Added(1300) == "+1.3k" && ClaudeText.Removed(216) == "−216" && ClaudeText.Dollars(7.1) == "7.10" && ClaudeText.Mix(new[] { 0.8, 0.2, 0 }) == "Opus 80% · Sonnet 20%" && ClaudeText.Chip(PitState.Pit) == "Compacting"
                && ClaudeText.Where(new ClaudeSession { Running = false, LastActive = now.AddHours(-1) }, now) == "ended 1 h ago" && ClaudeText.Where(new ClaudeSession { Running = true, Where = "Desktop", Since = now.AddMinutes(-6) }, now) == "Desktop · 6 min", "Pit words wrong.");
        });
    }
}
