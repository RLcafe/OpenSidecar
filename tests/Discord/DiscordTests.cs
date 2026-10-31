using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WidgetScreen;

// Every check talks to an in-process fake of the Discord app's pipe and a fake token server.
// Nothing here opens the real discord-ipc pipe or contacts discord.com.
internal static class DiscordTests
{
    private const string ClientId = "123456789012345678";
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private sealed class FakeError : Exception { public int Code; public FakeError(int code, string message) : base(message) { Code = code; } }

    // Accepts connections on <prefix>0, answers the handshake with READY and each command through Answer.
    private sealed class FakeDiscord : IDisposable
    {
        public readonly string Prefix = "widgetscreen-test-" + Guid.NewGuid().ToString("N") + "-";
        public readonly List<string> Commands = new List<string>();
        public readonly List<Dictionary<string, object>> Args = new List<Dictionary<string, object>>();
        public Func<string, Dictionary<string, object>, object> Answer = (cmd, args) => null;
        public Dictionary<string, object> CloseOnHandshake;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private NamedPipeServerStream current;
        public int Connections;
        public FakeDiscord() { Task.Run(() => Serve()); }
        private async Task Serve()
        {
            while (!stop.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(Prefix + "0", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try
                {
                    await Task.Factory.FromAsync(server.BeginWaitForConnection, server.EndWaitForConnection, null);
                    lock (Commands) { current = server; Connections++; }
                    var hello = await DiscordRpc.ReadFrameAsync(server, stop.Token);
                    if (CloseOnHandshake != null) { await Send(server, DiscordRpc.OpClose, CloseOnHandshake); server.Dispose(); continue; }
                    await Send(server, DiscordRpc.OpFrame, new Dictionary<string, object> { { "cmd", "DISPATCH" }, { "evt", "READY" }, { "data", new Dictionary<string, object> { { "user", new Dictionary<string, object> { { "username", "tester" } } } } } });
                    while (!stop.IsCancellationRequested)
                    {
                        var frame = await DiscordRpc.ReadFrameAsync(server, stop.Token);
                        var message = (Dictionary<string, object>)json.DeserializeObject(frame.Item2);
                        string cmd = (string)message["cmd"]; var args = message["args"] as Dictionary<string, object> ?? new Dictionary<string, object>();
                        lock (Commands) { Commands.Add(cmd); Args.Add(args); }
                        var reply = new Dictionary<string, object> { { "cmd", cmd }, { "nonce", message["nonce"] } };
                        try { reply["data"] = Answer(cmd, args); }
                        catch (FakeError e) { reply["evt"] = "ERROR"; reply["data"] = new Dictionary<string, object> { { "code", e.Code }, { "message", e.Message } }; }
                        await Send(server, DiscordRpc.OpFrame, reply);
                    }
                }
                catch (Exception) { }
                finally { server.Dispose(); }
            }
        }
        private Task Send(Stream stream, int op, object body) { byte[] frame = DiscordRpc.Encode(op, json.Serialize(body)); return stream.WriteAsync(frame, 0, frame.Length); }
        public void Dispatch(string evt, Dictionary<string, object> data)
        {
            NamedPipeServerStream server; lock (Commands) server = current;
            Send(server, DiscordRpc.OpFrame, new Dictionary<string, object> { { "cmd", "DISPATCH" }, { "evt", evt }, { "data", data } }).Wait();
        }
        public void Drop() { NamedPipeServerStream server; lock (Commands) server = current; server.Dispose(); }
        public int Count(string cmd) { lock (Commands) return Commands.Count(c => c == cmd); }
        public void Dispose() { stop.Cancel(); try { if (current != null) current.Dispose(); } catch (Exception) { } }
    }
    private sealed class TokenServer : HttpMessageHandler
    {
        public readonly List<string> Bodies = new List<string>();
        public Func<string, HttpResponseMessage> Reply = body => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"fresh-access\",\"refresh_token\":\"fresh-refresh\",\"expires_in\":604800,\"scope\":\"rpc rpc.voice.read rpc.voice.write\"}") };
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Assert(request.RequestUri.ToString() == DiscordService.TokenUrl, "Unexpected request to " + request.RequestUri);
            string body = await request.Content.ReadAsStringAsync(); lock (Bodies) Bodies.Add(body); return Reply(body);
        }
    }
    private static object Channel() { return new Dictionary<string, object> { { "id", "111" }, { "name", "General" }, { "guild_id", "222" } }; }
    private static object Sounds()
    {
        Func<string, string, string, bool, object> sound = (id, guild, name, available) => new Dictionary<string, object> { { "sound_id", id }, { "guild_id", guild }, { "name", name }, { "available", available } };
        return new object[] { sound("3", "999", "Other server", true), sound("1", "DEFAULT", "Quack", true), sound("2", "222", "Airhorn", true), sound("4", "222", "Hidden", false), sound("2", "222", "Airhorn", true) };
    }
    private static object Standard(string cmd, Dictionary<string, object> args)
    {
        if (cmd == "AUTHENTICATE") { if ((string)args["access_token"] == "rejected") throw new FakeError(4009, "Invalid access token"); return new Dictionary<string, object> { { "user", new Dictionary<string, object> { { "username", "tester" }, { "global_name", "Tester" } } } }; }
        if (cmd == "GET_SELECTED_VOICE_CHANNEL") return Channel();
        if (cmd == "GET_SOUNDBOARD_SOUNDS") return Sounds();
        if (cmd == "PLAY_SOUNDBOARD_SOUND" && (string)args["sound_id"] == "3") throw new FakeError(4000, "Invalid Sound.");
        return null;
    }
    private static string Session(string directory, string name, string access, DateTime expires)
    {
        string path = Path.Combine(directory, "discord-" + name + "-" + Guid.NewGuid().ToString("N") + ".dat");
        DiscordCredentials.Save(path, new DiscordSession { ClientId = ClientId, ClientSecret = "test-secret-0123456789", AccessToken = access, RefreshToken = "test-refresh", Scope = "rpc rpc.voice.read rpc.voice.write", ExpiresUtc = expires });
        return path;
    }
    private static DiscordService Service(string path, FakeDiscord fake, TokenServer tokens)
    {
        return new DiscordService(path, tokens ?? new TokenServer(), (id, token) => DiscordRpc.ConnectAsync(id, token, fake.Prefix));
    }
    private static string Failure(Func<Task> action)
    {
        try { action().GetAwaiter().GetResult(); } catch (Exception e) { return e.Message; } throw new Exception("Expected failure was silently accepted.");
    }
    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        timer.Tick += (sender, e) => { timer.Stop(); frame.Continue = false; }; timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    private static void Wait(Func<bool> condition, string message) { for (int i = 0; i < 50 && !condition(); i++) Pump(); Assert(condition(), message); }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Discord frames round-trip and reject oversized lengths", () => {
            byte[] frame = DiscordRpc.Encode(DiscordRpc.OpFrame, "{\"cmd\":\"PING\",\"name\":\"Ünïcode\"}");
            var read = DiscordRpc.ReadFrameAsync(new MemoryStream(frame), CancellationToken.None).Result;
            Assert(read.Item1 == DiscordRpc.OpFrame && read.Item2.Contains("Ünïcode") && BitConverter.ToInt32(frame, 4) == frame.Length - 8, "Frame header or UTF-8 body is wrong.");
            byte[] huge = new byte[8]; BitConverter.GetBytes(1).CopyTo(huge, 0); BitConverter.GetBytes(DiscordRpc.MaxFrame + 1).CopyTo(huge, 4);
            Assert(Failure(() => DiscordRpc.ReadFrameAsync(new MemoryStream(huge), CancellationToken.None)).Contains("too large"), "Oversized frame was accepted.");
        });
        check("Discord app details validate and stay encrypted on disk", () => {
            Assert(DiscordService.ValidClientId(ClientId) && !DiscordService.ValidClientId("12345") && !DiscordService.ValidClientId("12345678901234567x"), "Application ID validation is wrong.");
            string path = Path.Combine(directory, "discord-app-" + Guid.NewGuid().ToString("N") + ".dat");
            using (var service = new DiscordService(path, new TokenServer(), (id, token) => { throw new Exception("No pipe expected."); }))
            {
                service.SaveApp(ClientId, "test-secret-0123456789");
                Assert(service.ClientId == ClientId && service.HasSecret && !service.Connected, "App details were not kept.");
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("test-secret"), "Client Secret was written in plain text.");
                Assert(Failure(() => Task.Run(() => service.SaveApp("not-a-number", ""))).Contains("Application ID"), "Bad Application ID was saved.");
            }
            using (var restored = new DiscordService(path, new TokenServer(), (id, token) => { throw new Exception("No pipe expected."); }))
                Assert(restored.ClientId == ClientId && restored.HasSecret, "App details did not survive a restart.");
        });
        check("Discord not running is reported plainly without a long wait", () => {
            string path = Session(directory, "offline", "test-access", DateTime.UtcNow.AddDays(1));
            using (var service = new DiscordService(path, new TokenServer(), (id, token) => DiscordRpc.ConnectAsync(id, token, "widgetscreen-missing-" + Guid.NewGuid().ToString("N") + "-")))
            {
                var started = DateTime.UtcNow; Exception error = null;
                try { service.SelectedChannelAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch (Exception e) { error = e; }
                Assert(error is DiscordNotRunningException && SoundboardWidget.Message(error).Contains("Open Discord"), "Missing Discord app was not reported as not running.");
                Assert(DateTime.UtcNow - started < TimeSpan.FromSeconds(8), "Looking for Discord took too long.");
                Assert(service.Connected, "Discord being closed signed the user out.");
            }
        });
        check("Discord sounds load, order by server and play with the right ids", () => {
            using (var fake = new FakeDiscord { Answer = Standard })
            using (var service = Service(Session(directory, "play", "test-access", DateTime.UtcNow.AddDays(1)), fake, null))
            {
                var channel = service.SelectedChannelAsync(CancellationToken.None).Result;
                Assert(channel != null && channel.Name == "General" && channel.GuildId == "222" && service.Linked, "Voice channel was not read.");
                Assert(fake.Commands.Take(2).SequenceEqual(new[] { "AUTHENTICATE", "SUBSCRIBE" }), "Discord was not signed in before commands.");
                var sounds = DiscordService.Order(service.SoundsAsync(CancellationToken.None).Result, channel.GuildId);
                Assert(sounds.Select(s => s.Name).SequenceEqual(new[] { "Airhorn", "Quack", "Other server" }), "Sounds are not deduplicated, filtered or ordered: " + String.Join(", ", sounds.Select(s => s.Name)));
                service.PlayAsync(sounds[0], CancellationToken.None).Wait();
                var played = fake.Args[fake.Commands.LastIndexOf("PLAY_SOUNDBOARD_SOUND")];
                Assert((string)played["sound_id"] == "2" && (string)played["guild_id"] == "222" && played.Count == 2, "Play sent the wrong sound.");
                Assert(Failure(() => service.PlayAsync(sounds[2], CancellationToken.None)).Contains("Nitro"), "Another server's sound failure was not explained.");
                Assert(fake.Connections == 1, "Each command opened a new connection.");
            }
        });
        check("Discord errors and token failures become actionable messages", () => {
            Assert(DiscordService.CommandMessage(new DiscordRpcException(4005, "Invalid Channel.")).Contains("Join a voice channel"), "Not-in-voice was not explained.");
            Assert(DiscordService.CommandMessage(new DiscordRpcException(4006, "Invalid Permissions.")).Contains("can't use the soundboard"), "Missing permission was not explained.");
            Assert(DiscordService.AuthorizeMessage(new DiscordRpcException(5000, "OAuth2 Error: access_denied")).Contains("declined"), "Declined approval was not explained.");
            Assert(DiscordService.TokenMessage(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\"}", true).Contains("expired"), "Expired code was not explained.");
            Assert(DiscordService.TokenMessage(HttpStatusCode.Unauthorized, "{\"error\":\"invalid_client\"}", false).Contains("Client Secret"), "Missing secret was not explained.");
            using (var fake = new FakeDiscord { CloseOnHandshake = new Dictionary<string, object> { { "code", 4000 }, { "message", "Invalid Client ID" } } })
            using (var service = Service(Session(directory, "badid", "test-access", DateTime.UtcNow.AddDays(1)), fake, null))
                Assert(Failure(() => service.SoundsAsync(CancellationToken.None)).Contains("doesn't recognize that Application ID"), "Unknown app id was not explained.");
        });
        check("Discord refreshes an expired or rejected token once and keeps it encrypted", () => {
            var tokens = new TokenServer();
            string path = Session(directory, "refresh", "rejected", DateTime.UtcNow.AddDays(1));
            using (var fake = new FakeDiscord { Answer = Standard })
            using (var service = Service(path, fake, tokens))
            {
                service.SelectedChannelAsync(CancellationToken.None).Wait();
                Assert(tokens.Bodies.Count == 1 && tokens.Bodies[0].Contains("grant_type=refresh_token") && tokens.Bodies[0].Contains("refresh_token=test-refresh") && tokens.Bodies[0].Contains("client_secret="), "Rejected token was not refreshed with the app secret.");
                Assert((string)fake.Args[fake.Commands.LastIndexOf("AUTHENTICATE")]["access_token"] == "fresh-access" && service.Linked, "Fresh token was not used.");
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("fresh-access") && DiscordCredentials.Load(path).AccessToken == "fresh-access", "Refreshed token was not saved encrypted.");
            }
            tokens.Reply = body => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\"}") };
            string expired = Session(directory, "expired", "test-access", DateTime.UtcNow.AddMinutes(-1));
            using (var fake = new FakeDiscord { Answer = Standard })
            using (var service = Service(expired, fake, tokens))
            {
                Assert(Failure(() => service.SoundsAsync(CancellationToken.None)).Contains("Connect again"), "Revoked sign-in was not reported.");
                Assert(!service.Connected && service.ClientId == ClientId && service.HasSecret && fake.Connections == 0, "Revoked sign-in kept tokens, lost the app details or opened the pipe.");
            }
        });
        check("Discord connection loss and voice channel changes notify without polling", () => {
            using (var fake = new FakeDiscord { Answer = Standard })
            using (var service = Service(Session(directory, "events", "test-access", DateTime.UtcNow.AddDays(1)), fake, null))
            {
                int changes = 0; service.Changed += () => Interlocked.Increment(ref changes);
                service.SelectedChannelAsync(CancellationToken.None).Wait();
                fake.Dispatch("VOICE_CHANNEL_SELECT", new Dictionary<string, object> { { "channel_id", null }, { "guild_id", null } });
                for (int i = 0; i < 50 && Volatile.Read(ref changes) == 0; i++) Thread.Sleep(20);
                Assert(Volatile.Read(ref changes) == 1, "Voice channel change was not reported.");
                fake.Drop();
                for (int i = 0; i < 50 && service.Linked; i++) Thread.Sleep(20);
                Assert(!service.Linked && Volatile.Read(ref changes) == 2 && service.Connected, "Closed pipe was not reported or signed the user out.");
                service.SelectedChannelAsync(CancellationToken.None).Wait();
                Assert(service.Linked && fake.Connections == 2, "The widget could not reconnect after Discord came back.");
            }
        });
        check("Soundboard widget shows setup until connected and plays from a page of sounds", () => {
            var context = SynchronizationContext.Current; SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
            try
            {
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                using (var fake = new FakeDiscord { Answer = Standard })
                {
                    var owner = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    var card = (FrameworkElement)owner.FindName("SoundboardCard"); ((Panel)card.Parent).Children.Remove(card);
                    var host = new Grid { Width = 472, Height = 250 }; host.Children.Add(card);
                    Action layout = () => { host.Measure(new Size(472, 250)); host.Arrange(new Rect(0, 0, 472, 250)); host.UpdateLayout(); };
                    string blank = Path.Combine(directory, "discord-none-" + Guid.NewGuid().ToString("N") + ".dat");
                    using (var offline = new SoundboardWidget(owner, new DiscordService(blank, new TokenServer(), (id, token) => { throw new Exception("No pipe expected."); })))
                    {
                        offline.Tick(true, false); Pump(); layout();
                        Assert(((FrameworkElement)owner.FindName("SoundboardEmpty")).Visibility == Visibility.Visible && ((FrameworkElement)owner.FindName("SoundboardGrid")).Visibility == Visibility.Collapsed, "Disconnected soundboard did not show its setup state.");
                        var setup = (FrameworkElement)owner.FindName("SoundboardSetup");
                        Assert(setup.TransformToAncestor(card).Transform(new Point(0, setup.ActualHeight)).Y <= card.ActualHeight - 16, "Setup button escapes the card.");
                    }
                    using (var widget = new SoundboardWidget(owner, Service(Session(directory, "widget", "test-access", DateTime.UtcNow.AddDays(1)), fake, null)))
                    {
                        widget.Tick(false, false); Pump(); Assert(fake.Connections == 0, "Hidden soundboard connected to Discord.");
                        widget.Tick(true, false); layout();
                        var grid = (UniformGrid)owner.FindName("SoundboardGrid");
                        Wait(() => grid.Children.Count == 3 && grid.Children.Cast<Button>().All(b => b.IsEnabled), "Sounds did not load into enabled buttons.");
                        Assert(((TextBlock)owner.FindName("SoundboardChannel")).Text == "General", "Voice channel name is missing.");
                        layout();
                        foreach (Button button in grid.Children) Assert(button.ActualHeight >= 48 && button.ActualWidth >= 100, "Sound buttons are below touch size.");
                        int sounds = fake.Count("GET_SOUNDBOARD_SOUNDS"); widget.Tick(true, false); Pump();
                        Assert(fake.Count("GET_SOUNDBOARD_SOUNDS") == sounds, "Sounds reloaded on every tick.");
                        ((Button)grid.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Wait(() => fake.Count("PLAY_SOUNDBOARD_SOUND") == 1, "Tapping a sound did not play it.");
                        Wait(() => ((TextBlock)owner.FindName("SoundboardStatus")).Text == "Played Airhorn.", "Played sound was not confirmed.");
                        fake.Answer = (cmd, args) => cmd == "GET_SELECTED_VOICE_CHANNEL" ? null : Standard(cmd, args);
                        fake.Dispatch("VOICE_CHANNEL_SELECT", new Dictionary<string, object>());
                        Wait(() => grid.Children.Cast<Button>().All(b => !b.IsEnabled), "Leaving voice left the sounds enabled.");
                        Assert(((TextBlock)owner.FindName("SoundboardStatus")).Text.Contains("Join a voice channel"), "Leaving voice was not explained.");
                    }
                    owner.Close();
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        });
        check("Soundboard pages six sounds at any size and the widget starts hidden", () => {
            int columns, rows;
            Assert(SoundboardWidget.Capacity(432, 154, 20, out columns, out rows) == 6 && columns == 3 && rows == 2, "Compact page size is wrong.");
            Assert(SoundboardWidget.Capacity(1400, 500, 20, out columns, out rows) == 6 && SoundboardWidget.Capacity(0, 0, 20, out columns, out rows) == 6, "A page should always hold six sounds.");
            Assert(SoundboardWidget.Capacity(432, 154, 3, out columns, out rows) == 3, "Three sounds should fill the grid.");
            var settings = new AppSettings();
            Assert(WidgetLayout.Ids.Contains("Soundboard") && !WidgetLayout.Default(settings).First(p => p.Id == "Soundboard").Visible && !WidgetLayout.SpotifyDefault(settings).First(p => p.Id == "Soundboard").Visible, "Soundboard is visible by default.");
            var old = WidgetLayout.Default(settings).Where(p => p.Id != "Soundboard").ToArray();
            var migrated = WidgetLayout.Normalize(old, settings);
            Assert(migrated.Any(p => p.Id == "Soundboard" && !p.Visible), "Layouts saved before the soundboard did not get a hidden placement.");
        });
    }
}
