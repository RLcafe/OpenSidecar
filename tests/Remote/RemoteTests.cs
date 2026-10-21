using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Threading;
using WidgetScreen;

// The phone link. Every check listens on loopback with free ports and isolated settings; nothing here opens the
// real port 43830 or touches the user's paired phones.
internal static class RemoteTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }

    // Requests are answered on the UI thread, so the test thread keeps the dispatcher running while it waits.
    private static T Pump<T>(Task<T> task)
    {
        var frame = new DispatcherFrame();
        var limit = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        limit.Tick += (s, e) => { limit.Stop(); frame.Continue = false; };
        task.ContinueWith(t => frame.Continue = false, TaskScheduler.Default);
        limit.Start();
        Dispatcher.PushFrame(frame);
        limit.Stop();
        if (!task.IsCompleted) throw new TimeoutException("The phone link did not answer.");
        return task.Result;
    }

    private static Dictionary<string, object> Json(string text) { return (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text); }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Phone link parses requests and rejects malformed ones", () => {
            var request = RemoteServer.ParseHead("GET /state?parts=focus%2Cdeck&x=1 HTTP/1.1\r\nHost: pc\r\nAuthorization: Bearer abc123");
            Assert(request.Method == "GET" && request.Path == "/state" && request.QueryValue("parts") == "focus,deck" && request.Token == "abc123", "Request head misread.");
            foreach (string bad in new[] { "GET /state", "GET state HTTP/1.1", "GET /a FTP/1.0", "GET /a HTTP/1.1\r\nNoColon" })
            {
                bool rejected = false; try { RemoteServer.ParseHead(bad); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "Malformed request accepted: " + bad);
            }
        });
        check("Only phones on the local network may connect", () => {
            foreach (string ok in new[] { "192.168.1.20", "10.0.0.5", "172.20.1.1", "169.254.3.4", "127.0.0.1", "::1", "fe80::1", "fd00::5" }) Assert(DashboardController.IsLocal(IPAddress.Parse(ok)), ok + " refused.");
            foreach (string no in new[] { "8.8.8.8", "172.32.0.1", "100.64.0.1", "2001:4860::8888" }) Assert(!DashboardController.IsLocal(IPAddress.Parse(no)), no + " allowed.");
        });
        check("Pairing codes expire, lock after five wrong tries, and tokens survive a restart", () => {
            DateTime now = new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);
            string path = Path.Combine(directory, "remote-devices-test.dat"); if (File.Exists(path)) File.Delete(path);
            var pairing = new RemotePairing(path, () => now);
            Assert(Failure(() => pairing.Pair("123456", "Phone")).Contains("isn't open"), "Pairing open without a code.");
            string code = pairing.StartPairing();
            Assert(code.Length == 6 && code.Trim('0', '1', '2', '3', '4', '5', '6', '7', '8', '9').Length == 0, "Code isn't six digits.");
            now = now.AddMinutes(6);
            Assert(Failure(() => pairing.Pair(code, "Phone")).Contains("isn't open"), "Expired code accepted.");
            code = pairing.StartPairing();
            string wrong = code == "000000" ? "111111" : "000000";
            for (int i = 0; i < 4; i++) Assert(Failure(() => pairing.Pair(wrong, "Phone")).Contains("doesn't match"), "Wrong code accepted.");
            Assert(Failure(() => pairing.Pair(wrong, "Phone")).Contains("Too many"), "Guessing wasn't stopped.");
            Assert(pairing.Code == null && Failure(() => pairing.Pair(code, "Phone")).Contains("isn't open"), "Code stayed valid after five wrong tries.");
            code = pairing.StartPairing();
            string token = pairing.Pair(code, "Pixel 9");
            string name;
            Assert(token.Length >= 40 && pairing.Code == null && pairing.Authorize(token, out name) && name == "Pixel 9" && !pairing.Authorize(token + "x", out name), "Token doesn't authorize.");
            Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(token) && !Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(RemotePairing.Hash(token)), "Pairing store isn't encrypted.");
            var restored = new RemotePairing(path, () => now);
            Assert(restored.Authorize(token, out name) && restored.Devices.Count == 1, "Pairing lost on restart.");
            restored.ForgetAll();
            Assert(!new RemotePairing(path, () => now).Authorize(token, out name), "Forgotten phone still authorized.");
        });
        check("Phone link pairs, answers with state and commands, and refuses strangers", () => {
            string path = Path.Combine(directory, "remote-settings.xml");
            string store = RemotePairing.StorePath(path); if (File.Exists(store)) File.Delete(store);
            SettingsStore.Save(path, new AppSettings { RemoteEnabled = true });
            using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
            {
                var window = (System.Windows.Window)System.Windows.Markup.XamlReader.Load(stream);
                using (var controller = new DashboardController(window, path, true))
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    controller.RemoteAddress = IPAddress.Loopback; controller.RemotePort = 0; controller.RemoteDiscoveryPort = 0;
                    controller.Tray.ShowTips = false;
                    controller.ApplyRemote();
                    Assert(controller.Remote != null, "The phone link didn't start.");
                    string root = "http://127.0.0.1:" + controller.Remote.Port;
                    Func<HttpRequestMessage, Tuple<int, string>> send = message => Pump(Task.Run(async () => {
                        using (var response = await http.SendAsync(message)) return Tuple.Create((int)response.StatusCode, await response.Content.ReadAsStringAsync());
                    }));
                    Func<string, string, string, Tuple<int, string>> post = (where, body, bearer) => {
                        var message = new HttpRequestMessage(HttpMethod.Post, root + where) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                        if (bearer != null) message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                        return send(message);
                    };
                    Func<string, string, Tuple<int, string>> get = (where, bearer) => {
                        var message = new HttpRequestMessage(HttpMethod.Get, root + where);
                        if (bearer != null) message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                        return send(message);
                    };

                    Assert(get("/hello", null).Item1 == 200 && (bool)Json(get("/hello", null).Item2)["paired"] == false, "Hello failed.");
                    Assert(get("/state?parts=focus", null).Item1 == 401 && get("/state?parts=focus", "made-up").Item1 == 401, "Unpaired phone got state.");
                    Assert(post("/pair", "{\"code\":\"123456\",\"device\":\"Test\"}", null).Item1 == 403, "Pairing without an open code.");
                    string code = controller.Pairing.StartPairing();
                    var paired = post("/pair", "{\"code\":\"" + code + "\",\"device\":\"Test phone\"}", null);
                    Assert(paired.Item1 == 200, "Pairing failed: " + paired.Item2);
                    string token = (string)Json(paired.Item2)["token"];
                    Assert((bool)Json(get("/hello", token).Item2)["paired"], "Paired phone not recognised.");

                    var state = get("/state?parts=focus,deck,weather,spotify,discord", token);
                    Assert(state.Item1 == 200, "State failed: " + state.Item2);
                    var parsed = Json(state.Item2);
                    var focus = (Dictionary<string, object>)parsed["focus"];
                    Assert(Convert.ToDouble(focus["durationMs"]) == 1500000 && !(bool)focus["running"], "Focus state wrong.");
                    Assert(((object[])parsed["deck"]).Length == AppSettings.DefaultShortcuts().Count && parsed.ContainsKey("weather") && !(bool)((Dictionary<string, object>)parsed["spotify"])["connected"] && !(bool)((Dictionary<string, object>)parsed["discord"])["connected"], "Deck, weather, Spotify or Discord state wrong.");
                    Assert(!parsed.ContainsKey("media") && !parsed.ContainsKey("audio"), "Unrequested parts were read.");
                    // Live devices are only read here, never changed. "Unavailable" in a sandbox is fine; a crash isn't.
                    var live = get("/state?parts=media,audio,system", token);
                    Assert(live.Item1 == 200, "Live state failed: " + live.Item2);
                    var liveState = Json(live.Item2);
                    // The Android checks parse this real answer, so both apps agree on the format.
                    File.WriteAllText(Path.Combine(directory, "remote-state-sample.json"), get("/state?parts=focus,media,audio,system,deck,weather,spotify,discord", token).Item2);
                    Assert(liveState.ContainsKey("media") && liveState.ContainsKey("audio") && liveState.ContainsKey("system"), "Live parts missing.");
                    int art = get("/art", token).Item1;
                    Assert(art == 200 || art == 404, "Artwork request failed.");

                    // A phone reading the dashboard moves the window to the tray; it comes back a minute after the phone stops.
                    Assert(controller.Tray.Hidden && controller.Tray.Automatic && !window.ShowInTaskbar, "The phone didn't move the dashboard to the tray.");
                    controller.Tray.Show();
                    controller.UpdateTray();
                    Assert(!controller.Tray.Hidden && window.ShowInTaskbar, "The window hid again after it was brought back while the phone was connected.");
                    controller.PhoneSeenAt(DateTime.UtcNow - TimeSpan.FromMinutes(2)); controller.UpdateTray();
                    controller.PhoneSeen(); controller.UpdateTray();
                    Assert(controller.Tray.Hidden, "A reconnected phone didn't move the dashboard to the tray.");
                    controller.PhoneSeenAt(DateTime.UtcNow - TimeSpan.FromMinutes(2)); controller.UpdateTray();
                    Assert(!controller.Tray.Hidden && window.ShowInTaskbar, "The dashboard didn't come back after the phone left.");
                    controller.Tray.Hide(false); controller.UpdateTray();
                    Assert(controller.Tray.Hidden && !controller.Tray.Automatic, "Hide to tray didn't stay hidden.");
                    controller.Tray.Show();
                    Assert(post("/command", "{\"action\":\"focus.preset\",\"minutes\":50}", token).Item1 == 200, "Focus preset failed.");
                    Assert(Convert.ToDouble(((Dictionary<string, object>)Json(get("/state?parts=focus", token).Item2)["focus"])["durationMs"]) == 3000000, "Focus preset not applied on the PC.");
                    Assert(post("/command", "{\"action\":\"focus.toggle\"}", token).Item1 == 200 && (bool)((Dictionary<string, object>)Json(get("/state?parts=focus", token).Item2)["focus"])["running"], "Focus didn't start from the phone.");
                    Assert(post("/command", "{\"action\":\"deck.run\",\"index\":99}", token).Item1 == 409, "Missing deck button accepted.");

                    string ignored;
                    // Layout pages and mirroring: the phone reads the PC's pages and layouts, switches pages and saves its own shape.
                    var layoutState = Json(get("/state?parts=layout", token).Item2);
                    var layout = (Dictionary<string, object>)layoutState["layout"]; string version = (string)layoutState["layoutVersion"];
                    Assert(((object[])layout["pages"]).Length == 1 && Convert.ToInt32(layout["page"]) == 0 && ((object[])layout["shown"]).Length == WidgetLayout.Ids.Length, "Layout section wrong.");
                    Assert(!Json(get("/state?parts=focus", token).Item2).ContainsKey("layout") && (string)Json(get("/state?parts=focus", token).Item2)["layoutVersion"] == version, "Layout sent unasked, or its version moved without a change.");
                    Assert(post("/command", "{\"action\":\"page.add\",\"name\":\"Gaming\"}", token).Item1 == 200, "Adding a page from the phone failed.");
                    layoutState = Json(get("/state?parts=layout", token).Item2); layout = (Dictionary<string, object>)layoutState["layout"];
                    Assert((string)layoutState["layoutVersion"] != version && ((object[])layout["pages"]).Length == 2 && Convert.ToInt32(layout["page"]) == 1 && WidgetLayout.CurrentPage(SettingsStore.Load(path, out ignored)) == 1, "The new page isn't shown on the PC.");
                    Assert(post("/command", "{\"action\":\"page.show\",\"index\":0}", token).Item1 == 200 && WidgetLayout.CurrentPage(SettingsStore.Load(path, out ignored)) == 0, "The phone couldn't switch the PC's page.");
                    Assert(post("/command", "{\"action\":\"page.show\",\"index\":7}", token).Item1 == 409, "A missing page was accepted.");
                    string phoneLayout = "{\"action\":\"layout.save\",\"index\":1,\"page\":\"Gaming\",\"width\":1480,\"height\":552,\"widgets\":[{\"id\":\"Focus\",\"x\":0,\"y\":0,\"width\":300,\"height\":552,\"visible\":true,\"layer\":0},{\"id\":\"Mixer\",\"x\":316,\"y\":0,\"width\":400,\"height\":552,\"visible\":true,\"layer\":0},{\"id\":\"Spotify\",\"x\":800,\"y\":0,\"width\":500,\"height\":552,\"visible\":false,\"layer\":0}]}";
                    var savedLayout = post("/command", phoneLayout, token);
                    Assert(savedLayout.Item1 == 200, "Saving the phone's layout failed: " + savedLayout.Item2);
                    var afterSave = SettingsStore.Load(path, out ignored);
                    var gaming = WidgetLayout.Page(afterSave, 1);
                    Assert(gaming.CanvasLayouts != null && gaming.CanvasLayouts.Single().Size == new System.Windows.Size(1480, 552) && !WidgetLayout.Saved(gaming, new System.Windows.Size(1480, 552)).First(p => p.Id == "Spotify").Visible, "The phone's layout wasn't saved for its shape on that page.");
                    Assert(WidgetLayout.Page(afterSave, 0).CanvasLayouts == null && WidgetLayout.CurrentPage(afterSave) == 0, "Saving another page's layout changed the page shown or the first page.");
                    Assert(post("/command", phoneLayout.Replace("Gaming", "Renamed"), token).Item1 == 409, "A layout for a page that changed was accepted.");
                    controller.Editor.Begin();
                    Assert(post("/command", "{\"action\":\"page.show\",\"index\":1}", token).Item1 == 409, "Pages changed while the PC was editing.");
                    controller.Editor.Cancel();
                    Assert(post("/command", "{\"action\":\"page.rename\",\"index\":1,\"name\":\"  Play  \"}", token).Item1 == 200 && WidgetLayout.Page(SettingsStore.Load(path, out ignored), 1).Name == "Play", "Renaming from the phone failed.");
                    Assert(post("/command", "{\"action\":\"page.move\",\"index\":1,\"to\":0}", token).Item1 == 200 && WidgetLayout.Page(SettingsStore.Load(path, out ignored), 0).Name == "Play" && WidgetLayout.CurrentPage(SettingsStore.Load(path, out ignored)) == 1, "Moving a page didn't keep the page shown.");
                    File.WriteAllText(Path.Combine(directory, "remote-state-sample.json"), get("/state?parts=focus,media,audio,system,deck,weather,spotify,discord,layout", token).Item2);
                    Assert(post("/command", "{\"action\":\"page.delete\",\"index\":0}", token).Item1 == 200 && post("/command", "{\"action\":\"page.delete\",\"index\":0}", token).Item1 == 409 && WidgetLayout.PageCount(SettingsStore.Load(path, out ignored)) == 1, "Deleting pages from the phone failed, or the last page was deleted.");
                    Assert(post("/command", "{\"action\":\"launch-missiles\"}", token).Item1 == 400, "Unknown command accepted.");
                    Assert(post("/spotify", "{\"op\":\"current\"}", token).Item1 == 409, "Spotify proxy answered without a connection.");
                    // The PC answers 413 and closes without reading the rest, so the phone may see either the answer or a reset.
                    int oversized = 0;
                    try { oversized = post("/command", "{\"action\":\"x\",\"pad\":\"" + new string('a', 70000) + "\"}", token).Item1; }
                    catch (AggregateException) { oversized = 413; }
                    Assert(oversized == 413, "Oversized request accepted.");

                    using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
                    {
                        byte[] ask = Encoding.UTF8.GetBytes(RemoteServer.DiscoveryRequest);
                        udp.Send(ask, ask.Length, new IPEndPoint(IPAddress.Loopback, controller.Remote.DiscoveryPort));
                        var reply = udp.ReceiveAsync();
                        Assert(reply.Wait(5000), "Discovery didn't answer.");
                        var found = Json(Encoding.UTF8.GetString(reply.Result.Buffer));
                        Assert((string)found["app"] == "Widget Screen" && Convert.ToInt32(found["port"]) == controller.Remote.Port, "Discovery reply wrong.");
                    }
                }
                window.Close();
            }
            if (File.Exists(store)) File.Delete(store);
        });
    }

    private static string Failure(Action action) { try { action(); } catch (InvalidOperationException e) { return e.Message; } throw new Exception("Expected failure was silently accepted."); }
}
