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
        check("Web pages and foreign host names are refused", () => {
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: 192.168.1.20:43830")) == null, "The phone's own request was refused.");
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: [fe80::1]:43830")) == null && RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: localhost")) == null && RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1")) == null, "An address host or no host was refused.");
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: 192.168.1.20:43830\r\nOrigin: http://evil.example")) != null, "A browser Origin was accepted.");
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: 192.168.1.20\r\nSec-Fetch-Mode: no-cors")) != null, "A browser fetch was accepted.");
            Assert(RemoteServer.RefuseBrowser(RemoteServer.ParseHead("GET /hello HTTP/1.1\r\nHost: evil.example:43830")) != null, "A rebound host name was accepted.");
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
            // Last seen follows each request and is saved now and then; one phone can be forgotten on its own.
            Assert(restored.Devices[0].LastSeenUtc == now, "Last seen not recorded.");
            now = now.AddMinutes(10);
            string second = restored.Pair(restored.StartPairing(), "Tablet");
            Assert(restored.Authorize(second, out name) && name == "Tablet" && restored.Devices.Count == 2, "Second phone didn't pair.");
            Assert(new RemotePairing(path, () => now).Devices.First(d => d.Name == "Tablet").LastSeenUtc == now, "Last seen wasn't saved.");
            Assert(restored.Forget(restored.Devices.First(d => d.Name == "Pixel 9").TokenHash) && !restored.Forget("missing"), "Forget didn't report what it did.");
            var afterForget = new RemotePairing(path, () => now);
            Assert(!afterForget.Authorize(token, out name) && afterForget.Authorize(second, out name) && afterForget.Devices.Count == 1, "Forgetting one phone didn't stick, or took the other with it.");
            restored.ForgetAll();
            Assert(!new RemotePairing(path, () => now).Authorize(second, out name), "Forgotten phone still authorized.");
        });
        check("Phone link pairs, answers with state and commands, and refuses strangers", () => {
            string path = Path.Combine(directory, "remote-settings.xml");
            string store = RemotePairing.StorePath(path); if (File.Exists(store)) File.Delete(store);
            if (File.Exists(NotesStore.PathFor(path))) File.Delete(NotesStore.PathFor(path));
            SettingsStore.Save(path, new AppSettings { Macros = new[] { new Macro { Label = "Quiet twice", Steps = new[] { new MacroStep { Kind = "Control", Target = "quiet" }, new MacroStep { Kind = "Delay", DelayMs = 100 }, new MacroStep { Kind = "Control", Target = "quiet" } } }, new Macro { Label = "Asks", Confirm = true, Steps = new[] { new MacroStep { Kind = "Delay", DelayMs = 100 } } } }, PrayerLocation = "Riyadh", PrayerLatitude = 24.7136, PrayerLongitude = 46.6753, PrayerMethod = "Makkah", RemoteEnabled = true, HideToTrayWithPhone = true, WorldClocks = new[] { new WorldClock { Zone = "Tokyo Standard Time", Label = "Tokyo" }, new WorldClock { Zone = "UTC", Label = "" } } });
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
                    var fromPage = new HttpRequestMessage(HttpMethod.Get, root + "/hello"); fromPage.Headers.Add("Origin", "http://evil.example");
                    Assert(send(fromPage).Item1 == 403, "A web page's request was answered.");
                    var rebound = new HttpRequestMessage(HttpMethod.Get, root + "/hello"); rebound.Headers.Host = "evil.example";
                    Assert(send(rebound).Item1 == 403, "A rebound host name was answered.");
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
                    // The Claude section comes from the labelled fixture, never from this PC's Claude Code logs.
                    controller.UseClaudeSample(true);
                    File.WriteAllText(Path.Combine(directory, "remote-state-sample.json"), get("/state?parts=focus,media,audio,system,deck,weather,spotify,discord,storage,clocks,prayer,notes,claude", token).Item2);
                    var claudeState = (Dictionary<string, object>)Json(get("/state?parts=claude", token).Item2)["claude"];
                    var claudeSessions = (object[])claudeState["sessions"]; var firstSession = (Dictionary<string, object>)claudeSessions[0];
                    var claudeLimits = (Dictionary<string, object>)claudeState["limits"]; var claudeFive = (Dictionary<string, object>)claudeLimits["fiveHour"];
                    Assert((bool)claudeState["connected"] && (bool)claudeState["sample"] && claudeSessions.Length == 4 && (string)firstSession["state"] == "needs" && (string)firstSession["project"] == "api-server"
                        && Convert.ToInt64(((Dictionary<string, object>)firstSession["tokens"])["fiveHour"]) == 412000 && Convert.ToDouble(claudeFive["pct"]) == 84 && Convert.ToDouble(claudeFive["resetsAt"]) > 1e9
                        && ((object[])((Dictionary<string, object>)claudeState["usage"])["week"]).Length == 5 && !claudeState.ContainsKey("token") && !state.Item2.Contains("accessToken"), "Claude state wrong.");
                    Assert(post("/command", "{\"action\":\"claude.focus\",\"id\":\"sample-2\"}", token).Item1 == 200 && post("/command", "{\"action\":\"claude.focus\",\"id\":\"gone\"}", token).Item1 == 409, "Focusing a Claude session from the phone failed.");
                    Assert(liveState.ContainsKey("media") && liveState.ContainsKey("audio") && liveState.ContainsKey("system"), "Live parts missing.");
                    var storageState = (Dictionary<string, object>)Json(get("/state?parts=storage", token).Item2)["storage"];
                    Assert(Convert.ToInt32(storageState["lowPercent"]) == 10 && ((object[])storageState["drives"]).Length > 0 && ((Dictionary<string, object>)((object[])storageState["drives"])[0]).ContainsKey("free"), "Storage state wrong.");
                    var clockState = (Dictionary<string, object>)Json(get("/state?parts=clocks", token).Item2)["clocks"];
                    var clockList = (object[])clockState["clocks"];
                    Assert(clockList.Length == 2 && (string)((Dictionary<string, object>)clockList[0])["label"] == "Tokyo" && Convert.ToInt32(((Dictionary<string, object>)clockList[0])["offset"]) == 540 && Convert.ToInt32(((Dictionary<string, object>)clockList[1])["offset"]) == 0 && clockState.ContainsKey("pcOffset"), "Clocks state wrong.");
                    var prayerState = (Dictionary<string, object>)Json(get("/state?parts=prayer", token).Item2)["prayer"];
                    Assert((bool)prayerState["located"] && (string)prayerState["city"] == "Riyadh" && ((object[])prayerState["times"]).Length == 5 && !(bool)prayerState["alerting"] && Convert.ToDouble(prayerState["nextAt"]) > 0, "Prayer state wrong.");
                    Assert(post("/command", "{\"action\":\"prayer.stop\"}", token).Item1 == 200, "Stopping the prayer alert from the phone failed.");
                    Assert(post("/command", "{\"action\":\"notes.text\",\"text\":\"From the phone\"}", token).Item1 == 200 && post("/command", "{\"action\":\"notes.add\",\"text\":\"Milk\"}", token).Item1 == 200 && post("/command", "{\"action\":\"notes.add\",\"text\":\"Bread\"}", token).Item1 == 200, "Phone notes changes failed.");
                    var notesState = (Dictionary<string, object>)Json(get("/state?parts=notes", token).Item2)["notes"];
                    var noteItems = (object[])notesState["items"]; string milk = (string)((Dictionary<string, object>)noteItems[0])["id"];
                    Assert((string)notesState["text"] == "From the phone" && noteItems.Length == 2 && Convert.ToInt32(notesState["version"]) == 3, "Notes state wrong.");
                    Assert(post("/command", "{\"action\":\"notes.toggle\",\"id\":\"" + milk + "\"}", token).Item1 == 200 && controller.Notes.Data.Items[0].Done, "Ticking from the phone failed.");
                    Assert(post("/command", "{\"action\":\"notes.toggle\",\"id\":\"nope\"}", token).Item1 == 409 && post("/command", "{\"action\":\"notes.add\",\"text\":\"  \"}", token).Item1 == 400, "Bad notes requests weren't refused.");
                    Assert(post("/command", "{\"action\":\"notes.delete\",\"id\":\"" + milk + "\"}", token).Item1 == 200 && controller.Notes.Data.Items.Count == 1, "Deleting from the phone failed.");
                    string ignoredMacro;
                    var macroState = (Dictionary<string, object>)Json(get("/state?parts=macros", token).Item2)["macros"];
                    var macroItems = (object[])macroState["items"];
                    Assert(macroItems.Length == 2 && (string)((Dictionary<string, object>)macroItems[0])["label"] == "Quiet twice" && ((object[])((Dictionary<string, object>)macroItems[0])["steps"]).Length == 3 && (bool)((Dictionary<string, object>)macroItems[1])["confirm"] && Convert.ToInt32(macroState["running"]) == -1, "Macros state wrong.");
                    bool quietBefore = SettingsStore.Load(path, out ignoredMacro).QuietMode;
                    Assert(post("/command", "{\"action\":\"macro.run\",\"index\":0,\"label\":\"Quiet twice\"}", token).Item1 == 200 && SettingsStore.Load(path, out ignoredMacro).QuietMode == quietBefore, "Running a macro from the phone failed.");
                    Assert(post("/command", "{\"action\":\"macro.run\",\"index\":1,\"label\":\"Asks\"}", token).Item1 == 409 && post("/command", "{\"action\":\"macro.run\",\"index\":1,\"label\":\"Asks\",\"confirmed\":true}", token).Item1 == 200, "A macro that asks first ran unconfirmed, or not at all.");
                    Assert(post("/command", "{\"action\":\"macro.run\",\"index\":0,\"label\":\"Renamed\"}", token).Item1 == 409, "A changed macro ran.");
                    var calendarState = (Dictionary<string, object>)Json(get("/state?parts=calendar", token).Item2)["calendar"];
                    Assert(post("/command", "{\"action\":\"calendar.refresh\"}", token).Item1 == 200 && Convert.ToInt32(calendarState["feeds"]) == 0 && !(bool)calendarState["read"] && ((object[])calendarState["events"]).Length == 0 && !get("/state?parts=calendar", token).Item2.Contains("ics"), "Calendar state wrong.");
                    controller.Clipboard.Offer("TEST FIXTURE: copied text");
                    var clipState = (Dictionary<string, object>)Json(get("/state?parts=clipboard", token).Item2)["clipboard"];
                    Assert(((object[])clipState["items"]).Length == 1 && (string)((Dictionary<string, object>)((object[])clipState["items"])[0])["text"] == "TEST FIXTURE: copied text" && !(bool)clipState["paused"], "Clipboard state wrong.");
                    Assert(post("/command", "{\"action\":\"clipboard.pause\",\"paused\":true}", token).Item1 == 200 && controller.Clipboard.Paused && post("/command", "{\"action\":\"clipboard.pause\",\"paused\":false}", token).Item1 == 200 && !controller.Clipboard.Paused, "Pausing from the phone failed.");
                    Assert(post("/command", "{\"action\":\"clipboard.paste\",\"id\":\"missing\"}", token).Item1 == 409, "Pasting a missing text wasn't refused.");
                    File.WriteAllText(Path.Combine(directory, "remote-state-sample.json"), get("/state?parts=focus,media,audio,system,deck,weather,spotify,discord,storage,clocks,prayer,notes,claude,clipboard,macros,calendar", token).Item2);
                    Assert(post("/command", "{\"action\":\"clipboard.clear\"}", token).Item1 == 200 && controller.Clipboard.History.Items.Count == 0, "Clearing from the phone failed.");
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
                    string ignored;
                    var focusState = (Dictionary<string, object>)Json(get("/state?parts=focus", token).Item2)["focus"];
                    Assert(!(bool)focusState["finished"] && !(bool)focusState["paused"] && Convert.ToInt32(focusState["customMinutes"]) == 0 && Convert.ToInt32(focusState["sessionsToday"]) == 0 && focusState.ContainsKey("chime"), "Focus state is missing the finish, preset or session fields.");
                    Assert(post("/command", "{\"action\":\"focus.custom\",\"minutes\":45}", token).Item1 == 200, "Custom focus length failed.");
                    focusState = (Dictionary<string, object>)Json(get("/state?parts=focus", token).Item2)["focus"];
                    Assert(Convert.ToDouble(focusState["durationMs"]) == 2700000 && (bool)focusState["running"] && Convert.ToInt32(focusState["customMinutes"]) == 45 && SettingsStore.Load(path, out ignored).FocusCustomMinutes == 45, "The custom length wasn't kept or started.");
                    Assert(post("/command", "{\"action\":\"focus.dismiss\"}", token).Item1 == 200 && post("/command", "{\"action\":\"focus.custom\",\"minutes\":1000}", token).Item1 == 200 && Convert.ToDouble(((Dictionary<string, object>)Json(get("/state?parts=focus", token).Item2)["focus"])["durationMs"]) == 10800000, "Dismiss failed, or the custom length wasn't clamped to 180.");
                    Assert(post("/command", "{\"action\":\"focus.preset\",\"minutes\":25}", token).Item1 == 200, "Focus preset failed.");
                    Assert(post("/command", "{\"action\":\"deck.run\",\"index\":99}", token).Item1 == 409, "Missing deck button accepted.");

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
                    File.WriteAllText(Path.Combine(directory, "remote-state-sample.json"), get("/state?parts=focus,media,audio,system,deck,weather,spotify,discord,storage,clocks,prayer,notes,claude,clipboard,macros,calendar,layout", token).Item2);
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
