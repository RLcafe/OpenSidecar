using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace WidgetScreen
{
    // Clipboard history in three designs that follow the card's size (CardSizing.Clip; docs/design/clipboard-history.md).
    // Each copied text is a button: a tap puts it back on the clipboard and pastes it into the app in front. Small rows
    // show one line; Medium adds when it was copied; Large shows two lines and its length. Pause and Clear sit under the
    // list. The phone's ClipboardWidget.java draws the same three.
    public sealed class ClipboardCard : CardParts
    {
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private Rect bounds = Rect.Empty;
        private string built = "", listed;
        private CardSizing.Design design;
        private StackPanel rows;
        private TextBlock empty;
        private Button pause, clear;
        private IList<ClipItem> items = new ClipItem[0];
        private bool paused;

        public event Action<string> Pasted;
        public event Action PauseToggled;
        public event Action ClearAsked;

        public ClipboardCard(FrameworkElement scope, Grid host, TextBlock status) { this.host = host; this.status = status; names = new PartNames(scope); }
        public CardSizing.Design Current { get { return design; } }

        public void Fit(Rect card)
        {
            bounds = card;
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height);
            if (key != built) { built = key; Build(); }
        }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); listed = null;
            design = CardSizing.Clip(bounds.Width, bounds.Height);
            bool small = design == CardSizing.Design.Small;
            var grid = Rows(Star, Auto);
            rows = names.Add(new StackPanel(), "ClipRows");
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly, Content = rows };
            grid.Children.Add(scroll);
            empty = names.Add(Caption(""), "ClipEmpty"); empty.TextWrapping = TextWrapping.Wrap; empty.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(empty);
            var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            pause = names.Add(Tool(small), "ClipPause"); pause.Click += (sender, e) => { if (PauseToggled != null) PauseToggled(); };
            clear = names.Add(Tool(small), "ClipClear"); clear.Margin = new Thickness(8, 0, 0, 0); clear.Click += (sender, e) => { if (ClearAsked != null) ClearAsked(); };
            tools.Children.Add(pause); tools.Children.Add(clear);
            grid.Children.Add(At(tools, 1));
            host.Children.Add(names.Add(grid, "ClipView"));
            Show(items, paused);
        }
        private Button Tool(bool icon)
        {
            var button = icon ? new Button { Style = (Style)host.FindResource("IconButton"), FontSize = 18 } : new Button { Padding = new Thickness(16, 4, 16, 4) };
            if (!icon) button.SetResourceReference(Control.FontSizeProperty, "SmallSize");
            return button;
        }

        public void Show(IList<ClipItem> next, bool paused)
        {
            items = next ?? new ClipItem[0]; this.paused = paused;
            status.Text = paused ? "PAUSED" : items.Count == 0 ? "" : items.Count.ToString();
            status.SetResourceReference(TextBlock.ForegroundProperty, paused ? "Warning" : "Muted");
            if (rows == null) return;
            bool small = design == CardSizing.Design.Small;
            pause.Content = small ? (paused ? "" : "") : (paused ? "Resume" : "Pause");
            pause.ToolTip = paused ? "Resume watching the clipboard" : "Pause: new copies aren't kept";
            AutomationProperties.SetName(pause, paused ? "Resume clipboard history" : "Pause clipboard history");
            clear.Content = small ? "" : "Clear"; clear.ToolTip = "Clear the history"; clear.IsEnabled = items.Count > 0;
            AutomationProperties.SetName(clear, "Clear clipboard history");
            empty.Text = items.Count > 0 ? "" : paused ? "Paused. New copies aren't kept until you resume." : "Copy some text in any app and it shows up here. Tap it to paste it again.";
            empty.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            string key = String.Join("\n", items.Select(i => i.Id + "|" + i.AtUtc.Ticks)) + "|" + (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMinute);
            if (key == listed) return;
            listed = key;
            rows.Children.Clear();
            for (int i = 0; i < items.Count; i++) rows.Children.Add(Row(items[i], i));
        }

        // A copied text as a button; a tap pastes it.
        private Button Row(ClipItem item, int index)
        {
            var content = new StackPanel();
            bool large = design == CardSizing.Design.Large;
            var text = Text(ClipboardHistory.Preview(item.Text, large ? 240 : 120), "BodySize", "Text");
            text.TextTrimming = TextTrimming.CharacterEllipsis;
            if (large) { text.TextWrapping = TextWrapping.Wrap; text.MaxHeight = text.FontSize * 2.8; }
            content.Children.Add(text);
            if (design != CardSizing.Design.Small)
            {
                string when = ClipboardHistory.Ago(item.AtUtc, DateTime.UtcNow) + (large ? "  ·  " + item.Text.Length + (item.Text.Length == 1 ? " character" : " characters") : "");
                var caption = Caption(when); caption.Margin = new Thickness(0, 2, 0, 0); content.Children.Add(caption);
            }
            var button = new Button { Content = content, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, index == 0 ? 0 : 6, 0, 0), Background = Brushes.Transparent };
            button.SetResourceReference(Control.BackgroundProperty, "Raised");
            button.ToolTip = "Tap to paste into the app you're using";
            AutomationProperties.SetName(button, "Paste " + ClipboardHistory.Preview(item.Text, 60));
            string id = item.Id;
            button.Click += (sender, e) => { if (Pasted != null) Pasted(id); };
            return names.Add(button, "ClipItem" + index);
        }
    }
}
