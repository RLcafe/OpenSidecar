package com.alialyami.widgetscreen.tests;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.Spotify;
import com.alialyami.widgetscreen.core.StatsLayout;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.Weather;
import com.alialyami.widgetscreen.core.WidgetLayout;
import com.alialyami.widgetscreen.core.WidgetLayout.Box;
import com.alialyami.widgetscreen.core.WidgetLayout.Placement;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpHandler;
import com.sun.net.httpserver.HttpServer;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.time.LocalDateTime;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Map;

// Checks for the phone app's platform-independent core, run on the desktop JVM by
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

    // A stand-in for the PC: answers like the Windows app's phone link and remembers what it was asked.
    private static final class FakePc implements HttpHandler {
        final List<String> requests = new ArrayList<String>();
        final List<String> bodies = new ArrayList<String>();
        final List<String> tokens = new ArrayList<String>();
        HttpServer server;
        int port() { return server.getAddress().getPort(); }
        FakePc() throws Exception {
            server = HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(), 0), 0);
            server.createContext("/", this);
            server.start();
        }
        @Override public void handle(HttpExchange exchange) {
            try {
                InputStream in = exchange.getRequestBody();
                ByteArrayOutputStream body = new ByteArrayOutputStream();
                byte[] buffer = new byte[4096]; int count;
                while ((count = in.read(buffer)) != -1) body.write(buffer, 0, count);
                String path = exchange.getRequestURI().toString(), text = new String(body.toByteArray(), StandardCharsets.UTF_8);
                String auth = exchange.getRequestHeaders().getFirst("Authorization");
                requests.add(exchange.getRequestMethod() + " " + path); bodies.add(text); tokens.add(auth == null ? "" : auth);
                int status = 200; String reply;
                if (path.equals("/hello")) reply = "{\"app\":\"Widget Screen\",\"name\":\"DESKTOP-1\",\"paired\":false}";
                else if (path.equals("/pair")) {
                    if (text.contains("\"123456\"")) reply = "{\"token\":\"tok\",\"name\":\"DESKTOP-1\"}";
                    else { status = 403; reply = "{\"error\":\"That code doesn't match. Check the code on the PC.\"}"; }
                } else if (!"Bearer tok".equals(auth)) { status = 401; reply = "{\"error\":\"This phone isn't paired with the PC anymore.\"}"; }
                else if (path.startsWith("/state")) reply = "{\"name\":\"DESKTOP-1\",\"quiet\":true,\"use24\":false,\"focus\":{\"remainingMs\":60000,\"durationMs\":1500000,\"running\":true,\"progress\":0.96}}";
                else if (path.equals("/spotify")) { status = 409; reply = "{\"error\":\"No active Spotify device.\",\"noDevice\":true}"; }
                else reply = "{\"ok\":true,\"message\":\"Played Airhorn.\"}";
                byte[] data = reply.getBytes(StandardCharsets.UTF_8);
                exchange.getResponseHeaders().add("Content-Type", "application/json");
                exchange.sendResponseHeaders(status, data.length);
                OutputStream out = exchange.getResponseBody(); out.write(data); out.close();
            } catch (Exception e) { exchange.close(); }
        }
    }

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
            String text = Json.write(Json.map("action", "deck.run", "index", 3, "list", new Object[] { "a\"b", 1.5, false, null }));
            ok(text.equals("{\"action\":\"deck.run\",\"index\":3,\"list\":[\"a\\\"b\",1.5,false,null]}"), text);
            ok(Json.write(Json.parse(text)).equals(text), "Round trip changed the text.");
        } });

        check("Default settings match the Windows look", new Action() { public void run() {
            AppSettings s = new AppSettings();
            ok(s.accent.equals("Lime") && s.theme.equals("Midnight") && s.cornerRadius == 20 && s.textScale == 1 && s.snapWidgets, "appearance");
            ok(s.widgetLayout == null && s.layoutMode.equals("Auto") && s.keepScreenOn && s.hideSystemBars, "layout or display");
        } });
        check("Settings save atomically and load back", new Action() { public void run() throws Exception {
            File file = new File(folder, "settings.xml");
            AppSettings s = new AppSettings();
            s.theme = "Warm"; s.accent = "Amber"; s.textScale = 1.1; s.layoutMode = "List"; s.keepScreenOn = false;
            s.widgetLayout = WidgetLayout.defaults(); WidgetLayout.find(s.widgetLayout, "Soundboard").visible = true; s.widgetLayout[0].layer = 7;
            AppSettings.save(file, s);
            ok(!new File(folder, "settings.xml.tmp").exists(), "Temporary file left behind.");
            AppSettings loaded = AppSettings.load(file, null);
            ok(loaded.theme.equals("Warm") && loaded.accent.equals("Amber") && Math.abs(loaded.textScale - 1.1) < 1e-9 && loaded.layoutMode.equals("List") && !loaded.keepScreenOn, "settings");
            ok(WidgetLayout.find(loaded.widgetLayout, "Soundboard").visible && WidgetLayout.find(loaded.widgetLayout, "Focus").layer == 7, "layout");
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
            s.textScale = 3; s.cornerRadius = -4; s.accent = "Pink"; s.theme = "Neon"; s.layoutMode = "Grid";
            s.normalize();
            ok(s.textScale == 1.15 && s.cornerRadius == 0 && s.accent.equals("Lime") && s.theme.equals("Midnight") && s.layoutMode.equals("Auto"), "Not normalized.");
        } });

        check("Default layout is the Windows default arrangement", new Action() { public void run() {
            Placement[] d = WidgetLayout.defaults();
            ok(WidgetLayout.find(d, "Focus").bounds().equals(new Box(0, 0, 320, 576)) && WidgetLayout.find(d, "Spotify").bounds().equals(new Box(1296, 0, 560, 576)), "geometry");
            ok(!WidgetLayout.find(d, "System").visible && !WidgetLayout.find(d, "Weather").visible && !WidgetLayout.find(d, "Soundboard").visible && WidgetLayout.find(d, "Mixer").visible, "visibility");
            ok(WidgetLayout.find(d, "Soundboard").bounds().equals(new Box(336, 326, 472, 250)) && d.length == WidgetLayout.IDS.length, "soundboard");
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

        check("Clock, focus and readings use the Windows formats", new Action() { public void run() {
            LocalDateTime t = LocalDateTime.of(2026, 10, 8, 14, 5);
            ok(Format.clockTime(t, false).equals("2:05") && Format.clockTime(t, true).equals("14:05") && Format.clockPeriod(t).equals("PM"), "time");
            ok(Format.clockDay(t).equals("THURSDAY") && Format.clockDate(t).equals("October 8") && Format.endsAt(t, false).equals("2:05 PM"), "date");
            ok(Format.focus(25 * 60000L).equals("25:00") && Format.focus(25 * 60000L - 1500).equals("24:59") && Format.focusStatus(0, false, 1, "x").equals("Done. Take a break."), "focus");
            ok(Format.focusButton(1000, false, 0.5).equals("Resume") && Format.focusButton(1000, true, 0.5).equals("Pause") && Format.focusButton(1500000, false, 0).equals("Start focus"), "focus button");
            ok(Format.mediaTime(185000).equals("3:05") && Format.rate(2048).equals("2 KB/s") && Format.rate(3 * 1048576).equals("3.0 MB/s"), "media and rate");
            ok(Format.uptime((2 * 1440 + 3 * 60 + 4) * 60000L).equals("2d 3h 4m"), "uptime");
        } });

        check("Weather parses Open-Meteo responses and describes codes", new Action() { public void run() {
            Weather.Snapshot w = Weather.parse("{\"current\":{\"temperature_2m\":31.6,\"apparent_temperature\":33.2,\"weather_code\":2,\"wind_speed_10m\":12.4}}", 5);
            ok(w.temperature == 31.6 && w.code == 2 && Weather.describe(w.code).equals("Partly cloudy") && Weather.details(w).equals("Feels 33°C  ·  12 km/h"), "snapshot");
            throwsOn(new Action() { public void run() { Weather.parse("{\"current\":{\"temperature_2m\":1}}", 0); } }, "Incomplete weather accepted.");
            ok(Weather.describe(95).equals("Thunderstorms") && Weather.describe(50).equals("Conditions unavailable") && Weather.describe(45).equals("Foggy"), "codes");
            ok(Weather.forecastUrl(24.7, 46.7).contains("latitude=24.7&longitude=46.7"), "forecast address");
        } });

        check("Spotify links, covers and list wording match the Windows app", new Action() { public void run() {
            ok(Spotify.webUrl("spotify:track:../../../../dangerous") == null && Spotify.webUrl(TRACK).equals("https://open.spotify.com/track/1234567890123456789012"), "links");
            ok(Spotify.isImageUrl("https://i.scdn.co/image/ab") && !Spotify.isImageUrl("http://i.scdn.co/image/ab") && !Spotify.isImageUrl("https://evil.example/x.scdn.co"), "images");
            Spotify.Item album = new Spotify.Item(); album.uri = "spotify:album:1234567890123456789012";
            ok(Spotify.listStatus(0, "Albums", null, true, true).startsWith("No recent albums yet") && Spotify.listStatus(3, "Albums", album, true, true).equals("Tap a song to play it. The rest of the album follows."), "statuses");
        } });
        check("Spotify items from the PC parse, and unsafe ones are dropped", new Action() { public void run() {
            Spotify.Page page = PcState.page(Json.obj(Json.parse("{\"items\":[{\"uri\":\"" + TRACK + "\",\"name\":\"Song\",\"subtitle\":\"A, B\",\"imageUrl\":\"https://i.scdn.co/x\",\"position\":21},{\"uri\":\"spotify:local:a:b:c:1\"}],\"hasMore\":true}")));
            ok(page.items.size() == 1 && page.items.get(0).position == 21 && page.items.get(0).subtitle.equals("A, B") && page.hasMore, "page");
            Spotify.Playback playback = PcState.playback(Json.obj(Json.parse("{\"item\":{\"uri\":\"" + TRACK + "\",\"name\":\"Song\",\"position\":-1},\"playing\":true,\"positionMs\":5000,\"durationMs\":200000}")));
            ok(playback.playing && playback.item.isTrack() && playback.positionMs == 5000, "playback");
        } });

        check("PC addresses and discovery replies are read safely", new Action() { public void run() {
            ok(Arrays.equals(PcLink.parseAddress(" 192.168.1.20 "), new String[] { "192.168.1.20", "43830" }) && Arrays.equals(PcLink.parseAddress("desktop-1.local:5000"), new String[] { "desktop-1.local", "5000" }), "addresses");
            ok(PcLink.parseAddress("http://x/") == null && PcLink.parseAddress("1.2.3.4:99999") == null && PcLink.parseAddress("") == null && PcLink.parseAddress("a b") == null, "bad addresses");
            PcLink.Found pc = PcLink.parseDiscovery("{\"app\":\"Widget Screen\",\"name\":\"DESKTOP-1\",\"port\":43830}", "192.168.1.20");
            ok(pc != null && pc.name.equals("DESKTOP-1") && pc.port == 43830 && pc.host.equals("192.168.1.20"), "discovery");
            ok(PcLink.parseDiscovery("{\"app\":\"Other\",\"port\":1}", "x") == null && PcLink.parseDiscovery("garbage", "x") == null, "foreign replies");
        } });
        check("The phone pairs, reads state and sends commands like the PC expects", new Action() { public void run() throws Exception {
            FakePc fake = new FakePc();
            try {
                PcLink anonymous = new PcLink("127.0.0.1", fake.port(), null);
                ok("DESKTOP-1".equals(Json.field(anonymous.hello(), "name")), "hello");
                try { anonymous.pair("000000", "Pixel"); throw new AssertionError("Wrong code paired."); }
                catch (PcLink.Failure f) { ok(f.code == 403 && f.getMessage().contains("doesn't match"), "wrong code message"); }
                String token = anonymous.pair("123 456", "Pixel");
                ok(token.equals("tok") && fake.bodies.get(fake.bodies.size() - 1).contains("\"code\":\"123456\""), "pair");
                try { anonymous.state(Arrays.asList("focus")); throw new AssertionError("Unpaired state."); }
                catch (PcLink.Failure f) { ok(f.unpaired(), "unpaired"); }
                PcLink link = new PcLink("127.0.0.1", fake.port(), token);
                PcState state = link.state(Arrays.asList("focus", "deck"));
                ok(fake.requests.get(fake.requests.size() - 1).equals("GET /state?parts=focus,deck") && fake.tokens.get(fake.tokens.size() - 1).equals("Bearer tok"), "state request");
                ok(state.quiet && state.focus.running && state.focus.durationMs == 1500000 && state.media == null, "state");
                ok(link.command(Json.map("action", "discord.play", "id", "1")).equals("Played Airhorn.") && fake.bodies.get(fake.bodies.size() - 1).equals("{\"action\":\"discord.play\",\"id\":\"1\"}"), "command");
                try { link.spotify(Json.map("op", "playTrack", "uri", TRACK)); throw new AssertionError("No device accepted."); }
                catch (PcLink.Failure f) { ok(f.noDevice && f.code == 409, "no device"); }
            } finally { fake.server.stop(0); }
            try { new PcLink("127.0.0.1", 1, "tok").state(Arrays.asList("focus")); throw new AssertionError("Offline PC answered."); }
            catch (PcLink.Failure f) { ok(f.offline() && f.getMessage().startsWith("Can't reach the PC"), "offline"); }
        } });
        check("The phone reads a real answer from the Windows app", new Action() { public void run() throws Exception {
            String path = System.getProperty("widgetscreen.sample");
            File sample = path == null ? null : new File(path);
            if (sample == null || !sample.exists()) throw new AssertionError("No sample. Run scripts\\build.ps1 -Test first; it saves artifacts\\tests\\remote-state-sample.json.");
            PcState state = PcState.parse(Json.obj(Json.parse(new String(Files.readAllBytes(sample.toPath()), StandardCharsets.UTF_8))));
            ok(!state.name.isEmpty() && state.focus != null && state.focus.durationMs > 0, "focus");
            ok(state.deck != null && !state.deck.isEmpty() && state.deck.get(0).label != null && !state.deck.get(0).glyph.isEmpty(), "deck");
            ok(state.media != null && state.audio != null && state.system != null && state.weather != null && state.spotify != null && state.discord != null, "every part");
            if (state.audio.available) ok(!state.audio.device.isEmpty() && state.audio.volume >= 0 && state.audio.volume <= 1, "audio");
            if (!state.system.error) ok(state.system.uptimeMs > 0, "system");
        } });
        check("Quick Launch glyphs from the PC map to drawn icons", new Action() { public void run() {
            ok(PcState.glyph("E8D6").equals("music") && PcState.glyph("e774").equals("globe") && PcState.glyph("E720").equals("microphone") && PcState.glyph("FFFF").equals("app") && PcState.glyph(null).equals("app"), "glyphs");
        } });
        check("System stats pick columns like the Windows layout", new Action() { public void run() {
            ok(StatsLayout.bestColumns(400, 262, 336, 46, 5) == 1, "narrow card");
            ok(StatsLayout.bestColumns(1100, 100, 336, 46, 5) == 3 && StatsLayout.bestColumns(1100, 200, 336, 46, 5) == 2, "wide card");
            ok(StatsLayout.rowWidth(1100, 200, 324, 12, 46, 5, 3) >= 324, "row width");
        } });
        check("Palettes and accents match the Windows resources", new Action() { public void run() {
            Theme midnight = Theme.of("Midnight", "Lime"), slate = Theme.of("Slate", "Glacier");
            ok(midnight.page == 0xFF0C0E13 && midnight.accent == 0xFFBAF28B && slate.surface == 0xFF1B2938 && slate.accent == 0xFF8ED8F8 && Theme.of("Warm", "Amber").text == 0xFFFAF3E9, "colors");
        } });

        System.out.println(passes + " passed, " + failures + " failed.");
        System.exit(failures == 0 ? 0 : 1);
    }
}
