using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml.Linq;
using Microsoft.Win32;

namespace WidgetScreen
{
    // One installed game and how to start it.
    public sealed class GameEntry
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Store { get; set; }
        public string LaunchUri { get; set; }
        // Only what the store records on this PC; null when it doesn't (never estimated).
        public int? PlaytimeMinutes { get; set; }
        public DateTime? LastPlayedUtc { get; set; }
    }

    // The games installed on this PC from Steam, the Epic Games Launcher and the Xbox app (docs/design/games.md), read
    // from the files and registry those stores keep, without their apps running and without the network.
    public static class GameLibrary
    {
        // ----- Valve KeyValues (.vdf, .acf) -----

        // Nested "key" "value" / "key" { … } text, as a case-insensitive dictionary of strings and dictionaries.
        public static Dictionary<string, object> ParseVdf(string text)
        {
            int i = 0;
            return Block(text ?? "", ref i);
        }
        private static Dictionary<string, object> Block(string text, ref int i)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            string key = null;
            while (i < text.Length)
            {
                char c = text[i];
                if (Char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
                if (c == '}') { i++; return result; }
                if (c == '{') { i++; var child = Block(text, ref i); if (key != null) { result[key] = child; key = null; } continue; }
                string token = Token(text, ref i);
                if (key == null) key = token; else { result[key] = token; key = null; }
            }
            return result;
        }
        private static string Token(string text, ref int i)
        {
            var value = new StringBuilder();
            if (text[i] == '"')
            {
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length) { i++; value.Append(text[i] == 'n' ? '\n' : text[i] == 't' ? '\t' : text[i]); }
                    else value.Append(text[i]);
                    i++;
                }
                i++;
            }
            else while (i < text.Length && !Char.IsWhiteSpace(text[i]) && text[i] != '{' && text[i] != '}' && text[i] != '"') value.Append(text[i++]);
            return value.ToString();
        }
        private static object Path(Dictionary<string, object> root, params string[] keys)
        {
            object current = root;
            foreach (string key in keys)
            {
                var map = current as Dictionary<string, object>; object next;
                if (map == null || !map.TryGetValue(key, out next)) return null;
                current = next;
            }
            return current;
        }
        private static string Text(Dictionary<string, object> map, string key) { object value; return map != null && map.TryGetValue(key, out value) ? value as string : null; }
        private static long Number(Dictionary<string, object> map, string key) { long n; return long.TryParse(Text(map, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0; }
        private static DateTime? Unix(long seconds) { return seconds > 0 ? (DateTime?)new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(seconds) : null; }
        private static string Read(string path) { try { return File.Exists(path) ? File.ReadAllText(path) : null; } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; } }

        // ----- Steam -----

        // Tools Steam installs beside the games.
        private static readonly string[] SteamTools = { "Steamworks Common Redistributables", "Steam Linux Runtime", "Proton", "SteamVR", "Steam Controller Configs", "Wallpaper Engine" };
        public static string SteamRoot()
        {
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) { var path = key == null ? null : key.GetValue("SteamPath") as string; if (!String.IsNullOrEmpty(path)) return path.Replace('/', '\\'); } }
            catch (Exception) { }
            string fallback = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return Directory.Exists(fallback) ? fallback : null;
        }

        public static List<GameEntry> Steam(string root)
        {
            var games = new List<GameEntry>();
            if (String.IsNullOrEmpty(root) || !Directory.Exists(root)) return games;
            // Libraries: the Steam folder and every folder in libraryfolders.vdf.
            var libraries = new List<string> { root };
            var folders = ParseVdf(Read(System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf")));
            var list = Path(folders, "libraryfolders") as Dictionary<string, object> ?? folders;
            foreach (var entry in list.Values)
            {
                string path = entry is Dictionary<string, object> ? Text((Dictionary<string, object>)entry, "path") : entry as string;
                if (!String.IsNullOrEmpty(path) && !libraries.Any(l => String.Equals(l.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) libraries.Add(path);
            }
            // Playtime and last played live in the signed-in user's localconfig.vdf (the most recently written one).
            Dictionary<string, object> apps = null;
            try
            {
                var newest = Directory.Exists(System.IO.Path.Combine(root, "userdata")) ? Directory.GetDirectories(System.IO.Path.Combine(root, "userdata")).Select(d => System.IO.Path.Combine(d, "config", "localconfig.vdf")).Where(File.Exists).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() : null;
                if (newest != null) apps = Path(ParseVdf(Read(newest)), "UserLocalConfigStore", "Software", "Valve", "Steam", "apps") as Dictionary<string, object>;
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
            foreach (string library in libraries)
            {
                string steamapps = System.IO.Path.Combine(library, "steamapps");
                string[] manifests;
                try { manifests = Directory.Exists(steamapps) ? Directory.GetFiles(steamapps, "appmanifest_*.acf") : new string[0]; } catch (IOException) { continue; } catch (UnauthorizedAccessException) { continue; }
                foreach (string file in manifests)
                {
                    var state = Path(ParseVdf(Read(file)), "AppState") as Dictionary<string, object>;
                    string id = Text(state, "appid"), name = Text(state, "name");
                    if (String.IsNullOrEmpty(id) || String.IsNullOrWhiteSpace(name) || SteamTools.Any(t => name.StartsWith(t, StringComparison.OrdinalIgnoreCase))) continue;
                    // StateFlags 4 is "fully installed"; a game still downloading isn't listed yet.
                    if ((Number(state, "StateFlags") & 4) == 0) continue;
                    if (games.Any(g => g.Id == "steam:" + id)) continue;
                    var played = apps == null ? null : Path(apps, id) as Dictionary<string, object>;
                    long last = played != null && Number(played, "LastPlayed") > 0 ? Number(played, "LastPlayed") : Number(state, "LastPlayed");
                    games.Add(new GameEntry { Id = "steam:" + id, Name = name.Trim(), Store = "Steam", LaunchUri = "steam://rungameid/" + id,
                        PlaytimeMinutes = played != null && Text(played, "Playtime") != null ? (int?)Number(played, "Playtime") : null, LastPlayedUtc = Unix(last) });
                }
            }
            return games;
        }

        // ----- Epic Games -----

        public static string EpicManifests() { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests"); }

        // Epic keeps playtime online only, so its games have no playtime or last-played here.
        public static List<GameEntry> Epic(string folder)
        {
            var games = new List<GameEntry>();
            string[] files;
            try { files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.item") : new string[0]; } catch (IOException) { return games; } catch (UnauthorizedAccessException) { return games; }
            foreach (string file in files)
            {
                Dictionary<string, object> item;
                try { item = new JavaScriptSerializer().DeserializeObject(Read(file) ?? "") as Dictionary<string, object>; } catch (Exception) { continue; }
                if (item == null) continue;
                object categories, incomplete; item.TryGetValue("AppCategories", out categories); item.TryGetValue("bIsIncompleteInstall", out incomplete);
                var list = categories as object[];
                if (list == null || !list.OfType<string>().Contains("games") || (incomplete is bool && (bool)incomplete)) continue;
                string name = item.ContainsKey("DisplayName") ? item["DisplayName"] as string : null, app = item.ContainsKey("AppName") ? item["AppName"] as string : null;
                string ns = item.ContainsKey("CatalogNamespace") ? item["CatalogNamespace"] as string : null, catalog = item.ContainsKey("CatalogItemId") ? item["CatalogItemId"] as string : null;
                if (String.IsNullOrWhiteSpace(name) || String.IsNullOrEmpty(app) || String.IsNullOrEmpty(ns) || String.IsNullOrEmpty(catalog)) continue;
                games.Add(new GameEntry { Id = "epic:" + app, Name = name.Trim().TrimEnd('®', '™').Trim(), Store = "Epic",
                    LaunchUri = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(ns) + "%3A" + Uri.EscapeDataString(catalog) + "%3A" + Uri.EscapeDataString(app) + "?action=launch&silent=true" });
            }
            return games;
        }

        // ----- Xbox (Microsoft Store and Game Pass) -----

        // Installed packages for this Windows user: their full names and folders, from the AppModel repository.
        public static List<KeyValuePair<string, string>> InstalledPackages()
        {
            var result = new List<KeyValuePair<string, string>>();
            try
            {
                using (var packages = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"))
                    if (packages != null)
                        foreach (string name in packages.GetSubKeyNames())
                            using (var package = packages.OpenSubKey(name))
                            {
                                var folder = package == null ? null : package.GetValue("PackageRootFolder") as string;
                                if (!String.IsNullOrEmpty(folder)) result.Add(new KeyValuePair<string, string>(name, folder));
                            }
            }
            catch (Exception) { }
            return result;
        }

        // A package is a game when its folder has the GDK's MicrosoftGame.config. It starts through its family name and
        // app id; the store keeps no playtime on this PC.
        public static List<GameEntry> Xbox(IEnumerable<KeyValuePair<string, string>> packages)
        {
            var games = new List<GameEntry>();
            foreach (var package in packages)
            {
                try
                {
                    string config = System.IO.Path.Combine(package.Value, "MicrosoftGame.config");
                    if (!File.Exists(config)) continue;
                    var parts = package.Key.Split('_');
                    if (parts.Length < 2) continue;
                    string family = parts[0] + "_" + parts[parts.Length - 1];
                    var game = XDocument.Load(config).Root;
                    string name = null, app = null;
                    var visuals = game.Descendants().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals");
                    if (visuals != null && visuals.Attribute("DefaultDisplayName") != null) name = visuals.Attribute("DefaultDisplayName").Value;
                    string manifest = System.IO.Path.Combine(package.Value, "AppxManifest.xml");
                    if (File.Exists(manifest))
                    {
                        var application = XDocument.Load(manifest).Descendants().FirstOrDefault(e => e.Name.LocalName == "Application");
                        if (application != null && application.Attribute("Id") != null) app = application.Attribute("Id").Value;
                    }
                    if (app == null) { var executable = game.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable" && e.Attribute("Id") != null); if (executable != null) app = executable.Attribute("Id").Value; }
                    if (String.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:") || String.IsNullOrEmpty(app)) continue;
                    if (games.Any(g => g.Id == "xbox:" + family)) continue;
                    games.Add(new GameEntry { Id = "xbox:" + family, Name = name.Trim(), Store = "Xbox", LaunchUri = "shell:AppsFolder\\" + family + "!" + app });
                }
                catch (Exception) { }
            }
            return games;
        }

        // ----- Together -----

        // Recently played first (only where the store records it), then the rest by name.
        public static List<GameEntry> Order(IEnumerable<GameEntry> games)
        {
            return games.OrderByDescending(g => g.LastPlayedUtc.HasValue).ThenByDescending(g => g.LastPlayedUtc ?? DateTime.MinValue).ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        public static List<GameEntry> Read(bool steam, bool epic, bool xbox)
        {
            var games = new List<GameEntry>();
            if (steam) games.AddRange(Steam(SteamRoot()));
            if (epic) games.AddRange(Epic(EpicManifests()));
            if (xbox) games.AddRange(Xbox(InstalledPackages()));
            return Order(games);
        }

        // "42 h played", "35 m played", or nothing when the store doesn't say.
        public static string Playtime(int? minutes)
        {
            if (!minutes.HasValue || minutes.Value <= 0) return minutes.HasValue ? "Not played yet" : "";
            return minutes.Value >= 60 ? (minutes.Value / 60).ToString("N0", CultureInfo.InvariantCulture) + " h played" : minutes.Value + " m played";
        }
        // "Steam · 42 h played · 2 days ago".
        public static string Detail(GameEntry game, DateTime utcNow)
        {
            var parts = new List<string> { game.Store };
            string playtime = Playtime(game.PlaytimeMinutes); if (playtime.Length > 0) parts.Add(playtime);
            if (game.LastPlayedUtc.HasValue) parts.Add(ClipboardHistory.Ago(game.LastPlayedUtc.Value, utcNow));
            return String.Join("  ·  ", parts);
        }
    }
}
