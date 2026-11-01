using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Storage widget (docs/design/storage.md): free space on the fixed drives, read off the UI thread once a minute
    // while the card is shown (two in Quiet mode). A phone shares the same reading, so it never makes the PC read the
    // drives more often than the dashboard does.
    public sealed partial class DashboardController
    {
        private readonly StorageService storage = new StorageService();
        private StorageCard storageCard;
        private Task<List<StorageDrive>> storageRead;
        private List<StorageDrive> lastStorage;
        private DateTime lastStorageAt = DateTime.MinValue, lastStorageAttempt = DateTime.MinValue;
        private bool storageBusy;
        // The card, for the checks (tests/Storage/StorageTests.cs).
        public StorageCard StorageView { get { return storageCard; } }

        private void InitializeStorage()
        {
            storageCard = new StorageCard(window, Control<Grid>("StorageBody"), Control<TextBlock>("StorageStatus"));
            var choice = Control<Slider>("StorageLowChoice"); TouchSlider.Attach(choice);
            choice.ValueChanged += (sender, e) => Text("StorageLowLabel", Math.Round(e.NewValue) + "% free");
        }

        private TimeSpan StorageInterval { get { return settings.QuietMode ? StorageService.QuietInterval : StorageService.Interval; } }

        // The heartbeat reads the drives again once the interval has passed, and only while the card is shown.
        private void TickStorage()
        {
            if (Editor.IsVisible("Storage") && DateTime.UtcNow - lastStorageAttempt >= StorageInterval) RefreshStorage(true);
        }

        private async void RefreshStorage(bool fresh = false)
        {
            if (disposed || storageBusy || !window.IsLoaded || !Editor.IsVisible("Storage")) return;
            storageBusy = true; lastStorageAttempt = DateTime.UtcNow;
            try
            {
                var drives = await ReadStorageAsync(fresh);
                if (!disposed) storageCard.Update(drives);
            }
            catch (Exception) { if (!disposed) storageCard.Unavailable(); }
            finally { storageBusy = false; }
        }

        // The last reading, shared with a phone for the dashboard's interval; fresh reads the drives again.
        private Task<List<StorageDrive>> ReadStorageAsync(bool fresh)
        {
            if (!fresh && lastStorage != null && DateTime.UtcNow - lastStorageAt < StorageInterval) return Task.FromResult(lastStorage);
            if (storageRead != null && !storageRead.IsCompleted) return storageRead;
            storageRead = StoreStorage(Task.Run(() => storage.Read()));
            return storageRead;
        }
        private async Task<List<StorageDrive>> StoreStorage(Task<List<StorageDrive>> read)
        {
            try { var drives = await read; lastStorage = drives; lastStorageAt = DateTime.UtcNow; return drives; }
            catch (Exception) { lastStorage = null; throw; }
        }

        // The phone's storage section: the threshold and each drive's sizes; the phone works out what's low the same way.
        private async Task<Dictionary<string, object>> RemoteStorage()
        {
            try
            {
                var drives = await ReadStorageAsync(false);
                return D("lowPercent", settings.StorageLowPercent, "drives", drives.Select(d => (object)D("name", d.Name, "label", d.Label, "total", (double)d.TotalBytes, "free", (double)d.FreeBytes)).ToArray());
            }
            catch (Exception) { return D("error", true, "lowPercent", settings.StorageLowPercent); }
        }
    }
}
