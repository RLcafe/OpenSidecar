using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Smart home widget (docs/design/smart-home.md): the Home Assistant devices chosen in Settings → Home, read every
    // ten seconds while the card is shown (twenty in Quiet mode) and right after a change. A phone goes through the PC:
    // it never sees Home Assistant's address or token.
    public sealed partial class DashboardController
    {
        public static readonly TimeSpan HomeInterval = TimeSpan.FromSeconds(10), HomeQuietInterval = TimeSpan.FromSeconds(20);
        private HomeAssistantService home;
        private HomeCard homeCard;
        private List<HomeEntity> homeStates;
        private string homeProblem;
        private bool homeBusy;
        private DateTime lastHomeAttempt = DateTime.MinValue;
        // Settings → Home: every device Home Assistant offers, and the ones ticked.
        private List<HomeEntity> homeChoices;
        private HashSet<string> pendingHomeEntities = new HashSet<string>();
        // For the checks (tests/Home/HomeTests.cs).
        public HomeAssistantService Home { get { return home; } }
        public HomeCard HomeView { get { return homeCard; } }

        private void InitializeHome()
        {
            home = new HomeAssistantService(HomeAssistantService.PathFor(settingsPath));
            homeCard = new HomeCard(window, Control<Grid>("HomeBody"), Control<TextBlock>("HomeStatus"));
            homeCard.Toggled += e => HomeAction(e, token => home.ToggleAsync(e, token), e.Name + (e.On ? " off." : " on."));
            homeCard.Dimmed += (e, value) => HomeAction(e, token => home.BrightnessAsync(e.Id, value, token), e.Name + " at " + value + "%.");
            homeCard.Heated += (e, value) => HomeAction(e, token => home.TemperatureAsync(e, value, token), null);
            Button("HomeConnect", ConnectHome);
            Button("HomeDisconnect", () => {
                home.Disconnect(); homeStates = null; homeChoices = null; homeProblem = null; ShowHomeChoices(); ShowHome();
                Text("HomeConnectionStatus", "Disconnected. The token was forgotten.");
            });
        }

        private List<HomeEntity> ChosenStates { get { var chosen = settings.HomeEntities ?? new string[0]; return homeStates == null ? null : chosen.Select(id => homeStates.FirstOrDefault(e => e.Id == id)).Where(e => e != null).ToList(); } }
        private void ShowHome() { if (!disposed) homeCard.Show(home.Connected, (settings.HomeEntities ?? new string[0]).Length > 0, ChosenStates, homeProblem); }

        private void TickHome()
        {
            if (Editor.IsVisible("Home") && home.Connected && (settings.HomeEntities ?? new string[0]).Length > 0 && DateTime.UtcNow - lastHomeAttempt >= (settings.QuietMode ? HomeQuietInterval : HomeInterval)) RefreshHome();
        }

        private async void RefreshHome()
        {
            if (disposed || homeBusy || !home.Connected) return;
            homeBusy = true; lastHomeAttempt = DateTime.UtcNow;
            try { homeStates = await home.StatesAsync(remoteLifetime.Token); homeProblem = null; }
            catch (OperationCanceledException) { }
            catch (Exception e) { homeProblem = e is InvalidOperationException ? e.Message : "Home Assistant couldn't be read."; }
            finally { homeBusy = false; ShowHome(); }
        }

        // Runs a change, then reads the states again so the card (and a phone) show what really happened.
        private async void HomeAction(HomeEntity e, Func<CancellationToken, Task> action, string done) { await HomeActionAsync(action, done); }
        private async Task<string> HomeActionAsync(Func<CancellationToken, Task> action, string done)
        {
            try { await action(remoteLifetime.Token); if (done != null && !disposed) Notify(done); return null; }
            catch (OperationCanceledException) { return "Widget Screen is closing."; }
            catch (Exception error) { string message = error is InvalidOperationException || error is ArgumentException ? error.Message : "Home Assistant couldn't make that change."; if (!disposed) Notify(message); return message; }
            finally { if (!disposed) { lastHomeAttempt = DateTime.MinValue; RefreshHome(); } }
        }

        // ----- Settings → Home -----

        private void OpenHomeSettings()
        {
            Control<TextBox>("HomeAddress").Text = home.Address == null ? "" : home.Address.ToString().TrimEnd('/');
            Control<PasswordBox>("HomeToken").Password = "";
            Text("HomeConnectionStatus", home.Connected ? "Connected to Home Assistant at " + home.Address.Authority + ". The token is kept encrypted and not shown again." : "Not connected.");
            pendingHomeEntities = new HashSet<string>(settings.HomeEntities ?? new string[0]);
            ShowHomeChoices();
            if (home.Connected && homeChoices == null) LoadHomeChoices();
        }
        private async void LoadHomeChoices()
        {
            int generation = settingsGeneration;
            try { var all = await home.StatesAsync(remoteLifetime.Token); if (disposed || generation != settingsGeneration) return; homeChoices = all; homeStates = all; }
            catch (Exception e) { if (!disposed && generation == settingsGeneration) Text("HomeConnectionStatus", e is InvalidOperationException ? e.Message : "Home Assistant couldn't be read."); }
            ShowHomeChoices();
        }
        private async void ConnectHome()
        {
            int generation = settingsGeneration;
            var button = Control<Button>("HomeConnect"); button.IsEnabled = false; Text("HomeConnectionStatus", "Connecting…");
            try
            {
                string name = await home.ConnectAsync(Control<TextBox>("HomeAddress").Text, Control<PasswordBox>("HomeToken").Password, remoteLifetime.Token);
                if (disposed) return;
                Control<PasswordBox>("HomeToken").Password = "";
                Text("HomeConnectionStatus", "Connected to " + name + ". Choose the devices to show, then save.");
                homeChoices = null; LoadHomeChoices(); lastHomeAttempt = DateTime.MinValue; ShowHome();
            }
            catch (Exception e) { if (!disposed && generation == settingsGeneration) Text("HomeConnectionStatus", e is InvalidOperationException || e is ArgumentException ? e.Message : "Couldn't connect: " + e.GetBaseException().Message); }
            finally { if (!disposed) button.IsEnabled = true; }
        }
        private void ShowHomeChoices()
        {
            var list = Control<StackPanel>("HomeChoices"); list.Children.Clear();
            if (!home.Connected) { list.Children.Add(new TextBlock { Text = "Connect first to see your lights, switches and thermostats.", TextWrapping = TextWrapping.Wrap }); return; }
            if (homeChoices == null) { list.Children.Add(new TextBlock { Text = "Reading your devices…" }); return; }
            if (homeChoices.Count == 0) { list.Children.Add(new TextBlock { Text = "Home Assistant has no lights, switches or thermostats.", TextWrapping = TextWrapping.Wrap }); return; }
            foreach (var e in homeChoices)
            {
                string id = e.Id;
                var box = new CheckBox { Content = e.Name + "  ·  " + (e.Domain == "climate" ? "thermostat" : e.Domain), IsChecked = pendingHomeEntities.Contains(id), ToolTip = id };
                AutomationProperties.SetName(box, e.Name);
                box.Click += (s, a) => {
                    if (box.IsChecked == true)
                    {
                        if (pendingHomeEntities.Count >= HomeAssistantService.MaxEntities) { box.IsChecked = false; Text("SettingsStatus", "The card shows up to " + HomeAssistantService.MaxEntities + " devices."); return; }
                        pendingHomeEntities.Add(id);
                    }
                    else pendingHomeEntities.Remove(id);
                };
                list.Children.Add(box);
            }
        }
        // The order devices were ticked in is kept for ones already chosen; new ones follow in the list's order.
        private string[] HomeChoicesToSave()
        {
            var before = (settings.HomeEntities ?? new string[0]).Where(pendingHomeEntities.Contains);
            var added = (homeChoices ?? new List<HomeEntity>()).Select(e => e.Id).Where(id => pendingHomeEntities.Contains(id) && !(settings.HomeEntities ?? new string[0]).Contains(id));
            return before.Concat(added).Concat(pendingHomeEntities.Where(id => !(homeChoices ?? new List<HomeEntity>()).Any(e => e.Id == id) && !(settings.HomeEntities ?? new string[0]).Contains(id))).Distinct().Take(HomeAssistantService.MaxEntities).ToArray();
        }

        // ----- The phone -----

        private Dictionary<string, object> RemoteHome()
        {
            // A phone showing the card keeps the states fresh even when the PC's own window is in the tray.
            if (home.Connected && (settings.HomeEntities ?? new string[0]).Length > 0 && DateTime.UtcNow - lastHomeAttempt >= (settings.QuietMode ? HomeQuietInterval : HomeInterval)) RefreshHome();
            var chosen = ChosenStates;
            return D("connected", home.Connected, "chosen", (settings.HomeEntities ?? new string[0]).Length, "read", chosen != null, "problem", homeProblem ?? "",
                "entities", (chosen ?? new List<HomeEntity>()).Select(e => (object)D("id", e.Id, "name", e.Name, "domain", e.Domain, "available", e.Available, "on", e.On, "state", HomeAssistantService.Describe(e),
                    "dimmable", e.Dimmable, "brightness", e.Brightness, "current", e.Current, "target", e.Target, "min", e.Min, "max", e.Max, "step", e.Step)).ToArray());
        }
        private async Task<RemoteResponse> RemoteHomeCommand(string action, Dictionary<string, object> body)
        {
            var entity = (ChosenStates ?? new List<HomeEntity>()).FirstOrDefault(e => e.Id == Str(body, "id"));
            if (entity == null) throw new InvalidOperationException("That device isn't on the card anymore.");
            string error = action == "home.toggle" ? await HomeActionAsync(token => home.ToggleAsync(entity, token), null)
                : action == "home.brightness" ? await HomeActionAsync(token => home.BrightnessAsync(entity.Id, (int)Required(body, "value"), token), null)
                : await HomeActionAsync(token => home.TemperatureAsync(entity, Required(body, "value"), token), null);
            if (error != null) throw new InvalidOperationException(error);
            return Done(null);
        }
    }
}
