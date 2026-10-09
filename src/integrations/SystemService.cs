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
    }

    public sealed class SystemService : IDisposable
    {
        private readonly GpuService gpu = new GpuService();
        private readonly object gate = new object();
        private bool disposed;
        private ulong lastIdle, lastKernel, lastUser;
        private long lastReceived, lastSent;
        private DateTime lastNetwork = DateTime.MinValue;
        private string lastInterfaces = "";

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
            lock (gate)
            {
                if (disposed) throw new ObjectDisposedException("SystemService");
                return ReadCore(readCpu, readGpu, readRam, readNetwork, readUptime);
            }
        }

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
                var interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel).OrderBy(n => n.Id).ToArray();
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

        public void Dispose() { lock (gate) { disposed = true; gpu.Dispose(); } }

        public static string FormatRate(double bytes)
        {
            if (bytes >= 1048576) return (bytes / 1048576).ToString("0.0") + " MB/s";
            return (bytes / 1024).ToString("0") + " KB/s";
        }
    }
}
