using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace WidgetScreen
{
    // Settings → Macros (docs/design/macros.md): the macros on the left, and the selected one's name, icon, colour and
    // steps on the right, with a step editor under the steps. Everything is a draft until Save changes; every step is
    // shown in words, so what a button will do can always be read here before it runs.
    public sealed class MacroEditor
    {
        private readonly Window window;
        private readonly Grid host;
        private readonly Action<string> status;
        private List<Macro> draft = new List<Macro>();
        private int selected = -1, editing = -1;
        private bool syncing;
        private StackPanel list, steps, fields;
        private TextBox name, target, arguments;
        private ComboBox icon, color, kind, control;
        private CheckBox confirm;
        private Slider delay;
        private TextBlock delayLabel, targetLabel, stepTitle, help, empty;
        private Button browse, addStep, cancelStep;
        private FrameworkElement targetRow, argumentsRow, delayRow;

        public MacroEditor(Window window, Grid host, Action<string> status)
        {
            this.window = window; this.host = host; this.status = status;
            Build();
        }

        private Button MakeButton(string content, Action click, string automation = null)
        {
            var button = new Button { Content = content, MinHeight = 52 };
            button.Click += (s, e) => click();
            if (automation != null) AutomationProperties.SetName(button, automation);
            return button;
        }
        private Button Icon(string glyph, string automation, Action click)
        {
            var button = new Button { Content = glyph, Style = (Style)window.Resources["IconButton"], FontSize = 16, MinHeight = 48, MinWidth = 48 };
            AutomationProperties.SetName(button, automation); button.ToolTip = automation;
            button.Click += (s, e) => click();
            return button;
        }
        private static TextBlock Small(string text) { var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }; block.SetResourceReference(FrameworkElement.StyleProperty, "Small"); return block; }
        private static UIElement Labelled(string label, UIElement field) { var panel = new StackPanel(); var text = Small(label); text.Margin = new Thickness(0, 0, 0, 6); panel.Children.Add(text); panel.Children.Add(field); return panel; }

        private void Build()
        {
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) }); host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) }); host.ColumnDefinitions.Add(new ColumnDefinition());
            // The macros: a list, Add, and move and remove under it.
            var left = new Grid(); left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); left.RowDefinitions.Add(new RowDefinition()); left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var add = MakeButton("+ Add", AddMacro, "Add a macro"); add.FontSize = 16; DockPanel.SetDock(add, Dock.Right); top.Children.Add(add);
            top.Children.Add(new TextBlock { Text = "Your macros", FontSize = 22, VerticalAlignment = VerticalAlignment.Center });
            left.Children.Add(top);
            list = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, PanningMode = PanningMode.VerticalOnly }; Grid.SetRow(scroll, 1); left.Children.Add(scroll);
            var moves = new UniformGrid3();
            moves.Add(Icon("", "Move macro earlier", () => MoveMacro(-1))); moves.Add(Icon("", "Move macro later", () => MoveMacro(1)));
            var remove = MakeButton("Remove", RemoveMacro, "Remove macro"); remove.FontSize = 15; moves.Add(remove);
            Grid.SetRow(moves.Panel, 2); moves.Panel.Margin = new Thickness(0, 10, 0, 0); left.Children.Add(moves.Panel);
            host.Children.Add(left);

            // The selected macro: its button, then its steps, then the step editor.
            var right = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            empty = new TextBlock { Text = "Tap + Add to make a macro: one button that runs several steps, such as a hotkey, then a wait, then opening an app.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
            right.Children.Add(empty);
            fields = new StackPanel(); right.Children.Add(fields);
            var row = new Grid(); foreach (var w in new[] { new GridLength(1, GridUnitType.Star), new GridLength(16), new GridLength(200), new GridLength(16), new GridLength(190) }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = w });
            name = new TextBox { MaxLength = 40 }; AutomationProperties.SetName(name, "Macro name"); name.TextChanged += (s, e) => Capture();
            icon = new ComboBox { ItemsSource = DeckActions.Icons, ItemTemplate = IconTemplate() }; AutomationProperties.SetName(icon, "Macro icon"); icon.SelectionChanged += (s, e) => Capture();
            color = new ComboBox { ItemsSource = DeckActions.Colors }; AutomationProperties.SetName(color, "Macro icon color"); color.SelectionChanged += (s, e) => Capture();
            row.Children.Add(Labelled("Button name", name)); var iconField = Labelled("Icon", icon); Grid.SetColumn(iconField, 2); row.Children.Add(iconField);
            var colorField = Labelled("Icon color", color); Grid.SetColumn(colorField, 4); row.Children.Add(colorField);
            fields.Children.Add(row);
            confirm = new CheckBox { Content = "Ask before running this macro", Margin = new Thickness(0, 10, 0, 0) }; confirm.Click += (s, e) => Capture();
            fields.Children.Add(confirm);
            fields.Children.Add(new TextBlock { Text = "Steps", FontSize = 22, Margin = new Thickness(0, 16, 0, 6) });
            var note = Small("The steps run in order, one after another. A step that fails stops the rest. Scripts and steps that would ask on a Quick Launch button make the whole macro ask first, listing every step.");
            note.Margin = new Thickness(0, 0, 0, 8); fields.Children.Add(note);
            steps = new StackPanel(); fields.Children.Add(steps);

            // The step editor: what the step does, and the field that kind needs.
            var editor = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(16), Margin = new Thickness(0, 12, 0, 0) };
            editor.SetResourceReference(Border.BackgroundProperty, "Raised");
            var form = new StackPanel(); editor.Child = form; fields.Children.Add(editor);
            stepTitle = new TextBlock { Text = "Add a step", FontSize = 18, Margin = new Thickness(0, 0, 0, 8) }; form.Children.Add(stepTitle);
            kind = new ComboBox { ItemsSource = MacroActions.Kinds }; AutomationProperties.SetName(kind, "Step type"); kind.SelectionChanged += (s, e) => ShowStepFields();
            form.Children.Add(Labelled("The step", kind));
            target = new TextBox { MaxLength = 2048, FontSize = 16 }; AutomationProperties.SetName(target, "Step target");
            browse = new Button { Content = "Browse…", Margin = new Thickness(10, 0, 0, 0), FontSize = 15, Padding = new Thickness(8), MinHeight = 52 }; browse.Click += (s, e) => Browse();
            var targetGrid = new Grid(); targetGrid.ColumnDefinitions.Add(new ColumnDefinition()); targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            targetGrid.Children.Add(target); Grid.SetColumn(browse, 1); targetGrid.Children.Add(browse);
            targetLabel = Small("Target"); targetLabel.Margin = new Thickness(0, 10, 0, 6);
            var targetPanel = new StackPanel(); targetPanel.Children.Add(targetLabel); targetPanel.Children.Add(targetGrid); targetRow = targetPanel; form.Children.Add(targetPanel);
            control = new ComboBox { ItemsSource = DeckActions.Controls, Margin = new Thickness(0, 10, 0, 0) }; AutomationProperties.SetName(control, "Step control"); form.Children.Add(control);
            arguments = new TextBox { MaxLength = 2048 }; AutomationProperties.SetName(arguments, "Step arguments");
            var argumentsPanel = new StackPanel(); var argumentsLabel = Small("Arguments (optional)"); argumentsLabel.Margin = new Thickness(0, 10, 0, 6); argumentsPanel.Children.Add(argumentsLabel); argumentsPanel.Children.Add(arguments); argumentsRow = argumentsPanel; form.Children.Add(argumentsPanel);
            delay = new Slider { Minimum = MacroActions.MinDelay, Maximum = 10000, SmallChange = 100, LargeChange = 500, TickFrequency = 100, IsSnapToTickEnabled = true, Value = 500 }; AutomationProperties.SetName(delay, "Wait length"); TouchSlider.Attach(delay);
            delayLabel = new TextBlock { Text = "0.5 s", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            delay.ValueChanged += (s, e) => delayLabel.Text = MacroActions.Seconds((int)Math.Round(e.NewValue));
            var delayGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) }; delayGrid.ColumnDefinitions.Add(new ColumnDefinition()); delayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            delayGrid.Children.Add(delay); Grid.SetColumn(delayLabel, 1); delayGrid.Children.Add(delayLabel); delayRow = delayGrid; form.Children.Add(delayGrid);
            help = Small(""); help.Margin = new Thickness(0, 8, 0, 0); form.Children.Add(help);
            var stepButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            addStep = MakeButton("Add step", SaveStep, "Add this step"); addStep.Style = (Style)window.Resources["AccentButton"]; addStep.MinHeight = 52; stepButtons.Children.Add(addStep);
            cancelStep = MakeButton("Cancel", () => EditStep(-1), "Stop editing this step"); cancelStep.Margin = new Thickness(8, 0, 0, 0); stepButtons.Children.Add(cancelStep);
            form.Children.Add(stepButtons);
            var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly };
            Grid.SetColumn(rightScroll, 2); host.Children.Add(rightScroll);
        }

        // Three equal columns, like the Quick Launch editor's move and remove row.
        private sealed class UniformGrid3
        {
            public readonly Grid Panel = new Grid();
            public void Add(UIElement element)
            {
                int column = Panel.Children.Count;
                Panel.ColumnDefinitions.Add(new ColumnDefinition());
                if (element is FrameworkElement && column < 2) ((FrameworkElement)element).Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(element, column); Panel.Children.Add(element);
            }
        }
        private static DataTemplate IconTemplate()
        {
            var template = new DataTemplate(); var row = new FrameworkElementFactory(typeof(StackPanel)); row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            var glyph = new FrameworkElementFactory(typeof(TextBlock)); glyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets")); glyph.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 10, 0)); glyph.SetBinding(TextBlock.TextProperty, new Binding("Id")); row.AppendChild(glyph);
            var label = new FrameworkElementFactory(typeof(TextBlock)); label.SetBinding(TextBlock.TextProperty, new Binding("Name")); row.AppendChild(label);
            template.VisualTree = row; return template;
        }

        private Macro Current { get { return selected >= 0 && selected < draft.Count ? draft[selected] : null; } }

        public void Begin(IEnumerable<Macro> macros)
        {
            draft = (macros ?? new Macro[0]).Select(m => m.Copy()).ToList();
            Select(draft.Count > 0 ? 0 : -1);
        }

        // Saving checks every macro; the first problem selects that macro and says what's wrong.
        public bool TryGet(out List<Macro> result)
        {
            result = null;
            for (int i = 0; i < draft.Count; i++)
            {
                string error = MacroActions.Validate(draft[i]);
                if (error != null) { Select(i); DashboardController.SelectSettingsTab(window, "Macros"); status("Macro " + (i + 1) + " (" + draft[i].Label + "): " + error); return false; }
            }
            result = draft.Select(m => m.Copy()).ToList();
            return true;
        }

        private void AddMacro()
        {
            if (draft.Count >= MacroActions.MaxMacros) { status("That's the most macros there can be."); return; }
            draft.Add(new Macro { Label = "New macro", Glyph = "", Color = "", Steps = new MacroStep[0] });
            Select(draft.Count - 1); name.Focus(); name.SelectAll();
        }
        private void RemoveMacro() { if (selected < 0) return; draft.RemoveAt(selected); Select(Math.Min(selected, draft.Count - 1)); }
        private void MoveMacro(int by)
        {
            int to = selected + by;
            if (selected < 0 || to < 0 || to >= draft.Count) return;
            var macro = draft[selected]; draft.RemoveAt(selected); draft.Insert(to, macro); Select(to);
        }

        public void Select(int index)
        {
            selected = index; editing = -1;
            syncing = true;
            try
            {
                var macro = Current;
                empty.Visibility = macro == null ? Visibility.Visible : Visibility.Collapsed;
                fields.Visibility = macro == null ? Visibility.Collapsed : Visibility.Visible;
                if (macro != null)
                {
                    name.Text = macro.Label;
                    icon.SelectedItem = DeckActions.Icons.FirstOrDefault(o => o.Id == macro.Glyph) ?? DeckActions.Icons[0];
                    color.SelectedItem = DeckActions.Colors.FirstOrDefault(o => o.Id == (macro.Color ?? "")) ?? DeckActions.Colors[0];
                    confirm.IsChecked = macro.Confirm;
                }
            }
            finally { syncing = false; }
            ShowList(); ShowSteps(); EditStep(-1);
        }
        private void Capture()
        {
            if (syncing || Current == null) return;
            Current.Label = name.Text;
            var glyph = icon.SelectedItem as DeckOption; if (glyph != null) Current.Glyph = glyph.Id;
            var tone = color.SelectedItem as DeckOption; Current.Color = tone == null ? "" : tone.Id;
            Current.Confirm = confirm.IsChecked == true;
            ShowList();
        }

        private void ShowList()
        {
            list.Children.Clear();
            for (int i = 0; i < draft.Count; i++)
            {
                int index = i; var macro = draft[i];
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                var glyph = new TextBlock { Text = macro.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                string hex = DeckActions.AccentHex(macro.Color);
                if (hex != null) glyph.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); else glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                content.Children.Add(glyph);
                var text = new StackPanel(); text.Children.Add(new TextBlock { Text = macro.Label, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 220 });
                var count = Small(MacroActions.Summary(macro) + (MacroActions.NeedsConfirmation(macro) ? "  ·  asks first" : "")); text.Children.Add(count);
                content.Children.Add(text);
                var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6), MinHeight = 52 };
                if (i == selected) { button.SetResourceReference(Control.BorderBrushProperty, "Accent"); button.SetResourceReference(Control.BackgroundProperty, "Selected"); button.BorderThickness = new Thickness(2); }
                AutomationProperties.SetName(button, macro.Label + (i == selected ? ", selected" : ""));
                button.Click += (s, e) => Select(index);
                list.Children.Add(button);
            }
        }

        private void ShowSteps()
        {
            steps.Children.Clear();
            var macro = Current; if (macro == null) return;
            var all = macro.Steps ?? new MacroStep[0];
            if (all.Length == 0) steps.Children.Add(Small("No steps yet. Add the first one below."));
            for (int i = 0; i < all.Length; i++)
            {
                int index = i;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition()); foreach (int k in new[] { 0, 1, 2 }) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var open = new Button { Content = new TextBlock { Text = (i + 1) + ".  " + MacroActions.Describe(all[i]), TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalContentAlignment = HorizontalAlignment.Left, MinHeight = 48, ToolTip = "Tap to change this step" };
                if (i == editing) { open.SetResourceReference(Control.BorderBrushProperty, "Accent"); open.BorderThickness = new Thickness(2); }
                AutomationProperties.SetName(open, "Step " + (i + 1) + ": " + MacroActions.Describe(all[i]));
                open.Click += (s, e) => EditStep(index); row.Children.Add(open);
                var up = Icon("", "Move step " + (i + 1) + " earlier", () => MoveStep(index, -1)); up.IsEnabled = i > 0; up.Margin = new Thickness(6, 0, 0, 0); Grid.SetColumn(up, 1); row.Children.Add(up);
                var down = Icon("", "Move step " + (i + 1) + " later", () => MoveStep(index, 1)); down.IsEnabled = i < all.Length - 1; Grid.SetColumn(down, 2); row.Children.Add(down);
                var remove = Icon("", "Remove step " + (i + 1), () => RemoveStep(index)); Grid.SetColumn(remove, 3); row.Children.Add(remove);
                steps.Children.Add(row);
            }
        }
        private void MoveStep(int index, int by)
        {
            var list = Current.Steps.ToList(); int to = index + by; if (to < 0 || to >= list.Count) return;
            var step = list[index]; list.RemoveAt(index); list.Insert(to, step); Current.Steps = list.ToArray(); EditStep(-1); ShowSteps(); ShowList();
        }
        private void RemoveStep(int index) { var list = Current.Steps.ToList(); list.RemoveAt(index); Current.Steps = list.ToArray(); EditStep(-1); ShowSteps(); ShowList(); }

        // -1 clears the step editor for a new step; otherwise it loads that step to change it.
        private void EditStep(int index)
        {
            editing = index;
            var step = index >= 0 && Current != null && index < Current.Steps.Length ? Current.Steps[index] : null;
            syncing = true;
            try
            {
                kind.SelectedItem = MacroActions.Kinds.FirstOrDefault(k => k.Id == (step == null ? "Hotkey" : step.Kind)) ?? MacroActions.Kinds[0];
                target.Text = step == null ? "" : step.Target; arguments.Text = step == null ? "" : step.Arguments;
                control.SelectedItem = step == null ? DeckActions.Controls[0] : DeckActions.Controls.FirstOrDefault(c => c.Id == step.Target) ?? DeckActions.Controls[0];
                delay.Value = step == null || step.Kind != "Delay" ? 500 : step.DelayMs;
            }
            finally { syncing = false; }
            stepTitle.Text = index < 0 ? "Add a step" : "Change step " + (index + 1);
            addStep.Content = index < 0 ? "Add step" : "Save step";
            cancelStep.Visibility = index < 0 ? Visibility.Collapsed : Visibility.Visible;
            ShowStepFields(); ShowSteps();
        }
        private void ShowStepFields()
        {
            var item = kind.SelectedItem as DeckOption; string id = item == null ? "Hotkey" : item.Id;
            bool path = id == "App" || id == "File" || id == "Folder" || id == "Script";
            targetRow.Visibility = id == "Control" || id == "Delay" ? Visibility.Collapsed : Visibility.Visible;
            control.Visibility = id == "Control" ? Visibility.Visible : Visibility.Collapsed;
            delayRow.Visibility = id == "Delay" ? Visibility.Visible : Visibility.Collapsed;
            argumentsRow.Visibility = id == "App" || id == "Script" ? Visibility.Visible : Visibility.Collapsed;
            browse.Visibility = path ? Visibility.Visible : Visibility.Collapsed;
            targetLabel.Text = id == "Hotkey" ? "Keys, such as Ctrl+Shift+S or Win+D" : id == "Website" ? "Website address" : id == "Script" ? "Script (.ps1, .bat, .cmd or .vbs)" : id == "Folder" ? "Folder" : id == "File" ? "File" : "App, Start menu shortcut or app link";
            help.Text = id == "Hotkey" ? "Sent to the app you're using, like a Quick Launch hotkey. Delete and Alt+F4 make the macro ask first."
                : id == "Script" ? "Starts the script and goes on to the next step without waiting for it. A macro with a script always asks first."
                : id == "Delay" ? "Waits before the next step, for example while an app opens."
                : id == "App" ? "Arguments make the macro ask first, like a Quick Launch button with arguments." : "";
            help.Visibility = help.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        private void Browse()
        {
            var item = kind.SelectedItem as DeckOption; string id = item == null ? "" : item.Id;
            if (id == "Folder")
            {
                using (var dialog = new System.Windows.Forms.FolderBrowserDialog()) if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) target.Text = dialog.SelectedPath;
                return;
            }
            var open = new OpenFileDialog { CheckFileExists = true, Filter = id == "Script" ? "Scripts|*.ps1;*.bat;*.cmd;*.vbs" : id == "App" ? "Apps|*.exe;*.lnk" : "All files|*.*" };
            if (open.ShowDialog(window) == true) target.Text = open.FileName;
        }
        // Adds the step (or saves the one being changed) once it checks out.
        private void SaveStep()
        {
            if (Current == null) return;
            var item = kind.SelectedItem as DeckOption; if (item == null) return;
            var chosen = control.SelectedItem as DeckOption;
            var step = new MacroStep { Kind = item.Id, Target = item.Id == "Control" ? (chosen == null ? "" : chosen.Id) : item.Id == "Delay" ? "" : target.Text.Trim(), Arguments = item.Id == "App" || item.Id == "Script" ? arguments.Text.Trim() : "", DelayMs = item.Id == "Delay" ? (int)Math.Round(delay.Value) : 0 };
            string error = MacroActions.Validate(step);
            if (error != null) { status(error); return; }
            var list = (Current.Steps ?? new MacroStep[0]).ToList();
            if (editing >= 0 && editing < list.Count) list[editing] = step;
            else if (list.Count >= MacroActions.MaxSteps) { status("A macro has up to " + MacroActions.MaxSteps + " steps."); return; }
            else list.Add(step);
            Current.Steps = list.ToArray(); status("");
            EditStep(-1); ShowList();
        }
    }
}
