using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WidgetScreen
{
    // Card contents follow their card's size (CardSizing; docs/design/any-screen-size.md and
    // docs/design/resizable-widgets.md). Big readings grow with the card; small cards fold secondary rows away and
    // move what stays into the room left, so every card keeps its main reading and controls at full touch size.
    public sealed partial class DashboardController
    {
        // A Master mixer card shows the app list in place of the master volume while this is set.
        private bool mixerAppsOpen;

        private void FitCards()
        {
            FitFocus(Editor.Bounds("Focus"));
            FitMedia(Editor.Bounds("Media"));
            FitMixer(Editor.Bounds("Mixer"));
            // System stats and Weather switch between a Small, Medium and Large design (docs/design/widget-size-views.md).
            systemCard.Fit(Editor.Bounds("System"));
            weatherCard.Fit(Editor.Bounds("Weather"));
            storageCard.Fit(Editor.Bounds("Storage"));
            clocksCard.Fit(Editor.Bounds("Clocks"));
            prayerCard.Fit(Editor.Bounds("Prayer")); RenderPrayer();
            notesCard.Fit(Editor.Bounds("Notes"));
            FitSpotify(Editor.Bounds("Spotify"));
            var board = Editor.Bounds("Soundboard");
            Control<TextBlock>("SoundboardChannel").MaxWidth = CardSizing.SoundboardChannelShown(board.Width) ? Double.PositiveInfinity : 0;
            Show("SoundboardHeading", CardSizing.HeadingShown(board.Width, Control<FrameworkElement>("SoundboardPages").Visibility == Visibility.Visible));
            FitEmpty(null, "SoundboardEmptyTitle", "SoundboardEmptyText", board.Height - 90);
        }
        private void Show(string name, bool shown) { Control<FrameworkElement>(name).Visibility = shown ? Visibility.Visible : Visibility.Collapsed; }
        private static void Show(FrameworkElement element, bool shown) { element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed; }

        // Focus in three designs (CardSizing.Focus): Small keeps the countdown and the start row, and its length button
        // steps through the presets; Medium adds the status, presets and a progress line; Large draws the progress as a
        // ring around the countdown, with the status and today's sessions inside it.
        private RingGauge focusRing;
        private void FitFocus(Rect bounds)
        {
            var design = CardSizing.Focus(bounds.Width, bounds.Height);
            bool compact = bounds.Height < 320, folded = design == CardSizing.Design.Small, large = design == CardSizing.Design.Large;
            if (focusRing == null)
            {
                focusRing = new RingGauge { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                focusRing.SetResourceReference(RingGauge.TrackProperty, "Track"); focusRing.SetResourceReference(RingGauge.FillProperty, "Accent");
                Control<Grid>("FocusDial").Children.Insert(0, focusRing);
            }
            double ring = large ? CardSizing.FocusRing(bounds.Width, bounds.Height) : 0;
            focusRing.Width = focusRing.Height = Math.Max(1, ring); focusRing.Thickness = Math.Max(8, Math.Min(14, Math.Round(ring * 0.045)));
            Show(focusRing, large);
            Control<TextBlock>("FocusTime").FontSize = large ? Math.Round(ring * 0.22) : CardSizing.FocusTime(bounds.Width, bounds.Height);
            Control<StackPanel>("FocusMiddle").MaxWidth = large ? ring * 0.74 : Double.PositiveInfinity;
            Control<TextBlock>("FocusStatus").Margin = compact ? new Thickness(0, 2, 0, 8) : large ? new Thickness(0, 4, 0, 0) : new Thickness(0, 6, 0, 12);
            Control<Grid>("FocusPresets").Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 12);
            Show("FocusPresets", !folded); Show("FocusLength", folded);
            Show("FocusStatus", !folded); Show("FocusSessions", !folded);
            Show("FocusProgress", !large);
            Control<TextBlock>("FocusSessions").Margin = new Thickness(0, large ? 4 : 10, 0, 0);
            Control<ProgressBar>("FocusProgress").Margin = folded ? new Thickness(0, 4, 0, 12) : new Thickness(0);
            UpdateFocus();
        }
        // The next length for the folded presets: 25, 50, 5, then the chosen fourth length when there is one.
        private void NextFocusLength()
        {
            if (focus.Running) return;
            var lengths = new[] { 25, 50, 5 }.ToList();
            if (settings.FocusCustomMinutes > 0 && !lengths.Contains(settings.FocusCustomMinutes)) lengths.Add(settings.FocusCustomMinutes);
            int index = lengths.IndexOf((int)focus.Duration.TotalMinutes);
            SetFocus(lengths[(index + 1) % lengths.Count]);
        }

        // Now Playing in three designs (CardSizing.Media): Small is one row of cover and song over the buttons; Medium
        // puts the cover beside the song over the seek row; Large centres a big cover over the song. A narrow card draws
        // the buttons closer.
        private void FitMedia(Rect bounds)
        {
            bool folded = CardSizing.MediaShort(bounds.Height), narrow = CardSizing.MediaNarrow(bounds.Width);
            bool large = CardSizing.Media(bounds.Width, bounds.Height) == CardSizing.Design.Large;
            // Larger text takes more of the room under the cover.
            double art = large ? Math.Max(120, CardSizing.MediaCover(bounds.Width, bounds.Height) - Math.Round((settings.TextScale - 1) * 80)) : CardSizing.MediaArt(bounds.Width, bounds.Height);
            var box = Control<Border>("MediaArtBox"); var main = Control<Grid>("MediaMain"); var text = Control<StackPanel>("MediaText");
            if (main.RowDefinitions.Count == 0) { main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            Grid.SetColumnSpan(box, large ? 2 : 1); Grid.SetRow(text, large ? 1 : 0); Grid.SetColumn(text, large ? 0 : 1); Grid.SetColumnSpan(text, large ? 2 : 1);
            box.Width = large ? art : Double.NaN; box.Height = large ? art : Double.NaN; box.Margin = large ? new Thickness(0) : new Thickness(0, 0, 12, 0);
            box.HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Stretch; box.VerticalAlignment = large ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            text.Margin = large ? new Thickness(0, 14, 0, 0) : new Thickness(8, 0, 0, 0);
            foreach (string name in new[] { "TrackTitle", "TrackArtist" }) Control<TextBlock>(name).TextAlignment = large ? TextAlignment.Center : TextAlignment.Left;
            if (large) Control<TextBlock>("TrackTitle").FontSize = Math.Round(28 * settings.TextScale); else Control<TextBlock>("TrackTitle").SetResourceReference(TextBlock.FontSizeProperty, "TitleSize");
            Control<ColumnDefinition>("MediaArtColumn").Width = large ? new GridLength(0) : new GridLength(art + 12); box.MaxHeight = art;
            box.CornerRadius = new CornerRadius(art < 80 ? 10 : large ? 20 : 14);
            ((TextBlock)((Grid)box.Child).Children[0]).FontSize = Math.Round(Math.Min(large ? 96 : 46, art * 0.44));
            Control<RowDefinition>("MediaSeekRow").Height = new GridLength(folded ? 0 : 48);
            Control<RowDefinition>("MediaHintRow").Height = new GridLength(folded ? 0 : 26);
            Control<RowDefinition>("MediaMainRow").MinHeight = folded ? 48 : 86;
            Show((FrameworkElement)Control<Slider>("MediaProgress").Parent, !folded); Show("MediaHint", !folded);
            Control<Grid>("MediaMain").Margin = folded ? new Thickness(0, 8, 0, 4) : new Thickness(0, 12, 0, 4);
            Control<TextBlock>("TrackArtist").Margin = new Thickness(0, folded ? 2 : 8, 0, 0);
            Control<Button>("PreviousButton").Margin = new Thickness(0, 0, narrow ? 4 : 14, 0);
            Control<Button>("NextButton").Margin = new Thickness(narrow ? 4 : 14, 0, 0, 0);
            Control<Button>("PlayButton").Width = narrow ? 56 : 76;
        }

        // Full, Compact or Master (CardSizing.Mixer). Rows that fold away also hide their content, so nothing spills.
        private void FitMixer(Rect bounds)
        {
            var mode = CardSizing.Mixer(bounds.Height);
            if (mode != CardSizing.MixerMode.Master) mixerAppsOpen = false;
            bool full = mode == CardSizing.MixerMode.Full, master = mode == CardSizing.MixerMode.Master, apps = master && mixerAppsOpen;
            bool settingsShown = CardSizing.MixerSettingsShown(bounds.Width, mode);
            Control<RowDefinition>("MixerHeaderRow").Height = new GridLength(master ? 48 : 34);
            Control<RowDefinition>("MixerMasterRow").Height = new GridLength(apps ? 0 : 66);
            Control<RowDefinition>("MixerSliderRow").Height = new GridLength(apps ? 0 : 52);
            Show((FrameworkElement)Control<Button>("OutputDeviceButton").Parent, !apps);
            Show((FrameworkElement)Control<Button>("MuteButton").Parent, !apps);
            Control<RowDefinition>("MixerAppsHeaderRow").Height = new GridLength(full ? 42 : 0); Show("MixerAppsHeader", full);
            bool list = !master || apps;
            Control<RowDefinition>("MixerAppsRow").Height = list ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Show((FrameworkElement)Control<TextBlock>("NoSessions").Parent, list);
            Control<RowDefinition>("MixerMicrophoneRow").Height = new GridLength(full ? 56 : 0); Show("MicrophoneButton", full);
            Control<ColumnDefinition>("MicrophoneCompactColumn").Width = new GridLength(full ? 0 : 52); Show("MicrophoneCompact", !full);
            Control<ColumnDefinition>("SoundSettingsColumn").Width = new GridLength(settingsShown ? 60 : 0); Show("SoundSettingsButton", settingsShown);
            // A wide Large card lists the apps in two columns.
            Control<UniformGrid>("SessionPanel").Columns = CardSizing.MixerColumns(bounds.Width, bounds.Height); SpaceSessionColumns();
            Show("MixerApps", master); UpdateMixerApps();
            UpdateLevelTimer();
        }
        private void UpdateMixerApps()
        {
            var button = Control<Button>("MixerApps");
            string count = Control<TextBlock>("SessionCount").Text;
            button.Content = mixerAppsOpen ? "Done" : String.IsNullOrEmpty(count) ? "Apps" : "Apps · " + count;
            AutomationProperties.SetName(button, mixerAppsOpen ? "Back to master volume" : "Show app volumes");
        }
        // Two app columns keep a 24-unit gap between them.
        private void SpaceSessionColumns()
        {
            var panel = Control<UniformGrid>("SessionPanel");
            for (int i = 0; i < panel.Children.Count; i++) ((FrameworkElement)panel.Children[i]).Margin = panel.Columns < 2 ? new Thickness(0) : i % 2 == 0 ? new Thickness(0, 0, 12, 0) : new Thickness(12, 0, 0, 0);
        }
        private void ToggleMixerApps() { mixerAppsOpen = !mixerAppsOpen; outputPicker.Close(); FitMixer(Editor.Bounds("Mixer")); }

        // Spotify's Small, Medium and Large designs live in SpotifyWidget.Fit.
        private void FitSpotify(Rect bounds)
        {
            spotify.Fit(bounds, settings.TextScale);
            FitEmpty("SpotifyEmptyIcon", "SpotifyEmptyTitle", "SpotifyEmptyText", bounds.Height - 98);
        }
        // "Connect …" empty states keep their button and give up the icon, sentence and heading as room runs out.
        private void FitEmpty(string icon, string title, string text, double room)
        {
            int detail = CardSizing.EmptyDetail(room);
            if (icon != null) Show(icon, detail >= 3);
            Show(text, detail >= 2); Show(title, detail >= 1);
        }
    }
}
