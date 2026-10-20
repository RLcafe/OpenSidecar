package com.alialyami.widgetscreen.core;

import java.io.UnsupportedEncodingException;
import java.net.URLEncoder;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;

// Open-Meteo requests and parsing, ported from the Windows WeatherService. No account is needed.
public final class Weather {
    public static final class Location {
        public final String name;
        public final double latitude, longitude;
        public Location(String name, double latitude, double longitude) { this.name = name; this.latitude = latitude; this.longitude = longitude; }
        @Override public String toString() { return name; }
    }

    public static final class Snapshot {
        public double temperature, feelsLike, wind;
        public int code;
        public long retrievedMs;
    }

    public static String searchUrl(String query) {
        if (query == null || query.trim().length() < 2) throw new IllegalStateException("Enter at least two characters for a city.");
        try { return "https://geocoding-api.open-meteo.com/v1/search?count=5&language=en&format=json&name=" + URLEncoder.encode(query.trim(), "UTF-8").replace("+", "%20"); }
        catch (UnsupportedEncodingException e) { throw new IllegalStateException(e); }
    }

    public static String forecastUrl(double latitude, double longitude) {
        return "https://api.open-meteo.com/v1/forecast?latitude=" + String.format(Locale.ROOT, "%s", latitude) + "&longitude=" + String.format(Locale.ROOT, "%s", longitude)
            + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto";
    }

    public static List<Location> parseLocations(String json) {
        List<Location> locations = new ArrayList<Location>();
        Map<String, Object> root = Json.obj(Json.parse(json));
        for (Object item : Json.array(root, "results")) {
            Map<String, Object> location = Json.obj(item);
            if (!(Json.value(location, "latitude") instanceof Number) || !(Json.value(location, "longitude") instanceof Number)) continue;
            String name = String.valueOf(Json.field(location, "name"));
            if (Json.field(location, "admin1") != null) name += ", " + Json.field(location, "admin1");
            if (Json.field(location, "country") != null) name += ", " + Json.field(location, "country");
            locations.add(new Location(name, Json.number(location, "latitude"), Json.number(location, "longitude")));
        }
        return locations;
    }

    public static Snapshot parse(String json, long nowMs) {
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

    public static String details(Snapshot snapshot) {
        return "Feels " + Math.round(snapshot.feelsLike) + "°C  ·  " + Math.round(snapshot.wind) + " km/h";
    }
}
