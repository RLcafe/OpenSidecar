package com.alialyami.widgetscreen.core;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;

// Open-Meteo current conditions, today's high and low and the next hours, ported from the Windows WeatherService.
// The city and the units come from the PC.
public final class Weather {
    public static final class Hour {
        // "yyyy-MM-ddTHH:mm" in the city's local time, as Open-Meteo gives it.
        public String time;
        public double temperature;
        public int code;
    }
    public static final class Snapshot {
        public double temperature, feelsLike, wind;
        public int code;
        public Double high, low;
        public final List<Hour> hours = new ArrayList<Hour>();
        public long retrievedMs;
    }

    public static String forecastUrl(double latitude, double longitude) { return forecastUrl(latitude, longitude, false); }
    public static String forecastUrl(double latitude, double longitude, boolean imperial) {
        return "https://api.open-meteo.com/v1/forecast?latitude=" + String.format(Locale.ROOT, "%s", latitude) + "&longitude=" + String.format(Locale.ROOT, "%s", longitude)
            + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min&hourly=temperature_2m,weather_code&forecast_days=2&timezone=auto"
            + (imperial ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
    }

    public static Snapshot parse(String json, long nowMs) { return parse(json, nowMs, null); }
    // nowHour is "yyyy-MM-ddTHH:00" in the city's local time; the next hours (now, +3 h, +6 h) start from it.
    public static Snapshot parse(String json, long nowMs, String nowHour) {
        Map<String, Object> root = Json.obj(Json.parse(json));
        if (!Json.has(root, "current")) throw new IllegalStateException("Weather data is unavailable.");
        Map<String, Object> current = Json.obj(Json.value(root, "current"));
        for (String key : new String[] { "temperature_2m", "apparent_temperature", "wind_speed_10m", "weather_code" })
            if (!(Json.value(current, key) instanceof Number)) throw new IllegalStateException("Weather data is incomplete.");
        Snapshot snapshot = new Snapshot();
        snapshot.temperature = Json.number(current, "temperature_2m");
        snapshot.feelsLike = Json.number(current, "apparent_temperature");
        snapshot.wind = Json.number(current, "wind_speed_10m");
        snapshot.code = (int) Json.number(current, "weather_code");
        snapshot.retrievedMs = nowMs;
        // The forecast parts are extras: a missing or odd one leaves current conditions intact.
        try {
            if (Json.has(root, "daily")) {
                Map<String, Object> daily = Json.obj(Json.value(root, "daily"));
                List<Object> highs = Json.array(daily, "temperature_2m_max"), lows = Json.array(daily, "temperature_2m_min");
                if (!highs.isEmpty() && !lows.isEmpty() && highs.get(0) instanceof Number && lows.get(0) instanceof Number) {
                    snapshot.high = ((Number) highs.get(0)).doubleValue(); snapshot.low = ((Number) lows.get(0)).doubleValue();
                }
            }
            if (Json.has(root, "hourly")) {
                Map<String, Object> hourly = Json.obj(Json.value(root, "hourly"));
                List<Object> times = Json.array(hourly, "time"), temperatures = Json.array(hourly, "temperature_2m"), codes = Json.array(hourly, "weather_code");
                List<Hour> all = new ArrayList<Hour>();
                for (int i = 0; i < times.size() && i < temperatures.size() && i < codes.size(); i++) {
                    if (!(times.get(i) instanceof String) || !(temperatures.get(i) instanceof Number) || !(codes.get(i) instanceof Number)) continue;
                    Hour hour = new Hour();
                    hour.time = (String) times.get(i); hour.temperature = ((Number) temperatures.get(i)).doubleValue(); hour.code = ((Number) codes.get(i)).intValue();
                    all.add(hour);
                }
                int start = 0;
                if (nowHour != null) { for (int i = 0; i < all.size(); i++) if (all.get(i).time.compareTo(nowHour) >= 0) { start = i; break; } }
                for (int offset : new int[] { 0, 3, 6 }) if (start + offset < all.size()) snapshot.hours.add(all.get(start + offset));
            }
        } catch (RuntimeException e) { snapshot.high = snapshot.low = null; snapshot.hours.clear(); }
        return snapshot;
    }

    public static String describe(int code) {
        if (code == 0) return "Clear skies";
        if (code <= 2) return "Partly cloudy";
        if (code == 3) return "Overcast";
        if (code == 45 || code == 48) return "Foggy";
        if (code >= 51 && code <= 57) return "Drizzle";
        if (code >= 61 && code <= 67) return "Rain";
        if (code >= 71 && code <= 77) return "Snow";
        if (code >= 80 && code <= 82) return "Rain showers";
        if (code == 85 || code == 86) return "Snow showers";
        if (code >= 95) return "Thunderstorms";
        return "Conditions unavailable";
    }

    public static String details(Snapshot snapshot) { return details(snapshot, false); }
    // "Feels 25°  ·  12 km/h  ·  ↑ 28° ↓ 15°", as WeatherService.Details on Windows.
    public static String details(Snapshot snapshot, boolean imperial) {
        String text = "Feels " + Math.round(snapshot.feelsLike) + "°  ·  " + Math.round(snapshot.wind) + (imperial ? " mph" : " km/h");
        if (snapshot.high != null && snapshot.low != null) text += "  ·  ↑ " + Math.round(snapshot.high) + "° ↓ " + Math.round(snapshot.low) + "°";
        return text;
    }

    // The glyph family for a condition (Glyphs.java draws them): sun, partly, cloud, fog, rain, snow or storm.
    public static String family(int code) {
        if (code == 0) return "sun";
        if (code <= 2) return "partly";
        if (code == 3) return "cloud";
        if (code == 45 || code == 48) return "fog";
        if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) return "rain";
        if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "snow";
        if (code >= 95) return "storm";
        return "cloud";
    }

    // "14:00" or "2 PM" from an Open-Meteo hour, following the clock's choice.
    public static String hourLabel(String isoTime, boolean use24) {
        if (isoTime == null || isoTime.length() < 16) return "";
        int hour;
        try { hour = Integer.parseInt(isoTime.substring(11, 13)); } catch (NumberFormatException e) { return ""; }
        if (use24) return String.format(Locale.US, "%02d:00", hour);
        return (hour % 12 == 0 ? 12 : hour % 12) + (hour < 12 ? " AM" : " PM");
    }
}
