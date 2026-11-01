using System;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WidgetScreen
{
    // The processor's and graphics card's names for the System stats card (Sticker Sheet phase 3,
    // docs/design/sticker-sheet.md): read once, and shortened to what a person calls them ("Ryzen 7 7800X3D", "RTX 4070").
    public sealed partial class SystemService
    {
        private static string cpuModel;
        private static bool cpuModelRead;
        public static string CpuModel
        {
            get
            {
                if (cpuModelRead) return cpuModel;
                cpuModelRead = true;
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                        cpuModel = key == null ? null : (key.GetValue("ProcessorNameString") as string ?? "").Trim();
                }
                catch (Exception) { cpuModel = null; }
                return cpuModel;
            }
        }

        // "AMD Ryzen 7 7800X3D 8-Core Processor" → "Ryzen 7 7800X3D"; "Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz" →
        // "i7-12700K"; "NVIDIA GeForce RTX 4070" → "RTX 4070"; "AMD Radeon RX 7900 XTX" → "Radeon RX 7900 XTX".
        // The phone's core/SystemText.shortModel does the same.
        public static string ShortModel(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return "";
            string s = name.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "").Replace("(r)", "");
            s = Regex.Replace(s, @"\s*@\s*[\d.]+\s*GHz", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b\d+-Core\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(Processor|CPU|with Radeon Graphics|Graphics)\b", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^\s*(AMD|Intel|NVIDIA)\s+", "", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"^\s*(Core|GeForce)\s+", "", RegexOptions.IgnoreCase);
            return Regex.Replace(s, @"\s+", " ").Trim();
        }
    }
}
