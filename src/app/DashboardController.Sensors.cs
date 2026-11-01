using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // Settings → Stats (decision 016; docs/design/system-sensors.md): the System stats readings and their graph styles,
    // and the hardware-sensor status, full-sensors mode, PawnIO and frame-rate notes. Turning full-sensors mode on or off
    // takes effect at once (Windows asks through UAC); the readings and styles are saved with Save changes.
    public sealed partial class DashboardController
    {
        private void InitializeSensors()
        {
            foreach (string id in SystemStyles.Ids.Where(i => i != "Uptime")) Control<ComboBox>(id + "Style").ItemsSource = SystemStyles.StylesFor(id);
            Button("FullSensorsOn", TurnOnFullSensors);
            Button("FullSensorsOff", TurnOffFullSensors);
            Button("FullSensorsRestart", RestartForSensors);
            Button("PawnIOLink", () => { try { ProcessLauncher.Start("https://pawnio.eu", "", null); } catch (Exception e) { Notify("The browser didn't open: " + e.Message); } });
        }

        private void OpenSensorSettings()
        {
            foreach (string id in SystemStyles.Ids)
            {
                Control<CheckBox>(id + "Choice").IsChecked = SystemStyles.Shown(settings, id);
                if (id != "Uptime") Control<ComboBox>(id + "Style").SelectedItem = SystemStyles.Get(settings.StatStyles, id);
            }
            ShowSensorStatus(null);
            int generation = settingsGeneration;
            // Asking Task Scheduler takes a moment; the rest of the page doesn't wait for it.
            Task.Run(() => FullSensors.RegisteredExe()).ContinueWith(t => { if (!disposed && generation == settingsGeneration) ShowSensorStatus(t.Result ?? ""); }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void SaveSensorChoices(AppSettings candidate)
        {
            var styles = SystemStyles.Parse(settings.StatStyles);
            foreach (string id in SystemStyles.Ids)
            {
                SystemStyles.Show(candidate, id, Control<CheckBox>(id + "Choice").IsChecked == true);
                if (id != "Uptime") styles[id] = (Control<ComboBox>(id + "Style").SelectedItem as string) ?? "Auto";
            }
            candidate.StatStyles = SystemStyles.Write(styles);
            // Full-sensors mode was saved when it was switched, not with this page.
            candidate.FullSensors = settings.FullSensors;
        }

        private static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }

        // registered: the exe the task starts, "" when there's no task, null while it's being looked up.
        private void ShowSensorStatus(string registered)
        {
            var package = system.Package;
            Text("SensorPackageStatus", package == SensorPackage.State.Installed ? "LibreHardwareMonitor " + SensorPackage.Version + " is installed, and its files match their pinned copies."
                : package == SensorPackage.State.Damaged ? "A sensor file doesn't match its pinned copy, so the hardware sensors are off. Run setup again to repair them."
                : "LibreHardwareMonitor isn't installed. Run setup again and tick Install hardware sensors to add it.");
            bool elevated = system.Elevated, here = registered != null && registered.Length > 0 && FullSensors.PointsTo(registered, ExePath);
            string status;
            if (elevated) status = "On. Widget Screen is running with administrator rights.";
            else if (registered == null) status = settings.FullSensors ? "On. Checking the task…" : "Off.";
            else if (settings.FullSensors && here) status = "On, but this copy started without administrator rights. Restart to use it.";
            else if (registered.Length > 0 && !here) status = "The full-sensors task starts another copy of Widget Screen (" + registered + "). Turn the mode off and on again to use this one.";
            else if (settings.FullSensors) status = "On, but its Windows task is missing. Turn it off and on again.";
            else status = "Off.";
            Text("FullSensorsStatus", status);
            bool on = settings.FullSensors || elevated || (registered != null && registered.Length > 0);
            Control<Button>("FullSensorsOn").Visibility = on && (elevated || here || registered == null) ? Visibility.Collapsed : Visibility.Visible;
            Control<Button>("FullSensorsOff").Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("FullSensorsRestart").Visibility = settings.FullSensors && here && !elevated ? Visibility.Visible : Visibility.Collapsed;
            Text("PawnIOStatus", HardwareSensors.PawnIOInstalled ? "PawnIO is installed on this PC." : "PawnIO isn't installed on this PC, so CPU temperatures can't be read.");
            double? ignored;
            Text("FpsStatus", RtssReader.Read(0, out ignored) ? "RivaTuner Statistics Server is running, so frame rates come from it, for any game."
                : HardwareSensors.CanTrace ? "Counting frames of DirectX 9, 10, 11 and 12 games" + (elevated ? "" : " (this Windows account may read performance events, so full-sensors mode isn't needed for this)") + ". For Vulkan and OpenGL games, run RivaTuner Statistics Server."
                : "Frame rates need full-sensors mode (DirectX games) or RivaTuner Statistics Server running (any game).");
        }

        private async void TurnOnFullSensors()
        {
            if (!await confirm.AskAsync("FULL-SENSORS MODE", "Run Widget Screen with administrator rights?",
                "Windows asks once to let Task Scheduler add a task that starts Widget Screen with administrator rights. After that it starts that way by itself, so it can read CPU temperatures and count frames. Apps you start from it still start without administrator rights. Turn it off here at any time.", "Turn on")) return;
            string exe = ExePath, problem = await Task.Run(() => FullSensors.Register(exe));
            if (disposed) return;
            if (problem != null) { Text("FullSensorsStatus", "Not turned on: " + problem); return; }
            settings.FullSensors = true; Persist();
            ShowSensorStatus(exe);
            Notify("Full-sensors mode is on. Restart Widget Screen to use it.");
        }

        private async void TurnOffFullSensors()
        {
            string problem = await Task.Run(() => FullSensors.Unregister());
            if (disposed) return;
            settings.FullSensors = false; Persist();
            if (problem != null) { Text("FullSensorsStatus", "The mode is off, but its Windows task is still there: " + problem); return; }
            ShowSensorStatus("");
            if (system.Elevated)
            {
                // Start again without administrator rights, through the desktop's shell.
                if (ProcessLauncher.StartThroughShell(ExePath, "", System.IO.Path.GetDirectoryName(ExePath), false)) window.Close();
                else Notify("Full-sensors mode is off. Restart Widget Screen to run without administrator rights.", true);
            }
        }

        private void RestartForSensors()
        {
            if (FullSensors.StartElevatedCopy()) window.Close();
            else Notify("Windows didn't start the full-sensors task. Turn the mode off and on again.");
        }

        // Program.Main: with the mode on, a copy started normally hands over to the elevated one and closes.
        public static bool HandOverToElevatedCopy(string settingsPath)
        {
            try
            {
                if (HardwareSensors.Elevated) return false;
                string warning; var early = SettingsStore.Load(settingsPath, out warning);
                if (!early.FullSensors) return false;
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                return FullSensors.PointsTo(FullSensors.RegisteredExe(), exe) && FullSensors.StartElevatedCopy();
            }
            catch (Exception) { return false; }
        }
    }
}
