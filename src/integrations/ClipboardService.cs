using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;

namespace WidgetScreen
{
    // One copied text.
    public sealed class ClipItem
    {
        public string Id { get; set; }
        public string Text { get; set; }
        public DateTime AtUtc { get; set; }
    }

    // The last copied texts, newest first (docs/design/clipboard-history.md). Copying a text that's already there moves
    // it to the top instead of repeating it.
    public sealed class ClipboardHistory
    {
        public const int DefaultSize = 10, MinSize = 5, MaxSize = 50, MaxText = 20000;
        private readonly List<ClipItem> items = new List<ClipItem>();
        private int size = DefaultSize;
        public IList<ClipItem> Items { get { return items.AsReadOnly(); } }
        public int Size { get { return size; } set { size = ClampSize(value); Trim(); } }
        public static int ClampSize(int value) { return value == 0 ? DefaultSize : Math.Max(MinSize, Math.Min(MaxSize, value)); }

        public bool Add(string text, DateTime utc)
        {
            if (String.IsNullOrWhiteSpace(text)) return false;
            if (text.Length > MaxText) text = text.Substring(0, MaxText);
            var same = items.FirstOrDefault(i => i.Text == text);
            if (same != null) items.Remove(same);
            items.Insert(0, new ClipItem { Id = same != null ? same.Id : Guid.NewGuid().ToString("N").Substring(0, 12), Text = text, AtUtc = utc });
            Trim();
            return true;
        }
        public ClipItem Find(string id) { return items.FirstOrDefault(i => i.Id == id); }
        public bool Remove(string id) { return items.RemoveAll(i => i.Id == id) > 0; }
        public void Clear() { items.Clear(); }
        public void Load(IEnumerable<ClipItem> saved) { items.Clear(); foreach (var item in (saved ?? new ClipItem[0]).Where(i => i != null && !String.IsNullOrWhiteSpace(i.Text) && !String.IsNullOrEmpty(i.Id))) items.Add(item); Trim(); }
        private void Trim() { if (items.Count > size) items.RemoveRange(size, items.Count - size); }

        // One line for a row: whitespace runs become single spaces, cut to the length asked for.
        public static string Preview(string text, int length)
        {
            string line = String.Join(" ", (text ?? "").Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries));
            return line.Length > length ? line.Substring(0, length - 1).TrimEnd() + "…" : line;
        }
        // "just now", "4 min ago", "2 h ago", "yesterday".
        public static string Ago(DateTime utc, DateTime now)
        {
            var span = now - utc;
            if (span < TimeSpan.FromMinutes(1)) return "just now";
            if (span < TimeSpan.FromHours(1)) return (int)span.TotalMinutes + " min ago";
            if (span < TimeSpan.FromDays(1)) return (int)span.TotalHours + " h ago";
            return (int)span.TotalDays == 1 ? "yesterday" : (int)span.TotalDays + " days ago";
        }
    }

    // Watches the Windows clipboard with a clipboard format listener (no polling) while the Clipboard widget is on a
    // page. Texts that password managers and other apps mark as private are skipped: ExcludeClipboardContentFromMonitorProcessing,
    // Clipboard Viewer Ignore, and CanIncludeInClipboardHistory set to 0. History lives in memory unless the user asks to
    // keep it, and then only in clipboard-history.dat, encrypted for the Windows account.
    public sealed class ClipboardService : IDisposable
    {
        private const int ClipboardUpdate = 0x031D;
        private readonly string storePath;
        private HwndSource listener;
        public ClipboardHistory History { get; private set; }
        public bool Watching { get { return listener != null; } }
        public bool Paused { get; set; }
        public bool Keep { get; private set; }
        public event Action Changed;

        public static string PathFor(string settingsPath) { return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(settingsPath)), "clipboard-history.dat"); }

        public ClipboardService(string storePath) { this.storePath = storePath; History = new ClipboardHistory(); }

        // Keeping the history on disk, or forgetting the file when the user turns that off.
        public void SetKeep(bool keep)
        {
            if (keep == Keep && (keep || !File.Exists(storePath))) return;
            Keep = keep;
            if (keep) { if (History.Items.Count == 0) LoadSaved(); else Save(); }
            else { try { if (File.Exists(storePath)) File.Delete(storePath); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
        private void LoadSaved()
        {
            try
            {
                if (!File.Exists(storePath)) return;
                var json = Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(storePath), null, DataProtectionScope.CurrentUser));
                History.Load(new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Deserialize<List<ClipItem>>(json));
            }
            catch (Exception) { History.Clear(); }
        }
        private void Save()
        {
            if (!Keep) return;
            try
            {
                var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }.Serialize(History.Items.ToList())), null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storePath)));
                string temporary = storePath + ".tmp"; File.WriteAllBytes(temporary, bytes);
                if (File.Exists(storePath)) File.Replace(temporary, storePath, null); else File.Move(temporary, storePath);
            }
            catch (Exception) { }
        }

        public void Watch(bool on)
        {
            if (on && listener == null)
            {
                // A message-only window: it never shows, and only receives the clipboard notice.
                var parameters = new HwndSourceParameters("Widget Screen clipboard") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 };
                listener = new HwndSource(parameters);
                listener.AddHook(Hook);
                if (!AddClipboardFormatListener(listener.Handle)) { listener.Dispose(); listener = null; }
            }
            else if (!on && listener != null)
            {
                RemoveClipboardFormatListener(listener.Handle); listener.Dispose(); listener = null;
            }
        }
        private IntPtr Hook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == ClipboardUpdate) { handled = true; Read(); }
            return IntPtr.Zero;
        }

        private void Read()
        {
            if (Paused) return;
            IDataObject data = null;
            // Another app may still hold the clipboard; try a few times.
            for (int attempt = 0; attempt < 4 && data == null; attempt++)
            {
                try { data = Clipboard.GetDataObject(); }
                catch (COMException) { System.Threading.Thread.Sleep(20); }
                catch (ExternalException) { System.Threading.Thread.Sleep(20); }
            }
            if (data == null || Excluded(data)) return;
            string text;
            try { text = data.GetDataPresent(DataFormats.UnicodeText) ? data.GetData(DataFormats.UnicodeText) as string : null; }
            catch (Exception) { return; }
            if (Offer(text)) { }
        }

        // A copied text joins the history (unless paused); returns whether it did.
        public bool Offer(string text)
        {
            if (Paused || !History.Add(text, DateTime.UtcNow)) return false;
            Save();
            if (Changed != null) Changed();
            return true;
        }

        // Formats other apps set to keep a copy out of clipboard managers and history.
        public static bool Excluded(IDataObject data)
        {
            try
            {
                if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return true;
                if (data.GetDataPresent("CanIncludeInClipboardHistory"))
                {
                    var value = data.GetData("CanIncludeInClipboardHistory");
                    var stream = value as MemoryStream;
                    if (stream != null) { var bytes = stream.ToArray(); return bytes.Length >= 4 && BitConverter.ToInt32(bytes, 0) == 0; }
                    if (value is int) return (int)value == 0;
                    return true;
                }
                return false;
            }
            catch (Exception) { return true; }
        }

        public void Remove(string id) { if (History.Remove(id)) { Save(); if (Changed != null) Changed(); } }
        public void Clear() { History.Clear(); Save(); if (Changed != null) Changed(); }
        public void Resize(int size) { History.Size = size; Save(); }

        public void Dispose() { Watch(false); Changed = null; }

        [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr window);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RemoveClipboardFormatListener(IntPtr window);
    }
}
