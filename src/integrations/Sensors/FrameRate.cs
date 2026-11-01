using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace WidgetScreen
{
    // In-game frame rate (decision 016) for the game in front: LibreHardwareMonitor has none, so it comes from
    //   1. RivaTuner Statistics Server's shared memory when RTSS is running (any API, no admin), or
    //   2. DirectX present events (DXGI and Direct3D 9) counted from a real-time ETW session, which Windows only allows
    //      elevated, so only in full-sensors mode. Vulkan and OpenGL games need RTSS.
    // The dashboard never takes the focus, so the window in front is the game.
    public static class FrameRate
    {
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);

        public static int ForegroundProcess()
        {
            uint process; IntPtr window = GetForegroundWindow();
            return window == IntPtr.Zero || GetWindowThreadProcessId(window, out process) == 0 ? 0 : (int)process;
        }
    }

    // RTSSSharedMemoryV2 (RTSSSharedMemory.h): a header, then an array of per-application entries with frames counted
    // over a window of dwTime0..dwTime1 milliseconds.
    public static class RtssReader
    {
        public const string MappingName = "RTSSSharedMemoryV2";
        public const uint Signature = 0x52545353; // 'RTSS'
        private const uint FileMapRead = 4;
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint high, uint low, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(IntPtr view);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        // Whether RTSS is running (its shared memory exists); fps is the process's rate, null when RTSS doesn't track it.
        public static bool Read(int process, out double? fps, string mapping = MappingName)
        {
            fps = null;
            IntPtr handle = OpenFileMapping(FileMapRead, false, mapping);
            if (handle == IntPtr.Zero) return false;
            try
            {
                IntPtr view = MapViewOfFile(handle, FileMapRead, 0, 0, UIntPtr.Zero);
                if (view == IntPtr.Zero) return false;
                try { fps = Find(view, process); return Valid(view); }
                finally { UnmapViewOfFile(view); }
            }
            finally { CloseHandle(handle); }
        }

        private static bool Valid(IntPtr view) { return (uint)Marshal.ReadInt32(view, 0) == Signature && (uint)Marshal.ReadInt32(view, 4) >= 0x00020000; }

        public static double? Find(IntPtr view, int process)
        {
            if (!Valid(view) || process == 0) return null;
            uint entrySize = (uint)Marshal.ReadInt32(view, 8), arrayOffset = (uint)Marshal.ReadInt32(view, 12), count = (uint)Marshal.ReadInt32(view, 16);
            if (entrySize < 284 || count > 1024 || arrayOffset < 36) return null;
            for (uint i = 0; i < count; i++)
            {
                IntPtr entry = IntPtr.Add(view, (int)(arrayOffset + i * entrySize));
                if (Marshal.ReadInt32(entry, 0) != process) continue;
                uint time0 = (uint)Marshal.ReadInt32(entry, 268), time1 = (uint)Marshal.ReadInt32(entry, 272), frames = (uint)Marshal.ReadInt32(entry, 276);
                if (time1 <= time0 || time1 - time0 > 10000) return null;
                return Math.Round(1000.0 * frames / (time1 - time0), 1);
            }
            return null;
        }
    }

    // DXGI and Direct3D 9 Present events per process, from a real-time ETW session (elevated only).
    public sealed class PresentCounter : IDisposable
    {
        public const string SessionName = "WidgetScreen-FrameRate";
        public static readonly Guid Dxgi = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), D3d9 = new Guid("783ACA0A-790E-4D7F-8451-AA850511C6B9");
        public const ushort DxgiPresentStart = 42, D3d9PresentStart = 1;

        private const int PropertiesSize = 120, NameRoom = 512, LogFileSize = 448;
        private const uint RealTimeMode = 0x100, TracedGuid = 0x20000, ControlStop = 1, EnableProvider = 1;
        private const uint ProcessRealTime = 0x100, ProcessEventRecord = 0x10000000;
        private static readonly ulong InvalidTrace = UInt64.MaxValue;

        private delegate void RecordCallback(IntPtr record);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int StartTrace(out ulong session, string name, IntPtr properties);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern int ControlTrace(ulong session, string name, IntPtr properties, uint code);
        [DllImport("advapi32.dll")] private static extern int EnableTraceEx2(ulong session, ref Guid provider, uint code, byte level, ulong any, ulong all, uint timeout, IntPtr parameters);
        // OpenTraceW: without CharSet.Unicode .NET binds OpenTraceA, which reads the session name as ANSI and never finds it.
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] private static extern ulong OpenTrace(IntPtr logfile);
        [DllImport("advapi32.dll")] private static extern int ProcessTrace(ulong[] handles, uint count, IntPtr start, IntPtr end);
        [DllImport("advapi32.dll")] private static extern int CloseTrace(ulong trace);

        private readonly object gate = new object();
        private readonly Dictionary<int, int> counts = new Dictionary<int, int>();
        private DateTime since = DateTime.UtcNow;
        private ulong session, trace = InvalidTrace;
        private RecordCallback callback;
        private IntPtr loggerName = IntPtr.Zero, logFile = IntPtr.Zero;
        private Thread worker;
        public bool Running { get; private set; }
        public string Problem { get; private set; }

        public bool Start()
        {
            if (Running) return true;
            Stop();
            IntPtr properties = Properties();
            try
            {
                int error = StartTrace(out session, SessionName, properties);
                // A session left over from a copy that didn't close cleanly: stop it and start again.
                if (error == 183) { ControlTrace(0, SessionName, properties, ControlStop); Marshal.FreeHGlobal(properties); properties = Properties(); error = StartTrace(out session, SessionName, properties); }
                if (error != 0) { session = 0; Problem = error == 5 ? "Counting frames needs full-sensors mode." : "Windows didn't start frame counting (" + error + ")."; return false; }
            }
            finally { Marshal.FreeHGlobal(properties); }
            var dxgi = Dxgi; var d3d9 = D3d9;
            EnableTraceEx2(session, ref dxgi, EnableProvider, 4, UInt64.MaxValue, 0, 0, IntPtr.Zero);
            EnableTraceEx2(session, ref d3d9, EnableProvider, 4, UInt64.MaxValue, 0, 0, IntPtr.Zero);
            callback = OnRecord;
            loggerName = Marshal.StringToHGlobalUni(SessionName);
            logFile = Marshal.AllocHGlobal(LogFileSize);
            for (int i = 0; i < LogFileSize; i += 8) Marshal.WriteInt64(logFile, i, 0);
            Marshal.WriteIntPtr(logFile, 8, loggerName);
            Marshal.WriteInt32(logFile, 28, unchecked((int)(ProcessRealTime | ProcessEventRecord)));
            Marshal.WriteIntPtr(logFile, 424, Marshal.GetFunctionPointerForDelegate(callback));
            trace = OpenTrace(logFile);
            if (trace == InvalidTrace) { Problem = "Windows didn't open frame counting."; Stop(); return false; }
            ulong opened = trace;
            worker = new Thread(() => { try { ProcessTrace(new[] { opened }, 1, IntPtr.Zero, IntPtr.Zero); } catch (Exception) { } }) { IsBackground = true, Name = "Frame counting" };
            worker.Start();
            lock (gate) { counts.Clear(); since = DateTime.UtcNow; }
            Running = true; Problem = null;
            return true;
        }

        // EVENT_TRACE_PROPERTIES with room for the session name after it.
        private static IntPtr Properties()
        {
            int size = PropertiesSize + NameRoom;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            for (int i = 0; i < size; i += 4) Marshal.WriteInt32(buffer, i, 0);
            Marshal.WriteInt32(buffer, 0, size);              // Wnode.BufferSize
            Marshal.WriteInt32(buffer, 40, 1);                // Wnode.ClientContext: QPC
            Marshal.WriteInt32(buffer, 44, (int)TracedGuid);  // Wnode.Flags
            Marshal.WriteInt32(buffer, 48, 64);               // BufferSize, KB
            Marshal.WriteInt32(buffer, 64, (int)RealTimeMode);// LogFileMode
            Marshal.WriteInt32(buffer, 68, 1);                // FlushTimer, s
            Marshal.WriteInt32(buffer, 116, PropertiesSize);  // LoggerNameOffset
            return buffer;
        }

        // EVENT_RECORD: ProcessId at 12, ProviderId at 24, the event id at 40.
        private void OnRecord(IntPtr record)
        {
            int process = Marshal.ReadInt32(record, 12);
            var bytes = new byte[16]; Marshal.Copy(IntPtr.Add(record, 24), bytes, 0, 16);
            Count(new Guid(bytes), (ushort)Marshal.ReadInt16(record, 40), process);
        }

        public void Count(Guid provider, ushort id, int process)
        {
            if (!((provider == Dxgi && id == DxgiPresentStart) || (provider == D3d9 && id == D3d9PresentStart))) return;
            lock (gate) { int n; counts.TryGetValue(process, out n); counts[process] = n + 1; }
        }

        // Frames a second for the process since the last call; null when it presented nothing (not a DirectX game).
        public double? Rate(int process, DateTime now)
        {
            lock (gate)
            {
                double seconds = (now - since).TotalSeconds;
                int n; counts.TryGetValue(process, out n);
                counts.Clear(); since = now;
                if (seconds < 0.25 || n == 0) return null;
                return Math.Round(n / seconds, 1);
            }
        }

        public void Stop()
        {
            if (session != 0 || Running)
            {
                IntPtr properties = Properties();
                try { ControlTrace(0, SessionName, properties, ControlStop); } finally { Marshal.FreeHGlobal(properties); }
            }
            if (trace != InvalidTrace) { CloseTrace(trace); trace = InvalidTrace; }
            if (worker != null) { worker.Join(2000); worker = null; }
            if (logFile != IntPtr.Zero) { Marshal.FreeHGlobal(logFile); logFile = IntPtr.Zero; }
            if (loggerName != IntPtr.Zero) { Marshal.FreeHGlobal(loggerName); loggerName = IntPtr.Zero; }
            session = 0; Running = false;
        }

        public void Dispose() { Stop(); }
    }
}
