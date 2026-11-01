using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Prayer times widget (docs/design/prayer-times.md): today's five prayers worked out on this PC, the next one
    // counted down on the one-second heartbeat while the card is shown, and an optional alert at each prayer that plays
    // on the PC (silent in Quiet mode). A phone gets the same times and can stop the alert.
    public sealed partial class DashboardController
    {
        private PrayerCard prayerCard;
        private PrayerSchedule prayerSchedule;
        private readonly PrayerAlert prayerAlert = new PrayerAlert();
        // The prayer the alert last played for (UTC), so each one plays once.
        private DateTime prayerAlerted = DateTime.MinValue;
        private WeatherLocation pendingPrayerCity;
        private bool pendingPrayerFollowsWeather;
        private string pendingPrayerSound = "";
        // For the checks (tests/Prayer/PrayerTests.cs).
        public PrayerCard PrayerView { get { return prayerCard; } }
        public PrayerSchedule Prayer { get { return prayerSchedule; } }
        public PrayerAlert PrayerSound { get { return prayerAlert; } }

        private void InitializePrayer()
        {
            prayerCard = new PrayerCard(window, Control<Grid>("PrayerBody"), Control<TextBlock>("PrayerStatus"));
            prayerAlert.Stopped += () => { if (!disposed) { UpdatePrayerStop(); RenderPrayer(); } };
            Button("PrayerStop", StopPrayerAlert);
            // Without a city, a tap on the card opens Settings → Prayer.
            Action tapped = () => {
                if (Editor.Editing || prayerSchedule != null) return;
                OpenSettingsTab("Prayer"); window.UpdateLayout(); Control<TextBox>("PrayerCitySearch").Focus();
            };
            Control<Border>("PrayerCard").PreviewMouseLeftButtonUp += (sender, e) => tapped();
            Control<Border>("PrayerCard").PreviewTouchUp += (sender, e) => tapped();
            Button("PrayerSearchButton", SearchPrayerCity);
            Control<ComboBox>("PrayerCityResults").SelectionChanged += (sender, e) => {
                var selected = Control<ComboBox>("PrayerCityResults").SelectedItem as WeatherLocation;
                if (selected != null) { pendingPrayerCity = selected; pendingPrayerFollowsWeather = false; ShowPrayerCity(); }
            };
            Button("PrayerUseWeather", () => { pendingPrayerCity = null; pendingPrayerFollowsWeather = true; Control<ComboBox>("PrayerCityResults").Visibility = Visibility.Collapsed; ShowPrayerCity(); });
            Button("PrayerSoundBrowse", () => {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose the prayer alert sound", Filter = "Audio files|*.mp3;*.wav;*.wma;*.m4a;*.aac", CheckFileExists = true };
                if (dialog.ShowDialog(window) == true) { pendingPrayerSound = dialog.FileName; Control<TextBox>("PrayerSoundPath").Text = pendingPrayerSound; }
            });
            Button("PrayerSoundDefault", () => { pendingPrayerSound = ""; Control<TextBox>("PrayerSoundPath").Text = "The built-in chime"; });
            Button("PrayerSoundTest", () => {
                if (prayerAlert.Playing) prayerAlert.Stop();
                else if (!prayerAlert.Play(pendingPrayerSound)) Text("SettingsStatus", "That file couldn't be played, so the chime played instead.");
                UpdatePrayerStop();
            });
        }

        // The schedule follows Settings → Prayer, or the weather city; the cached days are kept while nothing changes.
        private void ApplyPrayer()
        {
            var next = PrayerSchedule.For(settings);
            if (next == null || !next.Same(prayerSchedule)) prayerSchedule = next;
            // Changing the city or method never sounds an alert for a prayer that has already begun.
            if (prayerSchedule != null) prayerAlerted = prayerSchedule.Next(DateTime.UtcNow, TimeZoneInfo.Local).Previous;
            prayerCard.Configure(prayerSchedule != null, settings.Use24HourClock, PrayerService.AlertText(settings.PrayerAlert, settings.PrayerSound));
            RenderPrayer();
        }

        private void RenderPrayer()
        {
            if (Editor.IsVisible("Prayer")) prayerCard.Render(prayerSchedule, DateTime.UtcNow, prayerAlert.Playing);
        }

        // Every second, even minimized or in the tray: the alert is due when a prayer has just begun.
        private void TickPrayer()
        {
            if (prayerSchedule == null) return;
            var now = DateTime.UtcNow;
            var next = prayerSchedule.Next(now, TimeZoneInfo.Local);
            if (next.Previous > prayerAlerted && now - next.Previous < TimeSpan.FromMinutes(1))
            {
                prayerAlerted = next.Previous;
                string name = PrayerService.Names[next.CurrentIndex];
                Notify("It's time for " + name + ".");
                if (settings.PrayerAlert && !settings.QuietMode && !Program.Rendering) { prayerAlert.Play(settings.PrayerSound); UpdatePrayerStop(); }
            }
            if (Showing) RenderPrayer();
        }

        private void StopPrayerAlert() { prayerAlert.Stop(); UpdatePrayerStop(); }
        private void UpdatePrayerStop()
        {
            Control<Button>("PrayerStop").Visibility = prayerAlert.Playing ? Visibility.Visible : Visibility.Collapsed;
            if (Control<Grid>("SettingsOverlay").Visibility == Visibility.Visible) Control<Button>("PrayerSoundTest").Content = prayerAlert.Playing ? "Stop" : "Play";
        }

        // ----- Settings → Prayer -----

        private void OpenPrayerSettings()
        {
            pendingPrayerFollowsWeather = String.IsNullOrWhiteSpace(settings.PrayerLocation);
            pendingPrayerCity = pendingPrayerFollowsWeather ? null : new WeatherLocation { Name = settings.PrayerLocation, Latitude = settings.PrayerLatitude, Longitude = settings.PrayerLongitude };
            Control<TextBox>("PrayerCitySearch").Text = ""; Control<ComboBox>("PrayerCityResults").Visibility = Visibility.Collapsed;
            ShowPrayerCity();
            var methods = Control<ComboBox>("PrayerMethodChoice"); methods.ItemsSource = PrayerService.Methods; methods.SelectedItem = PrayerService.Method(settings.PrayerMethod);
            var asr = Control<ComboBox>("PrayerAsrChoice"); asr.ItemsSource = new[] { "Standard (Shafi'i, Maliki, Hanbali)", "Hanafi" }; asr.SelectedIndex = settings.PrayerHanafi ? 1 : 0;
            Control<CheckBox>("PrayerAlertChoice").IsChecked = settings.PrayerAlert;
            pendingPrayerSound = settings.PrayerSound ?? "";
            Control<TextBox>("PrayerSoundPath").Text = pendingPrayerSound.Length == 0 ? "The built-in chime" : pendingPrayerSound;
            Control<Button>("PrayerSoundTest").Content = prayerAlert.Playing ? "Stop" : "Play";
        }
        private void ShowPrayerCity()
        {
            string weatherCity = pendingLocation == null ? "" : pendingLocation.Name;
            Text("PrayerCityNote", pendingPrayerCity != null ? pendingPrayerCity.Name
                : weatherCity.Length > 0 ? "Using the weather city: " + weatherCity
                : "No city yet. Search for one, or set the weather city.");
            Control<Button>("PrayerUseWeather").IsEnabled = pendingPrayerCity != null;
        }
        private async void SearchPrayerCity()
        {
            int generation = settingsGeneration;
            var button = Control<Button>("PrayerSearchButton"); button.IsEnabled = false; button.Content = "Searching…"; Text("SettingsStatus", "Searching for cities…");
            try
            {
                var results = await weather.SearchAsync(Control<TextBox>("PrayerCitySearch").Text);
                if (disposed || generation != settingsGeneration) return;
                var list = Control<ComboBox>("PrayerCityResults"); list.ItemsSource = results; list.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                Text("SettingsStatus", results.Count == 0 ? "No cities found. Try a nearby city or another spelling." : "Pick your city from the list, then save.");
                if (results.Count > 0) list.IsDropDownOpen = true;
            }
            catch (Exception e) { if (!disposed && generation == settingsGeneration) Text("SettingsStatus", "City search didn't work. Check your connection and try again. (" + e.GetBaseException().Message + ")"); }
            finally { if (!disposed) { button.IsEnabled = true; button.Content = "Search"; } }
        }
        private void SavePrayerChoices(AppSettings candidate)
        {
            candidate.PrayerLocation = pendingPrayerCity == null ? "" : pendingPrayerCity.Name;
            candidate.PrayerLatitude = pendingPrayerCity == null ? 0 : pendingPrayerCity.Latitude;
            candidate.PrayerLongitude = pendingPrayerCity == null ? 0 : pendingPrayerCity.Longitude;
            var method = Control<ComboBox>("PrayerMethodChoice").SelectedItem as PrayerMethod;
            if (method != null) candidate.PrayerMethod = method.Id;
            candidate.PrayerHanafi = Control<ComboBox>("PrayerAsrChoice").SelectedIndex == 1;
            candidate.PrayerAlert = Control<CheckBox>("PrayerAlertChoice").IsChecked == true;
            candidate.PrayerSound = pendingPrayerSound ?? "";
        }

        // ----- The phone -----

        private static double Epoch(DateTime utc) { return utc == DateTime.MinValue ? 0 : (utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }

        // The phone's prayer section: today's times as moments (the phone counts down with its own clock), the next and
        // the current prayer, and whether the alert is playing so the phone can offer Stop.
        private Dictionary<string, object> RemotePrayer()
        {
            if (prayerSchedule == null) return D("located", false);
            var now = DateTime.UtcNow;
            var next = prayerSchedule.Next(now, TimeZoneInfo.Local);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.Local);
            var today = prayerSchedule.Day(localNow.Date);
            return D("located", true, "city", prayerSchedule.City, "method", PrayerService.Method(prayerSchedule.MethodId).Name, "hijri", PrayerService.Hijri(localNow.Date),
                "pcOffset", (int)Math.Round(TimeZoneInfo.Local.GetUtcOffset(now).TotalMinutes), "alerting", prayerAlert.Playing, "alert", PrayerService.AlertText(settings.PrayerAlert, settings.PrayerSound),
                "next", next.Index, "nextAt", Epoch(next.At), "previousAt", Epoch(next.Previous), "current", next.CurrentIndex, "sunrise", Epoch(today.Sunrise),
                "times", today.Prayers.Select(t => (object)Epoch(t)).ToArray());
        }
    }
}
