using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WidgetScreen
{
    // One thing Claude Code's telemetry said about a session: a prompt, a tool decision or result, an API request
    // (with its usage), or only that the session was active (a metric).
    public sealed class ClaudeTelemetryEvent
    {
        public string Name = "", Session = "", Service = "", Entrypoint = "", Repository = "";
        public DateTime At;
        public ClaudeUsageEntry Usage;
    }

    // Claude Code's OpenTelemetry export in OTLP/HTTP JSON (docs/decisions/015-claude-sign-in-and-telemetry.md). Logs carry
    // events (claude_code.user_prompt, tool_decision, tool_result, api_request with model, tokens, cost_usd and
    // request_id); metrics carry claude_code.token.usage and cost.usage sums. Events count usage, because each one
    // names its request and so can be matched to the log line for the same reply; metrics are sums without one, so
    // they only say a session was active. Everything is parsed defensively: anything unexpected is skipped.
    public static class ClaudeOtlp
    {
        public static List<ClaudeTelemetryEvent> ParseLogs(string json)
        {
            var result = new List<ClaudeTelemetryEvent>();
            object parsed; if (!ClaudeJson.TryParse(json, out parsed)) return result;
            foreach (var resource in Objects(ClaudeJson.List(ClaudeJson.Obj(parsed), "resourceLogs")))
            {
                var resourceAttributes = Attributes(ClaudeJson.List(ClaudeJson.Obj(resource, "resource"), "attributes"));
                foreach (var scope in Objects(ClaudeJson.List(resource, "scopeLogs")))
                    foreach (var record in Objects(ClaudeJson.List(scope, "logRecords")))
                    {
                        var attributes = Attributes(ClaudeJson.List(record, "attributes"));
                        string name = Value(attributes, "event.name") ?? "";
                        string body = Any(ClaudeJson.Obj(record, "body")) as string ?? "";
                        if (name.Length == 0 && body.StartsWith("claude_code.", StringComparison.Ordinal)) name = body.Substring("claude_code.".Length);
                        if (name.StartsWith("claude_code.", StringComparison.Ordinal)) name = name.Substring("claude_code.".Length);
                        if (name.Length == 0) continue;
                        var e = Event(name, attributes, resourceAttributes, Time(record, attributes));
                        if (e.Session.Length == 0) continue;
                        if (name == "api_request")
                        {
                            long input = Long(attributes, "input_tokens"), output = Long(attributes, "output_tokens"), read = Long(attributes, "cache_read_tokens"), creation = Long(attributes, "cache_creation_tokens");
                            double cost = Double(attributes, "cost_usd");
                            string model = Value(attributes, "model") ?? "";
                            if (System.Double.IsNaN(cost))
                            {
                                long hour = Math.Min(creation, Long(attributes, "cache_creation_1h_tokens"));
                                cost = ClaudePrices.Cost(model, input, output, creation - hour, hour, read);
                            }
                            e.Usage = new ClaudeUsageEntry { At = e.At, Session = e.Session, Model = model, Request = Value(attributes, "request_id") ?? "", Tokens = input + output + read + creation, Cost = Math.Max(0, cost), FromTelemetry = true };
                        }
                        result.Add(e);
                    }
            }
            return result;
        }

        // One "active" event per session a metrics export mentions, at its newest data point.
        public static List<ClaudeTelemetryEvent> ParseMetrics(string json)
        {
            var newest = new Dictionary<string, ClaudeTelemetryEvent>();
            object parsed; if (!ClaudeJson.TryParse(json, out parsed)) return new List<ClaudeTelemetryEvent>();
            foreach (var resource in Objects(ClaudeJson.List(ClaudeJson.Obj(parsed), "resourceMetrics")))
            {
                var resourceAttributes = Attributes(ClaudeJson.List(ClaudeJson.Obj(resource, "resource"), "attributes"));
                foreach (var scope in Objects(ClaudeJson.List(resource, "scopeMetrics")))
                    foreach (var metric in Objects(ClaudeJson.List(scope, "metrics")))
                    {
                        string name = ClaudeJson.Str(metric, "name") ?? "";
                        if (!name.StartsWith("claude_code.", StringComparison.Ordinal)) continue;
                        var data = ClaudeJson.Obj(metric, "sum") ?? ClaudeJson.Obj(metric, "gauge");
                        foreach (var point in Objects(ClaudeJson.List(data, "dataPoints")))
                        {
                            var attributes = Attributes(ClaudeJson.List(point, "attributes"));
                            var e = Event("active", attributes, resourceAttributes, Time(point, attributes));
                            if (e.Session.Length == 0) continue;
                            ClaudeTelemetryEvent known;
                            if (!newest.TryGetValue(e.Session, out known) || known.At < e.At) newest[e.Session] = e;
                        }
                    }
            }
            return newest.Values.ToList();
        }

        private static ClaudeTelemetryEvent Event(string name, Dictionary<string, object> attributes, Dictionary<string, object> resource, DateTime at)
        {
            Func<string, string> either = key => Value(attributes, key) ?? Value(resource, key);
            return new ClaudeTelemetryEvent { Name = name, Session = either("session.id") ?? "", Service = Value(resource, "service.name") ?? "",
                Entrypoint = either("app.entrypoint") ?? either("entrypoint") ?? "", Repository = either("vcs.repository.name") ?? "", At = at };
        }

        private static IEnumerable<JsonObject> Objects(List<object> list) { return list == null ? Enumerable.Empty<JsonObject>() : list.OfType<JsonObject>(); }

        // OTLP attributes: [{ "key": "...", "value": { "stringValue" | "intValue" | "doubleValue" | "boolValue": ... } }].
        private static Dictionary<string, object> Attributes(object value)
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in Objects(value as List<object>))
            {
                string key = ClaudeJson.Str(pair, "key");
                if (!String.IsNullOrEmpty(key)) result[key] = Any(ClaudeJson.Obj(pair, "value"));
            }
            return result;
        }
        private static object Any(JsonObject value)
        {
            if (value == null) return null;
            if (value.Contains("stringValue")) return value.Get("stringValue") as string;
            if (value.Contains("intValue")) return Number(value.Get("intValue"));
            if (value.Contains("doubleValue")) return Number(value.Get("doubleValue"));
            if (value.Contains("boolValue")) return value.Get("boolValue");
            return null;
        }
        // int64s come as strings in OTLP JSON; doubles as numbers (or strings, from some exporters).
        private static object Number(object value)
        {
            if (value is JsonNumber) return ((JsonNumber)value).Value;
            double number; if (value is string && System.Double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return number;
            return null;
        }
        private static string Value(Dictionary<string, object> attributes, string key)
        {
            object value; if (!attributes.TryGetValue(key, out value) || value == null) return null;
            if (value is double) return ((double)value).ToString(CultureInfo.InvariantCulture);
            string text = value as string;
            return String.IsNullOrEmpty(text) ? null : text;
        }
        private static double Double(Dictionary<string, object> attributes, string key)
        {
            object value; if (!attributes.TryGetValue(key, out value)) return System.Double.NaN;
            if (value is double) return (double)value;
            double number; return value is string && System.Double.TryParse((string)value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) ? number : System.Double.NaN;
        }
        private static long Long(Dictionary<string, object> attributes, string key) { double v = Double(attributes, key); return System.Double.IsNaN(v) || v < 0 ? 0 : (long)v; }

        // timeUnixNano (or observedTimeUnixNano), else the event.timestamp attribute, else now.
        private static DateTime Time(JsonObject record, Dictionary<string, object> attributes)
        {
            foreach (var key in new[] { "timeUnixNano", "observedTimeUnixNano" })
            {
                var number = Number(record.Get(key));
                if (number is double && (double)number > 0) return ClaudeBridge.FromEpoch((double)number / 1e6);
            }
            var stamp = ClaudeBridge.ParseTime(Value(attributes, "event.timestamp"));
            return stamp ?? DateTime.UtcNow;
        }
    }

    // A small OTLP/HTTP receiver for Claude Code's telemetry, on 127.0.0.1 only (Ali asked for it, 1 Nov 2026). It
    // accepts POST /v1/logs and POST /v1/metrics with a JSON body of at most 4 MB (gzip allowed), answers anything else
    // with 404, and drops any connection that isn't from this PC. It never touches the phone link.
    public sealed class ClaudeTelemetry : IDisposable
    {
        public const int DefaultPort = 43832;
        public const int MaxBody = 4 * 1024 * 1024;
        private readonly int requestedPort;
        private readonly Action<List<ClaudeTelemetryEvent>> received;
        private TcpListener listener;
        private CancellationTokenSource stop;
        public int Port { get; private set; }
        public DateTime LastReceived { get; private set; }
        public string Problem { get; private set; }
        public bool Running { get { return listener != null; } }

        public ClaudeTelemetry(int port, Action<List<ClaudeTelemetryEvent>> received) { requestedPort = port; this.received = received; }

        public bool Start()
        {
            if (listener != null) return true;
            try
            {
                var next = new TcpListener(IPAddress.Loopback, requestedPort);
                next.Start(16);
                listener = next; Port = ((IPEndPoint)next.LocalEndpoint).Port; Problem = null;
                stop = new CancellationTokenSource();
                Task.Run(() => AcceptLoop(next, stop.Token));
                return true;
            }
            catch (SocketException e) { Problem = "Port " + requestedPort + " on this PC is in use (" + e.SocketErrorCode + "), so telemetry can't arrive."; return false; }
        }

        public void Stop()
        {
            if (listener == null) return;
            try { stop.Cancel(); listener.Stop(); } catch (Exception) { }
            listener = null;
        }

        public void Dispose() { Stop(); }

        public static bool Loopback(EndPoint remote)
        {
            var ip = remote as IPEndPoint;
            return ip != null && IPAddress.IsLoopback(ip.Address.IsIPv4MappedToIPv6 ? ip.Address.MapToIPv4() : ip.Address);
        }

        private async Task AcceptLoop(TcpListener server, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await server.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) { if (token.IsCancellationRequested) return; continue; }
                var ignored = Task.Run(() => Serve(client));
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    if (!Loopback(client.Client.RemoteEndPoint)) return;
                    client.ReceiveTimeout = 10000; client.SendTimeout = 10000;
                    var stream = client.GetStream();
                    string path; byte[] body;
                    int status = Read(stream, out path, out body);
                    if (status == 200)
                    {
                        string json = Encoding.UTF8.GetString(body);
                        var events = path == "/v1/logs" ? ClaudeOtlp.ParseLogs(json) : ClaudeOtlp.ParseMetrics(json);
                        LastReceived = DateTime.UtcNow;
                        if (events.Count > 0) try { received(events); } catch (Exception) { }
                    }
                    Respond(stream, status);
                }
                catch (Exception) { }
            }
        }

        // Reads one request. 200 with the body for an accepted export; otherwise the status to answer with.
        public static int Read(Stream stream, out string path, out byte[] body)
        {
            path = ""; body = null;
            var head = new StringBuilder(); int b;
            while (true)
            {
                if (head.Length > 16384) return 431;
                b = stream.ReadByte(); if (b < 0) return 400;
                head.Append((char)b);
                int n = head.Length;
                if (n >= 4 && head[n - 4] == '\r' && head[n - 3] == '\n' && head[n - 2] == '\r' && head[n - 1] == '\n') break;
            }
            var lines = head.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
            var request = lines[0].Split(' ');
            if (request.Length < 3) return 400;
            path = request[1]; int query = path.IndexOf('?'); if (query >= 0) path = path.Substring(0, query);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines.Skip(1)) { int colon = line.IndexOf(':'); if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim(); }
            if (path != "/v1/logs" && path != "/v1/metrics") return 404;
            if (request[0] != "POST") return 405;
            string type; if (headers.TryGetValue("Content-Type", out type) && type.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0) return 415;
            byte[] raw;
            string encoding;
            if (headers.TryGetValue("Transfer-Encoding", out encoding) && encoding.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0) raw = Chunked(stream);
            else
            {
                string text; long length;
                if (!headers.TryGetValue("Content-Length", out text) || !Int64.TryParse(text, out length) || length < 0) return 411;
                if (length > MaxBody) return 413;
                raw = Exactly(stream, (int)length);
            }
            if (raw == null) return 413;
            if (headers.TryGetValue("Content-Encoding", out encoding) && encoding.Equals("gzip", StringComparison.OrdinalIgnoreCase))
            {
                using (var zip = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress))
                using (var plain = new MemoryStream())
                {
                    var buffer = new byte[8192]; int count;
                    while ((count = zip.Read(buffer, 0, buffer.Length)) > 0) { plain.Write(buffer, 0, count); if (plain.Length > MaxBody) return 413; }
                    raw = plain.ToArray();
                }
            }
            body = raw;
            return 200;
        }
        private static byte[] Exactly(Stream stream, int length)
        {
            var data = new byte[length]; int read = 0;
            while (read < length) { int n = stream.Read(data, read, length - read); if (n <= 0) return null; read += n; }
            return data;
        }
        private static byte[] Chunked(Stream stream)
        {
            using (var all = new MemoryStream())
            {
                while (true)
                {
                    var size = new StringBuilder(); int b;
                    while ((b = stream.ReadByte()) >= 0 && b != '\n') { if (b != '\r') size.Append((char)b); if (size.Length > 32) return null; }
                    string hex = size.ToString().Split(';')[0].Trim(); int length;
                    if (!Int32.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out length) || length < 0) return null;
                    if (length == 0) { while ((b = stream.ReadByte()) >= 0 && b != '\n') { } return all.ToArray(); }
                    if (all.Length + length > MaxBody) return null;
                    var chunk = Exactly(stream, length); if (chunk == null) return null;
                    all.Write(chunk, 0, length);
                    stream.ReadByte(); stream.ReadByte();
                }
            }
        }
        private static void Respond(Stream stream, int status)
        {
            string reason = status == 200 ? "OK" : status == 404 ? "Not Found" : status == 405 ? "Method Not Allowed" : status == 413 ? "Payload Too Large" : status == 415 ? "Unsupported Media Type" : status == 411 ? "Length Required" : "Bad Request";
            byte[] body = Encoding.UTF8.GetBytes(status == 200 ? "{}" : "");
            byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
            stream.Write(head, 0, head.Length); stream.Write(body, 0, body.Length); stream.Flush();
        }
    }
}
