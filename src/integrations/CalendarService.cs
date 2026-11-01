using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    // A read-only calendar feed: a name for the card and its iCalendar address. The address is a secret (anyone who has
    // it can read the calendar), so it's only kept in calendar-feeds.dat, encrypted for the Windows account.
    public sealed class CalendarFeed
    {
        public string Name { get; set; }
        public string Url { get; set; }
        public CalendarFeed Copy() { return (CalendarFeed)MemberwiseClone(); }
    }

    // How each feed's last update went.
    public sealed class FeedStatus { public string Name; public bool Ok; public string Problem; public DateTime UpdatedUtc; }

    // The Calendar widget's feeds (docs/design/calendar.md): read-only .ics addresses, fetched every 15 minutes while
    // the card is shown, merged into one agenda for the next two weeks. No accounts and no OAuth: a feed is the
    // "secret address in iCal format" Google gives, or an Outlook calendar published as ICS.
    public sealed class CalendarService : IDisposable
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15), QuietInterval = TimeSpan.FromMinutes(30), Window = TimeSpan.FromDays(14);
        public const int MaxFeeds = 8, MaxBytes = 5 * 1024 * 1024, MaxEvents = 200;
        private readonly string storePath;
        private readonly HttpClient http = new HttpClient(new PreferIPv4Handler()) { Timeout = TimeSpan.FromSeconds(20) };
        private readonly Dictionary<string, string> lastText = new Dictionary<string, string>();
        public List<CalendarFeed> Feeds { get; private set; }
        public string LoadWarning { get; private set; }

        public static string PathFor(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "calendar-feeds.dat"); }

        public CalendarService(string storePath)
        {
            this.storePath = storePath; Feeds = new List<CalendarFeed>();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WidgetScreen/1.0");
            try
            {
                if (File.Exists(storePath))
                {
                    var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(storePath), null, DataProtectionScope.CurrentUser));
                    Feeds = Clean(new JavaScriptSerializer().Deserialize<List<CalendarFeed>>(json));
                }
            }
            catch (Exception) { LoadWarning = "Your calendar feeds couldn't be read. Add them again in Settings → Calendar."; Feeds = new List<CalendarFeed>(); }
        }

        public static List<CalendarFeed> Clean(IEnumerable<CalendarFeed> feeds)
        {
            return (feeds ?? new CalendarFeed[0]).Where(f => f != null && Address(f.Url) != null).Take(MaxFeeds)
                .Select(f => new CalendarFeed { Name = String.IsNullOrWhiteSpace(f.Name) ? DefaultName(f.Url) : f.Name.Trim().Substring(0, Math.Min(30, f.Name.Trim().Length)), Url = f.Url.Trim() }).ToList();
        }

        // webcal:// is the same feed over https; only http and https addresses are fetched.
        public static Uri Address(string url)
        {
            url = (url ?? "").Trim();
            if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http") && !String.IsNullOrEmpty(uri.Host) ? uri : null;
        }
        public static string DefaultName(string url)
        {
            var uri = Address(url); if (uri == null) return "Calendar";
            return uri.Host.Contains("google.") ? "Google Calendar" : uri.Host.Contains("outlook.") || uri.Host.Contains("office365.") || uri.Host.Contains("live.") ? "Outlook" : uri.Host.Contains("icloud.") ? "iCloud" : "Calendar";
        }
        // The address as Settings shows it after saving: the host and the end of the path, never the secret part.
        public static string Masked(string url)
        {
            var uri = Address(url); if (uri == null) return "";
            string last = uri.Segments.Length > 0 ? uri.Segments[uri.Segments.Length - 1] : "";
            return uri.Host + "/…/" + (last.Length > 24 ? "…" + last.Substring(last.Length - 20) : last);
        }

        public void Save(List<CalendarFeed> feeds)
        {
            var clean = Clean(feeds);
            if (clean.Count == 0) { if (File.Exists(storePath)) File.Delete(storePath); Feeds = clean; return; }
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(clean)), null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storePath)));
            string temporary = storePath + ".tmp"; File.WriteAllBytes(temporary, bytes);
            if (File.Exists(storePath)) File.Replace(temporary, storePath, null); else File.Move(temporary, storePath);
            Feeds = clean; LoadWarning = null;
        }

        // Fetches every feed (a failed one keeps its last good copy) and merges the next two weeks, soonest first.
        public async Task<Tuple<List<CalendarEvent>, List<FeedStatus>>> ReadAsync(DateTime utcNow, TimeZoneInfo local, CancellationToken cancel)
        {
            var feeds = Feeds.ToList();
            var texts = await Task.WhenAll(feeds.Select(f => Fetch(f, cancel)));
            var events = new List<CalendarEvent>(); var statuses = new List<FeedStatus>();
            for (int i = 0; i < feeds.Count; i++)
            {
                var status = new FeedStatus { Name = feeds[i].Name, Ok = texts[i].Item2 == null, Problem = texts[i].Item2, UpdatedUtc = utcNow };
                statuses.Add(status);
                string text = texts[i].Item1;
                if (text == null) continue;
                try { int feed = i; events.AddRange(await Task.Run(() => IcsCalendar.Events(text, utcNow, utcNow + Window, local, feed), cancel)); }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { status.Ok = false; status.Problem = "The feed couldn't be read as a calendar."; }
            }
            return Tuple.Create(Merge(events, utcNow), statuses);
        }
        public static List<CalendarEvent> Merge(IEnumerable<CalendarEvent> events, DateTime utcNow)
        {
            return events.Where(e => (e.EndUtc > e.StartUtc ? e.EndUtc : e.StartUtc.AddMinutes(1)) > utcNow).OrderBy(e => e.StartUtc).ThenBy(e => e.AllDay ? 0 : 1).ThenBy(e => e.Title).Take(MaxEvents).ToList();
        }

        private async Task<Tuple<string, string>> Fetch(CalendarFeed feed, CancellationToken cancel)
        {
            string last; lastText.TryGetValue(feed.Url, out last);
            try
            {
                using (var response = await http.GetAsync(Address(feed.Url), HttpCompletionOption.ResponseHeadersRead, cancel))
                {
                    if ((int)response.StatusCode == 401 || (int)response.StatusCode == 403 || (int)response.StatusCode == 404) return Tuple.Create(last, "The address was refused. It may have been reset; copy it again.");
                    if (!response.IsSuccessStatusCode) return Tuple.Create(last, "The calendar server answered " + (int)response.StatusCode + ".");
                    if (response.Content.Headers.ContentLength > MaxBytes) return Tuple.Create(last, "The feed is too large.");
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    if (bytes.Length > MaxBytes) return Tuple.Create(last, "The feed is too large.");
                    string text = Encoding.UTF8.GetString(bytes);
                    if (!text.Contains("BEGIN:VCALENDAR")) return Tuple.Create(last, "That address isn't an iCalendar feed. Use the iCal or ICS address.");
                    lastText[feed.Url] = text;
                    return Tuple.Create(text, (string)null);
                }
            }
            catch (OperationCanceledException) { if (cancel.IsCancellationRequested) throw; return Tuple.Create(last, "The calendar server didn't answer in time."); }
            catch (HttpRequestException) { return Tuple.Create(last, "Couldn't reach the calendar. Check the connection."); }
        }

        public void Dispose() { http.Dispose(); }
    }
}
