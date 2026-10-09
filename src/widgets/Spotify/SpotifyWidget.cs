using System;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace WidgetScreen
{
    public sealed class SpotifyWidget : IDisposable
    {
        private readonly Window window;
        private readonly SpotifyService service;
        private readonly HttpClient images = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        private readonly PlaybackScrubber scrubber;
        private SpotifyPlayback playback;
        private SpotifyItem collection;
        private string likedUri, artUrl, kind = "Now", searchQuery = "";
        private bool? liked;
        private bool busy, likeBusy, commandBusy, disposed, active = true;
        private int generation, browseGeneration, offset;
        private DateTime nextPoll, lastLikeCheck, observed;
        public bool Connected { get { return service.Connected; } }
        public SpotifyWidget(Window window, SpotifyService service)
        {
            this.window = window; this.service = service;
            C<Button>("SpotifyNow").Click += (s, e) => Select("Now");
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
            C<Button>("SpotifyOpenTrack").Click += (s, e) => Open(playback == null || playback.Item == null ? null : playback.Item.WebUrl);
            C<Button>("SpotifyPrevious").Click += (s, e) => Command("previous");
            C<Button>("SpotifyNext").Click += (s, e) => Command("next");
            C<Button>("SpotifyPlay").Click += (s, e) => Command(playback != null && playback.Playing ? "pause" : "play");
            scrubber = new PlaybackScrubber(C<Slider>("SpotifySeek"), Seek, value => Text("SpotifyPosition", Time(value)));
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
            Text("SpotifyPosition", "—:—"); Text("SpotifyDuration", "—:—");
            Status(Connected ? "Account connected." : "Set up your account in Settings → Spotify.");
            C<StackPanel>("SpotifyResults").Children.Clear(); Select("Now"); UpdateMode();
        }
        private void Select(string selected)
        {
            browseGeneration++; kind = selected; offset = 0; collection = null;
            C<Grid>("SpotifyNowView").Visibility = selected == "Now" ? Visibility.Visible : Visibility.Collapsed;
            C<Grid>("SpotifyLibraryView").Visibility = selected == "Now" ? Visibility.Collapsed : Visibility.Visible;
            C<DockPanel>("SpotifySearchRow").Visibility = selected == "Search" ? Visibility.Visible : Visibility.Collapsed;
            C<DockPanel>("SpotifyCollectionRow").Visibility = Visibility.Collapsed;
            foreach (string tab in new[] { "Now", "Albums", "Playlists", "Search" })
                C<Button>("Spotify" + tab).SetResourceReference(Button.BorderBrushProperty, tab == selected ? "Accent" : "ControlBorder");
            C<StackPanel>("SpotifyResults").Children.Clear(); Paging(false, false); Text("SpotifyPageLabel", "");
            if (!Connected) { Status("Connect in Settings → Spotify to use your library."); return; }
            if (selected == "Search") { Status("Enter a song or artist, then tap Find."); C<TextBox>("SpotifyQuery").Focus(); }
            else if (selected != "Now") LoadPage();
            else Status(service.CanControl ? "Spotify connected" : "Reconnect in Settings → Spotify to enable seeking.");
        }
        private void Search()
        {
            kind = "Search"; offset = 0; collection = null; searchQuery = C<TextBox>("SpotifyQuery").Text.Trim(); LoadPage();
        }
        private void Paging(bool previous, bool next) { C<Button>("SpotifyPagePrevious").IsEnabled = previous; C<Button>("SpotifyPageNext").IsEnabled = next; }
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
                foreach (var item in page.Items)
                {
                    var label = new StackPanel();
                    var title = new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                    title.SetResourceReference(TextBlock.FontSizeProperty, "BodySize"); label.Children.Add(title);
                    var sub = new TextBlock { Text = item.Subtitle, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) };
                    sub.SetResourceReference(TextBlock.FontSizeProperty, "SmallSize"); sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); label.Children.Add(sub);
                    var row = new Button { Content = label, HorizontalContentAlignment = HorizontalAlignment.Stretch, MinHeight = 70, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), ToolTip = item.Name };
                    var selected = item;
                    row.Click += (s, e) => {
                        if (selected.IsTrack || selected.Uri.StartsWith("spotify:episode:")) Open(selected.WebUrl);
                        else { collection = selected; offset = 0; C<DockPanel>("SpotifyCollectionRow").Visibility = Visibility.Visible; Text("SpotifyCollectionTitle", selected.Name); LoadPage(); }
                    };
                    AutomationProperties.SetName(row, item.IsTrack ? "Open " + item.Name + " in Spotify" : "Browse " + item.Name);
                    C<StackPanel>("SpotifyResults").Children.Add(row);
                }
                C<ScrollViewer>("SpotifyResultsScroll").ScrollToTop();
                Paging(offset > 0, page.HasMore); Text("SpotifyPageLabel", page.Items.Length == 0 ? "" : (offset + 1) + "–" + (offset + page.Items.Length));
                Status(page.Items.Length == 0 ? "No results. Try another search or save music in Spotify." : collection == null && kind != "Search" ? "Choose an album or playlist to browse its songs." : "Tap a song to open it in Spotify.");
            }
            catch (Exception error) { if (!disposed && version == browseGeneration) { Status(SpotifyAccount.Message(error)); Paging(offset > 0, false); } }
        }
        private void UpdateMode()
        {
            bool item = Connected && playback != null && playback.Item != null;
            C<Button>("SpotifyLike").IsEnabled = item && playback.Item.IsTrack && liked.HasValue && !likeBusy;
            C<Button>("SpotifyLike").Content = liked == true ? "♥ Liked" : "♡ Like";
            AutomationProperties.SetName(C<Button>("SpotifyLike"), liked == true ? "Remove current song from Liked Songs" : "Save current song to Liked Songs");
            C<Button>("SpotifyOpenTrack").IsEnabled = item;
            foreach (string name in new[] { "SpotifyPrevious", "SpotifyPlay", "SpotifyNext" }) C<Button>(name).IsEnabled = item && service.CanControl && !commandBusy;
            C<Button>("SpotifyPlay").Content = item && playback.Playing ? "\uE769" : "\uE768";
            C<Slider>("SpotifySeek").ToolTip = service.CanControl ? "Drag or tap to seek. Arrow keys move five seconds." : "Reconnect in Settings → Spotify to enable seeking. Premium required.";
            UpdateTimeline();
        }
        public void Tick(bool enabled, bool quiet)
        {
            active = enabled;
            if (!enabled) { scrubber.End(false); return; }
            UpdateTimeline(); Refresh(quiet);
        }
        private void UpdateTimeline()
        {
            bool item = playback != null && playback.Item != null;
            double position = item ? playback.PositionMs + (playback.Playing ? (DateTime.UtcNow - observed).TotalMilliseconds : 0) : 0;
            position = item ? Math.Min(playback.DurationMs, position) : 0;
            scrubber.Update(item ? playback.Item.Uri : null, position, item ? playback.DurationMs : 0, item && service.CanControl && playback.CanSeek && !commandBusy);
            if (!scrubber.Active) Text("SpotifyPosition", item ? Time(position) : "—:—");
            Text("SpotifyDuration", item ? Time(playback.DurationMs) : "—:—");
        }
        public async void Refresh(bool quiet)
        {
            if (!active || !Connected || busy || disposed || commandBusy || scrubber.Active || DateTime.UtcNow < nextPoll) return;
            busy = true; int version = generation; nextPoll = DateTime.UtcNow.AddSeconds(quiet ? 30 : 10);
            try
            {
                var snapshot = await service.CurrentAsync(); if (disposed || !Connected || version != generation) return;
                playback = snapshot; observed = DateTime.UtcNow; var item = snapshot.Item;
                Text("SpotifyTitle", item == null ? "Nothing playing" : item.Name);
                Text("SpotifyArtist", item == null ? "Start music in Spotify on any device." : item.Subtitle);
                if (kind == "Now") Status(!service.CanControl ? "Reconnect in Settings → Spotify to enable seeking." : item == null ? "Spotify connected · waiting for playback" : snapshot.Playing ? "Playing on Spotify" : "Spotify playback paused");
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
                    Uri uri;
                    if (Uri.TryCreate(requestedArt, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.Host.EndsWith(".scdn.co", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            byte[] data = await images.GetByteArrayAsync(uri);
                            if (disposed || !Connected || artUrl != requestedArt || version != generation) return;
                            using (var stream = new MemoryStream(data))
                            {
                                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 192; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); C<Image>("SpotifyArt").Source = bitmap;
                            }
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
        private async void Seek(string uri, double position)
        {
            if (commandBusy || disposed) return;
            commandBusy = true; int version = generation; UpdateMode();
            try
            {
                await service.SeekAsync(uri, position);
                if (!disposed && version == generation && playback != null && playback.Item != null && playback.Item.Uri == uri)
                { playback.PositionMs = SpotifyService.SeekPosition(position, playback.DurationMs); observed = DateTime.UtcNow; Status("Playback position updated."); }
            }
            catch (Exception error) { if (!disposed && version == generation) Status(SpotifyAccount.Message(error)); }
            finally { commandBusy = false; if (!disposed) { nextPoll = DateTime.UtcNow.AddSeconds(2); UpdateMode(); } }
        }
        private async void Command(string command)
        {
            if (commandBusy || disposed) return;
            commandBusy = true; int version = generation; UpdateMode();
            try { await service.PlaybackAsync(command); if (!disposed && version == generation) { playback = null; nextPoll = DateTime.UtcNow.AddSeconds(1); Status("Updating playback…"); } }
            catch (Exception error) { if (!disposed && version == generation) Status(SpotifyAccount.Message(error)); }
            finally { commandBusy = false; if (!disposed) UpdateMode(); }
        }
        private static string Time(double ms) { var value = TimeSpan.FromMilliseconds(Math.Max(0, ms)); return ((int)value.TotalMinutes) + ":" + value.Seconds.ToString("00"); }
        public void Dispose() { disposed = true; generation++; browseGeneration++; scrubber.End(false); service.Dispose(); images.Dispose(); }
    }
}
