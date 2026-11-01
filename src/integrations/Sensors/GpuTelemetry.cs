using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace WidgetScreen
{
    // GPU temperature and video memory from Windows itself, with no extra files and no admin rights (decision 016): the
    // graphics kernel's adapter performance data (what Task Manager shows, WDDM 2.4+ drivers) and dedicated memory size,
    // and the "GPU Adapter Memory" counters for memory in use. The adapter with the most dedicated memory is the one
    // shown, so a gaming PC's discrete GPU wins over the integrated one.
    public sealed class GpuTelemetry : IDisposable
    {
        public sealed class Adapter
        {
            public long Luid;
            public string Name;
            public double? Temperature;
            public long DedicatedBytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        // D3DKMT_ADAPTERINFO: the LUID is two 32-bit halves, so it sits at offset 4 (a C# long would move it to 8).
        private struct AdapterInfo { public uint Handle, LuidLow; public int LuidHigh; public uint Sources; public int PreciseRegions; public long Luid { get { return ((long)LuidHigh << 32) | LuidLow; } } }
        [StructLayout(LayoutKind.Sequential)]
        private struct EnumAdapters2 { public uint Count; public IntPtr Adapters; }
        [StructLayout(LayoutKind.Sequential)]
        private struct QueryAdapterInfo { public uint Adapter; public int Type; public IntPtr Data; public uint Size; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PerfData
        {
            public uint PhysicalAdapterIndex;
            public ulong MemoryFrequency, MaxMemoryFrequency, MaxMemoryFrequencyOC, MemoryBandwidth, PCIEBandwidth;
            public uint FanRPM, Power, Temperature;
            public byte PowerStateOverride;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct SegmentSize { public ulong DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RegistryInfo
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string AdapterString;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string BiosString;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DacType;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ChipType;
        }
        private const int GetSegmentSize = 3, AdapterRegistryInfo = 8, AdapterPerfData = 62;

        [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref EnumAdapters2 data);
        [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo data);
        [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref uint adapter);

        [StructLayout(LayoutKind.Sequential)]
        private struct CounterValue { public uint Status; public double Value; }
        [StructLayout(LayoutKind.Sequential)]
        private struct CounterItem { public IntPtr Name; public CounterValue Value; }
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string source, IntPtr data, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr data, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr items);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);

        private IntPtr query, usage;
        private List<Adapter> adapters;
        private DateTime listedAt = DateTime.MinValue;

        // Every adapter with dedicated memory, its name and temperature (null where the driver doesn't report one).
        public static List<Adapter> Adapters(bool temperatures)
        {
            var result = new List<Adapter>();
            var list = new EnumAdapters2();
            if (D3DKMTEnumAdapters2(ref list) != 0 || list.Count == 0 || list.Count > 64) return result;
            int size = Marshal.SizeOf(typeof(AdapterInfo));
            list.Adapters = Marshal.AllocHGlobal(size * (int)list.Count);
            try
            {
                if (D3DKMTEnumAdapters2(ref list) != 0) return result;
                for (int i = 0; i < list.Count; i++)
                {
                    var info = (AdapterInfo)Marshal.PtrToStructure(IntPtr.Add(list.Adapters, i * size), typeof(AdapterInfo));
                    try
                    {
                        var segments = Query<SegmentSize>(info.Handle, GetSegmentSize, new SegmentSize());
                        if (!segments.HasValue || segments.Value.DedicatedVideoMemory == 0) continue;
                        var names = Query<RegistryInfo>(info.Handle, AdapterRegistryInfo, new RegistryInfo());
                        var adapter = new Adapter { Luid = info.Luid, DedicatedBytes = (long)segments.Value.DedicatedVideoMemory, Name = names.HasValue ? (names.Value.AdapterString ?? "").Trim() : "" };
                        if (temperatures)
                        {
                            var perf = Query<PerfData>(info.Handle, AdapterPerfData, new PerfData());
                            // Deci-degrees Celsius; zero means the driver doesn't report it.
                            if (perf.HasValue && perf.Value.Temperature > 0 && perf.Value.Temperature < 1500) adapter.Temperature = perf.Value.Temperature / 10.0;
                        }
                        result.Add(adapter);
                    }
                    finally { uint handle = info.Handle; D3DKMTCloseAdapter(ref handle); }
                }
            }
            finally { Marshal.FreeHGlobal(list.Adapters); }
            return result;
        }

        private static T? Query<T>(uint adapter, int type, T initial) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(initial, buffer, false);
                var request = new QueryAdapterInfo { Adapter = adapter, Type = type, Data = buffer, Size = (uint)size };
                if (D3DKMTQueryAdapterInfo(ref request) != 0) return null;
                return (T)Marshal.PtrToStructure(buffer, typeof(T));
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public sealed class Reading { public string Name; public double? Temperature, VramUsedGB, VramTotalGB; }

        // The main adapter's temperature and memory. The adapter list is kept for a minute; memory in use is live.
        public Reading Read(bool temperature, bool memory)
        {
            if (!temperature && !memory) { Pause(); return null; }
            if (temperature || adapters == null || DateTime.UtcNow - listedAt > TimeSpan.FromMinutes(1))
            {
                var fresh = Adapters(temperature);
                if (adapters == null || fresh.Count > 0) { adapters = fresh; listedAt = DateTime.UtcNow; }
            }
            var main = MainAdapter(adapters);
            if (main == null) return null;
            var reading = new Reading { Name = main.Name, Temperature = main.Temperature, VramTotalGB = main.DedicatedBytes / 1073741824.0 };
            if (memory)
            {
                double? used = DedicatedUsage(main.Luid);
                if (used.HasValue) reading.VramUsedGB = used / 1073741824.0;
            }
            return reading;
        }

        // The most dedicated memory; of equals (Windows can list a card twice), the one with a name.
        public static Adapter MainAdapter(List<Adapter> list) { return list == null ? null : list.OrderByDescending(a => a.DedicatedBytes).ThenByDescending(a => String.IsNullOrEmpty(a.Name) ? 0 : 1).FirstOrDefault(); }

        // "luid_0x00000000_0x0000F2A1_phys_0" in the counter's instance name, from the adapter's LUID.
        public static string LuidName(long luid) { return String.Format("luid_0x{0:X8}_0x{1:X8}", (uint)(luid >> 32), (uint)(luid & 0xFFFFFFFF)); }

        private double? DedicatedUsage(long luid)
        {
            if (query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) { query = IntPtr.Zero; return null; }
                if (PdhAddEnglishCounter(query, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out usage) != 0) { Pause(); return null; }
            }
            if (PdhCollectQueryData(query) != 0) { Pause(); return null; }
            const uint format = 0x400 | 0x8000; // PDH_FMT_LARGE | PDH_FMT_NOCAP100
            uint bytes = 0, count;
            if (PdhGetFormattedCounterArray(usage, format, ref bytes, out count, IntPtr.Zero) != 0x800007D2 || bytes == 0 || bytes > 16777216) return null;
            IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (PdhGetFormattedCounterArray(usage, format, ref bytes, out count, buffer) != 0) return null;
                int size = Marshal.SizeOf(typeof(CounterItem));
                string wanted = LuidName(luid);
                double total = 0; bool found = false;
                for (int i = 0; i < count; i++)
                {
                    IntPtr item = IntPtr.Add(buffer, i * size);
                    string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item));
                    uint status = (uint)Marshal.ReadInt32(item, IntPtr.Size);
                    long value = Marshal.ReadInt64(item, IntPtr.Size + 8);
                    if (status <= 1 && name != null && name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)) { total += value; found = true; }
                }
                return found ? (double?)total : null;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public void Pause() { if (query != IntPtr.Zero) PdhCloseQuery(query); query = usage = IntPtr.Zero; }
        public void Dispose() { Pause(); }
    }
}
