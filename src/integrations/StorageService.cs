using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WidgetScreen
{
    // One fixed drive's space (docs/design/storage.md).
    public sealed class StorageDrive
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }
        public double FreePercent { get { return TotalBytes <= 0 ? 0 : 100.0 * FreeBytes / TotalBytes; } }
        public double UsedPercent { get { return TotalBytes <= 0 ? 0 : 100 - FreePercent; } }
    }

    // Free and used space on the PC's fixed drives. Network, removable and optical drives are left out, and a drive
    // that isn't ready (a locked BitLocker volume, a card reader slot) is skipped. Reading can wait on a sleeping disk,
    // so the dashboard runs it off the UI thread and only once a minute while the card is shown.
    public sealed class StorageService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60), QuietInterval = TimeSpan.FromSeconds(120);

        public List<StorageDrive> Read()
        {
            var drives = new List<StorageDrive>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady || drive.TotalSize <= 0) continue;
                    drives.Add(new StorageDrive { Name = drive.Name.TrimEnd('\\'), Label = drive.VolumeLabel ?? "", TotalBytes = drive.TotalSize, FreeBytes = drive.TotalFreeSpace });
                }
                catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
            }
            return Order(drives);
        }

        // Drive letters in order, so C: comes first and the card doesn't reshuffle between readings.
        public static List<StorageDrive> Order(IEnumerable<StorageDrive> drives) { return drives.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList(); }

        // A drive is low when its free space is under the percentage chosen in Settings → Widgets.
        public static bool Low(StorageDrive drive, int percent) { return drive.TotalBytes > 0 && drive.FreePercent < percent; }
        public const int DefaultLowPercent = 10;
        public static int ClampPercent(int percent) { return Math.Max(1, Math.Min(50, percent)); }

        // Sizes the way File Explorer shows them (1024-based): "820 MB", "45.2 GB", "212 GB", "1.82 TB".
        public static string FormatBytes(double bytes)
        {
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (Double.IsNaN(bytes) || bytes < 0) bytes = 0;
            const double kb = 1024, mb = kb * 1024, gb = mb * 1024, tb = gb * 1024;
            if (bytes >= tb) return (bytes / tb).ToString(bytes / tb >= 100 ? "0" : bytes / tb >= 10 ? "0.0" : "0.00", culture) + " TB";
            if (bytes >= gb) return (bytes / gb).ToString(bytes / gb >= 100 ? "0" : "0.0", culture) + " GB";
            if (bytes >= mb) return (bytes / mb).ToString("0", culture) + " MB";
            return (bytes / kb).ToString("0", culture) + " KB";
        }

        // "C:  Windows", or the letter alone when the drive has no name.
        public static string Title(StorageDrive drive) { return String.IsNullOrWhiteSpace(drive.Label) ? drive.Name : drive.Name + "  " + drive.Label; }

        // The heading's flag: nothing, "C: LOW" or "2 DRIVES LOW".
        public static string LowFlag(IList<StorageDrive> drives, int percent)
        {
            var low = drives.Where(d => Low(d, percent)).ToList();
            return low.Count == 0 ? "" : low.Count == 1 ? low[0].Name + " LOW" : low.Count + " DRIVES LOW";
        }
    }
}
