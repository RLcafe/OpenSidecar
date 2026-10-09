using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WidgetScreen
{
    // Library browsing (recent albums, playlists, search) with a now-playing strip: cover, song, artist and Like.
    public sealed class SpotifyWidget : IDisposable
    {
        private readonly Window window;
        private readonly SpotifyService service;
        private readonly HttpClient images = new HttpClient(new PreferIPv4Handler()) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        // Small in-memory cover cache for list rows; cleared when it grows, never written to disk.
        private readonly Dictionary<string, BitmapSource> covers = new Dictionary<string, BitmapSource>();
        private SpotifyPlayback playback;
        private SpotifyItem collection;
        private string likedUri, artUrl, kind = "Albums", searchQuery = "";
        private bool? liked;
        private bool busy, likeBusy, commandBusy, disposed, active = true;
        private int generation, browseGeneration, offset;
        private DateTime nextPoll, lastLikeCheck;
        public bool Connected { get { return service.Connected; } }
        public SpotifyWidget(Window window, SpotifyService service)
        {
            this.window = window; this.service = service;
            C<Button>("SpotifyAlbums").Click += (s, e) => Select("Albums");
            C<Button>("SpotifyPlaylists").Click += (s, e) => Select("Playlists");
            C<Button>("SpotifySearch").Click += (s, e) => Select("Search");
            C<Button>("SpotifyFind").Click += (s, e) => Search();
            C<TextBox>("SpotifyQuery").KeyDown += (s, e) => { if (e.Key == Key.Enter) { Search(); e.Handled = true; } };
            C<Button>("SpotifyPagePrevious").Click += (s, e) => { offset = Math.Max(0, offset - 10); LoadPage(); };
            C<Button>("SpotifyPageNext").Click += (s, e) => { offset += 10; LoadPage(); };
            C<Button>("SpotifyBack").Click += (s, e) => Select(kind);
            C<Button>("SpotifyOpenCollection").Click += (s, e) => Open(collection == null ? null : collection.WebUrl);
            C<Button>("SpotifyLike").Click += (s, e) => ToggleLike();
            AccountChanged();
        }
        private T C<T>(string name) where T : class { return (T)(object)window.FindName(name); }
        private void Text(string name, string value) { C<TextBlock>(name).Text = value; }
        private void Status(string value) { Text("SpotifyStatus", value); }
        private void Open(string url) { if (url == null) return; try { Launcher.Open(url); } catch (Exception) { Status("Could not open Spotify."); } }
        public void AccountChanged()
        {
            generation++; browseGeneration++; playback = null; likedUri = artUrl = null; liked = null; nextPoll = DateTime.MinValue;
            C<Image>("SpotifyArt").Source = null;
            Text("SpotifyTitle", Connected ? "Loading Spotify…" : "Your music, here");
            Text("SpotifyArtist", Connected ? "Checking current playback." : "Connect in Settings → Spotify.");
            Status(Connected ? "Account connected." : "Set up your account in Settings → Spotify.");
            C<StackPanel>("SpotifyResults").Children.Clear(); Select(kind); UpdateMode();
        }
        private void Select(string selected)
        {
            browseGeneration++; kind = selected; offset = 0; collection = null;
            C<DockPanel>("SpotifySearchRow").Visibility = selected == "Search" ? Visibility.Visible : Visibility.Collapsed;
            C<DockPanel>("SpotifyCollectionRow").Visibility = Visibility.Collapsed;
            foreach (string tab in new[] { "Albums", "Playlists", "Search" })
                C<Button>("Spotify" + tab).SetResourceReference(Button.BorderBrushProperty, tab == selected ? "Accent" : "ControlBorder");
            C<StackPanel>("SpotifyResults").Children.Clear(); Paging(false, false); Text("SpotifyPageLabel", "");
            if (!Connected) { Status("Connect in Settings → Spotify to use your library."); return; }
            if (selected == "Search") { Status("Enter a song or artist, then tap Find."); C<TextBox>("SpotifyQuery").Focus(); }
            else LoadPage();
        }
        private void Search()
        {
            kind = "Search"; offset = 0; collection = null; searchQuery = C<TextBox>("SpotifyQuery").Text.Trim(); LoadPage();
        }
        private void Paging(bool previous, bool next) { C<Button>("SpotifyPagePrevious").IsEnabled = previous; C<Button>("SpotifyPageNext").IsEnabled = next; C<DockPanel>("SpotifyPaging").Visibility = previous || next ? Visibility.Visible : Visibility.Collapsed; }
        private async void LoadPage()
        {
            int version = ++browseGeneration;
            C<StackPanel>("SpotifyResults").Children.Clear(); Paging(false, false); Text("SpotifyPageLabel", "");
            if (!Connected) { Status("Connect in Settings → Spotify to use your library."); return; }
            if (kind == "Search" && String.IsNullOrWhiteSpace(searchQuery)) { Status("Enter a song or artist to search."); return; }
            Status("Loading from Spotify…");
            try
            {
                var page = collection == null ? await service.BrowseAsync(kind, searchQuery, offset) : await service.TracksAsync(collection, offset);
                if (disposed || version != browseGeneration) return;
                foreach (var item in page.Items) C<StackPanel>("SpotifyResults").Children.Add(Row(item, version));
                C<ScrollViewer>("SpotifyResultsScroll").ScrollToTop();
                Paging(offset > 0, page.HasMore); Text("SpotifyPageLabel", page.Items.Length == 0 ? "" : (offset + 1) + "–" + (offset + page.Items.Length));
                string empty = kind == "Albums" && collection == null && service.CanReadRecent ? "No recently played albums yet. Play an album in Spotify." : "No results. Try another search or save music in Spotify.";
                Status(page.Items.Length == 0 ? empty
                    : collection == null && kind == "Albums" && !service.CanReadRecent ? "Showing saved albums. Reconnect in Settings → Spotify to see recently played albums."
                    : collection == null && kind != "Search" ? "Choose " + (kind == "Albums" ? "an album" : "a playlist") + " to browse its songs."
                    : collection != null && service.CanControl ? "Tap a song to play it here; Spotify continues through this " + (collection.Uri.StartsWith("spotify:album:") ? "album." : "playlist.")
                    : "Tap a song to open it in Spotify.");
            }
            catch (Exception error) { if (!disposed && version == browseGeneration) { Status(SpotifyAccount.Message(error)); Paging(offset > 0, false); } }
        }
        private Button Row(SpotifyItem item, int version)
        {
            var layout = new Grid();
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) }); layout.ColumnDefinitions.Add(new ColumnDefinition());
            var cover = new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(8), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
            cover.SetResourceReference(Border.BackgroundProperty, "ClockSurface");
            var fallback = new TextBlock { Text = item.Uri.StartsWith("spotify:playlist:") ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            fallback.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var art = new Image { Stretch = Stretch.UniformToFill };
            var coverContent = new Grid(); coverContent.Children.Add(fallback); coverContent.Children.Add(art); cover.Child = coverContent; layout.Children.Add(cover);
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(label, 1);
            var title = new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.FontSizeProperty, "BodySize"); label.Children.Add(title);
            var sub = new TextBlock { Text = item.Subtitle, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
            sub.SetResourceReference(TextBlock.FontSizeProperty, "SmallSize"); sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); label.Children.Add(sub);
            layout.Children.Add(label);
            var row = new Button { Content = layout, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 70, Padding = new Thickness(10, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), ToolTip = item.Name };
            var selected = item; var parent = collection;
            bool playsInCollection = parent != null && selected.IsTrack && selected.Position >= 0 && service.CanControl;
            row.Click += (s, e) => {
                if (playsInCollection) PlayInCollection(parent, selected);
                else if (selected.IsTrack || selected.Uri.StartsWith("spotify:episode:")) Open(selected.WebUrl);
                else { collection = selected; offset = 0; C<DockPanel>("SpotifyCollectionRow").Visibility = Visibility.Visible; Text("SpotifyCollectionTitle", selected.Name); LoadPage(); }
            };
            AutomationProperties.SetName(row, playsInCollection ? "Play " + item.Name + " in " + parent.Name : item.IsTrack ? "Open " + item.Name + " in Spotify" : "Browse " + item.Name);
            LoadCover(item.ImageUrl, art, version);
            return row;
        }
        private async void LoadCover(string url, Image target, int version)
        {
            if (!SpotifyService.IsImageUrl(url)) return;
            BitmapSource cached;
            if (covers.TryGetValue(url, out cached)) { target.Source = cached; return; }
            try
            {
                var bitmap = await Download(url, 104);
                if (disposed || version != browseGeneration) return;
                if (covers.Count >= 120) covers.Clear();
                covers[url] = bitmap; target.Source = bitmap;
            }
            catch (Exception) { }
        }
        private async Task<BitmapSource> Download(string url, int width)
        {
            byte[] data = await images.GetByteArrayAsync(new Uri(url));
            using (var stream = new MemoryStream(data))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = width; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }
        }
        private void UpdateMode()
        {
            bool item = Connected && playback != null && playback.Item != null;
            C<Button>("SpotifyLike").IsEnabled = item && playback.Item.IsTrack && liked.HasValue && !likeBusy;
            C<Button>("SpotifyLike").Content = liked == true ? "♥ Liked" : "♡ Like";
            AutomationProperties.SetName(C<Button>("SpotifyLike"), liked == true ? "Remove current song from Liked Songs" : "Save current song to Liked Songs");
        }
        public void Tick(bool enabled, bool quiet)
        {
            active = enabled;
            if (enabled) Refresh(quiet);
        }
        public async void Refresh(bool quiet)
        {
            if (!active || !Connected || busy || disposed || commandBusy || DateTime.UtcNow < nextPoll) return;
            busy = true; int version = generation; nextPoll = DateTime.UtcNow.AddSeconds(quiet ? 30 : 10);
            try
            {
                var snapshot = await service.CurrentAsync(); if (disposed || !Connected || version != generation) return;
                playback = snapshot; var item = snapshot.Item;
                Text("SpotifyTitle", item == null ? "Nothing playing" : item.Name);
                Text("SpotifyArtist", item == null ? "Start music in Spotify on any device." : item.Subtitle);
                if (likedUri != (item == null ? null : item.Uri)) { likedUri = item == null ? null : item.Uri; liked = null; }
                UpdateMode();
                if (active && item != null && item.IsTrack && (!liked.HasValue || DateTime.UtcNow - lastLikeCheck >= TimeSpan.FromMinutes(1)) && !likeBusy)
                {
                    try { bool saved = await service.IsLikedAsync(item.Uri); if (disposed || !Connected || version != generation) return; liked = saved; lastLikeCheck = DateTime.UtcNow; }
                    catch (Exception error) { if (disposed || version != generation) return; Status(SpotifyAccount.Message(error)); }
                }
                UpdateMode();
                string requestedArt = item == null ? null : item.ImageUrl;
                if (active && requestedArt != artUrl)
                {
                    artUrl = requestedArt; C<Image>("SpotifyArt").Source = null;
                    if (SpotifyService.IsImageUrl(requestedArt))
                    {
                        try
                        {
                            var bitmap = await Download(requestedArt, 128);
                            if (disposed || !Connected || artUrl != requestedArt || version != generation) return;
                            C<Image>("SpotifyArt").Source = bitmap;
                        }
                        catch (Exception) { artUrl = null; }
                    }
                }
            }
            catch (Exception error)
            {
                if (disposed || version != generation) return;
                nextPoll = DateTime.UtcNow.AddSeconds(60); playback = null; likedUri = artUrl = null; liked = null;
                Text("SpotifyTitle", "Spotify unavailable"); Text("SpotifyArtist", "Check your connection in Settings → Spotify.");
                C<Image>("SpotifyArt").Source = null;
                Status(SpotifyAccount.Message(error)); UpdateMode();
            }
            finally { busy = false; }
        }
        private async void ToggleLike()
        {
            if (likeBusy || playback == null || playback.Item == null || !liked.HasValue) return;
            int version = generation; string uri = playback.Item.Uri; bool target = !liked.Value; likeBusy = true; UpdateMode();
            try { await service.SetLikedAsync(uri, target); if (!disposed && version == generation && likedUri == uri) { liked = target; lastLikeCheck = DateTime.UtcNow; Status(target ? "Saved to your Liked Songs" : "Removed from your Liked Songs"); } }
            catch (Exception error) { if (!disposed && version == generation) Status(SpotifyAccount.Message(error)); }
            finally { likeBusy = false; if (!disposed) UpdateMode(); }
        }
        private async void PlayInCollection(SpotifyItem parent, SpotifyItem song)
        {
            if (commandBusy || disposed) return;
            commandBusy = true; int version = generation;
            Status("Starting " + song.Name + "…");
            try
            {
                bool noDevice = false;
                try { await service.PlayInCollectionAsync(parent.Uri, song.Position); }
                catch (SpotifyNoDeviceException) { noDevice = true; }
                if (noDevice)
                {
                    // No device is active: open the Spotify app and retry briefly while it starts.
                    if (disposed || version != generation) return;
                    Open("spotify:"); Status("Opening Spotify to play " + song.Name + "…");
                    bool started = false;
                    for (int attempt = 0; attempt < 6 && !started; attempt++)
                    {
                        await Task.Delay(2000); if (disposed || version != generation) return;
                        try { await service.PlayInCollectionAsync(parent.Uri, song.Position); started = true; }
                        catch (SpotifyNoDeviceException) { }
                    }
                    if (!started) { Status("Spotify is open but not ready. Play anything in Spotify once, then tap the song again."); return; }
                }
                if (!disposed && version == generation) { playback = null; nextPoll = DateTime.UtcNow.AddSeconds(1); Status("Playing " + song.Name + " from " + parent.Name + "."); }
            }
            catch (Exception error) { if (!disposed && version == generation) Status(SpotifyAccount.Message(error)); }
            finally { commandBusy = false; }
        }
        public void Dispose() { disposed = true; generation++; browseGeneration++; service.Dispose(); images.Dispose(); }
    }
}
