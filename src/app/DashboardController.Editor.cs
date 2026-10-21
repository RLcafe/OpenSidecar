using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WidgetScreen
{
    // Edit layout's list, the same as the phone app's (Dashboard.java, openWidgetsPanel): a check mark per widget to
    // show or hide it, with the selected widget highlighted, and the layout pages: rename the one shown, open, move or
    // delete any, and add one. Page actions save the layout changes first and keep the editor open on the page shown.
    public sealed partial class DashboardController
    {
        private bool editPanelOpen;
        private int deleteArmed = -1, deleteArmedAt;
        private readonly Dictionary<string, CheckBox> widgetChecks = new Dictionary<string, CheckBox>();
        private readonly Dictionary<string, Border> widgetRows = new Dictionary<string, Border>();
        public bool EditPanelOpen { get { return editPanelOpen; } }

        private void InitializeEditPanel()
        {
            Button("EditWidgetsButton", () => { if (editPanelOpen) CloseEditPanel(); else OpenEditPanel(); });
            var layer = Control<Grid>("EditPanelLayer");
            layer.MouseLeftButtonDown += (sender, e) => { if (e.OriginalSource == layer) { CloseEditPanel(); e.Handled = true; } };
            layer.TouchDown += (sender, e) => { if (e.OriginalSource == layer) { CloseEditPanel(); e.Handled = true; } };
        }

        public void OpenEditPanel()
        {
            if (!Editor.Editing) return;
            editPanelOpen = true;
            Control<Border>("EditPanel").Margin = new Thickness(0, 20 + (fit == null ? ScreenFit.Header : fit.HeaderHeight), 332, 16 + ScreenFit.Footer);
            BuildEditPanel();
            Control<Grid>("EditPanelLayer").Visibility = Visibility.Visible;
        }

        public void CloseEditPanel()
        {
            editPanelOpen = false; deleteArmed = -1;
            Control<Grid>("EditPanelLayer").Visibility = Visibility.Collapsed;
        }

        private TextBlock PanelLabel(string text, double top)
        {
            var label = new TextBlock { Text = text, Margin = new Thickness(4, top, 0, 8) };
            label.SetResourceReference(FrameworkElement.StyleProperty, "Label");
            return label;
        }

        private Button PanelIcon(string glyph, string name, bool enabled, Action action)
        {
            var button = new Button { Content = glyph, IsEnabled = enabled, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Width = 48, Height = 48, Margin = new Thickness(4, 0, 0, 0), ToolTip = name };
            button.SetResourceReference(FrameworkElement.StyleProperty, "IconButton");
            button.FontSize = 18;
            AutomationProperties.SetName(button, name);
            button.Click += (sender, e) => action();
            return button;
        }

        private void BuildEditPanel()
        {
            var content = Control<StackPanel>("EditPanelContent"); content.Children.Clear();
            widgetChecks.Clear(); widgetRows.Clear();
            content.Children.Add(PanelLabel("SHOW ON THE DASHBOARD", 4));
            foreach (string id in WidgetLayout.Ids)
            {
                string widget = id;
                var check = new CheckBox { Content = WidgetLayout.Title(id), MinHeight = 52, VerticalContentAlignment = VerticalAlignment.Center };
                check.Click += (sender, e) => { Editor.SetVisible(widget, check.IsChecked == true); if (check.IsChecked == true) Editor.Select(widget); };
                var row = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Child = check, Margin = new Thickness(0, 0, 0, 2) };
                widgetChecks[id] = check; widgetRows[id] = row;
                content.Children.Add(row);
            }
            content.Children.Add(PanelLabel("PAGES", 20));
            var hint = new TextBlock { Text = "Page actions save your layout changes first.", Margin = new Thickness(4, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
            hint.SetResourceReference(FrameworkElement.StyleProperty, "Small");
            content.Children.Add(hint);
            int current = WidgetLayout.CurrentPage(settings), count = WidgetLayout.PageCount(settings);
            var rename = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            rename.ColumnDefinitions.Add(new ColumnDefinition()); rename.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = new TextBox { Text = WidgetLayout.Page(settings, current).Name, MinHeight = 52, MaxLength = 24 };
            AutomationProperties.SetName(name, "Page name");
            var renameButton = new Button { Content = "Rename", MinHeight = 52, Margin = new Thickness(8, 0, 0, 0) };
            renameButton.Click += (sender, e) => PageAction(() => WidgetLayout.RenamePage(settings, WidgetLayout.CurrentPage(settings), name.Text));
            Grid.SetColumn(renameButton, 1); rename.Children.Add(name); rename.Children.Add(renameButton);
            content.Children.Add(rename);
            for (int i = 0; i < count; i++)
            {
                int index = i; string pageName = WidgetLayout.Page(settings, i).Name;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                for (int c = 0; c < 3; c++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var open = new Button { Content = new TextBlock { Text = pageName }, MinHeight = 52, HorizontalContentAlignment = HorizontalAlignment.Left };
                open.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, i == current ? "Accent" : "ControlBorder");
                open.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, i == current ? "Selected" : "Raised");
                open.BorderThickness = new Thickness(i == current ? 2 : 1);
                AutomationProperties.SetName(open, pageName + (i == current ? ", shown" : ", open this page"));
                open.Click += (sender, e) => { if (index != WidgetLayout.CurrentPage(settings)) PageAction(() => settings.CurrentPage = index); };
                row.Children.Add(open);
                var up = PanelIcon("", "Move " + pageName + " up", i > 0, () => PageAction(() => WidgetLayout.MovePage(settings, index, index - 1)));
                var down = PanelIcon("", "Move " + pageName + " down", i < count - 1, () => PageAction(() => WidgetLayout.MovePage(settings, index, index + 1)));
                var delete = PanelIcon("", "Delete " + pageName, count > 1, () => ConfirmDeletePage(index));
                Grid.SetColumn(up, 1); Grid.SetColumn(down, 2); Grid.SetColumn(delete, 3);
                row.Children.Add(up); row.Children.Add(down); row.Children.Add(delete);
                content.Children.Add(row);
            }
            var add = new Button { Content = "Add page", MinHeight = 52, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0), IsEnabled = count < WidgetLayout.MaxPages,
                ToolTip = "A new page starts with the default widgets. Up to " + WidgetLayout.MaxPages + " pages." };
            add.Click += (sender, e) => PageAction(() => { if (WidgetLayout.AddPage(settings, null) < 0) throw new InvalidOperationException("A dashboard can have up to " + WidgetLayout.MaxPages + " pages."); });
            content.Children.Add(add);
            UpdateEditPanel();
        }

        // Check marks and the selected widget follow the editor without rebuilding the list (it changes on every drag).
        private void UpdateEditPanel()
        {
            if (!editPanelOpen) return;
            foreach (string id in WidgetLayout.Ids)
            {
                CheckBox check; Border row;
                if (!widgetChecks.TryGetValue(id, out check) || !widgetRows.TryGetValue(id, out row)) continue;
                check.IsChecked = Editor.IsEnabled(id);
                bool selected = id == Editor.SelectedId;
                if (selected) { row.SetResourceReference(Border.BackgroundProperty, "Selected"); row.SetResourceReference(Border.BorderBrushProperty, "Accent"); }
                else { row.Background = Brushes.Transparent; row.BorderBrush = Brushes.Transparent; }
            }
        }

        // Deleting a page asks first: a second tap within a few seconds confirms.
        private void ConfirmDeletePage(int index)
        {
            if (deleteArmed == index && ticks - deleteArmedAt <= 5)
            {
                deleteArmed = -1;
                PageAction(() => { if (!WidgetLayout.DeletePage(settings, index)) throw new InvalidOperationException("The only page can't be deleted."); });
                return;
            }
            deleteArmed = index; deleteArmedAt = ticks;
            Notify("Tap delete again to delete " + WidgetLayout.Page(settings, index).Name + " and its layouts.", true);
        }

        // Saves the layout changes (if any), changes the pages, and opens the editor again on the page shown.
        public void PageAction(Action change)
        {
            if (!Editor.Editing) return;
            if (Editor.CanUndo) { SaveLayoutEdit(); if (Editor.Editing) return; }
            else { CloseEditPanel(); Editor.Cancel(); }
            string problem = ChangePages(change);
            BeginLayoutEdit(); OpenEditPanel();
            Notify(problem ?? "Editing " + WidgetLayout.Page(settings, WidgetLayout.CurrentPage(settings)).Name + ".");
        }
    }
}
