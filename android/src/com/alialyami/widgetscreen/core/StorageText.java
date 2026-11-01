package com.alialyami.widgetscreen.core;

import java.util.List;
import java.util.Locale;

// The Storage widget's wording and the low-space rule, the same as StorageService.cs on Windows (docs/design/storage.md).
public final class StorageText {
    public static double freePercent(PcState.Drive drive) { return drive.total <= 0 ? 0 : 100 * drive.free / drive.total; }
    public static double usedPercent(PcState.Drive drive) { return drive.total <= 0 ? 0 : 100 - freePercent(drive); }
    public static boolean low(PcState.Drive drive, int percent) { return drive.total > 0 && freePercent(drive) < percent; }

    // Sizes the way File Explorer shows them (1024-based): "820 MB", "45.2 GB", "212 GB", "1.82 TB".
    public static String bytes(double bytes) {
        if (Double.isNaN(bytes) || bytes < 0) bytes = 0;
        double kb = 1024, mb = kb * 1024, gb = mb * 1024, tb = gb * 1024;
        if (bytes >= tb) { double v = bytes / tb; return String.format(Locale.US, v >= 100 ? "%.0f TB" : v >= 10 ? "%.1f TB" : "%.2f TB", v); }
        if (bytes >= gb) { double v = bytes / gb; return String.format(Locale.US, v >= 100 ? "%.0f GB" : "%.1f GB", v); }
        if (bytes >= mb) return String.format(Locale.US, "%.0f MB", bytes / mb);
        return String.format(Locale.US, "%.0f KB", bytes / kb);
    }

    public static String title(PcState.Drive drive) { return drive.label == null || drive.label.trim().isEmpty() ? drive.name : drive.name + "  " + drive.label; }

    // The heading's flag: nothing, "C: LOW" or "2 DRIVES LOW".
    public static String lowFlag(List<PcState.Drive> drives, int percent) {
        PcState.Drive first = null; int count = 0;
        for (PcState.Drive d : drives) if (low(d, percent)) { if (first == null) first = d; count++; }
        return count == 0 ? "" : count == 1 ? first.name + " LOW" : count + " DRIVES LOW";
    }

    // "+2 more drives  ·  1 low": the drives the card has no room for.
    public static String more(List<PcState.Drive> drives, int shown, int percent) {
        int hidden = drives.size() - shown, low = 0;
        for (int i = shown; i < drives.size(); i++) if (low(drives.get(i), percent)) low++;
        return "+" + hidden + (hidden == 1 ? " more drive" : " more drives") + (low > 0 ? "  ·  " + low + " low" : "");
    }
}
