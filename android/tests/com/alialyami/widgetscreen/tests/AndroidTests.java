package com.alialyami.widgetscreen.tests;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.Deck;
import com.alialyami.widgetscreen.core.FocusTimer;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.Spotify;
import com.alialyami.widgetscreen.core.StatsLayout;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.Weather;
import com.alialyami.widgetscreen.core.WidgetLayout;
import com.alialyami.widgetscreen.core.WidgetLayout.Box;
import com.alialyami.widgetscreen.core.WidgetLayout.Placement;

import java.io.File;
import java.io.FileOutputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.time.LocalDateTime;
import java.util.List;
import java.util.Map;

// Checks for the Android app's platform-independent core, run on the desktop JVM by
// scripts\build-android.ps1 -Test. Prints PASS/FAIL lines like the Windows test exe.
public final class AndroidTests {
    private interface Action { void run() throws Exception; }
    private static int failures, passes;

    private static void check(String name, Action action) {
        try { action.run(); passes++; System.out.println("PASS " + name); }
        catch (Throwable e) { failures++; System.out.println("FAIL " + name + ": " + e); }
    }

    private static void ok(boolean condition, String message) { if (!condition) throw new AssertionError(message); }

    private static void throwsOn(Action action, String message) {
        try { action.run(); } catch (Exception e) { return; }
        throw new AssertionError(message);
    }

    private static final String TRACK = "spotify:track:1234567890123456789012";

    public static void main(String[] args) throws Exception {
        final File folder = Files.createTempDirectory("widget-screen-tests").toFile();

        check("JSON reads objects, arrays, numbers, escapes and literals", new Action() { public void run() {
            Map<String, Object> root = Json.obj(Json.parse("{\"a\":[1,2.5,-3e2],\"b\":\"x\\\"y\\u00e9\\n\",\"c\":true,\"d\":null,\"e\":{}}"));
            List<Object> a = Json.array(root, "a");
            ok(a.size() == 3 && ((Double) a.get(1)) == 2.5 && ((Double) a.get(2)) == -300, "numbers");
            ok("x\"yé\n".equals(Json.field(root, "b")), "escapes");
            ok(Boolean.TRUE.equals(Json.value(root, "c")) && root.containsKey("d") && Json.value(root, "d") == null, "literals");
        } });
        check("JSON rejects malformed input", new Action() { public void run() {
            for (final String bad : new String[] { "", "{", "{\"a\":}", "[1,]x", "\"open", "nul", "{\"a\" 1}" }) throwsOn(new Action() { public void run() { Json.parse(bad); } }, "Accepted " + bad);
        } });
        check("JSON writes what it reads", new Action() { public void run() {
            String text = Json.write(Json.map("context_uri", "spotify:album:x", "offset", Json.map("position", 3), "list", new Object[] { "a\"b", 1.5, false, null }));
            ok(text.equals("{\"context_uri\":\"spotify:album:x\",\"offset\":{\"position\":3},\"list\":[\"a\\\"b\",1.5,false,null]}"), text);
            ok(Json.write(Json.parse(text)).equals(text), "Round trip changed the text.");
        } });

        check("Default settings match the Windows defaults that apply", new Action() { public void run() {
            AppSettings s = new AppSettings();
            ok(s.accent.equals("Lime") && s.theme.equals("Midnight") && s.cornerRadius == 20 && s.textScale == 1 && !s.use24HourClock && s.snapWidgets, "appearance");
            ok(s.shortcuts.size() == 6 && s.widgetLayout == null && s.layoutMode.equals("Auto"), "deck or layout");
        } });
        check("Settings save atomically and load back", new Action() { public void run() throws Exception {
            File file = new File(folder, "settings.xml");
            AppSettings s = new AppSettings();
            s.theme = "Warm"; s.accent = "Amber"; s.textScale = 1.1; s.use24HourClock = true; s.weatherLocation = "Riyadh, Saudi Arabia"; s.latitude = 24.7; s.longitude = 46.7;
            s.shortcuts.get(0).label = "Müzik & <stuff>"; s.shortcuts.get(1).confirm = true;
            s.widgetLayout = WidgetLayout.defaults(); s.widgetLayout[4].visible = true; s.widgetLayout[0].layer = 7;
            AppSettings.save(file, s);
            ok(!new File(folder, "settings.xml.tmp").exists(), "Temporary file left behind.");
            AppSettings loaded = AppSettings.load(file, null);
            ok(loaded.theme.equals("Warm") && loaded.accent.equals("Amber") && Math.abs(loaded.textScale - 1.1) < 1e-9 && loaded.use24HourClock, "appearance");
            ok(loaded.weatherLocation.equals("Riyadh, Saudi Arabia") && loaded.latitude == 24.7 && loaded.longitude == 46.7, "weather");
            ok(loaded.shortcuts.get(0).label.equals("Müzik & <stuff>") && loaded.shortcuts.get(1).confirm, "shortcuts");
            ok(WidgetLayout.find(loaded.widgetLayout, "System").visible && WidgetLayout.find(loaded.widgetLayout, "Focus").layer == 7, "layout");
        } });
        check("An intentionally empty deck stays empty", new Action() { public void run() throws Exception {
            File file = new File(folder, "empty.xml");
            AppSettings s = new AppSettings(); s.shortcuts.clear();
            AppSettings.save(file, s);
            ok(AppSettings.load(file, null).shortcuts.isEmpty(), "Defaults came back.");
        } });
        check("Unreadable settings fall back to defaults with a warning and keep the file", new Action() { public void run() throws Exception {
            File file = new File(folder, "broken.xml");
            FileOutputStream out = new FileOutputStream(file); out.write("not xml".getBytes(StandardCharsets.UTF_8)); out.close();
            String[] warning = new String[1];
            AppSettings loaded = AppSettings.load(file, warning);
            ok(warning[0] != null && loaded.theme.equals("Midnight") && file.length() == 7, "No warning, or the file was changed.");
        } });
        check("Settings normalize out-of-range values", new Action() { public void run() {
            AppSettings s = new AppSettings();
            s.textScale = 3; s.cornerRadius = -4; s.accent = "Pink"; s.theme = "Neon"; s.layoutMode = "Grid"; s.latitude = 200; s.weatherLocation = "Nowhere";
            s.normalize();
            ok(s.textScale == 1.15 && s.cornerRadius == 0 && s.accent.equals("Lime") && s.theme.equals("Midnight") && s.layoutMode.equals("Auto") && s.weatherLocation.isEmpty(), "Not normalized.");
        } });

        check("Default layout is the Windows Spotify arrangement", new Action() { public void run() {
            Placement[] d = WidgetLayout.defaults();
            ok(WidgetLayout.find(d, "Focus").bounds().equals(new Box(0, 0, 320, 576)) && WidgetLayout.find(d, "Spotify").bounds().equals(new Box(1296, 0, 560, 576)), "geometry");
            ok(!WidgetLayout.find(d, "System").visible && !WidgetLayout.find(d, "Weather").visible && WidgetLayout.find(d, "Mixer").visible, "visibility");
            ok(WidgetLayout.find(d, "Soundboard") == null && d.length == WidgetLayout.IDS.length, "ids");
        } });
        check("Layouts normalize to the canvas and minimum sizes", new Action() { public void run() {
            Placement[] input = { new Placement("Focus", -50, 900, 10, 10, true), new Placement("Media", Double.NaN, 0, 5000, 5000, false) };
            Placement[] n = WidgetLayout.normalize(input);
            Placement focus = WidgetLayout.find(n, "Focus"), media = WidgetLayout.find(n, "Media");
            ok(n.length == WidgetLayout.IDS.length && focus.x == 0 && focus.width == 296 && focus.height == 300 && focus.y == 576 - 300, "focus " + focus.bounds());
            ok(media.width == 1856 && media.height == 576 && media.x == 0 && !media.visible, "media " + media.bounds());
        } });
        check("Moving and resizing respect edges, minimums and snap", new Action() { public void run() {
            Box start = new Box(100, 100, 400, 320);
            ok(WidgetLayout.transform("Focus", start, "", -500, 5, false).equals(new Box(0, 105, 400, 320)), "move");
            ok(WidgetLayout.transform("Focus", start, "", 3, 5, true).equals(new Box(104, 104, 400, 320)), "snap");
            ok(WidgetLayout.transform("Focus", start, "SE", -1000, -1000, false).equals(new Box(100, 100, 296, 300)), "minimum");
            ok(WidgetLayout.transform("Focus", start, "NW", -1000, -1000, false).equals(new Box(0, 0, 500, 420)), "grow to edge");
            ok(WidgetLayout.transform("Focus", start, "NE", Double.NaN, 0, false) == start, "NaN");
        } });

        check("Focus timer counts down, pauses, resumes and finishes", new Action() { public void run() {
            final long[] now = { 1000 };
            FocusTimer timer = new FocusTimer(new FocusTimer.Clock() { public long millis() { return now[0]; } });
            ok(timer.remainingMs() == 25 * 60000L && Format.focus(timer.remainingMs()).equals("25:00") && Format.focusButton(timer.remainingMs(), false, 0).equals("Start focus"), "idle");
            timer.toggle(); now[0] += 1500;
            ok(Format.focus(timer.remainingMs()).equals("24:59") && timer.running() && Format.focusButton(timer.remainingMs(), true, timer.progress()).equals("Pause"), "running " + Format.focus(timer.remainingMs()));
            timer.toggle(); now[0] += 60000;
            ok(timer.remainingMs() == 25 * 60000L - 1500 && Format.focusStatus(timer.remainingMs(), false, timer.progress(), "x").equals("Paused") && Format.focusButton(timer.remainingMs(), false, timer.progress()).equals("Resume"), "paused");
            timer.toggle(); now[0] += 30 * 60000L;
            ok(timer.finishIfElapsed() && timer.remainingMs() == 0 && !timer.running() && Format.focusStatus(0, false, 1, "x").equals("Done. Take a break."), "finished");
            timer.toggle();
            ok(timer.running() && timer.remainingMs() == 25 * 60000L, "restarts after finishing");
            timer.reset(500);
            ok(timer.durationMinutes() == 180 && !timer.running(), "clamped");
        } });
        check("Focus timer restores a run, but not one from before a restart", new Action() { public void run() {
            final long[] now = { 100000 };
            FocusTimer.Clock clock = new FocusTimer.Clock() { public long millis() { return now[0]; } };
            FocusTimer timer = new FocusTimer(clock);
            timer.reset(50); timer.toggle(); now[0] += 60000;
            FocusTimer restored = new FocusTimer(clock); restored.restore(timer.save());
            ok(restored.running() && restored.durationMinutes() == 50 && restored.remainingMs() == 49 * 60000L, "restore");
            now[0] = 10;
            FocusTimer rebooted = new FocusTimer(clock); rebooted.restore(timer.save());
            ok(!rebooted.running(), "A run from before a restart resumed.");
            new FocusTimer(clock).restore("garbage");
        } });

        check("Clock and readings use the Windows formats", new Action() { public void run() {
            LocalDateTime t = LocalDateTime.of(2026, 10, 8, 14, 5);
            ok(Format.clockTime(t, false).equals("2:05") && Format.clockTime(t, true).equals("14:05") && Format.clockPeriod(t).equals("PM"), "time");
            ok(Format.clockDay(t).equals("THURSDAY") && Format.clockDate(t).equals("October 8") && Format.endsAt(t, false).equals("2:05 PM"), "date");
            ok(Format.mediaTime(185000).equals("3:05") && Format.rate(2048).equals("2 KB/s") && Format.rate(3 * 1048576).equals("3.0 MB/s"), "media and rate");
            ok(Format.uptime((2 * 1440 + 3 * 60 + 4) * 60000L).equals("2d 3h 4m") && Format.gigabytes(1610612736L).equals("1.5"), "uptime and memory");
        } });

        check("Weather parses Open-Meteo responses and describes codes", new Action() { public void run() {
            Weather.Snapshot w = Weather.parse("{\"current\":{\"temperature_2m\":31.6,\"apparent_temperature\":33.2,\"weather_code\":2,\"wind_speed_10m\":12.4}}", 5);
            ok(w.temperature == 31.6 && w.code == 2 && Weather.describe(w.code).equals("Partly cloudy") && Weather.details(w).equals("Feels 33°C  ·  12 km/h"), "snapshot");
            throwsOn(new Action() { public void run() { Weather.parse("{\"current\":{\"temperature_2m\":1}}", 0); } }, "Incomplete weather accepted.");
            ok(Weather.describe(95).equals("Thunderstorms") && Weather.describe(50).equals("Conditions unavailable") && Weather.describe(45).equals("Foggy"), "codes");
            List<Weather.Location> places = Weather.parseLocations("{\"results\":[{\"name\":\"Riyadh\",\"admin1\":\"Riyadh Region\",\"country\":\"Saudi Arabia\",\"latitude\":24.68,\"longitude\":46.72},{\"name\":\"Broken\"}]}");
            ok(places.size() == 1 && places.get(0).name.equals("Riyadh, Riyadh Region, Saudi Arabia"), "places");
            ok(Weather.searchUrl(" New York ").endsWith("name=New%20York"), Weather.searchUrl("New York"));
            throwsOn(new Action() { public void run() { Weather.searchUrl("a"); } }, "One-letter search accepted.");
        } });

        check("Spotify PKCE matches the RFC 7636 example", new Action() { public void run() {
            ok(Spotify.challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk").equals("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"), "challenge");
            ok(Spotify.validClientId("0123456789abcdef0123456789ABCDEF") && !Spotify.validClientId("0123") && !Spotify.validClientId("0123456789abcdef0123456789abcdeg"), "client id");
        } });
        check("Spotify authorize address carries the Android redirect and PKCE", new Action() { public void run() {
            String url = Spotify.authorizeUrl("0123456789abcdef0123456789abcdef", "state1", "verifier1");
            ok(url.contains("redirect_uri=widgetscreen%3A%2F%2Fspotify-callback") && url.contains("code_challenge=" + Spotify.challenge("verifier1")) && url.contains("state=state1") && url.contains("scope=user-read-currently-playing%20"), url);
        } });
        check("Spotify callbacks need the matching state", new Action() { public void run() {
            String[] good = Spotify.readCallback("widgetscreen://spotify-callback?code=abc%2Fd&state=s1", "s1");
            ok(good != null && good[0].equals("abc/d") && good[1] == null, "code");
            String[] declined = Spotify.readCallback("widgetscreen://spotify-callback?error=access_denied&state=s1", "s1");
            ok(declined != null && declined[0] == null && declined[1].equals("access_denied"), "declined");
            ok(Spotify.readCallback("widgetscreen://spotify-callback?code=abc&state=other", "s1") == null && Spotify.readCallback("https://evil.example/?code=a&state=s1", "s1") == null && Spotify.readCallback("widgetscreen://spotify-callback?state=s1", "s1") == null, "forged");
        } });
        check("Spotify links and play requests match the Windows app", new Action() { public void run() {
            ok(Spotify.webUrl("spotify:track:../../../../dangerous") == null && Spotify.webUrl(TRACK).equals("https://open.spotify.com/track/1234567890123456789012"), "links");
            ok(Spotify.playInCollectionBody("spotify:playlist:1234567890123456789012", 11).equals("{\"context_uri\":\"spotify:playlist:1234567890123456789012\",\"offset\":{\"position\":11}}"), "collection body");
            ok(Spotify.playTrackBody(TRACK).equals("{\"uris\":[\"" + TRACK + "\"]}"), "track body");
            throwsOn(new Action() { public void run() { Spotify.playInCollectionBody(TRACK, 0); } }, "A song accepted as a collection.");
            throwsOn(new Action() { public void run() { Spotify.playTrackBody("spotify:album:1234567890123456789012"); } }, "An album accepted as a song.");
            ok(Spotify.isImageUrl("https://i.scdn.co/image/ab") && !Spotify.isImageUrl("http://i.scdn.co/image/ab") && !Spotify.isImageUrl("https://evil.example/x.scdn.co"), "images");
        } });
        check("Spotify items, playback and pages parse like the Windows app", new Action() { public void run() {
            Spotify.Item episode = Spotify.parseItem(Json.parse("{\"uri\":\"spotify:episode:1234567890123456789012\",\"name\":\"Episode\",\"show\":{\"name\":\"Podcast\"}}"));
            ok(episode != null && episode.subtitle.equals("Podcast") && episode.isEpisode(), "episode");
            ok(Spotify.parseItem(Json.parse("{\"uri\":\"spotify:local:a:b:c:1\"}")) == null, "local track");
            Spotify.Item song = Spotify.parseItem(Json.parse("{\"uri\":\"" + TRACK + "\",\"name\":\"Song\",\"artists\":[{\"name\":\"A\"},{\"name\":\"B\"}],\"album\":{\"uri\":\"spotify:album:1234567890123456789012\",\"images\":[{\"url\":\"https://i.scdn.co/big\"},{\"url\":\"https://i.scdn.co/small\"}]}}"));
            ok(song.subtitle.equals("A, B") && song.imageUrl.equals("https://i.scdn.co/small") && song.albumUrl.endsWith("/album/1234567890123456789012"), "song");
            Spotify.Playback playback = Spotify.parsePlayback(Json.parse("{\"is_playing\":true,\"progress_ms\":5000,\"item\":{\"uri\":\"" + TRACK + "\",\"duration_ms\":200000},\"actions\":{\"disallows\":{\"seeking\":true}}}"));
            ok(playback.playing && !playback.canSeek && playback.positionMs == 5000 && playback.durationMs == 200000, "playback");
            Spotify.Item playlist = new Spotify.Item(); playlist.uri = "spotify:playlist:1234567890123456789012";
            Spotify.Page tracks = Spotify.parseTracks(playlist, Json.parse("{\"items\":[{\"item\":{\"uri\":\"spotify:local:x:y:z:1\"}},{\"track\":{\"uri\":\"" + TRACK + "\",\"name\":\"Second\"}}],\"next\":\"more\"}"), 20);
            ok(tracks.items.size() == 1 && tracks.items.get(0).position == 21 && tracks.hasMore, "positions count hidden items");
            ok(Spotify.tracksPath(playlist, 20).equals("playlists/1234567890123456789012/items?limit=10&offset=20"), "tracks path");
            ok(Spotify.browsePath("Albums", null, 0, true).equals("me/player/recently-played?limit=50") && Spotify.browsePath("Search", "a b", 10, true).equals("search?type=track&limit=10&offset=10&q=a%20b"), "browse paths");
        } });
        check("Recent albums are distinct, newest first, ten per page", new Action() { public void run() {
            StringBuilder items = new StringBuilder();
            for (int i = 0; i < 30; i++) {
                if (i > 0) items.append(',');
                String id = String.format("%022d", i % 12);
                items.append("{\"track\":{\"album\":{\"uri\":\"spotify:album:").append(id).append("\",\"name\":\"Album ").append(i % 12).append("\"}}}");
            }
            Object recent = Json.parse("{\"items\":[" + items + "]}");
            Spotify.Page first = Spotify.recentAlbums(recent, 0), second = Spotify.recentAlbums(recent, 10);
            ok(first.items.size() == 10 && first.hasMore && first.items.get(0).name.equals("Album 0") && second.items.size() == 2 && !second.hasMore, "pages");
        } });
        check("Spotify sessions survive a round trip", new Action() { public void run() {
            Spotify.Session s = new Spotify.Session(); s.clientId = "c"; s.accessToken = "a"; s.refreshToken = "r"; s.scope = Spotify.SCOPES; s.expiresMs = 1234567890123L;
            Spotify.Session back = Spotify.Session.fromJson(s.toJson());
            ok(back.connected() && back.expiresMs == 1234567890123L && back.hasScope("user-modify-playback-state") && !Spotify.Session.cleared("c").connected(), "session");
        } });
        check("List statuses use the Windows wording", new Action() { public void run() {
            Spotify.Item album = new Spotify.Item(); album.uri = "spotify:album:1234567890123456789012";
            ok(Spotify.listStatus(0, "Albums", null, true, true).startsWith("No recent albums yet") && Spotify.listStatus(3, "Albums", album, true, true).equals("Tap a song to play it. The rest of the album follows."), "statuses");
        } });

        check("Quick Launch validates Android actions", new Action() { public void run() {
            ok(Deck.validate(new Deck.Shortcut("Spotify", "App", "com.spotify.music", "music")) == null && Deck.validate(new Deck.Shortcut("Files", "App", "role:files", "folder")) == null, "apps");
            ok(Deck.validate(new Deck.Shortcut("Web", "Website", "https://example.com", "globe")) == null && Deck.validate(new Deck.Shortcut("Web", "Website", "javascript:alert(1)", "globe")) != null, "websites");
            ok(Deck.validate(new Deck.Shortcut("Play", "Control", "play", "play")) == null && Deck.validate(new Deck.Shortcut("Mic", "Control", "microphone", "volume")) != null, "controls");
            ok(Deck.validate(new Deck.Shortcut("", "App", "com.x.y", "app")) != null && Deck.validate(new Deck.Shortcut("Files", "File", "C:\\x", "app")) != null && Deck.validate(new Deck.Shortcut("Bad", "App", "not a package", "app")) != null, "rejects");
            for (Deck.Shortcut preset : Deck.presets()) ok(Deck.validate(preset) == null, "Preset invalid: " + preset.label);
            ok(Deck.pageCount(0, 6) == 1 && Deck.pageCount(7, 6) == 2 && Deck.pageCount(6, 3) == 2, "pages");
        } });
        check("System stats pick columns like the Windows layout", new Action() { public void run() {
            ok(StatsLayout.bestColumns(400, 262, 356, 46, 5) == 1, "narrow card");
            ok(StatsLayout.bestColumns(1100, 100, 356, 46, 5) == 3 && StatsLayout.bestColumns(1100, 200, 356, 46, 5) == 2, "wide card");
            ok(StatsLayout.rowWidth(1100, 200, 344, 12, 46, 5, 3) >= 344, "row width");
        } });
        check("Palettes and accents match the Windows resources", new Action() { public void run() {
            Theme midnight = Theme.of("Midnight", "Lime"), slate = Theme.of("Slate", "Glacier");
            ok(midnight.page == 0xFF0C0E13 && midnight.accent == 0xFFBAF28B && slate.surface == 0xFF1B2938 && slate.accent == 0xFF8ED8F8 && Theme.of("Warm", "Amber").text == 0xFFFAF3E9, "colors");
        } });

        System.out.println(passes + " passed, " + failures + " failed.");
        System.exit(failures == 0 ? 0 : 1);
    }
}
