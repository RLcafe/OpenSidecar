using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace WidgetScreen
{
    public sealed class OutputChoice
    {
        public string Id, Label, Detail;
        public bool Selected;
    }

    // Output device list for the master volume or one app. It is a panel inside the mixer card rather than a popup,
    // so it scales with the dashboard and never opens a separate window over a fullscreen game.
    public sealed class OutputPicker
    {
        private readonly Window window;
        private readonly Border panel;
        private readonly StackPanel options;
        private Action<OutputChoice> choose;

        public OutputPicker(Window window)
        {
            this.window = window;
            panel = (Border)window.FindName("OutputPicker");
            options = (StackPanel)window.FindName("OutputOptions");
            ((Button)window.FindName("OutputPickerClose")).Click += (sender, e) => Close();
        }

        public bool IsOpen { get { return panel.Visibility == Visibility.Visible; } }

        public void Show(string title, string subject, IList<OutputChoice> choices, bool enabled, string note, Action<OutputChoice> choose)
        {
            this.choose = choose;
            ((TextBlock)window.FindName("OutputPickerTitle")).Text = title;
            ((TextBlock)window.FindName("OutputPickerSubject")).Text = subject;
            ((TextBlock)window.FindName("OutputPickerNote")).Text = note ?? "";
            options.Children.Clear();
            foreach (var choice in choices) options.Children.Add(Option(choice, enabled));
            ((ScrollViewer)window.FindName("OutputOptionsScroll")).ScrollToTop();
            panel.Visibility = Visibility.Visible;
            var selected = options.Children.OfType<Button>().FirstOrDefault(button => AutomationProperties.GetName(button).EndsWith(", selected", StringComparison.Ordinal));
            // Wait for layout so the current output can scroll into view and take focus.
            if (selected != null) panel.Dispatcher.BeginInvoke(new Action(() => { if (IsOpen && selected.Parent == options) { selected.BringIntoView(); if (selected.IsEnabled) selected.Focus(); } }), DispatcherPriority.Loaded);
        }

        public void Close()
        {
            choose = null;
            options.Children.Clear();
            panel.Visibility = Visibility.Collapsed;
        }

        // Segoe MDL2 glyph for an endpoint form factor: headphones and headsets, display audio, otherwise speakers.
        public static string Glyph(uint formFactor) { return formFactor == 3 || formFactor == 5 ? "" : formFactor == 9 ? "" : ""; }

        private Button Option(OutputChoice choice, bool enabled)
        {
            var mark = new TextBlock { Text = choice.Selected ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 18, Width = 32, VerticalAlignment = VerticalAlignment.Center };
            mark.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = choice.Label });
            if (!String.IsNullOrEmpty(choice.Detail)) text.Children.Add(new TextBlock { Text = choice.Detail, Style = (Style)window.Resources["Small"], Margin = new Thickness(0, 2, 0, 0) });
            var content = new DockPanel(); DockPanel.SetDock(mark, Dock.Left); content.Children.Add(mark); content.Children.Add(text);
            var button = new Button { Content = content, MinHeight = 52, Padding = new Thickness(12, 6, 16, 6), Margin = new Thickness(0, 0, 0, 8), HorizontalContentAlignment = HorizontalAlignment.Stretch, IsEnabled = enabled };
            if (choice.Selected) button.SetResourceReference(Button.BorderBrushProperty, "Accent");
            AutomationProperties.SetName(button, choice.Label + (choice.Selected ? ", selected" : ""));
            button.Click += (sender, e) => {
                var action = choose;
                Close();
                if (action != null && !choice.Selected) action(choice);
            };
            return button;
        }
    }
}
