using System;
using System.Diagnostics;
using System.IO;

namespace WidgetScreen
{
    public static class Launcher
    {
        public static bool IsAllowed(string target)
        {
            if (String.IsNullOrWhiteSpace(target)) return false;
            target = Environment.ExpandEnvironmentVariables(target.Trim());
            Uri uri;
            if (Uri.TryCreate(target, UriKind.Absolute, out uri) && (uri.Scheme == "https" || uri.Scheme == "http")) return !String.IsNullOrEmpty(uri.Host);
            if (target.Equals("spotify:", StringComparison.OrdinalIgnoreCase) || target.Equals("ms-settings:sound", StringComparison.OrdinalIgnoreCase)) return true;
            return Path.IsPathRooted(target) && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target);
        }

        public static void Open(string target)
        {
            if (!IsAllowed(target)) throw new InvalidOperationException("Choose an existing .exe file, an http(s) website, spotify:, or ms-settings:sound.");
            ProcessLauncher.Start(Environment.ExpandEnvironmentVariables(target.Trim()), "", null);
        }
    }
}
