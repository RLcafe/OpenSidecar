using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WidgetScreen
{
    // An error the Discord app returned for one command (an ERROR response) or for the whole connection (a CLOSE frame).
    public sealed class DiscordRpcException : InvalidOperationException
    {
        public int Code { get; private set; }
        public DiscordRpcException(int code, string message) : base(message ?? "Discord returned an error.") { Code = code; }
    }
    public sealed class DiscordNotRunningException : InvalidOperationException
    {
        public DiscordNotRunningException() : base("Discord isn't running. Open the Discord app on this PC.") { }
    }

    // One connection to the Discord desktop app's local pipe. Every frame is an opcode and a length
    // (both 32-bit little-endian) followed by that many bytes of UTF-8 JSON. Commands carry a nonce and
    // the reply echoes it; DISPATCH frames without a pending nonce are subscribed events.
    public sealed class DiscordRpc : IDisposable
    {
        public const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
        public const int MaxFrame = 8 * 1024 * 1024;
        private readonly Stream stream;
        private readonly SemaphoreSlim writeLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, TaskCompletionSource<object>> pending = new Dictionary<string, TaskCompletionSource<object>>();
        private readonly object sync = new object();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = MaxFrame };
        private int nonce, closed;
        // Raised on a worker thread with the event name and its data.
        public event Action<string, Dictionary<string, object>> EventReceived;
        // Raised once, on a worker thread, when the pipe closes for any reason other than Dispose.
        public event Action<string> Closed;
        public Dictionary<string, object> ReadyUser { get; private set; }

        private DiscordRpc(Stream stream) { this.stream = stream; }

        public static async Task<DiscordRpc> ConnectAsync(string clientId, CancellationToken cancellation, string pipePrefix = "discord-ipc-")
        {
            Stream pipe = null;
            // Discord takes the first free name from discord-ipc-0 to -9; a missing pipe fails fast.
            for (int index = 0; index < 10 && pipe == null; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                var candidate = new NamedPipeClientStream(".", pipePrefix + index, PipeDirection.InOut, PipeOptions.Asynchronous);
                try { await Task.Run(() => candidate.Connect(100), cancellation).ConfigureAwait(false); pipe = candidate; }
                catch (Exception e) { candidate.Dispose(); if (e is OperationCanceledException) throw; }
            }
            if (pipe == null) throw new DiscordNotRunningException();
            var rpc = new DiscordRpc(pipe);
            try { await rpc.HandshakeAsync(clientId, cancellation).ConfigureAwait(false); }
            catch { rpc.Dispose(); throw; }
            return rpc;
        }
        // Wraps any stream; the tests use this with an in-process pipe server.
        public static async Task<DiscordRpc> OpenAsync(Stream stream, string clientId, CancellationToken cancellation)
        {
            var rpc = new DiscordRpc(stream);
            try { await rpc.HandshakeAsync(clientId, cancellation).ConfigureAwait(false); }
            catch { rpc.Dispose(); throw; }
            return rpc;
        }

        private async Task HandshakeAsync(string clientId, CancellationToken cancellation)
        {
            await WriteAsync(OpHandshake, json.Serialize(new Dictionary<string, object> { { "v", 1 }, { "client_id", clientId } }), cancellation).ConfigureAwait(false);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var frame = await ReadFrameAsync(stream, timeout.Token).ConfigureAwait(false);
                var body = Object(Parse(frame.Item2));
                if (frame.Item1 == OpClose) throw new DiscordRpcException(Number(body, "code"), Field(body, "message"));
                if (frame.Item1 != OpFrame || Field(body, "evt") != "READY") throw new DiscordRpcException(0, "Discord didn't accept the connection.");
                ReadyUser = Object(Value(Object(Value(body, "data")), "user"));
            }
            // The read loop runs for the life of the connection and reports its end through Closed.
            var reader = Task.Run(() => ReadLoopAsync());
        }

        public static byte[] Encode(int opcode, string payload)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload ?? "");
            byte[] frame = new byte[8 + body.Length];
            BitConverter.GetBytes(opcode).CopyTo(frame, 0); BitConverter.GetBytes(body.Length).CopyTo(frame, 4); body.CopyTo(frame, 8);
            return frame;
        }
        public static async Task<Tuple<int, string>> ReadFrameAsync(Stream source, CancellationToken cancellation)
        {
            byte[] header = await ReadExactlyAsync(source, 8, cancellation).ConfigureAwait(false);
            int opcode = BitConverter.ToInt32(header, 0), length = BitConverter.ToInt32(header, 4);
            if (length < 0 || length > MaxFrame) throw new IOException("Discord sent a frame that is too large.");
            byte[] body = await ReadExactlyAsync(source, length, cancellation).ConfigureAwait(false);
            return Tuple.Create(opcode, Encoding.UTF8.GetString(body));
        }
        private static async Task<byte[]> ReadExactlyAsync(Stream source, int count, CancellationToken cancellation)
        {
            byte[] buffer = new byte[count]; int read = 0;
            while (read < count)
            {
                int chunk = await source.ReadAsync(buffer, read, count - read, cancellation).ConfigureAwait(false);
                if (chunk == 0) throw new EndOfStreamException("Discord closed the connection.");
                read += chunk;
            }
            return buffer;
        }
        private async Task WriteAsync(int opcode, string payload, CancellationToken cancellation)
        {
            byte[] frame = Encode(opcode, payload);
            await writeLock.WaitAsync(cancellation).ConfigureAwait(false);
            try { await stream.WriteAsync(frame, 0, frame.Length, cancellation).ConfigureAwait(false); await stream.FlushAsync(cancellation).ConfigureAwait(false); }
            finally { writeLock.Release(); }
        }

        private async Task ReadLoopAsync()
        {
            string reason = "Discord closed the connection.";
            try
            {
                while (Volatile.Read(ref closed) == 0)
                {
                    var frame = await ReadFrameAsync(stream, CancellationToken.None).ConfigureAwait(false);
                    if (frame.Item1 == OpPing) { await WriteAsync(OpPong, frame.Item2, CancellationToken.None).ConfigureAwait(false); continue; }
                    var body = Object(Parse(frame.Item2));
                    if (frame.Item1 == OpClose) { reason = Field(body, "message") ?? reason; break; }
                    if (frame.Item1 != OpFrame) continue;
                    string id = Field(body, "nonce");
                    TaskCompletionSource<object> waiter = null;
                    if (id != null) lock (sync) { if (pending.TryGetValue(id, out waiter)) pending.Remove(id); }
                    if (waiter != null)
                    {
                        var data = Value(body, "data");
                        if (Field(body, "evt") == "ERROR") waiter.TrySetException(new DiscordRpcException(Number(Object(data), "code"), Field(Object(data), "message")));
                        else waiter.TrySetResult(data);
                    }
                    else if (Field(body, "cmd") == "DISPATCH")
                    {
                        var handler = EventReceived;
                        if (handler != null) try { handler(Field(body, "evt"), Object(Value(body, "data"))); } catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
            Shutdown(reason, true);
        }

        public Task<object> CommandAsync(string command, Dictionary<string, object> args, CancellationToken cancellation) { return CommandAsync(command, args, null, TimeSpan.FromSeconds(10), cancellation); }
        public async Task<object> CommandAsync(string command, Dictionary<string, object> args, string evt, TimeSpan timeout, CancellationToken cancellation)
        {
            if (Volatile.Read(ref closed) != 0) throw new DiscordRpcException(0, "Discord closed the connection.");
            string id = Interlocked.Increment(ref nonce).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var waiter = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (sync) pending[id] = waiter;
            var message = new Dictionary<string, object> { { "cmd", command }, { "args", args ?? new Dictionary<string, object>() }, { "nonce", id } };
            if (evt != null) message["evt"] = evt;
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            using (limit.Token.Register(() => { lock (sync) pending.Remove(id); waiter.TrySetCanceled(); }))
            {
                limit.CancelAfter(timeout);
                try
                {
                    await WriteAsync(OpFrame, json.Serialize(message), limit.Token).ConfigureAwait(false);
                    return await waiter.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref closed) != 0) throw new DiscordRpcException(0, "Discord closed the connection.");
                    throw new TimeoutException("Discord didn't answer in time.");
                }
                catch (IOException) { throw new DiscordRpcException(0, "Discord closed the connection."); }
                catch (ObjectDisposedException) { throw new DiscordRpcException(0, "Discord closed the connection."); }
                finally { lock (sync) pending.Remove(id); }
            }
        }

        private void Shutdown(string reason, bool notify)
        {
            if (Interlocked.Exchange(ref closed, 1) != 0) return;
            try { stream.Dispose(); } catch (Exception) { }
            List<TaskCompletionSource<object>> waiting;
            lock (sync) { waiting = new List<TaskCompletionSource<object>>(pending.Values); pending.Clear(); }
            foreach (var waiter in waiting) waiter.TrySetCanceled();
            var handler = Closed;
            if (notify && handler != null) try { handler(reason); } catch (Exception) { }
        }
        public bool IsOpen { get { return Volatile.Read(ref closed) == 0; } }
        public void Dispose() { Shutdown(null, false); }

        private object Parse(string value) { try { return String.IsNullOrWhiteSpace(value) ? null : json.DeserializeObject(value); } catch (Exception) { return null; } }
        internal static Dictionary<string, object> Object(object value) { return value as Dictionary<string, object> ?? new Dictionary<string, object>(); }
        internal static object Value(Dictionary<string, object> value, string key) { object result; return value.TryGetValue(key, out result) ? result : null; }
        internal static string Field(Dictionary<string, object> value, string key) { var result = Value(value, key); return result == null ? null : result as string ?? Convert.ToString(result, System.Globalization.CultureInfo.InvariantCulture); }
        internal static int Number(Dictionary<string, object> value, string key)
        {
            var result = Value(value, key);
            try { return result == null ? 0 : Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture); } catch (Exception) { return 0; }
        }
    }
}
