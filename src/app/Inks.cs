using System;
using System.Collections.Generic;
using System.Linq;

namespace WidgetScreen
{
    // The Sticker Sheet inks (decision 017, docs/design/sticker-sheet.md): eight colours a widget's label strip, status
    // sticker and drawn parts take. Lime, Glacier, Lavender and Amber are also the system accents; Coral, Mint, Rose and
    // Lemon are widget inks only. Each passes 9:1 with Inks.DarkText and on every palette surface. The phone's
    // core/Inks.java holds the same names, colours and defaults.
    public static class Inks
    {
        public static readonly string[] Names = { "Lime", "Glacier", "Lavender", "Amber", "Coral", "Mint", "Rose", "Lemon" };
        // The inks Edit layout offers. Amber means a warning, so no widget owns it.
        public static readonly string[] Choices = { "Lime", "Glacier", "Lavender", "Coral", "Mint", "Rose", "Lemon" };
        // Text on an ink (the accent buttons' dark text).
        public const string DarkText = "#11170E";
        // Settings → Theme → Widget colours.
        public const string EachOwn = "Each", AccentOnly = "Accent";

        public static string Hex(string ink)
        {
            switch (ink)
            {
                case "Glacier": return "#8ED8F8";
                case "Lavender": return "#C5AEFF";
                case "Amber": return "#F5C47D";
                case "Coral": return "#FF9E85";
                case "Mint": return "#7FE6C0";
                case "Rose": return "#FFA6CF";
                case "Lemon": return "#F0E36E";
                default: return "#BAF28B";
            }
        }

        public static bool Known(string ink) { return ink != null && Names.Contains(ink); }

        // Every widget but Claude Code wears a label strip (the Claude card keeps its own design).
        public static bool InScope(string id) { return id != "Claude" && WidgetLayout.Ids.Contains(id); }

        // The widget's own ink. Weather, World clocks and Smart home will take theirs from live data later and use
        // Glacier until then.
        public static string Default(string id)
        {
            switch (id)
            {
                case "Mixer": case "Games": return "Lime";
                case "Soundboard": case "Storage": return "Lavender";
                case "Focus": case "Calendar": return "Coral";
                case "Spotify": case "Prayer": return "Mint";
                case "Media": case "Macros": return "Rose";
                case "System": case "Notes": return "Lemon";
                default: return "Glacier";
            }
        }

        // The label strip's words, and the short name a strip under 200 units wide shows beside its ink dot.
        public static string StripName(string id)
        {
            switch (id)
            {
                case "Focus": return "FOCUS";
                case "Media": return "NOW PLAYING";
                case "Shortcuts": return "QUICK LAUNCH";
                case "Mixer": return "SOUND MIXER";
                case "System": return "SYSTEM STATS";
                case "Prayer": return "PRAYER";
                case "Clocks": return "WORLD CLOCKS";
                case "Home": return "SMART HOME";
                case "Notes": return "NOTES";
                case "Clipboard": return "CLIPBOARD";
                default: return WidgetLayout.Title(id).ToUpperInvariant();
            }
        }
        public static string ShortName(string id)
        {
            switch (id)
            {
                case "Media": return "PLAYING";
                case "Shortcuts": return "LAUNCH";
                case "Mixer": return "MIXER";
                case "System": return "STATS";
                case "Soundboard": return "SOUNDS";
                case "Clocks": return "CLOCKS";
                case "Notes": return "NOTES";
                case "Clipboard": return "CLIPS";
                case "Home": return "HOME";
                default: return StripName(id);
            }
        }
        // A strip narrower than this shows the ink dot and the short name.
        public const double CompactBelow = 200;
        public static bool Compact(double cardWidth) { return cardWidth < CompactBelow; }

        // "Focus=Coral;Media=Rose": only widgets whose ink differs from their default are kept.
        public static Dictionary<string, string> Parse(string text)
        {
            var inks = new Dictionary<string, string>();
            foreach (string part in (text ?? "").Split(';'))
            {
                int equals = part.IndexOf('=');
                if (equals <= 0) continue;
                string id = part.Substring(0, equals).Trim(), ink = part.Substring(equals + 1).Trim();
                if (InScope(id) && Choices.Contains(ink)) inks[id] = ink;
            }
            return inks;
        }
        public static string Format(IDictionary<string, string> inks)
        {
            return String.Join(";", WidgetLayout.Ids.Where(id => inks != null && inks.ContainsKey(id) && InScope(id) && Choices.Contains(inks[id]) && inks[id] != Default(id)).Select(id => id + "=" + inks[id]));
        }
        public static string Clean(string text) { return Format(Parse(text)); }

        // The ink a widget chose (its default unless changed in Edit layout), before Widget colours applies.
        public static string Chosen(string id, string inks)
        {
            string ink; return Parse(inks).TryGetValue(id, out ink) ? ink : Default(id);
        }
        // Changes one widget's ink in an ink list; the default removes the entry.
        public static string With(string inks, string id, string ink)
        {
            var all = Parse(inks); all[id] = ink;
            return Format(all);
        }
        // The colour a widget wears: its ink, or the system accent everywhere with Widget colours → Accent only.
        public static string Resolve(string id, string inks, string mode, string accent)
        {
            return mode == AccentOnly ? (Known(accent) ? accent : "Lime") : Chosen(id, inks);
        }
        public static string Mode(string mode) { return mode == AccentOnly ? AccentOnly : EachOwn; }

        // What a status sticker shows (docs/design/sticker-sheet.md, state language): words that name a state get its
        // look; anything else is an ordinary fact, or a warning on a card whose caption only speaks up for problems.
        public enum StickerState { Fact, Warning, Unavailable, Offline, Loading }
        public static StickerState StateOf(string text, bool warns)
        {
            string words = (text ?? "").Trim().ToUpperInvariant();
            if (words == "UNAVAILABLE" || words == "NOT SET UP") return StickerState.Unavailable;
            if (words == "OFFLINE" || words == "STALE" || words == "PC OFFLINE") return StickerState.Offline;
            if (words == "…" || words == "LOADING" || words == "LOADING…") return StickerState.Loading;
            return warns ? StickerState.Warning : StickerState.Fact;
        }
    }
}
