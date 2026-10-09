using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WidgetScreen
{
    // A finger on a slider owns the gesture, so a surrounding list cannot pan instead of dragging.
    public static class TouchSlider
    {
        public static void Attach(Slider slider)
        {
            TouchDevice touch = null;
            // The slider becomes the nearest manipulation target; its manipulation events stop here.
            slider.IsManipulationEnabled = true;
            slider.ManipulationStarting += (s, e) => e.Handled = true;
            slider.ManipulationDelta += (s, e) => e.Handled = true;
            slider.ManipulationInertiaStarting += (s, e) => e.Handled = true;
            slider.ManipulationBoundaryFeedback += (s, e) => e.Handled = true;
            slider.PreviewTouchDown += (s, e) => {
                e.Handled = true;
                if (touch != null || !slider.IsEnabled) return;
                touch = e.TouchDevice; slider.CaptureTouch(touch);
                slider.Value = ValueAt(slider, e.GetTouchPoint(slider).Position.X);
            };
            slider.PreviewTouchMove += (s, e) => { if (e.TouchDevice == touch) { slider.Value = ValueAt(slider, e.GetTouchPoint(slider).Position.X); e.Handled = true; } };
            slider.PreviewTouchUp += (s, e) => {
                if (e.TouchDevice != touch) return;
                slider.Value = ValueAt(slider, e.GetTouchPoint(slider).Position.X); e.Handled = true;
                var device = touch; touch = null; slider.ReleaseTouchCapture(device);
            };
            slider.LostTouchCapture += (s, e) => { if (e.TouchDevice == touch) touch = null; };
        }

        // Maps an x position (slider coordinates) to a value using the rendered track and thumb sizes.
        // It deliberately ignores the thumb's current position, which lags behind rapid value changes until relayout.
        public static double ValueAt(Slider slider, double x)
        {
            double thumb = 48, start = 0, length = slider.ActualWidth;
            var track = slider.Template == null ? null : slider.Template.FindName("PART_Track", slider) as Track;
            if (track != null && track.ActualWidth > 0)
            {
                if (track.Thumb != null && track.Thumb.ActualWidth > 0) thumb = track.Thumb.ActualWidth;
                start = track.TranslatePoint(new Point(0, 0), slider).X; length = track.ActualWidth;
            }
            double range = Math.Max(1, length - thumb);
            double fraction = Math.Max(0, Math.Min(1, (x - start - thumb / 2) / range));
            if (track != null && track.IsDirectionReversed) fraction = 1 - fraction;
            double value = slider.Minimum + fraction * (slider.Maximum - slider.Minimum);
            if (slider.IsSnapToTickEnabled && slider.TickFrequency > 0)
                value = slider.Minimum + Math.Round((value - slider.Minimum) / slider.TickFrequency) * slider.TickFrequency;
            return Math.Max(slider.Minimum, Math.Min(slider.Maximum, value));
        }
    }
}
