package com.alialyami.widgetscreen.core;

import java.util.Locale;
import java.util.Map;

// Open-Meteo current conditions, ported from the Windows WeatherService. The city comes from the PC.
public final class Weather {
    public static final class Snapshot {
        public double temperature, feelsLike, wind;
        public int code;
        public long retrievedMs;
    }

    public static String forecastUrl(double latitude, double longitude) {
        return "https://api.open-meteo.com/v1/forecast?latitude=" + String.format(Locale.ROOT, "%s", latitude) + "&longitude=" + String.format(Locale.ROOT, "%s", longitude)
            + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto";
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
