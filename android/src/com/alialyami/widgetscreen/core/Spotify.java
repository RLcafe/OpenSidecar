package com.alialyami.widgetscreen.core;

import java.io.UnsupportedEncodingException;
import java.net.URLEncoder;
import java.util.Arrays;
import java.util.List;

// Spotify items as the phone shows them. The PC's Spotify connection does the Web API calls (the phone asks
// through the PC link), so only links, cover checks and the list wording live here, as on Windows.
public final class Spotify {
    public static final class Item {
        public String id, uri, name, subtitle, imageUrl, albumUrl;
        // "11 songs · 2024", "Ali · 24 songs" or a song's year, from the PC (Sticker Sheet phase 2); "" without one.
        public String caption = "";
        // Zero-based place in its album or playlist, counting items this app cannot show; -1 outside a collection.
        public int position = -1;
        public String webUrl() { return Spotify.webUrl(uri); }
        public boolean isTrack() { return uri != null && uri.startsWith("spotify:track:"); }
        public boolean isEpisode() { return uri != null && uri.startsWith("spotify:episode:"); }
    }

    public static final class Page {
        public final List<Item> items;
        public final boolean hasMore;
        public Page(List<Item> items, boolean hasMore) { this.items = items; this.hasMore = hasMore; }
    }

    public static final class Playback {
        public Item item;
        public boolean playing, canSeek;
        public double positionMs, durationMs;
    }

    public static String encode(String value) {
        try { return URLEncoder.encode(value, "UTF-8").replace("+", "%20"); }
        catch (UnsupportedEncodingException e) { throw new IllegalStateException(e); }
    }

    public static String webUrl(String value) {
        if (value == null) return null;
        String[] parts = value.split(":");
        if (parts.length != 3 || !parts[0].equals("spotify") || !Arrays.asList("track", "album", "playlist", "episode").contains(parts[1]) || parts[2].length() != 22) return null;
        for (char c : parts[2].toCharArray()) if (!Character.isLetterOrDigit(c) || c > 'z') return null;
        return "https://open.spotify.com/" + parts[1] + "/" + parts[2];
    }

    public static boolean isImageUrl(String value) {
        try {
            java.net.URI uri = new java.net.URI(value);
            String host = uri.getHost() == null ? "" : uri.getHost().toLowerCase(java.util.Locale.ROOT);
            return "https".equals(uri.getScheme()) && (host.endsWith(".scdn.co") || host.endsWith(".spotifycdn.com"));
        } catch (Exception e) { return false; }
    }

    // Statuses shown under the list, the same words as the Windows widget.
    public static String listStatus(int count, String kind, Item collection, boolean canReadRecent, boolean canControl) {
        if (count == 0) return kind.equals("Albums") && collection == null && canReadRecent ? "No recent albums yet. Play an album in Spotify and it shows up here."
            : kind.equals("Search") && collection == null ? "No matches. Try a different spelling or fewer words." : "Nothing here yet. Save music in Spotify to see it here.";
        if (collection == null && kind.equals("Albums") && !canReadRecent) return "Showing saved albums. Reconnect on the PC to see recent ones.";
        if (collection == null && !kind.equals("Search")) return "Tap " + (kind.equals("Albums") ? "an album" : "a playlist") + " to see its songs.";
        if (collection != null && canControl) return "Tap a song to play it. The rest of the " + (collection.uri.startsWith("spotify:album:") ? "album" : "playlist") + " follows.";
        return canControl ? "Tap a song to play it." : "Tap a song to open it in Spotify.";
    }
}
