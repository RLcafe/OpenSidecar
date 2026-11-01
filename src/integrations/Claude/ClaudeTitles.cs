using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace WidgetScreen
{
    // A thread's name on the Claude card (Ali, 1 Nov 2026; decision 018). The Claude app gives every Remote Control
    // thread the same placeholder title ("Remote Control session (from a project)"), so placeholders count as no title.
    // The name is then, in order: one the user typed (a rename), one the app or Claude Code generated, the first line
    // of the thread's first request, and last a short label from its id. Where a thread runs ("Remote Control",
    // "Desktop") is shown beside the name, never as it.
    public static class ClaudeTitles
    {
        public const int Longest = 60;
        private static readonly Regex Generic = new Regex(@"^(remote control|new|untitled|claude code|claude|desktop|vs code|terminal|session|thread)( (session|chat|thread|conversation))?( ?\(.*\))?$", RegexOptions.CultureInvariant);
        private static readonly Regex Message = new Regex(@"<message\b[^>]*>(.*?)</message>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex Note = new Regex(@"<note\b[^>]*>(.*?)</note>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex Hidden = new Regex(@"<(system-reminder|local-command-stdout|local-command-stderr|command-message|command-args)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex Tag = new Regex(@"<[^>]+>", RegexOptions.CultureInvariant);

        // An empty title, or one that names the kind of thread rather than the thread.
        public static bool Placeholder(string title)
        {
            if (String.IsNullOrWhiteSpace(title)) return true;
            string plain = Regex.Replace(title.Trim().ToLowerInvariant(), @"\s+", " ");
            return Generic.IsMatch(plain);
        }

        // The first candidate that is a real title, or "".
        public static string Pick(params string[] candidates)
        {
            var found = candidates.FirstOrDefault(c => !Placeholder(c));
            return found == null ? "" : found.Trim();
        }

        // A request's first line, about 60 characters, as a thread's name. A Remote Control thread's request arrives
        // wrapped in its envelope (the person's <message>, or a brief's <note>); Claude Code's own notes and a
        // compacted thread's summary aren't requests.
        public static string FromPrompt(string text)
        {
            if (String.IsNullOrWhiteSpace(text)) return "";
            string body = text;
            var message = Message.Match(body);
            if (message.Success) body = message.Groups[1].Value;
            else { var note = Note.Match(body); if (note.Success) body = note.Groups[1].Value; }
            body = Tag.Replace(Hidden.Replace(body, " "), " ");
            body = WebUtility.HtmlDecode(body).Trim();
            if (body.StartsWith("This session is being continued", StringComparison.OrdinalIgnoreCase) || body.StartsWith("Caveat:", StringComparison.OrdinalIgnoreCase) || body.StartsWith("[Request interrupted", StringComparison.OrdinalIgnoreCase)) return "";
            string line = body.Split('\n').Select(l => Regex.Replace(l, @"\s+", " ").Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            return Placeholder(line) ? "" : Short(line);
        }

        // Whether a message carries a Remote Control request (a person's <message> or a brief's <note>).
        public static bool Envelope(string text) { return !String.IsNullOrEmpty(text) && (Message.IsMatch(text) || Note.IsMatch(text)); }

        // At most 60 characters, cut at a word with "…".
        public static string Short(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length <= Longest) return text;
            int cut = text.LastIndexOf(' ', Longest - 1);
            if (cut < Longest / 2) cut = Longest - 1;
            return text.Substring(0, cut).TrimEnd(' ', ',', '.', ';', ':', '-') + "…";
        }

        // The last resort: "Thread 1b2187".
        public static string IdLabel(string id)
        {
            string letters = new string((id ?? "").Where(Char.IsLetterOrDigit).Take(6).ToArray());
            return letters.Length == 0 ? "Thread" : "Thread " + letters.ToLowerInvariant();
        }
    }
}
