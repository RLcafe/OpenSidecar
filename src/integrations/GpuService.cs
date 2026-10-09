using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WidgetScreen
{
    public sealed class GpuService : IDisposable
    {
        private IntPtr query, counter;
        private DateTime retryAfter;
        public string Status { get; private set; }
        [StructLayout(LayoutKind.Sequential)]
        private struct CounterValue { public uint Status; public double Value; }
        [StructLayout(LayoutKind.Sequential)]
        private struct CounterItem { public IntPtr Name; public CounterValue Value; }
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string source, IntPtr data, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr data, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bytes, out uint count, IntPtr items);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);

        public double? Read()
        {
            if (DateTime.UtcNow < retryAfter) return null;
            if (query == IntPtr.Zero)
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0 ||
                    PdhAddEnglishCounter(query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out counter) != 0 ||
                    PdhCollectQueryData(query) != 0) return Unavailable();
                Status = "Sampling…"; return null; // Rate counters need two observations.
            }
            if (PdhCollectQueryData(query) != 0) return Unavailable();
            const uint format = 0x200 | 0x8000; // PDH_FMT_DOUBLE | PDH_FMT_NOCAP100
            uint bytes = 0, count;
            if (PdhGetFormattedCounterArray(counter, format, ref bytes, out count, IntPtr.Zero) != 0x800007D2 || bytes == 0 || bytes > 16777216) return Unavailable();
            IntPtr buffer = Marshal.AllocHGlobal((int)bytes);
            try
            {
                if (PdhGetFormattedCounterArray(counter, format, ref bytes, out count, buffer) != 0) return Unavailable();
                var readings = new List<KeyValuePair<string, double>>();
                int size = Marshal.SizeOf(typeof(CounterItem));
                for (int i = 0; i < count; i++)
                {
                    var item = (CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer, i * size), typeof(CounterItem));
                    if (item.Value.Status <= 1) readings.Add(new KeyValuePair<string, double>(Marshal.PtrToStringUni(item.Name), item.Value.Value));
                }
                double? value = BusiestEngine(readings);
                Status = value.HasValue ? "Busiest engine" : "Unavailable";
                return value;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }

        public static double? BusiestEngine(IEnumerable<KeyValuePair<string, double>> readings)
        {
            var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var reading in readings)
            {
                // Sum processes on the same physical engine; never add independent engines or GPUs.
                int start = (reading.Key ?? "").IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                if (start < 0 || Double.IsNaN(reading.Value) || Double.IsInfinity(reading.Value) || reading.Value < 0) continue;
                string engine = reading.Key.Substring(start);
                int duplicate = engine.IndexOf('#'); if (duplicate >= 0) engine = engine.Substring(0, duplicate);
                double previous; totals.TryGetValue(engine, out previous); totals[engine] = previous + reading.Value;
            }
            if (totals.Count == 0) return null;
            double busiest = 0; foreach (double total in totals.Values) busiest = Math.Max(busiest, total);
            return Math.Min(100, busiest);
        }

        private double? Unavailable() { Dispose(); Status = "Unavailable"; retryAfter = DateTime.UtcNow.AddMinutes(1); return null; }
        public void Dispose() { if (query != IntPtr.Zero) PdhCloseQuery(query); query = counter = IntPtr.Zero; }
        public void Pause() { Dispose(); retryAfter = DateTime.MinValue; }
    }
}
