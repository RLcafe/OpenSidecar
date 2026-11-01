using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // The Clipboard widget (docs/design/clipboard-history.md): the last texts copied on this PC. The clipboard is only
    // watched while the widget is on a page; a tap puts a text back on the clipboard and sends Ctrl+V to the app the
    // user was in (the dashboard never takes focus, decision 013). A phone sees short previews and can paste too.
    public sealed partial class DashboardController
    {
        private ClipboardService clipboard;
        private ClipboardCard clipCard;
        // For the checks (tests/Clipboard/ClipboardTests.cs).
        public ClipboardService Clipboard { get { return clipboard; } }
        public ClipboardCard ClipboardView { get { return clipCard; } }

        private void InitializeClipboard()
        {
            clipboard = new ClipboardService(ClipboardService.PathFor(settingsPath));
            clipCard = new ClipboardCard(window, Control<Grid>("ClipBody"), Control<TextBlock>("ClipStatus"));
            clipboard.Changed += ShowClipboard;
            clipCard.Pasted += id => PasteClip(id);
            clipCard.PauseToggled += () => { clipboard.Paused = !clipboard.Paused; ShowClipboard(); Notify(clipboard.Paused ? "Clipboard history paused." : "Clipboard history resumed."); };
            clipCard.ClearAsked += async () => {
                if (clipboard.History.Items.Count == 0) return;
                bool clear = await confirm.AskAsync("CLIPBOARD", "Clear the clipboard history?", "The " + clipboard.History.Items.Count + " texts kept here are forgotten. What's on the clipboard now stays.", "Clear");
                if (clear && !disposed) clipboard.Clear();
            };
            var size = Control<Slider>("ClipSizeChoice"); TouchSlider.Attach(size);
            size.ValueChanged += (sender, e) => Text("ClipSizeLabel", Math.Round(e.NewValue) + " texts");
        }

        private void ShowClipboard() { if (!disposed) clipCard.Show(clipboard.History.Items, clipboard.Paused); }

        // The clipboard is watched only while the widget is on one of the pages (and never while rendering a picture).
        private void ApplyClipboard()
        {
            clipboard.Resize(settings.ClipboardSize);
            clipboard.SetKeep(settings.ClipboardKeep);
            clipboard.Watch(!Program.Rendering && (Editor.IsVisible("Clipboard") || WidgetLayout.ShownAnywhere(settings, "Clipboard")));
            ShowClipboard();
        }

        // Puts the text back on the clipboard and pastes it into the app in front; returns null, or what went wrong.
        private async Task<string> PasteClip(string id)
        {
            var item = clipboard.History.Find(id);
            if (item == null) { Notify("That text isn't in the history anymore."); return "That text isn't in the history anymore."; }
            if (!SetClipboardText(item.Text)) { Notify("Another app is using the clipboard. Try again."); return "Another app is using the clipboard on the PC. Try again."; }
            try
            {
                await deckHotkeys.SendAsync("Ctrl+V");
                string target = deckHotkeys.TargetName;
                if (!disposed) Notify("Pasted" + (target == null ? "." : " into " + target + "."));
                return null;
            }
            catch (InvalidOperationException e)
            {
                if (!disposed) Notify("Copied, but not pasted: " + e.Message);
                return "Copied on the PC, but not pasted: " + e.Message;
            }
        }
        private static bool SetClipboardText(string text)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { System.Windows.Clipboard.SetText(text); return true; }
                catch (System.Runtime.InteropServices.COMException) { System.Threading.Thread.Sleep(30); }
                catch (System.Runtime.InteropServices.ExternalException) { System.Threading.Thread.Sleep(30); }
            }
            return false;
        }

        // ----- Settings → Widgets -----

        private void OpenClipboardSettings()
        {
            Control<Slider>("ClipSizeChoice").Value = settings.ClipboardSize; Text("ClipSizeLabel", settings.ClipboardSize + " texts");
            Control<CheckBox>("ClipKeepChoice").IsChecked = settings.ClipboardKeep;
        }
        private void SaveClipboardChoices(AppSettings candidate)
        {
            candidate.ClipboardSize = (int)Math.Round(Control<Slider>("ClipSizeChoice").Value);
            candidate.ClipboardKeep = Control<CheckBox>("ClipKeepChoice").IsChecked == true;
        }

        // ----- The phone -----

        // Short previews only: the phone shows what each text starts with, and pastes on the PC by id.
        private Dictionary<string, object> RemoteClipboard()
        {
            var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return D("watching", clipboard.Watching, "paused", clipboard.Paused,
                "items", clipboard.History.Items.Select(i => (object)D("id", i.Id, "text", ClipboardHistory.Preview(i.Text, 200), "length", i.Text.Length, "at", (i.AtUtc - epoch).TotalMilliseconds)).ToArray());
        }
        private async Task<RemoteResponse> RemoteClipboardCommand(string action, Dictionary<string, object> body)
        {
            switch (action)
            {
                case "clipboard.paste":
                    {
                        string error = await PasteClip(Str(body, "id"));
                        if (error != null) throw new InvalidOperationException(error);
                        return Done("Pasted on the PC.");
                    }
                case "clipboard.pause": clipboard.Paused = Flag(body, "paused"); ShowClipboard(); return Done(null);
                case "clipboard.clear": clipboard.Clear(); return Done(null);
                default: throw new ArgumentException("Unknown clipboard request. Update Widget Screen on the phone or the PC.");
            }
        }
    }
}
