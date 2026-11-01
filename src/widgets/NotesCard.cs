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
    // Notes and to-do in three designs that follow the card's size (CardSizing.Notes; docs/design/notes.md). Small and
    // Medium switch between the scratchpad and the list with two tabs; Large shows them side by side. The card only
    // draws and reports edits; DashboardController.Notes saves them through NotesStore. The phone's NotesWidget.java
    // draws the same three.
    public sealed class NotesCard : CardParts
    {
        private readonly Grid host;
        private readonly TextBlock status;
        private readonly PartNames names;
        private Rect bounds = Rect.Empty;
        private string built = "", listed = null;
        private CardSizing.Design design;
        private bool showList, applying;
        private NotesData data = new NotesData();
        private TextBox text, adding;
        private StackPanel rows;
        private Button notesTab, todoTab;

        public event Action<string> TextEdited;
        public event Action TextLeft;
        public event Action<string> Added;
        public event Action<string, bool> Toggled;
        public event Action<string> Deleted;
        public event Action ClearDoneAsked;

        public NotesCard(FrameworkElement scope, Grid host, TextBlock status) { this.host = host; this.status = status; names = new PartNames(scope); }

        public CardSizing.Design Current { get { return design; } }
        public bool ShowingList { get { return showList; } }
        // The scratchpad is being typed in, so a reading from elsewhere doesn't replace it.
        public bool Typing { get { return text != null && text.IsKeyboardFocusWithin; } }

        public void Fit(Rect card)
        {
            bounds = card;
            if (bounds.IsEmpty) return;
            string key = Math.Round(bounds.Width) + "x" + Math.Round(bounds.Height) + "|" + showList;
            if (key != built) { built = key; Build(); }
        }
        public void ShowList(bool list) { if (showList == list) return; showList = list; built = ""; Fit(bounds); }

        private void Build()
        {
            host.Children.Clear(); names.Clear(); text = adding = null; rows = null; notesTab = todoTab = null; listed = null;
            double width = Math.Max(1, bounds.Width - 48);
            design = CardSizing.Notes(bounds.Width, bounds.Height);
            FrameworkElement view;
            if (design == CardSizing.Design.Large)
            {
                var grid = Columns(Star, new GridLength(20), new GridLength(CardSizing.NotesListColumn(width)));
                var left = Rows(Auto, Star); left.Children.Add(Label("SCRATCHPAD")); var pad = Scratchpad(); pad.Margin = new Thickness(0, 8, 0, 0); left.Children.Add(At(pad, 1));
                var right = Rows(Auto, Star); right.Children.Add(Label("TO-DO")); var list = List(); list.Margin = new Thickness(0, 8, 0, 0); right.Children.Add(At(list, 1));
                grid.Children.Add(left); grid.Children.Add(At(right, 0, 2));
                view = grid;
            }
            else
            {
                var grid = Rows(Auto, Star);
                var tabs = Columns(Star, new GridLength(8), Star);
                notesTab = names.Add(TabButton("Notes", false), "NotesTabNotes"); todoTab = names.Add(TabButton("To-do", true), "NotesTabTodo");
                tabs.Children.Add(notesTab); tabs.Children.Add(At(todoTab, 0, 2));
                grid.Children.Add(tabs);
                FrameworkElement body = showList ? (FrameworkElement)List() : Scratchpad();
                body.Margin = new Thickness(0, 8, 0, 0);
                grid.Children.Add(At(body, 1));
                view = grid;
            }
            host.Children.Add(names.Add(view, "NotesView"));
            Show(data);
        }

        private Button TabButton(string label, bool list)
        {
            var button = new Button { Content = label, Padding = new Thickness(8, 4, 8, 4), FontSize = 15 };
            button.SetResourceReference(Control.FontSizeProperty, "SmallSize");
            button.Click += (sender, e) => ShowList(list);
            return button;
        }

        private TextBox Scratchpad()
        {
            text = names.Add(new TextBox { AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = NotesStore.MaxText, MinHeight = 48 }, "NotesText");
            ScrollViewer.SetPanningMode(text, PanningMode.VerticalOnly);
            AutomationProperties.SetName(text, "Scratchpad");
            text.TextChanged += (sender, e) => { if (!applying && TextEdited != null) TextEdited(text.Text); };
            text.LostKeyboardFocus += (sender, e) => { if (TextLeft != null) TextLeft(); };
            return text;
        }

        // The to-do list scrolls; its last row adds an item, and Clear done follows when something is done.
        private ScrollViewer List()
        {
            rows = names.Add(new StackPanel(), "NotesRows");
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, PanningMode = PanningMode.VerticalOnly, Content = rows };
            return scroll;
        }

        // The latest notes: the scratchpad unless it's being typed in, and the list when anything on it changed.
        public void Show(NotesData next)
        {
            data = next ?? new NotesData();
            int open = data.Items.Count(i => !i.Done);
            status.Text = open == 0 ? "" : open == 1 ? "1 TO DO" : open + " TO DO";
            if (todoTab != null)
            {
                todoTab.Content = open > 0 ? "To-do · " + open : "To-do";
                foreach (var pair in new[] { Tuple.Create(notesTab, !showList), Tuple.Create(todoTab, showList) })
                {
                    pair.Item1.SetResourceReference(Control.BorderBrushProperty, pair.Item2 ? "Accent" : "ControlBorder");
                    pair.Item1.SetResourceReference(Control.BackgroundProperty, pair.Item2 ? "Selected" : "Raised");
                    pair.Item1.BorderThickness = new Thickness(pair.Item2 ? 2 : 1);
                    AutomationProperties.SetName(pair.Item1, (pair.Item1 == notesTab ? "Notes" : "To-do") + (pair.Item2 ? ", shown" : ""));
                }
            }
            if (text != null && !Typing && text.Text != data.Text)
            {
                applying = true;
                try { int caret = text.CaretIndex; text.Text = data.Text; text.CaretIndex = Math.Min(caret, text.Text.Length); }
                finally { applying = false; }
            }
            if (rows == null) return;
            string key = String.Join("\n", data.Items.Select(i => i.Id + "|" + i.Done + "|" + i.Text));
            if (key == listed) return;
            listed = key;
            string draft = adding == null ? "" : adding.Text; bool addFocused = adding != null && adding.IsKeyboardFocusWithin;
            rows.Children.Clear();
            for (int i = 0; i < data.Items.Count; i++) rows.Children.Add(Row(data.Items[i], i));
            // The add row: a box and a button; Enter adds too.
            var add = Columns(Star, Auto); add.Margin = new Thickness(0, 6, 0, 0);
            adding = names.Add(new TextBox { MaxLength = NotesStore.MaxItem, Text = draft }, "NotesAdd");
            AutomationProperties.SetName(adding, "New to-do");
            adding.KeyDown += (sender, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
            add.Children.Add(adding);
            var button = names.Add(new Button { Content = "Add", Margin = new Thickness(8, 0, 0, 0), MinWidth = 72 }, "NotesAddButton");
            button.Click += (sender, e) => Add();
            add.Children.Add(At(button, 0, 1));
            rows.Children.Add(add);
            if (addFocused) adding.Focus();
            if (data.Items.Any(i => i.Done))
            {
                var clear = names.Add(new Button { Content = "Clear done", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(14, 4, 14, 4) }, "NotesClearDone");
                clear.SetResourceReference(Control.FontSizeProperty, "SmallSize");
                clear.Click += (sender, e) => { if (ClearDoneAsked != null) ClearDoneAsked(); };
                rows.Children.Add(clear);
            }
        }
        private void Add()
        {
            if (adding == null || adding.Text.Trim().Length == 0) return;
            string value = adding.Text; adding.Text = "";
            if (Added != null) Added(value);
        }

        // A to-do row: a check box with the text (done items dimmed) and a delete button.
        private Grid Row(TodoItem item, int index)
        {
            var row = Columns(Star, new GridLength(48));
            var box = new CheckBox { IsChecked = item.Done, Content = new TextBlock { Text = item.Text, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, ToolTip = item.Text }, VerticalAlignment = VerticalAlignment.Center };
            ((TextBlock)box.Content).SetResourceReference(TextBlock.ForegroundProperty, item.Done ? "Muted" : "Text");
            if (item.Done) ((TextBlock)box.Content).TextDecorations = TextDecorations.Strikethrough;
            AutomationProperties.SetName(box, item.Text);
            string id = item.Id;
            box.Click += (sender, e) => { if (Toggled != null) Toggled(id, box.IsChecked == true); };
            row.Children.Add(names.Add(box, "NotesItem" + index));
            var delete = new Button { Content = "", Style = (Style)host.FindResource("IconButton"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 14, ToolTip = "Delete" };
            AutomationProperties.SetName(delete, "Delete " + item.Text);
            delete.Click += (sender, e) => { if (Deleted != null) Deleted(id); };
            row.Children.Add(At(names.Add(delete, "NotesDelete" + index), 0, 1));
            return row;
        }
    }
}
