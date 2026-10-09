using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WidgetScreen;

internal static class SpotifyTests
{
    private const string Track = "spotify:track:1234567890123456789012";
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private sealed class Handler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Send;
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation) { Calls++; return Send(request); }
    }
    private static Task<HttpResponseMessage> Reply(HttpStatusCode status, string body)
    {
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body ?? "") });
    }
    private static string Session(string directory, string name, bool expired)
    {
        string path = Path.Combine(directory, "spotify-" + name + ".dat");
        SpotifyCredentials.Save(path, new SpotifySession { ClientId = new string('a', 32), AccessToken = "test-access", RefreshToken = "test-refresh", Scope = SpotifyService.Scopes, ExpiresUtc = expired ? DateTime.MinValue : DateTime.UtcNow.AddHours(1) }); return path;
    }
    private static string Failure(Func<Task> action)
    {
        try { action().GetAwaiter().GetResult(); } catch (Exception e) { return e.Message; } throw new Exception("Expected failure was silently accepted.");
    }
    public static void Run(string directory, Action<string, Action> check)
    {
        check("HTTPS uses operating-system TLS defaults and Framework 4.8 targeting", () => {
            var target = (System.Runtime.Versioning.TargetFrameworkAttribute)Attribute.GetCustomAttribute(typeof(AppSettings).Assembly, typeof(System.Runtime.Versioning.TargetFrameworkAttribute));
            Assert(target != null && target.FrameworkName == ".NETFramework,Version=v4.8", "Application silently targets legacy Framework networking.");
            Assert(ServicePointManager.SecurityProtocol == SecurityProtocolType.SystemDefault, "HTTPS is pinned to legacy TLS protocols.");
        });
        check("Spotify browser callback waits for encrypted tokens before claiming success", () => {
            RunCallback(directory, true);
        });
        check("Spotify failed token exchange returns failure in browser and remains disconnected", () => {
            RunCallback(directory, false);
        });
        check("Spotify Settings saves Client ID before sign-in and reports real callback outcome", () => {
            var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            try
            {
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var owner = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    foreach (bool success in new[] { true, false })
                    {
                        Task<string> callback = null; bool idSaved = false; int changed = 0;
                        var handler = new Handler { Send = request => Reply(success ? HttpStatusCode.OK : HttpStatusCode.BadRequest, success ? "{\"access_token\":\"panel-access\",\"refresh_token\":\"panel-refresh\",\"expires_in\":3600}" : "{\"error\":\"invalid_grant\"}") };
                        using (var service = new SpotifyService(Session(directory, success ? "panel-success" : "panel-failure", false), handler, url => {
                            Assert(idSaved, "Browser opened before Client ID was saved.");
                            string state = HttpUtility.ParseQueryString(new Uri(url).Query)["state"]; callback = SendCallbackAsync("/callback?state=" + Uri.EscapeDataString(state) + "&code=test-code");
                        }))
                        {
                            service.Disconnect();
                            using (var account = new SpotifyAccount(owner, service, service.ClientId, id => { idSaved = true; }, () => changed++))
                            {
                                var button = (Button)owner.FindName("SpotifyConnectButton"); var status = (TextBlock)owner.FindName("SpotifyConnectionStatus");
                                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                for (int i = 0; i < 30 && !button.IsEnabled; i++) Pump();
                                Assert(idSaved && button.IsEnabled && (success ? service.Connected && changed == 1 && status.Text.Contains("saved") : !service.Connected && changed == 0 && status.Text.Contains("Connect again")), "Settings did not follow the actual connection result.");
                                Assert(callback != null && callback.Wait(1000), "Browser callback did not finish.");
                            }
                        }
                    }
                    owner.Close();
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        });
        check("Spotify PKCE and callback reject forged or incomplete sign-in", () => {
            Assert(SpotifyService.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "PKCE differs from RFC 7636 example.");
            string code, error;
            Assert(SpotifyService.ReadCallback("GET /callback?state=expected&code=abc%2B123 HTTP/1.1", "expected", out code, out error) && code == "abc+123", "Valid callback rejected.");
            Assert(!SpotifyService.ReadCallback("GET /callback?state=forged&code=abc HTTP/1.1", "expected", out code, out error), "Forged state accepted.");
            Assert(!SpotifyService.ReadCallback("GET /callback?state=expected HTTP/1.1", "expected", out code, out error), "Missing code accepted.");
            Assert(!SpotifyService.ReadCallback("POST /callback?state=expected&code=abc HTTP/1.1", "expected", out code, out error), "Unexpected method accepted.");
            Assert(SpotifyService.ReadCallback("GET /callback?state=expected&error=access_denied HTTP/1.1", "expected", out code, out error) && error == "access_denied", "Denied authorization lost.");
            Assert(SpotifyService.WebUrl("spotify:track:../../../../dangerous") == null && SpotifyService.WebUrl(Track).StartsWith("https://open.spotify.com/track/"), "Unsafe Spotify link accepted.");
        });
        check("Spotify tokens encrypted and disconnect survives restart", () => {
            string path = Session(directory, "encrypted", false);
            Assert(!System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("test-refresh"), "Token stored as plaintext.");
            Assert(SpotifyCredentials.Load(path).RefreshToken == "test-refresh", "Protected session did not round trip.");
            using (var service = new SpotifyService(path)) { service.Disconnect(); Assert(!service.Connected && service.ClientId.Length == 32, "Disconnect did not clear tokens."); }
            using (var service = new SpotifyService(path)) Assert(!service.Connected, "Disconnected account reappeared after restart.");
        });
        check("Spotify saved connection restores in a fresh process independent of working directory", () => {
            string path = Session(directory, "process-restart", false);
            var start = new System.Diagnostics.ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,
                "--spotify-restore-check \"" + path + "\"") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
            using (var process = System.Diagnostics.Process.Start(start))
            { Assert(process.WaitForExit(10000) && process.ExitCode == 0, "Fresh process lost the saved Client ID, scopes or refresh token."); }
        });
        check("Spotify Client ID persists through normal Settings save, cancellation and restart", () => {
            string path = Path.Combine(directory, "spotify-preferences.xml");
            SettingsStore.Save(path, new AppSettings());
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                {
                    ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((TextBox)window.FindName("SpotifyClientId")).Text = new string('a', 32);
                    ((Button)window.FindName("SpotifySaveId")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((Button)window.FindName("CancelSettings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((Button)window.FindName("SettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    ((Button)window.FindName("SaveSettings")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                window.Close();
            }
            string warning; Assert(SettingsStore.Load(path, out warning).SpotifyClientId == new string('a', 32), "Another Settings save dropped the Client ID.");
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true)) Assert(((TextBox)window.FindName("SpotifyClientId")).Text == new string('a', 32), "Restart lost the Client ID.");
                window.Close();
            }
        });
        check("Spotify transient refresh errors preserve encrypted login", () => {
            foreach (var status in new[] { HttpStatusCode.BadRequest, HttpStatusCode.ServiceUnavailable })
            {
                string path = Session(directory, "transient-" + status, true);
                var handler = new Handler { Send = request => Reply(status, "{\"error\":\"temporarily_unavailable\"}") };
                using (var service = new SpotifyService(path, handler)) { Failure(async () => { await service.CurrentAsync(); }); Assert(service.Connected, "Temporary token error erased login."); }
                using (var service = new SpotifyService(path)) Assert(service.Connected, "Temporary error erased stored login.");
            }
        });
        check("Spotify layout keeps independent media widgets and can be cancelled", () => {
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (Window)System.Windows.Markup.XamlReader.Load(stream); var editor = new LayoutEditor(window); editor.Apply(new AppSettings());
                var original = editor.Bounds("Media"); editor.Begin(); editor.ArrangeSpotify();
                Assert(editor.IsVisible("Spotify") && editor.IsVisible("Media") && editor.IsVisible("Focus") && editor.IsVisible("Shortcuts") && !editor.IsVisible("System") && !editor.IsVisible("Weather") && !editor.HasOverlaps(), "Spotify arrangement lost required widgets or overlaps.");
                editor.Cancel(); Assert(editor.Bounds("Media") == original && !editor.IsVisible("Spotify"), "Layout preview changed saved layout.");
                editor.Begin(); editor.Reset(); Assert(editor.IsVisible("Spotify") && !editor.IsVisible("System") && !editor.IsVisible("Weather"), "Reset did not use the chosen default.");
                window.Close();
            }
        });
        check("Spotify album and playlist songs use inline collection endpoints", () => {
            var handler = new Handler { Send = request => {
                Assert(HttpUtility.ParseQueryString(request.RequestUri.Query)["offset"] == "10", "Collection paging lost offset.");
                bool album = request.RequestUri.AbsolutePath.EndsWith("/tracks");
                Assert(album || request.RequestUri.AbsolutePath.EndsWith("/items"), "Wrong playlist content endpoint.");
                return Reply(HttpStatusCode.OK, album ? "{\"items\":[{\"name\":\"Album song\",\"uri\":\"" + Track + "\"}]}" : "{\"items\":[null,{\"item\":{\"name\":\"Playlist song\",\"uri\":\"" + Track + "\"}}],\"next\":\"next\"}");
            } };
            using (var service = new SpotifyService(Session(directory, "collection-tracks", false), handler))
            {
                foreach (string kind in new[] { "album", "playlist" })
                {
                    var page = service.TracksAsync(new SpotifyItem { Uri = "spotify:" + kind + ":1234567890123456789012" }, 10).Result;
                    Assert(page.Items.Length == 1 && page.Items[0].IsTrack && page.HasMore == (kind == "playlist"), "Collection song wrapper or paging lost.");
                    Assert(page.Items[0].Position == (kind == "playlist" ? 11 : 10), "Song position ignores paging or skipped items.");
                }
            }
        });
        check("Spotify Albums lists recently played albums, with saved albums for older connections", () => {
            Func<string, string, string> played = (album, name) => "{\"track\":{\"uri\":\"" + Track + "\",\"album\":{\"name\":\"" + name + "\",\"uri\":\"spotify:album:" + album + "\",\"images\":[{\"url\":\"https://i.scdn.co/image/" + album + "\"}]}}}";
            var history = new List<string>();
            for (int i = 0; i < 14; i++) { string id = (1000000000000000000000m + i).ToString(); history.Add(played(id, "Album " + i)); history.Add(played(id, "Album " + i + " again")); }
            history.Insert(1, played("1000000000000000000003", "Album 3 early"));
            var recent = new JavaScriptSerializer().DeserializeObject("{\"items\":[" + String.Join(",", history) + "]}");
            var first = SpotifyService.RecentAlbums(recent, 0); var second = SpotifyService.RecentAlbums(recent, 10);
            Assert(first.Items.Length == 10 && first.HasMore && first.Items[0].Name == "Album 0" && first.Items[1].Name == "Album 3 early" && first.Items[2].Name == "Album 1", "Recent albums are not distinct or not most recent first.");
            Assert(second.Items.Length == 4 && !second.HasMore && second.Items[0].ImageUrl.StartsWith("https://i.scdn.co/"), "Recent album paging or cover lost.");
            Assert(SpotifyService.IsImageUrl("https://i.scdn.co/image/a") && SpotifyService.IsImageUrl("https://image-cdn-ak.spotifycdn.com/image/a") && SpotifyService.IsImageUrl("https://mosaic.scdn.co/640/a")
                && !SpotifyService.IsImageUrl("http://i.scdn.co/image/a") && !SpotifyService.IsImageUrl("https://example.com/scdn.co") && !SpotifyService.IsImageUrl(null), "Cover host filter is wrong.");
            string requested = null;
            var handler = new Handler { Send = request => { requested = request.RequestUri.AbsolutePath; return Reply(HttpStatusCode.OK, "{\"items\":[]}"); } };
            using (var service = new SpotifyService(Session(directory, "recent-albums", false), handler)) { service.BrowseAsync("Albums", "", 0).Wait(); Assert(requested == "/v1/me/player/recently-played", "Albums did not use recently played."); }
            string legacy = Session(directory, "recent-old-scope", false); var session = SpotifyCredentials.Load(legacy); session.Scope = session.Scope.Replace("user-read-recently-played ", ""); SpotifyCredentials.Save(legacy, session);
            using (var service = new SpotifyService(legacy, handler)) { service.BrowseAsync("Albums", "", 0).Wait(); Assert(!service.CanReadRecent && requested == "/v1/me/albums", "Older connection lost its saved albums."); }
        });
        check("Spotify widget starts without network calls and loads Albums on its first visible tick", () => {
            // The app builds the dashboard before the dispatcher runs; a request started then once crashed the app on resume.
            var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var owner = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    var paths = new List<string>();
                    var handler = new Handler { Send = request => { lock (paths) paths.Add(request.RequestUri.AbsolutePath); return Reply(HttpStatusCode.OK, "{\"items\":[]}"); } };
                    using (var widget = new SpotifyWidget(owner, new SpotifyService(Session(directory, "startup", false), handler)))
                    {
                        Pump(); Assert(handler.Calls == 0, "Spotify widget made requests while the dashboard was being built.");
                        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
                        widget.Tick(false, false); Pump(); Assert(handler.Calls == 0, "Hidden Spotify widget loaded its library.");
                        widget.Tick(true, false); Pump();
                        lock (paths) Assert(paths.Contains("/v1/me/player/recently-played"), "Albums did not load on the first visible tick.");
                        int calls = handler.Calls; widget.Tick(true, false); Pump(); Assert(handler.Calls == calls, "Albums reloaded on every tick.");
                    }
                    owner.Close();
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        });
        check("Spotify songs tapped in a playlist or album play inside that collection", () => {
            Assert(SpotifyService.PlayInCollectionBody("spotify:playlist:1234567890123456789012", 11) == "{\"context_uri\":\"spotify:playlist:1234567890123456789012\",\"offset\":{\"position\":11}}", "Play request does not start the playlist at the song.");
            foreach (string invalid in new[] { Track, "spotify:playlist:bad", null }) { bool rejected = false; try { SpotifyService.PlayInCollectionBody(invalid, 0); } catch (ArgumentException) { rejected = true; } Assert(rejected, "Invalid collection accepted."); }
            using (var service = new SpotifyService(Session(directory, "no-device-collection", false), new Handler { Send = request => Reply(HttpStatusCode.NotFound, "") }))
            {
                bool typed = false; try { service.PlayInCollectionAsync("spotify:album:1234567890123456789012", 0).GetAwaiter().GetResult(); } catch (SpotifyNoDeviceException) { typed = true; }
                Assert(typed, "Missing device is not distinguishable.");
            }
            var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            try
            {
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var owner = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    string playBody = null, playMethod = null;
                    var handler = new Handler { Send = request => {
                        string path = request.RequestUri.AbsolutePath;
                        if (path.EndsWith("/me/player/play")) { playMethod = request.Method.Method; playBody = request.Content.ReadAsStringAsync().Result; return Reply(HttpStatusCode.NoContent, ""); }
                        if (path.EndsWith("/me/playlists")) return Reply(HttpStatusCode.OK, "{\"items\":[{\"name\":\"Road trip - TEST FIXTURE\",\"uri\":\"spotify:playlist:1234567890123456789012\"}]}");
                        if (path.EndsWith("/items")) return Reply(HttpStatusCode.OK, "{\"items\":[{\"item\":{\"name\":\"First - TEST FIXTURE\",\"uri\":\"spotify:track:2234567890123456789012\"}},{\"item\":{\"name\":\"Second - TEST FIXTURE\",\"uri\":\"" + Track + "\"}}]}");
                        return Reply(HttpStatusCode.OK, "{}");
                    } };
                    using (var widget = new SpotifyWidget(owner, new SpotifyService(Session(directory, "play-in-playlist", false), handler)))
                    {
                        ((Button)owner.FindName("SpotifyPlaylists")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                        var results = (StackPanel)owner.FindName("SpotifyResults");
                        Assert(results.Children.Count == 1, "Playlist missing."); ((Button)results.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                        Assert(results.Children.Count == 2, "Playlist songs missing.");
                        var second = (Button)results.Children[1];
                        // Never click a row that would open a link: that would launch the browser during tests.
                        Assert(System.Windows.Automation.AutomationProperties.GetName(second) == "Play Second - TEST FIXTURE in Road trip - TEST FIXTURE", "Song row would open a link instead of playing in the playlist.");
                        second.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                        Assert(playMethod == "PUT" && playBody == SpotifyService.PlayInCollectionBody("spotify:playlist:1234567890123456789012", 1), "Tapping a playlist song did not start the playlist at that song.");
                        Assert(((TextBlock)owner.FindName("SpotifyStatus")).Text.Contains("from Road trip"), "Playback start not confirmed.");
                    }
                    owner.Close();
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        });
        check("Spotify seek clamps song end, refuses changed/restricted items and requires scope", () => {
            Assert(SpotifyService.SeekPosition(300000, 200000) == 199999 && SpotifyService.SeekPosition(-10, 200000) == 0, "Seek boundary can skip to the next song.");
            int writes = 0; bool changed = false, restricted = false;
            var handler = new Handler { Send = request => {
                if (request.Method == HttpMethod.Put) { writes++; Assert(HttpUtility.ParseQueryString(request.RequestUri.Query)["position_ms"] == "199999", "Wrong seek position."); return Reply(HttpStatusCode.NoContent, ""); }
                return Reply(HttpStatusCode.OK, "{\"actions\":{\"disallows\":{\"seeking\":" + (restricted ? "true" : "false") + "}},\"item\":{\"uri\":\"" + (changed ? "spotify:track:2234567890123456789012" : Track) + "\",\"duration_ms\":200000}}");
            } };
            using (var service = new SpotifyService(Session(directory, "seek", false), handler))
            {
                service.SeekAsync(Track, 300000).Wait(); changed = true;
                Assert(Failure(() => service.SeekAsync(Track, 5000)).Contains("changed") && writes == 1, "Seek applied to a different song.");
                changed = false; restricted = true;
                Assert(Failure(() => service.SeekAsync(Track, 5000)).Contains("does not allow") && writes == 1, "Restricted item was seeked.");
            }
            string legacy = Session(directory, "old-scope", false); var session = SpotifyCredentials.Load(legacy); session.Scope = null; SpotifyCredentials.Save(legacy, session);
            var unused = new Handler { Send = request => { throw new Exception("Playback sent without permission."); } };
            using (var service = new SpotifyService(legacy, unused)) Assert(service.Connected && Failure(() => service.SeekAsync(Track, 1000)).Contains("Reconnect"), "Legacy login discarded or missing-scope guidance lost.");
            using (var service = new SpotifyService(Session(directory, "no-device", false), new Handler { Send = request => Reply(HttpStatusCode.NotFound, "") }))
                Assert(Failure(() => service.PlaybackAsync("play")).Contains("No active Spotify device"), "Inactive device failure unclear.");
        });
        check("Scrubber previews many movements but seeks once and cancels stale gestures", () => {
            var slider = new Slider(); int writes = 0; double committed = 0;
            var scrubber = new PlaybackScrubber(slider, (key, position) => { writes++; committed = position; }, value => { });
            scrubber.Update(Track, 1000, 200000, true); scrubber.Begin(); slider.Value = 10000; slider.Value = 25000;
            scrubber.Update(Track, 2000, 200000, true); Assert(slider.Value == 25000 && writes == 0, "Polling overwrote a drag or sent premature seeks.");
            scrubber.End(true); scrubber.End(true); Assert(writes == 1 && committed == 25000, "Gesture committed more than once.");
            scrubber.Begin(); slider.Value = 50000; scrubber.Update("new-song", 0, 100000, true); scrubber.End(true); Assert(writes == 1, "Changed song received old seek.");
            scrubber.Begin(); slider.Value = 20000; scrubber.End(false); Assert(writes == 1 && slider.Value == 0, "Cancelled drag changed playback.");
        });
        check("Spotify empty, podcast, local and missing playback parse safely", () => {
            var json = new JavaScriptSerializer();
            Assert(SpotifyService.ParsePlayback(null).Item == null, "204 response fabricated a track.");
            var track = SpotifyService.ParsePlayback(json.DeserializeObject("{\"is_playing\":true,\"progress_ms\":12345,\"item\":{\"uri\":\"" + Track + "\",\"name\":\"Test song\",\"duration_ms\":200000,\"artists\":[{\"name\":\"Test artist\"}],\"album\":{\"uri\":\"spotify:album:1234567890123456789012\"}}}"));
            Assert(track.Playing && track.Item.Name == "Test song" && track.Item.Subtitle == "Test artist" && track.Item.AlbumUrl != null && track.PositionMs == 12345, "Track metadata lost.");
            var episode = SpotifyService.ParseItem(json.DeserializeObject("{\"uri\":\"spotify:episode:1234567890123456789012\",\"name\":\"Episode\",\"show\":{\"name\":\"Podcast\"}}"));
            Assert(episode != null && !episode.IsTrack && episode.Subtitle == "Podcast", "Podcast incorrectly enabled likes.");
            Assert(SpotifyService.ParseItem(json.DeserializeObject("{\"uri\":\"spotify:local:a:b:c:1\"}")) == null, "Local track accepted for Web API likes.");
        });
        check("Spotify refresh rotates access token and retains omitted refresh token", () => {
            string path = Session(directory, "refresh", true);
            var handler = new Handler { Send = request => {
                if (request.RequestUri.Host == "accounts.spotify.com")
                {
                    string form = request.Content.ReadAsStringAsync().Result;
                    Assert(form.Contains("grant_type=refresh_token") && form.Contains("client_id=") && !form.Contains("client_secret"), "PKCE refresh request invalid.");
                    return Reply(HttpStatusCode.OK, "{\"access_token\":\"new-access\",\"expires_in\":3600}");
                }
                Assert(request.Headers.Authorization.Parameter == "new-access", "Expired access token reused."); return Reply(HttpStatusCode.NoContent, null);
            } };
            using (var service = new SpotifyService(path, handler)) Assert(service.CurrentAsync().Result.Item == null, "No active playback misreported.");
            Assert(handler.Calls == 2 && SpotifyCredentials.Load(path).RefreshToken == "test-refresh" && SpotifyCredentials.Load(path).Scope == SpotifyService.Scopes, "Refresh token or scope dropped, or extra requests made.");
        });
        check("Spotify saved state and like use URI library endpoints", () => {
            var requests = new List<string>();
            var handler = new Handler { Send = request => {
                requests.Add(request.Method + " " + HttpUtility.UrlDecode(request.RequestUri.PathAndQuery));
                return Reply(HttpStatusCode.OK, request.Method == HttpMethod.Get ? "[true]" : "");
            } };
            using (var service = new SpotifyService(Session(directory, "likes", false), handler))
            {
                Assert(service.IsLikedAsync(Track).Result, "Saved state missing."); service.SetLikedAsync(Track, true).Wait(); service.SetLikedAsync(Track, false).Wait();
                Assert(Failure(() => service.SetLikedAsync("spotify:episode:1234567890123456789012", true)).Contains("Only Spotify songs"), "Episode liked as track.");
            }
            Assert(requests.Count == 3 && requests[0] == "GET /v1/me/library/contains?uris=" + Track && requests[1] == "PUT /v1/me/library?uris=" + Track && requests[2] == "DELETE /v1/me/library?uris=" + Track, "Legacy or incorrect save endpoint used.");
        });
        check("Spotify albums, playlists and song search preserve paging and escape query", () => {
            var handler = new Handler { Send = request => {
                string path = request.RequestUri.AbsolutePath;
                if (path.EndsWith("/search"))
                {
                    var query = HttpUtility.ParseQueryString(request.RequestUri.Query); Assert(query["q"] == "AC/DC & friends" && query["limit"] == "10" && query["offset"] == "10", "Search query or paging malformed.");
                    return Reply(HttpStatusCode.OK, "{\"tracks\":{\"items\":[{\"name\":\"Song\",\"uri\":\"" + Track + "\"}],\"next\":null}}");
                }
                if (path.EndsWith("/albums")) return Reply(HttpStatusCode.OK, "{\"items\":[{\"album\":{\"uri\":\"spotify:album:1234567890123456789012\",\"name\":\"Album\"}}],\"next\":\"next-page\"}");
                Assert(path.EndsWith("/me/playlists"), "Wrong playlist endpoint."); return Reply(HttpStatusCode.OK, "{\"items\":[null,{\"uri\":\"spotify:playlist:1234567890123456789012\",\"name\":\"Playlist\"}],\"next\":null}");
            } };
            // Saved albums are the fallback for connections made before the recently-played permission.
            string browse = Session(directory, "browse", false); var saved = SpotifyCredentials.Load(browse); saved.Scope = saved.Scope.Replace("user-read-recently-played ", ""); SpotifyCredentials.Save(browse, saved);
            using (var service = new SpotifyService(browse, handler))
            {
                var album = service.BrowseAsync("Albums", "", 0).Result; Assert(album.Items.Length == 1 && album.HasMore && album.Items[0].Name == "Album", "Album wrapper lost.");
                Assert(service.BrowseAsync("Playlists", "", 0).Result.Items.Length == 1, "Nullable playlist broke browsing.");
                var results = service.BrowseAsync("Search", "AC/DC & friends", 10).Result; Assert(results.Items.Length == 1 && !results.HasMore, "Search results lost.");
            }
        });
        check("Spotify 403 and rate/quota limits are actionable and stop extra requests", () => {
            foreach (bool quota in new[] { false, true })
            {
                var handler = new Handler { Send = request => { var response = new HttpResponseMessage((HttpStatusCode)429) { Content = new StringContent(quota ? "{\"error\":{\"reason\":\"QUOTA_EXCEEDED\"}}" : "{}") }; response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120)); return Task.FromResult(response); } };
                using (var service = new SpotifyService(Session(directory, quota ? "quota" : "rate", false), handler))
                {
                    string message = Failure(async () => { await service.CurrentAsync(); }); Assert(message.Contains(quota ? "quota" : "limiting"), "Limit error unclear.");
                    Failure(async () => { await service.BrowseAsync("Albums", "", 0); }); Assert(handler.Calls == 1, "Retry-After ignored.");
                }
            }
            var denied = new Handler { Send = request => Reply(HttpStatusCode.Forbidden, "") };
            using (var service = new SpotifyService(Session(directory, "denied", false), denied)) Assert(Failure(async () => { await service.CurrentAsync(); }).Contains("allowlist"), "403 lacked setup guidance.");
        });
        check("Spotify 401 retries once; invalid refresh requires reconnection", () => {
            var handler = new Handler { Send = request => request.RequestUri.Host == "accounts.spotify.com" ? Reply(HttpStatusCode.OK, "{\"access_token\":\"rotated\",\"refresh_token\":\"rotated-refresh\",\"expires_in\":3600}") : Reply(HttpStatusCode.Unauthorized, "") };
            string path = Session(directory, "unauthorized", false);
            using (var service = new SpotifyService(path, handler)) { Assert(Failure(async () => { await service.CurrentAsync(); }).Contains("Connect again"), "401 guidance missing."); Assert(!service.Connected && handler.Calls == 3, "401 retry loop or stale tokens."); }
            var expired = new Handler { Send = request => Reply(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}") };
            using (var service = new SpotifyService(Session(directory, "expired", true), expired)) { Failure(async () => { await service.CurrentAsync(); }); Assert(!service.Connected, "Expired refresh token retained."); }
        });
        check("Spotify disconnect cannot be undone by in-flight token refresh", () => {
            var pending = new TaskCompletionSource<HttpResponseMessage>();
            var handler = new Handler { Send = request => pending.Task };
            string path = Session(directory, "disconnect-race", true);
            using (var service = new SpotifyService(path, handler))
            {
                var current = service.CurrentAsync(); Assert(handler.Calls == 1, "Refresh did not start."); service.Disconnect();
                pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"late-token\",\"refresh_token\":\"late-refresh\",\"expires_in\":3600}") });
                Failure(async () => { await current; }); Assert(!service.Connected && String.IsNullOrEmpty(SpotifyCredentials.Load(path).RefreshToken), "Late refresh resurrected disconnected account.");
            }
        });
        check("Spotify library and search stay inline and disconnected tabs do not open setup", () => {
            var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            try
            {
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var owner = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    var settings = new AppSettings { TextScale = 1.15 }; DashboardAppearance.Apply(owner, settings);
                    owner.Resources["SmallSize"] = 16.1; owner.Resources["BodySize"] = 20.7; owner.Resources["TitleSize"] = 27.6;
                    settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings); var editor = new LayoutEditor(owner); editor.Apply(settings);
                    var handler = new Handler { Send = request => Reply(HttpStatusCode.OK,
                        request.RequestUri.AbsolutePath.EndsWith("/recently-played") ? "{\"items\":[{\"track\":{\"album\":{\"name\":\"Sample album - TEST FIXTURE\",\"uri\":\"spotify:album:1234567890123456789012\"}}}]}"
                        : request.RequestUri.AbsolutePath.EndsWith("/search") ? "{\"tracks\":{\"items\":[{\"name\":\"Sample song - TEST FIXTURE\",\"uri\":\"" + Track + "\"}]}}"
                        : "{\"items\":[{\"name\":\"Sample playlist - TEST FIXTURE\",\"uri\":\"spotify:playlist:1234567890123456789012\"}]}") };
                    using (var service = new SpotifyService(Session(directory, "inline", false), handler))
                    using (var widget = new SpotifyWidget(owner, service))
                    {
                        foreach (string kind in new[] { "Albums", "Playlists", "Search" })
                        {
                            ((Button)owner.FindName("Spotify" + kind)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (kind == "Search") { ((TextBox)owner.FindName("SpotifyQuery")).Text = "Sample"; ((Button)owner.FindName("SpotifyFind")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
                            Pump();
                            Assert(((StackPanel)owner.FindName("SpotifyResults")).Children.Count == 1, "Inline results missing.");
                            Assert(((Grid)owner.FindName("SpotifyLibraryView")).Visibility == Visibility.Visible && ((Grid)owner.FindName("SettingsOverlay")).Visibility != Visibility.Visible, "Browsing opened setup.");
                            Render(owner, directory, "spotify-" + kind.ToLowerInvariant() + "-fixture.png");
                        }
                        service.Disconnect(); widget.AccountChanged();
                        ((Button)owner.FindName("SpotifyAlbums")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
                        Assert(((Grid)owner.FindName("SettingsOverlay")).Visibility != Visibility.Visible && ((TextBlock)owner.FindName("SpotifyStatus")).Text.Contains("Settings"), "Disconnected tab forced setup or lost guidance.");
                    }
                    owner.Close();
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        });
        check("Spotify compact widget shows confirmed likes and keeps actions inside bounds", () => {
            var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            try
            {
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    window.Resources["SmallSize"] = 16.1; window.Resources["BodySize"] = 20.7; window.Resources["TitleSize"] = 27.6;
                    var settings = new AppSettings { TextScale = 1.15 }; var editor = new LayoutEditor(window); editor.Apply(settings);
                    editor.Begin(); editor.Select("Spotify"); editor.SetVisible("Spotify", true); editor.TransformSelected("SE", -1000, -1000, false); editor.Accept();
                    bool deny = true;
                    var handler = new Handler { Send = request => request.RequestUri.AbsolutePath.EndsWith("currently-playing")
                        ? Reply(HttpStatusCode.OK, "{\"is_playing\":true,\"progress_ms\":60000,\"item\":{\"name\":\"Song · TEST FIXTURE\",\"uri\":\"" + Track + "\",\"duration_ms\":200000,\"artists\":[{\"name\":\"Sample artist\"}]}}")
                        : request.Method == HttpMethod.Get ? Reply(HttpStatusCode.OK, "[false]") : Reply(deny ? HttpStatusCode.Forbidden : HttpStatusCode.OK, "") };
                    using (var widget = new SpotifyWidget(window, new SpotifyService(Session(directory, "widget", false), handler)))
                    {
                        widget.Refresh(false); Pump();
                        var like = (Button)window.FindName("SpotifyLike"); Assert(like.IsEnabled && (string)like.Content == "♡ Like", "Like state unavailable after playback loaded.");
                        like.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Assert((string)like.Content == "♡ Like", "Denied write optimistically showed Liked.");
                        deny = false; like.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Assert((string)like.Content == "♥ Liked", "Confirmed save not shown.");
                        var dashboard = (FrameworkElement)window.FindName("Dashboard"); dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                        var card = (FrameworkElement)window.FindName("SpotifyCard");
                        var hint = (FrameworkElement)window.FindName("SpotifyStatus"); Assert(hint.ActualHeight + hint.Margin.Top + hint.Margin.Bottom + .1 >= hint.DesiredSize.Height && hint.ActualHeight >= 20, "Spotify status line is clipped.");
                        Assert(window.FindName("SpotifyNow") == null && window.FindName("SpotifySeek") == null && window.FindName("SpotifyPlay") == null, "Playing tab or playback controls still present.");
                        ((DockPanel)window.FindName("SpotifyPaging")).Visibility = Visibility.Visible; dashboard.UpdateLayout();
                        foreach (string name in new[] { "SpotifyLike", "SpotifyAlbums", "SpotifyPlaylists", "SpotifySearch", "SpotifyAccount", "SpotifyPagePrevious", "SpotifyPageNext" })
                        {
                            var control = (FrameworkElement)window.FindName(name); var corner = control.TransformToAncestor(card).Transform(new Point(control.ActualWidth, control.ActualHeight));
                            Assert(control.ActualHeight >= 48 && corner.X <= card.ActualWidth - 20 && corner.Y <= card.ActualHeight - 20, name + " escapes minimum-size card.");
                        }
                        var bitmap = new RenderTargetBitmap(560, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(card); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using (var file = File.Create(Path.Combine(directory, "spotify-widget-fixture.png"))) encoder.Save(file);
                        like.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump(); Assert((string)like.Content == "♡ Like", "Confirmed unsave not shown.");
                        int calls = handler.Calls; widget.Refresh(false); Pump(); Assert(handler.Calls == calls, "Widget polling ignored minimum interval.");
                    }
                    window.Close();
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        });
    }
    private static void Render(Window window, string directory, string name)
    {
        var dashboard = (FrameworkElement)window.FindName("Dashboard"); dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1920, 720, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dashboard);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(directory, name))) encoder.Save(file);
    }
    private static List<Button> Buttons(DependencyObject parent)
    {
        var result = new List<Button>(); if (parent is Button) result.Add((Button)parent);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++) result.AddRange(Buttons(VisualTreeHelper.GetChild(parent, i))); return result;
    }
    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (sender, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private static void RunCallback(string directory, bool succeed)
    {
        var opened = new TaskCompletionSource<string>(); var tokenRequested = new TaskCompletionSource<bool>(); var tokenResponse = new TaskCompletionSource<HttpResponseMessage>();
        var handler = new Handler { Send = request => {
            Assert(request.RequestUri.AbsoluteUri == "https://accounts.spotify.com/api/token", "Incorrect token endpoint.");
            var form = HttpUtility.ParseQueryString(request.Content.ReadAsStringAsync().Result);
            var authorize = HttpUtility.ParseQueryString(new Uri(opened.Task.Result).Query);
            Assert(form["grant_type"] == "authorization_code" && form["code"] == "test-code" && form["redirect_uri"] == SpotifyService.RedirectUri && SpotifyService.Challenge(form["code_verifier"]) == authorize["code_challenge"], "Approval was not exchanged with its PKCE verifier.");
            tokenRequested.SetResult(true); return tokenResponse.Task;
        } };
        string path = Session(directory, succeed ? "callback-success" : "callback-failure", false);
        using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        using (var service = new SpotifyService(path, handler, url => opened.SetResult(url)))
        {
            service.Disconnect(); string progress = null;
            var connection = service.ConnectAsync(new string('a', 32), cancellation.Token, message => progress = message);
            try
            {
                Assert(opened.Task.Wait(2000), "Authorization browser was never opened.");
                string state = HttpUtility.ParseQueryString(new Uri(opened.Task.Result).Query)["state"];
                var callback = SendCallbackAsync("/callback?state=" + Uri.EscapeDataString(state) + "&code=test-code");
                Assert(tokenRequested.Task.Wait(2000), "Callback never reached token exchange.");
                Assert(!callback.IsCompleted && !service.Connected && progress.Contains("Finishing"), "Browser reported success before token exchange completed.");
                tokenResponse.SetResult(new HttpResponseMessage(succeed ? HttpStatusCode.OK : HttpStatusCode.BadRequest) { Content = new StringContent(succeed ? "{\"access_token\":\"callback-access\",\"refresh_token\":\"callback-refresh\",\"expires_in\":3600}" : "{\"error\":\"invalid_grant\"}") });
                if (succeed)
                {
                    connection.GetAwaiter().GetResult(); Assert(service.Connected && SpotifyCredentials.Load(path).RefreshToken == "callback-refresh", "Approval did not become a persistent connected account.");
                }
                else { Assert(Failure(async () => { await connection; }).Contains("Connect again") && !service.Connected, "Failed exchange silently connected an account."); }
                string response = callback.GetAwaiter().GetResult(); Assert(response.Contains(succeed ? "200 OK" : "400 Bad Request") && response.Contains(succeed ? "Spotify is connected" : "could not be completed"), "Browser response disagrees with actual connection outcome.");
            }
            finally { cancellation.Cancel(); tokenResponse.TrySetCanceled(); }
        }
    }
    private static async Task<string> SendCallbackAsync(string target)
    {
        using (var client = new System.Net.Sockets.TcpClient())
        {
            await client.ConnectAsync(IPAddress.Loopback, 43827).ConfigureAwait(false);
            using (var stream = client.GetStream())
            {
                byte[] request = System.Text.Encoding.ASCII.GetBytes("GET " + target + " HTTP/1.1\r\nHost: 127.0.0.1:43827\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(request, 0, request.Length).ConfigureAwait(false);
                var output = new System.Text.StringBuilder(); byte[] buffer = new byte[2048]; int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0) output.Append(System.Text.Encoding.UTF8.GetString(buffer, 0, count));
                return output.ToString();
            }
        }
    }
}
