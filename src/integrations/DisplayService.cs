using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace WidgetScreen
{
    public static class DisplayService
    {
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
        public static Forms.Screen[] Screens { get { return Forms.Screen.AllScreens; } }
        public static Forms.Screen Secondary(string preferred)
        {
            return Screens.FirstOrDefault(s => !s.Primary && s.DeviceName == preferred) ?? Screens.Where(s => !s.Primary).OrderBy(s => Math.Abs(s.Bounds.Width - 1920) + Math.Abs(s.Bounds.Height - 720)).FirstOrDefault();
        }
        public static bool Fullscreen(Window window, string preferred)
        {
            var screen = Secondary(preferred);
            if (screen == null) return false;
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.None;
            window.ResizeMode = ResizeMode.NoResize;
            var bounds = screen.Bounds;
            return SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0040 | 0x0010 | 0x0004);
        }
        public static void Windowed(Window window)
        {
            window.WindowState = WindowState.Normal;
            window.WindowStyle = WindowStyle.SingleBorderWindow;
            window.ResizeMode = ResizeMode.CanResize;
            var bounds = Forms.Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(1536, (int)(bounds.Width * .90));
            int height = Math.Min(660, (int)(bounds.Height * .88));
            SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height, 0x0040 | 0x0010 | 0x0004);
        }
    }
}
