using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace WidgetScreen
{
    // The Sticker Sheet mixer (docs/design/sticker-sheet.md, phase 2): Large cards (the Full mixer, at least 420 tall)
    // show channel strips, master first with an LED ladder, then one 88-unit strip per app that fits; Apps opens the
    // list with every app. The microphone gets an ON AIR badge in the heading, and the level meter is LED segments.
    // Strips are built when the card's size or the set of apps changes and only updated otherwise.
    public sealed partial class DashboardController
    {
        private const double MasterStripWidth = CardSizing.MasterStripWidth, AppStripWidth = CardSizing.AppStripWidth, StripGap = CardSizing.StripGap;
        private sealed class AppStrip { public Border Root; public Slider Slider; public Button Mute, Output; public TextBlock Badge, Name, Percent, Device; public AudioSession Session; }
        private Border masterStrip;
        private Slider masterStripSlider;
        private TextBlock masterStripPercent, masterStripDevice;
        private Button masterStripMute;
        private LedMeter masterLadder;
        private readonly List<AppStrip> appStrips = new List<AppStrip>();
        private int stripSlots = -1;
        private bool stripsShown;
        public bool MixerStripsShown { get { return stripsShown; } }
        public int MixerStripSlots { get { return Math.Max(0, stripSlots); } }


        private void FitStrips(Rect bounds, bool shown)
        {
            stripsShown = shown;
            var host = Control<Grid>("MixerStrips"); Show(host, shown);
            if (!shown) return;
            if (masterStrip == null) BuildMasterStrip(host);
            int slots = CardSizing.MixerStripSlots(bounds.Width);
            if (slots != stripSlots) { stripSlots = slots; LayOutStrips(host); }
            UpdateStrips(lastAudio);
        }

        private void LayOutStrips(Grid host)
        {
            host.Children.Clear(); host.ColumnDefinitions.Clear(); appStrips.Clear();
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MasterStripWidth) });
            host.Children.Add(masterStrip);
            for (int i = 0; i < stripSlots; i++)
            {
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(StripGap) });
                host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(AppStripWidth) });
                var strip = CreateAppStrip(); Grid.SetColumn(strip.Root, 2 + i * 2); host.Children.Add(strip.Root); appStrips.Add(strip);
            }
        }

        private static Border StripBox(UIElement child)
        {
            var box = new Border { CornerRadius = new CornerRadius(14), Padding = new Thickness(6, 10, 6, 6), Child = child };
            box.SetResourceReference(Border.BackgroundProperty, "Raised");
            return box;
        }
        private TextBlock StripText(double size, string brush, FontWeight weight)
        {
            var text = new TextBlock { FontSize = size, FontWeight = weight, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            text.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return text;
        }
        private Button StripButton(object content, string name, Action action)
        {
            var button = new Button { Content = content, MinHeight = 48, MinWidth = 48, Padding = new Thickness(4, 0, 4, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(button, name);
            button.Click += (sender, e) => action();
            return button;
        }
        private static Grid StripRows(params GridLength[] heights) { var grid = new Grid(); foreach (var h in heights) grid.RowDefinitions.Add(new RowDefinition { Height = h }); return grid; }
        private static T Row<T>(T element, int row) where T : UIElement { Grid.SetRow(element, row); return element; }

        private void BuildMasterStrip(Grid host)
        {
            var rows = StripRows(GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
            var label = new SpacedText { Text = "MASTER", FontSize = 13, FontWeight = FontWeights.SemiBold, Spacing = 0.08, HorizontalAlignment = HorizontalAlignment.Center };
            label.SetResourceReference(SpacedText.ForegroundProperty, "Muted"); rows.Children.Add(label);
            masterStripPercent = Row(StripText(30 * Math.Min(1.2, settings.TextScale), "Text", FontWeights.Light), 1); Typography.SetNumeralAlignment(masterStripPercent, FontNumeralAlignment.Tabular); rows.Children.Add(masterStripPercent);
            var fader = Row(new Grid { Margin = new Thickness(0, 6, 0, 6) }, 2);
            fader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); fader.ColumnDefinitions.Add(new ColumnDefinition());
            masterLadder = new LedMeter { Vertical = true, Segment = 8, Width = 10, Margin = new Thickness(0, 26, 0, 26) }; fader.Children.Add(masterLadder);
            masterStripSlider = new Slider { Style = (Style)window.Resources["VerticalSlider"] }; Grid.SetColumn(masterStripSlider, 1); fader.Children.Add(masterStripSlider);
            AutomationProperties.SetName(masterStripSlider, "Master volume");
            TouchSlider.Attach(masterStripSlider);
            masterStripSlider.ValueChanged += (sender, e) => { if (!updatingAudio) { double value = e.NewValue / 100; AudioAction(a => a.SetVolume(value), false); masterStripPercent.Text = Math.Round(e.NewValue) + "%"; Text("MasterPercent", Math.Round(e.NewValue) + "%"); } };
            rows.Children.Add(fader);
            masterStripMute = Row(StripButton("", "Mute output", () => AudioAction(a => a.ToggleMute())), 3);
            masterStripMute.FontFamily = Stickers.Glyphs; masterStripMute.FontSize = 18; rows.Children.Add(masterStripMute);
            masterStripDevice = StripText(13, "Muted", FontWeights.Normal);
            var device = Row(StripButton(masterStripDevice, "Choose output device", OpenMasterOutputs), 4); rows.Children.Add(device);
            masterStrip = StripBox(rows);
        }

        private AppStrip CreateAppStrip()
        {
            var strip = new AppStrip();
            var rows = StripRows(GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto);
            // A round badge in the mixer's ink with the app's initial.
            strip.Badge = new TextBlock { FontSize = 14, FontWeight = FontWeights.Black, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            strip.Badge.SetResourceReference(TextBlock.ForegroundProperty, "InkText");
            var badge = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), HorizontalAlignment = HorizontalAlignment.Center, Child = strip.Badge };
            badge.SetResourceReference(Border.BackgroundProperty, Stickers.InkKey("Mixer")); rows.Children.Add(badge);
            strip.Name = Row(StripText(13 * settings.TextScale, "Text", FontWeights.SemiBold), 1); strip.Name.Margin = new Thickness(0, 6, 0, 0); rows.Children.Add(strip.Name);
            strip.Percent = Row(StripText(22 * Math.Min(1.2, settings.TextScale), "Text", FontWeights.Light), 2); Typography.SetNumeralAlignment(strip.Percent, FontNumeralAlignment.Tabular); rows.Children.Add(strip.Percent);
            strip.Slider = Row(new Slider { Style = (Style)window.Resources["VerticalSlider"], Margin = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Center }, 3); rows.Children.Add(strip.Slider);
            TouchSlider.Attach(strip.Slider);
            strip.Slider.ValueChanged += (sender, e) => { if (!updatingAudio && strip.Session != null) { string id = strip.Session.Id; double value = e.NewValue / 100; AudioAction(a => a.SetSessionVolume(id, value), false); strip.Percent.Text = Math.Round(e.NewValue) + "%"; } };
            strip.Mute = Row(StripButton("", "Mute", () => { if (strip.Session != null) { string id = strip.Session.Id; AudioAction(a => a.ToggleSessionMute(id)); } }), 4);
            strip.Mute.FontFamily = Stickers.Glyphs; strip.Mute.FontSize = 18; rows.Children.Add(strip.Mute);
            strip.Device = StripText(13, "Muted", FontWeights.Normal);
            strip.Output = Row(StripButton(strip.Device, "Output", () => { if (strip.Session != null) OpenAppOutputs(strip.Session); }), 5); rows.Children.Add(strip.Output);
            strip.Root = StripBox(rows);
            return strip;
        }

        // Fills the strips from the last reading: the master, then the first apps in the reading's order.
        private void UpdateStrips(AudioSnapshot snapshot)
        {
            if (!stripsShown || masterStrip == null) return;
            bool available = snapshot != null && audioAvailable;
            masterStripSlider.IsEnabled = available; masterStripMute.IsEnabled = available;
            masterStripPercent.Text = available ? Math.Round(snapshot.Volume * 100) + "%" : "—";
            if (available && !masterStripSlider.IsMouseCaptureWithin && !masterStripSlider.AreAnyTouchesCapturedWithin) { bool was = updatingAudio; updatingAudio = true; masterStripSlider.Value = snapshot.Volume * 100; updatingAudio = was; }
            masterStripMute.Content = available && snapshot.Muted ? "" : "";
            AutomationProperties.SetName(masterStripMute, available && snapshot.Muted ? "Unmute output" : "Mute output");
            masterStripDevice.Text = available ? ShortDevice(snapshot.Device) + " ▾" : "No output";
            var sessions = available ? snapshot.Sessions : new List<AudioSession>();
            for (int i = 0; i < appStrips.Count; i++)
            {
                var strip = appStrips[i]; var session = i < sessions.Count ? sessions[i] : null;
                strip.Session = session; strip.Root.Visibility = session == null ? Visibility.Hidden : Visibility.Visible;
                if (session == null) continue;
                strip.Badge.Text = String.IsNullOrEmpty(session.Name) ? "?" : session.Name.Substring(0, 1).ToUpperInvariant();
                strip.Name.Text = session.Name; strip.Name.ToolTip = session.Name;
                strip.Percent.Text = Math.Round(session.Volume * 100) + "%";
                if (!strip.Slider.IsMouseCaptureWithin && !strip.Slider.AreAnyTouchesCapturedWithin) { bool was = updatingAudio; updatingAudio = true; strip.Slider.Value = session.Volume * 100; updatingAudio = was; }
                AutomationProperties.SetName(strip.Slider, session.Name + " volume");
                strip.Mute.Content = session.Muted ? "" : "";
                strip.Mute.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, session.Muted ? "Warning" : "Text");
                AutomationProperties.SetName(strip.Mute, (session.Muted ? "Unmute " : "Mute ") + session.Name);
                var device = outputs.FirstOrDefault(d => SameOutput(d.Id, session.DeviceId));
                strip.Device.Text = ShortDevice(device != null ? device.Name : "Output");
                AutomationProperties.SetName(strip.Output, "Output for " + session.Name + ": " + (device != null ? device.Name : "unknown"));
            }
            Text("SessionCount", sessions.Count == 0 ? "" : sessions.Count.ToString()); UpdateMixerApps();
        }

        // "Speakers (Logi Z407)" reads "Z407" on a strip: the part in brackets, or the first word.
        public static string ShortDevice(string name)
        {
            if (String.IsNullOrWhiteSpace(name)) return "";
            int open = name.LastIndexOf('('), close = name.LastIndexOf(')');
            string inner = open >= 0 && close > open ? name.Substring(open + 1, close - open - 1).Trim() : name.Trim();
            var words = inner.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Length == 0 ? inner : words[words.Length - 1].Length >= 3 ? words[words.Length - 1] : inner;
        }

        // The heading's microphone badge: ON AIR (filled) while it's live, MUTED while muted, nothing without one.
        private void ShowMicState(bool? muted)
        {
            Text("MicState", !muted.HasValue ? "" : muted.Value ? "MUTED" : "ON AIR");
            var sticker = StatusSticker.For(Control<TextBlock>("MicState"));
            if (sticker != null) { sticker.Filled = muted == false; sticker.Glyph = muted.HasValue ? (muted.Value ? "" : "") : null; }
        }

        // The level meter's newest reading: the row under the master slider, or the master strip's ladder.
        private void PushLevel(double peak)
        {
            levels.Level = peak;
            if (masterLadder != null) masterLadder.Level = stripsShown ? peak : 0;
        }
        private void ClearLevels() { levels.Level = 0; if (masterLadder != null) masterLadder.Level = 0; }
    }
}
