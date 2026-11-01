using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

namespace WidgetScreen
{
    // CPU and GPU readings from LibreHardwareMonitor (decision 016), reached only by reflection so the app builds and runs
    // without it. The library opens lazily, on the system reading's background thread, the first time a reading needs it,
    // and closes again when nothing does. CPU temperatures need full-sensors mode (the app running elevated) and the
    // PawnIO driver; this says which is missing rather than showing a number it doesn't have.
    public sealed class HardwareSensors : IDisposable
    {
        private readonly string folder;
        private readonly object gate = new object();
        private Assembly library;
        private object computer;
        private bool cpuOn, gpuOn, failed;
        private Type hardwareType, sensorType;
        public string Problem { get; private set; }

        public HardwareSensors(string folder) { this.folder = folder; }

        public static bool Elevated
        {
            get { try { using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } catch (Exception) { return false; } }
        }
        // Whether Windows lets this account read performance events (frame counting): administrators, and members of
        // Performance Log Users (S-1-5-32-559) without elevation.
        public static bool CanTrace
        {
            get { try { using (var identity = WindowsIdentity.GetCurrent()) return Elevated || new WindowsPrincipal(identity).IsInRole(new SecurityIdentifier("S-1-5-32-559")); } catch (Exception) { return false; } }
        }
        // The PawnIO driver (https://pawnio.eu) that LibreHardwareMonitor uses for CPU registers; Widget Screen never installs it.
        public static bool PawnIOInstalled
        {
            get { try { using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO")) return key != null; } catch (Exception) { return false; } }
        }

        public SensorPackage.State PackageState { get { string damaged; return SensorPackage.Check(folder, out damaged); } }

        public sealed class Reading
        {
            public double? CpuTemperature, GpuTemperature, GpuLoad, VramUsedGB, VramTotalGB;
            public string GpuName;
        }

        // Reads the CPU and/or GPU groups. Null when the library can't be used (Problem says why).
        public Reading Read(bool cpu, bool gpu)
        {
            lock (gate)
            {
                if (!cpu && !gpu) { Close(); return null; }
                if (failed) return null;
                try
                {
                    if (computer == null && !Open()) return null;
                    if (cpu != cpuOn) { Set("IsCpuEnabled", cpu); cpuOn = cpu; }
                    if (gpu != gpuOn) { Set("IsGpuEnabled", gpu); gpuOn = gpu; }
                    var reading = new Reading();
                    double biggest = -1;
                    foreach (var hardware in Items(Get(computer, "Hardware")))
                    {
                        string kind = Convert.ToString(Value(hardwareType, hardware, "HardwareType"));
                        Invoke(hardware, "Update");
                        if (cpu && kind == "Cpu") reading.CpuTemperature = Max(reading.CpuTemperature, CpuTemperature(hardware));
                        if (gpu && kind.StartsWith("Gpu", StringComparison.Ordinal))
                        {
                            // The GPU with the most memory: a gaming PC's discrete card, not the processor's own graphics.
                            double? used = Sensor(hardware, "SmallData", "GPU Memory Used"), total = Sensor(hardware, "SmallData", "GPU Memory Total");
                            if ((total ?? 0) <= biggest) continue;
                            biggest = total ?? 0;
                            reading.GpuName = Convert.ToString(Value(hardwareType, hardware, "Name"));
                            reading.GpuTemperature = Sensor(hardware, "Temperature", "GPU Core");
                            reading.GpuLoad = Sensor(hardware, "Load", "GPU Core");
                            reading.VramUsedGB = reading.VramTotalGB = null;
                            if (used.HasValue && total.HasValue && total > 0) { reading.VramUsedGB = used / 1024; reading.VramTotalGB = total / 1024; }
                        }
                    }
                    Problem = null;
                    return reading;
                }
                catch (Exception error)
                {
                    // A library that throws is closed and left alone until the app starts again; the other readings carry on.
                    Problem = "The hardware sensors stopped working: " + error.GetBaseException().Message;
                    failed = true; Close();
                    return null;
                }
            }
        }

        private bool Open()
        {
            string problem = null;
            if (library == null) library = SensorPackage.Load(folder, out problem);
            if (library == null) { Problem = problem ?? "Hardware sensors aren't installed."; return false; }
            var computerType = library.GetType("LibreHardwareMonitor.Hardware.Computer", true);
            hardwareType = library.GetType("LibreHardwareMonitor.Hardware.IHardware", true);
            sensorType = library.GetType("LibreHardwareMonitor.Hardware.ISensor", true);
            computer = Activator.CreateInstance(computerType);
            cpuOn = gpuOn = false;
            Invoke(computer, "Open");
            return true;
        }

        private void Close()
        {
            if (computer == null) return;
            try { Invoke(computer, "Close"); } catch (Exception) { }
            computer = null; cpuOn = gpuOn = false;
        }

        // The package temperature where the CPU reports one (Intel "CPU Package", AMD "Core (Tctl/Tdie)"), else the hottest core.
        private double? CpuTemperature(object hardware)
        {
            double? best = null, cores = null;
            foreach (var sensor in Sensors(hardware))
            {
                if (Convert.ToString(Value(sensorType, sensor, "SensorType")) != "Temperature") continue;
                string name = Convert.ToString(Value(sensorType, sensor, "Name"));
                double? value = Number(Value(sensorType, sensor, "Value"));
                if (!value.HasValue || value <= 0 || value > 150) continue;
                if (name == "CPU Package" || name.StartsWith("Core (Tctl", StringComparison.Ordinal) || name == "Tctl/Tdie") best = value;
                else if (name.StartsWith("Core", StringComparison.Ordinal) || name.StartsWith("CCD", StringComparison.Ordinal)) cores = Max(cores, value);
            }
            return best ?? cores;
        }
        private double? Sensor(object hardware, string type, string name)
        {
            foreach (var sensor in Sensors(hardware))
                if (Convert.ToString(Value(sensorType, sensor, "SensorType")) == type && Convert.ToString(Value(sensorType, sensor, "Name")) == name)
                    return Number(Value(sensorType, sensor, "Value"));
            return null;
        }
        private IEnumerable Sensors(object hardware) { return Items(Value(hardwareType, hardware, "Sensors")); }

        private static double? Max(double? a, double? b) { return !a.HasValue ? b : !b.HasValue ? a : Math.Max(a.Value, b.Value); }
        private static double? Number(object value) { if (value == null) return null; double number = Convert.ToDouble(value); return Double.IsNaN(number) || Double.IsInfinity(number) ? (double?)null : number; }
        private static IEnumerable Items(object list) { return list as IEnumerable ?? new object[0]; }
        private static object Get(object target, string property) { return target.GetType().GetProperty(property).GetValue(target, null); }
        private static object Value(Type contract, object target, string property) { return contract.GetProperty(property).GetValue(target, null); }
        private void Set(string property, bool value) { computer.GetType().GetProperty(property).SetValue(computer, value, null); }
        private static void Invoke(object target, string method)
        {
            var info = target.GetType().GetMethod(method, Type.EmptyTypes) ?? target.GetType().GetInterfaces().Select(i => i.GetMethod(method, Type.EmptyTypes)).FirstOrDefault(m => m != null);
            info.Invoke(target, null);
        }

        public void Dispose() { lock (gate) Close(); }
    }
}
