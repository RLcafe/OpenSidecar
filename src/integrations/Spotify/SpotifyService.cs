using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    public sealed class SpotifySession
    {
        public string ClientId { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string Scope { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }

    public static class SpotifyCredentials
    {
        public static SpotifySession Load(string path)
        {
            if (!File.Exists(path)) return new SpotifySession();
            return new JavaScriptSerializer().Deserialize<SpotifySession>(Encoding.UTF8.GetString(
                ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)));
        }
        public static void Save(string path, SpotifySession session)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(session)), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }

    public sealed class SpotifyItem
    {
        public string Id, Uri, Name, Subtitle, ImageUrl, AlbumUrl;
        // Zero-based place in its album or playlist, counting items this app cannot show; -1 outside a collection.
        public int Position = -1;
        public string WebUrl { get { return SpotifyService.WebUrl(Uri); } }
        public bool IsTrack { get { return Uri != null && Uri.StartsWith("spotify:track:", StringComparison.Ordinal); } }
    }
    public sealed class SpotifyNoDeviceException : InvalidOperationException
    {
        public SpotifyNoDeviceException() : base("No active Spotify device. Start a song in Spotify, then try again.") { }
    }
    public sealed class SpotifyPlayback
    {
        public SpotifyItem Item;
        public bool Playing, CanSeek;
        public double PositionMs, DurationMs;
    }
    public sealed class SpotifyPage
    {
        public SpotifyItem[] Items;
        public bool HasMore;
    }

    // All Web API traffic is serialized: refresh once, honor Retry-After across all views,
    // and invalidate in-flight results when the account is disconnected.
    public sealed class SpotifyService : IDisposable
    {
        public const string RedirectUri = "http://127.0.0.1:43827/callback";
        public const string Scopes = "user-read-currently-playing user-read-recently-played user-library-read user-library-modify playlist-read-private playlist-read-collaborative user-modify-playback-state";
        private readonly string credentialPath;
        private readonly HttpClient http;
        private readonly Action<string> openAuthorization;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private SpotifySession session;
        private readonly object stateLock = new object();
        private int generation;
        private DateTime retryAt;
        public string StartupWarning { get; private set; }
        public string ClientId { get { return session.ClientId ?? ""; } }
        public bool Connected { get { return !String.IsNullOrEmpty(session.RefreshToken); } }
        public bool CanControl { get { return Connected && (session.Scope ?? "").Split(' ').Contains("user-modify-playback-state"); } }
        public bool CanReadRecent { get { return Connected && (session.Scope ?? "").Split(' ').Contains("user-read-recently-played"); } }

        public SpotifyService(string path) : this(path, new PreferIPv4Handler()) { }
        public SpotifyService(string path, HttpMessageHandler handler) : this(path, handler, Launcher.Open) { }
        public SpotifyService(string path, HttpMessageHandler handler, Action<string> openAuthorization)
        {
            credentialPath = path; http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) }; this.openAuthorization = openAuthorization;
            try { session = SpotifyCredentials.Load(path) ?? new SpotifySession(); }
            catch (Exception) { session = new SpotifySession(); StartupWarning = "Spotify connection could not be restored. Connect again."; }
        }
        public static string Challenge(string verifier)
        {
            using (var sha = SHA256.Create()) return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }
        private static string Base64Url(byte[] value) { return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        private static string RandomValue()
        {
            byte[] bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Base64Url(bytes);
        }
        public static bool ValidClientId(string value) { return value != null && value.Length == 32 && value.All(c => Uri.IsHexDigit(c)); }
        public static bool ReadCallback(string requestLine, string expectedState, out string code, out string error)
        {
            code = error = null;
            var parts = (requestLine ?? "").Trim().Split(' '); Uri callback;
            if (String.IsNullOrEmpty(expectedState) || parts.Length != 3 || parts[0] != "GET" || !parts[1].StartsWith("/callback?", StringComparison.Ordinal) || !Uri.TryCreate("http://127.0.0.1" + parts[1], UriKind.Absolute, out callback)) return false;
            var query = HttpUtility.ParseQueryString(callback.Query);
            if (query["state"] != expectedState) return false;
            code = query["code"]; error = query["error"];
            return !String.IsNullOrWhiteSpace(code) || !String.IsNullOrWhiteSpace(error);
        }
        public static string WebUrl(string value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            var parts = value.Split(':');
            if (parts.Length != 3 || parts[0] != "spotify" || !new[] { "track", "album", "playlist", "episode" }.Contains(parts[1]) || parts[2].Length != 22 || !parts[2].All(Char.IsLetterOrDigit)) return null;
            return "https://open.spotify.com/" + parts[1] + "/" + parts[2];
        }
        public Task ConnectAsync(string clientId, CancellationToken cancellation) { return ConnectAsync(clientId, cancellation, null); }
        public async Task ConnectAsync(string clientId, CancellationToken cancellation, Action<string> progress)
        {
            if (!ValidClientId(clientId)) throw new InvalidOperationException("Paste the 32-character Client ID from your Spotify Developer app.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(3));
                await gate.WaitAsync(timeout.Token).ConfigureAwait(false);
                var listener = new TcpListener(IPAddress.Loopback, 43827);
                int version = generation;
                try
                {
                    listener.Start();
                    using (timeout.Token.Register(() => listener.Stop()))
                    {
                        string verifier = RandomValue(), state = RandomValue();
                        string url = "https://accounts.spotify.com/authorize?response_type=code&show_dialog=true&client_id=" + clientId + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri) + "&scope=" + Uri.EscapeDataString(Scopes) + "&state=" + state + "&code_challenge_method=S256&code_challenge=" + Challenge(verifier);
                        openAuthorization(url);
                        string code = null;
                        while (code == null)
                        {
                            using (var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false))
                            using (var stream = client.GetStream())
                            using (timeout.Token.Register(() => client.Close()))
                            {
                                var headers = new StringBuilder(); byte[] one = new byte[1];
                                while (headers.Length < 16384)
                                {
                                    int count = await stream.ReadAsync(one, 0, 1, timeout.Token).ConfigureAwait(false);
                                    if (count == 0) break; headers.Append((char)one[0]);
                                    int length = headers.Length;
                                    if (length >= 4 && headers[length - 4] == '\r' && headers[length - 3] == '\n' && headers[length - 2] == '\r' && headers[length - 1] == '\n') break;
                                }
                                string receivedCode, error;
                                bool valid = ReadCallback(headers.ToString().Split('\n')[0], state, out receivedCode, out error);
                                if (!valid) { await WriteCallbackAsync(stream, "Unrecognized callback. Return to Widget Screen and try connecting again.", false, timeout.Token).ConfigureAwait(false); continue; }
                                if (!String.IsNullOrEmpty(error))
                                {
                                    await WriteCallbackAsync(stream, "Spotify sign-in was declined. Return to Widget Screen to try again.", false, timeout.Token).ConfigureAwait(false);
                                    throw new InvalidOperationException("Spotify sign-in was declined. You can connect again when ready.");
                                }
                                code = receivedCode;
                                if (progress != null) progress("Spotify approved. Finishing the connection…");
                                Exception exchangeFailure = null;
                                try
                                {
                                    var result = await TokenAsync(new Dictionary<string, string> { { "grant_type", "authorization_code" }, { "code", code }, { "redirect_uri", RedirectUri }, { "client_id", clientId }, { "code_verifier", verifier } }, timeout.Token).ConfigureAwait(false);
                                    var connected = SessionFrom(result, clientId, null);
                                    connected.Scope = Field(result, "scope") ?? Scopes;
                                    timeout.Token.ThrowIfCancellationRequested();
                                    lock (stateLock)
                                    {
                                        if (version != generation) throw new OperationCanceledException();
                                        try { SpotifyCredentials.Save(credentialPath, connected); }
                                        catch (Exception storageError) { throw new InvalidOperationException("Spotify approved, but the login could not be saved on this PC. Check access to your settings folder, then reconnect.", storageError); }
                                        session = connected; StartupWarning = null;
                                    }
                                }
                                catch (Exception e) { exchangeFailure = e; }
                                if (exchangeFailure != null)
                                {
                                    await TryWriteCallbackAsync(stream, "Spotify connection could not be completed. Return to Widget Screen for the error and try again.", false, timeout.Token).ConfigureAwait(false);
                                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exchangeFailure).Throw();
                                }
                                // Report success only after usable tokens are securely saved. A closed
                                // browser tab must not turn a completed connection into a failed login.
                                await TryWriteCallbackAsync(stream, "Spotify is connected and saved. Return to Widget Screen. You can close this browser tab.", true, timeout.Token).ConfigureAwait(false);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    if (timeout.IsCancellationRequested) throw new OperationCanceledException("Spotify connection cancelled or timed out.");
                    throw;
                }
                finally { listener.Stop(); gate.Release(); }
            }
        }
        private static async Task WriteCallbackAsync(NetworkStream stream, string message, bool success, CancellationToken cancellation)
        {
            byte[] body = Encoding.UTF8.GetBytes(message);
            byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + (success ? "200 OK" : "400 Bad Request") + "\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, 0, header.Length, cancellation).ConfigureAwait(false);
            await stream.WriteAsync(body, 0, body.Length, cancellation).ConfigureAwait(false);
        }
        private static async Task TryWriteCallbackAsync(NetworkStream stream, string message, bool success, CancellationToken cancellation)
        {
            try { await WriteCallbackAsync(stream, message, success, cancellation).ConfigureAwait(false); }
            catch (IOException) { } catch (ObjectDisposedException) { } catch (OperationCanceledException) { }
        }
        public void Disconnect()
        {
            lock (stateLock)
            {
                var cleared = new SpotifySession { ClientId = ClientId };
                SpotifyCredentials.Save(credentialPath, cleared); generation++; session = cleared; retryAt = DateTime.MinValue;
            }
        }
        private async Task<Dictionary<string, object>> TokenAsync(Dictionary<string, string> values, CancellationToken cancellation)
        {
            try
            {
            using (var content = new FormUrlEncodedContent(values))
            using (var response = await http.PostAsync("https://accounts.spotify.com/api/token", content, cancellation).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (error.Contains("invalid_grant")) throw new InvalidOperationException("Spotify authorization expired. Connect again.");
                    if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Unauthorized)
                        throw new InvalidOperationException("Spotify rejected the app details. Check the Client ID and redirect address in Settings. Connect again.");
                    throw new InvalidOperationException("Spotify sign-in is temporarily unavailable. Try again later.");
                }
                return Object(json.DeserializeObject(await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
            }
            }
            catch (HttpRequestException) { throw new InvalidOperationException("Could not contact Spotify to finish sign-in. Check your internet connection and try again."); }
            catch (TaskCanceledException)
            {
                cancellation.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Spotify's sign-in server did not respond in time. Check your connection and try again.");
            }
        }
        private static SpotifySession SessionFrom(Dictionary<string, object> result, string clientId, string refresh)
        {
            var value = new SpotifySession { ClientId = clientId, AccessToken = Field(result, "access_token"), RefreshToken = Field(result, "refresh_token") ?? refresh, Scope = Field(result, "scope"), ExpiresUtc = DateTime.UtcNow.AddSeconds(Number(result, "expires_in")) };
            if (String.IsNullOrEmpty(value.AccessToken) || String.IsNullOrEmpty(value.RefreshToken)) throw new InvalidOperationException("Spotify did not return a usable connection. Connect again.");
            return value;
        }
        private async Task<object> RequestAsync(HttpMethod method, string path, string body = null)
        {
            await gate.WaitAsync(lifetime.Token).ConfigureAwait(false);
            int version = generation;
            try
            {
                if (!Connected) throw new InvalidOperationException("Connect Spotify to use your library.");
                if (DateTime.UtcNow < retryAt) throw new InvalidOperationException("Spotify is limiting requests. Try again after " + retryAt.ToLocalTime().ToString("HH:mm") + ".");
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (session.ExpiresUtc <= DateTime.UtcNow.AddSeconds(60))
                    {
                        Dictionary<string, object> result;
                        try { result = await TokenAsync(new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", session.RefreshToken }, { "client_id", ClientId } }, lifetime.Token).ConfigureAwait(false); }
                        catch (InvalidOperationException e)
                        {
                            lock (stateLock)
                            {
                                if (version == generation && e.Message.StartsWith("Spotify authorization expired", StringComparison.Ordinal)) { session = new SpotifySession { ClientId = ClientId }; SpotifyCredentials.Save(credentialPath, session); }
                            }
                            throw;
                        }
                        lock (stateLock)
                        {
                            if (version != generation) throw new OperationCanceledException();
                            var refreshed = SessionFrom(result, ClientId, session.RefreshToken);
                            refreshed.Scope = refreshed.Scope ?? session.Scope;
                            SpotifyCredentials.Save(credentialPath, refreshed); session = refreshed;
                        }
                    }
                    using (var request = new HttpRequestMessage(method, "https://api.spotify.com/v1/" + path))
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
                        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        using (var response = await http.SendAsync(request, lifetime.Token).ConfigureAwait(false))
                        {
                            if (version != generation) throw new OperationCanceledException();
                            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) { session.ExpiresUtc = DateTime.MinValue; continue; }
                            if ((int)response.StatusCode == 429)
                            {
                                var retry = response.Headers.RetryAfter;
                                retryAt = retry != null && retry.Date.HasValue ? retry.Date.Value.UtcDateTime : DateTime.UtcNow.Add(retry != null && retry.Delta.HasValue ? retry.Delta.Value : TimeSpan.FromMinutes(1));
                                string errorBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                                if (errorBody.Contains("QUOTA_EXCEEDED")) { retryAt = DateTime.UtcNow.AddHours(1); throw new InvalidOperationException("Spotify's developer quota is exhausted. Requests paused for an hour; try later."); }
                                throw new InvalidOperationException("Spotify is limiting requests. Requests paused until " + retryAt.ToLocalTime().ToString("HH:mm") + ".");
                            }
                            if (response.StatusCode == HttpStatusCode.Forbidden) throw new InvalidOperationException("Spotify denied access. Check the app owner's Premium, user allowlist and permissions, then reconnect.");
                            if (response.StatusCode == HttpStatusCode.NotFound && path.StartsWith("me/player/")) throw new SpotifyNoDeviceException();
                            if (response.StatusCode == HttpStatusCode.Unauthorized)
                            {
                                lock (stateLock) { if (version != generation) throw new OperationCanceledException(); session = new SpotifySession { ClientId = ClientId }; SpotifyCredentials.Save(credentialPath, session); }
                                throw new InvalidOperationException("Spotify authorization expired. Connect again.");
                            }
                            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Spotify is unavailable (" + (int)response.StatusCode + "). Try again later.");
                            string data = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            return String.IsNullOrWhiteSpace(data) ? null : json.DeserializeObject(data);
                        }
                    }
                }
                throw new InvalidOperationException("Spotify authorization expired. Connect again.");
            }
            finally { gate.Release(); }
        }
        public async Task<SpotifyPlayback> CurrentAsync() { return ParsePlayback(await RequestAsync(HttpMethod.Get, "me/player/currently-playing?additional_types=episode").ConfigureAwait(false)); }
        public static int SeekPosition(double position, double duration)
        {
            if (Double.IsNaN(position) || Double.IsInfinity(position) || Double.IsNaN(duration) || Double.IsInfinity(duration) || duration <= 0) throw new InvalidOperationException("This song has no seekable timeline.");
            return (int)Math.Max(0, Math.Min(Math.Min(Int32.MaxValue, duration - 1), position));
        }
        public async Task SeekAsync(string expectedUri, double position)
        {
            RequirePlaybackPermission();
            var current = await CurrentAsync().ConfigureAwait(false);
            if (current.Item == null || current.Item.Uri != expectedUri) throw new InvalidOperationException("The song changed. Try seeking again.");
            if (!current.CanSeek) throw new InvalidOperationException("Spotify does not allow seeking in this item.");
            await RequestAsync(HttpMethod.Put, "me/player/seek?position_ms=" + SeekPosition(position, current.DurationMs)).ConfigureAwait(false);
        }
        public async Task PlaybackAsync(string command)
        {
            RequirePlaybackPermission();
            if (command != "previous" && command != "next" && command != "play" && command != "pause") throw new ArgumentException("Unknown playback command.");
            await RequestAsync(command == "previous" || command == "next" ? HttpMethod.Post : HttpMethod.Put, "me/player/" + command).ConfigureAwait(false);
        }
        // Starts the album or playlist at the chosen song, so playback continues through the collection.
        public async Task PlayInCollectionAsync(string collectionUri, int position)
        {
            RequirePlaybackPermission();
            await RequestAsync(HttpMethod.Put, "me/player/play", PlayInCollectionBody(collectionUri, position)).ConfigureAwait(false);
        }
        public static string PlayInCollectionBody(string collectionUri, int position)
        {
            if (WebUrl(collectionUri) == null || !(collectionUri.StartsWith("spotify:album:", StringComparison.Ordinal) || collectionUri.StartsWith("spotify:playlist:", StringComparison.Ordinal)) || position < 0)
                throw new ArgumentException("Choose a song from an album or playlist.");
            return new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "context_uri", collectionUri }, { "offset", new Dictionary<string, object> { { "position", position } } } });
        }
        private void RequirePlaybackPermission()
        {
            if (!CanControl) throw new InvalidOperationException("Reconnect in Settings → Spotify to enable playback and seeking. Spotify Premium is required.");
        }
        public async Task<SpotifyPage> TracksAsync(SpotifyItem collection, int offset)
        {
            if (collection == null || WebUrl(collection.Uri) == null) throw new ArgumentException("Invalid Spotify collection.");
            string[] parts = collection.Uri.Split(':'); bool album = parts[1] == "album";
            if (!album && parts[1] != "playlist") throw new ArgumentException("Choose an album or playlist.");
            var root = Object(await RequestAsync(HttpMethod.Get, (album ? "albums/" : "playlists/") + parts[2] + (album ? "/tracks" : "/items") + "?limit=10&offset=" + offset).ConfigureAwait(false));
            var items = Array(root, "items").Select((value, index) => {
                var item = ParseItem(album ? value : Value(Object(value), "item") ?? Value(Object(value), "track"));
                if (item != null) item.Position = offset + index;
                return item;
            });
            return new SpotifyPage { Items = items.Where(value => value != null).ToArray(), HasMore = !String.IsNullOrEmpty(Field(root, "next")) };
        }
        public async Task<bool> IsLikedAsync(string uri)
        {
            var values = await RequestAsync(HttpMethod.Get, "me/library/contains?uris=" + Uri.EscapeDataString(uri)).ConfigureAwait(false) as object[];
            if (values == null || values.Length != 1 || !(values[0] is bool)) throw new InvalidOperationException("Spotify did not return the track's saved state.");
            return (bool)values[0];
        }
        public async Task SetLikedAsync(string uri, bool liked)
        {
            if (WebUrl(uri) == null || !uri.StartsWith("spotify:track:", StringComparison.Ordinal)) throw new InvalidOperationException("Only Spotify songs can be liked here.");
            await RequestAsync(liked ? HttpMethod.Put : HttpMethod.Delete, "me/library?uris=" + Uri.EscapeDataString(uri)).ConfigureAwait(false);
        }
        public async Task<SpotifyPage> BrowseAsync(string kind, string query, int offset)
        {
            // Albums lists recently played albums; older connections without that permission fall back to saved albums.
            if (kind == "Albums" && CanReadRecent) return RecentAlbums(await RequestAsync(HttpMethod.Get, "me/player/recently-played?limit=50").ConfigureAwait(false), offset);
            string path = kind == "Albums" ? "me/albums?limit=10&offset=" + offset : kind == "Playlists" ? "me/playlists?limit=10&offset=" + offset : "search?type=track&limit=10&offset=" + offset + "&q=" + Uri.EscapeDataString(query ?? "");
            var root = Object(await RequestAsync(HttpMethod.Get, path).ConfigureAwait(false));
            if (kind == "Search") root = Object(Value(root, "tracks"));
            return new SpotifyPage { Items = Array(root, "items").Select(value => ParseItem(kind == "Albums" ? Value(Object(value), "album") : value)).Where(value => value != null).ToArray(), HasMore = !String.IsNullOrEmpty(Field(root, "next")) };
        }
        // Distinct albums from the last 50 played songs, most recent first, paged ten at a time.
        public static SpotifyPage RecentAlbums(object value, int offset)
        {
            var albums = Array(Object(value), "items").Select(item => ParseItem(Value(Object(Value(Object(item), "track")), "album")))
                .Where(album => album != null && album.Uri.StartsWith("spotify:album:", StringComparison.Ordinal)).GroupBy(album => album.Uri).Select(group => group.First()).ToArray();
            return new SpotifyPage { Items = albums.Skip(Math.Max(0, offset)).Take(10).ToArray(), HasMore = offset + 10 < albums.Length };
        }
        public static bool IsImageUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && (uri.Host.EndsWith(".scdn.co", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".spotifycdn.com", StringComparison.OrdinalIgnoreCase));
        }
        public static SpotifyPlayback ParsePlayback(object value)
        {
            var root = Object(value); var item = Object(Value(root, "item"));
            return new SpotifyPlayback { Item = ParseItem(item), Playing = Value(root, "is_playing") is bool && (bool)Value(root, "is_playing"), CanSeek = !Equals(Value(Object(Value(Object(Value(root, "actions")), "disallows")), "seeking"), true) && Number(item, "duration_ms") > 0, PositionMs = Number(root, "progress_ms"), DurationMs = Number(item, "duration_ms") };
        }
        public static SpotifyItem ParseItem(object value)
        {
            var root = Object(value); string uri = Field(root, "uri");
            if (WebUrl(uri) == null) return null;
            var album = Object(Value(root, "album")); var show = Object(Value(root, "show"));
            var images = Array(root, "images"); if (images.Length == 0) images = Array(album, "images");
            string subtitle = System.String.Join(", ", Array(root, "artists").Select(a => Field(Object(a), "name")).Where(a => !System.String.IsNullOrEmpty(a)));
            if (System.String.IsNullOrEmpty(subtitle)) subtitle = Field(show, "name") ?? Field(Object(Value(root, "owner")), "display_name") ?? Field(root, "type");
            return new SpotifyItem { Id = Field(root, "id"), Uri = uri, Name = Field(root, "name") ?? "Untitled", Subtitle = subtitle, ImageUrl = images.Length > 0 ? Field(Object(images[images.Length - 1]), "url") : null, AlbumUrl = WebUrl(Field(album, "uri")) };
        }
        private static Dictionary<string, object> Object(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        private static object Value(Dictionary<string, object> value, string key) { object result; return value.TryGetValue(key, out result) ? result : null; }
        private static string Field(Dictionary<string, object> value, string key) { return Value(value, key) as string; }
        private static double Number(Dictionary<string, object> value, string key) { object number = Value(value, key); return number == null ? 0 : Convert.ToDouble(number, System.Globalization.CultureInfo.InvariantCulture); }
        private static object[] Array(Dictionary<string, object> value, string key) { return Value(value, key) as object[] ?? new object[0]; }
        public void Dispose() { lock (stateLock) { generation++; } lifetime.Cancel(); http.Dispose(); }
    }
}
