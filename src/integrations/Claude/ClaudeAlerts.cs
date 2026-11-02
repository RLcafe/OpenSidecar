using System;
using System.Collections.Generic;
using System.Linq;

namespace WidgetScreen
{
    // One moment worth telling the user about (package 3, decision 025): a session started needing you, got stuck, or
    // finished a turn of 10 minutes or more. Seq only grows, so the phone can tell which it has already shown.
    public sealed class ClaudeAlert
    {
        public long Seq;
        // "need", "stuck" or "done".
        public string Kind = "";
        public string Session = "", Project = "", Title = "", Text = "";
        public DateTime At;
    }

    // Watches the running sessions from one view to the next and says what newly happened. The PC shows each new alert
    // as a Windows notification (Settings → Claude), and the phone link carries the last ten minutes of them so the
    // phone can raise its own. Only names, states and the waiting message the card already shows; never a prompt.
    public sealed class ClaudeAlerts
    {
        public static readonly TimeSpan LongTurn = TimeSpan.FromMinutes(10), Kept = TimeSpan.FromMinutes(10);
        public const int Most = 20;
        private readonly Dictionary<string, KeyValuePair<ClaudeState, DateTime>> last = new Dictionary<string, KeyValuePair<ClaudeState, DateTime>>();
        private readonly List<ClaudeAlert> recent = new List<ClaudeAlert>();
        private long seq;

        // The newest alert's number (0 before any).
        public long Seq { get { return seq; } }

        // The running sessions now; returns the alerts this change brings, oldest first.
        public List<ClaudeAlert> Update(IEnumerable<ClaudeSession> running, DateTime now)
        {
            var fresh = new List<ClaudeAlert>();
            var seen = new HashSet<string>();
            foreach (var s in running ?? Enumerable.Empty<ClaudeSession>())
            {
                if (s == null || !s.Running || !seen.Add(s.Id)) continue;
                KeyValuePair<ClaudeState, DateTime> before;
                bool known = last.TryGetValue(s.Id, out before);
                string kind = null, text = "";
                if (s.State == ClaudeState.NeedsYou && (!known || before.Key != ClaudeState.NeedsYou)) { kind = "need"; text = s.Message ?? ""; }
                else if (s.State == ClaudeState.Stuck && (!known || before.Key != ClaudeState.Stuck)) { kind = "stuck"; text = s.ReasonText.Length > 0 ? s.ReasonText : s.Reason; }
                // A long turn finished: Working since the turn began, Done now.
                else if (s.State == ClaudeState.Done && known && before.Key == ClaudeState.Working && s.Since - before.Value >= LongTurn)
                { kind = "done"; text = "Finished after " + Minutes(s.Since - before.Value); }
                // Working keeps the moment the turn began, however often its Since moves.
                DateTime since = s.State == ClaudeState.Working && known && before.Key == ClaudeState.Working ? before.Value : s.Since;
                last[s.Id] = new KeyValuePair<ClaudeState, DateTime>(s.State, since);
                if (kind == null) continue;
                var alert = new ClaudeAlert { Seq = ++seq, Kind = kind, Session = s.Id, Project = s.ProjectName, Title = String.IsNullOrWhiteSpace(s.Title) ? ClaudeTitles.IdLabel(s.Id) : s.Title, Text = text ?? "", At = now };
                fresh.Add(alert); recent.Add(alert);
            }
            foreach (var id in last.Keys.Where(k => !seen.Contains(k)).ToList()) last.Remove(id);
            recent.RemoveAll(a => now - a.At > Kept);
            if (recent.Count > Most) recent.RemoveRange(0, recent.Count - Most);
            return fresh;
        }

        // The alerts of the last ten minutes, oldest first (the phone link's claudeAlerts).
        public List<ClaudeAlert> Recent(DateTime now) { return recent.Where(a => now - a.At <= Kept).ToList(); }

        private static string Minutes(TimeSpan time)
        {
            int minutes = (int)Math.Round(time.TotalMinutes);
            return minutes >= 60 ? minutes / 60 + " h " + (minutes % 60).ToString("00") : minutes + " min";
        }
    }
}
