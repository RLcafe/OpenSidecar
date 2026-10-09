using System;
using System.Windows;
using System.Windows.Media;

namespace WidgetScreen
{
    public sealed class LevelVisualizer : FrameworkElement
    {
        private readonly double[] samples = new double[48];
        public Brush BarBrush { get; set; }
        public LevelVisualizer() { BarBrush = Brushes.PaleGreen; IsHitTestVisible = false; }
        public void Push(double peak)
        {
            Array.Copy(samples, 1, samples, 0, samples.Length - 1);
            samples[samples.Length - 1] = Math.Max(0, Math.Min(1, peak));
            InvalidateVisual();
        }
        public void Clear() { Array.Clear(samples, 0, samples.Length); InvalidateVisual(); }
        protected override void OnRender(DrawingContext drawing)
        {
            base.OnRender(drawing);
            double step = ActualWidth / samples.Length;
            for (int i = 0; i < samples.Length; i++)
            {
                double height = 2 + Math.Sqrt(samples[i]) * Math.Max(0, ActualHeight - 2);
                drawing.DrawRoundedRectangle(BarBrush, null, new Rect(i * step, (ActualHeight - height) / 2, Math.Max(1, step - 4), height), 2, 2);
            }
        }
    }
}
