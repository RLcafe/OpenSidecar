using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace WidgetScreen
{
    // The one way the dashboard asks "are you sure": a themed sheet over the dashboard with 52 px buttons, instead of
    // a Win32 message box that ignores the theme, the Viewbox scale and the touch sizes. Escape or the scrim cancels.
    public sealed class ConfirmSheet
    {
        private readonly Window window;
        private readonly Grid overlay;
        private TaskCompletionSource<bool> pending;
        private IInputElement focusedBefore;

        public ConfirmSheet(Window window)
        {
            this.window = window;
            overlay = (Grid)window.FindName("ConfirmOverlay");
            ((Button)window.FindName("ConfirmCancel")).Click += (sender, e) => Finish(false);
            ((Button)window.FindName("ConfirmAction")).Click += (sender, e) => Finish(true);
            overlay.MouseLeftButtonDown += (sender, e) => { if (e.OriginalSource == overlay) { Finish(false); e.Handled = true; } };
            overlay.TouchDown += (sender, e) => { if (e.OriginalSource == overlay) { Finish(false); e.Handled = true; } };
        }

        public bool IsOpen { get { return overlay.Visibility == Visibility.Visible; } }

        // Resolves true when the action button is tapped, false otherwise. A second question closes the first as "no".
        public Task<bool> AskAsync(string title, string subject, string detail, string action)
        {
            Finish(false);
            ((TextBlock)window.FindName("ConfirmTitle")).Text = title;
            ((TextBlock)window.FindName("ConfirmSubject")).Text = subject ?? "";
            var detailText = (TextBlock)window.FindName("ConfirmDetail");
            detailText.Text = detail ?? ""; detailText.Visibility = String.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
            var button = (Button)window.FindName("ConfirmAction");
            button.Content = action; AutomationProperties.SetName(button, action);
            pending = new TaskCompletionSource<bool>();
            focusedBefore = Keyboard.FocusedElement;
            overlay.Visibility = Visibility.Visible;
            ((Button)window.FindName("ConfirmCancel")).Focus();
            return pending.Task;
        }

        public void Cancel() { Finish(false); }

        private void Finish(bool yes)
        {
            var completion = pending; pending = null;
            if (overlay.Visibility == Visibility.Visible)
            {
                overlay.Visibility = Visibility.Collapsed;
                var element = focusedBefore as UIElement; focusedBefore = null;
                if (element != null && element.IsVisible) element.Focus();
            }
            if (completion != null) completion.TrySetResult(yes);
        }
    }
}
