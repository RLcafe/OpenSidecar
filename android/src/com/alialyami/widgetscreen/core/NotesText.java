package com.alialyami.widgetscreen.core;

import java.util.List;

// Notes and to-do wording and limits, the same as NotesStore.cs and NotesCard.cs on Windows (docs/design/notes.md).
public final class NotesText {
    public static final int MAX_TEXT = 16000, MAX_ITEM = 200;

    public static int open(List<PcState.Todo> items) { int open = 0; for (PcState.Todo item : items) if (!item.done) open++; return open; }
    // The heading's flag: nothing, "1 TO DO" or "3 TO DO".
    public static String status(List<PcState.Todo> items) { int open = open(items); return open == 0 ? "" : open + " TO DO"; }
    public static String todoTab(List<PcState.Todo> items) { int open = open(items); return open > 0 ? "To-do · " + open : "To-do"; }
    public static boolean anyDone(List<PcState.Todo> items) { for (PcState.Todo item : items) if (item.done) return true; return false; }

    // A to-do is one line: line breaks become spaces, and it's trimmed to 200 characters.
    public static String line(String text) {
        String joined = (text == null ? "" : text).replaceAll("[\r\n\t]+", " ").replaceAll(" +", " ").trim();
        return joined.length() > MAX_ITEM ? joined.substring(0, MAX_ITEM).trim() : joined;
    }
}
