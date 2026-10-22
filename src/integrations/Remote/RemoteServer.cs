using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    public sealed class RemoteRequest
    {
        public string Method, Path, Query = "", Body = "";
        public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string Header(string name) { string value; return Headers.TryGetValue(name, out value) ? value : null; }
        public string Token
        {
            get { string value = Header("Authorization"); return value != null && value.StartsWith("Bearer ", StringComparison.Ordinal) ? value.Substring(7).Trim() : null; }
        }
        public string QueryValue(string name)
        {
            foreach (string pair in Query.Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals > 0 && Uri.UnescapeDataString(pair.Substring(0, equals)) == name) return Uri.UnescapeDataString(pair.Substring(equals + 1).Replace('+', ' '));
            }
            return null;
        }
    }

    public sealed class RemoteResponse
    {
        public int Status = 200;
        public string ContentType = "application/json; charset=utf-8";
        public byte[] Body = new byte[0];
        public static RemoteResponse Json(int status, object value)
        {
            return new RemoteResponse { Status = status, Body = Encoding.UTF8.GetBytes(new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(value)) };
        }
        public static RemoteResponse Error(int status, string message) { return Json(status, new Dictionary<string, object> { { "error", message } }); }
    }

    // The phone link: a small HTTP/1.1 listener for the Widget Screen Android app on the local network, one request
    // per connection, plus a UDP responder so the phone can find this PC. Requests are handed to the dashboard;
    // nothing here knows about widgets. There is no separate server program.
    public sealed class RemoteServer : IDisposable
    {
        public const int DefaultPort = 43830, DefaultDiscoveryPort = 43831;
        public const string DiscoveryRequest = "WIDGETSCREEN-DISCOVER";
        private const int MaxHead = 16 * 1024, MaxBody = 64 * 1024;
        // Connections served at once. A misbehaving device can't hold the link open for everyone else.
        public const int MaxConnections = 32;
        private readonly SemaphoreSlim connections = new SemaphoreSlim(MaxConnections, MaxConnections);
        private readonly Func<RemoteRequest, Task<RemoteResponse>> handler;
        private readonly Func<IPAddress, bool> allowed;
        private readonly TcpListener listener;
        private readonly UdpClient discovery;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly string name;
        public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
        public int DiscoveryPort { get { return discovery == null ? 0 : ((IPEndPoint)discovery.Client.LocalEndPoint).Port; } }

        // Throws SocketException when the port is taken.
        public RemoteServer(IPAddress address, int port, int discoveryPort, string name, Func<IPAddress, bool> allowed, Func<RemoteRequest, Task<RemoteResponse>> handler)
        {
            this.handler = handler; this.name = name; this.allowed = allowed;
            listener = new TcpListener(address, port);
            listener.Start(16);
            if (discoveryPort >= 0)
            {
                try { discovery = new UdpClient(new IPEndPoint(address, discoveryPort)); }
                catch (SocketException) { discovery = null; /* Finding the PC is optional; the phone can type the address. */ }
            }
            Task.Run(() => AcceptLoop());
            if (discovery != null) Task.Run(() => DiscoveryLoop());
        }

        private async Task AcceptLoop()
        {
            while (!lifetime.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (lifetime.IsCancellationRequested) return; continue; }
                var accepted = client;
                var ignored = Task.Run(() => Serve(accepted));
            }
        }

        private async Task DiscoveryLoop()
        {
            byte[] reply = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new Dictionary<string, object> { { "app", "Widget Screen" }, { "name", name }, { "port", Port } }));
            while (!lifetime.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try { received = await discovery.ReceiveAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (lifetime.IsCancellationRequested) return; continue; }
                if (!allowed(received.RemoteEndPoint.Address) || Encoding.UTF8.GetString(received.Buffer).Trim() != DiscoveryRequest) continue;
                try { await discovery.SendAsync(reply, reply.Length, received.RemoteEndPoint).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }

        // A request a web page made rather than the phone app. A page in the user's browser could reach this port
        // through DNS rebinding, so anything carrying browser fetch headers, or a Host that isn't an address of this
        // PC, is refused. The phone app sends a bare IP address as Host and no Origin.
        public static string RefuseBrowser(RemoteRequest request)
        {
            if (request.Header("Origin") != null || request.Header("Sec-Fetch-Mode") != null || request.Header("Sec-Fetch-Site") != null) return "Web pages can't use the phone link.";
            string host = request.Header("Host");
            if (host == null) return null;
            host = host.Trim();
            if (host.StartsWith("[", StringComparison.Ordinal)) { int end = host.IndexOf(']'); host = end > 0 ? host.Substring(1, end - 1) : host; }
            else { int colon = host.LastIndexOf(':'); if (colon > 0 && host.IndexOf(':') == colon) host = host.Substring(0, colon); }
            IPAddress address;
            return host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(host, out address) ? null : "That name isn't this PC. Use the PC's address shown in Settings → Phone.";
        }

        private async Task Serve(TcpClient client)
        {
            // Over the limit: close at once rather than queue, so the phone's next request still gets a turn.
            if (!connections.Wait(0)) { try { client.Close(); } catch (Exception) { } return; }
            try { await ServeOne(client).ConfigureAwait(false); }
            finally { connections.Release(); }
        }

        private async Task ServeOne(TcpClient client)
        {
            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using (timeout.Token.Register(() => client.Close()))
                {
                    RemoteResponse response;
                    NetworkStream stream;
                    try
                    {
                        // Only devices on the local network; anything routed in from outside is dropped.
                        if (!allowed(((IPEndPoint)client.Client.RemoteEndPoint).Address)) return;
                        stream = client.GetStream();
                    }
                    catch (Exception) { return; }
                    try
                    {
                        RemoteRequest request = await ReadRequest(stream, timeout.Token).ConfigureAwait(false);
                        if (request == null) return;
                        string refusal = RefuseBrowser(request);
                        if (refusal != null) response = RemoteResponse.Error(403, refusal);
                        else
                        {
                            try { response = await handler(request).ConfigureAwait(false) ?? RemoteResponse.Error(500, "No response."); }
                            catch (Exception) { response = RemoteResponse.Error(500, "Widget Screen couldn't do that. Try again."); }
                        }
                    }
                    catch (InvalidDataException e) { response = RemoteResponse.Error(e.Message.StartsWith("Too large") ? 413 : 400, e.Message); }
                    catch (Exception) { return; }
                    try { await Write(stream, response, timeout.Token).ConfigureAwait(false); } catch (Exception) { }
                }
            }
        }

        public static RemoteRequest ParseHead(string head)
        {
            var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var parts = lines[0].Split(' ');
            if (parts.Length != 3 || !parts[2].StartsWith("HTTP/1.", StringComparison.Ordinal) || !parts[1].StartsWith("/", StringComparison.Ordinal)) throw new InvalidDataException("Malformed request.");
            var request = new RemoteRequest { Method = parts[0] };
            int question = parts[1].IndexOf('?');
            request.Path = question < 0 ? parts[1] : parts[1].Substring(0, question);
            request.Query = question < 0 ? "" : parts[1].Substring(question + 1);
            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) throw new InvalidDataException("Malformed header.");
                request.Headers[lines[i].Substring(0, colon).Trim()] = lines[i].Substring(colon + 1).Trim();
            }
            return request;
        }

        private static async Task<RemoteRequest> ReadRequest(NetworkStream stream, CancellationToken cancellation)
        {
            var buffer = new MemoryStream();
            byte[] chunk = new byte[4096];
            int headEnd = -1;
            while (headEnd < 0)
            {
                int count = await stream.ReadAsync(chunk, 0, chunk.Length, cancellation).ConfigureAwait(false);
                if (count == 0) return null;
                buffer.Write(chunk, 0, count);
                headEnd = IndexOfBlankLine(buffer.GetBuffer(), (int)buffer.Length);
                if (headEnd < 0 && buffer.Length > MaxHead) throw new InvalidDataException("Too large: request headers.");
            }
            byte[] data = buffer.ToArray();
            var request = ParseHead(Encoding.ASCII.GetString(data, 0, headEnd));
            int length = 0;
            string declared = request.Header("Content-Length");
            if (declared != null && (!Int32.TryParse(declared, out length) || length < 0)) throw new InvalidDataException("Malformed request.");
            if (length > MaxBody) throw new InvalidDataException("Too large: request body.");
            var body = new MemoryStream();
            body.Write(data, headEnd + 4, data.Length - headEnd - 4);
            while (body.Length < length)
            {
                int count = await stream.ReadAsync(chunk, 0, chunk.Length, cancellation).ConfigureAwait(false);
                if (count == 0) break;
                body.Write(chunk, 0, count);
            }
            request.Body = Encoding.UTF8.GetString(body.ToArray(), 0, (int)Math.Min(length, body.Length));
            return request;
        }

        private static int IndexOfBlankLine(byte[] data, int length)
        {
            for (int i = 0; i + 3 < length; i++) if (data[i] == '\r' && data[i + 1] == '\n' && data[i + 2] == '\r' && data[i + 3] == '\n') return i;
            return -1;
        }

        private static string Reason(int status)
        {
            switch (status)
            {
                case 200: return "OK"; case 400: return "Bad Request"; case 401: return "Unauthorized"; case 403: return "Forbidden";
                case 404: return "Not Found"; case 409: return "Conflict"; case 413: return "Payload Too Large"; case 429: return "Too Many Requests";
                default: return status >= 500 ? "Server Error" : "Status";
            }
        }

        private static async Task Write(NetworkStream stream, RemoteResponse response, CancellationToken cancellation)
        {
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + response.Status + " " + Reason(response.Status) + "\r\nContent-Type: " + response.ContentType + "\r\nContent-Length: " + response.Body.Length + "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(head, 0, head.Length, cancellation).ConfigureAwait(false);
            await stream.WriteAsync(response.Body, 0, response.Body.Length, cancellation).ConfigureAwait(false);
        }

        public void Dispose()
        {
            lifetime.Cancel();
            try { listener.Stop(); } catch (Exception) { }
            if (discovery != null) try { discovery.Close(); } catch (Exception) { }
        }
    }
}
