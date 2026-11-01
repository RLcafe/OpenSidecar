using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Text;

namespace WidgetScreen
{
    // Full-sensors mode (decision 016, Windows only). The app itself stays asInvoker. Turning the mode on registers one
    // Task Scheduler task, "Widget Screen full sensors", that starts this exe with the highest rights the user has; it
    // has no trigger of its own. Whenever Widget Screen starts normally (Start menu, Startup, a shortcut) with the mode
    // on, it runs that task and closes, so the copy that stays is the elevated one, without a UAC prompt each time.
    // Registering and removing the task each ask once through UAC. The uninstaller removes it too.
    public static class FullSensors
    {
        public const string TaskName = "Widget Screen full sensors", Argument = "--full-sensors";

        // The task definition: no trigger (Widget Screen runs it), highest rights for this user, never stopped for
        // battery or time limits, normal priority, one copy at a time.
        public static string TaskXml(string exePath, string user)
        {
            string exe = SecurityElement.Escape(exePath), folder = SecurityElement.Escape(Path.GetDirectoryName(exePath)), who = SecurityElement.Escape(user);
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Author>Widget Screen</Author><Description>Starts Widget Screen with administrator rights so the System stats card can read CPU temperatures and frame rates (full-sensors mode). Turn it off in Widget Screen: Settings, Widgets, System stats.</Description></RegistrationInfo>\r\n" +
                "  <Triggers />\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + who + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" +
                "<AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>false</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" +
                "<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled>" +
                "<Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>5</Priority></Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>\"" + exe + "\"</Command><Arguments>" + Argument + "</Arguments><WorkingDirectory>" + folder + "</WorkingDirectory></Exec></Actions>\r\n" +
                "</Task>\r\n";
        }

        public static string CurrentUser { get { return Environment.UserDomainName + "\\" + Environment.UserName; } }

        // The exe the registered task starts, or null when there's no task (or it can't be read).
        public static string RegisteredExe()
        {
            string output;
            if (Run("/Query /TN \"" + TaskName + "\" /XML", out output) != 0) return null;
            int start = output.IndexOf("<Command>", StringComparison.Ordinal), end = output.IndexOf("</Command>", StringComparison.Ordinal);
            if (start < 0 || end < start) return null;
            return System.Net.WebUtility.HtmlDecode(output.Substring(start + 9, end - start - 9)).Trim().Trim('"');
        }
        public static bool Registered { get { return RegisteredExe() != null; } }
        public static bool PointsTo(string registered, string exePath) { return registered != null && String.Equals(Path.GetFullPath(registered), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase); }

        // Runs the task, which starts the elevated copy. False when there's no task or Windows refused.
        public static bool StartElevatedCopy() { string output; return Run("/Run /TN \"" + TaskName + "\"", out output) == 0; }

        // Registers the task for this exe, asking through UAC. Null on success, else what went wrong.
        public static string Register(string exePath)
        {
            string file = Path.Combine(Path.GetTempPath(), "WidgetScreen-full-sensors.xml");
            File.WriteAllText(file, TaskXml(exePath, CurrentUser), Encoding.Unicode);
            try { return Elevated("/Create /TN \"" + TaskName + "\" /XML \"" + file + "\" /F"); }
            finally { try { File.Delete(file); } catch (IOException) { } }
        }
        public static string Unregister() { return Registered ? Elevated("/Delete /TN \"" + TaskName + "\" /F") : null; }

        private static string Elevated(string arguments)
        {
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(SchTasks, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                {
                    if (!process.WaitForExit(60000)) return "Task Scheduler didn't answer.";
                    return process.ExitCode == 0 ? null : "Task Scheduler refused (" + process.ExitCode + ").";
                }
            }
            catch (Win32Exception error) { return error.NativeErrorCode == 1223 ? "Windows asked for permission and it wasn't given." : error.Message; }
        }

        private static string SchTasks { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"); } }

        private static int Run(string arguments, out string output)
        {
            output = "";
            try
            {
                using (var process = Process.Start(new ProcessStartInfo(SchTasks, arguments) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    var read = process.StandardOutput.ReadToEndAsync(); process.StandardError.ReadToEnd();
                    if (!process.WaitForExit(15000)) { try { process.Kill(); } catch (Exception) { } return -1; }
                    output = read.Result;
                    return process.ExitCode;
                }
            }
            catch (Exception) { return -1; }
        }
    }
}
