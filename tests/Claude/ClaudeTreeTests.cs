using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WidgetScreen;

// Projects with their threads (docs/design/claude-watcher.md, decision 016): the Claude app's thread records, grouping
// under the folder a thread started in (worktrees under their parent), the period filter, folding a project into its
// critter and back, the two refresh intervals and the plan check's backoff. Everything runs on fixtures in temporary
// folders; the real Claude folders and Anthropic's servers are never touched.
internal static class ClaudeTreeTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
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
            Assert(ClaudeText.ThreadTitle(new ClaudeSession { LastActive = Now }, Now, true).StartsWith("Untitled · ") && ClaudeText.GroupDetail(five[0], true) == "2 threads · 150 · ≈ $0.00" && ClaudeText.Threads(1) == "1 thread", "Thread words wrong: " + ClaudeText.GroupDetail(five[0], true));
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

        check("Claude projects fold their babies in and out, and babies stay in their column", () => {
            string folder = Fresh(directory, "fold");
            foreach (var size in new[] { new Size(1856, 576), new Size(900, 576), new Size(640, 420), new Size(472, 310), new Size(639, 576), new Size(360, 280) })
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
                            var columns = cardView.Columns();
                            Assert(columns.Count > 0, "No project columns at " + size + ".");
                            foreach (var pair in columns)
                            {
                                var column = pair.Key.TransformToAncestor(card).TransformBounds(new Rect(0, 0, pair.Key.ActualWidth, pair.Key.ActualHeight));
                                Assert(column.Left >= 22.5 && column.Right <= card.ActualWidth - 22.5 && column.Bottom <= card.ActualHeight - 22.5, "A project column leaves its " + size + " card: " + column);
                                foreach (var baby in pair.Value.Where(b => b.ActualWidth > 0))
                                {
                                    var box = baby.TransformToAncestor(card).TransformBounds(new Rect(0, 0, baby.ActualWidth, baby.ActualHeight));
                                    // Layout rounding may add a pixel to a cell.
                                    Assert(box.Left >= column.Left - 1.5 && box.Right <= column.Right + 1.5 && box.Top >= column.Top - 0.5, "A baby leaves its project's column at " + size + ": " + box + " in " + column);
                                }
                            }
                            if (CardSizing.Claude(size.Width, size.Height) != CardSizing.Design.Large || size.Width < 900) continue;
                            // Folding widget-screen (5 threads) and letting them out again; the choice is kept in settings.xml.
                            const string project = "C:\\Code\\widget-screen";
                            int index = cardView.View.Groups(cardView.Period).FindIndex(g => g.Folder == project);
                            Assert(index >= 0 && columns[index].Value.Count == 5, "widget-screen doesn't show its five babies at " + size + ".");
                            Assert(cardView.Toggle(project) && !cardView.Animating, "Folding didn't happen at once without the motion (not on screen).");
                            layout();
                            string warning;
                            var critter = (ClaudeCritter)window.FindName("ClaudeCritter" + index);
                            Assert(cardView.Columns()[index].Value.Count == 0 && critter.Collapsed && critter.Count == 5, "The folded project still shows babies, or its critter doesn't hold them.");
                            Assert(SettingsStore.Load(path, out warning).ClaudeCollapsed.Contains(project), "The fold wasn't kept in settings.xml.");
                            Assert(!cardView.Toggle(project), "Letting the babies out didn't happen.");
                            layout();
                            Assert(cardView.Columns()[index].Value.Count == 5 && !((ClaudeCritter)window.FindName("ClaudeCritter" + index)).Collapsed && !SettingsStore.Load(path, out warning).ClaudeCollapsed.Contains(project), "The babies didn't come back, or the fold stayed in settings.xml.");
                            // A folded project with a thread that needs you still turns Amber.
                            var needs = ClaudeView.Fixture(DateTime.UtcNow, true); needs.Collapsed.Add("C:\\Code\\api-server");
                            cardView.Update(needs); layout();
                            int api = cardView.View.Groups(cardView.Period).FindIndex(g => g.Folder == "C:\\Code\\api-server");
                            var apiCritter = (ClaudeCritter)window.FindName("ClaudeCritter" + api);
                            Assert(apiCritter.Collapsed && apiCritter.State == ClaudeState.NeedsYou && cardView.Columns()[api].Value.Count == 0, "A folded project hides that a thread needs you.");
                        }
                    }
                    finally { window.Close(); }
                }
            }
        });
    }
}
