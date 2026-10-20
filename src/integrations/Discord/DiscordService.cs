using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    // The user's own Discord application and its authorization. Saved only DPAPI-encrypted in discord-session.dat.
    public sealed class DiscordSession
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public string Scope { get; set; }
        public DateTime ExpiresUtc { get; set; }
    }

    public static class DiscordCredentials
    {
        public static DiscordSession Load(string path)
        {
            if (!File.Exists(path)) return new DiscordSession();
            return new JavaScriptSerializer().Deserialize<DiscordSession>(Encoding.UTF8.GetString(
                ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser)));
        }
        public static void Save(string path, DiscordSession session)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporary = path + ".tmp";
            File.WriteAllBytes(temporary, ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(session)), null, DataProtectionScope.CurrentUser));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
    }

    public sealed class DiscordSound
    {
        public string Id, GuildId, Name, EmojiName;
        public bool Available = true;
        // Discord's built-in sounds carry no server, or the placeholder server id "0" or "DEFAULT".
        public bool IsDefault { get { return String.IsNullOrEmpty(GuildId) || GuildId == "0" || String.Equals(GuildId, "DEFAULT", StringComparison.OrdinalIgnoreCase); } }
    }
    public sealed class DiscordVoiceChannel
    {
        public string Id, Name, GuildId;
    }

    // Plays soundboard sounds as the signed-in user through the Discord desktop app's local RPC pipe.
    // The user creates their own Discord application; Discord's own popup asks them to allow it once.
    // PLAY_SOUNDBOARD_SOUND and GET_SOUNDBOARD_SOUNDS are commands of the Discord app's RPC server
    // (they require the rpc and rpc.voice.write scopes) but aren't in Discord's public documentation.
    public sealed class DiscordService : IDisposable
    {
        public static readonly string[] Scopes = { "rpc", "rpc.voice.read", "rpc.voice.write" };
        public const string TokenUrl = "https://discord.com/api/oauth2/token";
        public const string RedirectUri = "http://127.0.0.1";
        private readonly string credentialPath;
        private readonly HttpClient http;
        private readonly Func<string, CancellationToken, Task<DiscordRpc>> openPipe;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly object stateLock = new object();
        private DiscordSession session;
        private DiscordRpc rpc;
        private int generation;
        public string StartupWarning { get; private set; }
        public string UserName { get; private set; }
        // Raised on a worker thread when the pipe closes or the user joins, leaves or switches voice channels.
        public event Action Changed;

        public string ClientId { get { return session.ClientId ?? ""; } }
        public bool HasSecret { get { return !String.IsNullOrEmpty(session.ClientSecret); } }
        public bool Connected { get { return !String.IsNullOrEmpty(session.RefreshToken) || !String.IsNullOrEmpty(session.AccessToken); } }
        public bool Linked { get { var current = rpc; return current != null && current.IsOpen; } }

        public DiscordService(string path) : this(path, new PreferIPv4Handler(), (id, token) => DiscordRpc.ConnectAsync(id, token)) { }
        public DiscordService(string path, HttpMessageHandler handler, Func<string, CancellationToken, Task<DiscordRpc>> openPipe)
        {
            credentialPath = path; http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) }; this.openPipe = openPipe;
            try { session = DiscordCredentials.Load(path) ?? new DiscordSession(); }
            catch (Exception) { session = new DiscordSession(); StartupWarning = "The Discord connection could not be restored. Connect again."; }
        }

        // Discord application ids are snowflakes: 17 to 20 digits.
        public static bool ValidClientId(string value) { return value != null && value.Length >= 17 && value.Length <= 20 && value.All(c => c >= '0' && c <= '9'); }
        public static bool ValidSecret(string value) { return String.IsNullOrEmpty(value) || (value.Length >= 16 && value.Length <= 128 && value.All(c => c > ' ' && c < 127)); }

        // Saves the app details. A different app or secret drops the old authorization.
        public void SaveApp(string clientId, string secret)
        {
            if (!ValidClientId(clientId)) throw new InvalidOperationException("Paste the Application ID from your Discord app. It's a long number.");
            if (!ValidSecret(secret)) throw new InvalidOperationException("That Client Secret doesn't look right. Copy it again from your Discord app's OAuth2 page.");
            lock (stateLock)
            {
                bool sameApp = clientId == session.ClientId && (String.IsNullOrEmpty(secret) || secret == session.ClientSecret);
                var next = sameApp ? Copy(session) : new DiscordSession { ClientId = clientId };
                if (!String.IsNullOrEmpty(secret)) next.ClientSecret = secret;
                DiscordCredentials.Save(credentialPath, next);
                if (!sameApp) { generation++; CloseLocked(); }
                session = next; StartupWarning = null;
            }
        }
        private static DiscordSession Copy(DiscordSession value)
        {
            return new DiscordSession { ClientId = value.ClientId, ClientSecret = value.ClientSecret, AccessToken = value.AccessToken, RefreshToken = value.RefreshToken, Scope = value.Scope, ExpiresUtc = value.ExpiresUtc };
        }

        // First-time sign-in: Discord shows its own Authorize popup, then the code is exchanged for tokens.
        public async Task ConnectAsync(CancellationToken cancellation, Action<string> progress)
        {
            if (!ValidClientId(ClientId)) throw new InvalidOperationException("Save your Discord Application ID first.");
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
            {
                await gate.WaitAsync(limit.Token).ConfigureAwait(false);
                int version;
                lock (stateLock) { generation++; version = generation; CloseLocked(); }
                try
                {
                    var pipe = await OpenAsync(limit.Token).ConfigureAwait(false);
                    var app = Copy(session);
                    string verifier = null;
                    var args = new Dictionary<string, object> { { "client_id", app.ClientId }, { "scopes", Scopes } };
                    // Without a Client Secret the app must be a Public Client, which exchanges the code with PKCE.
                    if (!HasSecret) { verifier = RandomValue(); args["code_challenge"] = SpotifyService.Challenge(verifier); args["code_challenge_method"] = "S256"; }
                    if (progress != null) progress("Approve Widget Screen in the Discord window that just opened.");
                    object reply;
                    try { reply = await pipe.CommandAsync("AUTHORIZE", args, null, TimeSpan.FromMinutes(3), limit.Token).ConfigureAwait(false); }
                    catch (DiscordRpcException e) { throw new InvalidOperationException(AuthorizeMessage(e)); }
                    catch (TimeoutException) { throw new InvalidOperationException("Discord's approval window timed out. Connect again when you're ready."); }
                    string code = DiscordRpc.Field(DiscordRpc.Object(reply), "code");
                    if (String.IsNullOrEmpty(code)) throw new InvalidOperationException("Discord didn't return an approval. Connect again.");
                    if (progress != null) progress("Discord approved. Finishing the connection…");
                    var values = new Dictionary<string, string> { { "grant_type", "authorization_code" }, { "code", code } };
                    if (verifier != null) values["code_verifier"] = verifier;
                    var connected = SessionFrom(await TokenAsync(app, values, limit.Token).ConfigureAwait(false), app, null);
                    lock (stateLock)
                    {
                        if (version != generation) throw new OperationCanceledException();
                        try { DiscordCredentials.Save(credentialPath, connected); }
                        catch (Exception error) { throw new InvalidOperationException("Discord approved, but the sign-in could not be saved on this PC. Check access to your settings folder, then connect again.", error); }
                        session = connected; StartupWarning = null;
                    }
                    await AuthenticateAsync(pipe, version, limit.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    lock (stateLock) { if (version == generation) CloseLocked(); }
                    throw;
                }
                finally { gate.Release(); }
            }
        }
        public static string AuthorizeMessage(DiscordRpcException error)
        {
            string text = (error.Message ?? "").ToLowerInvariant();
            if (error.Code == 5000 || text.Contains("denied") || text.Contains("cancel")) return "Discord sign-in was declined. Connect again when you're ready.";
            if (error.Code == 4007 || text.Contains("client id") || text.Contains("client_id")) return "Discord doesn't recognize that Application ID. Check it in Settings.";
            if (text.Contains("scope")) return "Discord didn't allow this app to control voice. Use an app you own, signed in to the same Discord account.";
            return "Discord couldn't authorize the app: " + error.Message;
        }

        // Opens the pipe and signs in with the saved token, refreshing it once if Discord rejects it.
        public async Task EnsureAsync(CancellationToken cancellation)
        {
            if (Linked) return;
            if (!Connected) throw new InvalidOperationException("Connect Discord in Settings to use the soundboard.");
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token))
            {
                await gate.WaitAsync(limit.Token).ConfigureAwait(false);
                try
                {
                    if (Linked) return;
                    int version; lock (stateLock) version = generation;
                    if (session.ExpiresUtc <= DateTime.UtcNow.AddMinutes(5)) await RefreshAsync(version, limit.Token).ConfigureAwait(false);
                    var pipe = await OpenAsync(limit.Token).ConfigureAwait(false);
                    bool rejected = false;
                    try { await AuthenticateAsync(pipe, version, limit.Token).ConfigureAwait(false); }
                    catch (DiscordRpcException) { rejected = true; }
                    if (rejected)
                    {
                        // Discord rejected the token: refresh it once on a fresh pipe, since a failed AUTHENTICATE may end the connection.
                        lock (stateLock) { if (version == generation) CloseLocked(); }
                        await RefreshAsync(version, limit.Token).ConfigureAwait(false);
                        pipe = await OpenAsync(limit.Token).ConfigureAwait(false);
                        try { await AuthenticateAsync(pipe, version, limit.Token).ConfigureAwait(false); }
                        catch (DiscordRpcException) { lock (stateLock) { if (version == generation) CloseLocked(); } Expire(version); throw new InvalidOperationException("Discord sign-in expired. Connect again in Settings."); }
                    }
                }
                finally { gate.Release(); }
            }
        }
        private async Task<DiscordRpc> OpenAsync(CancellationToken cancellation)
        {
            DiscordRpc pipe;
            try { pipe = await openPipe(ClientId, cancellation).ConfigureAwait(false); }
            catch (DiscordRpcException e)
            {
                if (e.Code == 4000 || (e.Message ?? "").IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("Discord doesn't recognize that Application ID. Check it in Settings.");
                throw new InvalidOperationException("Discord refused the connection: " + e.Message);
            }
            catch (IOException) { throw new DiscordNotRunningException(); }
            catch (TimeoutException) { throw new InvalidOperationException("Discord didn't answer. If it's still starting, wait a moment."); }
            lock (stateLock)
            {
                CloseLocked(); rpc = pipe;
                pipe.Closed += reason => { lock (stateLock) { if (rpc == pipe) rpc = null; } Raise(); };
                pipe.EventReceived += (evt, data) => { if (evt == "VOICE_CHANNEL_SELECT") Raise(); };
            }
            return pipe;
        }
        private async Task AuthenticateAsync(DiscordRpc pipe, int version, CancellationToken cancellation)
        {
            var result = DiscordRpc.Object(await pipe.CommandAsync("AUTHENTICATE", new Dictionary<string, object> { { "access_token", session.AccessToken } }, cancellation).ConfigureAwait(false));
            if (version != generation) throw new OperationCanceledException();
            var user = DiscordRpc.Object(DiscordRpc.Value(result, "user"));
            UserName = DiscordRpc.Field(user, "global_name") ?? DiscordRpc.Field(user, "username");
            // Voice channel changes arrive as events, so the widget never polls Discord.
            try { await pipe.CommandAsync("SUBSCRIBE", new Dictionary<string, object>(), "VOICE_CHANNEL_SELECT", TimeSpan.FromSeconds(10), cancellation).ConfigureAwait(false); }
            catch (DiscordRpcException) { }
        }
        private async Task RefreshAsync(int version, CancellationToken cancellation)
        {
            var app = Copy(session);
            if (String.IsNullOrEmpty(app.RefreshToken)) { Expire(version); throw new InvalidOperationException("Discord sign-in expired. Connect again in Settings."); }
            Dictionary<string, object> result;
            try { result = await TokenAsync(app, new Dictionary<string, string> { { "grant_type", "refresh_token" }, { "refresh_token", app.RefreshToken } }, cancellation).ConfigureAwait(false); }
            catch (InvalidOperationException e) { if (e.Message.StartsWith("Discord sign-in expired", StringComparison.Ordinal)) Expire(version); throw; }
            lock (stateLock)
            {
                if (version != generation) throw new OperationCanceledException();
                var refreshed = SessionFrom(result, app, app.RefreshToken);
                DiscordCredentials.Save(credentialPath, refreshed); session = refreshed;
            }
        }
        private void Expire(int version)
        {
            lock (stateLock)
            {
                if (version != generation) return;
                session = new DiscordSession { ClientId = session.ClientId, ClientSecret = session.ClientSecret };
                try { DiscordCredentials.Save(credentialPath, session); } catch (Exception) { }
            }
        }
        private async Task<Dictionary<string, object>> TokenAsync(DiscordSession app, Dictionary<string, string> values, CancellationToken cancellation)
        {
            values["client_id"] = app.ClientId;
            if (!String.IsNullOrEmpty(app.ClientSecret)) values["client_secret"] = app.ClientSecret;
            for (int attempt = 0; ; attempt++)
            {
                string body; HttpStatusCode status;
                try
                {
                    using (var content = new FormUrlEncodedContent(values))
                    using (var response = await http.PostAsync(TokenUrl, content, cancellation).ConfigureAwait(false))
                    {
                        body = await response.Content.ReadAsStringAsync().ConfigureAwait(false); status = response.StatusCode;
                        if (response.IsSuccessStatusCode) return DiscordRpc.Object(new JavaScriptSerializer().DeserializeObject(body));
                    }
                }
                catch (HttpRequestException) { throw new InvalidOperationException("Could not reach Discord to finish sign-in. Check your internet connection and try again."); }
                catch (TaskCanceledException) { cancellation.ThrowIfCancellationRequested(); throw new InvalidOperationException("Discord's sign-in server didn't respond in time. Try again."); }
                // Some apps only accept the code together with their registered redirect address.
                if (attempt == 0 && status == HttpStatusCode.BadRequest && body.Contains("redirect_uri") && !values.ContainsKey("redirect_uri")) { values["redirect_uri"] = RedirectUri; continue; }
                throw new InvalidOperationException(TokenMessage(status, body, !String.IsNullOrEmpty(app.ClientSecret)));
            }
        }
        public static string TokenMessage(HttpStatusCode status, string body, bool hasSecret)
        {
            body = body ?? "";
            if (body.Contains("invalid_grant")) return "Discord sign-in expired. Connect again in Settings.";
            if (body.Contains("invalid_client") || status == HttpStatusCode.Unauthorized)
                return hasSecret ? "Discord rejected the app details. Check the Application ID and Client Secret in Settings." : "Discord needs your app's Client Secret. Paste it in Settings, or turn on Public Client for the app.";
            if (body.Contains("redirect_uri")) return "Add http://127.0.0.1 as a redirect in your Discord app's OAuth2 page, then connect again.";
            if ((int)status == 429) return "Discord is limiting sign-in requests. Try again in a minute.";
            return "Discord sign-in is temporarily unavailable (" + (int)status + "). Try again later.";
        }
        private static DiscordSession SessionFrom(Dictionary<string, object> result, DiscordSession app, string refresh)
        {
            var value = new DiscordSession { ClientId = app.ClientId, ClientSecret = app.ClientSecret, AccessToken = DiscordRpc.Field(result, "access_token"), RefreshToken = DiscordRpc.Field(result, "refresh_token") ?? refresh, Scope = DiscordRpc.Field(result, "scope") ?? app.Scope };
            double seconds = DiscordRpc.Number(result, "expires_in");
            value.ExpiresUtc = DateTime.UtcNow.AddSeconds(seconds > 0 ? seconds : 3600);
            if (String.IsNullOrEmpty(value.AccessToken)) throw new InvalidOperationException("Discord didn't return a usable sign-in. Connect again.");
            return value;
        }

        public async Task<DiscordVoiceChannel> SelectedChannelAsync(CancellationToken cancellation)
        {
            await EnsureAsync(cancellation).ConfigureAwait(false);
            return ParseChannel(await Run(pipe => pipe.CommandAsync("GET_SELECTED_VOICE_CHANNEL", null, cancellation)).ConfigureAwait(false));
        }
        public async Task<List<DiscordSound>> SoundsAsync(CancellationToken cancellation)
        {
            await EnsureAsync(cancellation).ConfigureAwait(false);
            return ParseSounds(await Run(pipe => pipe.CommandAsync("GET_SOUNDBOARD_SOUNDS", null, cancellation)).ConfigureAwait(false));
        }
        public async Task PlayAsync(DiscordSound sound, CancellationToken cancellation)
        {
            if (sound == null || String.IsNullOrEmpty(sound.Id)) throw new ArgumentException("Choose a sound.");
            await EnsureAsync(cancellation).ConfigureAwait(false);
            await Run(pipe => pipe.CommandAsync("PLAY_SOUNDBOARD_SOUND", PlayArgs(sound), cancellation)).ConfigureAwait(false);
        }
        public static Dictionary<string, object> PlayArgs(DiscordSound sound)
        {
            var args = new Dictionary<string, object> { { "sound_id", sound.Id } };
            if (!String.IsNullOrEmpty(sound.GuildId)) args["guild_id"] = sound.GuildId;
            return args;
        }
        private async Task<object> Run(Func<DiscordRpc, Task<object>> command)
        {
            var pipe = rpc;
            if (pipe == null || !pipe.IsOpen) throw new DiscordNotRunningException();
            try { return await command(pipe).ConfigureAwait(false); }
            catch (DiscordRpcException e) { throw new InvalidOperationException(CommandMessage(e)); }
            catch (TimeoutException) { throw new InvalidOperationException("Discord didn't answer in time. Try again."); }
        }
        public static string CommandMessage(DiscordRpcException error)
        {
            string text = (error.Message ?? "").ToLowerInvariant();
            if (text.Contains("invalid channel")) return "Join a voice channel in Discord to play sounds.";
            if (text.Contains("invalid permission")) return "You can't use the soundboard in this voice channel. Check that you aren't muted or deafened.";
            if (text.Contains("invalid sound")) return "That sound can't be played here. Sounds from other servers need Nitro.";
            if (text.Contains("closed")) return "Discord closed the connection. Reconnecting…";
            if (error.Code == 4006 || text.Contains("not authenticated") || text.Contains("scope")) return "Discord needs you to connect again in Settings.";
            return "Discord: " + error.Message;
        }

        public static DiscordVoiceChannel ParseChannel(object value)
        {
            var root = value as Dictionary<string, object>;
            if (root == null) return null;
            string id = DiscordRpc.Field(root, "id");
            if (String.IsNullOrEmpty(id)) return null;
            return new DiscordVoiceChannel { Id = id, Name = DiscordRpc.Field(root, "name") ?? "Voice channel", GuildId = DiscordRpc.Field(root, "guild_id") };
        }
        public static List<DiscordSound> ParseSounds(object value)
        {
            var items = value as object[];
            if (items == null) items = DiscordRpc.Value(DiscordRpc.Object(value), "sounds") as object[] ?? new object[0];
            var sounds = new List<DiscordSound>();
            foreach (var item in items)
            {
                var root = DiscordRpc.Object(item);
                string id = DiscordRpc.Field(root, "sound_id") ?? DiscordRpc.Field(root, "id");
                if (String.IsNullOrEmpty(id)) continue;
                var available = DiscordRpc.Value(root, "available");
                sounds.Add(new DiscordSound { Id = id, GuildId = DiscordRpc.Field(root, "guild_id"), Name = String.IsNullOrWhiteSpace(DiscordRpc.Field(root, "name")) ? "Sound" : DiscordRpc.Field(root, "name").Trim(), EmojiName = DiscordRpc.Field(root, "emoji_name"), Available = !(available is bool) || (bool)available });
            }
            return sounds.GroupBy(s => (s.GuildId ?? "") + "/" + s.Id).Select(g => g.First()).ToList();
        }
        // The current server's sounds first, then Discord's built-in sounds, then other servers' sounds (which need Nitro).
        public static List<DiscordSound> Order(IEnumerable<DiscordSound> sounds, string guildId)
        {
            return sounds.Where(s => s.Available)
                .OrderBy(s => !String.IsNullOrEmpty(guildId) && s.GuildId == guildId ? 0 : s.IsDefault ? 1 : 2)
                .ThenBy(s => s.IsDefault ? "" : s.GuildId, StringComparer.Ordinal)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        public void Disconnect()
        {
            lock (stateLock)
            {
                var cleared = new DiscordSession { ClientId = session.ClientId, ClientSecret = session.ClientSecret };
                DiscordCredentials.Save(credentialPath, cleared); generation++; session = cleared; UserName = null; CloseLocked();
            }
            Raise();
        }
        private void CloseLocked() { var current = rpc; rpc = null; if (current != null) current.Dispose(); }
        private void Raise() { var handler = Changed; if (handler != null) try { handler(); } catch (Exception) { } }
        private static string RandomValue()
        {
            byte[] bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        public void Dispose() { lock (stateLock) { generation++; CloseLocked(); } lifetime.Cancel(); http.Dispose(); }
    }
}
