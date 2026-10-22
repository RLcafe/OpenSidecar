using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace WidgetScreen
{
    // Foreground events retain the last external app without a polling timer.
    public sealed class DeckHotkeys : IDisposable
    {
        private readonly WinEvent callback;
        private IntPtr hook, previous;
        private uint previousProcess;
        private readonly uint ownProcess = (uint)Process.GetCurrentProcess().Id;
        private bool disposed, sending;
        public DeckHotkeys()
        {
            callback = (h, e, w, o, c, t, ms) => Remember(w);
            Remember(GetForegroundWindow());
            hook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 2);
        }
        private void Remember(IntPtr handle)
        {
            uint process; GetWindowThreadProcessId(handle, out process);
            if (handle != IntPtr.Zero && process != ownProcess && IsWindowVisible(handle)) { previous = handle; previousProcess = process; }
        }
        // The app a hotkey would go to right now (its process name), or null while there is none.
        private uint namedProcess; private string namedName;
        public string TargetName
        {
            get
            {
                IntPtr target = previous; uint process; GetWindowThreadProcessId(target, out process);
                if (target == IntPtr.Zero || process == 0 || process != previousProcess || !IsWindow(target)) return null;
                if (process != namedProcess)
                {
                    namedProcess = process;
                    try { using (var owner = Process.GetProcessById((int)process)) namedName = owner.ProcessName; } catch (Exception) { namedName = null; }
                }
                return namedName;
            }
        }
        public static bool TryParse(string text, out ushort[] keys)
        {
            keys = null;
            var result = new List<ushort>();
            var parts = (text ?? "").Split('+').Select(p => p.Trim()).ToArray();
            if (parts.Length < 1 || parts.Length > 5) return false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                ushort modifier = parts[i].Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ? (ushort)0x11 : parts[i].Equals("Alt", StringComparison.OrdinalIgnoreCase) ? (ushort)0x12 : parts[i].Equals("Shift", StringComparison.OrdinalIgnoreCase) ? (ushort)0x10 : parts[i].Equals("Win", StringComparison.OrdinalIgnoreCase) ? (ushort)0x5B : (ushort)0;
                if (modifier == 0 || result.Contains(modifier)) return false; result.Add(modifier);
            }
            string last = parts[parts.Length - 1];
            if (last.Length == 1 && Char.IsDigit(last[0])) last = "D" + last;
            Key key;
            if (!Enum.TryParse<Key>(last, true, out key) || !Enum.IsDefined(typeof(Key), key) || key == Key.None) return false;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            if (vk < 8 || vk > 254 || new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x5D, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 }.Contains(vk)) return false;
            // Windows reserves the secure-attention sequence.
            if (vk == 0x2E && result.Contains(0x11) && result.Contains(0x12)) return false;
            result.Add((ushort)vk); keys = result.ToArray(); return true;
        }
        public async Task SendAsync(string text)
        {
            ushort[] keys; if (!TryParse(text, out keys)) throw new InvalidOperationException("Invalid keyboard shortcut.");
            if (sending || disposed) throw new InvalidOperationException("Wait for the previous shortcut to finish.");
            IntPtr target = previous; uint process; GetWindowThreadProcessId(target, out process);
            if (target == IntPtr.Zero || !IsWindow(target) || process != previousProcess) throw new InvalidOperationException("Select the app you want to control first, then tap this button.");
            sending = true;
            try
            {
                if (IsIconic(target)) ShowWindow(target, 9);
                if (!SetForegroundWindow(target)) throw new InvalidOperationException("Windows could not focus the previous app. Select it and try again.");
                await Task.Delay(100);
                if (disposed) return;
                if (GetForegroundWindow() != target) throw new InvalidOperationException("The active app changed; the shortcut was not sent.");
                if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0)) throw new InvalidOperationException("Release the keyboard modifiers, then try again.");
                var inputs = keys.Select(k => Input(k, false)).Concat(keys.Reverse().Select(k => Input(k, true))).ToArray();
                if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT))) != inputs.Length)
                {
                    var releases = keys.Reverse().Select(k => Input(k, true)).ToArray(); SendInput((uint)releases.Length, releases, Marshal.SizeOf(typeof(INPUT)));
                    throw new InvalidOperationException("Windows blocked the shortcut. Elevated apps and some games do not accept it.");
                }
            }
            finally { sending = false; }
        }
        private static INPUT Input(ushort key, bool up)
        {
            bool extended = (key >= 0x21 && key <= 0x2E) || key == 0x5B || key == 0x5C || key == 0x6F || (key >= 0xA6 && key <= 0xB7);
            return new INPUT { Type = 1, Key = key, Flags = (up ? 2u : 0u) | (extended ? 1u : 0u) };
        }
        public void Dispose() { disposed = true; if (hook != IntPtr.Zero) UnhookWinEvent(hook); hook = IntPtr.Zero; GC.KeepAlive(callback); }
        // The app builds x64 only. INPUT's keyboard union starts at byte 8.
        [StructLayout(LayoutKind.Explicit, Size = 40)] private struct INPUT
        { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(12)] public uint Flags; }
        private delegate void WinEvent(IntPtr hook, uint type, IntPtr window, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    }
}
