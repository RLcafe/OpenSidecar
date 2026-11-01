package com.alialyami.widgetscreen.core;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

// What the PC sends back for GET /state: only the sections the phone asked for (one per visible widget).
// A missing section stays null; a section the PC couldn't read is marked unavailable.
public final class PcState {
    public static final class Focus {
        public double remainingMs, durationMs, progress;
        public boolean running, paused, finished, chime;
        public int customMinutes, sessionsToday, minutesToday;
    }

    public static final class Media {
        public boolean error, available, playing, canPlayPause, canPrevious, canNext, canSeek, art;
        public String title = "", artist = "", source = "", key = "", app = "";
        public double positionMs, durationMs;
    }

    public static final class Device { public String id, name; public boolean isDefault; public int formFactor; }
    public static final class Session { public String id, name, deviceId; public long processId; public double volume; public boolean muted; }
    public static final class Audio {
        public boolean available, muted, sessionsAvailable, canRoute;
        public Boolean microphoneMuted;
        public String device = "", deviceId = "";
        public double volume;
        public final List<Device> devices = new ArrayList<Device>();
        public final List<Session> sessions = new ArrayList<Session>();
        public Device find(String id) { for (Device d : devices) if (d.id != null && d.id.equalsIgnoreCase(id)) return d; return null; }
        // The app's mixer row: its process name and the app's short name share a word (DashboardController.MatchSession).
        public Session sessionFor(String appName) {
            if (appName == null || appName.trim().isEmpty()) return null;
            String wanted = appName.replace(" ", "").toLowerCase(java.util.Locale.ROOT);
            for (Session s : sessions) {
                if (s.processId == 0 || s.name == null || s.name.isEmpty()) continue;
                String name = s.name.replace(" ", "").toLowerCase(java.util.Locale.ROOT);
                if (name.contains(wanted) || wanted.contains(name)) return s;
            }
            return null;
        }
    }

    public static final class System {
        public boolean error, showCpu, showGpu, showRam, showNetwork, showUptime;
        public Double cpu, gpu, memoryPercent, download, upload;
        public double memoryUsedGB, memoryTotalGB, uptimeMs;
        public String gpuStatus = "";
        public boolean anyShown() { return showCpu || showGpu || showRam || showNetwork || showUptime; }
    }

    public static final class Button { public String label, glyph, action, describe, color = "", kind = ""; public boolean confirm, enabled, on; }

    public static final class Weather { public String location = ""; public double latitude, longitude; public boolean imperial; }

    // The PC's fixed drives and its low-space threshold (docs/design/storage.md).
    public static final class Drive { public String name = "", label = ""; public double total, free; }
    public static final class Storage {
        public boolean error;
        public int lowPercent = 10;
        public final List<Drive> drives = new ArrayList<Drive>();
    }

    // The PC's world clocks (docs/design/world-clocks.md): each city and its offset from UTC now, and the PC's own offset.
    public static final class Clock { public String label = ""; public int offset; }
    public static final class Clocks {
        public int pcOffset;
        public final List<Clock> clocks = new ArrayList<Clock>();
    }

    // The PC's prayer times (docs/design/prayer-times.md): today's five as moments, the next and the current prayer.
    public static final class Prayer {
        public boolean located, alerting;
        public String city = "", method = "", hijri = "";
        public int pcOffset, next, current;
        public double nextAt, previousAt, sunrise;
        public final double[] times = new double[5];
    }

    // The PC's notes (docs/design/notes.md): the scratchpad and the to-do list, with the PC's version of them.
    public static final class Todo { public String id = "", text = ""; public boolean done; }
    public static final class Notes {
        public int version;
        public String text = "";
        public final List<Todo> items = new ArrayList<Todo>();
    }

    // Claude Code on the PC (docs/design/claude-watcher.md): its sessions, the plan's limits and usage by project.
    // Periods are 0 five hours, 1 today, 2 the week. Times are Unix seconds.
    public static final class ClaudeSession {
        public String id = "", project = "", title = "", state = "ready", message = "", model = "";
        public double since;
        public boolean canFocus;
        public final long[] tokens = new long[3];
        public String shown() { return title == null || title.trim().isEmpty() ? project : title; }
    }
    public static final class ClaudeWindow { public double pct; public double resetsAt; }
    public static final class ClaudeProject { public String project = ""; public long tokens; }
    public static final class Claude {
        public boolean connected, sample, logsFailed, logsRead, noPlan;
        public final List<ClaudeSession> sessions = new ArrayList<ClaudeSession>();
        public ClaudeWindow fiveHour, week;
        public double updatedAt;
        public String source = "";
        @SuppressWarnings("unchecked")
        public final List<ClaudeProject>[] usage = new List[] { new ArrayList<ClaudeProject>(), new ArrayList<ClaudeProject>(), new ArrayList<ClaudeProject>() };
        public boolean limitsKnown() { return fiveHour != null || week != null; }
        public int needsYou() { int n = 0; for (ClaudeSession s : sessions) if ("needs".equals(s.state)) n++; return n; }
    }

    public static final class SpotifyFlags { public boolean connected, canControl, canReadRecent; }

    public static final class Sound { public String id, guild, name; public boolean isDefault; }
    public static final class Discord {
        public boolean connected, linked;
        public String channel = "", channelGuild = "", status = "";
        public final List<Sound> sounds = new ArrayList<Sound>();
    }

    // The PC's layout pages (docs/design/any-screen-size.md): the page it shows, every page's name, the current page's
    // layouts saved per canvas size ("WxH" keys) and the layout on the PC's own screen. The phone mirrors them.
    public static final class Layout {
        public int page;
        public final List<String> pages = new ArrayList<String>();
        public boolean snap, editing;
        public double canvasWidth, canvasHeight;
        public WidgetLayout.Placement[] shown;
        public final Map<String, WidgetLayout.Placement[]> layouts = new LinkedHashMap<String, WidgetLayout.Placement[]>();
        public String pageName() { return page >= 0 && page < pages.size() ? pages.get(page) : ""; }
        // Every layout the phone can stretch from: the saved ones, and the PC screen's own when that size isn't saved.
        public Map<String, WidgetLayout.Placement[]> sources() {
            Map<String, WidgetLayout.Placement[]> all = new LinkedHashMap<String, WidgetLayout.Placement[]>(layouts);
            if (shown != null && canvasWidth >= 64 && canvasHeight >= 64) {
                String key = WidgetLayout.keyName(canvasWidth, canvasHeight);
                if (!all.containsKey(key)) all.put(key, shown);
            }
            return all;
        }
    }

    public String name = "", version = "", layoutVersion = "";
    public boolean quiet, use24;
    // Sessions that need you while the Claude card is on some page, and the first such page (-1: on none), so the
    // header's pill can light up from another page without asking for the whole claude section.
    public int claudeNeeds, claudePage = -1;
    public Claude claude;
    public Layout layout;
    public Focus focus;
    public Media media;
    public Audio audio;
    public System system;
    public List<Button> deck;
    public Weather weather;
    public SpotifyFlags spotify;
    public Discord discord;
    public Storage storage;
    public Clocks clocks;
    public Prayer prayer;
    public Notes notes;

    private static boolean flag(Map<String, Object> map, String key) { return Boolean.TRUE.equals(Json.value(map, key)); }
    private static String text(Map<String, Object> map, String key) { String value = Json.field(map, key); return value == null ? "" : value; }
    private static Double maybe(Map<String, Object> map, String key) { Object value = Json.value(map, key); return value instanceof Number ? ((Number) value).doubleValue() : null; }

    public static PcState parse(Map<String, Object> root) {
        PcState state = new PcState();
        state.name = text(root, "name"); state.version = text(root, "version");
        state.quiet = flag(root, "quiet"); state.use24 = flag(root, "use24"); state.layoutVersion = text(root, "layoutVersion");
        state.claudeNeeds = (int) Json.number(root, "claudeNeeds"); state.claudePage = Json.has(root, "claudePage") ? (int) Json.number(root, "claudePage") : -1;
        if (Json.has(root, "claude")) state.claude = parseClaude(Json.obj(Json.value(root, "claude")));
        if (Json.has(root, "layout")) state.layout = parseLayout(Json.obj(Json.value(root, "layout")));
        if (Json.has(root, "focus")) {
            Map<String, Object> f = Json.obj(Json.value(root, "focus"));
            Focus focus = new Focus();
            focus.remainingMs = Json.number(f, "remainingMs"); focus.durationMs = Json.number(f, "durationMs");
            focus.progress = Json.number(f, "progress"); focus.running = flag(f, "running");
            focus.paused = flag(f, "paused"); focus.finished = flag(f, "finished"); focus.chime = flag(f, "chime");
            focus.customMinutes = (int) Json.number(f, "customMinutes"); focus.sessionsToday = (int) Json.number(f, "sessionsToday"); focus.minutesToday = (int) Json.number(f, "minutesToday");
            state.focus = focus;
        }
        if (Json.has(root, "media")) {
            Map<String, Object> m = Json.obj(Json.value(root, "media"));
            Media media = new Media();
            media.error = flag(m, "error"); media.available = flag(m, "available"); media.playing = flag(m, "playing");
            media.canPlayPause = flag(m, "canPlayPause"); media.canPrevious = flag(m, "canPrevious"); media.canNext = flag(m, "canNext"); media.canSeek = flag(m, "canSeek");
            media.art = flag(m, "art"); media.title = text(m, "title"); media.artist = text(m, "artist"); media.source = text(m, "source"); media.key = text(m, "key"); media.app = text(m, "app");
            media.positionMs = Json.number(m, "positionMs"); media.durationMs = Json.number(m, "durationMs");
            state.media = media;
        }
        if (Json.has(root, "audio")) {
            Map<String, Object> a = Json.obj(Json.value(root, "audio"));
            Audio audio = new Audio();
            audio.available = flag(a, "available"); audio.muted = flag(a, "muted"); audio.sessionsAvailable = flag(a, "sessionsAvailable"); audio.canRoute = flag(a, "canRoute");
            Object microphone = Json.value(a, "microphone");
            audio.microphoneMuted = microphone instanceof Boolean ? (Boolean) microphone : null;
            audio.device = text(a, "device"); audio.deviceId = text(a, "deviceId"); audio.volume = Json.number(a, "volume");
            for (Object item : Json.array(a, "devices")) {
                Map<String, Object> d = Json.obj(item);
                Device device = new Device();
                device.id = Json.field(d, "id"); device.name = text(d, "name"); device.isDefault = flag(d, "isDefault"); device.formFactor = (int) Json.number(d, "formFactor");
                if (device.id != null) audio.devices.add(device);
            }
            for (Object item : Json.array(a, "sessions")) {
                Map<String, Object> s = Json.obj(item);
                Session session = new Session();
                session.id = Json.field(s, "id"); session.name = text(s, "name"); session.deviceId = text(s, "deviceId");
                session.processId = (long) Json.number(s, "processId"); session.volume = Json.number(s, "volume"); session.muted = flag(s, "muted");
                if (session.id != null) audio.sessions.add(session);
            }
            state.audio = audio;
        }
        if (Json.has(root, "system")) {
            Map<String, Object> s = Json.obj(Json.value(root, "system"));
            System system = new System();
            system.error = flag(s, "error");
            system.showCpu = flag(s, "showCpu"); system.showGpu = flag(s, "showGpu"); system.showRam = flag(s, "showRam"); system.showNetwork = flag(s, "showNetwork"); system.showUptime = flag(s, "showUptime");
            system.cpu = maybe(s, "cpu"); system.gpu = maybe(s, "gpu"); system.gpuStatus = text(s, "gpuStatus");
            system.memoryPercent = maybe(s, "memoryPercent"); system.memoryUsedGB = Json.number(s, "memoryUsedGB"); system.memoryTotalGB = Json.number(s, "memoryTotalGB");
            system.download = maybe(s, "download"); system.upload = maybe(s, "upload"); system.uptimeMs = Json.number(s, "uptimeMs");
            state.system = system;
        }
        if (Json.has(root, "deck")) {
            state.deck = new ArrayList<Button>();
            for (Object item : Json.array(root, "deck")) {
                Map<String, Object> b = Json.obj(item);
                Button button = new Button();
                button.label = text(b, "label"); button.glyph = glyph(Json.field(b, "glyph")); button.action = text(b, "action"); button.describe = text(b, "describe");
                button.confirm = flag(b, "confirm"); button.enabled = flag(b, "enabled"); button.on = flag(b, "on"); button.color = text(b, "color"); button.kind = text(b, "kind");
                state.deck.add(button);
            }
        }
        if (Json.has(root, "weather")) {
            Map<String, Object> w = Json.obj(Json.value(root, "weather"));
            Weather weather = new Weather();
            weather.location = text(w, "location"); weather.latitude = Json.number(w, "latitude"); weather.longitude = Json.number(w, "longitude"); weather.imperial = flag(w, "imperial");
            state.weather = weather;
        }
        if (Json.has(root, "spotify")) {
            Map<String, Object> s = Json.obj(Json.value(root, "spotify"));
            SpotifyFlags spotify = new SpotifyFlags();
            spotify.connected = flag(s, "connected"); spotify.canControl = flag(s, "canControl"); spotify.canReadRecent = flag(s, "canReadRecent");
            state.spotify = spotify;
        }
        if (Json.has(root, "discord")) {
            Map<String, Object> d = Json.obj(Json.value(root, "discord"));
            state.discord = parseDiscord(d);
        }
        if (Json.has(root, "storage")) {
            Map<String, Object> s = Json.obj(Json.value(root, "storage"));
            Storage storage = new Storage();
            storage.error = flag(s, "error");
            if (Json.has(s, "lowPercent")) storage.lowPercent = (int) Json.number(s, "lowPercent");
            for (Object item : Json.array(s, "drives")) {
                Map<String, Object> d = Json.obj(item);
                Drive drive = new Drive();
                drive.name = text(d, "name"); drive.label = text(d, "label"); drive.total = Json.number(d, "total"); drive.free = Json.number(d, "free");
                if (!drive.name.isEmpty() && drive.total > 0) storage.drives.add(drive);
            }
            state.storage = storage;
        }
        if (Json.has(root, "clocks")) {
            Map<String, Object> c = Json.obj(Json.value(root, "clocks"));
            Clocks clocks = new Clocks();
            clocks.pcOffset = (int) Json.number(c, "pcOffset");
            for (Object item : Json.array(c, "clocks")) {
                Map<String, Object> m = Json.obj(item);
                Clock clock = new Clock();
                clock.label = text(m, "label"); clock.offset = (int) Json.number(m, "offset");
                if (Math.abs(clock.offset) <= 18 * 60) clocks.clocks.add(clock);
            }
            state.clocks = clocks;
        }
        if (Json.has(root, "prayer")) {
            Map<String, Object> p = Json.obj(Json.value(root, "prayer"));
            Prayer prayer = new Prayer();
            prayer.located = flag(p, "located"); prayer.alerting = flag(p, "alerting");
            prayer.city = text(p, "city"); prayer.method = text(p, "method"); prayer.hijri = text(p, "hijri");
            prayer.pcOffset = (int) Json.number(p, "pcOffset");
            prayer.next = Math.max(0, Math.min(4, (int) Json.number(p, "next"))); prayer.current = Math.max(0, Math.min(4, (int) Json.number(p, "current")));
            prayer.nextAt = Json.number(p, "nextAt"); prayer.previousAt = Json.number(p, "previousAt"); prayer.sunrise = Json.number(p, "sunrise");
            List<Object> times = Json.array(p, "times");
            for (int i = 0; i < 5 && i < times.size(); i++) prayer.times[i] = times.get(i) instanceof Number ? ((Number) times.get(i)).doubleValue() : 0;
            if (prayer.located && (times.size() != 5 || prayer.nextAt <= 0)) prayer.located = false;
            state.prayer = prayer;
        }
        if (Json.has(root, "notes")) state.notes = parseNotes(Json.obj(Json.value(root, "notes")));
        return state;
    }

    private static final String[] PERIODS = { "fiveHour", "today", "week" };

    public static Claude parseClaude(Map<String, Object> c) {
        Claude claude = new Claude();
        claude.connected = flag(c, "connected"); claude.sample = flag(c, "sample"); claude.logsFailed = flag(c, "logsFailed"); claude.logsRead = flag(c, "logsRead"); claude.noPlan = flag(c, "noPlan");
        for (Object item : Json.array(c, "sessions")) {
            Map<String, Object> s = Json.obj(item);
            ClaudeSession session = new ClaudeSession();
            session.id = text(s, "id"); session.project = text(s, "project"); session.title = text(s, "title"); session.state = text(s, "state");
            session.message = text(s, "message"); session.model = text(s, "model"); session.since = Json.number(s, "since"); session.canFocus = flag(s, "canFocus");
            Map<String, Object> tokens = Json.obj(Json.value(s, "tokens"));
            for (int i = 0; i < 3; i++) session.tokens[i] = (long) Json.number(tokens, PERIODS[i]);
            if (!session.id.isEmpty()) claude.sessions.add(session);
        }
        Map<String, Object> limits = Json.obj(Json.value(c, "limits"));
        claude.fiveHour = window(Json.value(limits, "fiveHour")); claude.week = window(Json.value(limits, "week"));
        claude.updatedAt = Json.number(limits, "updatedAt"); claude.source = text(limits, "source");
        Map<String, Object> usage = Json.obj(Json.value(c, "usage"));
        for (int i = 0; i < 3; i++)
            for (Object item : Json.array(usage, PERIODS[i])) {
                Map<String, Object> u = Json.obj(item);
                ClaudeProject project = new ClaudeProject(); project.project = text(u, "project"); project.tokens = (long) Json.number(u, "tokens");
                if (!project.project.isEmpty()) claude.usage[i].add(project);
            }
        return claude;
    }
    private static ClaudeWindow window(Object value) {
        if (!(value instanceof Map)) return null;
        Map<String, Object> w = Json.obj(value);
        if (!(Json.value(w, "pct") instanceof Number)) return null;
        ClaudeWindow window = new ClaudeWindow(); window.pct = Json.number(w, "pct"); window.resetsAt = Json.number(w, "resetsAt");
        return window;
    }

    public static WidgetLayout.Placement[] placements(List<Object> items) {
        List<WidgetLayout.Placement> result = new ArrayList<WidgetLayout.Placement>();
        for (Object item : items) {
            Map<String, Object> w = Json.obj(item);
            String id = Json.field(w, "id");
            if (id == null || !java.util.Arrays.asList(WidgetLayout.IDS).contains(id)) continue;
            WidgetLayout.Placement p = new WidgetLayout.Placement(id, Json.number(w, "x"), Json.number(w, "y"), Json.number(w, "width"), Json.number(w, "height"), flag(w, "visible"));
            p.layer = (int) Json.number(w, "layer");
            if (WidgetLayout.find(result.toArray(new WidgetLayout.Placement[0]), id) == null) result.add(p);
        }
        // A widget the PC didn't mention isn't on its page.
        for (String id : WidgetLayout.IDS)
            if (WidgetLayout.find(result.toArray(new WidgetLayout.Placement[0]), id) == null) result.add(new WidgetLayout.Placement(id, 0, 0, 0, 0, false));
        return result.toArray(new WidgetLayout.Placement[0]);
    }

    public static Layout parseLayout(Map<String, Object> l) {
        Layout layout = new Layout();
        for (Object name : Json.array(l, "pages")) layout.pages.add(name instanceof String ? (String) name : "");
        layout.page = Math.max(0, Math.min(Math.max(0, layout.pages.size() - 1), (int) Json.number(l, "page")));
        layout.snap = flag(l, "snap"); layout.editing = flag(l, "editing");
        Map<String, Object> canvas = Json.obj(Json.value(l, "canvas"));
        layout.canvasWidth = Json.number(canvas, "width"); layout.canvasHeight = Json.number(canvas, "height");
        if (Json.has(l, "shown")) layout.shown = placements(Json.array(l, "shown"));
        for (Object item : Json.array(l, "layouts")) {
            Map<String, Object> c = Json.obj(item);
            double width = Json.number(c, "width"), height = Json.number(c, "height");
            if (WidgetLayout.parseKey(WidgetLayout.keyName(width, height)) == null) continue;
            layout.layouts.put(WidgetLayout.keyName(width, height), placements(Json.array(c, "widgets")));
        }
        return layout;
    }

    public static List<Map<String, Object>> json(WidgetLayout.Placement[] layout) {
        List<Map<String, Object>> result = new ArrayList<Map<String, Object>>();
        for (WidgetLayout.Placement p : layout)
            result.add(Json.map("id", p.id, "x", p.x, "y", p.y, "width", p.width, "height", p.height, "visible", p.visible, "layer", p.layer));
        return result;
    }

    public static Notes parseNotes(Map<String, Object> n) {
        Notes notes = new Notes();
        notes.version = (int) Json.number(n, "version"); notes.text = text(n, "text");
        for (Object item : Json.array(n, "items")) {
            Map<String, Object> m = Json.obj(item);
            Todo todo = new Todo();
            todo.id = text(m, "id"); todo.text = text(m, "text"); todo.done = flag(m, "done");
            if (!todo.id.isEmpty()) notes.items.add(todo);
        }
        return notes;
    }

    public static Discord parseDiscord(Map<String, Object> d) {
        Discord discord = new Discord();
        discord.connected = flag(d, "connected"); discord.linked = flag(d, "linked");
        discord.channel = text(d, "channel"); discord.channelGuild = text(d, "channelGuild"); discord.status = text(d, "status");
        for (Object item : Json.array(d, "sounds")) {
            Map<String, Object> s = Json.obj(item);
            Sound sound = new Sound();
            sound.id = Json.field(s, "id"); sound.guild = text(s, "guild"); sound.name = text(s, "name"); sound.isDefault = flag(s, "isDefault");
            if (sound.id != null) discord.sounds.add(sound);
        }
        return discord;
    }

    // Quick Launch buttons carry a Segoe MDL2 code point from the PC; the phone draws the matching line glyph.
    public static String glyph(String hex) {
        if (hex == null) return "app";
        switch (hex.toUpperCase(java.util.Locale.ROOT)) {
            case "E774": return "globe";
            case "E8B7": return "folder";
            case "E8EF": return "calculator";
            case "E70B": case "E8A5": return "notes";
            case "E8D6": return "music";
            case "E767": return "volume";
            case "E720": return "microphone";
            case "E765": return "keyboard";
            case "E768": return "play";
            case "E916": return "timer";
            case "E713": return "settings";
            case "E734": return "star";
            case "E722": return "camera";
            case "E7F4": return "game";
            default: return "app";
        }
    }

    // A Spotify item as the PC's proxy sends it.
    public static Spotify.Item item(Object value) {
        Map<String, Object> m = Json.obj(value);
        String uri = Json.field(m, "uri");
        if (Spotify.webUrl(uri) == null) return null;
        Spotify.Item item = new Spotify.Item();
        item.id = Json.field(m, "id"); item.uri = uri; item.name = Json.field(m, "name") == null ? "Untitled" : Json.field(m, "name");
        item.subtitle = text(m, "subtitle"); item.imageUrl = Json.field(m, "imageUrl"); item.albumUrl = Json.field(m, "albumUrl");
        item.position = Json.has(m, "position") ? (int) Json.number(m, "position") : -1;
        return item;
    }

    public static Spotify.Page page(Map<String, Object> m) {
        List<Spotify.Item> items = new ArrayList<Spotify.Item>();
        for (Object value : Json.array(m, "items")) { Spotify.Item item = item(value); if (item != null) items.add(item); }
        return new Spotify.Page(items, flag(m, "hasMore"));
    }

    public static Spotify.Playback playback(Map<String, Object> m) {
        Spotify.Playback playback = new Spotify.Playback();
        playback.item = item(Json.value(m, "item"));
        playback.playing = flag(m, "playing"); playback.canSeek = flag(m, "canSeek");
        playback.positionMs = Json.number(m, "positionMs"); playback.durationMs = Json.number(m, "durationMs");
        return playback;
    }
}
