using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace WidgetScreen
{
    // Starts what Quick Launch, macros and Games open. Normally that's Process.Start. In full-sensors mode the app runs
    // with administrator rights, and anything it started would inherit them, so it asks the desktop's own shell
    // (Explorer, which runs without them) to start it instead: the documented "launch unelevated from an elevated
    // process" route through the desktop folder view's IShellDispatch2.ShellExecute. If that fails it falls back to
    // Process.Start, which starts the program elevated (decision 016 records the tradeoff).
    public static class ProcessLauncher
    {
        public static void Start(string file, string arguments, string directory, bool hidden = false)
        {
            if (HardwareSensors.Elevated && StartThroughShell(file, arguments, directory, hidden)) return;
            // Null when the shell handed it to a program that was already running (a browser, a store app).
            var started = Process.Start(new ProcessStartInfo(file) { Arguments = arguments ?? "", WorkingDirectory = directory ?? "", UseShellExecute = true, WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal });
            if (started != null) started.Dispose();
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider { [return: MarshalAs(UnmanagedType.IUnknown)] object QueryService(ref Guid service, ref Guid riid); }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            void GetWindow(); void ContextSensitiveHelp(); void InsertMenusSB(); void SetMenuSB(); void RemoveMenusSB(); void SetStatusTextSB();
            void EnableModelessSB(); void TranslateAcceleratorSB(); void BrowseObject(); void GetViewStateStream(); void GetControlWindow(); void SendControlMsg();
            IShellView QueryActiveShellView();
        }

        [ComImport, Guid("000214E3-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellView
        {
            void GetWindow(); void ContextSensitiveHelp(); void TranslateAccelerator(); void EnableModeless(); void UIActivate(); void Refresh(); void CreateViewWindow();
            void DestroyViewWindow(); void GetCurrentInfo(); void AddPropertySheetPages(); void SaveViewState(); void SelectItem();
            [return: MarshalAs(UnmanagedType.IUnknown)] object GetItemObject(uint item, ref Guid riid);
        }

        public static bool StartThroughShell(string file, string arguments, string directory, bool hidden)
        {
            object windows = null;
            try
            {
                windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")));
                // FindWindowSW(CSIDL_DESKTOP, empty, SWC_DESKTOP, out hwnd, SWFO_NEEDDISPATCH)
                var args = new object[] { 0, null, 8, 0, 1 };
                var byRef = new ParameterModifier(5); byRef[3] = true;
                object desktop = windows.GetType().InvokeMember("FindWindowSW", BindingFlags.InvokeMethod, null, windows, args, new[] { byRef }, null, null);
                if (desktop == null) return false;
                Guid topLevel = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"), browserId = typeof(IShellBrowser).GUID, dispatch = new Guid("00020400-0000-0000-C000-000000000046");
                var browser = (IShellBrowser)((IServiceProvider)desktop).QueryService(ref topLevel, ref browserId);
                object view = browser.QueryActiveShellView().GetItemObject(0, ref dispatch); // SVGIO_BACKGROUND
                object application = view.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, view, null);
                application.GetType().InvokeMember("ShellExecute", BindingFlags.InvokeMethod, null, application, new object[] { file, arguments ?? "", directory ?? "", "open", hidden ? 0 : 1 });
                return true;
            }
            catch (Exception) { return false; }
            finally { if (windows != null) Marshal.ReleaseComObject(windows); }
        }
    }
}
