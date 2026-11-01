using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    // One Home Assistant device the card shows (docs/design/smart-home.md).
    public sealed class HomeEntity
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Domain { get { return Id == null || Id.IndexOf('.') < 0 ? "" : Id.Substring(0, Id.IndexOf('.')); } }
        public string State { get; set; }
        public bool Available { get { return State != "unavailable" && State != "unknown" && State != null; } }
        public bool On { get { return Domain == "climate" ? State != "off" && Available : State == "on"; } }
        // Lights that dim: 0–100. Null when the light doesn't dim, or is off.
        public int? Brightness { get; set; }
        public bool Dimmable { get; set; }
        // Climate: the room temperature, the target and the range and step the device allows.
        public double? Current { get; set; }
        public double? Target { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Step { get; set; }
        public string Unit { get; set; }
    }

    // Home Assistant's REST API on the home network (docs/design/smart-home.md): the address and a long-lived access
    // token, kept only in home-assistant.dat encrypted for the Windows account. Only private, link-local and loopback
    // addresses are reached, so the token can't be sent out to the internet. The card reads the chosen devices' states
    // and calls toggle, brightness and temperature services; nothing else.
    public sealed class HomeAssistantService : IDisposable
    {
        public static readonly string[] Domains = { "light", "switch", "climate" };
        public const int MaxEntities = 24;
        private readonly string storePath;
        private readonly HttpClient http = new HttpClient(new PreferIPv4Handler()) { Timeout = TimeSpan.FromSeconds(8) };
        private string token;
        private DateTime checkedAt = DateTime.MinValue;
        public Uri Address { get; private set; }
        public bool Connected { get { return Address != null && !String.IsNullOrEmpty(token); } }

        public static string PathFor(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "home-assistant.dat"); }

        public HomeAssistantService(string storePath)
        {
            this.storePath = storePath;
            try
            {
                if (!File.Exists(storePath)) return;
                var saved = new JavaScriptSerializer().Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(storePath), null, DataProtectionScope.CurrentUser)));
                Uri address;
                if (saved != null && saved.ContainsKey("url") && saved.ContainsKey("token") && Uri.TryCreate(saved["url"], UriKind.Absolute, out address)) { Address = address; token = saved["token"]; }
            }
            catch (Exception) { Address = null; token = null; }
        }

        // "http://homeassistant.local:8123", "192.168.1.20:8123" or "homeassistant:8123"; http is assumed, a trailing
        // slash or /lovelace path is dropped.
        public static Uri Normalize(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            if (!text.Contains("://")) text = "http://" + text;
            Uri uri;
            if (!Uri.TryCreate(text, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https") || String.IsNullOrEmpty(uri.Host)) return null;
            return new Uri(uri.Scheme + "://" + uri.Authority + "/");
        }

        // Home Assistant has to be on the home network: every address its name resolves to must be private, link-local
        // or loopback (the same rule as the phone link, DashboardController.IsLocal).
        public static async Task<bool> OnHomeNetwork(Uri address)
        {
            IPAddress ip;
            if (IPAddress.TryParse(address.Host.Trim('[', ']'), out ip)) return DashboardController.IsLocal(ip);
            try
            {
                var all = await Dns.GetHostAddressesAsync(address.Host);
                return all.Length > 0 && all.All(DashboardController.IsLocal);
            }
            catch (Exception) { return false; }
        }

        private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string body, Uri address, string key, CancellationToken cancel)
        {
            if (DateTime.UtcNow - checkedAt > TimeSpan.FromMinutes(5) || address != Address)
            {
                if (!await OnHomeNetwork(address)) throw new InvalidOperationException("That address isn't on your home network. Widget Screen only talks to Home Assistant on the local network.");
                if (address == Address) checkedAt = DateTime.UtcNow;
            }
            var request = new HttpRequestMessage(method, new Uri(address, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, cancel); }
            catch (HttpRequestException) { throw new InvalidOperationException("Home Assistant didn't answer. Check that it's running and the address is right."); }
            catch (TaskCanceledException) { if (cancel.IsCancellationRequested) throw; throw new InvalidOperationException("Home Assistant didn't answer in time."); }
            if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) { response.Dispose(); throw new InvalidOperationException("Home Assistant refused the token. Make a new long-lived access token in your Home Assistant profile."); }
            if (!response.IsSuccessStatusCode) { int status = (int)response.StatusCode; response.Dispose(); throw new InvalidOperationException("Home Assistant answered " + status + "."); }
            return response;
        }

        // Checks the address and token, then keeps them (encrypted). Returns Home Assistant's name for itself.
        public async Task<string> ConnectAsync(string url, string key, CancellationToken cancel)
        {
            var address = Normalize(url);
            if (address == null) throw new ArgumentException("Enter Home Assistant's address, such as http://homeassistant.local:8123.");
            if (String.IsNullOrWhiteSpace(key)) throw new ArgumentException("Paste a long-lived access token from your Home Assistant profile.");
            key = key.Trim();
            string name;
            using (var response = await Send(HttpMethod.Get, "api/config", null, address, key, cancel))
            {
                var config = new JavaScriptSerializer().DeserializeObject(await response.Content.ReadAsStringAsync()) as Dictionary<string, object>;
                name = config != null && config.ContainsKey("location_name") ? config["location_name"] as string : null;
            }
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new Dictionary<string, string> { { "url", address.ToString() }, { "token", key } })), null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storePath)));
            string temporary = storePath + ".tmp"; File.WriteAllBytes(temporary, bytes);
            if (File.Exists(storePath)) File.Replace(temporary, storePath, null); else File.Move(temporary, storePath);
            Address = address; token = key; checkedAt = DateTime.UtcNow;
            return String.IsNullOrWhiteSpace(name) ? "Home Assistant" : name;
        }

        public void Disconnect() { Address = null; token = null; if (File.Exists(storePath)) File.Delete(storePath); }

        // Every light, switch and climate device Home Assistant has, for the chooser and the card.
        public async Task<List<HomeEntity>> StatesAsync(CancellationToken cancel)
        {
            if (!Connected) throw new InvalidOperationException("Connect Home Assistant in Settings → Home.");
            using (var response = await Send(HttpMethod.Get, "api/states", null, Address, token, cancel))
                return Parse(await response.Content.ReadAsStringAsync());
        }
        public static List<HomeEntity> Parse(string json)
        {
            var result = new List<HomeEntity>();
            object[] list;
            try { list = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.DeserializeObject(json ?? "") as object[]; }
            catch (ArgumentException) { return result; }
            if (list == null) return result;
            foreach (var item in list.OfType<Dictionary<string, object>>())
            {
                string id = item.ContainsKey("entity_id") ? item["entity_id"] as string : null;
                if (id == null || !Domains.Contains(id.Split('.')[0])) continue;
                var attributes = item.ContainsKey("attributes") ? item["attributes"] as Dictionary<string, object> ?? new Dictionary<string, object>() : new Dictionary<string, object>();
                var entity = new HomeEntity { Id = id, State = item.ContainsKey("state") ? item["state"] as string : null };
                entity.Name = attributes.ContainsKey("friendly_name") && attributes["friendly_name"] is string ? (string)attributes["friendly_name"] : id;
                if (entity.Domain == "light")
                {
                    object modes; attributes.TryGetValue("supported_color_modes", out modes);
                    var list2 = modes as object[];
                    entity.Dimmable = list2 != null ? list2.OfType<string>().Any(m => m != "onoff") : attributes.ContainsKey("brightness");
                    double brightness = Number(attributes, "brightness");
                    if (!Double.IsNaN(brightness)) entity.Brightness = (int)Math.Round(brightness / 255 * 100);
                }
                if (entity.Domain == "climate")
                {
                    double current = Number(attributes, "current_temperature"), target = Number(attributes, "temperature");
                    entity.Current = Double.IsNaN(current) ? (double?)null : current; entity.Target = Double.IsNaN(target) ? (double?)null : target;
                    double min = Number(attributes, "min_temp"), max = Number(attributes, "max_temp"), step = Number(attributes, "target_temp_step");
                    entity.Min = Double.IsNaN(min) ? 7 : min; entity.Max = Double.IsNaN(max) ? 35 : max; entity.Step = Double.IsNaN(step) || step <= 0 ? 0.5 : step;
                    entity.Unit = attributes.ContainsKey("temperature_unit") ? attributes["temperature_unit"] as string : null;
                }
                result.Add(entity);
            }
            return result.OrderBy(e => Array.IndexOf(Domains, e.Domain)).ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        private static double Number(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || value == null) return Double.NaN;
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); } catch (Exception) { return Double.NaN; }
        }

        private async Task Call(string domain, string service, Dictionary<string, object> data, CancellationToken cancel)
        {
            if (!Connected) throw new InvalidOperationException("Connect Home Assistant in Settings → Home.");
            using (await Send(HttpMethod.Post, "api/services/" + domain + "/" + service, new JavaScriptSerializer().Serialize(data), Address, token, cancel)) { }
        }
        private static Dictionary<string, object> For(string id) { return new Dictionary<string, object> { { "entity_id", id } }; }
        private static void Check(string id, params string[] domains)
        {
            if (String.IsNullOrEmpty(id) || !domains.Contains(id.Split('.')[0]) || id.Any(c => !(Char.IsLetterOrDigit(c) || c == '_' || c == '.'))) throw new ArgumentException("That isn't a light, switch or thermostat.");
        }

        // A light or switch turns on or off; a thermostat turns on or off.
        public Task ToggleAsync(HomeEntity entity, CancellationToken cancel)
        {
            Check(entity.Id, Domains);
            return entity.Domain == "climate" ? Call("climate", entity.On ? "turn_off" : "turn_on", For(entity.Id), cancel) : Call(entity.Domain, "toggle", For(entity.Id), cancel);
        }
        public Task BrightnessAsync(string id, int percent, CancellationToken cancel)
        {
            Check(id, "light");
            var data = For(id); percent = Math.Max(0, Math.Min(100, percent));
            if (percent == 0) return Call("light", "turn_off", data, cancel);
            data["brightness_pct"] = percent;
            return Call("light", "turn_on", data, cancel);
        }
        public Task TemperatureAsync(HomeEntity entity, double value, CancellationToken cancel)
        {
            Check(entity.Id, "climate");
            var data = For(entity.Id); data["temperature"] = Math.Max(entity.Min, Math.Min(entity.Max, Math.Round(value / entity.Step) * entity.Step));
            return Call("climate", "set_temperature", data, cancel);
        }

        // "On · 60%", "Off", "21.5° → 23°", "Unavailable".
        public static string Describe(HomeEntity e)
        {
            if (!e.Available) return "Unavailable";
            if (e.Domain == "climate")
            {
                string unit = String.IsNullOrEmpty(e.Unit) ? "°" : e.Unit;
                string now = e.Current.HasValue ? e.Current.Value.ToString("0.#", CultureInfo.InvariantCulture) + unit : "";
                if (!e.On) return now.Length > 0 ? "Off  ·  " + now : "Off";
                return (now.Length > 0 ? now + "  →  " : "Set to ") + (e.Target.HasValue ? e.Target.Value.ToString("0.#", CultureInfo.InvariantCulture) + unit : "—");
            }
            return e.On ? (e.Brightness.HasValue ? "On  ·  " + e.Brightness.Value + "%" : "On") : "Off";
        }

        public void Dispose() { http.Dispose(); }
    }
}
