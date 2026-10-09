using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace WidgetScreen
{
    public sealed class DeckEditor : IDisposable
    {
        private readonly Window window;
        private readonly Action<string> status;
        private List<Shortcut> draft = new List<Shortcut>();
        private readonly Dictionary<Shortcut, string> originalTargets = new Dictionary<Shortcut, string>();
        private readonly List<Shortcut> presets = DeckActions.Presets();
        private List<DeckOption> apps;
        private int selected = -1;
        private bool syncing, loadingApps, disposed;
        public DeckEditor(Window window, Action<string> status)
        {
            this.window = window; this.status = status;
            C<ComboBox>("DeckAction").ItemsSource = DeckActions.Types;
            C<ComboBox>("DeckControl").ItemsSource = DeckActions.Controls;
            C<ComboBox>("DeckIcon").ItemsSource = DeckActions.Icons;
            var template = new DataTemplate(); var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var glyph = new FrameworkElementFactory(typeof(TextBlock)); glyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); glyph.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 10, 0)); glyph.SetBinding(TextBlock.TextProperty, new Binding("Id")); row.AppendChild(glyph);
            var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding("Name")); row.AppendChild(label); template.VisualTree = row; C<ComboBox>("DeckIcon").ItemTemplate = template;
            C<ComboBox>("DeckPreset").ItemsSource = new[] { "Choose a preset…" }.Concat(presets.Select(p => p.Label)).ToArray();
            Click("DeckAdd", () => { draft.Add(new Shortcut { Label = "New button", Action = "App", Target = "", Glyph = "\uE8A7" }); Select(draft.Count - 1); C<TextBox>("DeckName").Focus(); C<TextBox>("DeckName").SelectAll(); });
            Click("DeckRemove", () => { if (selected < 0) return; draft.RemoveAt(selected); Select(Math.Min(selected, draft.Count - 1)); });
            Click("DeckMoveUp", () => Move(-1)); Click("DeckMoveDown", () => Move(1)); Click("DeckBrowse", Browse);
            foreach (string name in new[] { "DeckName", "DeckTarget", "DeckArguments" }) C<TextBox>(name).TextChanged += (s, e) => Capture();
            C<CheckBox>("DeckConfirm").Click += (s, e) => Capture();
            C<ComboBox>("DeckIcon").SelectionChanged += (s, e) => Capture();
            C<ComboBox>("DeckControl").SelectionChanged += (s, e) => { if (!syncing && Current != null) { var item = C<ComboBox>("DeckControl").SelectedItem as DeckOption; if (item != null) { Current.Target = item.Id; C<TextBox>("DeckTarget").Text = item.Id; } } };
            C<ComboBox>("DeckAction").SelectionChanged += (s, e) => {
                if (syncing || Current == null) return;
                var item = C<ComboBox>("DeckAction").SelectedItem as DeckOption;
                if (item == null) return; Current.Action = item.Id; Current.Target = ""; Current.Arguments = ""; Select(selected);
            };
            C<ComboBox>("DeckPreset").SelectionChanged += (s, e) => {
                int index = C<ComboBox>("DeckPreset").SelectedIndex - 1;
                if (!syncing && Current != null && index >= 0) { draft[selected] = presets[index].Copy(); Select(selected); }
            };
            C<ComboBox>("DeckApps").SelectionChanged += (s, e) => {
                if (syncing || Current == null) return;
                var app = C<ComboBox>("DeckApps").SelectedItem as DeckOption;
                if (app == null || String.IsNullOrEmpty(app.Id)) return;
                C<TextBox>("DeckTarget").Text = app.Id;
                if (Current.Label == "New button") C<TextBox>("DeckName").Text = app.Name;
            };
            C<TextBox>("DeckAppSearch").TextChanged += (s, e) => FilterApps();
        }
        private T C<T>(string name) where T : class { return (T)window.FindName(name); }
        private void Click(string name, Action action) { C<Button>(name).Click += (s, e) => action(); }
        private Shortcut Current { get { return selected >= 0 && selected < draft.Count ? draft[selected] : null; } }
        public void Begin(IEnumerable<Shortcut> shortcuts)
        {
            draft = shortcuts.Select(s => s.Copy()).ToList(); originalTargets.Clear();
            foreach (var item in draft) originalTargets[item] = TargetIdentity(item);
            Select(draft.Count == 0 ? -1 : 0);
        }
        private static string TargetIdentity(Shortcut item) { return item.Action + "\n" + item.Target + "\n" + item.Arguments; }
        private void Move(int delta)
        {
            int destination = selected + delta; if (Current == null || destination < 0 || destination >= draft.Count) return;
            var item = Current; draft.RemoveAt(selected); draft.Insert(destination, item); Select(destination);
        }
        private void Capture()
        {
            if (syncing || Current == null) return;
            Current.Label = C<TextBox>("DeckName").Text; Current.Target = C<TextBox>("DeckTarget").Text; Current.Arguments = C<TextBox>("DeckArguments").Text;
            Current.Confirm = C<CheckBox>("DeckConfirm").IsChecked == true;
            var icon = C<ComboBox>("DeckIcon").SelectedItem as DeckOption; if (icon != null) Current.Glyph = icon.Id;
            var list = C<StackPanel>("DeckButtonList");
            if (selected < list.Children.Count) UpdateButton((Button)list.Children[selected], Current, selected);
        }
        private void UpdateButton(Button button, Shortcut item, int index)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = new TextBlock { Text = item.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }; glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent"); row.Children.Add(glyph);
            row.Children.Add(new TextBlock { Text = (index + 1) + ". " + item.Label, FontSize = 16, MaxWidth = 214, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            button.Content = row; AutomationProperties.SetName(button, "Edit button " + (index + 1) + ": " + item.Label);
        }
        private void Select(int index)
        {
            selected = index; syncing = true;
            try
            {
                var list = C<StackPanel>("DeckButtonList"); list.Children.Clear();
                for (int i = 0; i < draft.Count; i++)
                {
                    int itemIndex = i; var button = new Button { Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(12), HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 56 };
                    UpdateButton(button, draft[i], i); button.SetResourceReference(Button.BorderBrushProperty, i == selected ? "Accent" : "ControlBorder"); button.Click += (s, e) => Select(itemIndex); list.Children.Add(button);
                }
                C<TextBlock>("DeckCount").Text = draft.Count + (draft.Count == 1 ? " button" : " buttons");
                C<Button>("DeckMoveUp").IsEnabled = selected > 0; C<Button>("DeckMoveDown").IsEnabled = selected >= 0 && selected < draft.Count - 1; C<Button>("DeckRemove").IsEnabled = Current != null;
                C<StackPanel>("DeckFields").Visibility = Current == null ? Visibility.Collapsed : Visibility.Visible;
                C<TextBlock>("DeckEditorEmpty").Visibility = Current == null ? Visibility.Visible : Visibility.Collapsed;
                if (Current == null) return;
                C<TextBox>("DeckName").Text = Current.Label; C<TextBox>("DeckTarget").Text = Current.Target; C<TextBox>("DeckArguments").Text = Current.Arguments ?? "";
                C<CheckBox>("DeckConfirm").IsChecked = Current.Confirm;
                C<ComboBox>("DeckIcon").SelectedItem = DeckActions.Icons.FirstOrDefault(i => i.Id == Current.Glyph);
                C<ComboBox>("DeckAction").SelectedItem = DeckActions.Types.FirstOrDefault(i => i.Id == Current.Action);
                C<ComboBox>("DeckControl").SelectedItem = DeckActions.Controls.FirstOrDefault(i => i.Id == Current.Target);
                C<ComboBox>("DeckPreset").SelectedIndex = 0;
                string action = Current.Action;
                C<StackPanel>("DeckTargetFields").Visibility = action == "Control" ? Visibility.Collapsed : Visibility.Visible;
                C<StackPanel>("DeckAppFields").Visibility = action == "App" ? Visibility.Visible : Visibility.Collapsed;
                C<ComboBox>("DeckControl").Visibility = action == "Control" ? Visibility.Visible : Visibility.Collapsed;
                C<Button>("DeckBrowse").Visibility = action == "App" || action == "File" || action == "Folder" ? Visibility.Visible : Visibility.Collapsed;
                C<TextBlock>("DeckTargetLabel").Text = action == "Website" ? "Website address" : action == "Hotkey" ? "Key combination" : action == "Folder" ? "Folder path" : action == "File" ? "File path" : "Application path or app link";
                C<TextBlock>("DeckHelp").Text = action == "Hotkey" ? "Type Ctrl+Shift+S, Win+D, Alt+F4, or a key such as F5. The button returns to your last active app before sending keys. Some games and elevated apps may block shortcuts. Enable confirmation for actions that close or delete things." : action == "App" ? "Choose a Start menu app, browse to an .exe or .lnk, or enter an installed app link such as spotify:. Filter the app list in the box beside it." : action == "Website" ? "Example: https://www.youtube.com — opens in your default browser." : action == "Control" ? "Choose a built-in action. Media controls use the active Windows media session." : "Opens with Windows. Script and installer files require confirmation before running.";
                if (action == "App") LoadApps();
            }
            finally { syncing = false; }
        }
        private async void LoadApps()
        {
            if (apps != null) { FilterApps(); return; } if (loadingApps) return;
            loadingApps = true; C<ComboBox>("DeckApps").ItemsSource = new[] { DeckActions.Option("", "Loading Start menu apps…") }; C<ComboBox>("DeckApps").SelectedIndex = 0;
            List<DeckOption> found;
            try { found = await Task.Run(() => DeckActions.StartMenuApps()); }
            catch (Exception) { found = new List<DeckOption>(); }
            await window.Dispatcher.InvokeAsync(new Action(() => { if (disposed) return; apps = found; loadingApps = false; FilterApps(); }));
        }
        private void FilterApps()
        {
            if (apps == null) return; bool wasSyncing = syncing; syncing = true;
            try
            {
                string query = C<TextBox>("DeckAppSearch").Text.Trim();
                var matches = apps.Where(a => a.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0).ToArray();
                C<ComboBox>("DeckApps").ItemsSource = new[] { DeckActions.Option("", matches.Length == 0 ? "No matches — use Browse" : "Choose a Start menu app…") }.Concat(matches).ToArray();
                C<ComboBox>("DeckApps").SelectedIndex = 0;
            }
            finally { syncing = wasSyncing; }
        }
        private void Browse()
        {
            if (Current == null) return;
            if (Current.Action == "Folder")
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose the folder this button opens", ShowNewFolderButton = false })
                {
                    var owner = new NativeOwner(new System.Windows.Interop.WindowInteropHelper(window).Handle);
                    if (dialog.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK) C<TextBox>("DeckTarget").Text = dialog.SelectedPath;
                }
            }
            else
            {
                var dialog = new OpenFileDialog { Filter = Current.Action == "App" ? "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk" : "All files (*.*)|*.*", CheckFileExists = true, DereferenceLinks = false, Title = "Choose what this button opens" };
                if (dialog.ShowDialog(window) == true) { C<TextBox>("DeckTarget").Text = dialog.FileName; if (Current.Label == "New button") C<TextBox>("DeckName").Text = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName); }
            }
        }
        public bool TryGet(out List<Shortcut> result)
        {
            Capture(); result = null;
            for (int i = 0; i < draft.Count; i++)
            {
                string error = DeckActions.Validate(draft[i]);
                // An unavailable drive/app in an unchanged assignment must not block other settings.
                string original;
                if (!String.IsNullOrWhiteSpace(draft[i].Label) && originalTargets.TryGetValue(draft[i], out original) && original == TargetIdentity(draft[i])) error = null;
                if (error != null) { Select(i); C<TabControl>("SettingsTabs").SelectedIndex = 5; status("Button " + (i + 1) + " (" + draft[i].Label + "): " + error); return false; }
            }
            result = draft.Select(s => s.Copy()).ToList();
            foreach (var item in result) { item.Label = item.Label.Trim(); item.Target = item.Target.Trim(); }
            return true;
        }
        public void Dispose() { disposed = true; }
        private sealed class NativeOwner : System.Windows.Forms.IWin32Window { public IntPtr Handle { get; private set; } public NativeOwner(IntPtr handle) { Handle = handle; } }
    }
}
