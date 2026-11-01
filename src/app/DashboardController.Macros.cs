using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WidgetScreen
{
    // The Macros widget (docs/design/macros.md): six buttons a page like Quick Launch, each running a chain of steps
    // in order. A macro with a script or a consequential step asks first on the confirm sheet, listing every step.
    // One macro runs at a time; a failing step stops the rest and says which one.
    public sealed partial class DashboardController
    {
        private MacroEditor macroEditor;
        private int macroPage, macroRunning = -1, macroStep;
        private string macroShape = "";
        // For the checks (tests/Macros/MacroTests.cs).
        public int MacroRunning { get { return macroRunning; } }

        private void InitializeMacros()
        {
            macroEditor = new MacroEditor(window, Control<Grid>("MacroEditorHost"), message => Text("SettingsStatus", message));
            Button("EditMacros", () => OpenSettingsTab("Macros"));
            Button("EmptyMacros", () => OpenSettingsTab("Macros"));
            Button("MacroPrevious", () => { macroPage--; BuildMacros(); });
            Button("MacroNext", () => { macroPage++; BuildMacros(); });
        }

        private Macro[] Macros { get { return settings.Macros ?? new Macro[0]; } }
        // The button grid: the card less its 20-unit padding, border and 48-unit header, as Quick Launch.
        private Size MacroArea() { var b = Editor.Bounds("Macros"); return new Size(Math.Max(0, b.Width - 42), Math.Max(0, b.Height - 90)); }
        private string MacroShape() { var a = MacroArea(); var s = CardSizing.DeckGrid(a.Width, a.Height, Macros.Length); return s[0] + "x" + s[1] + "|" + Math.Round(a.Width) + "x" + Math.Round(a.Height); }
        private void FitMacros() { if (macroShape != MacroShape()) BuildMacros(); }

        // The same six-a-page grid, labels and tiles as Quick Launch (CardSizing.DeckGrid, DeckLabel, Tile).
        private void BuildMacros()
        {
            var grid = Control<UniformGrid>("MacroGrid"); grid.Children.Clear();
            macroShape = MacroShape();
            var macros = Macros; var area = MacroArea();
            int[] shape = CardSizing.DeckGrid(area.Width, area.Height, macros.Length);
            int pageSize = shape[0] * shape[1], pages = Math.Max(1, (macros.Length + pageSize - 1) / pageSize);
            double cellWidth = area.Width / shape[0], cellHeight = area.Height / shape[1];
            var labelMode = CardSizing.DeckLabel(cellWidth, cellHeight); bool beside = labelMode == CardSizing.DeckLabelMode.Beside;
            macroPage = Math.Max(0, Math.Min(pages - 1, macroPage));
            grid.Columns = shape[0]; grid.Rows = shape[1];
            double cardWidth = Editor.Bounds("Macros").Width;
            Control<StackPanel>("MacroPages").Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("EditMacros").Visibility = CardSizing.DeckEditShown(cardWidth) ? Visibility.Visible : Visibility.Collapsed;
            Control<FrameworkElement>("MacroHeading").Visibility = CardSizing.HeadingShown(cardWidth, pages > 1) ? Visibility.Visible : Visibility.Collapsed;
            Control<Button>("MacroPrevious").IsEnabled = macroPage > 0; Control<Button>("MacroNext").IsEnabled = macroPage < pages - 1;
            Text("MacroPageLabel", (macroPage + 1) + "/" + pages);
            Control<Button>("EmptyMacros").Visibility = macros.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            for (int i = macroPage * pageSize; i < Math.Min(macros.Length, (macroPage + 1) * pageSize); i++)
            {
                var macro = macros[i]; int index = i;
                string named = labelMode == CardSizing.DeckLabelMode.None ? macro.Label + Environment.NewLine : "";
                var button = new Button { Margin = new Thickness(4), Padding = new Thickness(8), ToolTip = named + MacroActions.Detail(macro), Tag = index };
                double glyphSize = CardSizing.DeckGlyph(cellHeight, beside);
                var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Orientation = beside ? Orientation.Horizontal : Orientation.Vertical };
                var glyph = new TextBlock { Text = macro.Glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = glyphSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                string hex = DeckActions.AccentHex(macro.Color);
                if (hex != null) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); glyph.Foreground = brush; } else glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                content.Children.Add(glyph);
                var name = new TextBlock { Text = macro.Label, FontSize = (beside ? 14 : 15) * settings.TextScale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = beside ? new Thickness(8, 0, 0, 0) : new Thickness(0, 8, 0, 0), MaxWidth = Math.Max(40, beside ? cellWidth - glyphSize - 40 : cellWidth - 24), TextTrimming = TextTrimming.CharacterEllipsis };
                if (labelMode == CardSizing.DeckLabelMode.None) name.Visibility = Visibility.Collapsed;
                content.Children.Add(name);
                // Large cells are tiles: the glyph, the name and how many steps, from the top left.
                if (CardSizing.Tile(cellWidth, cellHeight) == CardSizing.Design.Large)
                {
                    button.Padding = new Thickness(16, 12, 12, 12); button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    content.HorizontalAlignment = HorizontalAlignment.Left; content.VerticalAlignment = VerticalAlignment.Stretch;
                    glyph.HorizontalAlignment = HorizontalAlignment.Left; glyph.FontSize = Math.Min(40, glyphSize);
                    name.HorizontalAlignment = HorizontalAlignment.Left; name.FontWeight = FontWeights.SemiBold; name.FontSize = 17 * settings.TextScale; name.Margin = new Thickness(0, 12, 0, 0); name.MaxWidth = cellWidth - 44;
                    var kind = new TextBlock { Text = MacroActions.Summary(macro) + (MacroActions.NeedsConfirmation(macro) ? "  ·  asks first" : ""), FontSize = 13, Margin = new Thickness(0, 3, 0, 0), MaxWidth = cellWidth - 44, TextTrimming = TextTrimming.CharacterEllipsis };
                    kind.SetResourceReference(TextBlock.ForegroundProperty, "Muted"); content.Children.Add(kind);
                }
                button.Content = content; AutomationProperties.SetName(button, macro.Label);
                button.Click += (sender, e) => RunMacro(index);
                grid.Children.Add(button);
            }
            ShowMacroRunning();
        }

        // The running macro's button takes the selected look and says which step it's on.
        private void ShowMacroRunning()
        {
            foreach (var button in Control<UniformGrid>("MacroGrid").Children.OfType<Button>())
            {
                bool on = button.Tag is int && (int)button.Tag == macroRunning;
                button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, on ? "Accent" : "ControlBorder");
                button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, on ? "Selected" : "Raised");
                button.BorderThickness = new Thickness(on ? 2 : 1);
                var macro = button.Tag is int && (int)button.Tag < Macros.Length ? Macros[(int)button.Tag] : null;
                if (macro != null) AutomationProperties.SetName(button, macro.Label + (on ? ", running step " + (macroStep + 1) + " of " + macro.Steps.Length : ""));
            }
        }

        private async void RunMacro(int index) { await RunMacroAsync(index, false); }

        // Returns null when the macro ran (or was cancelled at the confirm sheet), or what went wrong.
        private async Task<string> RunMacroAsync(int index, bool confirmed)
        {
            var macros = Macros;
            if (index < 0 || index >= macros.Length) return "That macro isn't there anymore.";
            var macro = macros[index].Copy();
            if (macroRunning >= 0) { Notify(macros[macroRunning].Label + " is still running."); return macros[macroRunning].Label + " is still running."; }
            string error = MacroActions.Validate(macro);
            if (error != null) { Notify(macro.Label + ": " + error); return macro.Label + ": " + error; }
            if (!confirmed && MacroActions.NeedsConfirmation(macro))
            {
                bool run = await confirm.AskAsync("MACRO", macro.Label, MacroActions.Detail(macro), "Run " + macro.Label);
                if (disposed || !run) return null;
            }
            macroRunning = index; macroStep = 0; ShowMacroRunning();
            try
            {
                for (macroStep = 0; macroStep < macro.Steps.Length; macroStep++)
                {
                    if (disposed) return null;
                    ShowMacroRunning();
                    var step = macro.Steps[macroStep];
                    try { await RunMacroStep(step); }
                    catch (Exception e)
                    {
                        string message = macro.Label + " stopped at step " + (macroStep + 1) + " (" + MacroActions.Describe(step) + "): " + e.Message;
                        if (!disposed) Notify(message);
                        return message;
                    }
                }
                if (!disposed) Notify("Ran " + macro.Label + ".");
                return null;
            }
            finally { macroRunning = -1; if (!disposed) ShowMacroRunning(); }
        }

        private async Task RunMacroStep(MacroStep step)
        {
            switch (step.Kind)
            {
                case "Delay": await Task.Delay(step.DelayMs); break;
                case "Hotkey": await deckHotkeys.SendAsync(step.Target); break;
                case "Control": RunControl(step.Target); break;
                case "Script": MacroActions.StartScript(step); break;
                default: DeckActions.Open(MacroActions.AsShortcut(step)); break;
            }
        }

        // ----- The phone -----

        private Dictionary<string, object> RemoteMacros()
        {
            return D("running", macroRunning, "step", macroStep,
                "items", Macros.Select(m => (object)D("label", m.Label, "glyph", String.IsNullOrEmpty(m.Glyph) ? "E768" : ((int)m.Glyph[0]).ToString("X4"), "color", m.Color ?? "",
                    "confirm", MacroActions.NeedsConfirmation(m), "steps", m.Steps.Select(s => (object)MacroActions.Describe(s)).ToArray())).ToArray());
        }
        // The phone asks first when the macro needs it, so it passes confirmed. The label guards against the list
        // having changed on the PC in between.
        private async Task<RemoteResponse> RemoteMacroCommand(Dictionary<string, object> body)
        {
            int index = (int)Required(body, "index"); string label = Str(body, "label");
            if (index < 0 || index >= Macros.Length || (label != null && label != Macros[index].Label)) throw new InvalidOperationException("That macro changed on the PC. Try again.");
            if (!Flag(body, "confirmed") && MacroActions.NeedsConfirmation(Macros[index])) throw new InvalidOperationException("This macro asks first. Update Widget Screen on the phone.");
            string error = await RunMacroAsync(index, true);
            if (error != null) throw new InvalidOperationException(error);
            return Done("Ran " + Macros[index].Label + " on the PC.");
        }
    }
}
