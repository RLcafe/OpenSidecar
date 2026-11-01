using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace WidgetScreen
{
    // ClaudeHook.exe, the bridge between Claude Code and the dashboard (docs/design/claude-watcher.md, decision 014).
    // Claude Code runs "ClaudeHook.exe hook" for its hooks and "ClaudeHook.exe statusline" for its status line, with JSON
    // on stdin. The files go into the folder this exe sits in (%LOCALAPPDATA%\WidgetScreen\claude once connected), so
    // the dashboard doesn't have to be running. It never blocks Claude Code and always exits 0.
    public static class ClaudeHook
    {
        public static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "";
            string input = "";
            try { using (var reader = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false))) input = reader.ReadToEnd(); }
            catch (Exception) { }
            string folder = AppDomain.CurrentDomain.BaseDirectory;
            Func<ClaudeProcesses.Owner> owner = () => ClaudeProcesses.Find(Process.GetCurrentProcess().Id);
            if (mode == "hook")
            {
                try { ClaudeBridge.Hook(input, folder, owner, DateTime.UtcNow); } catch (Exception) { }
                return 0;
            }
            if (mode == "statusline")
            {
                string line = "Claude Code";
                try { line = ClaudeBridge.StatusLine(input, folder, owner, DateTime.UtcNow); } catch (Exception) { }
                try { if (RunPrevious(folder, input)) return 0; } catch (Exception) { }
                Write(line + "\n");
                return 0;
            }
            Write("ClaudeHook.exe is Widget Screen's bridge for Claude Code. Connect it in Widget Screen: Settings → Claude.\n");
            return 0;
        }

        private static void Write(string text)
        {
            try { var bytes = new UTF8Encoding(false).GetBytes(text); using (var stdout = Console.OpenStandardOutput()) stdout.Write(bytes, 0, bytes.Length); }
            catch (Exception) { }
        }

        // The status line the user had before connecting keeps showing: it runs with the same input, and what it prints
        // is passed on unchanged. Claude Code runs commands through Git Bash on Windows, so this does too when it can.
        private static bool RunPrevious(string folder, string input)
        {
            string path = ClaudeBridge.PreviousStatusLinePath(folder);
            if (!File.Exists(path)) return false;
            string command = ClaudeJson.Str(ClaudeJson.Obj(ClaudeJson.Parse(File.ReadAllText(path, Encoding.UTF8))), "command");
            if (String.IsNullOrWhiteSpace(command)) return false;
            var start = Shell(command);
            start.UseShellExecute = false; start.CreateNoWindow = true;
            start.RedirectStandardInput = true; start.RedirectStandardOutput = true; start.RedirectStandardError = true;
            using (var process = Process.Start(start))
            {
                var output = new MemoryStream();
                var copy = process.StandardOutput.BaseStream.CopyToAsync(output);
                var errors = process.StandardError.ReadToEndAsync();
                try { var bytes = new UTF8Encoding(false).GetBytes(input); process.StandardInput.BaseStream.Write(bytes, 0, bytes.Length); process.StandardInput.Close(); }
                catch (IOException) { }
                if (!process.WaitForExit(5000)) { try { process.Kill(); } catch (Exception) { } }
                Task.WaitAll(new Task[] { copy, errors }, 1000);
                using (var stdout = Console.OpenStandardOutput()) { var data = output.ToArray(); stdout.Write(data, 0, data.Length); }
            }
            return true;
        }

        private static ProcessStartInfo Shell(string command)
        {
            string bash = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
            if (String.IsNullOrWhiteSpace(bash) || !File.Exists(bash))
            {
                bash = null;
                foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramW6432"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs") })
                {
                    if (String.IsNullOrEmpty(root)) continue;
                    string candidate = Path.Combine(root, "Git", "bin", "bash.exe");
                    if (File.Exists(candidate)) { bash = candidate; break; }
                }
            }
            if (bash != null) return new ProcessStartInfo(bash) { Arguments = "-c " + Quote(command) };
            return new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { Arguments = "/d /s /c \"" + command + "\"" };
        }

        // One argument in the usual Windows command-line quoting: backslashes stay as they are unless a quote follows.
        public static string Quote(string argument)
        {
            var b = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append('"'); }
                else { b.Append('\\', slashes); b.Append(c); }
                slashes = 0;
            }
            b.Append('\\', slashes * 2);
            return b.Append('"').ToString();
        }
    }
}
