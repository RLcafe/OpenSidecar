using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

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
            FitStrips();
            FitFocus(Editor.Bounds("Focus"));
            FitMedia(Editor.Bounds("Media"));
            FitMixer(Editor.Bounds("Mixer"));
            // System stats and Weather switch between a Small, Medium and Large design (docs/design/widget-size-views.md).
            systemCard.Fit(Editor.Bounds("System"), settings.TextScale);
            weatherCard.TextScale = stripScale; weatherCard.Fit(Editor.Bounds("Weather"));
            storageCard.Fit(Editor.Bounds("Storage"));
            clocksCard.Fit(Editor.Bounds("Clocks"));
            prayerCard.Fit(Editor.Bounds("Prayer")); RenderPrayer();
            notesCard.Fit(Editor.Bounds("Notes"));
            claudeCard.Fit(Editor.Bounds("Claude"), settings.TextScale);
            clipCard.Fit(Editor.Bounds("Clipboard"));
            FitMacros();
            calendarCard.Fit(Editor.Bounds("Calendar"));
            FitGames();
            homeCard.Fit(Editor.Bounds("Home"), settings.TextScale); ShowHome();
            FitSpotify(Editor.Bounds("Spotify"));
            var board = Editor.Bounds("Soundboard");
            // The voice sticker (Sticker Sheet phase 5) needs 360 units of card, as the channel name did; under that it steps aside.
            var voice = (FrameworkElement)StatusSticker.For(Control<TextBlock>("SoundboardChannel")) ?? Control<TextBlock>("SoundboardChannel");
            voice.MaxWidth = CardSizing.SoundboardChannelShown(board.Width) ? Double.PositiveInfinity : 0;
            Show("SoundboardHeading", CardSizing.HeadingShown(board.Width, Control<FrameworkElement>("SoundboardPages").Visibility == Visibility.Visible));
            FitEmpty(null, "SoundboardEmptyTitle", "SoundboardEmptyText", board.Height - 90);
        }
        private void Show(string name, bool shown) { Control<FrameworkElement>(name).Visibility = shown ? Visibility.Visible : Visibility.Collapsed; }
        private static void Show(FrameworkElement element, bool shown) { element.Visibility = shown ? Visibility.Visible : Visibility.Collapsed; }

        // Focus in three designs (CardSizing.Focus): Small keeps the countdown and the start row, and its length button
        // steps through the presets; Medium adds the status, presets and a progress line; Large draws the progress as a
        // ring around the countdown, with the status and today's sessions inside it.
        // Sticker Sheet phase 2 (docs/design/sticker-sheet.md): Large draws a kitchen-timer dial with the time left as a
        // wedge in the ink; Medium and Large show today's sessions as tomato pips, with the Today and Next break chips
        // when there's room. The pips take the place of the sessions line.
        private TimerDial focusDial;
        private PipRow focusPips;
        private TextBlock focusTodayChip, focusBreakChip;
        // Pips (24) and, with the chips, a 10-unit gap and a 52-unit chip row; the 12-unit gap under them.
        private const double FocusPipsHeight = 24 + 12, FocusChipsHeight = 10 + 52;
        private void FitFocus(Rect bounds)
        {
            var design = CardSizing.Focus(bounds.Width, bounds.Height);
            bool compact = bounds.Height < 320, folded = design == CardSizing.Design.Small, large = design == CardSizing.Design.Large;
            if (focusDial == null)
            {
                focusDial = new TimerDial { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Control<Grid>("FocusDial").Children.Insert(0, focusDial);
                focusPips = new PipRow { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                Control<Border>("FocusPipHost").Child = focusPips;
                var chips = Control<Grid>("FocusChips");
                var today = Stickers.FactChip("Today", "—", out focusTodayChip); chips.Children.Add(today);
                var next = Stickers.FactChip("Next break", "—", out focusBreakChip); Grid.SetColumn(next, 2); chips.Children.Add(next);
            }
            // The dial gives up room to the pips first, then to the chips (which fold away under a 160-unit dial).
            double ring = large ? CardSizing.FocusRing(bounds.Width, bounds.Height) : 0;
            bool chipsShown = !folded && (large ? Math.Min(ring, bounds.Height - 186 - FocusPipsHeight - FocusChipsHeight) >= 160 : bounds.Height >= 400 + 48 * (settings.TextScale - 1));
            if (large) ring = Math.Max(120, Math.Min(ring, bounds.Height - 186 - FocusPipsHeight - (chipsShown ? FocusChipsHeight : 0)));
            focusDial.Width = Math.Max(1, ring); focusDial.Height = Math.Max(1, ring + Stickers.Lift);
            Show(focusDial, large);
            Show("FocusFacts", !folded); Show("FocusChips", chipsShown);
            Control<TextBlock>("FocusTime").FontSize = large ? Math.Round(ring * 0.17) : CardSizing.FocusTime(bounds.Width, bounds.Height);
            Control<StackPanel>("FocusMiddle").MaxWidth = large ? ring * TimerDial.Hole * 0.92 : Double.PositiveInfinity;
            Control<StackPanel>("FocusMiddle").Margin = new Thickness(0, 0, 0, large ? Stickers.Lift : 0);
            Control<TextBlock>("FocusStatus").Margin = compact ? new Thickness(0, 2, 0, 8) : large ? new Thickness(0, 4, 0, 0) : new Thickness(0, 6, 0, 12);
            Control<Grid>("FocusPresets").Margin = compact ? new Thickness(0, 0, 0, 8) : new Thickness(0, 0, 0, 12);
            Show("FocusPresets", !folded); Show("FocusLength", folded);
            Show("FocusStatus", !folded); Show("FocusSessions", false);
            Show("FocusProgress", !large);
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
            // The album line under the song takes 24 units from a Large cover.
            double art = large ? Math.Max(120, CardSizing.MediaCover(bounds.Width, bounds.Height) - Math.Round((settings.TextScale - 1) * 80) - 24) : CardSizing.MediaArt(bounds.Width, bounds.Height);
            var box = Control<Border>("MediaArtBox"); var main = Control<Grid>("MediaMain"); var text = Control<StackPanel>("MediaText");
            if (main.RowDefinitions.Count == 0) { main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }
            Grid.SetColumnSpan(box, large ? 2 : 1); Grid.SetRow(text, large ? 1 : 0); Grid.SetColumn(text, large ? 0 : 1); Grid.SetColumnSpan(text, large ? 2 : 1);
            box.Width = large ? art : Double.NaN; box.Height = large ? art : Double.NaN; box.Margin = large ? new Thickness(0) : new Thickness(0, 0, 12, 0);
            box.HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Stretch; box.VerticalAlignment = large ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
            text.Margin = large ? new Thickness(0, 14, 0, 0) : new Thickness(8, 0, 0, 0);
            foreach (string name in new[] { "TrackTitle", "TrackArtist", "TrackAlbum" }) Control<TextBlock>(name).TextAlignment = large ? TextAlignment.Center : TextAlignment.Left;
            if (large) Control<TextBlock>("TrackTitle").FontSize = Math.Round(28 * settings.TextScale); else Control<TextBlock>("TrackTitle").SetResourceReference(TextBlock.FontSizeProperty, "TitleSize");
            // The record peeks out of the sleeve by 30% of the cover while something plays (not on the folded card).
            mediaArt = art; mediaLarge = large; mediaFolded = folded;
            double peek = MediaPeek;
            Control<ColumnDefinition>("MediaArtColumn").Width = large ? new GridLength(0) : new GridLength(art + peek + 12); box.MaxHeight = art;
            box.Width = art; box.HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            box.Margin = large ? new Thickness(0, 0, peek, 0) : new Thickness(0, 0, 12 + peek, 0);
            box.CornerRadius = new CornerRadius(art < 80 ? 10 : large ? 20 : 14);
            ((Grid)box.Child).Children.OfType<TextBlock>().First().FontSize = Math.Round(Math.Min(large ? 96 : 46, art * 0.44));
            ((Grid)box.Child).Children.OfType<System.Windows.Shapes.Rectangle>().First().RadiusX = box.CornerRadius.TopLeft - 2;
            FitMediaDisc();
            // The Length and Left chips need the text column to be roomy (Medium wide and tall enough, or Large).
            double textRoom = large ? bounds.Width - 48 : bounds.Width - 48 - art - peek - 20;
            mediaChipsRoom = !folded && textRoom >= 220 && (large ? bounds.Height - 48 - art - 28 - 48 - 54 - 26 >= 150 * settings.TextScale : bounds.Height >= 340 + 60 * (settings.TextScale - 1));
            Control<RowDefinition>("MediaSeekRow").Height = new GridLength(folded ? 0 : 48);
            Control<RowDefinition>("MediaHintRow").Height = new GridLength(folded ? 0 : 26);
            Control<RowDefinition>("MediaMainRow").MinHeight = folded ? 48 : 86;
            Show((FrameworkElement)Control<Slider>("MediaProgress").Parent, !folded); Show("MediaHint", !folded);
            Control<Grid>("MediaMain").Margin = folded ? new Thickness(0, 8, 0, 4) : new Thickness(0, 12, 0, 4);
            Control<TextBlock>("TrackArtist").Margin = new Thickness(0, folded ? 2 : 8, 0, 0);
            Control<Button>("PreviousButton").Margin = new Thickness(0, 0, narrow ? 4 : 14, 0);
            Control<Button>("NextButton").Margin = new Thickness(narrow ? 4 : 14, 0, 0, 0);
            Control<Button>("PlayButton").Width = narrow ? 56 : 76;
            ShowMediaParts();
        }

        // Now Playing's sleeve, disc, album line, chips and cover tint (docs/design/sticker-sheet.md, phase 2).
        private double mediaArt = 104; private bool mediaLarge, mediaFolded, mediaChipsRoom, mediaPlaying, mediaHasArt;
        private DiscPeek mediaDisc;
        private TextBlock mediaLengthChip, mediaLeftChip;
        private Color? mediaTint;
        private double MediaPeek { get { return mediaPlaying && !mediaFolded ? Math.Round(mediaArt * 0.3) : 0; } }
        private void FitMediaDisc()
        {
            if (mediaDisc == null)
            {
                mediaDisc = new DiscPeek(); Control<Grid>("MediaDiscHost").Children.Add(mediaDisc);
                var chips = Control<Grid>("MediaChips");
                var length = Stickers.FactChip("Length", "—", out mediaLengthChip); chips.Children.Add(length);
                var left = Stickers.FactChip("Left", "—", out mediaLeftChip); Grid.SetColumn(left, 2); chips.Children.Add(left);
            }
            var host = Control<Grid>("MediaDiscHost"); var box = Control<Border>("MediaArtBox");
            Grid.SetColumnSpan(host, Grid.GetColumnSpan(box)); Grid.SetRow(host, Grid.GetRow(box));
            double disc = Math.Round(mediaArt * 0.92), peek = MediaPeek;
            mediaDisc.Width = mediaDisc.Height = disc;
            // Its right edge meets the sleeve's group edge, centred with the sleeve on Large (see docs/design/sticker-sheet.md).
            mediaDisc.HorizontalAlignment = mediaLarge ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            mediaDisc.VerticalAlignment = mediaLarge ? VerticalAlignment.Bottom : VerticalAlignment.Center;
            mediaDisc.Margin = new Thickness(Math.Max(0, mediaArt + peek - disc), 0, 0, mediaLarge ? (mediaArt - disc) / 2 : 0);
            Show(mediaDisc, peek > 0);
        }
        private void ShowMediaParts()
        {
            var box = Control<Border>("MediaArtBox");
            box.BorderThickness = new Thickness(mediaHasArt ? 2 : 0);
            Show(((Grid)box.Child).Children.OfType<System.Windows.Shapes.Rectangle>().First(), !mediaPlaying);
            Show("TrackAlbum", !mediaFolded && Control<TextBlock>("TrackAlbum").Text.Length > 0);
            Show("MediaChips", mediaChipsRoom && mediaPlaying);
            if (mediaDisc != null) FitMediaDisc();
            Control<ColumnDefinition>("MediaArtColumn").Width = mediaLarge ? new GridLength(0) : new GridLength(mediaArt + MediaPeek + 12);
            box.Margin = mediaLarge ? new Thickness(0, 0, MediaPeek, 0) : new Thickness(0, 0, 12 + MediaPeek, 0);
            ApplyMediaTint();
        }
        // The card's background takes 22% of the cover's colour at the top, fading to the Surface.
        private void ApplyMediaTint()
        {
            var card = Control<Border>("MediaCard"); var surface = window.Resources["Surface"] as SolidColorBrush;
            if (!mediaTint.HasValue || surface == null || !mediaPlaying) { card.SetResourceReference(Border.BackgroundProperty, "Surface"); return; }
            var top = MediaService.Blend(surface.Color, mediaTint.Value, MediaService.TintPart);
            var brush = new LinearGradientBrush(top, surface.Color, 90); brush.Freeze();
            card.Background = brush;
        }

        // Full, Compact or Master (CardSizing.Mixer). Rows that fold away also hide their content, so nothing spills.
        private void FitMixer(Rect bounds)
        {
            var mode = CardSizing.Mixer(bounds.Height);
            // A Master card opens the list over the master volume; a Large one switches between channel strips and the list.
            if (mode == CardSizing.MixerMode.Compact) mixerAppsOpen = false;
            bool full = mode == CardSizing.MixerMode.Full, master = mode == CardSizing.MixerMode.Master, apps = master && mixerAppsOpen, strips = full && !mixerAppsOpen;
            bool settingsShown = CardSizing.MixerSettingsShown(bounds.Width, mode);
            // The Apps button (Master and Large cards) needs a 48-unit heading.
            Control<RowDefinition>("MixerHeaderRow").Height = new GridLength(master || full ? 48 : 34);
            Control<RowDefinition>("MixerMasterRow").Height = new GridLength(apps ? 0 : 66);
            Control<RowDefinition>("MixerSliderRow").Height = new GridLength(apps ? 0 : 52);
            Show((FrameworkElement)Control<Button>("OutputDeviceButton").Parent, !apps && !strips);
            Show((FrameworkElement)Control<Button>("MuteButton").Parent, !apps && !strips);
            Control<RowDefinition>("MixerAppsHeaderRow").Height = new GridLength(full ? 42 : 0); Show("MixerAppsHeader", full && !strips);
            bool list = !master || apps;
            Control<RowDefinition>("MixerAppsRow").Height = list ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            Show((FrameworkElement)Control<TextBlock>("NoSessions").Parent, list && !strips);
            Control<RowDefinition>("MixerMicrophoneRow").Height = new GridLength(full ? 56 : 0); Show("MicrophoneButton", full);
            Control<ColumnDefinition>("MicrophoneCompactColumn").Width = new GridLength(full ? 0 : 52); Show("MicrophoneCompact", !full);
            Control<ColumnDefinition>("SoundSettingsColumn").Width = new GridLength(settingsShown ? 60 : 0); Show("SoundSettingsButton", settingsShown);
            // A wide Large card lists the apps in two columns.
            Control<UniformGrid>("SessionPanel").Columns = CardSizing.MixerColumns(bounds.Width, bounds.Height); SpaceSessionColumns();
            FitStrips(bounds, strips);
            Show("MixerApps", master || full); UpdateMixerApps();
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
