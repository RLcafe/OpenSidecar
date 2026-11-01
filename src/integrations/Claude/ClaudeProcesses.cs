using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace WidgetScreen
{
    // The processes behind a Claude Code session (docs/design/claude-watcher.md). The hook walks up from itself to the
    // Claude Code process (claude.exe, or node.exe for an npm install) and on to the first ancestor with a window
    // (Windows Terminal, VS Code, a console, the Claude app), so the dashboard can drop sessions whose Claude Code has
    // gone and bring a session's window to the front when its critter is tapped. Shared by ClaudeHook.exe and the app.
    public static class ClaudeProcesses
    {
        public struct Owner { public int ClaudePid, WindowPid; public long Window; }

        private static bool IsClaude(string exe) { return String.Equals(exe, "claude.exe", StringComparison.OrdinalIgnoreCase) || String.Equals(exe, "node.exe", StringComparison.OrdinalIgnoreCase); }
        // Windows whose windows aren't a session's own: the desktop shell and system processes.
        private static bool StopsWalk(string exe)
        {
            foreach (string name in new[] { "explorer.exe", "svchost.exe", "services.exe", "wininit.exe", "winlogon.exe", "sihost.exe", "userinit.exe" })
                if (String.Equals(exe, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        // Walks up from the given process (the hook itself). Best effort: anything not found stays 0.
        public static Owner Find(int start)
        {
            var owner = new Owner();
            try
            {
                var tree = Snapshot();
                int pid = start; bool claudeSeen = false;
                for (int depth = 0; depth < 24; depth++)
                {
                    Entry entry; if (!tree.TryGetValue(pid, out entry)) break;
                    int parent = entry.Parent;
                    Entry up; if (parent == 0 || parent == pid || !tree.TryGetValue(parent, out up)) break;
                    if (StopsWalk(up.Exe)) break;
                    if (!claudeSeen) { if (IsClaude(up.Exe)) { owner.ClaudePid = parent; claudeSeen = true; } }
                    else
                    {
                        IntPtr window = MainWindow(parent);
                        if (window != IntPtr.Zero) { owner.WindowPid = parent; owner.Window = window.ToInt64(); break; }
                    }
                    pid = parent;
                }
                // A console Claude Code shares its console window with the hook.
                if (owner.Window == 0)
                {
                    IntPtr console = GetConsoleWindow();
                    if (console != IntPtr.Zero && IsWindowVisible(console)) { uint process; GetWindowThreadProcessId(console, out process); owner.Window = console.ToInt64(); owner.WindowPid = (int)process; }
                }
            }
            catch (Exception) { }
            return owner;
        }

        // The window of a Claude Code process found some other way (Claude Code's own session list): its first ancestor
        // with a window, such as the Claude desktop app for its Code tab, or the terminal a CLI runs in.
        public static Owner OwnerOf(int claudePid)
        {
            var owner = new Owner { ClaudePid = claudePid };
            if (claudePid <= 0) return owner;
            try
            {
                var tree = Snapshot();
                int pid = claudePid;
                for (int depth = 0; depth < 24; depth++)
                {
                    Entry entry; if (!tree.TryGetValue(pid, out entry)) break;
                    int parent = entry.Parent;
                    Entry up; if (parent == 0 || parent == pid || !tree.TryGetValue(parent, out up) || StopsWalk(up.Exe)) break;
                    IntPtr window = MainWindow(parent);
                    if (window != IntPtr.Zero) { owner.WindowPid = parent; owner.Window = window.ToInt64(); break; }
                    pid = parent;
                }
            }
            catch (Exception) { }
            return owner;
        }

        // Whether the session's Claude Code still runs. A reused process id belongs to another program, so the name counts too.
        public static bool Alive(int pid)
        {
            if (pid <= 0) return true;
            try
            {
                using (var process = Process.GetProcessById(pid))
                {
                    if (process.HasExited) return false;
                    string name = process.ProcessName + ".exe";
                    return IsClaude(name);
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (Exception) { return true; }
        }

        // The session's window, if it is still there: the recorded one, or any main window of the recorded process.
        public static IntPtr Window(long window, int windowPid)
        {
            var handle = new IntPtr(window);
            if (handle != IntPtr.Zero && IsWindow(handle))
            {
                uint process; GetWindowThreadProcessId(handle, out process);
                if (windowPid <= 0 || process == windowPid) return handle;
            }
            return windowPid > 0 ? MainWindow(windowPid) : IntPtr.Zero;
        }

        // Brings the window forward: restores it if minimized, then asks Windows to put it in front. Windows lets only
        // the foreground app move the foreground, so the dashboard (which never takes the focus itself) briefly joins
        // the foreground window's input first. Returns whether the window ended up in front.
        public static bool Focus(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window)) return false;
            if (IsIconic(window)) ShowWindow(window, 9);
            IntPtr foreground = GetForegroundWindow();
            uint ignored; uint foregroundThread = GetWindowThreadProcessId(foreground, out ignored), current = GetCurrentThreadId();
            bool attached = foregroundThread != 0 && foregroundThread != current && AttachThreadInput(current, foregroundThread, true);
            try { BringWindowToTop(window); SetForegroundWindow(window); }
            finally { if (attached) AttachThreadInput(current, foregroundThread, false); }
            if (GetForegroundWindow() == window) return true;
            // Still refused: a tap of Alt lets the next foreground change through.
            keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(window);
            return GetForegroundWindow() == window;
        }

        // A visible, unowned top-level window with a size, the kind the taskbar shows.
        private static IntPtr MainWindow(int pid)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((window, data) => {
                uint process; GetWindowThreadProcessId(window, out process);
                if (process != pid || !IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
                RECT r; if (!GetWindowRect(window, out r) || r.Right - r.Left < 40 || r.Bottom - r.Top < 40) return true;
                if (GetWindowTextLength(window) == 0) return true;
                found = window; return false;
            }, IntPtr.Zero);
            return found;
        }

        private struct Entry { public int Parent; public string Exe; }
        private static Dictionary<int, Entry> Snapshot()
        {
            var result = new Dictionary<int, Entry>();
            IntPtr snapshot = CreateToolhelp32Snapshot(2, 0);
            if (snapshot == new IntPtr(-1)) return result;
            try
            {
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32)) };
                if (!Process32First(snapshot, ref entry)) return result;
                do { result[(int)entry.th32ProcessID] = new Entry { Parent = (int)entry.th32ParentProcessID, Exe = entry.szExeFile }; }
                while (Process32Next(snapshot, ref entry));
            }
            finally { CloseHandle(snapshot); }
            return result;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
        }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        private delegate bool EnumWindowsProc(IntPtr window, IntPtr data);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint process);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr data);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint to, bool on);
        [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    }
}
