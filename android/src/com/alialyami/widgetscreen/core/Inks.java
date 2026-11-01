package com.alialyami.widgetscreen.core;

import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

// The Sticker Sheet inks (decision 017, docs/design/sticker-sheet.md), the same as the Windows Inks class: which ink
// each widget wears by default, the names on its label strip, the inks Edit layout offers, the "Focus=Coral;…" text the
// phone keeps them in, Widget colours (Each its own / Accent only) and the status sticker's state language. The colours
// themselves are Theme.ink(name).
public final class Inks {
    private Inks() { }

    // Amber means a warning, so no widget owns it and Edit layout doesn't offer it.
    public static final String[] CHOICES = { "Lime", "Glacier", "Lavender", "Coral", "Mint", "Rose", "Lemon" };
    public static final String EACH_OWN = "Each", ACCENT_ONLY = "Accent";
    public static final double COMPACT_BELOW = 200;

    public static boolean known(String ink) { return ink != null && Theme.contains(Theme.INKS, ink); }
    public static boolean choice(String ink) { return ink != null && Theme.contains(CHOICES, ink); }

    // Every widget but Claude Code wears a label strip.
    public static boolean inScope(String id) { return id != null && !"Claude".equals(id) && Arrays.asList(WidgetLayout.IDS).contains(id); }

    public static String defaultInk(String id) {
        if (id == null) return "Glacier";
        switch (id) {
            case "Mixer": case "Games": return "Lime";
            case "Claude": return "Coral";
            case "Soundboard": case "Storage": return "Lavender";
            case "Focus": case "Calendar": return "Coral";
            case "Spotify": case "Prayer": return "Mint";
            case "Media": case "Macros": return "Rose";
            case "System": case "Notes": return "Lemon";
            default: return "Glacier";
        }
    }

    public static String stripName(String id) {
        switch (id) {
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
            default: return WidgetLayout.title(id).toUpperCase(Locale.ROOT);
        }
    }

    public static String shortName(String id) {
        switch (id) {
            case "Media": return "PLAYING";
            case "Shortcuts": return "LAUNCH";
            case "Mixer": return "MIXER";
            case "System": return "STATS";
            case "Soundboard": return "SOUNDS";
            case "Clocks": return "CLOCKS";
            case "Clipboard": return "CLIPS";
            case "Home": return "HOME";
            default: return stripName(id);
        }
    }

    public static boolean compact(double cardWidth) { return cardWidth < COMPACT_BELOW; }

    public static Map<String, String> parse(String text) {
        Map<String, String> inks = new LinkedHashMap<String, String>();
        if (text == null) return inks;
        for (String part : text.split(";")) {
            int equals = part.indexOf('=');
            if (equals <= 0) continue;
            String id = part.substring(0, equals).trim(), ink = part.substring(equals + 1).trim();
            if (inScope(id) && choice(ink)) inks.put(id, ink);
        }
        return inks;
    }

    // Only inks that differ from the widget's default are written.
    public static String format(Map<String, String> inks) {
        StringBuilder text = new StringBuilder();
        for (String id : WidgetLayout.IDS) {
            String ink = inks == null ? null : inks.get(id);
            if (!inScope(id) || !choice(ink) || ink.equals(defaultInk(id))) continue;
            if (text.length() > 0) text.append(';');
            text.append(id).append('=').append(ink);
        }
        return text.toString();
    }

    public static String chosen(String id, Map<String, String> inks) {
        String ink = inks == null ? null : inks.get(id);
        return choice(ink) && inScope(id) ? ink : defaultInk(id);
    }

    // The ink a card wears: its own, or the accent everywhere with Accent only.
    public static String resolve(String id, Map<String, String> inks, String mode, String accent) {
        return ACCENT_ONLY.equals(mode) ? (known(accent) ? accent : "Lime") : chosen(id, inks);
    }

    public static String mode(String mode) { return ACCENT_ONLY.equals(mode) ? ACCENT_ONLY : EACH_OWN; }

    // The status sticker's look (the same words as the PC's Inks.StateOf).
    public static final int FACT = 0, WARNING = 1, UNAVAILABLE = 2, OFFLINE = 3, LOADING = 4;
    public static int stateOf(String text, boolean warns) {
        String words = text == null ? "" : text.trim().toUpperCase(Locale.ROOT);
        if (words.equals("UNAVAILABLE") || words.equals("NOT SET UP")) return UNAVAILABLE;
        if (words.equals("OFFLINE") || words.equals("STALE") || words.equals("PC OFFLINE")) return OFFLINE;
        if (words.equals("…") || words.equals("LOADING") || words.equals("LOADING…")) return LOADING;
        return warns ? WARNING : FACT;
    }

    // The strip's text follows the text size, up to 140%, inside its 28-unit pill.
    public static double stripTextSize(double textScale) {
        double scale = Double.isNaN(textScale) ? 1 : Math.max(0.9, Math.min(1.4, textScale));
        return Math.round(13 * scale * 2) / 2.0;
    }
}
