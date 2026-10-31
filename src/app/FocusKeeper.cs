using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;

namespace WidgetScreen
{
    // Keeps the game or app on the main screen focused while the dashboard is tapped (decision 013). The window
    // carries WS_EX_NOACTIVATE and answers mouse and touch activation with "don't", so buttons and sliders work
    // without Windows moving the foreground. A tap on a text field is the one exception: typing needs the keyboard,
    // so the window becomes an ordinary one until something else is activated, then goes back.
    public sealed class FocusKeeper
    {
        private const int GWL_EXSTYLE = -20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_APPWINDOW = 0x00040000;
        private const int WM_MOUSEACTIVATE = 0x0021, WM_STYLECHANGING = 0x007C, WM_POINTERACTIVATE = 0x024B, MA_NOACTIVATE = 3, PA_NOACTIVATE = 3;
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr window, int index, int value);

        private readonly Window window;
        private IntPtr handle;
        private bool enabled, typing;

        public FocusKeeper(Window window)
        {
            this.window = window;
            window.SourceInitialized += (sender, e) => Attach();
            if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Attach();
            window.PreviewMouseDown += (sender, e) => Pressed(e.OriginalSource as DependencyObject);
            window.PreviewStylusDown += (sender, e) => Pressed(e.OriginalSource as DependencyObject);
            window.PreviewTouchDown += (sender, e) => Pressed(e.OriginalSource as DependencyObject);
            window.Deactivated += (sender, e) => { if (typing) { typing = false; Apply(); } };
            window.IsVisibleChanged += (sender, e) => Apply();
        }

        // Settings → Display → "Keep the focus on the game or app I'm using". Also decides whether showing the window
        // (at launch, or back from the tray) activates it.
        public bool Enabled
        {
            get { return enabled; }
            set { enabled = value; if (!value) typing = false; window.ShowActivated = !value; Apply(); }
        }
        // True while a tap leaves the foreground where it is: on, and not typing in one of the dashboard's fields.
        public bool Holding { get { return enabled && !typing; } }

        // A text box, password box or editable dropdown: the controls that need the keyboard.
        public static bool IsTextField(DependencyObject element)
        {
            while (element != null)
            {
                if (element is TextBoxBase || element is PasswordBox) return true;
                var combo = element as ComboBox; if (combo != null && combo.IsEditable) return true;
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return false;
        }

        private void Pressed(DependencyObject source)
        {
            if (!Holding || !IsTextField(source)) return;
            // The press is this app's input, so Windows lets it take the foreground for the keyboard.
            typing = true; Apply();
            if (window.IsVisible) window.Activate();
        }

        private void Attach()
        {
            if (handle != IntPtr.Zero) return;
            handle = new WindowInteropHelper(window).Handle;
            var source = HwndSource.FromHwnd(handle); if (source != null) source.AddHook(Hook);
            Apply();
        }

        private void Apply()
        {
            if (handle == IntPtr.Zero) return;
            int style = GetWindowLong(handle, GWL_EXSTYLE), next = Styled(style);
            if (next != style) SetWindowLong(handle, GWL_EXSTYLE, next);
        }

        // A no-activate window drops off the taskbar unless it also says it's an app window.
        private int Styled(int style)
        {
            if (!Holding) return style & ~WS_EX_NOACTIVATE;
            style |= WS_EX_NOACTIVATE;
            return window.ShowInTaskbar ? style | WS_EX_APPWINDOW : style;
        }

        private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (!Holding) return IntPtr.Zero;
            if (message == WM_MOUSEACTIVATE) { handled = true; return new IntPtr(MA_NOACTIVATE); }
            if (message == WM_POINTERACTIVATE) { handled = true; return new IntPtr(PA_NOACTIVATE); }
            if (message == WM_STYLECHANGING && wParam.ToInt64() == GWL_EXSTYLE)
            {
                // WPF rewrites the extended style when the window changes (fullscreen, the tray); keep ours in it.
                int requested = Marshal.ReadInt32(lParam, 4), styled = Styled(requested);
                if (styled != requested) Marshal.WriteInt32(lParam, 4, styled);
            }
            return IntPtr.Zero;
        }
    }
}
