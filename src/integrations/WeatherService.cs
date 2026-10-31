using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    public sealed class WeatherLocation
    {
        public string Name { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public override string ToString() { return Name; }
    }
    public sealed class WeatherHour
    {
        public DateTime Time;
        public double Temperature;
        public int Code;
    }
    public sealed class WeatherSnapshot
    {
        public double Temperature, FeelsLike, Wind;
        public int Code;
        // Today's high and low, and the next hours (now, +3 h, +6 h, +9 h, +12 h), when the answer had them.
        public double? High, Low;
        public List<WeatherHour> Hours = new List<WeatherHour>();
        public DateTime Retrieved;
    }
    public sealed class WeatherService : IDisposable
    {
        private readonly HttpClient http = new HttpClient(new PreferIPv4Handler()) { Timeout = TimeSpan.FromSeconds(12) };
        public async Task<List<WeatherLocation>> SearchAsync(string query)
        {
            if (String.IsNullOrWhiteSpace(query) || query.Trim().Length < 2) throw new InvalidOperationException("Enter at least two characters for a city.");
            string json = await http.GetStringAsync("https://geocoding-api.open-meteo.com/v1/search?count=5&language=en&format=json&name=" + Uri.EscapeDataString(query.Trim()));
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            var locations = new List<WeatherLocation>();
            if (!root.ContainsKey("results")) return locations;
            foreach (var item in (System.Collections.IEnumerable)root["results"])
            {
                var location = (Dictionary<string, object>)item;
                string name = Convert.ToString(location["name"]);
                if (location.ContainsKey("admin1")) name += ", " + Convert.ToString(location["admin1"]);
                if (location.ContainsKey("country")) name += ", " + Convert.ToString(location["country"]);
                locations.Add(new WeatherLocation { Name = name, Latitude = Convert.ToDouble(location["latitude"], CultureInfo.InvariantCulture), Longitude = Convert.ToDouble(location["longitude"], CultureInfo.InvariantCulture) });
            }
            return locations;
        }
        // Celsius and km/h, or Fahrenheit and mph: Open-Meteo converts, so the numbers arrive ready to show.
        public static string ForecastUrl(double latitude, double longitude, bool imperial)
        {
            return "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString(CultureInfo.InvariantCulture) + "&longitude=" + longitude.ToString(CultureInfo.InvariantCulture)
                + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&daily=temperature_2m_max,temperature_2m_min&hourly=temperature_2m,weather_code&forecast_days=2&timezone=auto"
                + (imperial ? "&temperature_unit=fahrenheit&wind_speed_unit=mph" : "");
        }
        public Task<WeatherSnapshot> ReadAsync(double latitude, double longitude) { return ReadAsync(latitude, longitude, false); }
        public async Task<WeatherSnapshot> ReadAsync(double latitude, double longitude, bool imperial)
        {
            return Parse(await http.GetStringAsync(ForecastUrl(latitude, longitude, imperial)), DateTime.Now);
        }
        public static WeatherSnapshot Parse(string json) { return Parse(json, DateTime.Now); }
        // now is in the city's local time (Open-Meteo answers in it, timezone=auto); the next hours start from it.
        public static WeatherSnapshot Parse(string json, DateTime now)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("current")) throw new InvalidOperationException("Weather data is unavailable.");
            var current = (Dictionary<string, object>)root["current"];
            foreach (string key in new[] { "temperature_2m", "apparent_temperature", "wind_speed_10m", "weather_code" })
                if (!current.ContainsKey(key) || current[key] == null) throw new InvalidOperationException("Weather data is incomplete.");
            var snapshot = new WeatherSnapshot {
                Temperature = Convert.ToDouble(current["temperature_2m"], CultureInfo.InvariantCulture),
                FeelsLike = Convert.ToDouble(current["apparent_temperature"], CultureInfo.InvariantCulture),
                Wind = Convert.ToDouble(current["wind_speed_10m"], CultureInfo.InvariantCulture),
                Code = Convert.ToInt32(current["weather_code"], CultureInfo.InvariantCulture), Retrieved = DateTime.Now
            };
            // The forecast parts are extras: a missing or odd one leaves current conditions intact.
            try
            {
                object daily;
                if (root.TryGetValue("daily", out daily) && daily is Dictionary<string, object>)
                {
                    var d = (Dictionary<string, object>)daily;
                    var highs = Items(d, "temperature_2m_max"); var lows = Items(d, "temperature_2m_min");
                    if (highs != null && lows != null && highs.Length > 0 && lows.Length > 0 && highs[0] != null && lows[0] != null)
                    { snapshot.High = Convert.ToDouble(highs[0], CultureInfo.InvariantCulture); snapshot.Low = Convert.ToDouble(lows[0], CultureInfo.InvariantCulture); }
                }
                object hourly;
                if (root.TryGetValue("hourly", out hourly) && hourly is Dictionary<string, object>)
                {
                    var h = (Dictionary<string, object>)hourly;
                    var times = Items(h, "time"); var temperatures = Items(h, "temperature_2m"); var codes = Items(h, "weather_code");
                    if (times != null && temperatures != null && codes != null)
                    {
                        var all = new List<WeatherHour>();
                        for (int i = 0; i < times.Length && i < temperatures.Length && i < codes.Length; i++)
                        {
                            if (temperatures[i] == null || codes[i] == null) continue;
                            DateTime time;
                            if (!DateTime.TryParseExact(Convert.ToString(times[i]), "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) continue;
                            all.Add(new WeatherHour { Time = time, Temperature = Convert.ToDouble(temperatures[i], CultureInfo.InvariantCulture), Code = Convert.ToInt32(codes[i], CultureInfo.InvariantCulture) });
                        }
                        DateTime thisHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
                        int start = all.FindIndex(hour => hour.Time >= thisHour);
                        if (start < 0) start = 0;
                        foreach (int offset in new[] { 0, 3, 6, 9, 12 }) if (start + offset < all.Count) snapshot.Hours.Add(all[start + offset]);
                    }
                }
            }
            catch (Exception) { snapshot.High = snapshot.Low = null; snapshot.Hours.Clear(); }
            return snapshot;
        }
        // A JSON array from JavaScriptSerializer arrives as object[] at the top level and as an ArrayList when nested.
        private static object[] Items(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return null;
            var list = value as System.Collections.ArrayList;
            return list != null ? list.ToArray() : value as object[];
        }
        public static string Describe(int code)
        {
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
        // "Feels 25°  ·  12 km/h  ·  ↑ 28° ↓ 15°". The phone formats it the same way (Weather.details).
        public static string Details(WeatherSnapshot snapshot, bool imperial)
        {
            string text = "Feels " + Math.Round(snapshot.FeelsLike) + "°  ·  " + Math.Round(snapshot.Wind) + (imperial ? " mph" : " km/h");
            if (snapshot.High.HasValue && snapshot.Low.HasValue) text += "  ·  ↑ " + Math.Round(snapshot.High.Value) + "° ↓ " + Math.Round(snapshot.Low.Value) + "°";
            return text;
        }
        // The three facts under the reading in the Medium and Large designs: name and value (Weather.facts on the phone).
        public static string[][] Facts(WeatherSnapshot snapshot, bool imperial)
        {
            var facts = new List<string[]> { new[] { "FEELS", Math.Round(snapshot.FeelsLike) + "°" }, new[] { "WIND", Math.Round(snapshot.Wind) + (imperial ? " mph" : " km/h") } };
            if (snapshot.High.HasValue && snapshot.Low.HasValue) facts.Add(new[] { "HIGH / LOW", Math.Round(snapshot.High.Value) + "° / " + Math.Round(snapshot.Low.Value) + "°" });
            return facts.ToArray();
        }
        // The glyph family for a condition: sun, partly, cloud, fog, rain, snow or storm.
        public static string Family(int code)
        {
            if (code == 0) return "sun";
            if (code <= 2) return "partly";
            if (code == 3) return "cloud";
            if (code == 45 || code == 48) return "fog";
            if ((code >= 51 && code <= 67) || (code >= 80 && code <= 82)) return "rain";
            if ((code >= 71 && code <= 77) || code == 85 || code == 86) return "snow";
            if (code >= 95) return "storm";
            return "cloud";
        }
        // Line glyphs on a 24-unit grid, drawn as a Path (no images), the same shapes as the phone's Glyphs.java.
        public static string Glyph(string family)
        {
            const string cloud = "M7,17 H17 A3.5,3.5 0 0 0 17.3,10 A5.2,5.2 0 0 0 7.2,10.6 A3.2,3.2 0 0 0 7,17 Z";
            const string cloudHigh = "M7,13 H17 A3.5,3.5 0 0 0 17.3,6 A5.2,5.2 0 0 0 7.2,6.6 A3.2,3.2 0 0 0 7,13 Z";
            switch (family)
            {
                case "sun": return "M12,8.2 A3.8,3.8 0 1 1 11.99,8.2 M12,2.5 V4.8 M12,19.2 V21.5 M2.5,12 H4.8 M19.2,12 H21.5 M5.3,5.3 L6.9,6.9 M17.1,17.1 L18.7,18.7 M5.3,18.7 L6.9,17.1 M17.1,6.9 L18.7,5.3";
                case "partly": return "M15.5,9.3 A3,3 0 1 1 15.49,9.3 M15.5,3.5 V5 M21.3,9.3 H19.8 M19.6,5.2 L18.5,6.3 M5.5,20 H14 A3,3 0 0 0 14.2,14 A4.5,4.5 0 0 0 5.6,14.6 A2.7,2.7 0 0 0 5.5,20 Z";
                case "cloud": return cloud;
                case "fog": return "M4,8.5 H20 M4,12.5 H20 M6,16.5 H18";
                case "rain": return cloudHigh + " M8.5,16 L7.5,19.5 M12.5,16 L11.5,19.5 M16.5,16 L15.5,19.5";
                case "snow": return cloudHigh + " M8,17.5 A0.9,0.9 0 1 1 7.99,17.5 M12,19.5 A0.9,0.9 0 1 1 11.99,19.5 M16,17.5 A0.9,0.9 0 1 1 15.99,17.5";
                case "storm": return cloudHigh + " M13.5,14 L10,18.5 H13.5 L11,22.5";
                default: return cloud;
            }
        }
        // "14:00" or "2 PM", following the clock's choice.
        public static string HourLabel(DateTime time, bool use24) { return time.ToString(use24 ? "HH:mm" : "h tt", CultureInfo.InvariantCulture); }
        public void Dispose() { http.Dispose(); }
    }
}
