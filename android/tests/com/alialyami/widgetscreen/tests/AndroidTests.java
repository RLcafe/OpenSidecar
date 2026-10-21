package com.alialyami.widgetscreen.tests;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.DashboardFit;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcPairing;
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

    private static boolean overlaps(Placement[] layout) {
        for (int i = 0; i < layout.length; i++)
            for (int j = i + 1; j < layout.length; j++) {
                Placement a = layout[i], b = layout[j];
                if (!a.visible || !b.visible) continue;
                double width = Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x), height = Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y);
                if (width > 1 && height > 1) return true;
            }
        return false;
    }

    private static boolean inside(Placement[] layout, double width, double height) {
        for (Placement p : layout) if (p.x < 0 || p.y < 0 || p.x + p.width > width + 1e-9 || p.y + p.height > height + 1e-9) return false;
        return true;
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
            s.theme = "Warm"; s.accent = "Amber"; s.textScale = 1.1; s.layoutMode = "List"; s.keepScreenOn = false; s.landscapeOnly = true;
            s.widgetLayout = WidgetLayout.defaults(); WidgetLayout.find(s.widgetLayout, "Soundboard").visible = true; s.widgetLayout[0].layer = 7;
            AppSettings.save(file, s);
            ok(!new File(folder, "settings.xml.tmp").exists(), "Temporary file left behind.");
            AppSettings loaded = AppSettings.load(file, null);
            ok(loaded.theme.equals("Warm") && loaded.accent.equals("Amber") && Math.abs(loaded.textScale - 1.1) < 1e-9 && loaded.layoutMode.equals("List") && !loaded.keepScreenOn && loaded.landscapeOnly, "settings");
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
                // The pairing the phone keeps must be usable straight away, not only after a restart.
                PcPairing pairing = PcPairing.pair("127.0.0.1", fake.port(), "123456", "Pixel");
                ok(pairing.paired() && pairing.token.equals("tok") && pairing.name.equals("DESKTOP-1"), "pairing not usable");
                ok(pairing.link().state(Arrays.asList("focus")).focus != null, "a fresh pairing couldn't read the PC");
                PcPairing restored = PcPairing.fromJson(pairing.toJson());
                ok(restored.paired() && restored.token.equals("tok") && restored.port == fake.port() && !PcPairing.none().paired(), "pairing round trip");
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
            // The Windows checks add a page, rename it Play and move it first, so the PC shows Main, the second page.
            ok(state.layout != null && !state.layoutVersion.isEmpty() && state.layout.pages.equals(Arrays.asList("Play", "Main")) && state.layout.page == 1 && state.layout.pageName().equals("Main"), "layout pages");
            ok(state.layout.shown != null && state.layout.shown.length == WidgetLayout.IDS.length && state.layout.canvasWidth == 1856 && state.layout.sources().containsKey("1856x576"), "PC screen layout");
            AppSettings mirrored = new AppSettings();
            mirrored.mirror(state.layout);
            ok(mirrored.page == 1 && mirrored.pages.size() == 2 && mirrored.pcCanvasWidth == 1856, "mirrored pages");
            Placement[] phone = mirrored.layoutFor(1480, 552);
            ok(WidgetLayout.fits(phone, 1480, 552) && !overlaps(phone), "the PC layout on a sideways phone");
        } });
        check("The phone mirrors the PC layout: stretched, listed in reading order, pages kept offline", new Action() { public void run() {
            Map<String, Object> answer = Json.obj(Json.parse("{\"layoutVersion\":\"abc\",\"layout\":{\"page\":1,\"pages\":[\"Main\",\"Stats\"],\"snap\":false,\"editing\":false,"
                + "\"canvas\":{\"width\":1856,\"height\":576},\"shown\":[{\"id\":\"System\",\"x\":0,\"y\":0,\"width\":920,\"height\":576,\"visible\":true,\"layer\":0},"
                + "{\"id\":\"Weather\",\"x\":936,\"y\":0,\"width\":920,\"height\":576,\"visible\":true,\"layer\":2},{\"id\":\"Nope\",\"x\":0,\"y\":0,\"width\":1,\"height\":1,\"visible\":true}],"
                + "\"layouts\":[{\"width\":1000,\"height\":1560,\"widgets\":[{\"id\":\"Weather\",\"x\":0,\"y\":0,\"width\":1000,\"height\":700,\"visible\":true,\"layer\":0}]}]}}"));
            PcState state = PcState.parse(answer);
            ok(state.layoutVersion.equals("abc") && state.layout.page == 1 && !state.layout.snap && state.layout.shown.length == WidgetLayout.IDS.length && !WidgetLayout.find(state.layout.shown, "Mixer").visible && WidgetLayout.find(state.layout.shown, "Weather").layer == 2, "parse");
            AppSettings s = new AppSettings();
            s.mirror(state.layout);
            Placement[] phone = s.layoutFor(1480, 552);
            ok(WidgetLayout.find(phone, "System").visible && WidgetLayout.find(phone, "Weather").visible && !WidgetLayout.find(phone, "Mixer").visible, "the phone shows the PC's widgets");
            ok(WidgetLayout.find(phone, "Weather").x == Math.round(936 * 1480 / 1856.0), "stretched from the PC screen");
            ok(WidgetLayout.find(s.layoutFor(1000, 1560), "Weather").height == 700, "a saved shape is used as is");
            ok(WidgetLayout.readingOrder(s.layoutFor(s.pcCanvasWidth, s.pcCanvasHeight)).equals(Arrays.asList("System", "Weather")), "list order and widgets follow the PC");
            AppSettings back = AppSettings.fromProperties(s.toProperties());
            ok(back.pages.equals(Arrays.asList("Main", "Stats")) && back.page == 1 && !back.snapWidgets && WidgetLayout.find(back.layoutFor(1480, 552), "System").visible, "kept for offline use");
            Placement[] edited = back.layoutFor(1480, 552);
            WidgetLayout.find(edited, "Mixer").visible = true;
            back.store(1480, 552, edited);
            ok(WidgetLayout.find(back.canvasLayouts.get("1000x1560"), "Mixer").visible, "showing a widget shows it on the page's other shapes");
            List<Map<String, Object>> sent = PcState.json(edited);
            ok(sent.size() == WidgetLayout.IDS.length && "Focus".equals(sent.get(0).get("id")), "layout sent to the PC");
        } });
        check("Quick Launch glyphs from the PC map to drawn icons", new Action() { public void run() {
            ok(PcState.glyph("E8D6").equals("music") && PcState.glyph("e774").equals("globe") && PcState.glyph("E720").equals("microphone") && PcState.glyph("FFFF").equals("app") && PcState.glyph(null).equals("app"), "glyphs");
        } });
        check("System stats pick columns like the Windows layout", new Action() { public void run() {
            ok(StatsLayout.bestColumns(400, 262, 336, 46, 5) == 1, "narrow card");
            ok(StatsLayout.bestColumns(1100, 100, 336, 46, 5) == 3 && StatsLayout.bestColumns(1100, 200, 336, 46, 5) == 2, "wide card");
            ok(StatsLayout.rowWidth(1100, 200, 324, 12, 46, 5, 3) >= 324, "row width");
        } });
        check("Sideways screens get the Windows arrangement, sized for phones", new Action() { public void run() {
            DashboardFit phone = DashboardFit.choose(2400, 1080, 2.625, "Auto");
            ok(!phone.list && !phone.stacked && phone.canvasWidth() >= 1856 * 0.8 - 8 && phone.canvasHeight() >= 576 * 0.8 - 8, "phone canvas " + phone.canvasWidth() + " x " + phone.canvasHeight());
            ok(phone.scale / 2.625 > 0.55 && phone.scale > Math.min(2400 / 1920.0, 1080 / 720.0), "phone text not enlarged: " + phone.scale / 2.625);
            ok(phone.canvasWidth() / 1856 >= 0.79 && phone.canvasHeight() / 576 >= 0.79, "canvas shrank past 80%");
            DashboardFit bar = DashboardFit.choose(1920, 720, 1, "Auto");
            ok(!bar.list && bar.scale == 1 && bar.canvasWidth() == 1856 && bar.canvasHeight() == 576, "1920 x 720 isn't the Windows canvas");
            ok(DashboardFit.choose(1080, 2400, 2.625, "Auto").list && !DashboardFit.choose(1080, 2400, 2.625, "Dashboard").list && DashboardFit.choose(2400, 1080, 2.625, "List").list, "modes");
            DashboardFit tablet = DashboardFit.choose(2560, 1600, 2, "Auto");
            ok(!tablet.list && tablet.canvasWidth() >= 1856 * 0.8 - 1 && tablet.scale / 2 <= 0.75 + 1e-9, "tablet");
        } });
        check("Upright tablets get a tall dashboard, upright phones the list", new Action() { public void run() {
            DashboardFit tablet = DashboardFit.choose(1600, 2560, 2, "Auto");
            ok(!tablet.list && tablet.stacked && tablet.canvasHeight() > tablet.canvasWidth(), "upright tablet " + tablet.canvasWidth() + " x " + tablet.canvasHeight());
            ok(Math.abs(tablet.scale / 2 - 0.75) < 1e-9, "upright tablet text size " + tablet.scale / 2);
            ok(DashboardFit.choose(1080, 2400, 2.625, "Auto").list && !DashboardFit.choose(1080, 2400, 2.625, "Dashboard").list, "upright phone");
            DashboardFit forced = DashboardFit.choose(1080, 2400, 2.625, "Dashboard");
            Placement[] layout = WidgetLayout.defaults(forced.canvasWidth(), forced.canvasHeight());
            ok(inside(layout, forced.canvasWidth(), forced.canvasHeight()), "forced dashboard on an upright phone escapes its canvas");
        } });
        check("Screen fit matches the Windows rule and shapes the canvas like the screen", new Action() { public void run() {
            DashboardFit wide = DashboardFit.fit(2560, 720, 1, 1800), hd = DashboardFit.fit(1920, 1080, 1, 1800), uhd = DashboardFit.fit(3840, 2160, 1, 1800);
            ok(wide.scale == 1 && wide.canvasWidth() == 2496 && wide.canvasHeight() == 576, "2560 x 720");
            ok(hd.scale == 1 && hd.canvasWidth() == 1856 && hd.canvasHeight() == 936, "1920 x 1080");
            ok(uhd.scale == 2 && uhd.logicalWidth == 1920 && uhd.logicalHeight == 1080, "4K");
            DashboardFit small = DashboardFit.fit(1280, 800, 1, 1800);
            ok(Math.abs(small.scale - 0.75) < 1e-9 && small.stacked, "1280 x 800");
            ok(WidgetLayout.key(1485.7) == 1480 && WidgetLayout.keyName(1485.7, 764.2).equals("1480x760") && WidgetLayout.isReference(1859, 579), "keys");
        } });
        check("Minimum sizes shrink to about 80% at most on compact canvases", new Action() { public void run() {
            double[] reference = WidgetLayout.minimum("Spotify", 1856, 576), phone = WidgetLayout.minimum("Spotify", 1480, 552), tiny = WidgetLayout.minimum("Spotify", 900, 400);
            ok(reference[0] == 560 && reference[1] == 500 && phone[0] == 447 && phone[1] == 479 && tiny[0] == 437 && tiny[1] == 390, "minimums " + phone[0] + "," + phone[1]);
            ok(WidgetLayout.transform("Spotify", new Box(0, 0, 600, 552), "SE", -1000, -1000, false, 1480, 552).equals(new Box(0, 0, 447, 479)), "resize minimum");
            ok(WidgetLayout.transform("Focus", new Box(0, 0, 300, 300), "", 5000, 5000, false, 2496, 576).equals(new Box(2196, 276, 300, 300)), "move to this canvas's edge");
        } });
        check("Default arrangement fits phones and tablets both ways without overlaps", new Action() { public void run() {
            Placement[] columns = WidgetLayout.arrange(1856, 576), reference = WidgetLayout.defaults();
            for (String id : new String[] { "Focus", "Media", "Shortcuts", "Mixer", "Spotify" })
                ok(WidgetLayout.find(columns, id).bounds().equals(WidgetLayout.find(reference, id).bounds()), "columns template is not the Windows default for " + id);
            int[][] screens = { { 2400, 1080 }, { 1080, 2400 }, { 2560, 1600 }, { 1600, 2560 }, { 1920, 1200 }, { 1200, 1920 }, { 1280, 800 }, { 800, 1280 } };
            double[] densities = { 2.625, 2.625, 2, 2, 1.5, 1.5, 1, 1 };
            for (int i = 0; i < screens.length; i++) {
                DashboardFit fit = DashboardFit.choose(screens[i][0], screens[i][1], densities[i], "Dashboard");
                double w = fit.canvasWidth(), h = fit.canvasHeight();
                Placement[] layout = WidgetLayout.defaults(w, h);
                String name = screens[i][0] + "x" + screens[i][1] + " (" + w + " x " + h + ")";
                ok(layout.length == WidgetLayout.IDS.length && inside(layout, w, h), "escapes on " + name);
                if (screens[i][1] < screens[i][0] || Math.min(screens[i][0], screens[i][1]) / densities[i] >= 600)
                    ok(WidgetLayout.fits(layout, w, h) && !overlaps(layout), "squeezed or overlapping on " + name);
            }
            Placement[] wide = WidgetLayout.arrange(2496, 576);
            for (Placement p : wide) if (p.visible && !p.id.equals("Shortcuts")) ok(p.y == 0, "a wide canvas should get columns");
            ok(!overlaps(WidgetLayout.normalize(WidgetLayout.arrange(1000, 1560, java.util.Arrays.asList(WidgetLayout.IDS)), 1000, 1560)), "every widget on an upright tablet");
            double[] shares = WidgetLayout.distribute(100, new double[] { 1, 1 }, new double[] { 70, 10 });
            ok(shares[0] == 70 && shares[1] == 30, "distribution");
        } });
        check("Old phone settings keep their layout and other shapes are saved apart", new Action() { public void run() {
            java.util.Properties old = new java.util.Properties();
            old.setProperty("Layout.Focus", "40.0,0.0,320.0,576.0,true,0");
            AppSettings s = AppSettings.fromProperties(old);
            ok(s.canvasLayouts.isEmpty() && WidgetLayout.find(s.widgetLayout, "Focus").x == 40, "old layout");
            ok(WidgetLayout.find(s.layoutFor(1480, 552), "Focus").x == Math.round(40 * 1480 / 1856.0), "sideways phone stretches the saved layout like before");
            Placement[] tall = s.layoutFor(1000, 1560);
            ok(!overlaps(tall) && WidgetLayout.find(tall, "Spotify").visible, "upright tablet gets its default");
            WidgetLayout.find(tall, "Focus").x = 64;
            s.store(1000, 1560, tall);
            AppSettings back = AppSettings.fromProperties(s.toProperties());
            ok(WidgetLayout.find(back.widgetLayout, "Focus").x == 40 && back.canvasLayouts.size() == 1 && WidgetLayout.find(back.layoutFor(1000, 1560), "Focus").x == 64, "round trip");
            ok(back.toProperties().getProperty("Layout.1000x1560.Focus") != null && back.toProperties().getProperty("Layout.Focus") != null, "property keys");
            java.util.Properties odd = back.toProperties();
            odd.setProperty("Layout.1856x576.Focus", "999,0,320,576,true,0"); odd.setProperty("Layout.12x5.Focus", "0,0,1,1,true,0"); odd.setProperty("Layout.x.Focus", "bad");
            AppSettings cleaned = AppSettings.fromProperties(odd);
            ok(cleaned.canvasLayouts.size() == 1 && WidgetLayout.find(cleaned.widgetLayout, "Focus").x == 40, "invalid or duplicate canvas layouts were kept");
        } });
        check("A new shape stretches the closest saved layout that fits", new Action() { public void run() {
            AppSettings s = new AppSettings();
            s.widgetLayout = WidgetLayout.defaults();
            s.store(1856, 936, WidgetLayout.defaults(1856, 936));
            WidgetLayout.find(s.canvasLayouts.get("1856x936"), "Focus").x = 8;
            ok(WidgetLayout.find(s.layoutFor(1856, 1200), "Focus").x == 8, "picked the wrong saved layout");
            Placement[] wide = s.layoutFor(2496, 576);
            ok(WidgetLayout.find(wide, "Mixer").x == Math.round(824 * 2496 / 1856.0) && !overlaps(wide), "wide stretch");
        } });
        check("Cards size their content like the PC: deck grid, countdown, cover, temperature, Spotify columns", new Action() { public void run() {
            int[] reference = CardSizing.deckGrid(472 - 42, 250 - 90), compact = CardSizing.deckGrid(420 - 42, 190 - 90), big = CardSizing.deckGrid(681 - 42, 328 - 90);
            ok(reference[0] == 3 && reference[1] == 2 && compact[0] == 3 && compact[1] == 1 && big[0] * big[1] > 6, "deck " + big[0] + "x" + big[1]);
            int[] six = CardSizing.deckGrid(640 - 42, 360 - 90, 6), many = CardSizing.deckGrid(640 - 42, 360 - 90, 40);
            ok(six[0] * six[1] == 6 && many[0] == 4 && many[1] == 3, "six buttons fill a big card " + six[0] + "x" + six[1]);
            ok(CardSizing.deckGlyph(80, false) == 24 && CardSizing.deckGlyph(100, true) == 18 && CardSizing.deckGlyph(400, false) == 44, "glyphs");
            ok(CardSizing.focusTime(320, 576) == 84 && CardSizing.focusTime(296, 300) == 42 && CardSizing.focusTime(600, 900) > 84, "countdown");
            ok(CardSizing.mediaArt(472, 310) == 104 && CardSizing.mediaArt(600, 500) > 104 && CardSizing.mediaArt(2000, 2000) == 220, "cover");
            ok(CardSizing.weatherTemperature(472, 250) == 54 && CardSizing.weatherTemperature(360, 220) >= 40, "temperature");
            ok(CardSizing.spotifyColumns(560) == 1 && CardSizing.spotifyColumns(752) == 2 && CardSizing.spotifyColumns(3000) == 3, "Spotify");
        } });
        check("Palettes and accents match the Windows resources", new Action() { public void run() {
            Theme midnight = Theme.of("Midnight", "Lime"), slate = Theme.of("Slate", "Glacier");
            ok(midnight.page == 0xFF0C0E13 && midnight.accent == 0xFFBAF28B && slate.surface == 0xFF1B2938 && slate.accent == 0xFF8ED8F8 && Theme.of("Warm", "Amber").text == 0xFFFAF3E9, "colors");
        } });

        System.out.println(passes + " passed, " + failures + " failed.");
        System.exit(failures == 0 ? 0 : 1);
    }
}
