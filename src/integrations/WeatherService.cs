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
    public sealed class WeatherSnapshot
    {
        public double Temperature, FeelsLike, Wind;
        public int Code;
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
        public async Task<WeatherSnapshot> ReadAsync(double latitude, double longitude)
        {
            string url = "https://api.open-meteo.com/v1/forecast?latitude=" + latitude.ToString(CultureInfo.InvariantCulture) + "&longitude=" + longitude.ToString(CultureInfo.InvariantCulture) + "&current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m&timezone=auto";
            return Parse(await http.GetStringAsync(url));
        }
        public static WeatherSnapshot Parse(string json)
        {
            var root = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.ContainsKey("current")) throw new InvalidOperationException("Weather data is unavailable.");
            var current = (Dictionary<string, object>)root["current"];
            foreach (string key in new[] { "temperature_2m", "apparent_temperature", "wind_speed_10m", "weather_code" })
                if (!current.ContainsKey(key) || current[key] == null) throw new InvalidOperationException("Weather data is incomplete.");
            return new WeatherSnapshot {
                Temperature = Convert.ToDouble(current["temperature_2m"], CultureInfo.InvariantCulture),
                FeelsLike = Convert.ToDouble(current["apparent_temperature"], CultureInfo.InvariantCulture),
                Wind = Convert.ToDouble(current["wind_speed_10m"], CultureInfo.InvariantCulture),
                Code = Convert.ToInt32(current["weather_code"], CultureInfo.InvariantCulture), Retrieved = DateTime.Now
            };
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
        public void Dispose() { http.Dispose(); }
    }
}
