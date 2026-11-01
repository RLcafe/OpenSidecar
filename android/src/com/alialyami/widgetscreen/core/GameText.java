package com.alialyami.widgetscreen.core;

// The Games card's wording and store colours, the same as GameLibrary.cs on Windows (docs/design/games.md).
public final class GameText {
    // Steam Glacier, Epic Lavender, Xbox Lime.
    public static int storeColor(String store) { return "Steam".equals(store) ? 0xFF8ED8F8 : "Epic".equals(store) ? 0xFFC5AEFF : 0xFFBAF28B; }

    // "42 h played", "35 m played", "Not played yet", or nothing when the store doesn't say.
    public static String playtime(Integer minutes) {
        if (minutes == null) return "";
        if (minutes <= 0) return "Not played yet";
        return minutes >= 60 ? String.format(java.util.Locale.US, "%,d h played", minutes / 60) : minutes + " m played";
    }

    // "Steam · 42 h played · 2 days ago".
    public static String detail(PcState.Game game, long now) {
        StringBuilder out = new StringBuilder(game.store);
        String played = playtime(game.playtime);
        if (!played.isEmpty()) out.append("  ·  ").append(played);
        if (game.lastPlayed > 0) out.append("  ·  ").append(ClipText.ago(game.lastPlayed, now));
        return out.toString();
    }
}
