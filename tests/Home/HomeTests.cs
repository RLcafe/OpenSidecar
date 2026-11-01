using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WidgetScreen;

// Smart home (docs/design/smart-home.md): reading Home Assistant's states, the home-network rule, the encrypted token,
// the service calls, the card's designs and Settings → Home, against a fake Home Assistant on loopback. Every device
// here is a labelled test fixture.
internal static class HomeTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }
    private const string Token = "fixture-token-not-real-0123456789";
    public const string StatesFixture = "[" +
        "{\"entity_id\":\"light.desk\",\"state\":\"on\",\"attributes\":{\"friendly_name\":\"TEST FIXTURE desk lamp\",\"brightness\":153,\"supported_color_modes\":[\"brightness\"]}}," +
        "{\"entity_id\":\"light.hall\",\"state\":\"off\",\"attributes\":{\"friendly_name\":\"Fixture hall\",\"supported_color_modes\":[\"onoff\"]}}," +
        "{\"entity_id\":\"switch.plug\",\"state\":\"on\",\"attributes\":{\"friendly_name\":\"Fixture plug\"}}," +
        "{\"entity_id\":\"climate.living\",\"state\":\"heat\",\"attributes\":{\"friendly_name\":\"Fixture thermostat\",\"current_temperature\":21.5,\"temperature\":23,\"min_temp\":10,\"max_temp\":30,\"target_temp_step\":0.5}}," +
        "{\"entity_id\":\"sensor.outside\",\"state\":\"12\",\"attributes\":{\"friendly_name\":\"Fixture sensor\"}}," +
        "{\"entity_id\":\"switch.gone\",\"state\":\"unavailable\",\"attributes\":{\"friendly_name\":\"Fixture gone\"}}]";

    private static void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame); Thread.Sleep(10); }
    }
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (var e = element; e != null && e != root; e = System.Windows.Media.VisualTreeHelper.GetParent(e) ?? LogicalTreeHelper.GetParent(e))
            if (e is UIElement && ((UIElement)e).Visibility != Visibility.Visible) return false;
        return true;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }
    private static void Click(Button button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }

    // A fake Home Assistant: checks the Bearer token, answers config and states, and records each service call.
    private sealed class FakeHome : IDisposable
    {
        private readonly HttpListener listener;
        public readonly int Port;
        public readonly List<string> Calls = new List<string>();
        public FakeHome()
        {
            for (Port = 43920; ; Port++) { try { listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:" + Port + "/"); listener.Start(); break; } catch (HttpListenerException) { if (Port > 43999) throw; } }
            new Thread(() => {
                while (listener.IsListening)
                {
                    HttpListenerContext context; try { context = listener.GetContext(); } catch (Exception) { return; }
                    string body = new StreamReader(context.Request.InputStream).ReadToEnd(), path = context.Request.Url.AbsolutePath, reply = "[]";
                    if (context.Request.Headers["Authorization"] != "Bearer " + Token) { context.Response.StatusCode = 401; context.Response.Close(); continue; }
                    if (path == "/api/config") reply = "{\"location_name\":\"Fixture Home\"}";
                    else if (path == "/api/states") reply = StatesFixture;
                    else if (path.StartsWith("/api/services/") && context.Request.HttpMethod == "POST") lock (Calls) Calls.Add(path.Substring(14) + " " + body);
                    else context.Response.StatusCode = 404;
                    var bytes = Encoding.UTF8.GetBytes(reply); context.Response.ContentType = "application/json"; context.Response.OutputStream.Write(bytes, 0, bytes.Length); context.Response.Close();
                }
            }) { IsBackground = true }.Start();
        }
        public string Url { get { return "127.0.0.1:" + Port; } }
        public void Dispose() { listener.Stop(); }
    }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("Smart home reads lights, switches and thermostats from Home Assistant's states", () => {
            var all = HomeAssistantService.Parse(StatesFixture);
            Assert(all.Count == 5 && all.All(e => e.Domain != "sensor") && all.Select(e => e.Domain).SequenceEqual(new[] { "light", "light", "switch", "switch", "climate" }), "The wrong devices were kept: " + String.Join(", ", all.Select(e => e.Id)));
            var desk = all.Single(e => e.Id == "light.desk"); var hall = all.Single(e => e.Id == "light.hall"); var heat = all.Single(e => e.Id == "climate.living"); var gone = all.Single(e => e.Id == "switch.gone");
            Assert(desk.On && desk.Dimmable && desk.Brightness == 60 && !hall.On && !hall.Dimmable && hall.Brightness == null, "Lights are read wrong.");
            Assert(heat.On && heat.Current == 21.5 && heat.Target == 23 && heat.Min == 10 && heat.Max == 30 && heat.Step == 0.5, "The thermostat is read wrong.");
            Assert(!gone.Available && !gone.On && HomeAssistantService.Describe(gone) == "Unavailable" && HomeAssistantService.Describe(desk) == "On  ·  60%" && HomeAssistantService.Describe(hall) == "Off" && HomeAssistantService.Describe(heat) == "21.5°  →  23°", "Descriptions are wrong: " + HomeAssistantService.Describe(heat));
            Assert(HomeAssistantService.Parse("not json").Count == 0 && HomeAssistantService.Parse("{}").Count == 0, "Broken answers aren't ignored.");
        });
        check("Smart home only talks to Home Assistant on the home network", () => {
            Assert(HomeAssistantService.Normalize("homeassistant.local:8123/lovelace/0").ToString() == "http://homeassistant.local:8123/" && HomeAssistantService.Normalize("https://192.168.1.20:8123").ToString() == "https://192.168.1.20:8123/" && HomeAssistantService.Normalize("ftp://x") == null && HomeAssistantService.Normalize(" ") == null, "Addresses aren't normalized.");
            Assert(HomeAssistantService.OnHomeNetwork(new Uri("http://192.168.1.20:8123/")).Result && HomeAssistantService.OnHomeNetwork(new Uri("http://10.0.0.5/")).Result && HomeAssistantService.OnHomeNetwork(new Uri("http://127.0.0.1:8123/")).Result, "A home address is refused.");
            Assert(!HomeAssistantService.OnHomeNetwork(new Uri("http://8.8.8.8:8123/")).Result && !HomeAssistantService.OnHomeNetwork(new Uri("http://203.0.113.9/")).Result, "An internet address is allowed.");
            string path = Path.Combine(directory, "home-lan", "home-assistant.dat"); if (File.Exists(path)) File.Delete(path);
            using (var service = new HomeAssistantService(path))
            {
                try { service.ConnectAsync("http://8.8.8.8:8123", Token, CancellationToken.None).Wait(); throw new Exception("An internet address was connected."); }
                catch (AggregateException e) { Assert(e.InnerException is InvalidOperationException && e.InnerException.Message.Contains("home network"), "The refusal doesn't say why: " + e.InnerException.Message); }
                Assert(!service.Connected && !File.Exists(path), "A refused address was kept.");
            }
        });
        check("Smart home connects with the token (kept encrypted), refuses a bad one, and calls toggle, brightness and temperature", () => {
            string path = Path.Combine(directory, "home-service", "home-assistant.dat"); if (File.Exists(path)) File.Delete(path);
            using (var fake = new FakeHome())
            {
                using (var service = new HomeAssistantService(path))
                {
                    try { service.ConnectAsync(fake.Url, "wrong-token", CancellationToken.None).Wait(); throw new Exception("A bad token connected."); }
                    catch (AggregateException e) { Assert(e.InnerException.Message.Contains("refused the token"), "A refused token isn't explained: " + e.InnerException.Message); }
                    Assert(!File.Exists(path), "A refused token was kept.");
                    Assert(service.ConnectAsync(fake.Url, Token, CancellationToken.None).Result == "Fixture Home" && service.Connected, "Connecting failed.");
                    var bytes = File.ReadAllBytes(path);
                    Assert(!Encoding.UTF8.GetString(bytes).Contains(Token) && !Encoding.Unicode.GetString(bytes).Contains(Token), "The token is stored readable.");
                }
                using (var service = new HomeAssistantService(path))
                {
                    Assert(service.Connected && service.Address.Port == fake.Port, "The saved connection isn't read back.");
                    var all = service.StatesAsync(CancellationToken.None).Result;
                    var desk = all.Single(e => e.Id == "light.desk"); var heat = all.Single(e => e.Id == "climate.living"); var plug = all.Single(e => e.Id == "switch.plug");
                    service.ToggleAsync(plug, CancellationToken.None).Wait();
                    service.ToggleAsync(heat, CancellationToken.None).Wait();
                    service.BrightnessAsync(desk.Id, 40, CancellationToken.None).Wait();
                    service.BrightnessAsync(desk.Id, 0, CancellationToken.None).Wait();
                    service.TemperatureAsync(heat, 23.3, CancellationToken.None).Wait();
                    service.TemperatureAsync(heat, 50, CancellationToken.None).Wait();
                    var calls = fake.Calls.Select(c => c.Replace(" ", "")).ToList();
                    Assert(calls.SequenceEqual(new[] { "switch/toggle{\"entity_id\":\"switch.plug\"}", "climate/turn_off{\"entity_id\":\"climate.living\"}", "light/turn_on{\"entity_id\":\"light.desk\",\"brightness_pct\":40}",
                        "light/turn_off{\"entity_id\":\"light.desk\"}", "climate/set_temperature{\"entity_id\":\"climate.living\",\"temperature\":23.5}", "climate/set_temperature{\"entity_id\":\"climate.living\",\"temperature\":30}" }), "The service calls are wrong: " + String.Join(" | ", calls));
                    foreach (var bad in new[] { "sensor.outside", "light.desk/../../api", "" })
                        try { service.BrightnessAsync(bad, 50, CancellationToken.None).Wait(); throw new Exception("A bad entity was sent: " + bad); } catch (ArgumentException) { } catch (AggregateException) { }
                    Assert(fake.Calls.Count == 6, "A bad entity reached Home Assistant.");
                    service.Disconnect();
                    Assert(!service.Connected && !File.Exists(path), "Disconnect kept the token.");
                }
            }
        });
        check("Smart home designs keep devices inside the card with 48-unit controls at 140% text", () => {
            var fixture = HomeAssistantService.Parse(StatesFixture);
            Assert(CardSizing.Home(240, 152) == CardSizing.Design.Small && CardSizing.Home(472, 310) == CardSizing.Design.Medium && CardSizing.Home(600, 400) == CardSizing.Design.Large, "Home designs are picked at the wrong sizes.");
            Assert(CardSizing.HomeGrid(424, 226, 1.4, 5).SequenceEqual(new[] { 2, 2, 1 }) && CardSizing.HomeGrid(552, 316, 1.4, 5).SequenceEqual(new[] { 3, 2, 0 }), "The tile grid differs from the phone's (AndroidTests).");
            string folder = Path.Combine(directory, "home-card"); Directory.CreateDirectory(folder); string path = Path.Combine(folder, "settings.xml");
            foreach (var size in new[] { new Size(240, 152), new Size(340, 220), new Size(472, 250), new Size(472, 310), new Size(600, 400), new Size(900, 576) })
            {
                var settings = new AppSettings { TextScale = 1.4 };
                settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { p.Visible = p.Id == "Home"; if (p.Visible) { p.X = 0; p.Y = 0; p.Width = size.Width; p.Height = size.Height; } return p; }).ToArray();
                SettingsStore.Save(path, settings);
                using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                {
                    var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                    try
                    {
                        using (var controller = new DashboardController(window, path, true))
                        {
                            controller.HomeView.Show(true, true, fixture, null);
                            var dashboard = (FrameworkElement)window.FindName("Dashboard");
                            dashboard.Measure(new Size(1920, 720)); dashboard.Arrange(new Rect(0, 0, 1920, 720)); dashboard.UpdateLayout();
                            var card = (FrameworkElement)window.FindName("HomeCard");
                            Assert(controller.HomeView.Current == CardSizing.Home(size.Width, size.Height) && window.FindName("HomeToggle0") != null, "The devices aren't shown at " + size + ".");
                            Assert(window.FindName("HomeToggle4") == null || (CardSizing.Home(size.Width, size.Height) != CardSizing.Design.Small) == (window.FindName("HomeUp4") != null), "The thermostat's + is missing, or shown on a Small card, at " + size + ".");
                            // Small scrolls its rows: the list itself has to fit, and its rows are checked for height only.
                            var scroll = Descendants(card).OfType<ScrollViewer>().FirstOrDefault();
                            Assert((scroll != null) == (CardSizing.Home(size.Width, size.Height) == CardSizing.Design.Small), "Only Small should scroll, at " + size + ".");
                            foreach (var part in Descendants(card).OfType<FrameworkElement>().Where(e => (e is Button || e is TextBlock || e is Slider || e is ScrollViewer) && Shown(e, card) && e.ActualWidth > 0 && (scroll == null || !scroll.IsAncestorOf(e))))
                            {
                                var box = part.TransformToAncestor(card).TransformBounds(new Rect(0, 0, part.ActualWidth, part.ActualHeight));
                                Assert(box.Left >= 19.5 && box.Top >= 19.5 && box.Right <= card.ActualWidth - 19.5 && box.Bottom <= card.ActualHeight - 19.5, "\"" + (part is TextBlock ? ((TextBlock)part).Text : part.Name) + "\" is outside the " + size + " card: " + box);
                                Assert(!(part is Button) || part.ActualHeight >= 47.5, "A smart home button is shorter than 48 at " + size + ": " + part.ActualHeight);
                            }
                            if (scroll != null) Assert(Descendants(scroll).OfType<Button>().All(b => b.ActualHeight >= 47.5), "A smart home row is shorter than 48 at " + size + ".");
                            if (CardSizing.Home(size.Width, size.Height) == CardSizing.Design.Large) Assert(window.FindName("HomeDimmer1") != null && window.FindName("HomeDimmer0") == null, "Large doesn't give the dimmable light (and only it) a dimmer.");
                            // A picture of each design for review (the fixture devices, not real ones).
                            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(card.ActualWidth), (int)Math.Ceiling(card.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                            bitmap.Render(card);
                            var png = new System.Windows.Media.Imaging.PngBitmapEncoder(); png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                            using (var file = File.Create(Path.Combine(directory, "home-" + size.Width + "x" + size.Height + ".png"))) png.Save(file);
                        }
                    }
                    finally { window.Close(); }
                }
            }
        });
        check("Settings → Home connects, lists devices to choose, and the card toggles them through Home Assistant", () => {
            string folder = Path.Combine(directory, "home-settings"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "settings.xml"), store = HomeAssistantService.PathFor(path);
            if (File.Exists(store)) File.Delete(store);
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                using (var fake = new FakeHome())
                {
                    var settings = new AppSettings();
                    settings.WidgetLayout = WidgetLayout.SpotifyDefault(settings).Select(p => { if (p.Id == "Home") p.Visible = true; return p; }).ToArray();
                    SettingsStore.Save(path, settings);
                    using (var stream = typeof(AppSettings).Assembly.GetManifestResourceStream("WidgetScreen.MainWindow.xaml"))
                    {
                        var window = (Window)System.Windows.Markup.XamlReader.Load(stream);
                        try
                        {
                            using (var controller = new DashboardController(window, path, true))
                            {
                                Assert(((TextBlock)window.FindName("HomeView")).Text.StartsWith("Connect Home Assistant"), "The card doesn't say to connect.");
                                Click((Button)window.FindName("SettingsButton"));
                                ((TextBox)window.FindName("HomeAddress")).Text = fake.Url; ((PasswordBox)window.FindName("HomeToken")).Password = Token;
                                Click((Button)window.FindName("HomeConnect")); Pump(600);
                                Assert(((TextBlock)window.FindName("HomeConnectionStatus")).Text.StartsWith("Connected to Fixture Home") && ((PasswordBox)window.FindName("HomeToken")).Password == "" && File.Exists(store), "Connecting from Settings failed: " + ((TextBlock)window.FindName("HomeConnectionStatus")).Text);
                                var boxes = ((StackPanel)window.FindName("HomeChoices")).Children.OfType<CheckBox>().ToList();
                                Assert(boxes.Count == 5, "Settings doesn't list the devices.");
                                foreach (var box in boxes.Where(b => (string)b.ToolTip == "switch.plug" || (string)b.ToolTip == "light.desk")) { box.IsChecked = true; box.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); }
                                Click((Button)window.FindName("SaveSettings")); Pump(100);
                                string warning; var saved = SettingsStore.Load(path, out warning);
                                Assert(saved.HomeEntities.SequenceEqual(new[] { "light.desk", "switch.plug" }) && !File.ReadAllText(path).Contains(Token) && !File.ReadAllText(path).Contains(fake.Port.ToString() + "/"), "The chosen devices weren't saved, or the connection leaked into settings.xml.");
                                var toggle = (Button)window.FindName("HomeToggle1");
                                Assert(toggle != null && System.Windows.Automation.AutomationProperties.GetName(toggle).StartsWith("Fixture plug, On"), "The card doesn't show the chosen devices.");
                                Click(toggle); Pump(500);
                                Assert(fake.Calls.Count == 1 && fake.Calls[0].StartsWith("switch/toggle"), "Tapping a device didn't toggle it: " + String.Join(" | ", fake.Calls));
                                Click((Button)window.FindName("SettingsButton")); Click((Button)window.FindName("HomeDisconnect"));
                                Assert(!File.Exists(store) && ((TextBlock)window.FindName("HomeView")).Text.StartsWith("Connect Home Assistant"), "Disconnect didn't forget the connection.");
                                Click((Button)window.FindName("CancelSettings"));
                            }
                        }
                        finally { window.Close(); }
                    }
                }
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        });
    }
}
