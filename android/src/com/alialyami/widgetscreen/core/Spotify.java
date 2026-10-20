package com.alialyami.widgetscreen.core;

import java.io.UnsupportedEncodingException;
import java.net.URLDecoder;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.security.SecureRandom;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Base64;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;

// The platform-independent half of the Spotify integration, ported from the Windows SpotifyService:
// PKCE values, the authorize address, request bodies and response parsing. The network half is in
// integrations/SpotifyService.
public final class Spotify {
    // Android can't rely on a loopback listener while the browser is in front (the app may be frozen),
    // so it uses an app link. Add this address to the same Spotify app next to the Windows one.
    public static final String REDIRECT_URI = "widgetscreen://spotify-callback";
    public static final String SCOPES = "user-read-currently-playing user-read-recently-played user-library-read user-library-modify playlist-read-private playlist-read-collaborative user-modify-playback-state";

    public static final class Item {
        public String id, uri, name, subtitle, imageUrl, albumUrl;
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

    public static final class Session {
        public String clientId = "", accessToken, refreshToken, scope;
        public long expiresMs;
        public boolean connected() { return refreshToken != null && !refreshToken.isEmpty(); }
        public boolean hasScope(String name) { return connected() && Arrays.asList((scope == null ? "" : scope).split(" ")).contains(name); }
        public String toJson() { return Json.write(Json.map("ClientId", clientId, "AccessToken", accessToken, "RefreshToken", refreshToken, "Scope", scope, "ExpiresMs", (double) expiresMs)); }
        public static Session fromJson(String json) {
            Map<String, Object> root = Json.obj(Json.parse(json));
            Session session = new Session();
            session.clientId = Json.field(root, "ClientId") == null ? "" : Json.field(root, "ClientId");
            session.accessToken = Json.field(root, "AccessToken"); session.refreshToken = Json.field(root, "RefreshToken");
            session.scope = Json.field(root, "Scope"); session.expiresMs = (long) Json.number(root, "ExpiresMs");
            return session;
        }
        public static Session cleared(String clientId) { Session session = new Session(); session.clientId = clientId == null ? "" : clientId; return session; }
    }

    public static String base64Url(byte[] value) { return Base64.getUrlEncoder().withoutPadding().encodeToString(value); }

    public static String randomValue(SecureRandom random) { byte[] bytes = new byte[32]; random.nextBytes(bytes); return base64Url(bytes); }

    public static String challenge(String verifier) {
        try { return base64Url(MessageDigest.getInstance("SHA-256").digest(verifier.getBytes(StandardCharsets.US_ASCII))); }
        catch (NoSuchAlgorithmException e) { throw new IllegalStateException(e); }
    }

    public static boolean validClientId(String value) { return value != null && value.matches("[0-9A-Fa-f]{32}"); }

    public static String encode(String value) {
        try { return URLEncoder.encode(value, "UTF-8").replace("+", "%20"); }
        catch (UnsupportedEncodingException e) { throw new IllegalStateException(e); }
    }

    public static String authorizeUrl(String clientId, String state, String verifier) {
        return "https://accounts.spotify.com/authorize?response_type=code&show_dialog=true&client_id=" + clientId + "&redirect_uri=" + encode(REDIRECT_URI)
            + "&scope=" + encode(SCOPES) + "&state=" + state + "&code_challenge_method=S256&code_challenge=" + challenge(verifier);
    }

    public static String form(Map<String, String> values) {
        StringBuilder out = new StringBuilder();
        for (Map.Entry<String, String> entry : values.entrySet()) {
            if (out.length() > 0) out.append('&');
            out.append(encode(entry.getKey())).append('=').append(encode(entry.getValue() == null ? "" : entry.getValue()));
        }
        return out.toString();
    }

    // Reads widgetscreen://spotify-callback?code=…&state=…. Returns { code, error } when the state matches
    // and one of them is present, otherwise null (an unrelated or forged callback).
    public static String[] readCallback(String uri, String expectedState) {
        if (uri == null || expectedState == null || expectedState.isEmpty() || !uri.startsWith(REDIRECT_URI + "?")) return null;
        String code = null, error = null, state = null;
        for (String pair : uri.substring(REDIRECT_URI.length() + 1).split("&")) {
            int equals = pair.indexOf('=');
            if (equals <= 0) continue;
            String key = pair.substring(0, equals), value;
            try { value = URLDecoder.decode(pair.substring(equals + 1), "UTF-8"); }
            catch (Exception e) { return null; }
            if (key.equals("code")) code = value; else if (key.equals("error")) error = value; else if (key.equals("state")) state = value;
        }
        if (!expectedState.equals(state)) return null;
        boolean hasCode = code != null && !code.trim().isEmpty(), hasError = error != null && !error.trim().isEmpty();
        return hasCode || hasError ? new String[] { hasCode ? code : null, hasError ? error : null } : null;
    }

    public static String webUrl(String value) {
        if (value == null) return null;
        String[] parts = value.split(":");
        if (parts.length != 3 || !parts[0].equals("spotify") || !Arrays.asList("track", "album", "playlist", "episode").contains(parts[1]) || parts[2].length() != 22) return null;
        for (char c : parts[2].toCharArray()) if (!Character.isLetterOrDigit(c) || c > 'z') return null;
        return "https://open.spotify.com/" + parts[1] + "/" + parts[2];
    }

    public static int seekPosition(double position, double duration) {
        if (Double.isNaN(position) || Double.isInfinite(position) || Double.isNaN(duration) || Double.isInfinite(duration) || duration <= 0) throw new IllegalStateException("This song has no seekable timeline.");
        return (int) Math.max(0, Math.min(Math.min(Integer.MAX_VALUE, duration - 1), position));
    }

    public static String playInCollectionBody(String collectionUri, int position) {
        if (webUrl(collectionUri) == null || !(collectionUri.startsWith("spotify:album:") || collectionUri.startsWith("spotify:playlist:")) || position < 0)
            throw new IllegalArgumentException("Choose a song from an album or playlist.");
        return Json.write(Json.map("context_uri", collectionUri, "offset", Json.map("position", position)));
    }

    public static String playTrackBody(String trackUri) {
        if (webUrl(trackUri) == null || !trackUri.startsWith("spotify:track:")) throw new IllegalArgumentException("Choose a Spotify song.");
        return Json.write(Json.map("uris", new Object[] { trackUri }));
    }

    public static boolean isImageUrl(String value) {
        try {
            java.net.URI uri = new java.net.URI(value);
            String host = uri.getHost() == null ? "" : uri.getHost().toLowerCase(java.util.Locale.ROOT);
            return "https".equals(uri.getScheme()) && (host.endsWith(".scdn.co") || host.endsWith(".spotifycdn.com"));
        } catch (Exception e) { return false; }
    }

    // Browse paths. Albums are recently played ones when the connection allows it, otherwise saved albums.
    public static String browsePath(String kind, String query, int offset, boolean canReadRecent) {
        if (kind.equals("Albums") && canReadRecent) return "me/player/recently-played?limit=50";
        if (kind.equals("Albums")) return "me/albums?limit=10&offset=" + offset;
        if (kind.equals("Playlists")) return "me/playlists?limit=10&offset=" + offset;
        return "search?type=track&limit=10&offset=" + offset + "&q=" + encode(query == null ? "" : query);
    }

    public static String tracksPath(Item collection, int offset) {
        if (collection == null || webUrl(collection.uri) == null) throw new IllegalArgumentException("Invalid Spotify collection.");
        String[] parts = collection.uri.split(":");
        boolean album = parts[1].equals("album");
        if (!album && !parts[1].equals("playlist")) throw new IllegalArgumentException("Choose an album or playlist.");
        return (album ? "albums/" : "playlists/") + parts[2] + (album ? "/tracks" : "/items") + "?limit=10&offset=" + offset;
    }

    public static Page parseBrowse(String kind, Object value, int offset, boolean canReadRecent) {
        if (kind.equals("Albums") && canReadRecent) return recentAlbums(value, offset);
        Map<String, Object> root = Json.obj(value);
        if (kind.equals("Search")) root = Json.obj(Json.value(root, "tracks"));
        List<Item> items = new ArrayList<Item>();
        for (Object entry : Json.array(root, "items")) {
            Item item = parseItem(kind.equals("Albums") ? Json.value(Json.obj(entry), "album") : entry);
            if (item != null) items.add(item);
        }
        return new Page(items, Json.field(root, "next") != null && !Json.field(root, "next").isEmpty());
    }

    public static Page parseTracks(Item collection, Object value, int offset) {
        boolean album = collection.uri.startsWith("spotify:album:");
        Map<String, Object> root = Json.obj(value);
        List<Object> entries = Json.array(root, "items");
        List<Item> items = new ArrayList<Item>();
        for (int i = 0; i < entries.size(); i++) {
            Map<String, Object> entry = Json.obj(entries.get(i));
            Object track = album ? entry : (Json.has(entry, "item") ? Json.value(entry, "item") : Json.value(entry, "track"));
            Item item = parseItem(track);
            if (item == null) continue;
            item.position = offset + i;
            items.add(item);
        }
        return new Page(items, Json.field(root, "next") != null && !Json.field(root, "next").isEmpty());
    }

    // Distinct albums from the last 50 played songs, most recent first, paged ten at a time.
    public static Page recentAlbums(Object value, int offset) {
        Set<String> seen = new LinkedHashSet<String>();
        List<Item> albums = new ArrayList<Item>();
        for (Object entry : Json.array(Json.obj(value), "items")) {
            Item album = parseItem(Json.value(Json.obj(Json.value(Json.obj(entry), "track")), "album"));
            if (album == null || !album.uri.startsWith("spotify:album:") || !seen.add(album.uri)) continue;
            albums.add(album);
        }
        int start = Math.max(0, offset);
        List<Item> page = start >= albums.size() ? new ArrayList<Item>() : new ArrayList<Item>(albums.subList(start, Math.min(albums.size(), start + 10)));
        return new Page(page, offset + 10 < albums.size());
    }

    public static Playback parsePlayback(Object value) {
        Map<String, Object> root = Json.obj(value), item = Json.obj(Json.value(root, "item"));
        Playback playback = new Playback();
        playback.item = parseItem(item);
        playback.playing = Boolean.TRUE.equals(Json.value(root, "is_playing"));
        Object seeking = Json.value(Json.obj(Json.value(Json.obj(Json.value(root, "actions")), "disallows")), "seeking");
        playback.canSeek = !Boolean.TRUE.equals(seeking) && Json.number(item, "duration_ms") > 0;
        playback.positionMs = Json.number(root, "progress_ms");
        playback.durationMs = Json.number(item, "duration_ms");
        return playback;
    }

    public static Item parseItem(Object value) {
        Map<String, Object> root = Json.obj(value);
        String uri = Json.field(root, "uri");
        if (webUrl(uri) == null) return null;
        Map<String, Object> album = Json.obj(Json.value(root, "album")), show = Json.obj(Json.value(root, "show"));
        List<Object> images = Json.array(root, "images");
        if (images.isEmpty()) images = Json.array(album, "images");
        StringBuilder artists = new StringBuilder();
        for (Object artist : Json.array(root, "artists")) {
            String name = Json.field(Json.obj(artist), "name");
            if (name == null || name.isEmpty()) continue;
            if (artists.length() > 0) artists.append(", ");
            artists.append(name);
        }
        String subtitle = artists.toString();
        if (subtitle.isEmpty()) {
            subtitle = Json.field(show, "name");
            if (subtitle == null) subtitle = Json.field(Json.obj(Json.value(root, "owner")), "display_name");
            if (subtitle == null) subtitle = Json.field(root, "type");
        }
        Item item = new Item();
        item.id = Json.field(root, "id"); item.uri = uri;
        item.name = Json.field(root, "name") == null ? "Untitled" : Json.field(root, "name");
        item.subtitle = subtitle;
        // The smallest image is the last one; list rows only need a thumbnail.
        item.imageUrl = images.isEmpty() ? null : Json.field(Json.obj(images.get(images.size() - 1)), "url");
        item.albumUrl = webUrl(Json.field(album, "uri"));
        return item;
    }

    // Statuses shown under the list, the same words as the Windows widget.
    public static String listStatus(int count, String kind, Item collection, boolean canReadRecent, boolean canControl) {
        if (count == 0) return kind.equals("Albums") && collection == null && canReadRecent ? "No recent albums yet. Play an album in Spotify and it shows up here."
            : kind.equals("Search") && collection == null ? "No matches. Try a different spelling or fewer words." : "Nothing here yet. Save music in Spotify to see it here.";
        if (collection == null && kind.equals("Albums") && !canReadRecent) return "Showing saved albums. Reconnect in Settings to see recent ones.";
        if (collection == null && !kind.equals("Search")) return "Tap " + (kind.equals("Albums") ? "an album" : "a playlist") + " to see its songs.";
        if (collection != null && canControl) return "Tap a song to play it. The rest of the " + (collection.uri.startsWith("spotify:album:") ? "album" : "playlist") + " follows.";
        return canControl ? "Tap a song to play it." : "Tap a song to open it in Spotify.";
    }
}
