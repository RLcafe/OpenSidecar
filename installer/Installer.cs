using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace WidgetScreenSetup
{
    // Everything setup does to the PC, with no UI: copy the app, make the shortcuts, register the
    // Installed apps entry, and undo all of it. Installs for the signed-in user only (no admin rights).
    // With a sandbox root, every path and the registry key move under that root, for the automated checks.
    public sealed class Installer
    {
        public const string AppName = "Widget Screen", AppExe = "WidgetScreen.exe", UninstallerExe = "Uninstall Widget Screen.exe", Publisher = "Ali Alyami";
        public readonly string InstallDir, StartMenuShortcut, StartupShortcut, SettingsDir, RegistryPath, Root;

        public Installer(string sandboxRoot)
        {
            Root = string.IsNullOrEmpty(sandboxRoot) ? null : Path.GetFullPath(sandboxRoot);
            if (Root == null)
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                InstallDir = Path.Combine(Path.Combine(local, "Programs"), AppName);
                StartMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk");
                StartupShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppName + ".lnk");
                SettingsDir = Path.Combine(local, "WidgetScreen");
                RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WidgetScreen";
            }
            else
            {
                InstallDir = Path.Combine(Path.Combine(Root, "Programs"), AppName);
                StartMenuShortcut = Path.Combine(Path.Combine(Root, "StartMenu"), AppName + ".lnk");
                StartupShortcut = Path.Combine(Path.Combine(Root, "Startup"), AppName + ".lnk");
                SettingsDir = Path.Combine(Root, "Settings");
                RegistryPath = @"Software\WidgetScreen.SetupTests\Uninstall\WidgetScreen";
            }
        }

        public string AppPath { get { return Path.Combine(InstallDir, AppExe); } }
        public string UninstallerPath { get { return Path.Combine(InstallDir, UninstallerExe); } }

        // The installed version, or null when Widget Screen isn't installed.
        public string InstalledVersion()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath))
            {
                if (key == null || !File.Exists(AppPath)) return null;
                return key.GetValue("DisplayVersion") as string ?? "";
            }
        }

        public bool StartsWithWindows() { return File.Exists(StartupShortcut); }

        // Copies of Widget Screen started from the install folder (other builds are left alone).
        public Process[] RunningCopies()
        {
            return Process.GetProcessesByName("WidgetScreen").Where(p => IsInside(PathOf(p), InstallDir)).ToArray();
        }

        public static bool AnyCopyRunning() { return Process.GetProcessesByName("WidgetScreen").Length > 0; }

        // Other shortcuts in the Startup folders that also open Widget Screen, so it would open twice.
        public string[] OtherStartupShortcuts()
        {
            if (Root != null) return new string[0];
            var found = new List<string>();
            foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) })
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                foreach (var link in Directory.GetFiles(folder, "*.lnk"))
                {
                    if (string.Equals(link, StartupShortcut, StringComparison.OrdinalIgnoreCase)) continue;
                    string target = ShortcutTarget(link);
                    if (target != null && target.EndsWith("\\" + AppExe, StringComparison.OrdinalIgnoreCase)) found.Add(link);
                }
            }
            return found.ToArray();
        }

        public void Install(bool startWithWindows, Action<string> progress)
        {
            Report(progress, "Closing Widget Screen…");
            CloseRunningCopies();
            Report(progress, "Copying files…");
            Directory.CreateDirectory(InstallDir);
            var assembly = Assembly.GetExecutingAssembly();
            long bytes = 0;
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("payload/", StringComparison.Ordinal)))
            {
                string target = Path.Combine(InstallDir, name.Substring("payload/".Length));
                Retry(() =>
                {
                    using (var source = assembly.GetManifestResourceStream(name))
                    using (var file = File.Create(target)) source.CopyTo(file);
                });
                bytes += new FileInfo(target).Length;
            }
            if (!File.Exists(AppPath)) throw new InvalidOperationException("This setup file is incomplete: it has no " + AppExe + ".");
            string self = Path.GetFullPath(Assembly.GetEntryAssembly().Location);
            if (!string.Equals(self, Path.GetFullPath(UninstallerPath), StringComparison.OrdinalIgnoreCase))
                Retry(() => File.Copy(self, UninstallerPath, true));
            bytes += new FileInfo(UninstallerPath).Length;

            Report(progress, "Adding shortcuts…");
            CreateShortcut(StartMenuShortcut, AppPath, InstallDir, "Your second-screen dashboard");
            if (startWithWindows) CreateShortcut(StartupShortcut, AppPath, InstallDir, "Starts Widget Screen when you sign in");
            else DeleteFile(StartupShortcut);

            using (var key = Registry.CurrentUser.CreateSubKey(RegistryPath))
            {
                string uninstall = Quote(UninstallerPath) + " /uninstall" + (Root == null ? "" : " --root " + Quote(Root));
                key.SetValue("DisplayName", AppName);
                key.SetValue("DisplayVersion", SetupInfo.Version);
                key.SetValue("Publisher", Publisher);
                key.SetValue("DisplayIcon", AppPath + ",0");
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                key.SetValue("UninstallString", uninstall);
                key.SetValue("QuietUninstallString", uninstall + " /S");
                key.SetValue("EstimatedSize", (int)Math.Max(1, bytes / 1024), RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        // Removes the app, its shortcuts and its Installed apps entry. Settings, sign-ins and paired phones
        // stay unless removeSettings. When this runs from the install folder, the folder goes a moment later.
        public void Uninstall(bool removeSettings, Action<string> progress)
        {
            Environment.CurrentDirectory = Path.GetTempPath();
            Report(progress, "Closing Widget Screen…");
            CloseRunningCopies();
            Report(progress, "Removing Widget Screen…");
            DeleteFile(StartMenuShortcut);
            DeleteFile(StartupShortcut);
            Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, false);
            if (Directory.Exists(InstallDir))
            {
                string self = Path.GetFullPath(Assembly.GetEntryAssembly().Location);
                if (IsInside(self, InstallDir))
                {
                    foreach (var file in Directory.GetFiles(InstallDir, "*", SearchOption.AllDirectories))
                        if (!string.Equals(Path.GetFullPath(file), self, StringComparison.OrdinalIgnoreCase)) DeleteFile(file);
                    // This exe can't delete itself while it runs: a hidden command removes the folder after it exits.
                    var cleanup = new ProcessStartInfo("cmd.exe", "/d /c ping -n 3 127.0.0.1 >nul & rd /s /q " + Quote(InstallDir))
                    { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath(), WindowStyle = ProcessWindowStyle.Hidden };
                    Process.Start(cleanup);
                }
                else Retry(() => Directory.Delete(InstallDir, true));
            }
            if (removeSettings && Directory.Exists(SettingsDir)) Retry(() => Directory.Delete(SettingsDir, true));
        }

        public void Launch()
        {
            if (AnyCopyRunning() || !File.Exists(AppPath)) return;
            Process.Start(new ProcessStartInfo(AppPath) { WorkingDirectory = InstallDir, UseShellExecute = true });
        }

        // Asks each copy to close (it saves on the way out), then ends any that don't within a few seconds:
        // a copy hidden in the tray has no window to close.
        void CloseRunningCopies()
        {
            foreach (var process in RunningCopies())
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && process.CloseMainWindow() && process.WaitForExit(6000)) continue;
                    if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        static void Report(Action<string> progress, string text) { if (progress != null) progress(text); }

        static string PathOf(Process process) { try { return process.MainModule.FileName; } catch { return null; } }

        static bool IsInside(string path, string folder)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string full = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return Path.GetFullPath(path).StartsWith(full, StringComparison.OrdinalIgnoreCase);
        }

        static string Quote(string value) { return "\"" + value + "\""; }

        static void DeleteFile(string path) { if (File.Exists(path)) Retry(() => File.Delete(path)); }

        // A file that was just in use (a closing app, an antivirus scan) is usually free again within a second or two.
        static void Retry(Action action)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { action(); return; }
                catch (IOException) { if (attempt >= 12) throw; }
                catch (UnauthorizedAccessException) { if (attempt >= 12) throw; }
                Thread.Sleep(250);
            }
        }

        static void CreateShortcut(string path, string target, string workingDirectory, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Shell(shell =>
            {
                object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                try
                {
                    var type = link.GetType();
                    type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                    type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workingDirectory });
                    type.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description });
                    type.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                    type.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                }
                finally { Marshal.ReleaseComObject(link); }
                return null;
            });
        }

        public static string ShortcutTarget(string path)
        {
            try
            {
                return (string)Shell(shell =>
                {
                    object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
                    try { return link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null); }
                    finally { Marshal.ReleaseComObject(link); }
                });
            }
            catch { return null; }
        }

        static object Shell(Func<object, object> use)
        {
            object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            try { return use(shell); } finally { Marshal.ReleaseComObject(shell); }
        }
    }
}
