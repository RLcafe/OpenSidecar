using System;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // Fits the dashboard to the screen it is shown on (docs/design/any-screen-size.md): the root grid takes a logical
    // size shaped like that screen and the Viewbox scales it uniformly, so the dashboard still never scrolls.
    public sealed partial class DashboardController
    {
        // Logical widths under which the header's buttons move to a second row: the edit toolbar needs about 1800
        // design units beside the clock.
        private const double StackBelow = 1800;
        private ScreenFit fit;
        public ScreenFit Fit { get { return fit; } }

        // The pixel size the dashboard is laid out for: a fixed size for renders and tests, else the second screen
        // (also in the preview window, so the preview shows what the second screen will), else the preview window.
        private Size TargetScreen()
        {
            if (Program.ScreenSize.HasValue) return Program.ScreenSize.Value;
            var screen = DisplayService.Secondary(settings.Monitor);
            if (screen != null) return new Size(screen.Bounds.Width, screen.Bounds.Height);
            var content = window.Content as FrameworkElement; var source = PresentationSource.FromVisual(window);
            if (content != null && content.ActualWidth > 0 && content.ActualHeight > 0 && source != null && source.CompositionTarget != null)
            {
                var device = source.CompositionTarget.TransformToDevice;
                return new Size(content.ActualWidth * device.M11, content.ActualHeight * device.M22);
            }
            return new Size(1920, 720);
        }

        // Windows display scaling, as device pixels per DIP for this window (1 at 100%). Renders and tests use 1.
        private double DisplayScale()
        {
            if (Program.ScreenSize.HasValue) return 1;
            try
            {
                var source = PresentationSource.FromVisual(window);
                if (source != null && source.CompositionTarget != null) { double m = source.CompositionTarget.TransformToDevice.M11; if (m > 0 && !Double.IsInfinity(m)) return m; }
            }
            catch (Exception) { }
            return 1;
        }

        private void FitScreen()
        {
            Size target = TargetScreen();
            // A screen smaller than the 1920 × 720 reference on a high-DPI system (a 10-inch 1280 × 800 at 150%, say)
            // keeps its text readable: the compact floor rises with the display scaling and the canvas may give way
            // down to 60%. A screen at least the reference size, like the owner's, is never changed by scaling.
            double full = Math.Max(Math.Min(target.Width / 1920, target.Height / 720), Math.Min(target.Width / 720, target.Height / 1920));
            double density = full >= 1 ? 1 : DisplayScale();
            double minCanvas = density > 1 ? Math.Max(0.6, ScreenFit.MinCanvas / density) : ScreenFit.MinCanvas;
            var next = ScreenFit.For(target.Width, target.Height, density, StackBelow, minCanvas);
            if (next.SameAs(fit)) return;
            fit = next;
            var dashboard = Control<Grid>("Dashboard"); dashboard.Width = fit.LogicalWidth; dashboard.Height = fit.LogicalHeight;
            Control<Grid>("DashboardContent").RowDefinitions[0].Height = new GridLength(fit.HeaderHeight);
            foreach (string name in new[] { "MainActions", "EditToolbar", "PageSwitcher" })
            {
                var element = Control<FrameworkElement>(name);
                Grid.SetColumn(element, fit.Stacked ? 0 : 1); Grid.SetColumnSpan(element, fit.Stacked ? 2 : 1);
                element.VerticalAlignment = fit.Stacked ? VerticalAlignment.Bottom : VerticalAlignment.Top;
                element.Margin = new Thickness(0, 0, 0, fit.Stacked ? 16 : 0);
            }
            var card = Control<Border>("SettingsCard");
            card.Width = Math.Min(1440, fit.LogicalWidth - 32); card.Height = Math.Min(672, fit.LogicalHeight - 32);
            Control<TextBlock>("StatusMessage").MaxWidth = Math.Max(400, fit.LogicalWidth - 540);
            if (Editor.SetCanvas(new Size(fit.CanvasWidth, fit.CanvasHeight))) Notify("The screen changed size, so the layout changes were discarded.");
            ApplyLayout(settings);
            UpdateHint();
        }
    }
}
