using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
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
            C<ComboBox>("DeckColor").ItemsSource = DeckActions.Colors;
            C<ComboBox>("DeckColor").SelectionChanged += (s, e) => Capture();
            AttachReorder();
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
            Reorder(selected, destination);
        }
        // Moves a button to another place in the list (the up/down buttons and dragging both end here).
        public void Reorder(int from, int to)
        {
            if (from < 0 || from >= draft.Count || to < 0 || to >= draft.Count || from == to) return;
            var item = draft[from]; draft.RemoveAt(from); draft.Insert(to, item); Select(to);
        }

        // Drag a button up or down the list to reorder it, with a mouse or a finger. The list captures the pointer,
        // so rebuilding its rows mid-drag is fine; a drag never counts as a tap on the row.
        private int dragIndex = -1; private double dragStartY; private bool dragging; private TouchDevice dragTouch;
        private void AttachReorder()
        {
            var list = C<StackPanel>("DeckButtonList");
            list.PreviewMouseLeftButtonDown += (s, e) => { if (dragTouch != null) return; dragIndex = IndexAt(list, e.GetPosition(list).Y); dragStartY = e.GetPosition(list).Y; dragging = false; };
            list.PreviewMouseMove += (s, e) => {
                if (dragIndex < 0 || dragTouch != null || e.LeftButton != MouseButtonState.Pressed) return;
                double y = e.GetPosition(list).Y;
                if (!dragging) { if (Math.Abs(y - dragStartY) < 10) return; dragging = true; list.CaptureMouse(); }
                DragTo(list, y); e.Handled = true;
            };
            list.PreviewMouseLeftButtonUp += (s, e) => { if (dragging) { list.ReleaseMouseCapture(); e.Handled = true; } dragging = false; dragIndex = -1; };
            list.LostMouseCapture += (s, e) => { if (dragTouch == null) { dragging = false; dragIndex = -1; } };
            list.PreviewTouchDown += (s, e) => { if (dragTouch != null) return; dragTouch = e.TouchDevice; dragIndex = IndexAt(list, e.GetTouchPoint(list).Position.Y); dragStartY = e.GetTouchPoint(list).Position.Y; dragging = false; };
            list.PreviewTouchMove += (s, e) => {
                if (e.TouchDevice != dragTouch || dragIndex < 0) return;
                double y = e.GetTouchPoint(list).Position.Y;
                if (!dragging) { if (Math.Abs(y - dragStartY) < 12) return; dragging = true; list.CaptureTouch(dragTouch); }
                DragTo(list, y); e.Handled = true;
            };
            list.PreviewTouchUp += (s, e) => { if (e.TouchDevice != dragTouch) return; if (dragging) { list.ReleaseTouchCapture(dragTouch); e.Handled = true; } dragging = false; dragIndex = -1; dragTouch = null; };
            list.LostTouchCapture += (s, e) => { if (e.TouchDevice == dragTouch) { dragging = false; dragIndex = -1; dragTouch = null; } };
        }
        private void DragTo(StackPanel list, double y)
        {
            int target = IndexAt(list, y);
            if (target >= 0 && target != dragIndex) { Reorder(dragIndex, target); dragIndex = target; }
        }
        // The row under a y position in the list, or -1 outside the rows.
        private static int IndexAt(StackPanel list, double y)
        {
            double top = 0;
            for (int i = 0; i < list.Children.Count; i++)
            {
                var row = (FrameworkElement)list.Children[i];
                double bottom = top + row.ActualHeight + row.Margin.Top + row.Margin.Bottom;
                if (y >= top && y < bottom) return i;
                top = bottom;
            }
            return y < 0 ? 0 : list.Children.Count > 0 && y >= top ? list.Children.Count - 1 : -1;
        }
        private void Capture()
        {
            if (syncing || Current == null) return;
            Current.Label = C<TextBox>("DeckName").Text; Current.Target = C<TextBox>("DeckTarget").Text; Current.Arguments = C<TextBox>("DeckArguments").Text;
            Current.Confirm = C<CheckBox>("DeckConfirm").IsChecked == true;
            var icon = C<ComboBox>("DeckIcon").SelectedItem as DeckOption; if (icon != null) Current.Glyph = icon.Id;
            var color = C<ComboBox>("DeckColor").SelectedItem as DeckOption; if (color != null) Current.Color = color.Id;
            var list = C<StackPanel>("DeckButtonList");
            if (selected < list.Children.Count) UpdateButton((Button)list.Children[selected], Current, selected);
        }
        private void UpdateButton(Button button, Shortcut item, int index)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var glyph = new TextBlock { Text = item.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 22, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            string hex = DeckActions.AccentHex(item.Color);
            if (hex != null) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); glyph.Foreground = brush; } else glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            row.Children.Add(glyph);
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
                C<ComboBox>("DeckColor").SelectedItem = DeckActions.Colors.FirstOrDefault(c => c.Id == (Current.Color ?? "")) ?? DeckActions.Colors[0];
                C<ComboBox>("DeckAction").SelectedItem = DeckActions.Types.FirstOrDefault(i => i.Id == Current.Action);
                C<ComboBox>("DeckControl").SelectedItem = DeckActions.Controls.FirstOrDefault(i => i.Id == Current.Target);
                C<ComboBox>("DeckPreset").SelectedIndex = 0;
                string action = Current.Action;
                C<StackPanel>("DeckTargetFields").Visibility = action == "Control" ? Visibility.Collapsed : Visibility.Visible;
                C<StackPanel>("DeckAppFields").Visibility = action == "App" ? Visibility.Visible : Visibility.Collapsed;
                C<ComboBox>("DeckControl").Visibility = action == "Control" ? Visibility.Visible : Visibility.Collapsed;
                C<Button>("DeckBrowse").Visibility = action == "App" || action == "File" || action == "Folder" ? Visibility.Visible : Visibility.Collapsed;
                C<TextBlock>("DeckTargetLabel").Text = action == "Website" ? "Website address" : action == "Hotkey" ? "Key combination" : action == "Folder" ? "Folder path" : action == "File" ? "File path" : "Application path or app link";
                C<TextBlock>("DeckHelp").Text = action == "Hotkey" ? "Type a shortcut such as Ctrl+Shift+S, Win+D or F5. It's sent to the app you used last. Some games and admin apps block it, so turn on confirmation for shortcuts that close or delete things." : action == "App" ? "Pick a Start menu app, browse to an .exe or .lnk, or type an app link such as spotify:." : action == "Website" ? "For example https://www.youtube.com. Opens in your default browser." : action == "Control" ? "Media controls act on whatever is playing in Windows." : "Opens with its usual app. Scripts and installers ask for confirmation first.";
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
