using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WidgetScreen
{
    // The System stats readings, their graph styles (Settings → Stats) and what each one says, shared by the card and
    // the phone link (decision 016; docs/design/system-sensors.md). The phone's SystemText.java says the same.
    public static class SystemStyles
    {
        public static readonly string[] Ids = { "Cpu", "CpuTemp", "Gpu", "GpuTemp", "Vram", "Ram", "Fps", "Network", "Uptime" };
        public static readonly string[] Styles = { "Auto", "Line", "Bars", "Sparkline", "Ring", "Gauge" };
        public static readonly string[] HistoryStyles = { "Auto", "Line", "Bars", "Sparkline" };

        public static string Title(string id)
        {
            switch (id)
            {
                case "Cpu": return "CPU"; case "CpuTemp": return "CPU TEMP"; case "Gpu": return "GPU"; case "GpuTemp": return "GPU TEMP";
                case "Vram": return "VRAM"; case "Ram": return "RAM"; case "Fps": return "FPS"; case "Network": return "NET"; default: return "UPTIME";
            }
        }
        public static string Name(string id)
        {
            switch (id)
            {
                case "Cpu": return "CPU usage"; case "CpuTemp": return "CPU temperature"; case "Gpu": return "GPU usage"; case "GpuTemp": return "GPU temperature";
                case "Vram": return "Video memory (VRAM)"; case "Ram": return "Memory (RAM) usage"; case "Fps": return "Frame rate (FPS) of the game in front";
                case "Network": return "Network speed"; default: return "Time since restart";
            }
        }
        // The styles a reading can take: network has no single value for a dial, and uptime has no graph.
        public static string[] StylesFor(string id) { return id == "Uptime" ? new string[0] : id == "Network" ? HistoryStyles : Styles; }
        public static bool IsDial(string style) { return style == "Ring" || style == "Gauge"; }

        public static bool Shown(AppSettings s, string id)
        {
            switch (id)
            {
                case "Cpu": return s.ShowCpu; case "CpuTemp": return s.ShowCpuTemperature; case "Gpu": return s.ShowGpu; case "GpuTemp": return s.ShowGpuTemperature;
                case "Vram": return s.ShowVram; case "Ram": return s.ShowRam; case "Fps": return s.ShowFps; case "Network": return s.ShowNetwork; default: return s.ShowUptime;
            }
        }
        public static void Show(AppSettings s, string id, bool on)
        {
            switch (id)
            {
                case "Cpu": s.ShowCpu = on; break; case "CpuTemp": s.ShowCpuTemperature = on; break; case "Gpu": s.ShowGpu = on; break; case "GpuTemp": s.ShowGpuTemperature = on; break;
                case "Vram": s.ShowVram = on; break; case "Ram": s.ShowRam = on; break; case "Fps": s.ShowFps = on; break; case "Network": s.ShowNetwork = on; break; default: s.ShowUptime = on; break;
            }
        }
        public static string[] ShownIds(AppSettings s) { return Ids.Where(id => Shown(s, id)).ToArray(); }
        public static bool Any(AppSettings s) { return Ids.Any(id => Shown(s, id)); }

        // "Cpu=Ring;Fps=Bars"; unknown readings and styles are dropped, Auto isn't stored.
        public static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>();
            foreach (var part in (text ?? "").Split(';'))
            {
                int equals = part.IndexOf('='); if (equals <= 0) continue;
                string id = part.Substring(0, equals).Trim(), style = part.Substring(equals + 1).Trim();
                if (Ids.Contains(id) && StylesFor(id).Contains(style) && style != "Auto") result[id] = style;
            }
            return result;
        }
        public static string Write(Dictionary<string, string> styles) { return String.Join(";", Ids.Where(id => styles.ContainsKey(id) && styles[id] != "Auto").Select(id => id + "=" + styles[id])); }
        public static string Get(string text, string id) { string style; return Parse(text).TryGetValue(id, out style) ? style : "Auto"; }

        // What Auto means in each design: rings for usage, gauges for temperatures and a sparkline for the frame rate in
        // Medium; history lines in Large.
        public static string Resolve(string style, string id, CardSizing.Design design)
        {
            if (style != "Auto" && style != null) return style;
            if (design == CardSizing.Design.Large || id == "Network") return "Line";
            return id == "CpuTemp" || id == "GpuTemp" ? "Gauge" : id == "Fps" ? "Sparkline" : "Ring";
        }
    }

    public sealed class StatReading
    {
        public double? Value, Second;      // what the history graph keeps (°C, %, fps; network down and up)
        public double Percent;             // where a dial or bar stands, 0–100
        public string Text = "—", Detail = "", Short, Long;  // Short and Long: why there's no value
        public bool Warning;
        public bool Missing { get { return Short != null; } }
    }

    public static class SystemReadings
    {
        public const double HotCelsius = 85;

        // Why a reading is empty, short (a list row) and in full (details and tooltips).
        public static string Short(string state)
        {
            switch (state)
            {
                case "NotInstalled": return "Not installed"; case "Damaged": return "Repair sensors"; case "NeedsFullSensors": return "Needs admin";
                case "NeedsPawnIO": return "Needs PawnIO"; case "NoGame": return "No game"; default: return "Not reported";
            }
        }
        public static string Long(string state, string id)
        {
            switch (state)
            {
                case "NotInstalled": return "Hardware sensors aren't installed";
                case "Damaged": return "Sensor files damaged: run setup again";
                case "NeedsFullSensors": return id == "Fps" ? "Needs full-sensors mode or RivaTuner" : "Needs admin: full-sensors mode";
                case "NeedsPawnIO": return "Needs the PawnIO driver";
                case "NoGame": return "No game in front";
                default: return id == "Fps" ? "No frames counted" : "Not reported by this PC";
            }
        }

        // "62°C", or "144°F" with Settings → Weather in Fahrenheit; the unit is spelt out, as hardware readings are usually Celsius.
        public static string Degrees(double celsius, bool fahrenheit) { return Math.Round(fahrenheit ? celsius * 9 / 5 + 32 : celsius).ToString(CultureInfo.InvariantCulture) + (fahrenheit ? "°F" : "°C"); }

        // The frame-rate scale for dials and graphs: the nearest common refresh rate at or above the highest reading.
        public static double FpsScale(IEnumerable<double> history)
        {
            double top = history.DefaultIfEmpty(0).Max();
            foreach (double rate in new double[] { 60, 120, 144, 165, 240, 360 }) if (top <= rate) return rate;
            return Math.Ceiling(top / 60) * 60;
        }

        public static Dictionary<string, StatReading> From(SystemSnapshot s, bool fahrenheit, double fpsScale)
        {
            var r = new Dictionary<string, StatReading>();
            // The processor's and graphics card's short names under their readings (Sticker Sheet phase 3).
            r["Cpu"] = Percent(s != null && s.CpuAvailable ? (double?)s.Cpu : null, s == null ? null : SystemService.ShortModel(s.CpuName), "Unavailable");
            r["Gpu"] = Percent(s == null ? null : s.Gpu, s == null ? "" : s.Gpu.HasValue && !String.IsNullOrEmpty(SystemService.ShortModel(s.GpuName)) ? SystemService.ShortModel(s.GpuName) : s.GpuStatus ?? "Unavailable", "Unavailable");
            r["Ram"] = Percent(s != null && s.MemoryAvailable ? (double?)s.MemoryPercent : null, s == null || !s.MemoryAvailable ? "Unavailable" : s.UsedMemoryGB.ToString("0.0") + " / " + s.TotalMemoryGB.ToString("0.0") + " GB", "Unavailable");
            bool vram = s != null && s.VramUsedGB.HasValue && s.VramTotalGB > 0;
            r["Vram"] = Percent(vram ? (double?)(100 * s.VramUsedGB.Value / s.VramTotalGB.Value) : null, vram ? s.VramUsedGB.Value.ToString("0.0") + " / " + s.VramTotalGB.Value.ToString("0") + " GB" : "", s == null ? "Unavailable" : s.VramState);
            r["CpuTemp"] = Temperature(s == null ? null : s.CpuTemperature, s == null ? "Unavailable" : s.CpuTemperatureState, "CpuTemp", fahrenheit, "Package");
            r["GpuTemp"] = Temperature(s == null ? null : s.GpuTemperature, s == null ? "Unavailable" : s.GpuTemperatureState, "GpuTemp", fahrenheit, s == null ? "" : SystemService.ShortModel(s.GpuName));
            var fps = new StatReading();
            if (s != null && s.Fps.HasValue)
            {
                fps.Value = s.Fps; fps.Text = Math.Round(s.Fps.Value).ToString(CultureInfo.InvariantCulture); fps.Percent = Math.Min(100, 100 * s.Fps.Value / fpsScale);
                fps.Detail = (s.FpsApp ?? "") + (String.IsNullOrEmpty(s.FpsSource) ? "" : (String.IsNullOrEmpty(s.FpsApp) ? "" : "  ·  ") + s.FpsSource);
            }
            else Missing(fps, s == null ? "Unavailable" : s.FpsState, "Fps");
            r["Fps"] = fps;
            var network = new StatReading();
            if (s != null && s.NetworkAvailable) { network.Value = s.DownloadBytes; network.Second = s.UploadBytes; network.Text = SystemService.FormatRate(s.DownloadBytes); network.Detail = SystemService.FormatRate(s.UploadBytes); }
            else { network.Text = "— KB/s"; network.Detail = "— KB/s"; }
            r["Network"] = network;
            r["Uptime"] = new StatReading { Text = s == null ? "—" : SystemStatsCard.Uptime(s.Uptime, 3, false) };
            return r;
        }

        // The status sticker's peak (Sticker Sheet phase 3): the busiest of CPU, GPU, RAM and VRAM among those shown,
        // as "GPU 92%"; null when none has a value. The sticker fills from 80% (Strained).
        public const double Strained = 80;
        public static string PeakId(Dictionary<string, StatReading> readings, Func<string, bool> shown)
        {
            string best = null; double top = -1;
            foreach (string id in new[] { "Cpu", "Gpu", "Ram", "Vram" })
            {
                StatReading reading;
                if (!shown(id) || !readings.TryGetValue(id, out reading) || !reading.Value.HasValue) continue;
                if (reading.Percent > top) { top = reading.Percent; best = id; }
            }
            return best;
        }
        public static string PeakText(string id, StatReading reading) { return id == null ? "" : SystemStyles.Title(id) + " " + Math.Round(reading.Percent) + "%"; }
        // A temperature badge on a ring: "58°" in the chosen unit, no letter.
        public static string Badge(StatReading temperature) { return temperature == null || !temperature.Value.HasValue ? "" : temperature.Text.Replace("°C", "°").Replace("°F", "°"); }
        // "min 12% · max 87%" from a reading's history, or "" before two readings.
        public static string Range(string id, IList<double> history, bool fahrenheit)
        {
            if (history == null || history.Count < 2) return "";
            double low = history.Min(), high = history.Max();
            Func<double, string> show = v => id == "CpuTemp" || id == "GpuTemp" ? Degrees(v, fahrenheit).Replace("°C", "°").Replace("°F", "°") : id == "Fps" ? Math.Round(v).ToString(CultureInfo.InvariantCulture) : Math.Round(v) + "%";
            return "min " + show(low) + " · max " + show(high);
        }

        private static StatReading Percent(double? value, string detail, string state)
        {
            var reading = new StatReading { Detail = detail ?? "" };
            if (!value.HasValue) { reading.Text = "—"; if (state != null && state != "Unavailable") Missing(reading, state, ""); return reading; }
            reading.Value = value; reading.Percent = Math.Max(0, Math.Min(100, value.Value)); reading.Text = Math.Round(value.Value) + "%"; reading.Warning = value.Value >= 90;
            return reading;
        }
        private static StatReading Temperature(double? celsius, string state, string id, bool fahrenheit, string detail)
        {
            var reading = new StatReading();
            if (!celsius.HasValue) { Missing(reading, state ?? "Unavailable", id); return reading; }
            reading.Value = celsius; reading.Percent = Math.Max(0, Math.Min(100, celsius.Value)); reading.Text = Degrees(celsius.Value, fahrenheit); reading.Detail = detail; reading.Warning = celsius.Value >= HotCelsius;
            return reading;
        }
        private static void Missing(StatReading reading, string state, string id) { reading.Text = "—"; reading.Short = Short(state); reading.Long = Long(state, id); reading.Detail = reading.Long; }
    }
}
