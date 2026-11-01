using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using WidgetScreen;

// Games (docs/design/games.md): reading Steam, Epic and Xbox libraries from fixture folders laid out like theirs, the
// order and wording, the launch addresses allowed, and the card. No game is ever started here.
internal static class GameTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, text); }
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        return true;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static string Manifest(string id, string name, int flags, long lastPlayed) { return "\"AppState\"\n{\n\t\"appid\"\t\t\"" + id + "\"\n\t\"name\"\t\t\"" + name + "\"\n\t\"StateFlags\"\t\t\"" + flags + "\"\n\t\"LastPlayed\"\t\t\"" + lastPlayed + "\"\n}\n"; }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Games reads a Steam library: two folders, installed games only, playtime from localconfig", () => {
            string root = Path.Combine(directory, "games-steam"), second = Path.Combine(directory, "games-steam-library");
            if (Directory.Exists(root)) Directory.Delete(root, true); if (Directory.Exists(second)) Directory.Delete(second, true);
            Write(Path.Combine(root, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + root.Replace("\\", "\\\\") + "\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + second.Replace("\\", "\\\\") + "\"\n\t\t// a comment\n\t}\n}\n");
            Write(Path.Combine(root, "steamapps", "appmanifest_228980.acf"), Manifest("228980", "Steamworks Common Redistributables", 4, 0));
            Write(Path.Combine(root, "steamapps", "appmanifest_100.acf"), Manifest("100", "Fixture Racer", 4, 1700000000));
            Write(Path.Combine(second, "steamapps", "appmanifest_200.acf"), Manifest("200", "Fixture \\\"Quest\\\"", 4, 0));
            Write(Path.Combine(second, "steamapps", "appmanifest_300.acf"), Manifest("300", "Still Downloading", 1026 & ~4, 0));
            Write(Path.Combine(root, "userdata", "42", "config", "localconfig.vdf"), "\"UserLocalConfigStore\"\n{\n\t\"Software\"\n\t{\n\t\t\"Valve\"\n\t\t{\n\t\t\t\"steam\"\n\t\t\t{\n\t\t\t\t\"apps\"\n\t\t\t\t{\n\t\t\t\t\t\"100\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"LastPlayed\"\t\t\"1790000000\"\n\t\t\t\t\t\t\"Playtime\"\t\t\"2533\"\n\t\t\t\t\t}\n\t\t\t\t\t\"200\"\n\t\t\t\t\t{\n\t\t\t\t\t\t\"Playtime\"\t\t\"0\"\n\t\t\t\t\t}\n\t\t\t\t}\n\t\t\t}\n\t\t}\n\t}\n}\n");
            var games = GameLibrary.Steam(root);
            Assert(games.Count == 2, "Steam games found: " + String.Join(", ", games.Select(g => g.Name)));
            var racer = games.Single(g => g.Id == "steam:100"); var quest = games.Single(g => g.Id == "steam:200");
            Assert(racer.PlaytimeMinutes == 2533 && racer.LastPlayedUtc == new DateTime(2026, 9, 21, 14, 13, 20, DateTimeKind.Utc) && racer.LaunchUri == "steam://rungameid/100" && racer.Store == "Steam", "Steam playtime, last played or launch is wrong.");
            Assert(quest.Name == "Fixture \"Quest\"" && quest.PlaytimeMinutes == 0 && !quest.LastPlayedUtc.HasValue, "A Steam game without play is wrong.");
            Assert(GameLibrary.Steam(Path.Combine(directory, "no-steam-here")).Count == 0 && GameLibrary.Steam(null).Count == 0, "A missing Steam folder isn't empty.");
        });
        check("Games reads Epic manifests and Xbox packages, and shows no playtime where the store keeps none", () => {
            string epic = Path.Combine(directory, "games-epic"); if (Directory.Exists(epic)) Directory.Delete(epic, true);
            Write(Path.Combine(epic, "a.item"), "{\"DisplayName\":\"Fixture League\u00ae\",\"AppName\":\"Sugar\",\"CatalogNamespace\":\"ns1\",\"CatalogItemId\":\"cat1\",\"AppCategories\":[\"public\",\"games\",\"applications\"],\"bIsIncompleteInstall\":false}");
            Write(Path.Combine(epic, "b.item"), "{\"DisplayName\":\"A Launcher Tool\",\"AppName\":\"Tool\",\"CatalogNamespace\":\"ns2\",\"CatalogItemId\":\"cat2\",\"AppCategories\":[\"applications\"]}");
            Write(Path.Combine(epic, "c.item"), "{\"DisplayName\":\"Half Installed\",\"AppName\":\"Half\",\"CatalogNamespace\":\"ns3\",\"CatalogItemId\":\"cat3\",\"AppCategories\":[\"games\"],\"bIsIncompleteInstall\":true}");
            Write(Path.Combine(epic, "d.item"), "not json");
            var epicGames = GameLibrary.Epic(epic);
            Assert(epicGames.Count == 1 && epicGames[0].Name == "Fixture League" && epicGames[0].LaunchUri == "com.epicgames.launcher://apps/ns1%3Acat1%3ASugar?action=launch&silent=true" && !epicGames[0].PlaytimeMinutes.HasValue && !epicGames[0].LastPlayedUtc.HasValue, "Epic games are read wrongly.");
            string xbox = Path.Combine(directory, "games-xbox"); if (Directory.Exists(xbox)) Directory.Delete(xbox, true);
            Write(Path.Combine(xbox, "Game", "MicrosoftGame.config"), "<?xml version=\"1.0\"?><Game configVersion=\"1\"><Identity Name=\"Fixture.Game\"/><ShellVisuals DefaultDisplayName=\"Fixture Horizon\"/><ExecutableList><Executable Name=\"game.exe\" Id=\"Game\"/></ExecutableList></Game>");
            Write(Path.Combine(xbox, "Game", "AppxManifest.xml"), "<?xml version=\"1.0\"?><Package xmlns=\"http://schemas.microsoft.com/appx/manifest/foundation/windows10\"><Applications><Application Id=\"App\" Executable=\"game.exe\"/></Applications></Package>");
            Write(Path.Combine(xbox, "NotAGame", "AppxManifest.xml"), "<Package/>");
            var packages = new[] { new KeyValuePair<string, string>("Fixture.Game_1.2.3.0_x64__8wekyb3d8bbwe", Path.Combine(xbox, "Game")), new KeyValuePair<string, string>("Microsoft.Notepad_11.0_x64__8wekyb3d8bbwe", Path.Combine(xbox, "NotAGame")), new KeyValuePair<string, string>("Gone_1_x64__abc", Path.Combine(xbox, "Missing")) };
            var xboxGames = GameLibrary.Xbox(packages);
            Assert(xboxGames.Count == 1 && xboxGames[0].Name == "Fixture Horizon" && xboxGames[0].LaunchUri == "shell:AppsFolder\\Fixture.Game_8wekyb3d8bbwe!App" && xboxGames[0].Id == "xbox:Fixture.Game_8wekyb3d8bbwe", "Xbox games are read wrongly.");
            Assert(GameLibrary.Detail(epicGames[0], DateTime.UtcNow) == "Epic" && GameLibrary.Playtime(null) == "" && GameLibrary.Playtime(0) == "Not played yet" && GameLibrary.Playtime(2533) == "42 h played" && GameLibrary.Playtime(35) == "35 m played", "Wording is wrong.");
            var ordered = GameLibrary.Order(new[] { new GameEntry { Name = "B" }, new GameEntry { Name = "a" }, new GameEntry { Name = "Old", LastPlayedUtc = new DateTime(2020, 1, 1) }, new GameEntry { Name = "New", LastPlayedUtc = new DateTime(2026, 1, 1) } });
            Assert(ordered.Select(g => g.Name).SequenceEqual(new[] { "New", "Old", "a", "B" }), "Games aren't recently played first, then by name.");
            Assert(DashboardController.LaunchAllowed("steam://rungameid/100") && DashboardController.LaunchAllowed(epicGames[0].LaunchUri) && DashboardController.LaunchAllowed(xboxGames[0].LaunchUri)
                && !DashboardController.LaunchAllowed("steam://rungameid/100;calc") && !DashboardController.LaunchAllowed("C:\\Windows\\System32\\cmd.exe") && !DashboardController.LaunchAllowed("shell:AppsFolder\\x!y & calc"), "Launch addresses aren't checked.");
            // This PC's own libraries, read only: whatever is there, nothing fails.
            var live = GameLibrary.Read(true, true, true);
            Assert(live.All(g => !String.IsNullOrWhiteSpace(g.Name) && DashboardController.LaunchAllowed(g.LaunchUri)), "This PC's library gave a game without a name or a safe launch address.");
        });
        check("Games settings are saved and the card lists games six a page inside the card", () => {
            string path = Path.Combine(directory, "games-settings.xml"), warning;
            Assert(new AppSettings().ShowSteamGames && new AppSettings().ShowEpicGames && new AppSettings().ShowXboxGames, "Stores aren't on by default.");
            SettingsStore.Save(path, new AppSettings { ShowEpicGames = false });
            Assert(!SettingsStore.Load(path, out warning).ShowEpicGames && SettingsStore.Load(path, out warning).ShowSteamGames, "Store choices weren't saved.");
            Assert(WidgetLayout.Ids.Contains("Games") && WidgetLayout.KeepsSix("Games") && !WidgetLayout.SpotifyDefault(new AppSettings()).First(p => p.Id == "Games").Visible, "Games aren't set up like Quick Launch.");
            // A labelled test fixture: made-up games.
            var fixture = new List<GameEntry>();
            for (int i = 0; i < 8; i++) fixture.Add(new GameEntry { Id = "steam:" + i, Name = "Fixture game " + i + (i == 0 ? " with a very long name indeed" : ""), Store = i % 3 == 0 ? "Steam" : i % 3 == 1 ? "Epic" : "Xbox", LaunchUri = "steam://rungameid/" + i, PlaytimeMinutes = i % 3 == 0 ? (int?)(i * 90) : null, LastPlayedUtc = i == 3 ? (DateTime?)DateTime.UtcNow.AddDays(-2) : null });
            foreach (var size in new[] { new Size(288, 204), new Size(472, 250), new Size(1000, 400) })
            {
                var settings = new AppSettings { TextScale = 1.4, ShowSteamGames = false, ShowEpicGames = false, ShowXboxGames = false };
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Games"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                SettingsStore.Save(path, settings);
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            controller.ShowGames(fixture);
                            var dashboard = (FrameworkElement)window.FindName("Dashboard");
                            dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                            var grid = (System.Windows.Controls.Primitives.UniformGrid)window.FindName("GamesGrid");
                            Assert(grid.Children.Count == 6 && ((System.Windows.Controls.StackPanel)window.FindName("GamesPages")).Visibility == Visibility.Visible && System.Windows.Automation.AutomationProperties.GetName((UIElement)grid.Children[0]) == "Play Fixture game 3", "The first page isn't six games, recently played first, at " + size + ".");
                            var card = (FrameworkElement)window.FindName("GamesCard");
                            foreach (var part in Descendants(card).OfType<System.Windows.Controls.Button>().Where(e => Shown(e, card) && e.ActualWidth > 0))
                            {
                                var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                Assert(box.Left >= 19.5 && box.Top >= 19.5 && box.Right <= card.ActualWidth - 19.5 && box.Bottom <= card.ActualHeight - 19.5 && part.ActualHeight >= 47.5, "A games button is outside the " + size + " card or shorter than 48: " + box);
                            }
                        }
                    }
                    finally { window.Close(); }
                }
            }
        });
    }
}
