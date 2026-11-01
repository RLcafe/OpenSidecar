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
        // The chance of rain in that hour, 0–100, when Open-Meteo has it.
        public Integer rain;
    }
    public static final class Snapshot {
        public double temperature, feelsLike, wind;
        public int code;
        public Double high, low;
        public final List<Hour> hours = new ArrayList<Hour>();
        // Sticker Sheet phase 3: humidity now, today's UV index, sunrise and sunset ("yyyy-MM-ddTHH:mm", the city's local
        // time), the city's offset from UTC in seconds, and every hour from now to +12 h for the curve and rain bars.
        public Double humidity, uv;
        public String sunrise, sunset;
        public Long utcOffsetSeconds;
        public final List<Hour> curve = new ArrayList<Hour>();
        public long retrievedMs;
    }

    public static String forecastUrl(double latitude, double longitude) { return forecastUrl(latitude, longitude, false); }
    public static String forecastUrl(double latitude, double longitude, boolean imperial) {
        return "https://api.open-meteo.com/v1/forecast?latitude=" + String.format(Locale.ROOT, "%s", latitude) + "&longitude=" + String.format(Locale.ROOT, "%s", longitude)
            + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,relative_humidity_2m&daily=temperature_2m_max,temperature_2m_min,uv_index_max,sunrise,sunset&hourly=temperature_2m,weather_code,precipitation_probability&forecast_days=2&timezone=auto"
            + (imperial ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
    }

    public static Snapshot parse(String json, long nowMs) { return parse(json, nowMs, null); }
    // nowHour is "yyyy-MM-ddTHH:00" in the city's local time; the next hours (now, +3 h, +6 h, +9 h, +12 h) start from it.
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
            if (Json.value(current, "relative_humidity_2m") instanceof Number) snapshot.humidity = Json.number(current, "relative_humidity_2m");
            if (Json.value(root, "utc_offset_seconds") instanceof Number) snapshot.utcOffsetSeconds = (long) Json.number(root, "utc_offset_seconds");
            if (Json.has(root, "daily")) {
                Map<String, Object> daily = Json.obj(Json.value(root, "daily"));
                List<Object> highs = Json.array(daily, "temperature_2m_max"), lows = Json.array(daily, "temperature_2m_min");
                if (!highs.isEmpty() && !lows.isEmpty() && highs.get(0) instanceof Number && lows.get(0) instanceof Number) {
                    snapshot.high = ((Number) highs.get(0)).doubleValue(); snapshot.low = ((Number) lows.get(0)).doubleValue();
                }
                List<Object> uv = Json.array(daily, "uv_index_max"), rises = Json.array(daily, "sunrise"), sets = Json.array(daily, "sunset");
                if (!uv.isEmpty() && uv.get(0) instanceof Number) snapshot.uv = ((Number) uv.get(0)).doubleValue();
                if (!rises.isEmpty() && rises.get(0) instanceof String && ((String) rises.get(0)).length() >= 16) snapshot.sunrise = (String) rises.get(0);
                if (!sets.isEmpty() && sets.get(0) instanceof String && ((String) sets.get(0)).length() >= 16) snapshot.sunset = (String) sets.get(0);
            }
            if (Json.has(root, "hourly")) {
                Map<String, Object> hourly = Json.obj(Json.value(root, "hourly"));
                List<Object> times = Json.array(hourly, "time"), temperatures = Json.array(hourly, "temperature_2m"), codes = Json.array(hourly, "weather_code"), rain = Json.array(hourly, "precipitation_probability");
                List<Hour> all = new ArrayList<Hour>();
                for (int i = 0; i < times.size() && i < temperatures.size() && i < codes.size(); i++) {
                    if (!(times.get(i) instanceof String) || !(temperatures.get(i) instanceof Number) || !(codes.get(i) instanceof Number)) continue;
                    Hour hour = new Hour();
                    hour.time = (String) times.get(i); hour.temperature = ((Number) temperatures.get(i)).doubleValue(); hour.code = ((Number) codes.get(i)).intValue();
                    if (i < rain.size() && rain.get(i) instanceof Number) hour.rain = ((Number) rain.get(i)).intValue();
                    all.add(hour);
                }
                int start = 0;
                if (nowHour != null) { for (int i = 0; i < all.size(); i++) if (all.get(i).time.compareTo(nowHour) >= 0) { start = i; break; } }
                for (int offset : new int[] { 0, 3, 6, 9, 12 }) if (start + offset < all.size()) snapshot.hours.add(all.get(start + offset));
                for (int i = start; i <= start + 12 && i < all.size(); i++) snapshot.curve.add(all.get(i));
            }
        } catch (RuntimeException e) { snapshot.high = snapshot.low = snapshot.humidity = snapshot.uv = null; snapshot.sunrise = snapshot.sunset = null; snapshot.hours.clear(); snapshot.curve.clear(); }
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

    // The three facts under the reading in the Medium and Large designs: name and value (WeatherService.Facts on Windows).
    public static String[][] facts(Snapshot snapshot, boolean imperial) {
        String wind = Math.round(snapshot.wind) + (imperial ? " mph" : " km/h");
        if (snapshot.high == null || snapshot.low == null) return new String[][] { { "FEELS", Math.round(snapshot.feelsLike) + "°" }, { "WIND", wind } };
        return new String[][] { { "FEELS", Math.round(snapshot.feelsLike) + "°" }, { "WIND", wind }, { "HIGH / LOW", Math.round(snapshot.high) + "° / " + Math.round(snapshot.low) + "°" } };
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

    // Sticker Sheet phase 3 (docs/design/sticker-sheet.md), the same rules as WeatherService on the PC.
    // "6 High": the UV index with the WHO level.
    public static String uvText(double uv) {
        long v = Math.round(uv);
        return v + " " + (v <= 2 ? "Low" : v <= 5 ? "Moderate" : v <= 7 ? "High" : v <= 10 ? "Very high" : "Extreme");
    }
    // The fact-rail chips, most useful first: Feels, Humidity, Rain, UV, Wind, High / Low; count of those it has.
    public static String[][] chips(Snapshot s, boolean imperial, int count) {
        List<String[]> chips = new ArrayList<String[]>();
        chips.add(new String[] { "FEELS", Math.round(s.feelsLike) + "°" });
        if (s.humidity != null) chips.add(new String[] { "HUMIDITY", Math.round(s.humidity) + "%" });
        Integer rain = !s.curve.isEmpty() ? s.curve.get(0).rain : !s.hours.isEmpty() ? s.hours.get(0).rain : null;
        if (rain != null) chips.add(new String[] { "RAIN", rain + "%" });
        if (s.uv != null) chips.add(new String[] { "UV", uvText(s.uv) });
        chips.add(new String[] { "WIND", Math.round(s.wind) + (imperial ? " mph" : " km/h") });
        if (s.high != null && s.low != null) chips.add(new String[] { "HIGH / LOW", Math.round(s.high) + "° / " + Math.round(s.low) + "°" });
        return chips.subList(0, Math.max(0, Math.min(count, chips.size()))).toArray(new String[0][]);
    }
    // Minutes since midnight of a "yyyy-MM-ddTHH:mm" moment, counted from the first day it's compared with.
    private static Double minutes(String iso, String day) {
        if (iso == null || iso.length() < 16) return null;
        try {
            int h = Integer.parseInt(iso.substring(11, 13)), m = Integer.parseInt(iso.substring(14, 16));
            int days = day == null ? 0 : (int) (java.time.LocalDate.parse(iso.substring(0, 10)).toEpochDay() - java.time.LocalDate.parse(day.substring(0, 10)).toEpochDay());
            return days * 1440.0 + h * 60 + m;
        } catch (RuntimeException e) { return null; }
    }
    // The city's clock as "yyyy-MM-ddTHH:mm": UTC plus the offset Open-Meteo gave, or the phone's clock without one.
    public static String cityNow(Snapshot s, long utcMs) {
        java.time.LocalDateTime now = s != null && s.utcOffsetSeconds != null
            ? java.time.LocalDateTime.ofEpochSecond(utcMs / 1000, 0, java.time.ZoneOffset.ofTotalSeconds((int) (long) s.utcOffsetSeconds))
            : java.time.LocalDateTime.ofInstant(java.time.Instant.ofEpochMilli(utcMs), java.time.ZoneId.systemDefault());
        return now.format(java.time.format.DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm", Locale.ROOT));
    }
    // Where the sun is between sunrise (0) and sunset (1); under 0 before it rises, over 1 after it sets.
    public static Double sunPart(String now, String sunrise, String sunset) {
        Double rise = minutes(sunrise, sunrise), set = minutes(sunset, sunrise), at = minutes(now, sunrise);
        if (rise == null || set == null || at == null || set <= rise) return null;
        return (at - rise) / (set - rise);
    }
    // Day between sunrise and sunset; without them, 7 AM to 7 PM.
    public static boolean isDay(String now, String sunrise, String sunset) {
        Double part = sunPart(now, sunrise, sunset);
        if (part != null) return part >= 0 && part <= 1;
        Double at = minutes(now, null);
        return at != null && at >= 7 * 60 && at < 19 * 60;
    }
    // The sky band's ink and how strongly it shows: Lemon for a clear day, Glacier for cloud, rain and snow, Lavender
    // for storms and night; clear skies show most, grey ones least.
    public static String skyInk(String family, boolean day) { return !day || family.equals("storm") ? "Lavender" : family.equals("sun") ? "Lemon" : "Glacier"; }
    public static double skyStrength(String family, boolean day) {
        if (!day) return 0.32;
        switch (family) { case "sun": return 0.42; case "partly": return 0.36; case "storm": return 0.36; case "rain": return 0.3; case "snow": return 0.28; default: return 0.22; }
    }
    // The curve's heights, 0 (the lowest temperature) to 1 (the highest); a flat line sits at the middle.
    public static double[] curveLevels(double[] values) {
        if (values == null || values.length == 0) return new double[0];
        double low = values[0], high = values[0];
        for (double v : values) { low = Math.min(low, v); high = Math.max(high, v); }
        double[] levels = new double[values.length];
        for (int i = 0; i < values.length; i++) levels[i] = high - low < 0.5 ? 0.5 : (values[i] - low) / (high - low);
        return levels;
    }
    // "6:12 AM" or "06:12" from a "yyyy-MM-ddTHH:mm" moment, following the clock's choice.
    public static String clockTime(String iso, boolean use24) {
        if (iso == null || iso.length() < 16) return "";
        int hour, minute;
        try { hour = Integer.parseInt(iso.substring(11, 13)); minute = Integer.parseInt(iso.substring(14, 16)); } catch (NumberFormatException e) { return ""; }
        if (use24) return String.format(Locale.US, "%02d:%02d", hour, minute);
        return (hour % 12 == 0 ? 12 : hour % 12) + String.format(Locale.US, ":%02d", minute) + (hour < 12 ? " AM" : " PM");
    }}
