using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;

namespace WidgetScreen
{
    // System stats rows have one fixed design size. The card's Viewbox scales them uniformly,
    // so readings grow with the widget; this picks the column count that lets them grow the most.
    public static class SystemStatsLayout
    {
        public const int MaxColumns = 3;

        public static double Scale(Size available, Size design)
        {
            if (design.Width <= 0 || design.Height <= 0) return 0;
            return Math.Max(0, Math.Min(available.Width / design.Width, available.Height / design.Height));
        }

        public static int BestColumns(Size available, Size cell, int count)
        {
            int best = 1; double bestScale = -1;
            for (int columns = 1; columns <= Math.Min(MaxColumns, count); columns++)
            {
                int rows = (count + columns - 1) / columns;
                double scale = Scale(available, new Size(cell.Width * columns, cell.Height * rows));
                // Fewer columns win ties so a reading list stays in its natural top-to-bottom order.
                if (scale > bestScale + 0.001) { best = columns; bestScale = scale; }
            }
            return best;
        }

        public static void Attach(FrameworkElement body, UniformGrid metrics)
        {
            body.SizeChanged += (sender, e) => Update(body, metrics);
        }

        // Rows keep their design height and at least their MinWidth. After choosing columns, rows widen so the
        // scaled grid fills the card's width exactly; bars and the CPU graph use the extra room.
        public static void Update(FrameworkElement body, UniformGrid metrics)
        {
            var rows = metrics.Children.OfType<FrameworkElement>().Where(row => row.Visibility != Visibility.Collapsed).ToArray();
            if (rows.Length == 0) return;
            var available = new Size(body.ActualWidth, body.ActualHeight);
            double minimum = rows[0].MinWidth, horizontal = rows[0].Margin.Left + rows[0].Margin.Right;
            var cell = new Size(minimum + horizontal, rows[0].Height + rows[0].Margin.Top + rows[0].Margin.Bottom);
            int columns = BestColumns(available, cell, rows.Length);
            if (metrics.Columns != columns) metrics.Columns = columns;
            double scale = Scale(available, new Size(cell.Width * columns, cell.Height * ((rows.Length + columns - 1) / columns)));
            double width = scale > 0 ? Math.Max(minimum, available.Width / scale / columns - horizontal) : minimum;
            foreach (var row in metrics.Children.OfType<FrameworkElement>()) row.Width = width;
        }
    }
}
