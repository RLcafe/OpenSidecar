using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WidgetScreen
{
    // The Games widget (docs/design/games.md): installed games from Steam, Epic and the Xbox app in Quick Launch's
    // six-a-page grid, recently played first. The library is read off the UI thread when the card is shown and every
    // ten minutes while it is (twenty in Quiet mode); a tap starts the game through its store.
    public sealed partial class DashboardController
    {
        public static readonly TimeSpan GamesInterval = TimeSpan.FromMinutes(10), GamesQuietInterval = TimeSpan.FromMinutes(20);
        private List<GameEntry> games;
        private bool gamesBusy, gamesFailed;
        private DateTime lastGamesAttempt = DateTime.MinValue;
        private int gamePage;
        private string gameShape = "";
        // For the checks (tests/Games/GameTests.cs).
        public List<GameEntry> Games { get { return games; } }

        private void InitializeGames()
        {
            Button("GamesPrevious", () => { gamePage--; BuildGames(); });
            Button("GamesNext", () => { gamePage++; BuildGames(); });
            Button("GamesRefresh", () => RefreshGames());
        }

        private void TickGames()
        {
            if (Editor.IsVisible("Games") && DateTime.UtcNow - lastGamesAttempt >= (settings.QuietMode ? GamesQuietInterval : GamesInterval)) RefreshGames();
        }

        private async void RefreshGames()
        {
            if (disposed || gamesBusy) return;
            gamesBusy = true; lastGamesAttempt = DateTime.UtcNow;
            bool steam = settings.ShowSteamGames, epic = settings.ShowEpicGames, xbox = settings.ShowXboxGames;
            try { var found = await Task.Run(() => GameLibrary.Read(steam, epic, xbox)); if (!disposed) { games = found; gamesFailed = false; } }
            catch (Exception) { gamesFailed = true; }
            finally { gamesBusy = false; if (!disposed) BuildGames(); }
        }
        // Settings changed which stores to show: read again.
        private void ApplyGames() { lastGamesAttempt = DateTime.MinValue; if (games != null) { games = null; } BuildGames(); }
        // For the checks: show a labelled fixture library instead of reading this PC's.
        public void ShowGames(List<GameEntry> fixture) { games = GameLibrary.Order(fixture); gamesFailed = false; lastGamesAttempt = DateTime.UtcNow; BuildGames(); }

        private Size GameArea() { var b = Editor.Bounds("Games"); return new Size(Math.Max(0, b.Width - 42), Math.Max(0, b.Height - 90)); }
        private void FitGames() { var a = GameArea(); string shape = Math.Round(a.Width) + "x" + Math.Round(a.Height); if (shape != gameShape) BuildGames(); }

        // Store colours: Steam Glacier, Epic Lavender, Xbox Lime.
        private static string StoreHex(string store) { return store == "Steam" ? "#8ED8F8" : store == "Epic" ? "#C5AEFF" : "#BAF28B"; }

        private void BuildGames()
        {
            var grid = Control<UniformGrid>("GamesGrid"); grid.Children.Clear();
            var area = GameArea(); gameShape = Math.Round(area.Width) + "x" + Math.Round(area.Height);
            var list = games ?? new List<GameEntry>();
            int[] shape = CardSizing.DeckGrid(area.Width, area.Height, list.Count);
            int pageSize = shape[0] * shape[1], pages = Math.Max(1, (list.Count + pageSize - 1) / pageSize);
            double cellWidth = area.Width / shape[0], cellHeight = area.Height / shape[1];
            var labelMode = CardSizing.DeckLabel(cellWidth, cellHeight); bool beside = labelMode == CardSizing.DeckLabelMode.Beside;
            gamePage = Math.Max(0, Math.Min(pages - 1, gamePage));
            grid.Columns = shape[0]; grid.Rows = shape[1];
            double cardWidth = Editor.Bounds("Games").Width;
            Control<StackPanel>("GamesPages").Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            Control<TextBlock>("GamesHeading").Visibility = CardSizing.HeadingShown(cardWidth, pages > 1) ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("GamesPrevious").IsEnabled = gamePage > 0; Control<Button>("GamesNext").IsEnabled = gamePage < pages - 1;
            Text("GamesPageLabel", (gamePage + 1) + "/" + pages);
            Control<Button>("GamesRefresh").IsEnabled = !gamesBusy;
            var empty = Control<TextBlock>("GamesEmpty");
            empty.Text = games != null && games.Count > 0 ? "" : gamesFailed ? "The game library couldn't be read. Tap the refresh button to try again."
                : games == null ? "Looking for games…" : "No games found from Steam, Epic or the Xbox app. Choose the stores in Settings → Widgets.";
            empty.Visibility = empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            var now = DateTime.UtcNow;
            for (int i = gamePage * pageSize; i < Math.Min(list.Count, (gamePage + 1) * pageSize); i++)
            {
                var game = list[i];
                string named = labelMode == CardSizing.DeckLabelMode.None ? game.Name + Environment.NewLine : "";
                var button = new Button { Margin = new Thickness(4), Padding = new Thickness(8), ToolTip = named + GameLibrary.Detail(game, now) };
                double glyphSize = CardSizing.DeckGlyph(cellHeight, beside);
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Orientation = beside ? Orientation.Horizontal : Orientation.Vertical };
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(StoreHex(game.Store))); brush.Freeze();
                var glyph = new TextBlock { Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = glyphSize, Foreground = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                content.Children.Add(glyph);
                var name = new TextBlock { Text = game.Name, FontSize = (beside ? 14 : 15) * settings.TextScale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = beside ? new Thickness(8, 0, 0, 0) : new Thickness(0, 8, 0, 0), MaxWidth = Math.Max(40, beside ? cellWidth - glyphSize - 40 : cellWidth - 24), TextTrimming = TextTrimming.CharacterEllipsis };
                if (labelMode == CardSizing.DeckLabelMode.None) name.Visibility = Visibility.Collapsed;
                content.Children.Add(name);
                // Large cells are tiles: the glyph in the store's colour, the name, and what the store says about it.
                if (CardSizing.Tile(cellWidth, cellHeight) == CardSizing.Design.Large)
                {
                    button.Padding = new Thickness(16, 12, 12, 12); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    content.HorizontalAlignment = HorizontalAlignment.Left; content.VerticalAlignment = VerticalAlignment.Stretch;
                    glyph.HorizontalAlignment = HorizontalAlignment.Left; glyph.FontSize = Math.Min(40, glyphSize);
                    name.HorizontalAlignment = HorizontalAlignment.Left; name.FontWeight = FontWeights.SemiBold; name.FontSize = 17 * settings.TextScale; name.Margin = new Thickness(0, 12, 0, 0); name.MaxWidth = cellWidth - 44;
                    var detail = new TextBlock { Text = GameLibrary.Detail(game, now), FontSize = 13, Margin = new Thickness(0, 3, 0, 0), MaxWidth = cellWidth - 44, TextTrimming = TextTrimming.CharacterEllipsis };
                    detail.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(detail);
                }
                button.Content = content; AutomationProperties.SetName(button, "Play " + game.Name);
                var target = game; button.Click += (sender, e) => LaunchGame(target);
                grid.Children.Add(button);
            }
        }

        // Only the three stores' own launch addresses are opened.
        public static bool LaunchAllowed(string uri)
        {
            return Regex.IsMatch(uri ?? "", @"^steam://rungameid/\d+$")
                || Regex.IsMatch(uri ?? "", @"^com\.epicgames\.launcher://apps/[A-Za-z0-9%._-]+\?action=launch&silent=true$")
                || Regex.IsMatch(uri ?? "", @"^shell:AppsFolder\\[A-Za-z0-9._-]+_[a-z0-9]{13}![A-Za-z0-9._-]+$");
        }
        private string LaunchGame(GameEntry game)
        {
            try
            {
                if (!LaunchAllowed(game.LaunchUri)) throw new InvalidOperationException("That game's launch address doesn't look right.");
                ProcessLauncher.Start(game.Store == "Xbox" ? "explorer.exe" : game.LaunchUri, game.Store == "Xbox" ? game.LaunchUri : "", null);
                Notify("Starting " + game.Name + " through " + (game.Store == "Xbox" ? "the Xbox app" : game.Store) + ".");
                return null;
            }
            catch (Exception e) { string message = "Couldn't start " + game.Name + ": " + e.Message; Notify(message); return message; }
        }

        // ----- The phone -----

        private Dictionary<string, object> RemoteGames()
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return D("read", games != null, "failed", gamesFailed, "items", (games ?? new List<GameEntry>()).Take(120).Select(g => (object)D("id", g.Id, "name", g.Name, "store", g.Store,
                "playtime", g.PlaytimeMinutes.HasValue ? (object)g.PlaytimeMinutes.Value : null, "lastPlayed", g.LastPlayedUtc.HasValue ? (object)(g.LastPlayedUtc.Value - epoch).TotalMilliseconds : null)).ToArray());
        }
        private RemoteResponse RemoteGameCommand(string action, Dictionary<string, object> body)
        {
            if (action == "games.refresh") { lastGamesAttempt = DateTime.MinValue; RefreshGames(); return Done(null); }
            var game = (games ?? new List<GameEntry>()).FirstOrDefault(g => g.Id == Str(body, "id"));
            if (game == null) throw new InvalidOperationException("That game isn't installed on the PC anymore.");
            string error = LaunchGame(game);
            if (error != null) throw new InvalidOperationException(error);
            return Done("Starting " + game.Name + " on the PC.");
        }
    }
}
