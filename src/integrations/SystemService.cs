using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace WidgetScreen
{
    public sealed class SystemSnapshot
    {
        public double Cpu;
        public bool CpuAvailable, MemoryAvailable, NetworkAvailable;
        public double? Gpu;
        public string GpuStatus;
        public double MemoryPercent;
        public double UsedMemoryGB;
        public double TotalMemoryGB;
        public double DownloadBytes;
        public double UploadBytes;
        public TimeSpan Uptime;
        // Hardware sensors (decision 016). A reading that's null has a state saying why: "NotInstalled", "Damaged",
        // "NeedsFullSensors", "NeedsPawnIO", "NoGame" or "Unavailable"; "Working" when it has a value.
        public double? CpuTemperature, GpuTemperature, VramUsedGB, VramTotalGB, Fps;
        public string CpuTemperatureState, GpuTemperatureState, VramState, FpsState, FpsSource, FpsApp, GpuName;
    }

    // Which readings are wanted; anything not wanted isn't read (and its sensors close).
    public sealed class SystemWants
    {
        public bool Cpu, Gpu, Ram, Network, Uptime, CpuTemperature, GpuTemperature, Vram, Fps;
        public bool Any { get { return Cpu || Gpu || Ram || Network || Uptime || CpuTemperature || GpuTemperature || Vram || Fps; } }
        public string Key { get { return "" + Cpu + Gpu + Ram + Network + Uptime + CpuTemperature + GpuTemperature + Vram + Fps; } }
        public static SystemWants From(AppSettings s, bool shown)
        {
            return new SystemWants { Cpu = shown && s.ShowCpu, Gpu = shown && s.ShowGpu, Ram = shown && s.ShowRam, Network = shown && s.ShowNetwork, Uptime = shown && s.ShowUptime,
                CpuTemperature = shown && s.ShowCpuTemperature, GpuTemperature = shown && s.ShowGpuTemperature, Vram = shown && s.ShowVram, Fps = shown && s.ShowFps };
        }
    }

    public sealed class SystemService : IDisposable
    {
        private readonly GpuService gpu = new GpuService();
        private readonly GpuTelemetry telemetry = new GpuTelemetry();
        private readonly HardwareSensors hardware;
        private readonly PresentCounter presents = new PresentCounter();
        private SensorPackage.State? package;
        private readonly bool elevated = HardwareSensors.Elevated;
        private DateTime traceRetry = DateTime.MinValue;
        public string SensorFolder { get; private set; }
        private readonly object gate = new object();
        private bool disposed;
        private ulong lastIdle, lastKernel, lastUser;
        private long lastReceived, lastSent;
        private DateTime lastNetwork = DateTime.MinValue;
        private string lastInterfaces = "";
        // Enumerating adapters is slow on Windows (WMI behind it), so the list is kept for 30 s and dropped when
        // Windows reports an address or availability change; the byte counters themselves are read live.
        private NetworkInterface[] interfaces;
        private DateTime interfacesAt = DateTime.MinValue;
        private volatile bool interfacesStale = true;
        private readonly NetworkAddressChangedEventHandler addressChanged;
        private readonly NetworkAvailabilityChangedEventHandler availabilityChanged;
        public static readonly TimeSpan InterfacesLifetime = TimeSpan.FromSeconds(30);

        public SystemService() : this(SensorPackage.DefaultFolder) { }
        public SystemService(string sensorFolder)
        {
            SensorFolder = sensorFolder; hardware = new HardwareSensors(sensorFolder);
            addressChanged = (sender, e) => interfacesStale = true;
            availabilityChanged = (sender, e) => interfacesStale = true;
            try { NetworkChange.NetworkAddressChanged += addressChanged; NetworkChange.NetworkAvailabilityChanged += availabilityChanged; } catch (Exception) { }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();

        public SystemSnapshot Read(bool readCpu = true, bool readGpu = true, bool readRam = true, bool readNetwork = true, bool readUptime = true)
        {
            return Read(new SystemWants { Cpu = readCpu, Gpu = readGpu, Ram = readRam, Network = readNetwork, Uptime = readUptime });
        }
        public SystemSnapshot Read(SystemWants wants)
        {
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("SystemService");
                var snapshot = ReadCore(wants.Cpu, wants.Gpu, wants.Ram, wants.Network, wants.Uptime);
                ReadSensors(snapshot, wants);
                return snapshot;
            }
        }

        // Whether the sensor files are there and match their pins; hashed once (they don't change while the app runs).
        public SensorPackage.State Package { get { if (!package.HasValue) { string damaged; package = SensorPackage.Check(SensorFolder, out damaged); } return package.Value; } }
        public bool Elevated { get { return elevated; } }

        private void ReadSensors(SystemSnapshot snapshot, SystemWants wants)
        {
            // Windows' own GPU figures first; LibreHardwareMonitor only for what they don't give.
            var windows = wants.GpuTemperature || wants.Vram ? Safe(() => telemetry.Read(wants.GpuTemperature, wants.Vram)) : null;
            if (!wants.GpuTemperature && !wants.Vram) telemetry.Pause();
            if (windows != null) { snapshot.GpuName = windows.Name; snapshot.GpuTemperature = windows.Temperature; snapshot.VramUsedGB = windows.VramUsedGB; snapshot.VramTotalGB = windows.VramTotalGB; }
            bool installed = Package == SensorPackage.State.Installed;
            bool cpuSensors = wants.CpuTemperature && installed && elevated && HardwareSensors.PawnIOInstalled;
            bool gpuSensors = installed && ((wants.GpuTemperature && !snapshot.GpuTemperature.HasValue) || (wants.Vram && !snapshot.VramUsedGB.HasValue));
            var library = cpuSensors || gpuSensors ? hardware.Read(cpuSensors, gpuSensors) : hardware.Read(false, false);
            if (library != null)
            {
                if (cpuSensors) snapshot.CpuTemperature = library.CpuTemperature;
                if (gpuSensors && !snapshot.GpuTemperature.HasValue) snapshot.GpuTemperature = library.GpuTemperature;
                if (gpuSensors && !snapshot.VramUsedGB.HasValue && library.VramUsedGB.HasValue) { snapshot.VramUsedGB = library.VramUsedGB; snapshot.VramTotalGB = library.VramTotalGB; }
                if (String.IsNullOrEmpty(snapshot.GpuName)) snapshot.GpuName = library.GpuName;
            }
            if (wants.CpuTemperature)
                snapshot.CpuTemperatureState = snapshot.CpuTemperature.HasValue ? "Working" : Package == SensorPackage.State.Missing ? "NotInstalled" : Package == SensorPackage.State.Damaged ? "Damaged"
                    : !elevated ? "NeedsFullSensors" : !HardwareSensors.PawnIOInstalled ? "NeedsPawnIO" : "Unavailable";
            if (wants.GpuTemperature) snapshot.GpuTemperatureState = snapshot.GpuTemperature.HasValue ? "Working" : "Unavailable";
            if (wants.Vram) snapshot.VramState = snapshot.VramUsedGB.HasValue && snapshot.VramTotalGB > 0 ? "Working" : "Unavailable";
            if (wants.Fps) ReadFps(snapshot); else presents.Stop();
        }

        private void ReadFps(SystemSnapshot snapshot)
        {
            int process = FrameRate.ForegroundProcess();
            double? fps; string source = null;
            if (RtssReader.Read(process, out fps)) { source = "RTSS"; presents.Stop(); }
            // Windows lets administrators and members of Performance Log Users count frames; anyone else is asked again
            // a minute later rather than on every reading.
            else if (presents.Running || DateTime.UtcNow >= traceRetry)
            {
                if (presents.Start()) { fps = presents.Rate(process, DateTime.UtcNow); source = "DirectX"; }
                else traceRetry = DateTime.UtcNow.AddMinutes(1);
            }
            snapshot.Fps = fps; snapshot.FpsSource = source;
            snapshot.FpsState = fps.HasValue ? "Working" : source != null ? "NoGame" : elevated ? "Unavailable" : "NeedsFullSensors";
            if (fps.HasValue) try { using (var game = System.Diagnostics.Process.GetProcessById(process)) snapshot.FpsApp = game.ProcessName; } catch (Exception) { }
        }

        private static T Safe<T>(Func<T> read) where T : class { try { return read(); } catch (Exception) { return null; } }

        private SystemSnapshot ReadCore(bool readCpu, bool readGpu, bool readRam, bool readNetwork, bool readUptime)
        {
            var snapshot = new SystemSnapshot();
            ulong idle, kernel, user;
            if (readCpu && GetSystemTimes(out idle, out kernel, out user))
            {
            ulong total = (kernel - lastKernel) + (user - lastUser);
            snapshot.CpuAvailable = lastKernel != 0 && total > 0;
            if (lastKernel != 0 && total > 0) snapshot.Cpu = Math.Max(0, Math.Min(100, 100.0 * (total - Math.Min(total, idle - lastIdle)) / total));
            lastIdle = idle; lastKernel = kernel; lastUser = user;
            }
            else lastIdle = lastKernel = lastUser = 0;
            if (readGpu) { snapshot.Gpu = gpu.Read(); snapshot.GpuStatus = gpu.Status; }
            else gpu.Pause();
            var memory = new MemoryStatus(); memory.Length = (uint)Marshal.SizeOf(memory);
            if (readRam && GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0)
            {
            snapshot.MemoryAvailable = true;
            snapshot.TotalMemoryGB = memory.TotalPhysical / 1073741824.0;
            snapshot.UsedMemoryGB = (memory.TotalPhysical - memory.AvailablePhysical) / 1073741824.0;
            snapshot.MemoryPercent = 100.0 * snapshot.UsedMemoryGB / snapshot.TotalMemoryGB;
            }
            if (readUptime) snapshot.Uptime = TimeSpan.FromMilliseconds(GetTickCount64());
            if (readNetwork)
            {
            try
            {
                var now0 = DateTime.UtcNow;
                if (interfaces == null || interfacesStale || now0 - interfacesAt > InterfacesLifetime)
                {
                    interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel).OrderBy(n => n.Id).ToArray();
                    interfacesAt = now0; interfacesStale = false;
                }
                string ids = String.Join(";", interfaces.Select(n => n.Id));
                long received = 0, sent = 0;
                foreach (var adapter in interfaces) { var stats = adapter.GetIPv4Statistics(); received += stats.BytesReceived; sent += stats.BytesSent; }
                var now = DateTime.UtcNow;
                double seconds = (now - lastNetwork).TotalSeconds;
                if (lastNetwork != DateTime.MinValue && ids == lastInterfaces && seconds > 0)
                {
                    snapshot.NetworkAvailable = interfaces.Length > 0;
                    snapshot.DownloadBytes = Math.Max(0, received - lastReceived) / seconds;
                    snapshot.UploadBytes = Math.Max(0, sent - lastSent) / seconds;
                }
                lastReceived = received; lastSent = sent; lastNetwork = now; lastInterfaces = ids;
            }
            catch (NetworkInformationException) { }
            }
            else lastNetwork = DateTime.MinValue;
            return snapshot;
        }

        public void Dispose()
        {
            try { NetworkChange.NetworkAddressChanged -= addressChanged; NetworkChange.NetworkAvailabilityChanged -= availabilityChanged; } catch (Exception) { }
            lock (gate) { disposed = true; gpu.Dispose(); telemetry.Dispose(); hardware.Dispose(); presents.Dispose(); }
        }

        public static string FormatRate(double bytes)
        {
            if (bytes >= 1048576) return (bytes / 1048576).ToString("0.0") + " MB/s";
            return (bytes / 1024).ToString("0") + " KB/s";
        }
    }
}
