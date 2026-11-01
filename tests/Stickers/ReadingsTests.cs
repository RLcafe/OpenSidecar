using System;
using System.Collections.Generic;
using System.Linq;
using WidgetScreen;

// Sticker Sheet phase 3, the readings (docs/design/sticker-sheet.md): System stats' model names, peak sticker,
// temperature badges and min/max lines; Weather, Storage, World clocks and Prayer as they land.
// The phone's checks (android/tests) assert the same values, so both apps read alike.
internal static class ReadingsTests
{
    private static void Assert(bool valid, string message) { if (!valid) throw new Exception(message); }

    public static void Run(string directory, Action<string, Action> check)
    {
        check("System stats: processor and graphics card names shorten to what a person calls them", () => {
            Assert(SystemService.ShortModel("AMD Ryzen 7 7800X3D 8-Core Processor") == "Ryzen 7 7800X3D", "Ryzen: " + SystemService.ShortModel("AMD Ryzen 7 7800X3D 8-Core Processor"));
            Assert(SystemService.ShortModel("Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz") == "i7-12700K", "Intel: " + SystemService.ShortModel("Intel(R) Core(TM) i7-12700K CPU @ 3.60GHz"));
            Assert(SystemService.ShortModel("NVIDIA GeForce RTX 4070") == "RTX 4070", "NVIDIA");
            Assert(SystemService.ShortModel("AMD Radeon RX 7900 XTX") == "Radeon RX 7900 XTX", "Radeon");
            Assert(SystemService.ShortModel("AMD Ryzen 5 5600G with Radeon Graphics") == "Ryzen 5 5600G", "APU");
            Assert(SystemService.ShortModel(null) == "" && SystemService.ShortModel("  ") == "", "empty");
        });
        check("System stats: the peak sticker names the busiest shown reading and fills from 80%", () => {
            var s = new SystemSnapshot { Cpu = 41, CpuAvailable = true, Gpu = 92, GpuName = "NVIDIA GeForce RTX 4070", CpuName = "AMD Ryzen 7 7700 8-Core Processor", MemoryAvailable = true, MemoryPercent = 55, UsedMemoryGB = 17.6, TotalMemoryGB = 32 };
            var readings = SystemReadings.From(s, false, 60);
            Assert(readings["Cpu"].Detail == "Ryzen 7 7700" && readings["Gpu"].Detail == "RTX 4070", "details " + readings["Cpu"].Detail + " / " + readings["Gpu"].Detail);
            string peak = SystemReadings.PeakId(readings, id => true);
            Assert(peak == "Gpu" && SystemReadings.PeakText(peak, readings[peak]) == "GPU 92%" && readings[peak].Percent >= SystemReadings.Strained, "peak " + peak);
            Assert(SystemReadings.PeakId(readings, id => id != "Gpu") == "Ram", "peak without GPU");
            Assert(SystemReadings.PeakId(readings, id => false) == null && SystemReadings.PeakText(null, null) == "", "no peak");
            var noGpu = SystemReadings.From(new SystemSnapshot { Cpu = 12, CpuAvailable = true, GpuStatus = "No GPU counters" }, false, 60);
            Assert(noGpu["Gpu"].Detail == "No GPU counters", "GPU status kept without a reading");
        });
        check("System stats: temperature badges and min/max lines read the same on both apps", () => {
            var hot = new SystemSnapshot { CpuTemperature = 58.4, CpuTemperatureState = "Working", GpuTemperature = 71, GpuTemperatureState = "Working" };
            var readings = SystemReadings.From(hot, false, 60);
            Assert(SystemReadings.Badge(readings["CpuTemp"]) == "58°" && SystemReadings.Badge(SystemReadings.From(hot, true, 60)["GpuTemp"]) == "160°", "badges");
            Assert(SystemReadings.Badge(SystemReadings.From(new SystemSnapshot { CpuTemperatureState = "NeedsPawnIO" }, false, 60)["CpuTemp"]) == "", "no badge without a reading");
            Assert(SystemReadings.Range("Cpu", new List<double> { 12.2, 87.4, 40 }, false) == "min 12% · max 87%", "percent range");
            Assert(SystemReadings.Range("CpuTemp", new List<double> { 41, 42.4 }, true) == "min 106° · max 108°", "temperature range " + SystemReadings.Range("CpuTemp", new List<double> { 41, 42.4 }, true));
            Assert(SystemReadings.Range("Fps", new List<double> { 58.6, 144 }, false) == "min 59 · max 144", "fps range");
            Assert(SystemReadings.Range("Ram", new List<double> { 50 }, false) == "" && SystemReadings.Range("Ram", null, false) == "", "too few readings");
        });
    }
}
