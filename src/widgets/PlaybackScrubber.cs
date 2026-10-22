using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WidgetScreen
{
    // Preview locally while dragging; send one seek on release, never on polling updates.
    public sealed class PlaybackScrubber
    {
        private readonly Slider slider;
        private readonly Action<string, double> seek;
        private readonly Action<double> preview;
        private string key, gestureKey;
        private double position, duration;
        private bool active, ending;
        private TouchDevice touch;
        public bool Active { get { return active; } }
        public PlaybackScrubber(Slider slider, Action<string, double> seek, Action<double> preview)
        {
            this.slider = slider; this.seek = seek; this.preview = preview;
            slider.PreviewMouseLeftButtonDown += (s, e) => { if (touch != null || !slider.IsEnabled) return; Begin(); slider.Focus(); slider.CaptureMouse(); Move(e.GetPosition(slider).X); e.Handled = true; };
            slider.PreviewMouseMove += (s, e) => { if (active && touch == null && slider.IsMouseCaptured) { Move(e.GetPosition(slider).X); e.Handled = true; } };
            slider.PreviewMouseLeftButtonUp += (s, e) => { if (active && touch == null) { Move(e.GetPosition(slider).X); End(true); e.Handled = true; } };
            slider.LostMouseCapture += (s, e) => { if (!ending && touch == null) End(false); };
            slider.PreviewTouchDown += (s, e) => { e.Handled = true; if (active || !slider.IsEnabled) return; touch = e.TouchDevice; Begin(); touch.Capture(slider); Move(e.GetTouchPoint(slider).Position.X); };
            slider.PreviewTouchMove += (s, e) => { if (touch == e.TouchDevice) { Move(e.GetTouchPoint(slider).Position.X); e.Handled = true; } };
            slider.PreviewTouchUp += (s, e) => { if (touch == e.TouchDevice) { Move(e.GetTouchPoint(slider).Position.X); End(true); e.Handled = true; } };
            slider.LostTouchCapture += (s, e) => { if (!ending && touch == e.TouchDevice) End(false); };
            slider.PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Escape) { End(false); e.Handled = true; return; }
                if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Home || e.Key == Key.End || e.Key == Key.PageUp || e.Key == Key.PageDown) Begin();
            };
            slider.KeyUp += (s, e) => { if (active && touch == null && !slider.IsMouseCaptured) End(true); };
            slider.LostKeyboardFocus += (s, e) => { if (active && touch == null && !slider.IsMouseCaptured) End(true); };
            slider.ValueChanged += (s, e) => { if (active) preview(e.NewValue); };
        }
        public void Update(string itemKey, double current, double length, bool enabled)
        {
            if (active && (key != itemKey || !enabled)) End(false);
            key = itemKey; position = Math.Max(0, current); duration = Math.Max(0, length);
            slider.IsEnabled = enabled && duration > 0 && key != null;
            if (!active) { slider.Maximum = Math.Max(1, duration); slider.SmallChange = 5000; slider.LargeChange = 15000; slider.Value = Math.Min(position, slider.Maximum); }
        }
        // Between readings a playing song keeps moving here, a second at a time; the next reading corrects it.
        public void Advance(double milliseconds)
        {
            if (active || key == null || duration <= 0) return;
            position = Math.Min(duration, position + milliseconds);
            slider.Value = Math.Min(position, slider.Maximum);
            preview(position);
        }
        public void Begin() { if (active || !slider.IsEnabled) return; active = true; gestureKey = key; }
        private void Move(double x) { slider.Value = TouchSlider.ValueAt(slider, x); }
        public void End(bool commit)
        {
            if (!active) return;
            double value = slider.Value; string target = gestureKey; active = false; ending = true;
            if (touch != null) { touch.Capture(null); touch = null; }
            if (slider.IsMouseCaptured) slider.ReleaseMouseCapture(); ending = false;
            if (commit && target != null && target == key) seek(target, value);
            else { slider.Value = Math.Min(slider.Maximum, position); preview(position); }
        }
    }
}
