package com.alialyami.widgetscreen.core;

// Clipboard history wording, the same as ClipboardHistory.cs on Windows (docs/design/clipboard-history.md).
public final class ClipText {
    // "just now", "4 min ago", "2 h ago", "yesterday".
    public static String ago(double atMs, long nowMs) {
        long span = nowMs - (long) atMs;
        if (span < 60000) return "just now";
        if (span < 3600000) return (span / 60000) + " min ago";
        if (span < 86400000) return (span / 3600000) + " h ago";
        long days = span / 86400000;
        return days == 1 ? "yesterday" : days + " days ago";
    }

    public static String length(int characters) { return characters + (characters == 1 ? " character" : " characters"); }

    public static String status(PcState.Clipboard c) { return c.paused ? "PAUSED" : c.items.isEmpty() ? "" : String.valueOf(c.items.size()); }

    public static String empty(PcState.Clipboard c) {
        return c.paused ? "Paused on the PC. New copies aren't kept until you resume." : "Copy some text on the PC and it shows up here. Tap it to paste it there.";
    }
}
