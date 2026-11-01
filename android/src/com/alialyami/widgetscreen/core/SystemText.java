package com.alialyami.widgetscreen.core;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

// The System stats readings, their graph styles and what each says when the PC can't read it (decision 016;
// docs/design/system-sensors.md), the same as SystemStyles and SystemReadings on the PC.
public final class SystemText {
    private SystemText() { }

    public static final String[] IDS = { "Cpu", "CpuTemp", "Gpu", "GpuTemp", "Vram", "Ram", "Fps", "Network", "Uptime" };
    public static final double HOT_CELSIUS = 85;

    public static String title(String id) {
        switch (id) {
            case "Cpu": return "CPU"; case "CpuTemp": return "CPU TEMP"; case "Gpu": return "GPU"; case "GpuTemp": return "GPU TEMP";
            case "Vram": return "VRAM"; case "Ram": return "RAM"; case "Fps": return "FPS"; case "Network": return "NET"; default: return "UPTIME";
        }
    }

    public static boolean shown(PcState.System s, String id) {
        if (s == null) return !id.equals("CpuTemp") && !id.equals("GpuTemp") && !id.equals("Vram") && !id.equals("Fps");
        switch (id) {
            case "Cpu": return s.showCpu; case "CpuTemp": return s.showCpuTemp; case "Gpu": return s.showGpu; case "GpuTemp": return s.showGpuTemp;
            case "Vram": return s.showVram; case "Ram": return s.showRam; case "Fps": return s.showFps; case "Network": return s.showNetwork; default: return s.showUptime;
        }
    }
    public static List<String> shownIds(PcState.System s) { List<String> list = new ArrayList<String>(); for (String id : IDS) if (shown(s, id)) list.add(id); return list; }

    public static boolean isDial(String style) { return style.equals("Ring") || style.equals("Gauge"); }
    // Auto: rings for usage, gauges for temperatures, a sparkline for the frame rate on Medium; history lines on Large.
    public static String resolve(String style, String id, int design) {
        if (style != null && !style.isEmpty() && !style.equals("Auto")) return style;
        if (design == CardSizing.LARGE || id.equals("Network")) return "Line";
        return id.equals("CpuTemp") || id.equals("GpuTemp") ? "Gauge" : id.equals("Fps") ? "Sparkline" : "Ring";
    }
    public static String style(PcState.System s, String id) {
        String style = s == null ? null : s.styles.get(id);
        if (style == null) return "Auto";
        List<String> allowed = id.equals("Uptime") ? new ArrayList<String>() : id.equals("Network") ? Arrays.asList("Auto", "Line", "Bars", "Sparkline") : Arrays.asList("Auto", "Line", "Bars", "Sparkline", "Ring", "Gauge");
        return allowed.contains(style) ? style : "Auto";
    }

    public static String shortReason(String state) {
        switch (state == null ? "" : state) {
            case "NotInstalled": return "Not installed"; case "Damaged": return "Repair sensors"; case "NeedsFullSensors": return "Needs admin";
            case "NeedsPawnIO": return "Needs PawnIO"; case "NoGame": return "No game"; default: return "Not reported";
        }
    }
    public static String longReason(String state, String id) {
        switch (state == null ? "" : state) {
            case "NotInstalled": return "Hardware sensors aren't installed";
            case "Damaged": return "Sensor files damaged: run setup again";
            case "NeedsFullSensors": return id.equals("Fps") ? "Needs full-sensors mode or RivaTuner" : "Needs admin: full-sensors mode";
            case "NeedsPawnIO": return "Needs the PawnIO driver";
            case "NoGame": return "No game in front";
            default: return id.equals("Fps") ? "No frames counted" : "Not reported by the PC";
        }
    }

    public static String degrees(double celsius, boolean fahrenheit) { return Math.round(fahrenheit ? celsius * 9 / 5 + 32 : celsius) + (fahrenheit ? "°F" : "°C"); }

    public static double fpsScale(List<Double> history) {
        double top = 0; for (double v : history) top = Math.max(top, v);
        for (double rate : new double[] { 60, 120, 144, 165, 240, 360 }) if (top <= rate) return rate;
        return Math.ceil(top / 60) * 60;
    }

    // One reading as the card shows it.
    public static final class Reading {
        public Double value, second;
        public double percent;
        public String text = "—", detail = "", shortReason, longReason;
        public boolean warning;
        public boolean missing() { return shortReason != null; }
    }

    public static Map<String, Reading> readings(PcState.System s, double fpsScale) {
        Map<String, Reading> r = new LinkedHashMap<String, Reading>();
        boolean ok = s != null && !s.error;
        r.put("Cpu", percent(ok ? s.cpu : null, ok ? shortModel(s.cpuName) : null, null));
        r.put("Gpu", percent(ok ? s.gpu : null, !ok ? "Unavailable" : s.gpu != null && !shortModel(s.gpuName).isEmpty() ? shortModel(s.gpuName) : s.gpuStatus, null));
        r.put("Ram", percent(ok ? s.memoryPercent : null, ok && s.memoryPercent != null ? String.format(Locale.US, "%.1f / %.1f GB", s.memoryUsedGB, s.memoryTotalGB) : "Unavailable", null));
        boolean vram = ok && s.vramUsedGB != null && s.vramTotalGB != null && s.vramTotalGB > 0;
        r.put("Vram", percent(vram ? 100 * s.vramUsedGB / s.vramTotalGB : null, vram ? String.format(Locale.US, "%.1f / %.0f GB", s.vramUsedGB, s.vramTotalGB) : "", ok ? s.vramState : null));
        r.put("CpuTemp", temperature(ok ? s.cpuTemp : null, ok ? s.cpuTempState : null, "CpuTemp", ok && s.fahrenheit, "Package"));
        r.put("GpuTemp", temperature(ok ? s.gpuTemp : null, ok ? s.gpuTempState : null, "GpuTemp", ok && s.fahrenheit, ok ? shortModel(s.gpuName) : ""));
        Reading fps = new Reading();
        if (ok && s.fps != null) {
            fps.value = s.fps; fps.text = String.valueOf(Math.round(s.fps)); fps.percent = Math.min(100, 100 * s.fps / fpsScale);
            String app = s.fpsApp == null ? "" : s.fpsApp, source = s.fpsSource == null ? "" : s.fpsSource;
            fps.detail = app + (source.isEmpty() ? "" : (app.isEmpty() ? "" : "  ·  ") + source);
        } else missing(fps, ok ? s.fpsState : null, "Fps");
        r.put("Fps", fps);
        Reading network = new Reading();
        if (ok && s.download != null && s.upload != null) { network.value = s.download; network.second = s.upload; network.text = Format.rate(s.download); network.detail = Format.rate(s.upload); }
        else { network.text = "— KB/s"; network.detail = "— KB/s"; }
        r.put("Network", network);
        Reading uptime = new Reading(); uptime.text = ok ? Format.uptime((long) s.uptimeMs, 3) : "—";
        r.put("Uptime", uptime);
        return r;
    }

    // The status sticker's peak (Sticker Sheet phase 3): the busiest of CPU, GPU, RAM and VRAM among those shown,
    // as "GPU 92%"; null when none has a value. The sticker fills from 80% (STRAINED). Same as SystemReadings.PeakId.
    public static final double STRAINED = 80;
    public static String peakId(Map<String, Reading> readings, PcState.System s) {
        String best = null; double top = -1;
        for (String id : new String[] { "Cpu", "Gpu", "Ram", "Vram" }) {
            Reading reading = readings.get(id);
            if (!shown(s, id) || reading == null || reading.value == null) continue;
            if (reading.percent > top) { top = reading.percent; best = id; }
        }
        return best;
    }
    public static String peakText(String id, Reading reading) { return id == null || reading == null ? "" : title(id) + " " + Math.round(reading.percent) + "%"; }
    // A temperature badge on a ring: "58°" in the chosen unit, no letter.
    public static String badge(Reading temperature) { return temperature == null || temperature.value == null ? "" : temperature.text.replace("°C", "°").replace("°F", "°"); }
    // "min 12% · max 87%" from a reading's history, or "" before two readings.
    public static String range(String id, List<Double> history, boolean fahrenheit) {
        if (history == null || history.size() < 2) return "";
        double low = Double.MAX_VALUE, high = -Double.MAX_VALUE;
        for (double v : history) { low = Math.min(low, v); high = Math.max(high, v); }
        return "min " + rangeValue(id, low, fahrenheit) + " · max " + rangeValue(id, high, fahrenheit);
    }
    private static String rangeValue(String id, double v, boolean fahrenheit) {
        if (id.equals("CpuTemp") || id.equals("GpuTemp")) return degrees(v, fahrenheit).replace("°C", "°").replace("°F", "°");
        return id.equals("Fps") ? String.valueOf(Math.round(v)) : Math.round(v) + "%";
    }

    // "AMD Ryzen 7 7800X3D 8-Core Processor" → "Ryzen 7 7800X3D"; "NVIDIA GeForce RTX 4070" → "RTX 4070".
    // The PC's SystemService.ShortModel does the same.
    public static String shortModel(String name) {
        if (name == null || name.trim().isEmpty()) return "";
        String s = name.replace("(R)", "").replace("(TM)", "").replace("(tm)", "").replace("(r)", "");
        s = s.replaceAll("(?i)\\s*@\\s*[\\d.]+\\s*GHz", "");
        s = s.replaceAll("(?i)\\b\\d+-Core\\b", "");
        s = s.replaceAll("(?i)\\b(Processor|CPU|with Radeon Graphics|Graphics)\\b", "");
        s = s.replaceAll("(?i)^\\s*(AMD|Intel|NVIDIA)\\s+", "");
        s = s.replaceAll("(?i)^\\s*(Core|GeForce)\\s+", "");
        return s.replaceAll("\\s+", " ").trim();
    }

    private static Reading percent(Double value, String detail, String state) {
        Reading reading = new Reading(); reading.detail = detail == null ? "" : detail;
        if (value == null) { if (state != null && !state.isEmpty() && !state.equals("Unavailable")) missing(reading, state, ""); return reading; }
        reading.value = value; reading.percent = Math.max(0, Math.min(100, value)); reading.text = Math.round(value) + "%"; reading.warning = value >= 90;
        return reading;
    }
    private static Reading temperature(Double celsius, String state, String id, boolean fahrenheit, String detail) {
        Reading reading = new Reading();
        if (celsius == null) { missing(reading, state == null || state.isEmpty() ? "Unavailable" : state, id); return reading; }
        reading.value = celsius; reading.percent = Math.max(0, Math.min(100, celsius)); reading.text = degrees(celsius, fahrenheit); reading.detail = detail == null ? "" : detail; reading.warning = celsius >= HOT_CELSIUS;
        return reading;
    }
    private static void missing(Reading reading, String state, String id) { reading.text = "—"; reading.shortReason = shortReason(state); reading.longReason = longReason(state, id); reading.detail = reading.longReason; }
}
