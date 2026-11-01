using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Calendar widget (docs/design/calendar.md): read-only ICS feeds fetched every 15 minutes while the card is
    // shown (30 in Quiet mode), merged into one agenda. A phone shares the same reading; feed addresses never leave
    // the PC.
    public sealed partial class DashboardController
    {
        private CalendarService calendar;
        private CalendarCard calendarCard;
        private List<CalendarEvent> calendarEvents;
        private List<FeedStatus> calendarFeeds = new List<FeedStatus>();
        private DateTime lastCalendarAttempt = DateTime.MinValue;
        private bool calendarBusy;
        private int calendarGeneration;
        private List<CalendarFeed> pendingFeeds = new List<CalendarFeed>();
        // For the checks (tests/Calendar/CalendarTests.cs).
        public CalendarService Calendar { get { return calendar; } }
        public CalendarCard CalendarView { get { return calendarCard; } }

        private void InitializeCalendar()
        {
            calendar = new CalendarService(CalendarService.PathFor(settingsPath));
            if (calendar.LoadWarning != null && pendingMessage == null) pendingMessage = calendar.LoadWarning;
            calendarCard = new CalendarCard(window, Control<Grid>("CalendarBody"), Control<TextBlock>("CalendarStatus"));
            // The whole card is the refresh tap; without a feed it opens Settings → Calendar.
            Action tapped = () => {
                if (Editor.Editing) return;
                if (calendar.Feeds.Count == 0) { OpenSettingsTab("Calendar"); window.UpdateLayout(); Control<TextBox>("CalendarFeedUrl").Focus(); }
                else RefreshCalendar();
            };
            Control<Border>("CalendarCard").PreviewMouseLeftButtonUp += (sender, e) => tapped();
            Control<Border>("CalendarCard").PreviewTouchUp += (sender, e) => tapped();
            Button("CalendarFeedAdd", AddPendingFeed);
        }

        private TimeSpan CalendarInterval { get { return settings.QuietMode ? CalendarService.QuietInterval : CalendarService.Interval; } }
        private void ApplyCalendar() { calendarCard.Configure(calendar.Feeds.Count, settings.Use24HourClock, settings.TextScale); }

        // The heartbeat moves the countdown on, and reads the feeds again once the interval has passed.
        private void TickCalendar()
        {
            if (!Editor.IsVisible("Calendar")) return;
            calendarCard.Render(DateTime.UtcNow);
            if (calendar.Feeds.Count > 0 && DateTime.UtcNow - lastCalendarAttempt >= CalendarInterval) RefreshCalendar();
        }

        private async void RefreshCalendar()
        {
            if (disposed || calendarBusy || calendar.Feeds.Count == 0) return;
            calendarBusy = true; lastCalendarAttempt = DateTime.UtcNow; int generation = calendarGeneration;
            try
            {
                var read = await calendar.ReadAsync(DateTime.UtcNow, TimeZoneInfo.Local, remoteLifetime.Token);
                if (disposed || generation != calendarGeneration) return;
                calendarEvents = read.Item1; calendarFeeds = read.Item2;
                calendarCard.Update(calendarEvents, calendarFeeds);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!disposed && generation == calendarGeneration) calendarCard.Failed(); }
            finally { calendarBusy = false; }
        }

        // ----- Settings → Calendar -----

        private void OpenCalendarSettings()
        {
            pendingFeeds = calendar.Feeds.Select(f => f.Copy()).ToList();
            Control<TextBox>("CalendarFeedName").Text = ""; Control<TextBox>("CalendarFeedUrl").Text = "";
            ShowPendingFeeds();
        }
        private void ShowPendingFeeds()
        {
            var list = Control<StackPanel>("CalendarFeedList"); list.Children.Clear();
            if (pendingFeeds.Count == 0) { var none = new TextBlock { Text = "No calendars yet.", Margin = new Thickness(0, 0, 0, 8) }; list.Children.Add(none); }
            for (int i = 0; i < pendingFeeds.Count; i++)
            {
                var feed = pendingFeeds[i]; int index = i;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                text.Children.Add(new TextBlock { Text = feed.Name });
                var status = calendarFeeds.FirstOrDefault(s => s.Name == feed.Name);
                var detail = new TextBlock { Text = CalendarService.Masked(feed.Url) + (status != null && !status.Ok ? "  ·  " + status.Problem : ""), TextWrapping = TextWrapping.Wrap };
                detail.SetResourceReference(FrameworkElement.StyleProperty, "Small"); text.Children.Add(detail);
                row.Children.Add(text);
                var remove = new System.Windows.Controls.Button { Content = "Remove", MinHeight = 52, Margin = new Thickness(12, 0, 0, 0) };
                AutomationProperties.SetName(remove, "Remove " + feed.Name);
                remove.Click += (sender, e) => { pendingFeeds.RemoveAt(index); ShowPendingFeeds(); Text("SettingsStatus", "The calendar is removed when you save."); };
                Grid.SetColumn(remove, 1); row.Children.Add(remove);
                list.Children.Add(row);
            }
            Control<System.Windows.Controls.Button>("CalendarFeedAdd").IsEnabled = pendingFeeds.Count < CalendarService.MaxFeeds;
        }
        private void AddPendingFeed()
        {
            string url = Control<TextBox>("CalendarFeedUrl").Text.Trim(), name = Control<TextBox>("CalendarFeedName").Text.Trim();
            if (CalendarService.Address(url) == null) { Text("SettingsStatus", "Paste the calendar's full iCal address, starting with https:// or webcal://."); return; }
            if (pendingFeeds.Any(f => f.Url == url)) { Text("SettingsStatus", "That calendar is already in the list."); return; }
            pendingFeeds.Add(new CalendarFeed { Name = name.Length == 0 ? CalendarService.DefaultName(url) : name, Url = url });
            Control<TextBox>("CalendarFeedName").Text = ""; Control<TextBox>("CalendarFeedUrl").Text = "";
            ShowPendingFeeds(); Text("SettingsStatus", "The calendar is added when you save.");
        }
        // Saved with the other settings; a change of feeds reads the calendars again.
        private string SaveCalendarFeeds()
        {
            var before = String.Join("\n", calendar.Feeds.Select(f => f.Name + "|" + f.Url));
            if (before == String.Join("\n", CalendarService.Clean(pendingFeeds).Select(f => f.Name + "|" + f.Url))) return null;
            try { calendar.Save(pendingFeeds); }
            catch (Exception e) { return "Calendars: couldn't save the feeds (" + e.Message + ")."; }
            calendarGeneration++; calendarEvents = null; calendarFeeds = new List<FeedStatus>(); lastCalendarAttempt = DateTime.MinValue;
            calendarCard.Configure(calendar.Feeds.Count, settings.Use24HourClock, settings.TextScale); calendarCard.Update(null, null);
            return null;
        }

        // ----- The phone -----

        private static double CalendarEpoch(DateTime utc) { return (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
        // Titles, places and times only; feed addresses never leave the PC.
        private Dictionary<string, object> RemoteCalendar()
        {
            var now = DateTime.UtcNow;
            var events = calendarEvents == null ? null : CalendarService.Merge(calendarEvents, now);
            return D("feeds", calendar.Feeds.Count, "read", events != null, "pcOffset", (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(now).TotalMinutes),
                "failed", calendarFeeds.Where(f => !f.Ok).Select(f => (object)(f.Name + ": " + f.Problem)).ToArray(),
                "events", (events ?? new List<CalendarEvent>()).Take(60).Select(e => (object)D("title", e.Title, "location", e.Location, "start", CalendarEpoch(e.StartUtc), "end", CalendarEpoch(e.EndUtc),
                    "allDay", e.AllDay, "date", e.AllDay ? e.Date.ToString("yyyy-MM-dd") : "", "endDate", e.AllDay ? e.EndDate.ToString("yyyy-MM-dd") : "", "feed", e.Feed)).ToArray());
        }
    }
}
