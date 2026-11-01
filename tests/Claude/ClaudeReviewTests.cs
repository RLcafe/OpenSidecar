using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WidgetScreen;

// Checks for the Claude watcher review fixes (1 Nov 2026; docs/decisions/022-claude-review-fixes.md): text that fits on
// Medium and at 140%, the frame clock stopping when minimised, reads that never wait on a scan, whole ledger rows,
// covered cars rolling under the chips, the freshness stamp, and the telemetry, window-cache and backup hardening.
// Everything uses the labelled sample or temporary folders; nothing reads or writes the real Claude Code files.
internal static class ClaudeReviewTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static readonly DateTime Now = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
    private static string Fresh(string directory, string name)
    {
        string path = Path.Combine(directory, "claude-review", name);
        if (Directory.Exists(path)) Directory.Delete(path, true);
        Directory.CreateDirectory(path);
        return path;
    }
    private static Window NewWindow() { using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml")) return (Window)System.Windows.Markup.XamlReader.Load(stream); }
    private static string Settings(string folder, string name, double width, double height, double scale)
    {
        string path = Path.Combine(folder, name + ".xml");
        var settings = new AppSettings { TextScale = scale, ClaudePeriod = "Week", HintDismissed = true };
        settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Claude"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = width; p.Height = height; } return p; }).ToArray();
        SettingsStore.Save(path, settings);
        return path;
    }
    private static void Lay(Window window)
    {
        var dashboard = (FrameworkElement)window.FindName("Dashboard");
        for (int i = 0; i < 3; i++) { dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout(); }
    }
    private static void Wait(int ms)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (s, e) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    // A plain TextBlock whose words are wider than the room it got (cut off with an ellipsis, or clipped).
    private static bool Cut(TextBlock block)
    {
        if (String.IsNullOrEmpty(block.Text) || block.ActualWidth <= 0) return false;
        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
#pragma warning disable 618
        var text = new FormattedText(block.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, block.FontSize, Brushes.Black);
#pragma warning restore 618
        // A couple of units of slack: layout rounds widths that FormattedText keeps exact.
        return text.Width > block.ActualWidth + 2;
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Claude Medium at its default size cuts no text off, and keeps the two most urgent summary parts", () => {
            // Review fix 3: the heading summary, the project lines (with "+N hidden") and the replies column all fit.
            string folder = Fresh(directory, "medium");
            foreach (bool needs in new[] { false, true })
            {
                var window = NewWindow();
                try
                {
                    using (var controller = new DashboardController(window, Settings(folder, "medium-" + needs, 472, 310, 1), true))
                    {
                        controller.UseClaudeSample(needs); Lay(window);
                        var card = (FrameworkElement)window.FindName("ClaudeCard");
                        Assert(controller.ClaudeCardView.Current == CardSizing.Design.Medium, "The 472 × 310 card isn't Medium.");
                        var cut = Descendants(card).OfType<TextBlock>().Where(Cut).Select(t => "\"" + t.Text + "\" (" + Math.Round(t.ActualWidth) + ")").ToList();
                        Assert(cut.Count == 0, "Medium cuts text off" + (needs ? " while a thread needs you" : "") + ": " + String.Join(", ", cut));
                    }
                }
                finally { window.Close(); }
            }
            var view = ClaudeView.Fixture(Now, true);
            Assert(ClaudeText.Summary(view, ClaudePeriod.Week, false).Split('·').Length <= 2 && ClaudeText.Summary(view, ClaudePeriod.Week, true).Contains("this week"), "The Medium summary isn't the two most urgent parts: " + ClaudeText.Summary(view, ClaudePeriod.Week, false));
            var group = view.Groups(ClaudePeriod.Week).First(g => g.Name == "widget-screen");
            Assert(!ClaudeText.GroupDetail(group, true).Contains("Claude app") && ClaudeText.GroupDetail(group).Contains("Claude app") && ClaudeText.GroupDetail(group, true).EndsWith("hidden"), "The compact project line keeps where it runs: " + ClaudeText.GroupDetail(group, true));
        });

        check("Claude Large at 140% keeps thread titles readable: where-and-age, then Cache reads step aside", () => {
            // Review fix 4: at 1216 × 576 and 640 × 420, every thread title has room for about 14 characters.
            string folder = Fresh(directory, "large140");
            foreach (var size in new[] { new Size(1216, 576), new Size(640, 420), new Size(1856, 576) })
            {
                var window = NewWindow();
                try
                {
                    using (var controller = new DashboardController(window, Settings(folder, "large-" + size.Width, size.Width, size.Height, 1.4), true))
                    {
                        controller.UseClaudeSample(false); Lay(window);
                        var titles = Enumerable.Range(0, 4).SelectMany(i => Enumerable.Range(0, 4).Select(j => window.FindName("ClaudeThreadTitle" + i + "_" + j) as TextBlock)).Where(t => t != null && t.ActualWidth > 0).ToList();
                        Assert(titles.Count > 0, "No thread titles at " + size + ".");
                        Assert(titles.All(t => t.ActualWidth >= CardSizing.ClaudeTitleMin(1.4) - 2), "A thread title is squeezed at 140% on " + size + ": " + String.Join(", ", titles.Select(t => Math.Round(t.ActualWidth))));
                    }
                }
                finally { window.Close(); }
            }
            double narrow = 1216 - 48 - 16 - CardSizing.ClaudeSideWidth;
            Assert(!CardSizing.ClaudeWhere(narrow, 1.4) && CardSizing.ClaudeCache(narrow, 1.4) && CardSizing.ClaudeWhere(narrow, 1) && CardSizing.ClaudeCache(narrow, 1), "Where-and-age and Cache reads step aside in the wrong order.");
            Assert(!CardSizing.ClaudeCache(640 - 48, 1.4) && CardSizing.ClaudeCache(1856 - 48 - 16 - CardSizing.ClaudeSideWidth, 1.4), "Cache reads don't step aside on a small card at 140%.");
        });

        check("Claude's frame clock stops when the window is minimised and starts again when it's back; rolling cars keep their covers under the chips", () => {
            // Review fixes 5 and 8, in a real window off screen with the labelled sample.
            string folder = Fresh(directory, "minimise");
            var window = NewWindow();
            try
            {
                using (var controller = new DashboardController(window, Settings(folder, "min", 1216, 576, 1), true))
                {
                    controller.UseClaudeSample(false);
                    window.ShowActivated = false; window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -5000; window.Top = 0; window.Width = 1920; window.Height = 720;
                    window.Show(); Wait(400);
                    var card = controller.ClaudeCardView;
                    card.SetLive(true); Wait(200);
                    Assert(card.Clock.Running, "The cars don't move in a shown window.");
                    window.WindowState = WindowState.Minimized; Wait(300);
                    Assert(!card.Clock.Running, "The frame clock still runs while the window is minimised.");
                    window.WindowState = WindowState.Normal; Wait(300);
                    Assert(card.Clock.Running, "The frame clock didn't start again after the window came back.");
                    // The roll overlay is under the content, and an ended car rolls in under its cover, dimmed.
                    var host = (Panel)window.FindName("ClaudeBody");
                    Assert(host.Children.Count > 0 && host.Children[0] is Canvas, "The roll overlay isn't under the card's content.");
                    Assert(card.Toggle("C:\\Code\\api-server") && card.Animating, "The garage didn't start rolling its cars in.");
                    var overlay = (Canvas)host.Children[0];
                    bool coveredSeen = false;
                    for (int i = 0; i < 12 && card.Animating; i++)
                    {
                        Wait(90);
                        coveredSeen |= overlay.Children.OfType<PitSprite>().Any(s => s.Visibility == Visibility.Visible && s.Key != null && s.Key.EndsWith("|True") && Math.Abs(s.Opacity - 0.6) < 0.01);
                    }
                    Assert(coveredSeen, "The ended car lost its cover while rolling.");
                    window.Hide(); Wait(200);
                    Assert(!card.Clock.Running, "The frame clock runs while the window is hidden.");
                }
            }
            finally { window.Close(); }
        });

        check("Claude's log readings never wait on a scan in progress", () => {
            // Review fix 6: a reader during a long first scan gets the last published snapshot at once.
            string config = Fresh(directory, "slow-scan"), logs = Path.Combine(config, "projects", "C--Code-big");
            Directory.CreateDirectory(logs);
            var text = new StringBuilder();
            for (int i = 0; i < 60000; i++)
                text.Append("{\"type\":\"assistant\",\"sessionId\":\"big\",\"cwd\":\"C:\\\\Code\\\\big\",\"timestamp\":\"").Append(DateTime.UtcNow.AddMinutes(-i % 600).ToString("o")).Append("\",\"requestId\":\"r").Append(i)
                    .Append("\",\"message\":{\"id\":\"m").Append(i).Append("\",\"model\":\"claude-opus-5-5\",\"content\":[{\"type\":\"text\",\"text\":\"").Append(new string('x', 120)).Append("\"}],\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}}\n");
            File.WriteAllText(Path.Combine(logs, "big.jsonl"), text.ToString());
            var usage = new ClaudeUsage(config);
            var scan = Task.Run(() => usage.Scan(DateTime.UtcNow));
            long slowest = 0, reads = 0;
            var whole = Stopwatch.StartNew();
            while (!scan.IsCompleted && whole.ElapsedMilliseconds < 20000)
            {
                var one = Stopwatch.StartNew(); var seen = usage.LastSeen; var entries = usage.Entries; var titles = usage.Titles; one.Stop();
                slowest = Math.Max(slowest, one.ElapsedMilliseconds); reads++;
                System.Threading.Thread.Sleep(5);
            }
            scan.Wait();
            Assert(usage.Entries.Count == 60000, "The scan didn't read every reply: " + usage.Entries.Count);
            Assert(slowest < 150, "A reader waited " + slowest + " ms for a scan that took " + whole.ElapsedMilliseconds + " ms (" + reads + " reads).");
        });

        check("Claude's ledger shows whole rows only", () => {
            // Review fix 7: the list's height is a run of whole rows from the top.
            Assert(CardSizing.ClaudeWholeRows(new[] { 52.0, 36, 36, 52 }, 150) == 124 && CardSizing.ClaudeWholeRows(new[] { 52.0, 36 }, 200) == 88 && CardSizing.ClaudeWholeRows(new[] { 80.0 }, 50) == 50 && CardSizing.ClaudeWholeRows(new double[0], 100) == 0, "Whole rows worked out wrongly.");
            string folder = Fresh(directory, "rows");
            foreach (var size in new[] { new Size(1216, 576), new Size(640, 420) })
                foreach (bool needs in new[] { false, true })
                {
                    var window = NewWindow();
                    try
                    {
                        using (var controller = new DashboardController(window, Settings(folder, "rows-" + size.Width + needs, size.Width, size.Height, 1), true))
                        {
                            controller.UseClaudeSample(needs); Lay(window);
                            var scroll = (ScrollViewer)window.FindName("ClaudeLedger");
                            var list = (StackPanel)scroll.Content;
                            double used = 0; var ends = new List<double>();
                            foreach (FrameworkElement row in list.Children) { used += row.ActualHeight + row.Margin.Top + row.Margin.Bottom; ends.Add(used); }
                            Assert(ends.Any(e => Math.Abs(e - scroll.ActualHeight) < 1) || scroll.ActualHeight >= used - 0.5, "The ledger cuts a row at " + size + (needs ? " with the pit stop" : "") + ": " + Math.Round(scroll.ActualHeight) + " of " + String.Join(",", ends.Select(e => Math.Round(e))));
                        }
                    }
                    finally { window.Close(); }
                }
        });

        check("Claude counts a completed thread as resolved by its newest reply, not by when its log was last touched", () => {
            // Review, 1 Nov 2026: Claude Code touches a log about an hour after a turn, which kept every ended thread from
            // resolving. A reply after the app's record still un-resolves it.
            string config = Fresh(directory, "resolved-config"), app = Fresh(directory, "resolved-app"), logs = Path.Combine(config, "projects", "C--Code-res");
            Directory.CreateDirectory(logs);
            DateTime active = DateTime.UtcNow.AddHours(-3);
            Func<string, DateTime, string> reply = (id, at) => "{\"type\":\"assistant\",\"sessionId\":\"" + id + "\",\"cwd\":\"C:\\\\Code\\\\res\",\"timestamp\":\"" + at.ToString("o") + "\",\"requestId\":\"r-" + id + at.Ticks + "\",\"message\":{\"id\":\"m-" + id + at.Ticks + "\",\"model\":\"claude-opus-5-5\",\"usage\":{\"input_tokens\":10,\"output_tokens\":5}}}\n";
            foreach (var pair in new[] { new { Id = "done-quiet", Last = active }, new { Id = "done-again", Last = active.AddMinutes(10) } })
            {
                string log = Path.Combine(logs, pair.Id + ".jsonl");
                File.WriteAllText(log, reply(pair.Id, active.AddMinutes(-5)) + reply(pair.Id, pair.Last));
                File.SetLastWriteTimeUtc(log, active.AddMinutes(61));
                string record = Path.Combine(app, "org", "account", "local_" + pair.Id + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(record));
                File.WriteAllText(record, "{\"cliSessionId\":\"" + pair.Id + "\",\"originCwd\":\"C:\\\\Code\\\\res\",\"createdAt\":" + ClaudeBridge.Epoch(active.AddHours(-1)) + ",\"lastActivityAt\":" + ClaudeBridge.Epoch(active)
                    + ",\"isArchived\":false,\"title\":\"" + pair.Id + "\",\"postTurnSummary\":{\"status_category\":\"completed\",\"summarizes_uuid\":\"u\"},\"lastAssistantUuid\":\"u\"}");
            }
            using (var service = new ClaudeService(Fresh(directory, "resolved-bridge"), config, appThreads: new ClaudeAppThreads(() => new[] { app })))
            {
                service.Usage.Scan(DateTime.UtcNow);
                service.ReadSessions(DateTime.UtcNow, true);
                var view = service.View(DateTime.UtcNow, service.Usage.Entries);
                var quiet = view.Threads.First(t => t.Id == "done-quiet"); var again = view.Threads.First(t => t.Id == "done-again");
                Assert(quiet.Pit == PitState.Resolved && again.Pit == PitState.End, "Resolved is decided wrongly: " + quiet.Pit + " / " + again.Pit);
            }
        });

        check("Claude Large says how fresh the plan numbers are", () => {
            // Review fix 10: "LIVE · AS OF …" on Large, "AS OF …" alone when they're old, nothing extra on Medium.
            var view = ClaudeView.Fixture(DateTime.UtcNow, false);
            string large = ClaudeText.Flag(view, DateTime.UtcNow, false, true), medium = ClaudeText.Flag(view, DateTime.UtcNow, false);
            Assert(large.StartsWith("SAMPLE · LIVE · AS OF ") && medium == "SAMPLE", "The freshness stamp is wrong: \"" + large + "\" / \"" + medium + "\"");
            view.Limits.UpdatedAt = DateTime.UtcNow.AddHours(-1);
            string old = ClaudeText.Flag(view, DateTime.UtcNow, false, true);
            Assert(old.Contains("AS OF") && !old.Contains("LIVE"), "Old numbers still say live: " + old);
            view.Limits = null;
            Assert(ClaudeText.Flag(view, DateTime.UtcNow, false, true) == "SAMPLE", "No plan numbers still got a stamp.");
        });

        check("Claude's telemetry receiver wants application/json, the window cache forgets gone processes, and Connect keeps dated backups", () => {
            // Review fix 11. The receiver: no Content-Type, or another one, is refused.
            Func<string, int> post = header => {
                string body = "{}";
                var stream = new MemoryStream(Encoding.ASCII.GetBytes("POST /v1/logs HTTP/1.1\r\nHost: 127.0.0.1\r\n" + header + "Content-Length: " + body.Length + "\r\n\r\n" + body));
                string path; byte[] read; return ClaudeTelemetry.Read(stream, out path, out read);
            };
            Assert(post("") == 415 && post("Content-Type: text/plain\r\n") == 415 && post("Content-Type: application/json\r\n") == 200 && post("Content-Type: application/json; charset=utf-8\r\n") == 200, "The receiver takes posts that aren't marked JSON.");
            // The window cache: a process in Claude Code's list is looked up; once it leaves the list it's forgotten.
            string config = Fresh(directory, "owners"), sessions = Path.Combine(config, "sessions");
            Directory.CreateDirectory(sessions);
            string entry = Path.Combine(sessions, "4242.json");
            File.WriteAllText(entry, "{\"pid\":4242,\"sessionId\":\"o1\",\"cwd\":\"C:\\\\Code\\\\o\",\"status\":\"idle\",\"startedAt\":" + ClaudeBridge.Epoch(DateTime.UtcNow.AddMinutes(-5)) + ",\"statusUpdatedAt\":" + ClaudeBridge.Epoch(DateTime.UtcNow) + "}");
            using (var service = new ClaudeService(Fresh(directory, "owners-bridge"), config, appThreads: new ClaudeAppThreads(() => new string[0])))
            {
                service.RegistryAlive = pid => pid == 4242;
                service.ReadSessions(DateTime.UtcNow, true); service.View(DateTime.UtcNow, null);
                Assert(service.CachedOwners == 1, "The listed process's window wasn't looked up.");
                File.Delete(entry);
                service.ReadSessions(DateTime.UtcNow, true); service.View(DateTime.UtcNow, null);
                Assert(service.CachedOwners == 0, "A process that left Claude Code's list kept its window.");
            }
            // Dated backups: each Connect or Disconnect keeps one, the newest five stay, and the first file is safe until then.
            string claudeFolder = Fresh(directory, "backups"), bridge = Fresh(directory, "backups-bridge"), hook = Path.Combine(Fresh(directory, "backups-hook"), ClaudeBridge.HookExe);
            File.WriteAllText(hook, "not a real exe");
            File.WriteAllText(Path.Combine(claudeFolder, "settings.json"), "{\"model\":\"opus\"}");
            var connector = new ClaudeConnector(claudeFolder, bridge);
            connector.Connect(hook);
            Assert(connector.DatedBackups().Count == 1 && File.ReadAllText(connector.DatedBackups()[0]).Contains("opus") && !File.ReadAllText(connector.DatedBackups()[0]).Contains(ClaudeBridge.Marker), "The first backup isn't the original file.");
            for (int i = 0; i < 6; i++) { if (i % 2 == 0) connector.Disconnect(); else connector.Connect(hook); }
            var kept = connector.DatedBackups();
            Assert(kept.Count == ClaudeConnector.KeptBackups && File.Exists(Path.Combine(claudeFolder, "settings.json" + ClaudeConnector.BackupSuffix)), "Dated backups weren't kept to " + ClaudeConnector.KeptBackups + ": " + kept.Count);
        });
    }
}
