package com.alialyami.widgetscreen.tests;

import com.alialyami.widgetscreen.core.AppSettings;
import com.alialyami.widgetscreen.core.CalendarText;
import com.alialyami.widgetscreen.core.CardSizing;
import com.alialyami.widgetscreen.core.ClaudeText;
import com.alialyami.widgetscreen.core.ClaudePit;
import com.alialyami.widgetscreen.core.PitArt;
import com.alialyami.widgetscreen.core.PitPath;
import com.alialyami.widgetscreen.core.ClipText;
import com.alialyami.widgetscreen.core.DashboardFit;
import com.alialyami.widgetscreen.core.Format;
import com.alialyami.widgetscreen.core.GameText;
import com.alialyami.widgetscreen.core.HomeText;
import com.alialyami.widgetscreen.core.Inks;
import com.alialyami.widgetscreen.core.SoundboardText;
import com.alialyami.widgetscreen.core.SystemText;
import com.alialyami.widgetscreen.core.Json;
import com.alialyami.widgetscreen.core.MacroText;
import com.alialyami.widgetscreen.core.PcLink;
import com.alialyami.widgetscreen.core.PcPairing;
import com.alialyami.widgetscreen.core.NotesText;
import com.alialyami.widgetscreen.core.PcState;
import com.alialyami.widgetscreen.core.PrayerText;
import com.alialyami.widgetscreen.core.StorageText;
import com.alialyami.widgetscreen.core.Spotify;
import com.alialyami.widgetscreen.core.Theme;
import com.alialyami.widgetscreen.core.Weather;
import com.alialyami.widgetscreen.core.WidgetLayout;
import com.alialyami.widgetscreen.core.WorldClockText;
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
import java.util.Locale;
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
                else if (path.startsWith("/state")) reply = "{\"name\":\"DESKTOP-1\",\"quiet\":true,\"use24\":false,\"focus\":{\"remainingMs\":60000,\"durationMs\":1500000,\"running\":true,\"progress\":0.96,\"paused\":false,\"finished\":true,\"chime\":true,\"customMinutes\":45,\"sessionsToday\":2,\"minutesToday\":50}}";
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
            ok(s.widgetLayout == null && s.layoutMode.equals("Auto") && s.keepScreenOn.equals("Charging") && s.hideSystemBars, "layout or display");
        } });
        check("Settings save atomically and load back", new Action() { public void run() throws Exception {
            File file = new File(folder, "settings.xml");
            AppSettings s = new AppSettings();
            s.theme = "Warm"; s.accent = "Amber"; s.textScale = 1.1; s.layoutMode = "List"; s.keepScreenOn = "Never"; s.landscapeOnly = true;
            s.widgetLayout = WidgetLayout.defaults(); WidgetLayout.find(s.widgetLayout, "Soundboard").visible = true; s.widgetLayout[0].layer = 7;
            AppSettings.save(file, s);
            ok(!new File(folder, "settings.xml.tmp").exists(), "Temporary file left behind.");
            AppSettings loaded = AppSettings.load(file, null);
            ok(loaded.theme.equals("Warm") && loaded.accent.equals("Amber") && Math.abs(loaded.textScale - 1.1) < 1e-9 && loaded.layoutMode.equals("List") && loaded.keepScreenOn.equals("Never") && loaded.landscapeOnly, "settings");
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
            ok(s.textScale == 1.4 && s.cornerRadius == 0 && s.accent.equals("Lime") && s.theme.equals("Midnight") && s.layoutMode.equals("Auto"), "Not normalized.");
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
            ok(n.length == WidgetLayout.IDS.length && focus.x == 0 && focus.width == 280 && focus.height == 192 && focus.y == 576 - 192, "focus " + focus.bounds());
            ok(media.width == 1856 && media.height == 576 && media.x == 0 && !media.visible, "media " + media.bounds());
        } });
        check("Moving and resizing respect edges, minimums and snap", new Action() { public void run() {
            Box start = new Box(100, 100, 400, 320);
            ok(WidgetLayout.transform("Focus", start, "", -500, 5, false).equals(new Box(0, 105, 400, 320)), "move");
            ok(WidgetLayout.transform("Focus", start, "", 3, 5, true).equals(new Box(104, 104, 400, 320)), "snap");
            ok(WidgetLayout.transform("Focus", start, "SE", -1000, -1000, false).equals(new Box(100, 100, 280, 192)), "minimum");
            ok(WidgetLayout.transform("Focus", start, "NW", -1000, -1000, false).equals(new Box(0, 0, 500, 420)), "grow to edge");
            ok(WidgetLayout.transform("Focus", start, "NE", Double.NaN, 0, false) == start, "NaN");
        } });
        check("Snapping to other widgets matches Windows, with guides", new Action() { public void run() {
            List<WidgetLayout.Guide> guides = new ArrayList<WidgetLayout.Guide>();
            List<Box> other = java.util.Arrays.asList(new Box(705, 0, 400, 300));
            Box moved = WidgetLayout.align("Focus", new Box(0, 0, 300, 300), "", 385, 3, other, 1856, 576, guides);
            ok(moved.equals(new Box(389, 0, 300, 300)), "gap " + moved);
            boolean gapGuide = false;
            for (WidgetLayout.Guide g : guides) if (g.vertical && g.at == 697 && g.from == 0 && g.to == 300) gapGuide = true;
            ok(gapGuide, "gap guide");
            Box far = WidgetLayout.align("Focus", new Box(0, 0, 300, 300), "", 357, 3, other, 1856, 576, guides);
            ok(far.x == 360, "grid when out of reach " + far);
            Box centred = WidgetLayout.align("Focus", new Box(0, 400, 300, 176), "", 645, 0, java.util.Arrays.asList(new Box(600, 0, 400, 300)), 1856, 576, guides);
            ok(centred.x == 650, "centre " + centred);
            Box resized = WidgetLayout.align("Focus", new Box(0, 0, 300, 300), "SE", 95, 30, java.util.Arrays.asList(new Box(0, 341, 400, 200)), 1856, 576, guides);
            ok(resized.equals(new Box(0, 0, 400, 325)), "resize " + resized);
            Box squeezed = WidgetLayout.align("Focus", new Box(0, 0, 300, 300), "SE", -30, 0, java.util.Arrays.asList(new Box(0, 420, 270, 100)), 1856, 576, guides);
            ok(squeezed.width == 280, "minimum " + squeezed);
            Box still = WidgetLayout.align("Focus", new Box(0, 0, 300, 300), "", 0, 0, other, 1856, 576, guides);
            ok(still.equals(new Box(0, 0, 300, 300)) && guides.isEmpty(), "no drag");
        } });
        check("Edge grips resize one side only and cards fold like the PC", new Action() { public void run() {
            Box start = new Box(400, 100, 400, 360);
            ok(WidgetLayout.transform("Media", start, "E", 57, 80, false).equals(new Box(400, 100, 457, 360)), "right edge");
            ok(WidgetLayout.transform("Media", start, "N", 40, -60, false).equals(new Box(400, 40, 400, 420)), "top edge");
            ok(WidgetLayout.transform("Media", start, "W", 1000, 0, false).equals(new Box(488, 100, 312, 360)), "left edge minimum");
            ok(WidgetLayout.transform("Media", start, "S", 0, -1000, false).equals(new Box(400, 100, 400, 200)), "bottom edge minimum");
            List<WidgetLayout.Guide> guides = new ArrayList<WidgetLayout.Guide>();
            Box snapped = WidgetLayout.align("Media", start, "S", 0, 30, java.util.Arrays.asList(new Box(900, 0, 300, 486)), 1856, 576, guides);
            ok(snapped.equals(new Box(400, 100, 400, 386)), "bottom edge snaps alone " + snapped);
            double[][] windows = { { 280, 192 }, { 312, 200 }, { 288, 204 }, { 320, 224 }, { 260, 280 }, { 232, 152 }, { 360, 360 }, { 300, 240 } };
            String[] ids = { "Focus", "Media", "Shortcuts", "Mixer", "System", "Weather", "Spotify", "Soundboard" };
            for (int i = 0; i < ids.length; i++) ok(java.util.Arrays.equals(WidgetLayout.minimum(ids[i]), windows[i]), ids[i] + " minimum differs from Windows");
            ok(CardSizing.focusShort(299) && !CardSizing.focusShort(300) && CardSizing.mediaShort(289) && !CardSizing.mediaShort(290), "fold heights");
            ok(CardSizing.mixer(420) == CardSizing.MIXER_FULL && CardSizing.mixer(300) == CardSizing.MIXER_COMPACT && CardSizing.mixer(299) == CardSizing.MIXER_MASTER, "mixer");
            ok(CardSizing.emptyDetail(478) == 3 && CardSizing.emptyDetail(262) == 2 && CardSizing.emptyDetail(120) == 1 && CardSizing.emptyDetail(62) == 0, "empty states");
            ok(CardSizing.focusTime(320, 576) == 84 && CardSizing.mediaArt(552, 310) == 104 && CardSizing.focusTime(280, 192) == 40 && CardSizing.mediaArt(312, 200) == 64, "readings");
        } });
        check("Arrange keeps the reading order and tidies without overlaps", new Action() { public void run() {
            List<Placement> columns = java.util.Arrays.asList(new Placement("Focus", 0, 0, 320, 576, true), new Placement("Shortcuts", 344, 330, 460, 246, true),
                new Placement("Media", 336, 0, 472, 310, true), new Placement("Mixer", 830, 20, 456, 500, true));
            ok(WidgetLayout.arrangeOrder(columns, 1856, 576).equals(java.util.Arrays.asList("Focus", "Media", "Shortcuts", "Mixer")), "columns " + WidgetLayout.arrangeOrder(columns, 1856, 576));
            List<Placement> rows = java.util.Arrays.asList(new Placement("Mixer", 0, 700, 600, 500, true), new Placement("Media", 340, 10, 300, 320, true), new Placement("Focus", 0, 0, 320, 320, true));
            ok(WidgetLayout.arrangeOrder(rows, 656, 1712).equals(java.util.Arrays.asList("Focus", "Media", "Mixer")), "rows");
            double[][] sizes = { { 1856, 576 }, { 2496, 576 }, { 1216, 680 }, { 656, 1712 } };
            for (double[] size : sizes) {
                Placement[] start = WidgetLayout.defaults(size[0], size[1]);
                List<String> shown = java.util.Arrays.asList("Focus", "Media", "Shortcuts", "Mixer", "System", "Weather");
                Placement[] tidy = WidgetLayout.tidy(start, shown, size[0], size[1]);
                List<Placement> visible = new ArrayList<Placement>();
                for (Placement p : tidy) if (shown.contains(p.id)) { Placement q = p.copy(); q.visible = true; visible.add(q); }
                ok(!overlaps(visible.toArray(new Placement[0])), "overlap on " + size[0] + "x" + size[1]);
                for (Placement p : tidy) {
                    ok(p.x >= 0 && p.y >= 0 && p.x + p.width <= size[0] && p.y + p.height <= size[1], "inside " + p.id);
                    if (!shown.contains(p.id)) ok(p.bounds().equals(WidgetLayout.find(start, p.id).bounds()), "hidden kept " + p.id);
                    ok(p.visible == WidgetLayout.find(start, p.id).visible, "shown set kept");
                }
            }
        } });

        check("Clock, focus and readings use the Windows formats", new Action() { public void run() {
            LocalDateTime t = LocalDateTime.of(2026, 10, 8, 14, 5);
            ok(Format.clockTime(t, false).equals("2:05") && Format.clockTime(t, true).equals("14:05") && Format.clockPeriod(t).equals("PM"), "time");
            ok(Format.clockDay(t).equals("THURSDAY") && Format.clockDate(t).equals("October 8") && Format.endsAt(t, false).equals("2:05 PM"), "date");
            ok(Format.focus(25 * 60000L).equals("25:00") && Format.focus(25 * 60000L - 1500).equals("24:59") && Format.focusStatus(0, false, 1, "x").equals("Done. Take a break."), "focus");
            ok(Format.focusButton(1000, false, 0.5).equals("Resume") && Format.focusButton(1000, true, 0.5).equals("Pause") && Format.focusButton(1500000, false, 0).equals("Start focus"), "focus button");
            ok(Format.sessions(0, 0).isEmpty() && Format.sessions(1, 25).equals("1 session today  ·  25 m") && Format.sessions(3, 100).equals("3 sessions today  ·  1 h 40 m"), "sessions");
            ok(Format.mediaTime(185000).equals("3:05") && Format.rate(2048).equals("2 KB/s") && Format.rate(3 * 1048576).equals("3.0 MB/s"), "media and rate");
            ok(Format.uptime((2 * 1440 + 3 * 60 + 4) * 60000L).equals("2d 3h 4m") && Format.uptime((2 * 1440 + 3 * 60 + 4) * 60000L, 2).equals("2d 3h"), "uptime");
        } });

        check("Weather parses Open-Meteo responses and describes codes", new Action() { public void run() {
            Weather.Snapshot w = Weather.parse("{\"current\":{\"temperature_2m\":31.6,\"apparent_temperature\":33.2,\"weather_code\":2,\"wind_speed_10m\":12.4}}", 5);
            ok(w.temperature == 31.6 && w.code == 2 && Weather.describe(w.code).equals("Partly cloudy") && Weather.details(w).equals("Feels 33°  ·  12 km/h") && Weather.details(w, true).equals("Feels 33°  ·  12 mph"), "snapshot");
            StringBuilder times = new StringBuilder(), temps = new StringBuilder(), codes = new StringBuilder();
            for (int h = 0; h < 48; h++) {
                if (h > 0) { times.append(','); temps.append(','); codes.append(','); }
                times.append('"').append("2026-10-").append(h < 24 ? "22" : "23").append('T').append(String.format(Locale.US, "%02d", h % 24)).append(":00\"");
                temps.append(10 + h); codes.append(h % 2 == 0 ? 0 : 61);
            }
            Weather.Snapshot full = Weather.parse("{\"current\":{\"temperature_2m\":20,\"apparent_temperature\":19,\"wind_speed_10m\":8,\"weather_code\":61},\"daily\":{\"temperature_2m_max\":[24.6],\"temperature_2m_min\":[14.2]},\"hourly\":{\"time\":[" + times + "],\"temperature_2m\":[" + temps + "],\"weather_code\":[" + codes + "]}}", 5, "2026-10-22T21:00");
            ok(full.high == 24.6 && full.low == 14.2 && Weather.details(full, false).equals("Feels 19°  ·  8 km/h  ·  ↑ 25° ↓ 14°"), "high and low " + Weather.details(full, false));
            ok(full.hours.size() == 5 && full.hours.get(4).time.equals("2026-10-23T09:00") && full.hours.get(0).time.equals("2026-10-22T21:00") && full.hours.get(1).time.equals("2026-10-23T00:00") && full.hours.get(2).time.equals("2026-10-23T03:00") && full.hours.get(0).temperature == 31, "next hours");
            ok(Weather.hourLabel("2026-10-23T00:00", true).equals("00:00") && Weather.hourLabel("2026-10-22T14:00", false).equals("2 PM") && Weather.hourLabel("2026-10-22T00:00", false).equals("12 AM"), "hour labels");
            ok(Weather.family(0).equals("sun") && Weather.family(2).equals("partly") && Weather.family(3).equals("cloud") && Weather.family(45).equals("fog") && Weather.family(63).equals("rain") && Weather.family(81).equals("rain") && Weather.family(75).equals("snow") && Weather.family(95).equals("storm"), "glyph families");
            ok(Weather.forecastUrl(1.5, -2.25, true).contains("temperature_unit=fahrenheit&wind_speed_unit=mph") && !Weather.forecastUrl(1.5, -2.25, false).contains("fahrenheit") && Weather.forecastUrl(1.5, -2.25, false).contains("latitude=1.5&longitude=-2.25"), "forecast url");
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
            // Only home-network addresses: a typo must not send the token to the internet.
            ok(PcLink.parseAddress("8.8.8.8") == null && PcLink.parseAddress("example.com") == null && PcLink.parseAddress("172.32.0.1") == null, "public addresses");
            ok(PcLink.parseAddress("10.0.0.5") != null && PcLink.parseAddress("172.20.1.1:43830") != null && PcLink.parseAddress("169.254.3.4") != null && PcLink.parseAddress("localhost") != null, "home addresses");
            // Discovery goes to each interface's broadcast address, with the limited broadcast as the fallback.
            java.util.List<String> targets = PcLink.broadcastAddresses();
            ok(targets.contains("255.255.255.255") && targets.indexOf("255.255.255.255") == targets.size() - 1, "broadcast fallback last");
            for (String target : targets) ok(target.matches("[0-9]{1,3}(\\.[0-9]{1,3}){3}"), "broadcast address " + target);
            // An unreachable PC is asked again after 5 s, 15 s, then every minute.
            ok(PcLink.retryDelayMs(0) == 0 && PcLink.retryDelayMs(1) == 5000 && PcLink.retryDelayMs(2) == 15000 && PcLink.retryDelayMs(3) == 60000 && PcLink.retryDelayMs(40) == 60000, "retry delays");
            AppSettings remembered = new AppSettings(); remembered.use24 = true;
            ok(AppSettings.fromProperties(remembered.toProperties()).use24 && !AppSettings.fromProperties(new AppSettings().toProperties()).use24, "24-hour clock remembered");
            // Keep the screen on: Charging for new installs; an older file's KeepScreenOn becomes Always or Never.
            java.util.Properties old = new AppSettings().toProperties(); old.remove("KeepScreenOnMode"); old.setProperty("KeepScreenOn", "false");
            AppSettings never = AppSettings.fromProperties(old); old.setProperty("KeepScreenOn", "true"); AppSettings always = AppSettings.fromProperties(old);
            ok(new AppSettings().keepScreenOn.equals("Charging") && never.keepScreenOn.equals("Never") && always.keepScreenOn.equals("Always"), "keep-screen-on migration");
            ok(new AppSettings().keepsScreenOn(true) && !new AppSettings().keepsScreenOn(false) && always.keepsScreenOn(false) && !never.keepsScreenOn(true), "keep-screen-on modes");
            AppSettings badMode = new AppSettings(); badMode.keepScreenOn = "Sometimes"; badMode.normalize();
            ok(badMode.keepScreenOn.equals("Charging"), "keep-screen-on normalizes");
            AppSettings dismissed = new AppSettings(); dismissed.hintDismissed = true;
            ok(AppSettings.fromProperties(dismissed.toProperties()).hintDismissed && !new AppSettings().hintDismissed, "tips dismissal remembered");
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
                PcState.Audio mixer = new PcState.Audio();
            PcState.Session system = new PcState.Session(); system.id = "sys"; system.name = "System sounds"; system.processId = 0; mixer.sessions.add(system);
            PcState.Session spotify = new PcState.Session(); spotify.id = "s"; spotify.name = "Spotify"; spotify.processId = 5; mixer.sessions.add(spotify);
            PcState.Session chrome = new PcState.Session(); chrome.id = "c"; chrome.name = "chrome"; chrome.processId = 6; mixer.sessions.add(chrome);
            ok(mixer.sessionFor("Spotify") == spotify && mixer.sessionFor("Chrome") == chrome && mixer.sessionFor("VLC") == null && mixer.sessionFor("") == null, "playing app's mixer row");
            PcState deckState = PcState.parse(Json.obj(Json.parse("{\"deck\":[{\"label\":\"Quiet\",\"glyph\":\"E767\",\"action\":\"Control\",\"describe\":\"Toggle Quiet mode\",\"confirm\":false,\"enabled\":true,\"on\":true,\"color\":\"Amber\"},{\"label\":\"Snap\",\"glyph\":\"E722\",\"action\":\"Hotkey\",\"describe\":\"Win+Shift+S  →  chrome\",\"confirm\":false,\"enabled\":true}]}")));
            ok(deckState.deck.size() == 2 && deckState.deck.get(0).on && deckState.deck.get(0).color.equals("Amber") && !deckState.deck.get(1).on && deckState.deck.get(1).color.isEmpty() && deckState.deck.get(1).describe.endsWith("chrome"), "deck colour, state and hotkey target");
            ok(state.focus.finished && state.focus.chime && !state.focus.paused && state.focus.customMinutes == 45 && state.focus.sessionsToday == 2 && state.focus.minutesToday == 50, "focus finish, preset and session fields");
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
            ok(state.media != null && state.audio != null && state.system != null && state.weather != null && state.spotify != null && state.discord != null && state.storage != null && state.clocks != null && state.prayer != null && state.notes != null && state.clipboard != null && state.macros != null && state.calendar != null && state.games != null && state.home != null, "every part");
            ok(state.clocks != null && state.clocks.clocks.size() == 2 && state.clocks.clocks.get(0).label.equals("Tokyo") && state.clocks.clocks.get(0).offset == 540 && state.clocks.clocks.get(1).label.equals("UTC"), "clocks");
            ok(state.prayer != null && state.prayer.located && state.prayer.city.equals("Riyadh") && state.prayer.nextAt > 0 && state.prayer.times[0] > 0, "prayer");
            ok(state.notes != null && state.notes.text.equals("From the phone") && state.notes.items.size() == 1 && state.notes.items.get(0).text.equals("Bread"), "notes");
            ok(state.macros != null && state.macros.items.size() == 2 && state.macros.items.get(0).label.equals("Quiet twice") && state.macros.items.get(0).steps.size() == 3 && state.macros.items.get(1).confirm, "macros");
            ok(state.calendar != null && state.calendar.feeds == 0 && !state.calendar.read && state.calendar.events.isEmpty(), "calendar");
            ok(state.games != null, "games");
            ok(state.home != null && !state.home.connected && state.home.devices.isEmpty(), "home");
            if (!state.storage.error) ok(!state.storage.drives.isEmpty() && state.storage.drives.get(0).total > 0 && state.storage.lowPercent == 10, "storage");
            // The PC's Claude section comes from its labelled fixture: four sessions, the first one needs you.
            ok(state.claude != null && state.claude.sample && state.claude.connected && state.claude.sessions.size() == 4 && "needs".equals(state.claude.sessions.get(0).state) && state.claude.sessions.get(0).project.equals("api-server")
                && state.claude.sessions.get(0).tokens[0] == 412000 && state.claude.fiveHour != null && state.claude.fiveHour.pct == 84 && state.claude.fiveHour.resetsAt > 1e9 && state.claude.usage[2].size() == 5 && state.claude.needsYou() == 1, "claude");
            ok(state.claudePage == -1 && state.claudeNeeds == 0, "claude alert without the card on a page");
            // Its projects and threads: every group's babies are threads of that project, each counted once.
            ok(state.claude.threads.size() == 10 && state.claude.groups[2].size() == 3 && state.claude.every == 2, "claude threads and week groups from the PC");
            for (PcState.ClaudeGroup g : state.claude.groups[2]) for (String id : g.threads) ok(state.claude.thread(id) != null && state.claude.thread(id).project.equals(g.project), "a baby under the wrong project: " + id);
            PcState.ClaudeGroup api = null; for (PcState.ClaudeGroup g : state.claude.groups[2]) if (g.project.equals("api-server")) api = g;
            ok(api != null && api.key.equals("C:\\Code\\api-server") && api.threads.size() == 3 && "needs".equals(ClaudeText.groupState(state.claude, api)) && api.threads.get(0).equals("sample-1"), "the api-server project's garage");
            // The pit wall: each thread's car and what it does, the burn, the lanes and today's facts.
            PcState.ClaudeThread face = state.claude.thread("sample-3"), first = state.claude.thread("sample-1"), busy = state.claude.thread("sample-2");
            ok(face != null && "pit".equals(face.pit) && "sport".equals(face.car) && "need".equals(first.pit) && "gt".equals(first.car) && first.mix[2][0] == 1 && first.lines[0][0] == 412 && first.lines[0][1] == 96
                && "work".equals(busy.pit) && busy.rate == 41000 && "end".equals(state.claude.thread("sample-5").pit), "cars from the PC");
            ok(state.claude.burn.length == 60 && state.claude.burn[59] > 0 && state.claude.rate == 50000 && state.claude.scale >= 60000 && state.claude.lanes.size() == 3 && state.claude.lanes.get(0).bins.length == 30
                && state.claude.facts.tools == 312 && state.claude.facts.asked == 41 && state.claude.facts.telemetry && Math.round(state.claude.facts.cacheHit() * 100) == 91 && api.mix[0] > 0.9 && api.added == 462, "burn, lanes and facts from the PC");
            if (state.audio.available) ok(!state.audio.device.isEmpty() && state.audio.volume >= 0 && state.audio.volume <= 1, "audio");
            if (!state.system.error) ok(state.system.uptimeMs > 0, "system");
            if (!state.system.error) ok(!state.system.cpuTempState.isEmpty() && !state.system.fpsState.isEmpty() && state.system.showCpuTemp && state.system.showFps, "system sensors: " + state.system.cpuTempState + " " + state.system.fpsState);
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
        check("Every other card picks small, medium and large designs like the PC", new Action() { public void run() {
            ok(CardSizing.focus(280, 192) == CardSizing.SMALL && CardSizing.focus(320, 400) == CardSizing.MEDIUM && CardSizing.focus(320, 576) == CardSizing.LARGE && CardSizing.focusRing(320, 576) == 272, "focus");
            ok(CardSizing.media(312, 200) == CardSizing.SMALL && CardSizing.media(472, 310) == CardSizing.MEDIUM && CardSizing.media(472, 576) == CardSizing.LARGE && CardSizing.mediaCover(472, 576) == 280, "now playing");
            ok(CardSizing.mixerDesign(250) == CardSizing.SMALL && CardSizing.mixerDesign(350) == CardSizing.MEDIUM && CardSizing.mixerDesign(576) == CardSizing.LARGE && CardSizing.mixerColumns(800, 576) == 2 && CardSizing.mixerColumns(456, 576) == 1 && CardSizing.mixerColumns(800, 350) == 1, "mixer");
            ok(CardSizing.spotify(1000, 576) == CardSizing.LARGE && CardSizing.spotify(560, 576) == CardSizing.MEDIUM && CardSizing.spotify(760, 480) == CardSizing.SMALL, "spotify");
            ok(CardSizing.tile(80, 60) == CardSizing.SMALL && CardSizing.tile(140, 76) == CardSizing.MEDIUM && CardSizing.tile(160, 150) == CardSizing.LARGE, "tiles");
        } });        check("System stats and Weather pick small, medium and large designs like the PC", new Action() { public void run() {
            ok(CardSizing.stats(260, 280) == CardSizing.SMALL && CardSizing.stats(339, 576) == CardSizing.SMALL && CardSizing.stats(472, 310) == CardSizing.MEDIUM && CardSizing.stats(900, 300) == CardSizing.MEDIUM && CardSizing.stats(400, 420) == CardSizing.LARGE && CardSizing.stats(472, 576) == CardSizing.LARGE, "stats");
            ok(CardSizing.weather(232, 152) == CardSizing.SMALL && CardSizing.weather(600, 219) == CardSizing.SMALL && CardSizing.weather(472, 250) == CardSizing.MEDIUM && CardSizing.weather(360, 576) == CardSizing.MEDIUM && CardSizing.weather(400, 340) == CardSizing.LARGE, "weather");
            ok(CardSizing.statsTileColumns(472, 4) == 2 && CardSizing.statsTileColumns(1200, 4) == 4 && CardSizing.statsTileColumns(1200, 1) == 1 && CardSizing.weatherHours(400) == 4 && CardSizing.weatherHours(800) == 5 && CardSizing.weatherHours(300) == 3, "tiles and hours");
            ok(CardSizing.statsListValue(39) == 22 && CardSizing.statsRing(143, 110) == 110 && CardSizing.statsTileValue(240) == 58, "reading sizes");
        } });
        check("Storage picks small, medium and large designs and words drives like the PC", new Action() { public void run() {
            ok(CardSizing.storage(240, 152) == CardSizing.SMALL && CardSizing.storage(600, 199) == CardSizing.SMALL && CardSizing.storage(472, 250) == CardSizing.MEDIUM && CardSizing.storage(439, 576) == CardSizing.MEDIUM && CardSizing.storage(440, 340) == CardSizing.LARGE, "designs");
            ok(CardSizing.storageCapacity(CardSizing.SMALL, 192, 68, 3) == 1 && CardSizing.storageCapacity(CardSizing.MEDIUM, 424, 166, 5) == 4 && CardSizing.storageDetailRows(166, 2) && !CardSizing.storageDetailRows(166, 3) && CardSizing.storageCapacity(CardSizing.LARGE, 424, 492, 3) == 6 && CardSizing.storageCapacity(CardSizing.LARGE, 1200, 300, 6) == 4, "capacity");
            ok(CardSizing.storageTileColumns(472, 1) == 1 && CardSizing.storageTileColumns(472, 3) == 2 && CardSizing.storageTileColumns(1200, 6) == 4 && CardSizing.storageRing(200, 240) == 144 && CardSizing.storageListValue(40) == 20, "tiles");
            double gb = 1024.0 * 1024 * 1024;
            ok(StorageText.bytes(820.0 * 1024 * 1024).equals("820 MB") && StorageText.bytes(45.24 * gb).equals("45.2 GB") && StorageText.bytes(212.4 * gb).equals("212 GB") && StorageText.bytes(1.82 * 1024 * gb).equals("1.82 TB") && StorageText.bytes(12.5 * 1024 * gb).equals("12.5 TB") && StorageText.bytes(-5).equals("0 KB"), "sizes");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"storage\":{\"lowPercent\":10,\"drives\":[{\"name\":\"C:\",\"label\":\"Windows\",\"total\":536870912000,\"free\":42949672960},{\"name\":\"D:\",\"label\":\"\",\"total\":1073741824000,\"free\":644245094400},{\"name\":\"\",\"total\":5}]}}")));
            ok(state.storage != null && state.storage.drives.size() == 2 && state.storage.lowPercent == 10, "parse");
            ok(StorageText.low(state.storage.drives.get(0), 10) && !StorageText.low(state.storage.drives.get(1), 10) && StorageText.lowFlag(state.storage.drives, 10).equals("C: LOW") && StorageText.title(state.storage.drives.get(0)).equals("C:  Windows") && StorageText.title(state.storage.drives.get(1)).equals("D:"), "low");
            ok(StorageText.more(state.storage.drives, 1, 50).equals("+1 more drive") && StorageText.more(state.storage.drives, 0, 10).equals("+2 more drives  ·  1 low"), "more");
            ok(PcState.parse(Json.obj(Json.parse("{\"storage\":{\"error\":true}}"))).storage.error, "error");
            WidgetLayout.Placement storage = WidgetLayout.find(WidgetLayout.defaults(), "Storage");
            ok(storage != null && !storage.visible && !WidgetLayout.find(WidgetLayout.defaults(1480, 552), "Storage").visible, "hidden by default");
        } });
        check("World clocks pick the PC's designs and word days and hours like the PC", new Action() { public void run() {
            ok(CardSizing.clocks(240, 152) == CardSizing.SMALL && CardSizing.clocks(600, 199) == CardSizing.SMALL && CardSizing.clocks(472, 250) == CardSizing.MEDIUM && CardSizing.clocks(439, 576) == CardSizing.MEDIUM && CardSizing.clocks(440, 300) == CardSizing.LARGE, "designs");
            ok(CardSizing.clockRows(68) == 1 && CardSizing.clockRows(112) == 3 && CardSizing.clockTilesAcross(1000, 3) && !CardSizing.clockTilesAcross(392, 3) && CardSizing.clockTilesAcross(392, 2) && CardSizing.clockColumnTime(133, 166, false) == 36 && CardSizing.clockListTime(50) == 28, "sizes");
            ok(WorldClockText.difference(0).equals("Same time") && WorldClockText.difference(180).equals("+3 h") && WorldClockText.difference(-330).equals("−5:30 h") && WorldClockText.difference(345).equals("+5:45 h"), "hours");
            ok(WorldClockText.day(0).equals("Today") && WorldClockText.day(1).equals("Tomorrow") && WorldClockText.day(-1).equals("Yesterday") && WorldClockText.day(2).equals("+2 days"), "days");
            // 1 Nov 2026 20:30 UTC, the PC in Riyadh (+3): Tokyo (+9) is 05:30 tomorrow, Honolulu (-10) 10:30 today.
            long now = java.time.LocalDateTime.of(2026, 11, 1, 20, 30).toEpochSecond(java.time.ZoneOffset.UTC) * 1000;
            ok(WorldClockText.at(now, 540).getHour() == 5 && WorldClockText.dayOffset(now, 540, 180) == 1 && WorldClockText.dayOffset(now, -600, 180) == 0 && WorldClockText.at(now, -600).getHour() == 10, "times");
            ok(WorldClockText.date(WorldClockText.at(now, 540), 1).equals("Tomorrow, Nov 2") && Format.clockTime(WorldClockText.at(now, 540), false).equals("5:30") && Format.clockPeriod(WorldClockText.at(now, 540)).equals("AM"), "date");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"clocks\":{\"pcOffset\":180,\"clocks\":[{\"label\":\"Tokyo\",\"offset\":540},{\"label\":\"Bad\",\"offset\":99999}]}}")));
            ok(state.clocks != null && state.clocks.pcOffset == 180 && state.clocks.clocks.size() == 1 && state.clocks.clocks.get(0).label.equals("Tokyo"), "parse");
            WidgetLayout.Placement clocks = WidgetLayout.find(WidgetLayout.defaults(), "Clocks");
            ok(clocks != null && !clocks.visible && !WidgetLayout.find(WidgetLayout.defaults(1480, 552), "Clocks").visible && WidgetLayout.title("Clocks").equals("World clocks"), "hidden by default");
        } });
        check("Prayer times pick the PC's designs and show the PC's times and countdown", new Action() { public void run() {
            ok(CardSizing.prayer(240, 152) == CardSizing.SMALL && CardSizing.prayer(600, 219) == CardSizing.SMALL && CardSizing.prayer(472, 250) == CardSizing.MEDIUM && CardSizing.prayer(439, 576) == CardSizing.MEDIUM && CardSizing.prayer(440, 360) == CardSizing.LARGE, "designs");
            ok(CardSizing.prayerTilesAcross(860) && !CardSizing.prayerTilesAcross(859) && CardSizing.prayerNextColumn(424) == 195 && CardSizing.prayerTime(195, 166) == 47 && CardSizing.prayerLargeTop(492, false) == 187 && CardSizing.prayerTileTime(120, false) == 25, "sizes");
            ok(PrayerText.countdown(72 * 60000L + 12000).equals("in 1 h 13 m") && PrayerText.countdown(12 * 60000L).equals("in 12 m") && PrayerText.countdown(20000).equals("in under a minute"), "countdown");
            // 11:50 UTC is 14:50 on a PC three hours ahead.
            long asr = java.time.LocalDateTime.of(2026, 11, 1, 11, 50).toEpochSecond(java.time.ZoneOffset.UTC) * 1000;
            ok(PrayerText.time(asr, 180, false).equals("2:50 PM") && PrayerText.time(asr, 180, true).equals("14:50") && PrayerText.time(0, 180, true).equals("—"), "times");
            ok(PrayerText.justStarted(asr + 5 * 60000, asr) && !PrayerText.justStarted(asr + 25 * 60000, asr) && Math.abs(PrayerText.progress(asr + 30 * 60000, asr, asr + 60 * 60000) - 0.5) < 1e-9, "now and progress");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"prayer\":{\"located\":true,\"city\":\"Riyadh\",\"pcOffset\":180,\"next\":3,\"current\":2,\"nextAt\":" + (asr + 3600000) + ",\"previousAt\":" + asr + ",\"alerting\":true,\"times\":[1,2,3,4,5]}}")));
            ok(state.prayer.located && state.prayer.next == 3 && state.prayer.current == 2 && state.prayer.alerting && state.prayer.times[4] == 5 && state.prayer.city.equals("Riyadh"), "parse");
            ok(!PcState.parse(Json.obj(Json.parse("{\"prayer\":{\"located\":true,\"times\":[1]}}"))).prayer.located && !PcState.parse(Json.obj(Json.parse("{\"prayer\":{\"located\":false}}"))).prayer.located, "incomplete");
            ok(!WidgetLayout.find(WidgetLayout.defaults(), "Prayer").visible && !WidgetLayout.find(WidgetLayout.defaults(1480, 552), "Prayer").visible && WidgetLayout.title("Prayer").equals("Prayer times"), "hidden by default");
        } });
        check("Notes pick the PC's designs, read the PC's notes and word the list like the PC", new Action() { public void run() {
            ok(CardSizing.notes(260, 200) == CardSizing.SMALL && CardSizing.notes(472, 310) == CardSizing.MEDIUM && CardSizing.notes(699, 576) == CardSizing.MEDIUM && CardSizing.notes(700, 300) == CardSizing.LARGE && CardSizing.notesListColumn(652) == 300 && CardSizing.notesListColumn(1200) == 520, "designs");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"notes\":{\"version\":7,\"text\":\"Line one\\nLine two\",\"items\":[{\"id\":\"a\",\"text\":\"Milk\",\"done\":true},{\"id\":\"b\",\"text\":\"Bread\"},{\"text\":\"no id\"}]}}")));
            ok(state.notes.version == 7 && state.notes.text.equals("Line one\nLine two") && state.notes.items.size() == 2 && state.notes.items.get(0).done && !state.notes.items.get(1).done, "parse");
            ok(NotesText.status(state.notes.items).equals("1 TO DO") && NotesText.todoTab(state.notes.items).equals("To-do · 1") && NotesText.anyDone(state.notes.items), "wording");
            ok(NotesText.line("  Call the\nbank\t now ").equals("Call the bank now") && NotesText.line(new String(new char[500]).replace('\0', 'y')).length() == 200 && NotesText.line(" \n ").isEmpty(), "line");
            ok(!WidgetLayout.find(WidgetLayout.defaults(), "Notes").visible && WidgetLayout.title("Notes").equals("Notes and to-do"), "hidden by default");
        } });
        check("Claude pit wall sizes, colours and art match the PC's", new Action() { public void run() {
            ok(PitArt.hash("a") == 3826002220L && PitArt.hash("") == 2166136261L && PitArt.hash("api-server") == PitArt.hash(" API-Server "), "hash");
            ok(PitArt.accent("api-server") == PitArt.ACCENTS[1] && PitArt.accent("notes") == PitArt.ACCENTS[0] && PitArt.accent("photo-manager") == PitArt.ACCENTS[2], "the same project colours as the PC");
            ok(CardSizing.claude(260, 180) == CardSizing.SMALL && CardSizing.claude(359, 400) == CardSizing.SMALL && CardSizing.claude(600, 279) == CardSizing.SMALL && CardSizing.claude(472, 310) == CardSizing.MEDIUM && CardSizing.claude(639, 576) == CardSizing.MEDIUM && CardSizing.claude(640, 420) == CardSizing.LARGE, "designs");
            ok(CardSizing.claudeRevs(860) && !CardSizing.claudeRevs(859) && CardSizing.claudeSide(1000) && !CardSizing.claudeSide(999) && CardSizing.claudeModels(698, 1) && !CardSizing.claudeModels(697, 1) && CardSizing.claudeLines(792, 1) && !CardSizing.claudeLines(791, 1) && CardSizing.claudeModels(852, 1.4) && !CardSizing.claudeLines(852, 1.4)
                && CardSizing.claudeMediumRow(1) == 44 && CardSizing.claudeMediumRow(1.4) == 52 && CardSizing.claudeInlineCars(300, 5) == 3 && CardSizing.claudeInlineCars(200, 5) == 1 && CardSizing.claudeInlineCars(200, 2) == 2 && CardSizing.claudeInlineCars(100, 4) == 0
                && CardSizing.claudeSmallMeter(152, 1) && !CardSizing.claudeSmallMeter(132, 1.4) && CardSizing.claudeSmallCar(252, 100) == 85 && CardSizing.claudeSmallCar(100, 20) == 48, "sizes as on the PC");
            WidgetLayout.Placement claude = WidgetLayout.find(WidgetLayout.defaults(), "Claude");
            ok(claude != null && !claude.visible && WidgetLayout.title("Claude").equals("Claude Code") && WidgetLayout.minimum("Claude")[0] == 260 && WidgetLayout.minimum("Claude")[1] == 180, "hidden by default");
            // The SVG paths the art uses read the same as the PC's Geometry.Parse.
            java.util.List<float[]> path = PitPath.parse("M4 41 Q4 34 14 32 L40 29H50V31h-2v1l8 -6 z M20 32.5L-16 27");
            ok(path.size() == 11 && path.get(0)[0] == PitPath.MOVE && path.get(1)[0] == PitPath.QUAD && path.get(1)[3] == 14 && path.get(3)[1] == 50 && path.get(4)[2] == 31 && path.get(5)[1] == 48 && path.get(6)[2] == 32
                && path.get(7)[1] == 56 && path.get(7)[2] == 26 && path.get(8)[0] == PitPath.CLOSE && path.get(9)[0] == PitPath.MOVE && path.get(9)[2] == 32.5f, "path reading");
            ok(PitPath.parse("M84 20 L83 29").size() == 2 && PitPath.parse("M8 22h84v74H8Z M20 40h60v56H20Z").size() == 10, "path shapes");
            // Every state and car draws, moving states change between frames, Done and Ended play once and rest on frame 7.
            for (int state = PitArt.IDLE; state <= PitArt.END; state++)
                for (int car = PitArt.GT; car <= PitArt.HATCH; car++) {
                    PitArt.Look look = new PitArt.Look(car, true, true, true);
                    ok(PitArt.car(PitArt.ACCENTS[0], state, look, 0).size() > 20, "a car draws nothing in state " + state);
                    for (PitArt.Op op : PitArt.car(PitArt.ACCENTS[0], state, look, 3)) if (op.kind == PitArt.Op.PATH) ok(!PitPath.parse(op.path).isEmpty(), "an unreadable path: " + op.path);
                }
            StringBuilder first = new StringBuilder(), second = new StringBuilder();
            for (PitArt.Op op : PitArt.car(PitArt.LIME, PitArt.WORK, new PitArt.Look(), 0)) first.append(op.path).append(op.matrix == null ? "" : java.util.Arrays.toString(op.matrix));
            for (PitArt.Op op : PitArt.car(PitArt.LIME, PitArt.WORK, new PitArt.Look(), 1)) second.append(op.path).append(op.matrix == null ? "" : java.util.Arrays.toString(op.matrix));
            ok(!first.toString().equals(second.toString()), "working wheels don't turn");
            ok(PitArt.fps(PitArt.WORK, 0, false) == 4 && PitArt.fps(PitArt.WORK, 1, false) == 24 && PitArt.fps(PitArt.IDLE, 0, false) == 0 && PitArt.fps(PitArt.IDLE, 0, true) == 6 && PitArt.still(PitArt.DONE) == 0 && PitArt.fps(PitArt.DONE, 0, false) == 2 && !PitArt.once(PitArt.DONE) && PitArt.still(PitArt.END) == 7 && PitArt.still(PitArt.NEED) == 0 && PitArt.once(PitArt.END) && !PitArt.once(PitArt.WORK), "frame rates as on the PC");
            ok(PitArt.state("pit") == PitArt.PIT && PitArt.state("nonsense") == PitArt.IDLE && PitArt.car("gt") == PitArt.GT && PitArt.car("hatch") == PitArt.HATCH && PitArt.car("") == PitArt.SPORT && PitArt.pixelSize(150) == 3 && PitArt.pixelSize(48) == 2, "names");
            ok(PitArt.garage(PitArt.LIME, PitArt.NEED, true, 4, 0).size() > PitArt.garage(PitArt.LIME, PitArt.NEED, true, 4, 1).size() && PitArt.front(PitArt.LIME, true).size() > PitArt.front(PitArt.LIME, false).size(), "the beacon blinks, the headlights light");
        } });
        check("Claude thread names skip placeholders like the PC's, and say where they ran", new Action() { public void run() {
            for (String generic : new String[] { "", "Remote Control", "Remote Control session (from a project)", "New session", "Untitled", "Claude Code" }) ok(ClaudeText.placeholder(generic), "a placeholder counts as a title: " + generic);
            ok(!ClaudeText.placeholder("Remote control the TV") && !ClaudeText.placeholder("Add the export button"), "a real title counts as a placeholder");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"sessions\":[],\"threads\":["
                + "{\"id\":\"1b21871a-de34\",\"project\":\"alpha\",\"title\":\"Remote Control session (from a project)\",\"running\":false,\"where\":\"Remote Control\",\"lastActive\":1700000000},"
                + "{\"id\":\"t2\",\"project\":\"alpha\",\"title\":\"Add a dark mode toggle\",\"running\":true,\"where\":\"Remote Control\",\"since\":1699999640}]}}")));
            PcState.ClaudeThread placeholder = state.claude.thread("1b21871a-de34"), named = state.claude.thread("t2");
            ok(ClaudeText.threadTitle(placeholder, 1700003600, true).equals("Thread 1b2187") && ClaudeText.threadTitle(named, 1700000000, true).equals("Add a dark mode toggle"), "thread names");
            ok(ClaudeText.where(placeholder, 1700003600).equals("Remote Control · ended 1 h ago") && ClaudeText.where(named, 1700000000).equals("Remote Control · 6 min"), "where a thread ran");
        } });
        check("Claude tells Needs you, Your turn and Idle apart, and follows the PC's still flag and thread lanes", new Action() { public void run() {
            ok(ClaudeText.chip("need").equals("Needs you") && ClaudeText.chip("done").equals("Your turn") && ClaudeText.chip("idle").equals("Idle") && ClaudeText.state("done").equals("Your turn"), "chips");
            // The art: Your turn's brake glow in every frame, Idle's headlights off, Needs you's roof beacon in every frame.
            int turn = 0, idle = 0, need0 = 0, need5 = 0;
            for (PitArt.Op op : PitArt.car(PitArt.LIME, PitArt.DONE, new PitArt.Look(), 6)) if (op.fill == PitArt.alpha(0xFFF09A9A, 0.4) || op.fill == 0xFFFF8A8A) turn++;
            for (PitArt.Op op : PitArt.car(PitArt.LIME, PitArt.IDLE, new PitArt.Look(), 0)) if (op.fill == 0xFF4A5361 || op.fill == 0xFFFF8A8A) idle += op.fill == 0xFF4A5361 ? 1 : 100;
            for (PitArt.Op op : PitArt.car(PitArt.LIME, PitArt.NEED, new PitArt.Look(), 0)) if (op.fill == PitArt.AMBER) need0++;
            for (PitArt.Op op : PitArt.car(PitArt.LIME, PitArt.NEED, new PitArt.Look(), 5)) if (op.fill == PitArt.AMBER) need5++;
            ok(turn >= 2 && idle == 1 && need0 >= 3 && need5 >= 1, "the art doesn't tell the states apart: " + turn + " " + idle + " " + need0 + " " + need5);
            PcState state = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"still\":true,\"sessions\":[],\"threads\":[{\"id\":\"t1\",\"project\":\"alpha\",\"running\":true,\"state\":\"done\",\"pit\":\"done\"}],"
                + "\"lanes\":[{\"key\":\"C:\\\\Code\\\\alpha\",\"project\":\"alpha\",\"thread\":false,\"color\":\"alpha\",\"bins\":[1,2]},{\"key\":\"C:\\\\Code\\\\alpha\",\"project\":\"Fix the login\",\"thread\":true,\"color\":\"alpha\",\"bins\":[3]}]}}")));
            PcState.Claude c = state.claude;
            ok(c.still && c.lanes.size() == 2 && !c.lanes.get(0).thread && c.lanes.get(1).thread && c.lanes.get(1).color.equals("alpha") && c.lanes.get(1).project.equals("Fix the login") && c.lanes.get(1).bins[0] == 3, "still flag and thread lanes");
            ok(ClaudeText.summary(c, 1, false).equals("1 your turn"), "the summary counts your turn: " + ClaudeText.summary(c, 1, false));
            ok(!PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true}}"))).claude.still, "the cars stand still without being asked");
        } });
        check("Claude's Models column reads the PC's model names like the PC", new Action() { public void run() {
            PcState state = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"sessions\":[],\"threads\":[{\"id\":\"t1\",\"project\":\"alpha\",\"models\":{\"week\":[{\"name\":\"Fable 5.1\",\"share\":0.7},{\"name\":\"Sonnet 4.6\",\"share\":0.2},{\"name\":\"Haiku 4.5\",\"share\":0.1}],\"today\":[{\"name\":\"Opus 4.8\",\"share\":1}]}}],"
                + "\"groups\":{\"week\":[{\"key\":\"k\",\"project\":\"alpha\",\"threads\":[\"t1\"],\"models\":[{\"name\":\"Opus 4.8\",\"share\":0.97}]}]}}}")));
            PcState.ClaudeThread t = state.claude.thread("t1");
            java.util.List<String[]> mixed = ClaudeText.modelLabel(t.models[2]), one = ClaudeText.modelLabel(t.models[1]);
            ok(mixed.size() == 2 && mixed.get(0)[0].equals("Fable 70%") && mixed.get(1)[0].equals("Sonnet 20%") && mixed.get(1)[1].equals("Sonnet 4.6") && one.size() == 1 && one.get(0)[0].equals("Opus 4.8") && t.models[0].isEmpty(), "model labels");
            ok(ClaudeText.modelLabel(state.claude.groups[2].get(0).models).get(0)[0].equals("Opus 4.8") && ClaudeText.family("Fable 5.1") == 0 && ClaudeText.family("Sonnet 4.6") == 1 && ClaudeText.family("x") == -1, "group models and families");
        } });
        check("Claude pace, laps and the rev counter work out like the PC's", new Action() { public void run() {
            double now = 1700000000;
            PcState.ClaudeWindow five = new PcState.ClaudeWindow(); five.pct = 62; five.resetsAt = now + 100 * 60;
            double elapsed = ClaudePit.elapsed(five, ClaudePit.FIVE_HOURS, now);
            ok(Math.abs(elapsed - 2.0 / 3) < 1e-9 && Math.round(ClaudePit.projection(62, elapsed)) == 93 && ClaudePit.warns(93) && !ClaudePit.warns(89) && Double.isNaN(ClaudePit.projection(10, 0.04)) && ClaudePit.pace(92.4).equals("on pace for 92%"), "pace");
            PcState.ClaudeWindow unknown = new PcState.ClaudeWindow(); unknown.pct = 5;
            ok(Double.isNaN(ClaudePit.elapsed(unknown, ClaudePit.FIVE_HOURS, now)) && ClaudePit.lap(41, 1) == 1 && Math.abs(ClaudePit.lap(41, 2) - (0.41 - 2 / 7.0) * 7) < 1e-9 && ClaudePit.lap(41, 3) == 0 && ClaudePit.lapNow(0.62) == 4 && ClaudePit.lapNow(1) == 6, "laps");
            ok(ClaudePit.wheelFps(0.5) == 14 && ClaudePit.hot(0.75) && !ClaudePit.heavy(0.4) && ClaudePit.share(30000, 60000) == 0.5 && ClaudePit.fuel(90) && !ClaudePit.fuel(89), "the rev counter");
            ok(ClaudeText.added(1300).equals("+1.3k") && ClaudeText.removed(216).equals("−216") && ClaudeText.dollars(7.1).equals("7.10") && ClaudeText.mix(new double[] { 0.8, 0.2, 0 }).equals("Opus 80% · Sonnet 20%") && ClaudeText.chip("pit").equals("Compacting") && ClaudeText.share(0.914).equals("91%") && ClaudeText.share(Double.NaN).equals("—"), "pit words");
        } });
        check("Claude designs leave room for their text at 140%", new Action() { public void run() {
            // Line heights as the views lay them out: about 1.33 times the text size; captions are 12–13, unscaled.
            double t = 1.4;
            for (double[] size : new double[][] { { 360, 280 }, { 472, 310 }, { 639, 576 } }) {
                double height = size[1] - 48, head = Math.max(24, Math.ceil(15 * t * 1.34));
                double room = height - head - 10 - CardSizing.CLAUDE_MINI_HEIGHT - 6;
                ok(room >= CardSizing.claudeMediumRow(t), "Medium leaves no room for a garage at " + size[0] + " x " + size[1]);
                ok(CardSizing.claudeMediumRow(t) >= Math.round(15 * t) * 1.33 + 12 * 1.33, "a Medium row is too short for its text");
                ok(CardSizing.CLAUDE_MINI_HEIGHT >= 16 + 12 * 1.33 + 28 * 1.17 + 14, "the mini tiles are too short");
            }
            for (double[] size : new double[][] { { 640, 420 }, { 1216, 576 } }) {
                double width = size[0] - 48, height = size[1] - 48;
                double ledger = height - 48 - 12 - CardSizing.CLAUDE_PLAN_HEIGHT - 12 - Math.max(60, Math.round(16 * t) * 1.35 + 13 * 1.35 + 14) - 12;
                ok(ledger >= 24 + Math.max(52, Math.ceil(Math.round(17 * t) * 1.33 + 12 * 1.33 + 10)), "Large leaves no room for a garage at " + size[0] + " x " + size[1]);
                ok(CardSizing.CLAUDE_PLAN_HEIGHT >= 20 + 13 * 1.33 + 40 * 1.12 + 2 * 13 * 1.33, "the plan row is too short");
                ok(!CardSizing.claudeSide(width) || width - 16 - CardSizing.CLAUDE_SIDE_WIDTH >= 560, "the side panel squeezes the ledger at " + size[0]);
            }
            for (double[] size : new double[][] { { 260, 180 }, { 359, 279 } }) {
                double width = size[0] - 48, height = size[1] - 48;
                boolean meter = CardSizing.claudeSmallMeter(height, t);
                double room = height - (meter ? CardSizing.CLAUDE_SMALL_METER_HEIGHT : 0), car = CardSizing.claudeSmallCar(width, room);
                ok(car * 60 / 140 <= room + 0.5 && car <= width * 0.34 + 0.5 || car == 48, "the Small car leaves its card");
                double side = Math.round(17 * t) * 1.33 + 13 * 1.33 + 12 * 1.33 + 13 * 1.33;
                ok(side <= room + 0.5, "the Small text column overflows at " + size[0] + " x " + size[1]);
            }
        } });
        check("Claude words, meters and the claude section read like the PC's", new Action() { public void run() {
            ok(ClaudeText.tokens(950).equals("950") && ClaudeText.tokens(12400).equals("12.4k") && ClaudeText.tokens(1234567).equals("1.2M") && ClaudeText.tokens(3400000000L).equals("3.4B") && ClaudeText.tokens(999999).equals("999k") && ClaudeText.tokens(2000).equals("2k"), "tokens");
            ok(ClaudeText.stateWords("needs", 1000, 1120, false).equals("Needs you · 2 min") && ClaudeText.stateWords("working", 0, 10, false).equals("Working") && ClaudeText.stateWords("done", 0, 3 * 3600 + 5, true).equals("Your turn · 3 h") && ClaudeText.stateWords("ready", 0, 99, true).equals("Ready"), "state words");
            ok(ClaudeText.ask("Claude needs your permission to use Bash").equals("Wants to use Bash") && ClaudeText.ask("Pick one").equals("Pick one"), "asks");
            ok(ClaudeText.lit(23) == 2 && ClaudeText.lit(3) == 1 && ClaudeText.lit(0) == 0 && ClaudeText.lit(99) == 9 && ClaudeText.lit(100) == 10 && ClaudeText.warns(80) && !ClaudeText.warns(79.9), "meters");
            PcState.ClaudeWindow full = new PcState.ClaudeWindow(); full.pct = 100; full.resetsAt = 1000 + 3600;
            PcState.ClaudeWindow past = new PcState.ClaudeWindow(); past.pct = 90; past.resetsAt = 900;
            ok(ClaudeText.reset(full, 1000, true).startsWith("Limit reached · back ") && ClaudeText.percent(past, 1000) == 0 && ClaudeText.reset(past, 1000, false).equals("reset"), "resets");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"claudeNeeds\":2,\"claudePage\":1,\"claude\":{\"connected\":true,\"logsRead\":true,\"noPlan\":false,\"sessions\":[{\"id\":\"a\",\"project\":\"api\",\"title\":\"\",\"state\":\"needs\",\"since\":1000,\"message\":\"m\",\"model\":\"Opus\",\"canFocus\":true,\"tokens\":{\"fiveHour\":5,\"today\":6,\"week\":7}},{\"id\":\"\",\"state\":\"done\"},{\"id\":\"b\",\"project\":\"web\",\"state\":\"needs\"}],\"limits\":{\"fiveHour\":{\"pct\":23.5,\"resetsAt\":1738425600},\"week\":null,\"updatedAt\":1700000000,\"source\":\"online\"},\"usage\":{\"fiveHour\":[{\"project\":\"api\",\"tokens\":5}],\"today\":[],\"week\":[{\"project\":\"\",\"tokens\":1}]}}}")));
            PcState.Claude c = state.claude;
            ok(state.claudeNeeds == 2 && state.claudePage == 1 && c.connected && c.sessions.size() == 2 && c.sessions.get(0).tokens[2] == 7 && c.sessions.get(0).canFocus && c.sessions.get(0).shown().equals("api"), "sessions");
            ok(c.fiveHour.pct == 23.5 && c.fiveHour.resetsAt == 1738425600 && c.week == null && c.limitsKnown() && c.usage[0].size() == 1 && c.usage[2].isEmpty() && c.source.equals("online"), "limits and usage");
            ok(ClaudeText.summary(c, 1, false).equals("2 needs you") && ClaudeText.flag(c, 1700000000 + 60, true).equals("") && ClaudeText.flag(c, 1700000000 + 3600, true).startsWith("AS OF ") && ClaudeText.source(c, true).endsWith("checked online"), "summary and flag");
            PcState empty = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":false,\"noPlan\":true,\"plan\":\"current\",\"sessions\":[]}}")));
            ok(!empty.claude.limitsKnown() && ClaudeText.limitsMissing(empty.claude).equals("Plan limits need a Claude Pro or Max login.") && empty.claudePage == -1 && ClaudeText.summary(empty.claude, 1, true).equals("Not connected"), "empty");
        } });
        check("Claude project critters: threads, groups, folds and the polling interval", new Action() { public void run() {
            PcState state = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"logsRead\":true,\"every\":7,\"period\":\"Week\",\"sessions\":[],"
                + "\"threads\":[{\"id\":\"t1\",\"project\":\"alpha\",\"title\":\"Fix it\",\"running\":true,\"state\":\"working\",\"lastActive\":1700000000,\"tokens\":{\"fiveHour\":5,\"today\":6,\"week\":7}},"
                + "{\"id\":\"t2\",\"project\":\"alpha\",\"title\":\"\",\"running\":false,\"state\":\"ready\",\"lastActive\":1700000000}],"
                + "\"groups\":{\"fiveHour\":[],\"today\":[],\"week\":[{\"key\":\"C:\\\\Code\\\\alpha\",\"project\":\"alpha\",\"collapsed\":true,\"tokens\":7,\"cost\":0.5,\"threads\":[\"t1\",\"t2\",\"missing\"]}]}}}")));
            PcState.Claude c = state.claude;
            ok(c.every == 7 && ClaudeText.period(c.period) == 2 && ClaudeText.pollEvery(c) == 7 && ClaudeText.pollEvery(null) == 2, "the interval and period");
            c.every = 1; ok(ClaudeText.pollEvery(c) == 2, "the phone asks at most every 2 s");
            PcState.ClaudeGroup g = c.groups[2].get(0);
            ok(g.key.equals("C:\\Code\\alpha") && g.collapsed && g.threads.size() == 2 && c.groups[0].isEmpty(), "a group keeps only known threads");
            ok(ClaudeText.groupState(c, g).equals("working") && !ClaudeText.groupNeeds(c, g) && ClaudeText.groupDetail(c, g, 1700000000).equals("2 threads · 1 running") && ClaudeText.threads(1).equals("1 thread"), "project words");
            ok(ClaudeText.threadTitle(c.thread("t1"), 1700000000, true).equals("Fix it") && ClaudeText.threadTitle(c.thread("t2"), 1700000000, true).equals("Thread t2") && ClaudeText.noThreads(0).equals("No Claude Code threads in the last 5 hours."), "thread words");
            c.thread("t1").running = false; ok(ClaudeText.groupState(c, g).equals("ready"), "a project with nothing running sleeps");
            ok(ClaudeText.groupDetail(c, g, 1700000000 + 3600).equals("2 threads · ended 1 h ago") && ClaudeText.cars(2).equals("2 cars"), "an ended project's words");
        } });
        check("Claude review fixes on the phone: two-part Medium summary, compact project line, freshness stamp, title room, whole rows, Coral strip", new Action() { public void run() {
            double now = System.currentTimeMillis() / 1000.0;
            PcState.Claude c = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"sample\":true,\"plan\":\"current\",\"limits\":{\"fiveHour\":{\"pct\":40},\"week\":{\"pct\":60},\"updatedAt\":" + (now - 60) + ",\"source\":\"online\"},"
                + "\"sessions\":[{\"id\":\"a\",\"project\":\"x\",\"state\":\"needs\"},{\"id\":\"b\",\"project\":\"x\",\"state\":\"working\"}],"
                + "\"threads\":[{\"id\":\"a\",\"project\":\"x\",\"running\":true,\"state\":\"needs\",\"pit\":\"need\",\"where\":\"Desktop\"},{\"id\":\"b\",\"project\":\"x\",\"running\":true,\"state\":\"working\",\"pit\":\"work\",\"where\":\"Desktop\"},{\"id\":\"d\",\"project\":\"x\",\"running\":true,\"state\":\"done\",\"pit\":\"done\",\"where\":\"Desktop\"}],"
                + "\"groups\":{\"week\":[{\"key\":\"k\",\"project\":\"x\",\"threads\":[\"a\",\"b\",\"d\"],\"hidden\":1}]}}}"))).claude;
            ok(ClaudeText.summary(c, 2, false).split("·").length == 2 && ClaudeText.summary(c, 2, true).contains("this week"), "summary: " + ClaudeText.summary(c, 2, false));
            PcState.ClaudeGroup g = c.groups[2].get(0);
            ok(!ClaudeText.groupDetail(c, g, now, true).contains("Claude app") && ClaudeText.groupDetail(c, g, now).contains("Claude app") && ClaudeText.groupDetail(c, g, now, true).endsWith("+1 hidden"), "compact: " + ClaudeText.groupDetail(c, g, now, true));
            ok(ClaudeText.flag(c, now, false, true).startsWith("SAMPLE · LIVE · AS OF ") && ClaudeText.flag(c, now, false).equals("SAMPLE"), "flag: " + ClaudeText.flag(c, now, false, true));
            double narrow = 1216 - 48 - 16 - 300;
            ok(!CardSizing.claudeWhere(narrow, 1.4) && CardSizing.claudeCache(narrow, 1.4) && CardSizing.claudeWhere(narrow, 1) && !CardSizing.claudeCache(640 - 48, 1.4), "title room");
            ok(CardSizing.claudeWholeRows(new double[] { 52, 36, 36, 52 }, 150) == 124 && CardSizing.claudeWholeRows(new double[] { 80 }, 50) == 50 && CardSizing.claudeWholeRows(new double[0], 100) == 0, "whole rows");
            ok("Coral".equals(Inks.defaultInk("Claude")) && !Inks.inScope("Claude"), "Claude's strip ink");
        } });
        check("Claude's tools fact reads like the PC's, partial when telemetry saw fewer calls", new Action() { public void run() {
            PcState.Claude c = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"sessions\":[],\"facts\":{\"tools\":1878,\"asked\":5,\"telemetry\":true,\"askedPartial\":true,\"added\":426}}}"))).claude;
            ok(c.facts.tools == 1878 && c.facts.askedPartial && ClaudeText.toolsKey(c.facts).equals("TOOLS · 5 ASKED · PARTIAL"), "partial: " + ClaudeText.toolsKey(c.facts));
            c.facts.telemetry = false; ok(ClaudeText.toolsKey(c.facts).equals("TOOLS"), "no telemetry");
        } });
        check("Claude archived threads and hidden counts read like the PC's", new Action() { public void run() {
            PcState state = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"sessions\":[],"
                + "\"threads\":[{\"id\":\"t1\",\"project\":\"alpha\",\"running\":false,\"pit\":\"archived\",\"archived\":true,\"lastActive\":1700000000},"
                + "{\"id\":\"t2\",\"project\":\"alpha\",\"running\":false,\"pit\":\"end\",\"lastActive\":1700000000}],"
                + "\"groups\":{\"fiveHour\":[{\"key\":\"k\",\"project\":\"alpha\",\"threads\":[],\"hidden\":2,\"lastActive\":1700000000}],\"today\":[],"
                + "\"week\":[{\"key\":\"k\",\"project\":\"alpha\",\"threads\":[\"t2\",\"t1\"],\"hidden\":1}]}}}")));
            PcState.Claude c = state.claude;
            ok(c.thread("t1").archived && !c.thread("t2").archived && ClaudeText.chip("archived").equals("Archived") && PitArt.state("archived") == PitArt.END, "an archived thread");
            ok(ClaudeText.chip("resolved").equals("Resolved") && PitArt.state("resolved") == PitArt.END && ClaudeText.parked("resolved") && ClaudeText.parked("archived") && !ClaudeText.parked("end") && !ClaudeText.parked("done"), "a resolved thread");
            ok(ClaudeText.groupDetail(c, c.groups[2].get(0), 1700000000 + 3600).equals("2 threads · ended 1 h ago · +1 hidden"), "+N hidden: " + ClaudeText.groupDetail(c, c.groups[2].get(0), 1700000000 + 3600));
            ok(ClaudeText.groupDetail(c, c.groups[0].get(0), 1700000000).startsWith("2 hidden · Quiet · last used "), "a project whose threads are all hidden");
        } });
        check("Claude plan sign-in states, cost and extra windows read like the PC's", new Action() { public void run() {
            PcState state = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"logsRead\":true,\"plan\":\"failed\",\"sessions\":[{\"id\":\"a\",\"project\":\"api\",\"state\":\"working\",\"where\":\"Desktop\",\"tokens\":{\"week\":9},\"cost\":{\"fiveHour\":0.5,\"today\":1.25,\"week\":12.6}}],"
                + "\"limits\":{\"fiveHour\":{\"pct\":40},\"week\":{\"pct\":60},\"updatedAt\":1700000000,\"source\":\"online\",\"extra\":[{\"name\":\"Opus week\",\"pct\":12},{\"name\":\"\",\"pct\":3},{\"name\":\"Fable week\"}]},"
                + "\"usage\":{\"week\":[{\"project\":\"api\",\"tokens\":1800000,\"cost\":2.01}]}}}")));
            PcState.Claude c = state.claude;
            ok(c.plan.equals("failed") && c.sessions.get(0).where.equals("Desktop") && c.sessions.get(0).cost[1] == 1.25 && c.sessions.get(0).cost[2] == 12.6 && c.usage[2].get(0).cost == 2.01, "cost and where");
            ok(c.extra.size() == 1 && c.extra.get(0).name.equals("Opus week") && c.extra.get(0).pct == 12, "extra windows");
            ok(ClaudeText.source(c, true).startsWith("Plan limits as of ") && ClaudeText.old(c, 1700000000 + 60) && ClaudeText.flag(c, 1700000000 + 60, true).contains("AS OF "), "failed check shows old numbers");
            ok(ClaudeText.money(0.4219).equals("≈ $0.42") && ClaudeText.money(12.6).equals("≈ $13") && ClaudeText.money(1234).equals("≈ $1.2k") && ClaudeText.money(0).equals("≈ $0") && ClaudeText.usage(1800000, 2.01).equals("1.8M · ≈ $2.01"), "money");
            PcState.Claude signedOut = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"connected\":true,\"sessions\":[]}}"))).claude;
            PcState.Claude expired = PcState.parse(Json.obj(Json.parse("{\"claude\":{\"plan\":\"expired\",\"sessions\":[]}}"))).claude;
            ok(signedOut.plan.equals("signedOut") && ClaudeText.limitsMissing(signedOut).equals(ClaudeText.SIGN_IN) && ClaudeText.limitsMissing(expired).equals(ClaudeText.SIGN_IN_ENDED), "sign-in messages");
            // The same thresholds as the PC's CardSizing.
            ok(!CardSizing.claudeSmallMeter(132, 1.4) && CardSizing.claudeSmallMeter(152, 1), "small meter");
        } });
        check("Clipboard history picks the PC's designs and reads the PC's previews", new Action() { public void run() {
            ok(CardSizing.clip(240, 200) == CardSizing.SMALL && CardSizing.clip(472, 310) == CardSizing.MEDIUM && CardSizing.clip(439, 576) == CardSizing.MEDIUM && CardSizing.clip(440, 400) == CardSizing.LARGE, "designs");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"clipboard\":{\"watching\":true,\"paused\":false,\"items\":[{\"id\":\"a\",\"text\":\"Hello\",\"length\":5,\"at\":1000},{\"text\":\"no id\"}]}}")));
            ok(state.clipboard.watching && state.clipboard.items.size() == 1 && state.clipboard.items.get(0).length == 5 && ClipText.status(state.clipboard).equals("1"), "parse");
            ok(ClipText.ago(0, 30000).equals("just now") && ClipText.ago(0, 4 * 60000).equals("4 min ago") && ClipText.ago(0, 2 * 3600000L).equals("2 h ago") && ClipText.ago(0, 30 * 3600000L).equals("yesterday") && ClipText.length(1).equals("1 character"), "wording");
            state.clipboard.paused = true;
            ok(ClipText.status(state.clipboard).equals("PAUSED") && ClipText.empty(state.clipboard).startsWith("Paused"), "paused");
            ok(!WidgetLayout.find(WidgetLayout.defaults(), "Clipboard").visible && WidgetLayout.title("Clipboard").equals("Clipboard history"), "hidden by default");
        } });
        check("Macros read the PC's buttons and steps and keep six buttons a page", new Action() { public void run() {
            PcState state = PcState.parse(Json.obj(Json.parse("{\"macros\":{\"running\":1,\"step\":2,\"items\":[{\"label\":\"Stream\",\"glyph\":\"E722\",\"color\":\"Lavender\",\"confirm\":false,\"steps\":[\"Control  ·  Mute / unmute microphone\",\"Wait 0.5 s\"]},{\"label\":\"Break\",\"glyph\":\"E768\",\"confirm\":true,\"steps\":[\"Control  ·  Play / pause\"]}]}}")));
            ok(state.macros.running == 1 && state.macros.step == 2 && state.macros.items.size() == 2 && state.macros.items.get(0).glyph.equals("camera") && state.macros.items.get(1).confirm, "parse");
            ok(MacroText.kind(state.macros.items.get(0)).equals("2 steps") && MacroText.kind(state.macros.items.get(1)).equals("1 step  ·  asks first") && MacroText.detail(state.macros.items.get(0).steps).equals("1.  Control  ·  Mute / unmute microphone\n2.  Wait 0.5 s"), "wording");
            ok(PcState.parse(Json.obj(Json.parse("{\"macros\":{\"items\":[]}}"))).macros.running == -1, "nothing running");
            ok(!WidgetLayout.find(WidgetLayout.defaults(), "Macros").visible && WidgetLayout.keepsSix("Macros") && WidgetLayout.minimum("Macros")[0] == 288, "like Quick Launch");
        } });
        check("Calendar reads the PC's agenda and words it like the PC", new Action() { public void run() {
            ok(CardSizing.calendar(240, 152) == CardSizing.SMALL && CardSizing.calendar(472, 250) == CardSizing.MEDIUM && CardSizing.calendar(439, 576) == CardSizing.MEDIUM && CardSizing.calendar(440, 380) == CardSizing.LARGE && CardSizing.calendarNextHeight(1.4) == 128 && CardSizing.calendarNextHeight(1) == 99, "designs");
            // 2 Nov 2026 10:00 UTC, the PC three hours ahead (13:00 there).
            long now = java.time.LocalDateTime.of(2026, 11, 2, 10, 0).toEpochSecond(java.time.ZoneOffset.UTC) * 1000;
            String json = "{\"calendar\":{\"feeds\":2,\"read\":true,\"pcOffset\":180,\"failed\":[\"Work: The address was refused.\"],\"events\":["
                + "{\"title\":\"Meeting\",\"location\":\"Room 4\",\"start\":" + (now + 90 * 60000L) + ",\"end\":" + (now + 150 * 60000L) + ",\"allDay\":false,\"feed\":1},"
                + "{\"title\":\"Holiday\",\"start\":" + (now + 11 * 3600000L) + ",\"end\":" + (now + 35 * 3600000L) + ",\"allDay\":true,\"date\":\"2026-11-03\",\"endDate\":\"2026-11-04\",\"feed\":0},"
                + "{\"title\":\"Trip\",\"start\":" + (now + 72 * 3600000L) + ",\"end\":" + (now + 75 * 3600000L) + ",\"allDay\":false,\"feed\":0},{\"title\":\"no start\"}]}}";
            PcState.Calendar c = PcState.parse(Json.obj(Json.parse(json))).calendar;
            ok(c.feeds == 2 && c.read && c.events.size() == 3 && c.events.get(0).feed == 1 && c.events.get(0).location.equals("Room 4") && CalendarText.flag(c).equals("1 FEED FAILED"), "parse");
            ok(CalendarText.next(c.events, now) == c.events.get(0) && CalendarText.countdown(c.events.get(0), now).equals("in 1 h 30 m") && CalendarText.countdown(c.events.get(0), now + 105 * 60000L).equals("now  ·  45 m left"), "countdown");
            ok(CalendarText.when(c.events.get(0), 180, false).equals("2:30 PM – 3:30 PM") && CalendarText.when(c.events.get(0), 180, true).equals("14:30 – 15:30") && CalendarText.when(c.events.get(1), 180, false).equals("All day"), "times");
            ok(CalendarText.day(c.events.get(0), 180, now).equals("Today") && CalendarText.day(c.events.get(1), 180, now).equals("Tomorrow") && CalendarText.day(c.events.get(2), 180, now).equals("Thursday, Nov 5"), "days");
            ok(!WidgetLayout.find(WidgetLayout.defaults(), "Calendar").visible && WidgetLayout.title("Calendar").equals("Calendar"), "hidden by default");
        } });
        check("Games read the PC's library and word playtime like the PC", new Action() { public void run() {
            long now = 1800000000000L;
            PcState.Games games = PcState.parse(Json.obj(Json.parse("{\"games\":{\"read\":true,\"failed\":false,\"items\":[{\"id\":\"steam:100\",\"name\":\"Racer\",\"store\":\"Steam\",\"playtime\":2533,\"lastPlayed\":" + (now - 2 * 86400000L) + "},{\"id\":\"epic:Sugar\",\"name\":\"League\",\"store\":\"Epic\",\"playtime\":null,\"lastPlayed\":null},{\"id\":\"\",\"name\":\"no id\"}]}}"))).games;
            ok(games.read && games.items.size() == 2 && games.items.get(0).playtime == 2533 && games.items.get(1).playtime == null && games.items.get(1).lastPlayed == 0, "parse");
            ok(GameText.detail(games.items.get(0), now).equals("Steam  ·  42 h played  ·  2 days ago") && GameText.detail(games.items.get(1), now).equals("Epic") && GameText.playtime(0).equals("Not played yet") && GameText.playtime(35).equals("35 m played"), "wording");
            ok(GameText.storeColor("Steam") == 0xFF8ED8F8 && !WidgetLayout.find(WidgetLayout.defaults(), "Games").visible && WidgetLayout.keepsSix("Games"), "layout");
        } });
        check("System stats read the PC's sensors, say why one is missing, and size like the PC", new Action() { public void run() {
            PcState.System s = PcState.parse(Json.obj(Json.parse("{\"system\":{\"showCpu\":true,\"showGpu\":true,\"showRam\":true,\"showNetwork\":true,\"showUptime\":true,\"showCpuTemp\":true,\"showGpuTemp\":true,\"showVram\":true,\"showFps\":true,"
                + "\"cpu\":34,\"gpu\":91,\"gpuStatus\":\"Busiest engine\",\"memoryPercent\":52,\"memoryUsedGB\":16.1,\"memoryTotalGB\":31.1,\"download\":2400000,\"upload\":180000,\"uptimeMs\":3000000,"
                + "\"styles\":{\"Cpu\":\"Bars\",\"Network\":\"Ring\",\"Fps\":\"Gauge\"},\"fahrenheit\":false,\"cpuTemp\":null,\"cpuTempState\":\"NeedsFullSensors\",\"gpuTemp\":71,\"gpuTempState\":\"Working\",\"gpuName\":\"TEST FIXTURE GPU\","
                + "\"vramUsedGB\":6.2,\"vramTotalGB\":12,\"vramState\":\"Working\",\"fps\":143.6,\"fpsState\":\"Working\",\"fpsSource\":\"RTSS\",\"fpsApp\":\"FixtureGame\",\"fullSensors\":false}}"))).system;
            ok(s.showCpuTemp && s.showFps && s.anyShown() && SystemText.shownIds(s).size() == 9, "shown");
            ok(SystemText.style(s, "Cpu").equals("Bars") && SystemText.style(s, "Network").equals("Auto") && SystemText.style(s, "Fps").equals("Gauge") && SystemText.style(s, "Ram").equals("Auto"), "styles");
            ok(SystemText.resolve("Auto", "GpuTemp", CardSizing.MEDIUM).equals("Gauge") && SystemText.resolve("Auto", "Fps", CardSizing.MEDIUM).equals("Sparkline") && SystemText.resolve("Auto", "Cpu", CardSizing.LARGE).equals("Line"), "auto");
            java.util.Map<String, SystemText.Reading> r = SystemText.readings(s, 144);
            ok(r.get("CpuTemp").text.equals("—") && r.get("CpuTemp").shortReason.equals("Needs admin") && r.get("CpuTemp").longReason.equals("Needs admin: full-sensors mode"), "missing says why");
            ok(r.get("GpuTemp").text.equals("71°C") && r.get("Vram").text.equals("52%") && r.get("Vram").detail.equals("6.2 / 12 GB") && r.get("Gpu").warning && r.get("Fps").text.equals("144") && r.get("Fps").detail.equals("FixtureGame  ·  RTSS"), "wording " + r.get("Vram").detail);
            ok(SystemText.degrees(88, true).equals("190°F") && SystemText.fpsScale(java.util.Arrays.asList(130.0)) == 144, "units and scale");
            int[] list = CardSizing.statsListGrid(212, 196, 9, 1.4), cells = CardSizing.statsCellGrid(852, 248, 7, 1.4), tiles = CardSizing.statsTileGrid(1152, 492, 8, 1.4);
            ok(list[0] == 1 && list[2] == 2 && cells != null && cells[0] == 7 && tiles != null && tiles[0] == 4 && tiles[1] == 2 && CardSizing.statsCellGrid(424, 158, 7, 1.4) == null, "sizes " + list[0] + "," + list[2] + " tiles " + (tiles == null ? "-" : tiles[0] + "x" + tiles[1]));
        } });
        check("Smart home reads the PC's devices and steps thermostats like the PC", new Action() { public void run() {
            PcState.Home home = PcState.parse(Json.obj(Json.parse("{\"home\":{\"connected\":true,\"chosen\":3,\"read\":true,\"problem\":\"\",\"entities\":["
                + "{\"id\":\"light.desk\",\"name\":\"Desk\",\"domain\":\"light\",\"available\":true,\"on\":true,\"state\":\"On  ·  60%\",\"dimmable\":true,\"brightness\":60,\"current\":null,\"target\":null,\"min\":0,\"max\":0,\"step\":0},"
                + "{\"id\":\"climate.living\",\"name\":\"Living\",\"domain\":\"climate\",\"available\":true,\"on\":true,\"state\":\"21.5°  →  23°\",\"dimmable\":false,\"brightness\":null,\"current\":21.5,\"target\":29.8,\"min\":10,\"max\":30,\"step\":0.5},"
                + "{\"id\":\"\",\"name\":\"no id\"}]}}"))).home;
            ok(home.connected && home.read && home.chosen == 3 && home.devices.size() == 2, "parse");
            PcState.HomeDevice desk = home.devices.get(0), heat = home.devices.get(1);
            ok(desk.dimmable && desk.brightness == 60 && desk.target == null && heat.current == 21.5 && heat.max == 30 && heat.step == 0.5, "fields");
            ok(HomeText.stepped(heat, 1) == 30 && HomeText.stepped(heat, -1) == 29.5 && HomeText.degrees(23).equals("23°") && HomeText.degrees(21.5).equals("21.5°"), "steps");
            ok(HomeText.glyph(desk).equals("bulb") && HomeText.glyph(heat).equals("thermometer") && HomeText.status(home).isEmpty(), "glyphs");
            home.problem = "Home Assistant didn't answer."; ok(HomeText.status(home).equals("NOT UPDATING"), "status");
            PcState.Home off = new PcState.Home(); ok(HomeText.empty(off).startsWith("Connect Home Assistant") && !HomeText.status(off).equals("OFFLINE"), "not connected");
            ok(CardSizing.home(240, 152) == CardSizing.SMALL && CardSizing.home(472, 310) == CardSizing.MEDIUM && CardSizing.home(600, 400) == CardSizing.LARGE, "designs");
            int[] medium = CardSizing.homeGrid(424, 226, 1.4, 5), large = CardSizing.homeGrid(552, 316, 1.4, 5);
            ok(medium[0] == 2 && medium[1] == 2 && medium[2] == 1 && large[0] == 3 && large[1] == 2 && large[2] == 0, "grid " + medium[0] + "x" + medium[1] + " " + large[0] + "x" + large[1]);
            ok(!WidgetLayout.find(WidgetLayout.defaults(), "Home").visible && WidgetLayout.title("Home").equals("Smart home"), "hidden by default");
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
            ok(reference[0] == 360 && reference[1] == 360 && phone[0] == 287 && phone[1] == 345 && tiny[0] == 281 && tiny[1] == 281, "minimums " + phone[0] + "," + phone[1]);
            ok(WidgetLayout.transform("Spotify", new Box(0, 0, 600, 552), "SE", -1000, -1000, false, 1480, 552).equals(new Box(0, 0, 287, 345)), "resize minimum");
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
            ok(reference[0] == 3 && reference[1] == 2 && compact[0] == 3 && compact[1] == 2 && big[0] == 3 && big[1] == 2, "deck " + big[0] + "x" + big[1]);
            int[] six = CardSizing.deckGrid(640 - 42, 360 - 90, 6), many = CardSizing.deckGrid(640 - 42, 360 - 90, 40), four = CardSizing.deckGrid(430, 160, 4);
            ok(six[0] * six[1] == 6 && many[0] == 3 && many[1] == 2 && four[0] == 2 && four[1] == 2, "six buttons a page " + many[0] + "x" + many[1]);
            int[] wide = CardSizing.deckGrid(1500, 114), tall = CardSizing.deckGrid(246, 510), sounds = CardSizing.soundGrid(266, 116, 30), soundRow = CardSizing.soundGrid(900, 90, 30);
            ok(wide[0] == 6 && wide[1] == 1 && tall[0] == 1 && tall[1] == 6 && sounds[0] == 3 && sounds[1] == 2 && soundRow[0] == 6 && soundRow[1] == 1, "six-button shapes");
            ok(CardSizing.deckLabel(143, 80) == CardSizing.DECK_LABEL_BELOW && CardSizing.deckLabel(126, 57) == CardSizing.DECK_LABEL_BESIDE && CardSizing.deckLabel(82, 57) == CardSizing.DECK_LABEL_NONE, "deck labels");
            ok(java.util.Arrays.equals(WidgetLayout.minimum("Shortcuts", 1000, 400), WidgetLayout.minimum("Shortcuts")) && java.util.Arrays.equals(WidgetLayout.minimum("Soundboard", 1000, 400), WidgetLayout.minimum("Soundboard")) && WidgetLayout.minimum("Focus", 1000, 400)[1] < 192, "six-button cards keep their minimum");
            ok(CardSizing.deckGlyph(80, false) == 24 && CardSizing.deckGlyph(100, true) == 18 && CardSizing.deckGlyph(400, false) == 44, "glyphs");
            ok(CardSizing.focusTime(320, 576) == 84 && CardSizing.focusTime(296, 300) == 42 && CardSizing.focusTime(600, 900) > 84, "countdown");
            ok(CardSizing.mediaArt(472, 310) == 104 && CardSizing.mediaArt(600, 500) > 104 && CardSizing.mediaArt(2000, 2000) == 220, "cover");
            ok(CardSizing.weatherTemperature(CardSizing.MEDIUM, 472, 88) == 71 && CardSizing.weatherTemperature(CardSizing.SMALL, 232, 60) == 54 && CardSizing.weatherTemperature(CardSizing.LARGE, 400, 60) == 54, "temperature");
            ok(CardSizing.spotifyColumns(560) == 1 && CardSizing.spotifyColumns(752) == 2 && CardSizing.spotifyColumns(3000) == 3, "Spotify");
        } });
        check("Palettes and accents match the Windows resources", new Action() { public void run() {
            Theme midnight = Theme.of("Midnight", "Lime"), slate = Theme.of("Slate", "Glacier");
            ok(midnight.page == 0xFF0C0E13 && midnight.accent == 0xFFBAF28B && slate.surface == 0xFF1B2938 && slate.accent == 0xFF8ED8F8 && Theme.of("Warm", "Amber").text == 0xFFFAF3E9, "colors");
        } });

        // Sticker Sheet foundations (decision 021, docs/design/sticker-sheet.md).
        check("Sticker inks, defaults and strip names agree with the PC's", new Action() { public void run() throws Exception {
            String path = System.getProperty("widgetscreen.sample");
            File inks = path == null ? null : new File(new File(path).getParentFile(), "ink-defaults.json");
            if (inks == null || !inks.exists()) throw new AssertionError("No ink-defaults.json. Run scripts\\build.ps1 -Test first.");
            Map<String, Object> pc = Json.obj(Json.parse(new String(Files.readAllBytes(inks.toPath()), StandardCharsets.UTF_8)));
            ok(Arrays.asList(Theme.INKS).equals(Arrays.asList("Lime", "Glacier", "Lavender", "Amber", "Coral", "Mint", "Rose", "Lemon")), "ink names");
            for (String name : Theme.INKS) ok(Theme.ink(name) == (0xFF000000 | Integer.parseInt(Json.field(Json.obj(Json.value(pc, "hex")), name).substring(1), 16)), name + " differs from the PC");
            Map<String, Object> defaults = Json.obj(Json.value(pc, "defaults")), strips = Json.obj(Json.value(pc, "strips"));
            for (String id : WidgetLayout.IDS) {
                ok(Inks.inScope(id) == defaults.containsKey(id), id + " in scope differs from the PC");
                if (!Inks.inScope(id)) continue;
                ok(Inks.defaultInk(id).equals(Json.field(defaults, id)), id + "'s default ink differs from the PC");
                ok((Inks.stripName(id) + "|" + Inks.shortName(id)).equals(Json.field(strips, id)), id + "'s strip names differ from the PC");
            }
            ok(!Inks.inScope("Claude") && !Arrays.asList(Inks.CHOICES).contains("Amber"), "Claude Code or Amber");
        } });
        check("Widget colours: Accent only gives every card the accent; inks keep in the phone's settings", new Action() { public void run() {
            Map<String, String> chosen = Inks.parse("Focus=Mint;Media=Lemon;Claude=Rose;Notes=Amber");
            for (String accent : Theme.ACCENTS) for (String id : WidgetLayout.IDS) ok(Inks.resolve(id, chosen, Inks.ACCENT_ONLY, accent).equals(accent), id + " under Accent only");
            ok(Inks.format(chosen).equals("Focus=Mint;Media=Lemon") && Inks.resolve("Media", chosen, Inks.EACH_OWN, "Lime").equals("Lemon") && Inks.resolve("Games", chosen, Inks.EACH_OWN, "Lime").equals("Lime"), "chosen inks");
            AppSettings s = new AppSettings(); s.inks = "Spotify=Rose;Media=Rose"; s.widgetColours = "Accent"; s.accent = "Glacier";
            AppSettings copy = s.copy();
            ok(copy.inks.equals("Spotify=Rose") && copy.widgetColours.equals("Accent") && copy.ink("Focus") == Theme.INK_GLACIER, "settings round trip");
            copy.widgetColours = "Each"; ok(copy.ink("Spotify") == Theme.INK_ROSE && copy.ink("Focus") == Theme.INK_CORAL, "Each its own");
        } });
        check("The PC's layout section brings its inks and Widget colours to the phone", new Action() { public void run() throws Exception {
            String path = System.getProperty("widgetscreen.sample");
            PcState state = PcState.parse(Json.obj(Json.parse(new String(Files.readAllBytes(new File(path).toPath()), StandardCharsets.UTF_8))));
            ok(state.layout != null && state.layout.inks != null && "Mint".equals(state.layout.inks.get("Focus")) && "Rose".equals(state.layout.inks.get("Media")) && !state.layout.inks.containsKey("Claude") && "Each".equals(state.layout.colours), "inks in the sample");
            AppSettings s = new AppSettings(); s.mirror(state.layout);
            ok(s.inks.equals("Focus=Mint") && s.ink("Focus") == Theme.INK_MINT && s.ink("Mixer") == Theme.INK_LIME, "mirrored inks");
        } });
        check("Status stickers and label strips follow the PC's state words and text size", new Action() { public void run() {
            ok(Inks.stateOf("UNAVAILABLE", true) == Inks.UNAVAILABLE && Inks.stateOf("PC OFFLINE", false) == Inks.OFFLINE && Inks.stateOf("…", false) == Inks.LOADING && Inks.stateOf("C: LOW", true) == Inks.WARNING && Inks.stateOf("3 TO DO", false) == Inks.FACT, "states");
            ok(Inks.stripTextSize(1) == 13 && Inks.stripTextSize(1.4) == 18 && Inks.stripTextSize(3) == 18 && Inks.stripTextSize(0.5) == 11.5, "strip text size");
            ok(Inks.compact(199) && !Inks.compact(200) && Inks.shortName("Clipboard").equals("CLIPS"), "compact strips");
        } });

        // Sticker Sheet phase 2, the default five (docs/design/sticker-sheet.md): the same values as the PC's DefaultFiveTests.
        check("Focus facts read like the PC: today's minutes, the next break and the status sticker", new Action() { public void run() {
            ok(Format.focusMinutes(0).equals("0 min") && Format.focusMinutes(50).equals("50 min") && Format.focusMinutes(100).equals("1 h 40 min"), "minutes");
            LocalDateTime now = LocalDateTime.of(2026, 11, 1, 15, 0, 0);
            ok(Format.focusBreak(true, 25 * 60000, 25 * 60000, now, false).equals("3:25 PM") && Format.focusBreak(true, 25 * 60000, 25 * 60000, now, true).equals("15:25"), "running break " + Format.focusBreak(true, 25 * 60000, 25 * 60000, now, false));
            ok(Format.focusBreak(false, 25 * 60000, 25 * 60000, now, false).equals("In 25 min") && Format.focusBreak(false, (long) (10.2 * 60000), 25 * 60000, now, false).equals("In 11 min"), "stopped break");
            ok(Format.focusState(false, true, false, 3).equals("DONE") && Format.focusState(true, false, false, 3).equals("FOCUSING") && Format.focusState(false, false, true, 0).equals("PAUSED") && Format.focusState(false, false, false, 2).equals("2 TODAY") && Format.focusState(false, false, false, 0).isEmpty(), "sticker words");
        } });
        check("Keycap legends, mixer strips, device names and cover tints match the PC", new Action() { public void run() {
            ok("CTRL+SHIFT+M".equals(Format.hotkeyLegend("Hotkey  ·  Ctrl+Shift+M")) && Format.hotkeyLegend("Website  ·  example.com") == null && Format.hotkeyLegend("App") == null, "hotkey legend");
            ok(CardSizing.mixerStripSlots(456) == 3 && CardSizing.mixerStripSlots(820) == 6 && CardSizing.mixerStripSlots(300) == 1 && CardSizing.mixerStripSlots(200) == 0, "strip slots");
            ok(Format.shortDevice("Speakers (Logi Z407)").equals("Z407") && Format.shortDevice("Headset").equals("Headset") && Format.shortDevice("Realtek (R) Audio").equals("Audio") && Format.shortDevice("").isEmpty(), "device names");
            ok(Theme.parseTint("#7A4FB0") == 0xFF7A4FB0 && Theme.parseTint("") == 0 && Theme.parseTint("nope") == 0, "tint parse");
            ok(Theme.blend(0xFF161A22, 0xFFFF0000, 0.22) == 0xFF49141B, "blend " + Integer.toHexString(Theme.blend(0xFF161A22, 0xFFFF0000, 0.22)));
            ok(CardSizing.ledLit(0, 10) == 0 && CardSizing.ledLit(1, 10) == 10 && CardSizing.ledLit(0.25, 10) == 5 && CardSizing.ledLit(0.01, 10) == 1, "LED segments");
            ok(CardSizing.pipsShown(0) == 4 && CardSizing.pipsShown(6) == 6 && CardSizing.pipsShown(20) == 8, "pips shown");
        } });
        check("The phone link brings the album, the cover tint and Spotify captions", new Action() { public void run() {
            PcState state = PcState.parse(Json.obj(Json.parse("{\"media\":{\"available\":true,\"title\":\"Golden Hour\",\"album\":\"Night Lines\",\"tint\":\"#7A4FB0\"}}")));
            ok(state.media != null && state.media.album.equals("Night Lines") && Theme.parseTint(state.media.tint) == 0xFF7A4FB0, "media album and tint");
            com.alialyami.widgetscreen.core.Spotify.Item item = PcState.item(Json.parse("{\"id\":\"1\",\"uri\":\"spotify:album:4aawyAB9vmqN3uQ7FjRGTy\",\"name\":\"Dune Sea\",\"subtitle\":\"Lumen\",\"caption\":\"11 songs · 2024\"}"));
            ok(item != null && item.caption.equals("11 songs · 2024"), "Spotify caption");
            String path = System.getProperty("widgetscreen.sample");
            if (path != null && new File(path).exists()) {
                try {
                    PcState sample = PcState.parse(Json.obj(Json.parse(new String(Files.readAllBytes(new File(path).toPath()), StandardCharsets.UTF_8))));
                    ok(sample.media == null || sample.media.error || (sample.media.album != null && sample.media.tint != null), "sample media fields");
                } catch (java.io.IOException e) { throw new AssertionError(e.toString()); }
            }
        } });

        // Sticker Sheet phase 3, the readings (docs/design/sticker-sheet.md): the same values as the PC's ReadingsTests.
        check("System stats: processor and graphics card names shorten like the PC", new Action() { public void run() {
            ok(SystemText.shortModel("AMD Ryzen 7 7800X3D 8-Core Processor").equals("Ryzen 7 7800X3D"), "Ryzen " + SystemText.shortModel("AMD Ryzen 7 7800X3D 8-Core Processor"));
            ok(SystemText.shortModel("Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz").equals("i7-12700K"), "Intel " + SystemText.shortModel("Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz"));
            ok(SystemText.shortModel("NVIDIA GeForce RTX 4070").equals("RTX 4070") && SystemText.shortModel("AMD Radeon RX 7900 XTX").equals("Radeon RX 7900 XTX"), "graphics");
            ok(SystemText.shortModel("AMD Ryzen 5 5600G with Radeon Graphics").equals("Ryzen 5 5600G") && SystemText.shortModel(null).isEmpty() && SystemText.shortModel("  ").isEmpty(), "APU and empty");
        } });
        check("System stats: the peak sticker, temperature badges and min/max lines match the PC", new Action() { public void run() {
            PcState state = PcState.parse(Json.obj(Json.parse("{\"system\":{\"cpu\":41,\"gpu\":92,\"gpuName\":\"NVIDIA GeForce RTX 4070\",\"cpuName\":\"AMD Ryzen 7 7700 8-Core Processor\",\"memoryPercent\":55,\"memoryUsedGB\":17.6,\"memoryTotalGB\":32,\"cpuTemp\":58.4,\"gpuTemp\":71,\"showCpu\":true,\"showGpu\":true,\"showRam\":true}}")));
            ok(state.system != null && state.system.cpuName.equals("AMD Ryzen 7 7700 8-Core Processor"), "cpuName parsed");
            Map<String, SystemText.Reading> readings = SystemText.readings(state.system, 60);
            ok(readings.get("Cpu").detail.equals("Ryzen 7 7700") && readings.get("Gpu").detail.equals("RTX 4070"), "details " + readings.get("Cpu").detail + " / " + readings.get("Gpu").detail);
            String peak = SystemText.peakId(readings, state.system);
            ok("Gpu".equals(peak) && SystemText.peakText(peak, readings.get(peak)).equals("GPU 92%") && readings.get(peak).percent >= SystemText.STRAINED, "peak " + peak);
            ok(SystemText.badge(readings.get("CpuTemp")).equals("58°") && SystemText.badge(new SystemText.Reading()).isEmpty(), "badges");
            ok(SystemText.range("Cpu", Arrays.asList(12.2, 87.4, 40.0), false).equals("min 12% · max 87%"), "percent range");
            ok(SystemText.range("CpuTemp", Arrays.asList(41.0, 42.4), true).equals("min 106° · max 108°"), "temperature range " + SystemText.range("CpuTemp", Arrays.asList(41.0, 42.4), true));
            ok(SystemText.range("Fps", Arrays.asList(58.6, 144.0), false).equals("min 59 · max 144") && SystemText.range("Ram", Arrays.asList(50.0), false).isEmpty() && SystemText.range("Ram", null, false).isEmpty(), "fps and short ranges");
            String path = System.getProperty("widgetscreen.sample");
            if (path != null && new File(path).exists()) {
                try {
                    PcState sample = PcState.parse(Json.obj(Json.parse(new String(Files.readAllBytes(new File(path).toPath()), StandardCharsets.UTF_8))));
                    ok(sample.system == null || sample.system.error || sample.system.cpuName != null, "sample cpuName");
                } catch (java.io.IOException e) { throw new AssertionError(e.toString()); }
            }
        } });
        final String weatherSample = "{\"utc_offset_seconds\":0,\"current\":{\"temperature_2m\":10.2,\"apparent_temperature\":8.1,\"wind_speed_10m\":14,\"weather_code\":3,\"relative_humidity_2m\":82},"
            + "\"daily\":{\"temperature_2m_max\":[11.4],\"temperature_2m_min\":[8.6],\"uv_index_max\":[1.2],\"sunrise\":[\"2026-11-01T06:53\"],\"sunset\":[\"2026-11-01T16:33\"]},"
            + "\"hourly\":{\"time\":[\"2026-11-01T20:00\",\"2026-11-01T21:00\",\"2026-11-01T22:00\",\"2026-11-01T23:00\",\"2026-11-02T00:00\",\"2026-11-02T01:00\",\"2026-11-02T02:00\",\"2026-11-02T03:00\",\"2026-11-02T04:00\",\"2026-11-02T05:00\",\"2026-11-02T06:00\",\"2026-11-02T07:00\",\"2026-11-02T08:00\",\"2026-11-02T09:00\",\"2026-11-02T10:00\"],"
            + "\"temperature_2m\":[10.6,10.2,10,10.4,11,11.3,11,10.5,10,9.6,9.2,9,9.4,10.1,10.8],\"weather_code\":[3,3,3,61,61,61,3,3,2,2,1,0,0,0,1],\"precipitation_probability\":[5,10,20,45,60,40,20,10,5,0,0,0,0,0,null]}}";
        check("Weather: one Open-Meteo request brings humidity, rain chances, UV, sunrise and sunset, as on the PC", new Action() { public void run() {
            String url = Weather.forecastUrl(51.5, -0.12, false);
            for (String part : new String[] { "relative_humidity_2m", "precipitation_probability", "uv_index_max", "sunrise", "sunset" }) ok(url.contains(part), "request lacks " + part);
            Weather.Snapshot s = Weather.parse(weatherSample, 0, "2026-11-01T21:00");
            ok(s.humidity == 82 && Math.abs(s.uv - 1.2) < 0.001 && "2026-11-01T06:53".equals(s.sunrise) && "2026-11-01T16:33".equals(s.sunset) && s.utcOffsetSeconds == 0, "phase 3 fields");
            ok(s.curve.size() == 13 && s.curve.get(0).time.equals("2026-11-01T21:00") && s.curve.get(0).rain == 10 && s.curve.get(12).rain == 0 && s.hours.size() == 5, "curve " + s.curve.size());
            Weather.Snapshot none = Weather.parse("{\"current\":{\"temperature_2m\":20,\"apparent_temperature\":19,\"wind_speed_10m\":8,\"weather_code\":0}}", 0);
            ok(none.humidity == null && none.uv == null && none.sunrise == null && none.curve.isEmpty(), "missing extras");
        } });
        check("Weather: chips, UV words, sky inks, the sun's place and the curve match the PC", new Action() { public void run() {
            Weather.Snapshot s = Weather.parse(weatherSample, 0, "2026-11-01T21:00");
            StringBuilder three = new StringBuilder();
            for (String[] c : Weather.chips(s, false, 3)) three.append(three.length() == 0 ? "" : "|").append(c[0]).append('=').append(c[1]);
            ok(three.toString().equals("FEELS=8°|HUMIDITY=82%|RAIN=10%"), "medium chips " + three);
            String[][] six = Weather.chips(s, true, 6);
            ok(six.length == 6 && six[3][0].equals("UV") && six[4][1].equals("14 mph") && six[5][0].equals("HIGH / LOW"), "large chips");
            ok(Weather.uvText(1.2).equals("1 Low") && Weather.uvText(5.4).equals("5 Moderate") && Weather.uvText(7).equals("7 High") && Weather.uvText(9.6).equals("10 Very high") && Weather.uvText(11).equals("11 Extreme"), "UV words");
            ok(Weather.skyInk("sun", true).equals("Lemon") && Weather.skyInk("rain", true).equals("Glacier") && Weather.skyInk("storm", true).equals("Lavender") && Weather.skyInk("sun", false).equals("Lavender"), "sky inks");
            ok(Weather.skyStrength("sun", true) == 0.42 && Weather.skyStrength("cloud", true) == 0.22 && Weather.skyStrength("sun", false) == 0.32, "sky strengths");
            ok(Weather.sunPart("2026-11-01T12:00", "2026-11-01T06:00", "2026-11-01T18:00") == 0.5 && Weather.sunPart("2026-11-01T03:00", "2026-11-01T06:00", "2026-11-01T18:00") < 0 && Weather.sunPart("2026-11-02T01:00", "2026-11-01T06:00", "2026-11-01T18:00") > 1 && Weather.sunPart("2026-11-01T12:00", null, "2026-11-01T18:00") == null, "sun part");
            ok(Weather.isDay("2026-11-01T12:00", "2026-11-01T06:00", "2026-11-01T18:00") && !Weather.isDay("2026-11-01T21:00", "2026-11-01T06:00", "2026-11-01T18:00") && Weather.isDay("2026-11-01T08:00", null, null) && !Weather.isDay("2026-11-01T20:00", null, null), "day and night");
            ok(Weather.cityNow(s, 1793566800000L).equals("2026-11-01T21:00"), "city clock " + Weather.cityNow(s, 1793566800000L));
            ok(Arrays.equals(Weather.curveLevels(new double[] { 10, 12, 14 }), new double[] { 0, 0.5, 1 }) && Weather.curveLevels(new double[] { 10, 10.2 })[1] == 0.5 && Weather.curveLevels(new double[0]).length == 0, "curve levels");
            ok(Weather.clockTime("2026-11-01T06:53", false).equals("6:53 AM") && Weather.clockTime("2026-11-01T16:33", true).equals("16:33"), "clock times");
            ok(CardSizing.weatherChips(472) == 3 && CardSizing.weatherChips(560) == 4 && CardSizing.weatherChips(900) == 6, "chip counts");
        } });
        check("Storage: jars fill by used space like the PC's", new Action() { public void run() {
            ok(CardSizing.jarWidth(100) == 72 && CardSizing.jarLevel(0) == 0 && CardSizing.jarLevel(1) == 0.03 && CardSizing.jarLevel(85) == 0.85 && CardSizing.jarLevel(120) == 1 && CardSizing.jarLevel(Double.NaN) == 0, "jar sizes and levels");
        } });
        check("World clocks: faces, Day or Evening, hands and day fractions match the PC", new Action() { public void run() {
            ok(WorldClockText.ink(LocalDateTime.of(2026, 11, 1, 7, 0)).equals("Lemon") && WorldClockText.ink(LocalDateTime.of(2026, 11, 1, 18, 59)).equals("Lemon") && WorldClockText.ink(LocalDateTime.of(2026, 11, 1, 19, 0)).equals("Lavender") && WorldClockText.ink(LocalDateTime.of(2026, 11, 1, 6, 59)).equals("Lavender"), "inks");
            ok(WorldClockText.partOfDay(LocalDateTime.of(2026, 11, 1, 12, 0)).equals("Day") && WorldClockText.partOfDay(LocalDateTime.of(2026, 11, 1, 3, 0)).equals("Evening"), "day and evening");
            ok(WorldClockText.hourAngle(LocalDateTime.of(2026, 11, 1, 15, 30)) == 105 && WorldClockText.minuteAngle(LocalDateTime.of(2026, 11, 1, 15, 30)) == 180 && WorldClockText.hourAngle(LocalDateTime.of(2026, 11, 1, 0, 0)) == 0, "hands");
            ok(WorldClockText.dayFraction(LocalDateTime.of(2026, 11, 1, 12, 0)) == 0.5 && WorldClockText.dayFraction(LocalDateTime.of(2026, 11, 1, 18, 0)) == 0.75, "day fraction");
            ok(CardSizing.clockListFace(300, 40) == 34 && CardSizing.clockListFace(240, 60) == 0 && CardSizing.clockFace(140, 166) == 42 && CardSizing.clockTileFace(220, 300) == 64 && CardSizing.clockStackedBand(120) && !CardSizing.clockStackedBand(90) && CardSizing.clockColumnFaces(166) && !CardSizing.clockColumnFaces(116), "face sizes");
        } });
        check("Prayer: the sun path places prayers by time, the chips and the alert words match the PC", new Action() { public void run() {
            double fajr = 1793583660000.0, isha = fajr + 14 * 3600000.0;
            ok(PrayerText.pathPart(fajr, fajr, isha) == 0 && PrayerText.pathPart(isha, fajr, isha) == 1 && PrayerText.pathPart(fajr + 7 * 3600000.0, fajr, isha) == 0.5 && PrayerText.pathPart(fajr - 3600000.0, fajr, isha) < 0 && Double.isNaN(PrayerText.pathPart(fajr, isha, fajr)), "path parts");
            ok(PrayerText.methodShort("Umm al-Qura, Makkah").equals("Umm al-Qura") && PrayerText.methodShort("Muslim World League").equals("Muslim World League") && PrayerText.methodShort(null).isEmpty(), "method names");
            ok(CardSizing.prayerPathRoom(166, 47) == 57 && CardSizing.prayerSunriseRow(132) && !CardSizing.prayerSunriseRow(120) && CardSizing.prayerPathTop(267, false) == 120 && CardSizing.prayerPathTop(500, false) == 180, "prayer sizes");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"prayer\":{\"located\":true,\"alert\":\"Chime\",\"nextAt\":1,\"times\":[1,2,3,4,5]}}")));
            ok(state.prayer != null && state.prayer.alert.equals("Chime"), "alert words from the PC");
            String path = System.getProperty("widgetscreen.sample");
            if (path != null && new File(path).exists()) {
                try {
                    PcState sample = PcState.parse(Json.obj(Json.parse(new String(Files.readAllBytes(new File(path).toPath()), StandardCharsets.UTF_8))));
                    ok(sample.prayer == null || !sample.prayer.located || sample.prayer.alert.length() > 0, "sample alert");
                } catch (java.io.IOException e) { throw new AssertionError(e.toString()); }
            }
        } });
        // Sticker Sheet phase 5, the Soundboard: the same words as the PC's SoundboardPadTests.
        check("Soundboard: the voice sticker, pad numbers and sources match the PC", new Action() { public void run() {
            ok(SoundboardText.voice(false, false, false, null).equals("NOT SET UP") && SoundboardText.voice(true, true, true, "General").equals("UNAVAILABLE"), "not set up, unavailable");
            ok(SoundboardText.voice(true, true, false, "General").equals("General") && SoundboardText.voice(true, true, false, "").equals("NOT IN VOICE") && SoundboardText.voice(true, false, false, null).equals("…"), "in voice, not in voice, connecting");
            ok(Inks.stateOf("NOT SET UP", false) == Inks.UNAVAILABLE && Inks.stateOf("…", false) == Inks.LOADING && Inks.stateOf("General", false) == Inks.FACT, "sticker looks");
            ok(SoundboardText.padNumber(0).equals("01") && SoundboardText.padNumber(11).equals("12") && SoundboardText.padNumber(99).equals("100") && SoundboardText.FLASH_MS == 150, "pad numbers and flash");
            ok(SoundboardText.source(true, false).equals("Discord sound") && SoundboardText.source(false, true).equals("From another server") && SoundboardText.source(false, false).equals("This server"), "sources");
            PcState state = PcState.parse(Json.obj(Json.parse("{\"discord\":{\"connected\":true,\"linked\":false,\"failed\":true,\"status\":\"Open Discord on this PC to play sounds.\",\"sounds\":[]}}")));
            ok(state.discord != null && state.discord.failed && SoundboardText.voice(true, state.discord.linked, state.discord.failed, state.discord.channel).equals("UNAVAILABLE"), "failed from the PC");
        } });
        System.out.println(passes + " passed, " + failures + " failed.");
        System.exit(failures == 0 ? 0 : 1);
    }
}
