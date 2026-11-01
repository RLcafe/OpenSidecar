using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WidgetScreen
{
    // Sticker Sheet foundations (decision 017, docs/design/sticker-sheet.md): each card's label strip and status sticker,
    // the ink resources, Settings → Theme → Widget colours, and the ink picker in Edit layout's list. Card bodies are
    // unchanged in this phase; the Claude Code card keeps its own design.
    public sealed partial class DashboardController
    {
        // The strip hosts in MainWindow.xaml, by widget (Weather's strip is built by WeatherCard).
        private static readonly string[][] StripHosts = {
            new[] { "Focus", "FocusStrip" }, new[] { "Media", "MediaStrip" }, new[] { "Shortcuts", "DeckHeading" }, new[] { "Mixer", "MixerStrip" },
            new[] { "System", "SystemStrip" }, new[] { "Spotify", "SpotifyStrip" }, new[] { "Soundboard", "SoundboardHeading" }, new[] { "Storage", "StorageStrip" },
            new[] { "Prayer", "PrayerStrip" }, new[] { "Notes", "NotesStrip" }, new[] { "Clipboard", "ClipboardStrip" }, new[] { "Macros", "MacroHeading" },
            new[] { "Calendar", "CalendarStrip" }, new[] { "Games", "GamesHeading" }, new[] { "Home", "HomeStrip" }, new[] { "Clocks", "ClocksStrip" } };
        // The right-hand captions that become status stickers. Those drawn in the warning colour only speak up for problems.
        private static readonly string[][] StatusCaptions = {
            new[] { "Focus", "FocusState", "" }, new[] { "Media", "MediaSource", "" }, new[] { "Mixer", "MicState", "" }, new[] { "Mixer", "AudioStatus", "warns" }, new[] { "System", "SystemUptime", "" }, new[] { "System", "SystemStatus", "warns" },
            new[] { "Storage", "StorageStatus", "warns" }, new[] { "Prayer", "PrayerStatus", "" }, new[] { "Notes", "NotesStatus", "" }, new[] { "Clipboard", "ClipStatus", "" },
            new[] { "Calendar", "CalendarStatus", "warns" }, new[] { "Home", "HomeStatus", "warns" }, new[] { "Clocks", "ClocksStatus", "" } };

        private readonly Dictionary<string, LabelStrip> strips = new Dictionary<string, LabelStrip>();
        private readonly List<StatusSticker> statusStickers = new List<StatusSticker>();
        private double stripScale = 1;
        // The inks being chosen while Edit layout is open (null otherwise); saved with the layout, dropped by Cancel.
        private string editInks;
        private static readonly string[] InkModeNames = { "Each its own", "Accent only" };
        public IReadOnlyDictionary<string, LabelStrip> Strips { get { return strips; } }
        public IReadOnlyList<StatusSticker> StatusStickers { get { return statusStickers; } }
        public string EditInks { get { return editInks; } }
        public LabelStrip WeatherStrip { get { return weatherCard.Strip; } }

        // Runs before the card controllers are made, so they keep using their caption TextBlocks by name.
        private void InitializeStickers()
        {
            foreach (var host in StripHosts) strips[host[0]] = new LabelStrip(Control<Border>(host[1]), host[0]);
            foreach (var caption in StatusCaptions) statusStickers.Add(StatusSticker.Wrap(Control<TextBlock>(caption[1]), caption[0], caption[2] == "warns"));
        }

        private string ChosenInkMode() { return Control<ComboBox>("InkModeChoice").SelectedIndex == 1 ? Inks.AccentOnly : Inks.EachOwn; }

        // The eight ink tokens ("InkLime"…"InkLemon"), the dark text on them, and each widget's ink ("InkOf<id>").
        private void ApplyInks(string inks, string mode, string accent)
        {
            foreach (string name in Inks.Names) window.Resources["Ink" + name] = Frozen(Inks.Hex(name));
            window.Resources["InkText"] = Frozen(Inks.DarkText);
            foreach (string id in WidgetLayout.Ids) window.Resources[Stickers.InkKey(id)] = Frozen(Inks.Hex(Inks.Resolve(id, inks, mode, accent)));
        }
        private static readonly Dictionary<string, SolidColorBrush> inkBrushes = new Dictionary<string, SolidColorBrush>();
        private static SolidColorBrush Frozen(string hex)
        {
            SolidColorBrush brush;
            if (inkBrushes.TryGetValue(hex, out brush)) return brush;
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); inkBrushes[hex] = brush;
            return brush;
        }

        // Strips fold to a dot and a short name on narrow cards, and follow the text size.
        private void FitStrips()
        {
            foreach (var strip in strips.Values) strip.Fit(Editor.Bounds(strip.Id).Width, stripScale);
        }

        // ----- Edit layout's ink picker -----

        private StackPanel inkPicker;
        private TextBlock inkPickerTitle, inkPickerNote;
        private readonly Dictionary<string, Button> inkSwatches = new Dictionary<string, Button>();

        // The widget's name in Edit layout's list, after a dot in its ink.
        private FrameworkElement InkLabel(string id)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            if (Inks.InScope(id)) dot.SetResourceReference(Shape.FillProperty, Stickers.InkKey(id)); else dot.Visibility = Visibility.Hidden;
            row.Children.Add(dot); row.Children.Add(new TextBlock { Text = WidgetLayout.Title(id), VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        private StackPanel BuildInkPicker()
        {
            inkSwatches.Clear();
            inkPicker = new StackPanel();
            inkPickerTitle = PanelLabel("INK", 4); inkPicker.Children.Add(inkPickerTitle);
            var swatches = new WrapPanel();
            foreach (string name in Inks.Choices)
            {
                string ink = name;
                var swatch = new Button { Width = 48, Height = 48, MinWidth = 48, Padding = new Thickness(0), Margin = new Thickness(0, 0, 4, 4), FontFamily = Stickers.Glyphs, FontSize = 18, ToolTip = ink };
                swatch.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Ink" + ink);
                swatch.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "InkText");
                swatch.Click += (sender, e) => ChooseInk(ink);
                inkSwatches[ink] = swatch; swatches.Children.Add(swatch);
            }
            inkPicker.Children.Add(swatches);
            inkPickerNote = new TextBlock { Margin = new Thickness(4, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
            inkPickerNote.SetResourceReference(FrameworkElement.StyleProperty, "Small");
            inkPicker.Children.Add(inkPickerNote);
            return inkPicker;
        }

        private void UpdateInkPicker()
        {
            if (inkPicker == null) return;
            string id = Editor.SelectedId; bool inScope = Inks.InScope(id);
            inkPickerTitle.Text = "INK FOR " + WidgetLayout.Title(id).ToUpperInvariant();
            string chosen = Inks.Chosen(id, editInks ?? settings.WidgetInks);
            foreach (var pair in inkSwatches)
            {
                bool on = inScope && pair.Key == chosen;
                pair.Value.IsEnabled = inScope;
                pair.Value.Content = on ? "" : null;
                pair.Value.BorderThickness = new Thickness(on ? 3 : 1);
                pair.Value.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, on ? "Text" : "ControlBorder");
                AutomationProperties.SetName(pair.Value, pair.Key + " ink" + (on ? ", chosen" : ""));
            }
            inkPickerNote.Text = !inScope ? "Claude Code keeps its own colours."
                : settings.WidgetColours == Inks.AccentOnly ? "Widget colours is Accent only (Settings → Theme), so every card wears the accent until you choose Each its own."
                : "Saved with the layout. The phone shows it too.";
        }

        public void ChooseInk(string ink)
        {
            string id = Editor.SelectedId;
            if (!Editor.Editing || !Inks.InScope(id) || !Inks.Choices.Contains(ink)) return;
            editInks = Inks.With(editInks ?? settings.WidgetInks, id, ink);
            ApplyInks(editInks, settings.WidgetColours, settings.Accent);
            UpdateInkPicker();
            Notify(WidgetLayout.Title(id) + " in " + ink + ". Save layout to keep it.");
        }

        // ----- The phone link -----

        // Each widget's chosen ink and the Widget colours setting, in the layout section.
        private Dictionary<string, object> RemoteInks()
        {
            var inks = new Dictionary<string, object>();
            foreach (string id in WidgetLayout.Ids.Where(Inks.InScope)) inks[id] = Inks.Chosen(id, settings.WidgetInks);
            return inks;
        }

        // The phone changes inks with its layout save, or alone ("layout.inks", Settings → Theme on the phone).
        private void TakeRemoteInks(Dictionary<string, object> body)
        {
            var inks = body.ContainsKey("inks") ? body["inks"] as Dictionary<string, object> : null;
            if (inks != null)
            {
                string chosen = settings.WidgetInks;
                foreach (var pair in inks)
                {
                    var ink = pair.Value as string;
                    if (Inks.InScope(pair.Key) && Inks.Choices.Contains(ink)) chosen = Inks.With(chosen, pair.Key, ink);
                }
                settings.WidgetInks = chosen;
            }
            string mode = Str(body, "colours");
            if (mode != null) settings.WidgetColours = Inks.Mode(mode);
        }
    }
}
