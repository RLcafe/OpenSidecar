using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace WidgetScreen
{
    // The widget's own Claude sign-in, kept DPAPI-encrypted in claude-session.dat next to settings.xml.
    public sealed class ClaudeSignIn
    {
        public string AccessToken = "", RefreshToken = "", Scope = "";
        public DateTime ExpiresUtc;
        public bool Expired;
        public bool SignedIn { get { return RefreshToken.Length > 0 || AccessToken.Length > 0; } }

        public static ClaudeSignIn Load(string path)
        {
            if (!File.Exists(path)) return new ClaudeSignIn();
            var o = ClaudeJson.Obj(ClaudeJson.Parse(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser))));
            double expires = ClaudeJson.Num(o, "expires");
            return new ClaudeSignIn { AccessToken = ClaudeJson.Str(o, "access") ?? "", RefreshToken = ClaudeJson.Str(o, "refresh") ?? "", Scope = ClaudeJson.Str(o, "scope") ?? "",
                ExpiresUtc = Double.IsNaN(expires) ? DateTime.MinValue : ClaudeBridge.FromEpoch(expires), Expired = ClaudeJson.Bool(o, "expired") };
        }
        public void Save(string path)
        {
            var o = new JsonObject();
            o["access"] = AccessToken; o["refresh"] = RefreshToken; o["scope"] = Scope; o["expires"] = new JsonNumber(ClaudeBridge.Epoch(ExpiresUtc)); o["expired"] = Expired;
            byte[] bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(ClaudeJson.Compact(o)), Entropy, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("WidgetScreen.ClaudeSignIn");
    }

    // Settings → Claude → Sign in with Claude (docs/decisions/015-claude-sign-in-and-telemetry.md): the same browser sign-in
    // Claude Code's /login uses (OAuth 2 with PKCE, Claude Code's public client, a loopback redirect), asking only for
    // user:profile, which is what the plan usage check needs. It is the widget's own grant, so refreshing it never
    // touches Claude Code's sign-in. The tokens go only to Anthropic's hosts (platform.claude.com for tokens,
    // api.anthropic.com for usage), over HTTPS, and are never logged or sent to the phone. Sign out deletes them.
    public sealed class ClaudeAccount : IDisposable
    {
        // From Claude Code 2.1.309's own configuration (read 1 Nov 2026).
        public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
        public const string AuthorizeUrl = "https://claude.com/cai/oauth/authorize";
        public const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
        public const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
        public const string Scope = "user:profile";
        public const string Beta = "oauth-2025-04-20";
        private readonly string path;
        private readonly HttpClient http;
        private readonly Action<string> openBrowser;
        private readonly object gate = new object();
        private ClaudeSignIn session;
        private CancellationTokenSource signing;
        public string StartupWarning { get; private set; }

        public ClaudeAccount(string path) : this(path, new PreferIPv4Handler(), Launcher.Open) { }
        public ClaudeAccount(string path, HttpMessageHandler handler, Action<string> openBrowser)
        {
            this.path = path; this.openBrowser = openBrowser;
            http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "WidgetScreen/" + typeof(ClaudeAccount).Assembly.GetName().Version);
            try { session = ClaudeSignIn.Load(path); }
            catch (Exception) { session = new ClaudeSignIn(); StartupWarning = "The Claude sign-in couldn't be read on this PC. Sign in again in Settings → Claude."; }
        }

        public bool SignedIn { get { lock (gate) return session.SignedIn && !session.Expired; } }
        public bool Expired { get { lock (gate) return session.Expired; } }
        public bool SigningIn { get { return signing != null; } }

        public static string Challenge(string verifier) { using (var sha = SHA256.Create()) return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier))); }
        private static string Base64Url(byte[] value) { return Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        private static string RandomValue() { var bytes = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes); return Base64Url(bytes); }

        public static string AuthorizeLink(int port, string state, string verifier)
        {
            return AuthorizeUrl + "?code=true&client_id=" + ClientId + "&response_type=code&redirect_uri=" + Uri.EscapeDataString(Redirect(port)) + "&scope=" + Uri.EscapeDataString(Scope)
                + "&code_challenge=" + Challenge(verifier) + "&code_challenge_method=S256&state=" + state;
        }
        public static string Redirect(int port) { return "http://localhost:" + port + "/callback"; }

        // "GET /callback?code=…&state=… HTTP/1.1" → the code, when the state matches.
        public static bool ReadCallback(string requestLine, string expectedState, out string code, out string error)
        {
            code = error = null;
            var parts = (requestLine ?? "").Trim().Split(' '); Uri callback;
            if (String.IsNullOrEmpty(expectedState) || parts.Length != 3 || parts[0] != "GET" || !parts[1].StartsWith("/callback?", StringComparison.Ordinal) || !Uri.TryCreate("http://localhost" + parts[1], UriKind.Absolute, out callback)) return false;
            var query = HttpUtility.ParseQueryString(callback.Query);
            if (query["state"] != expectedState) return false;
            code = query["code"]; error = query["error"];
            // The authorize page may hand back "code#state".
            if (code != null && code.Contains("#")) code = code.Substring(0, code.IndexOf('#'));
            return !String.IsNullOrWhiteSpace(code) || !String.IsNullOrWhiteSpace(error);
        }

        // Opens the browser on Claude's sign-in page and waits (up to 5 minutes) for it to come back to this PC.
        public async Task SignInAsync(Action<string> progress)
        {
            var cancel = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            if (Interlocked.CompareExchange(ref signing, cancel, null) != null) throw new InvalidOperationException("A Claude sign-in is already waiting in your browser.");
            TcpListener v4 = null, v6 = null;
            try
            {
                v4 = new TcpListener(IPAddress.Loopback, 0); v4.Start();
                int port = ((IPEndPoint)v4.LocalEndpoint).Port;
                // "localhost" may reach the PC over IPv6 first; listen there too when it can.
                try { v6 = new TcpListener(IPAddress.IPv6Loopback, port); v6.Start(); } catch (Exception) { v6 = null; }
                string verifier = RandomValue(), state = RandomValue();
                openBrowser(AuthorizeLink(port, state, verifier));
                using (cancel.Token.Register(() => { try { v4.Stop(); } catch (Exception) { } try { if (v6 != null) v6.Stop(); } catch (Exception) { } }))
                {
                    while (true)
                    {
                        var accepts = new List<Task<TcpClient>> { v4.AcceptTcpClientAsync() };
                        if (v6 != null) accepts.Add(v6.AcceptTcpClientAsync());
                        TcpClient client;
                        try { client = await (await Task.WhenAny(accepts).ConfigureAwait(false)).ConfigureAwait(false); }
                        catch (Exception) { if (cancel.IsCancellationRequested) throw new OperationCanceledException("The Claude sign-in timed out. Try again."); throw; }
                        using (client)
                        using (var stream = client.GetStream())
                        {
                            stream.ReadTimeout = 10000;
                            string line = ReadRequestLine(stream);
                            string code, error;
                            if (!ReadCallback(line, state, out code, out error)) { Answer(stream, false, "This isn't the sign-in Widget Screen is waiting for. Go back to Widget Screen and try again."); continue; }
                            if (!String.IsNullOrEmpty(error)) { Answer(stream, false, "The Claude sign-in was cancelled. You can try again from Widget Screen."); throw new InvalidOperationException("The Claude sign-in was cancelled."); }
                            if (progress != null) progress("Claude approved. Finishing the sign-in…");
                            try
                            {
                                var fields = new JsonObject();
                                fields["grant_type"] = "authorization_code"; fields["code"] = code; fields["redirect_uri"] = Redirect(port); fields["client_id"] = ClientId; fields["code_verifier"] = verifier; fields["state"] = state;
                                var signedIn = await TokenAsync(fields, null).ConfigureAwait(false);
                                lock (gate) { signedIn.Save(path); session = signedIn; StartupWarning = null; }
                            }
                            catch (Exception)
                            {
                                Answer(stream, false, "Claude approved, but Widget Screen couldn't finish the sign-in. Go back to Widget Screen for the reason.");
                                throw;
                            }
                            Answer(stream, true, "Widget Screen is signed in to Claude. You can close this tab.");
                            return;
                        }
                    }
                }
            }
            finally
            {
                try { if (v4 != null) v4.Stop(); } catch (Exception) { }
                try { if (v6 != null) v6.Stop(); } catch (Exception) { }
                Interlocked.Exchange(ref signing, null);
                cancel.Dispose();
            }
        }

        public void CancelSignIn() { var current = signing; if (current != null) try { current.Cancel(); } catch (Exception) { } }

        // Forgets the sign-in: the file goes. (There is no public revoke for this grant; claude.ai's settings list it.)
        public void SignOut()
        {
            lock (gate)
            {
                session = new ClaudeSignIn();
                try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
            }
        }

        private static string ReadRequestLine(Stream stream)
        {
            var head = new StringBuilder(); int b;
            while (head.Length < 16384 && (b = stream.ReadByte()) >= 0)
            {
                head.Append((char)b); int n = head.Length;
                if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n') break;
            }
            return head.ToString().Split('\n')[0];
        }
        private static void Answer(Stream stream, bool ok, string message)
        {
            try
            {
                byte[] body = Encoding.UTF8.GetBytes(message);
                byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + (ok ? "200 OK" : "400 Bad Request") + "\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                stream.Write(head, 0, head.Length); stream.Write(body, 0, body.Length);
            }
            catch (Exception) { }
        }

        // The token endpoint takes JSON. An invalid_grant (or 401) means the sign-in is over: sign in again.
        private async Task<ClaudeSignIn> TokenAsync(JsonObject fields, string keepRefresh)
        {
            HttpResponseMessage response;
            try { response = await http.PostAsync(TokenUrl, new StringContent(ClaudeJson.Compact(fields), Encoding.UTF8, "application/json")).ConfigureAwait(false); }
            catch (HttpRequestException) { throw new IOException("Couldn't reach Claude. Check the internet connection."); }
            catch (TaskCanceledException) { throw new IOException("Claude didn't answer in time."); }
            using (response)
            {
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    if (text.Contains("invalid_grant") || response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.BadRequest) throw new UnauthorizedAccessException("Claude ended the sign-in.");
                    throw new IOException("Claude's sign-in service answered " + (int)response.StatusCode + ".");
                }
                return Parse(text, keepRefresh);
            }
        }
        public static ClaudeSignIn Parse(string text, string keepRefresh)
        {
            object parsed; if (!ClaudeJson.TryParse(text, out parsed)) throw new IOException("Claude's sign-in answer couldn't be read.");
            var o = ClaudeJson.Obj(parsed);
            string access = ClaudeJson.Str(o, "access_token") ?? "", refresh = ClaudeJson.Str(o, "refresh_token") ?? keepRefresh ?? "";
            double seconds = ClaudeJson.Num(o, "expires_in");
            if (access.Length == 0) throw new IOException("Claude's sign-in answer had no token.");
            return new ClaudeSignIn { AccessToken = access, RefreshToken = refresh, Scope = ClaudeJson.Str(o, "scope") ?? Scope, ExpiresUtc = DateTime.UtcNow.AddSeconds(Double.IsNaN(seconds) ? 3600 : seconds) };
        }

        private async Task<string> FreshTokenAsync(bool force)
        {
            ClaudeSignIn current; lock (gate) current = session;
            if (!current.SignedIn || current.Expired) return null;
            if (!force && current.AccessToken.Length > 0 && current.ExpiresUtc > DateTime.UtcNow.AddMinutes(2)) return current.AccessToken;
            if (current.RefreshToken.Length == 0) { MarkExpired(); return null; }
            var fields = new JsonObject();
            fields["grant_type"] = "refresh_token"; fields["refresh_token"] = current.RefreshToken; fields["client_id"] = ClientId; fields["scope"] = Scope;
            try
            {
                var next = await TokenAsync(fields, current.RefreshToken).ConfigureAwait(false);
                lock (gate) { if (session != current) return session.AccessToken; next.Save(path); session = next; }
                return next.AccessToken;
            }
            catch (UnauthorizedAccessException) { MarkExpired(); return null; }
        }
        private void MarkExpired()
        {
            lock (gate) { session.Expired = true; session.AccessToken = ""; try { session.Save(path); } catch (Exception) { } }
        }

        // The plan's usage, read-only and free: GET api.anthropic.com/api/oauth/usage with the widget's own token. A 401
        // gets one refresh and one retry. Null when signed out or the sign-in is over (Expired says which); throws
        // IOException for a network or server problem.
        public async Task<ClaudeLimits> UsageAsync(DateTime now)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                string token = await FreshTokenAsync(attempt > 0).ConfigureAwait(false);
                if (token == null) return null;
                using (var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl))
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    request.Headers.TryAddWithoutValidation("anthropic-beta", Beta);
                    token = null;
                    HttpResponseMessage response;
                    try { response = await http.SendAsync(request).ConfigureAwait(false); }
                    catch (HttpRequestException) { throw new IOException("Couldn't reach api.anthropic.com."); }
                    catch (TaskCanceledException) { throw new IOException("api.anthropic.com didn't answer in time."); }
                    using (response)
                    {
                        if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0) continue;
                        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) { MarkExpired(); return null; }
                        if (!response.IsSuccessStatusCode) throw new IOException("api.anthropic.com answered " + (int)response.StatusCode + ".");
                        var limits = ClaudeBridge.ParseUsageResponse(await response.Content.ReadAsStringAsync().ConfigureAwait(false), now);
                        if (limits == null) throw new IOException("The plan usage answer couldn't be read.");
                        return limits;
                    }
                }
            }
            return null;
        }

        public void Dispose() { CancelSignIn(); http.Dispose(); }
    }
}
