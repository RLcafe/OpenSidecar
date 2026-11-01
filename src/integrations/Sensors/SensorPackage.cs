using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace WidgetScreen
{
    // The optional hardware-sensor files (decision 016; third-party\LibreHardwareMonitor\README.md): LibreHardwareMonitor
    // 0.9.6 and the libraries it loads, in the "sensors" folder next to the exe when setup installed them. Each file is
    // read into memory and loaded only when its SHA-256 matches the pinned one, so a damaged or swapped file never runs.
    // Without the folder the app works as before and says the sensors aren't installed.
    public static class SensorPackage
    {
        public const string Version = "0.9.6", FolderName = "sensors", Library = "LibreHardwareMonitorLib";
        // The same list as third-party\LibreHardwareMonitor\SHA256SUMS.txt (tests\Sensors\SensorTests.cs keeps them equal).
        public static readonly KeyValuePair<string, string>[] Files = {
            Pin("LibreHardwareMonitorLib.dll", "6ebc194316536ba61af5be24508ad9fcbb2ecc685e716c12e787c79530f66bf0"),
            Pin("HidSharp.dll", "d86690efde30ea9179f669320f39148853793b743a98b531afeaf30598e22f54"),
            Pin("BlackSharp.Core.dll", "cafb93afcc8d8a367e21f619673d05c06887d8964867fed1371f02ded1cd3e23"),
            Pin("DiskInfoToolkit.dll", "1acbf51b3c10c51c986cf43021680d34a2e38d9a5ba652bcfa9a1b5f7fc09800"),
            Pin("RAMSPDToolkit-NDD.dll", "b6882354c7c8ec186617e421507743dbfae09c5c1fc24cef76a1d0c0c26651de"),
            Pin("System.Memory.dll", "d5e8e4866f9cfa66f7765660f84b210198893e55335487afe5ebda342c0e913d"),
            Pin("System.Buffers.dll", "2d78d770c9cb997199154ae8c018b9f1d1efbc86729f7264dde6dbad2a12cac3"),
            Pin("System.Numerics.Vectors.dll", "20c2fa81b8c70d651099d762954f285fd4f942e63b2d7217c145dab8d4b2f4c9"),
            Pin("System.Runtime.CompilerServices.Unsafe.dll", "08cbd7278b66f1e68425a82d4b97181a4130d93e3dd91831407aba7212ccdacf") };
        private static KeyValuePair<string, string> Pin(string name, string hash) { return new KeyValuePair<string, string>(name, hash); }

        public enum State { Installed, Missing, Damaged }

        public static string DefaultFolder { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FolderName); } }

        private static readonly object gate = new object();
        private static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static string loadFolder;
        private static bool resolving;

        // Whether every file is there with the pinned contents; damaged names the first one that isn't.
        public static State Check(string folder, out string damaged)
        {
            damaged = null;
            if (!Directory.Exists(folder) || Files.All(f => !File.Exists(Path.Combine(folder, f.Key)))) return State.Missing;
            foreach (var file in Files)
            {
                string path = Path.Combine(folder, file.Key);
                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); } catch (Exception) { damaged = file.Key; return State.Damaged; }
                if (Hash(bytes) != file.Value) { damaged = file.Key; return State.Damaged; }
            }
            return State.Installed;
        }

        public static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return String.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2"))); }

        // Loads LibreHardwareMonitorLib from the folder, with its libraries found the same checked way. Null (and the
        // reason) when a file is missing or doesn't match.
        public static Assembly Load(string folder, out string problem)
        {
            problem = null;
            lock (gate)
            {
                if (loadFolder != null && !String.Equals(loadFolder, folder, StringComparison.OrdinalIgnoreCase)) { problem = "The sensors were already loaded from another folder."; return null; }
                string damaged;
                var state = Check(folder, out damaged);
                if (state == State.Missing) { problem = "Hardware sensors aren't installed."; return null; }
                if (state == State.Damaged) { problem = damaged + " doesn't match the pinned file. Run setup again to repair the sensors."; return null; }
                loadFolder = folder;
                if (!resolving) { AppDomain.CurrentDomain.AssemblyResolve += Resolve; resolving = true; }
                return LoadChecked(Library);
            }
        }

        private static Assembly LoadChecked(string simpleName)
        {
            Assembly assembly;
            if (loaded.TryGetValue(simpleName, out assembly)) return assembly;
            var file = Files.FirstOrDefault(f => String.Equals(Path.GetFileNameWithoutExtension(f.Key), simpleName, StringComparison.OrdinalIgnoreCase));
            if (file.Key == null || loadFolder == null) return null;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(Path.Combine(loadFolder, file.Key)); } catch (Exception) { return null; }
            // The bytes that are hashed are the bytes that are loaded.
            if (Hash(bytes) != file.Value) return null;
            assembly = Assembly.Load(bytes);
            loaded[simpleName] = assembly;
            return assembly;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            lock (gate) { return LoadChecked(new AssemblyName(args.Name).Name); }
        }
    }
}
