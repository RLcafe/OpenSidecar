using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows;
using WidgetScreen;

// Calendar and agenda (docs/design/calendar.md): reading iCalendar feeds the way Google and Outlook publish them,
// recurrence, the encrypted feed store and a feed served from loopback. The feeds here are labelled test fixtures.
internal static class CalendarTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.FindSystemTimeZoneById("Arab Standard Time");
    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) { return new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc); }

    // A Google-style feed: IANA time zone names with their VTIMEZONE, folded lines, escaped text, recurrence and exceptions.
    public const string GoogleFixture = "BEGIN:VCALENDAR\r\nPRODID:-//Google Inc//Google Calendar 70.9054//EN\r\nVERSION:2.0\r\nX-WR-CALNAME:TEST FIXTURE\r\n" +
        "BEGIN:VTIMEZONE\r\nTZID:America/New_York\r\nBEGIN:DAYLIGHT\r\nTZOFFSETFROM:-0500\r\nTZOFFSETTO:-0400\r\nTZNAME:EDT\r\nDTSTART:19700308T020000\r\nRRULE:FREQ=YEARLY;BYMONTH=3;BYDAY=2SU\r\nEND:DAYLIGHT\r\n" +
        "BEGIN:STANDARD\r\nTZOFFSETFROM:-0400\r\nTZOFFSETTO:-0500\r\nTZNAME:EST\r\nDTSTART:19701101T020000\r\nRRULE:FREQ=YEARLY;BYMONTH=11;BYDAY=1SU\r\nEND:STANDARD\r\nEND:VTIMEZONE\r\n" +
        // Weekly on Monday and Wednesday at 9:00 in New York, from 26 Oct 2026, ten times, with 4 Nov skipped and 9 Nov moved to 11:00.
        "BEGIN:VEVENT\r\nDTSTART;TZID=America/New_York:20261026T090000\r\nDTEND;TZID=America/New_York:20261026T093000\r\nRRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=10\r\nEXDATE;TZID=America/New_York:20261104T090000\r\nUID:standup@fixture\r\nSUMMARY:Stand-up\\, team\r\nLOCATION:Room 4\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nDTSTART;TZID=America/New_York:20261109T110000\r\nDTEND;TZID=America/New_York:20261109T113000\r\nRECURRENCE-ID;TZID=America/New_York:20261109T090000\r\nUID:standup@fixture\r\nSUMMARY:Stand-up (moved)\r\nEND:VEVENT\r\n" +
        // A one-off in UTC with a folded summary, an all-day holiday, and a cancelled meeting.
        "BEGIN:VEVENT\r\nDTSTART:20261102T120000Z\r\nDTEND:20261102T130000Z\r\nUID:lunch@fixture\r\nSUMMARY:Lunch with a very long\r\n  name that folds\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nDTSTART;VALUE=DATE:20261103\r\nDTEND;VALUE=DATE:20261104\r\nUID:holiday@fixture\r\nSUMMARY:Holiday\r\nEND:VEVENT\r\n" +
        "BEGIN:VEVENT\r\nDTSTART:20261105T100000Z\r\nDTEND:20261105T110000Z\r\nUID:gone@fixture\r\nSTATUS:CANCELLED\r\nSUMMARY:Cancelled\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    // An Outlook-style feed: a Windows time zone name, a monthly "last Friday" rule with UNTIL, a daily rule from years ago.
    public const string OutlookFixture = "BEGIN:VCALENDAR\nMETHOD:PUBLISH\nPRODID:Microsoft Exchange Server 2010\nVERSION:2.0\nX-WR-CALNAME:TEST FIXTURE\n" +
        "BEGIN:VEVENT\nDTSTART;TZID=Arab Standard Time:20260130T160000\nDTEND;TZID=Arab Standard Time:20260130T170000\nRRULE:FREQ=MONTHLY;BYDAY=-1FR;UNTIL=20261231T000000Z\nUID:review@fixture\nSUMMARY:Monthly review\nEND:VEVENT\n" +
        "BEGIN:VEVENT\nDTSTART;TZID=Arab Standard Time:20200101T070000\nDURATION:PT15M\nRRULE:FREQ=DAILY\nUID:walk@fixture\nSUMMARY:Morning walk\nEND:VEVENT\n" +
        "BEGIN:VEVENT\nDTSTART;TZID=Arab Standard Time:20251225T190000\nDTEND;TZID=Arab Standard Time:20251225T200000\nRRULE:FREQ=YEARLY\nUID:dinner@fixture\nSUMMARY:Anniversary dinner\nEND:VEVENT\nEND:VCALENDAR\n";

    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        return true;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static void Click(Window window, string name) { ((System.Windows.Controls.Button)window.FindName(name)).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)); }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Calendar reads a Google feed: time zones, folded lines, weekly repeats, skipped, moved and cancelled events", () => {
            var events = IcsCalendar.Events(GoogleFixture, Utc(2026, 11, 1), Utc(2026, 11, 15), Riyadh);
            var standups = events.Where(e => e.Title.StartsWith("Stand-up")).ToList();
            // Mon 2 Nov 9:00 EST is 14:00 UTC (daylight saving ended on 1 Nov); Wed 4 Nov is skipped; Mon 9 Nov moved to 11:00 EST.
            Assert(standups.Count == 3 && standups[0].StartUtc == Utc(2026, 11, 2, 14) && standups[0].EndUtc == Utc(2026, 11, 2, 14, 30) && standups[0].Title == "Stand-up, team" && standups[0].Location == "Room 4", "The weekly stand-up is wrong: " + String.Join(", ", standups.Select(s => s.StartUtc.ToString("dd HH:mm"))));
            Assert(!standups.Any(s => s.StartUtc.Day == 4) && standups[1].StartUtc == Utc(2026, 11, 9, 16) && standups[1].Title == "Stand-up (moved)" && standups[2].StartUtc == Utc(2026, 11, 11, 14), "Skipped or moved stand-ups are wrong.");
            // Before 1 Nov the zone is on daylight time: Wed 28 Oct 9:00 EDT is 13:00 UTC.
            var october = IcsCalendar.Events(GoogleFixture, Utc(2026, 10, 27), Utc(2026, 10, 30), Riyadh).Single(e => e.Title.StartsWith("Stand-up"));
            Assert(october.StartUtc == Utc(2026, 10, 28, 13), "The feed's own daylight-saving rule isn't followed.");
            var lunch = events.Single(e => e.Title.StartsWith("Lunch"));
            Assert(lunch.Title == "Lunch with a very long name that folds" && lunch.StartUtc == Utc(2026, 11, 2, 12), "A folded line isn't read.");
            var holiday = events.Single(e => e.Title == "Holiday");
            Assert(holiday.AllDay && holiday.Date == new DateTime(2026, 11, 3) && holiday.EndDate == new DateTime(2026, 11, 4) && holiday.StartUtc == Utc(2026, 11, 2, 21), "The all-day event is wrong.");
            Assert(!events.Any(e => e.Title == "Cancelled"), "A cancelled event is shown.");
            Assert(events.Select(e => e.StartUtc).SequenceEqual(events.Select(e => e.StartUtc).OrderBy(t => t)), "Events aren't soonest first.");
        });
        check("Calendar reads an Outlook feed: Windows zones, last Friday of the month, daily repeats from years ago, yearly", () => {
            var events = IcsCalendar.Events(OutlookFixture, Utc(2026, 11, 1), Utc(2026, 12, 1), TimeZoneInfo.Utc);
            var review = events.Single(e => e.Title == "Monthly review");
            Assert(review.StartUtc == Utc(2026, 11, 27, 13), "The last Friday in November isn't found: " + review.StartUtc);
            var walks = events.Where(e => e.Title == "Morning walk").ToList();
            Assert(walks.Count == 30 && walks[0].StartUtc == Utc(2026, 11, 1, 4) && walks[0].EndUtc == Utc(2026, 11, 1, 4, 15), "A daily repeat from 2020 isn't expanded to today: " + walks.Count);
            Assert(IcsCalendar.Events(OutlookFixture, Utc(2027, 1, 1), Utc(2027, 2, 1), TimeZoneInfo.Utc).All(e => e.Title != "Monthly review"), "UNTIL isn't kept.");
            Assert(IcsCalendar.Events(OutlookFixture, Utc(2026, 12, 20), Utc(2026, 12, 31), TimeZoneInfo.Utc).Single(e => e.Title == "Anniversary dinner").StartUtc == Utc(2026, 12, 25, 16), "The yearly event is wrong.");
            Assert(IcsCalendar.Events("not a calendar", Utc(2026, 11, 1), Utc(2026, 12, 1), Riyadh).Count == 0 && IcsCalendar.Events("BEGIN:VCALENDAR\nBEGIN:VEVENT\nSUMMARY:No start\nEND:VEVENT\nEND:VCALENDAR", Utc(2026, 11, 1), Utc(2026, 12, 1), Riyadh).Count == 0, "Broken feeds aren't ignored.");
        });
        check("Calendar wording: the next event, its countdown and the day it's on", () => {
            var now = Utc(2026, 11, 2, 10);
            var meeting = new CalendarEvent { Title = "Meeting", StartUtc = Utc(2026, 11, 2, 11, 30), EndUtc = Utc(2026, 11, 2, 12, 30) };
            Assert(CalendarText.Countdown(meeting, now) == "in 1 h 30 m" && CalendarText.Countdown(meeting, Utc(2026, 11, 2, 11, 45)) == "now  \u00b7  45 m left" && CalendarText.Countdown(meeting, new DateTime(2026, 11, 2, 11, 29, 30, DateTimeKind.Utc)) == "in under a minute", "Countdowns are wrong.");
            var all = new CalendarEvent { Title = "Holiday", AllDay = true, Date = new DateTime(2026, 11, 3), EndDate = new DateTime(2026, 11, 4), StartUtc = Utc(2026, 11, 2, 21), EndUtc = Utc(2026, 11, 3, 21) };
            Assert(CalendarText.When(all, Riyadh, false) == "All day" && CalendarText.When(meeting, Riyadh, false) == "2:30 PM \u2013 3:30 PM" && CalendarText.When(meeting, Riyadh, true) == "14:30 \u2013 15:30", "Times are wrong.");
            Assert(CalendarText.Day(Utc(2026, 11, 2, 11), Riyadh, Utc(2026, 11, 2, 10)) == "Today" && CalendarText.Day(Utc(2026, 11, 3, 11), Riyadh, Utc(2026, 11, 2, 10)) == "Tomorrow" && CalendarText.Day(Utc(2026, 11, 5, 11), Riyadh, Utc(2026, 11, 2, 10)) == "Thursday, Nov 5", "Days are wrong.");
            Assert(CalendarText.Day(all, Riyadh, Utc(2026, 11, 2, 22)) == "Today", "An all-day event's day isn't its date.");
        });
        check("Calendar feeds are kept encrypted, checked and masked", () => {
            string folder = Path.Combine(directory, "calendar-store"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "calendar-feeds.dat"); if (File.Exists(path)) File.Delete(path);
            var service = new CalendarService(path);
            Assert(service.Feeds.Count == 0 && service.LoadWarning == null, "A new store isn't empty.");
            string secret = "https://calendar.google.com/calendar/ical/fixture%40gmail.com/private-0123456789abcdef/basic.ics";
            service.Save(new List<CalendarFeed> { new CalendarFeed { Name = "", Url = secret }, new CalendarFeed { Name = "Work", Url = "webcal://outlook.office365.com/owa/calendar/abc/def/calendar.ics" }, new CalendarFeed { Name = "Bad", Url = "ftp://example.com/x.ics" }, new CalendarFeed { Name = "Bad", Url = "file:///C:/x.ics" } });
            Assert(service.Feeds.Count == 2 && service.Feeds[0].Name == "Google Calendar" && File.Exists(path) && !Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("private-0123"), "Feeds aren't cleaned or encrypted.");
            Assert(new CalendarService(path).Feeds.Count == 2 && CalendarService.Address("webcal://outlook.office365.com/x.ics").Scheme == "https" && !CalendarService.Masked(secret).Contains("private-0123") && CalendarService.Masked(secret).StartsWith("calendar.google.com/"), "Feeds didn't come back, or the address isn't masked.");
            service.Save(new List<CalendarFeed>()); Assert(!File.Exists(path), "Removing every feed left the file.");
        });
        check("Calendar fetches a feed (served on loopback) and keeps the last copy when it fails", () => {
            string folder = Path.Combine(directory, "calendar-fetch"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "calendar-feeds.dat"); if (File.Exists(path)) File.Delete(path);
            int port; var listener = new HttpListener();
            for (port = 43900; ; port++) { try { listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start(); break; } catch (HttpListenerException) { if (port > 43990) throw; } }
            int served = 0; bool fail = false;
            var thread = new Thread(() => {
                while (listener.IsListening)
                {
                    HttpListenerContext context; try { context = listener.GetContext(); } catch (Exception) { return; }
                    served++;
                    if (fail) { context.Response.StatusCode = 404; context.Response.Close(); continue; }
                    var bytes = Encoding.UTF8.GetBytes(GoogleFixture); context.Response.ContentType = "text/calendar"; context.Response.OutputStream.Write(bytes, 0, bytes.Length); context.Response.Close();
                }
            }) { IsBackground = true };
            thread.Start();
            try
            {
                var service = new CalendarService(path);
                service.Save(new List<CalendarFeed> { new CalendarFeed { Name = "Fixture", Url = "http://127.0.0.1:" + port + "/basic.ics" } });
                var read = service.ReadAsync(Utc(2026, 11, 1), Riyadh, CancellationToken.None).Result;
                Assert(read.Item2.Single().Ok && read.Item1.Any(e => e.Title == "Holiday") && read.Item1.All(e => e.Feed == 0), "The feed wasn't read.");
                fail = true;
                read = service.ReadAsync(Utc(2026, 11, 1), Riyadh, CancellationToken.None).Result;
                Assert(!read.Item2.Single().Ok && read.Item2.Single().Problem.Contains("refused") && read.Item1.Any(e => e.Title == "Holiday"), "A failed update didn't keep the last copy, or didn't say why.");
            }
            finally { listener.Stop(); }
        });
        check("Calendar designs keep the next event and agenda inside the card, and Settings adds and removes feeds", () => {
            var now = DateTime.UtcNow;
            // A labelled test fixture: made-up events around now, from two feeds.
            var fixture = new List<CalendarEvent> {
                new CalendarEvent { Title = "TEST FIXTURE: design review with a long title that has to be cut", Location = "Room 4", StartUtc = now.AddMinutes(25), EndUtc = now.AddMinutes(85), Feed = 0 },
                new CalendarEvent { Title = "Fixture: dentist", StartUtc = now.AddHours(5), EndUtc = now.AddHours(6), Feed = 1 },
                new CalendarEvent { Title = "Fixture: holiday", AllDay = true, Date = DateTime.Today.AddDays(1), EndDate = DateTime.Today.AddDays(2), StartUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.Today.AddDays(1)), EndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.Today.AddDays(2)), Feed = 0 },
                new CalendarEvent { Title = "Fixture: football", StartUtc = now.AddDays(3), EndUtc = now.AddDays(3).AddHours(2), Feed = 1 },
                new CalendarEvent { Title = "Fixture: flight", Location = "Terminal 1", StartUtc = now.AddDays(6), EndUtc = now.AddDays(6).AddHours(3), Feed = 0 } };
            Assert(CardSizing.Calendar(240, 152) == CardSizing.Design.Small && CardSizing.Calendar(472, 250) == CardSizing.Design.Medium && CardSizing.Calendar(439, 576) == CardSizing.Design.Medium && CardSizing.Calendar(440, 380) == CardSizing.Design.Large, "Calendar designs are picked at the wrong sizes.");
            foreach (var size in new[] { new Size(240, 152), new Size(340, 220), new Size(472, 250), new Size(472, 376), new Size(600, 576) })
                foreach (bool use24 in new[] { false, true })
                {
                    string folder = Path.Combine(directory, "calendar-" + size.Width + "x" + size.Height); Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "settings.xml"), feeds = Path.Combine(folder, "calendar-feeds.dat");
                    if (File.Exists(feeds)) File.Delete(feeds);
                    new CalendarService(feeds).Save(new List<CalendarFeed> { new CalendarFeed { Name = "Fixture A", Url = "https://calendar.example.invalid/a.ics" }, new CalendarFeed { Name = "Fixture B", Url = "https://calendar.example.invalid/b.ics" } });
                    var settings = new AppSettings { TextScale = 1.4, Use24HourClock = use24 };
                    settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Calendar"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                    SettingsStore.Save(path, settings);
                    using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                    {
                        var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                        try
                        {
                            using (var controller = new DashboardController(window, path, true))
                            {
                                controller.CalendarView.Update(fixture, new List<FeedStatus> { new FeedStatus { Name = "Fixture A", Ok = true }, new FeedStatus { Name = "Fixture B", Ok = false, Problem = "test" } });
                                var dashboard = (FrameworkElement)window.FindName("Dashboard");
                                dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                                var card = (FrameworkElement)window.FindName("CalendarCard");
                                Assert(controller.CalendarView.Current == CardSizing.Calendar(size.Width, size.Height) && ((System.Windows.Controls.TextBlock)window.FindName("CalendarCountdown")).Text.Contains("in 2") && ((System.Windows.Controls.TextBlock)window.FindName("CalendarStatus")).Text == "1 FEED FAILED", "The calendar isn't shown at " + size + ".");
                                var scroll = Descendants(card).OfType<System.Windows.Controls.ScrollViewer>().FirstOrDefault();
                                foreach (var part in Descendants(card).OfType<System.Windows.Controls.TextBlock>().Where(e => Shown(e, card) && e.ActualWidth > 0 && e.Text.Length > 0 && (scroll == null || !scroll.IsAncestorOf(e))))
                                {
                                    var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                    Assert(box.Left >= 23.5 && box.Top >= 23.5 && box.Right <= card.ActualWidth - 23.5 && box.Bottom <= card.ActualHeight - 23.5, "The calendar pushes \"" + part.Text + "\" out of the " + size + " card" + (use24 ? " (24 h)" : "") + ": " + box);
                                }
                                if (size.Width == 472 && size.Height == 250 && !use24)
                                {
                                    Click(window, "SettingsButton");
                                    var list = (System.Windows.Controls.StackPanel)window.FindName("CalendarFeedList");
                                    Assert(list.Children.Count == 2, "Settings doesn't list the feeds.");
                                    ((System.Windows.Controls.TextBox)window.FindName("CalendarFeedUrl")).Text = "ftp://nope"; Click(window, "CalendarFeedAdd");
                                    Assert(((System.Windows.Controls.TextBlock)window.FindName("SettingsStatus")).Text.StartsWith("Paste the calendar"), "A bad address was taken.");
                                    ((System.Windows.Controls.TextBox)window.FindName("CalendarFeedUrl")).Text = "webcal://calendar.example.invalid/c.ics"; Click(window, "CalendarFeedAdd");
                                    var remove = (System.Windows.Controls.Button)((System.Windows.Controls.Grid)list.Children[0]).Children[1]; remove.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                                    Click(window, "SaveSettings");
                                    var saved = new CalendarService(feeds).Feeds;
                                    Assert(saved.Count == 2 && saved[0].Name == "Fixture B" && saved[1].Url.StartsWith("webcal://") && controller.Calendar.Feeds.Count == 2, "Settings didn't add and remove feeds.");
                                }
                            }
                        }
                        finally { window.Close(); }
                    }
                }
        });
    }
}
