package com.alialyami.widgetscreen.core;

import java.util.Locale;

// The Soundboard's words (Sticker Sheet phase 5, docs/design/sticker-sheet.md), the same as SoundboardWidget.Voice and
// PadNumber on the PC: the status sticker says where you are, and pads are numbered through the whole list.
public final class SoundboardText {
    private SoundboardText() { }

    // How long a tapped pad stays lit, in milliseconds.
    public static final long FLASH_MS = 150;

    // The voice channel's name, NOT IN VOICE, … while connecting, UNAVAILABLE when Discord can't be reached, and NOT
    // SET UP before Discord is connected on the PC.
    public static String voice(boolean connected, boolean linked, boolean failed, String channel) {
        if (!connected) return "NOT SET UP";
        if (failed) return "UNAVAILABLE";
        if (channel != null && !channel.isEmpty()) return channel;
        return linked ? "NOT IN VOICE" : "…";
    }

    public static String padNumber(int index) { return String.format(Locale.ROOT, "%02d", index + 1); }

    // Where a sound comes from, under its name on a Large pad.
    public static String source(boolean isDefault, boolean otherServer) { return isDefault ? "Discord sound" : otherServer ? "From another server" : "This server"; }
}
