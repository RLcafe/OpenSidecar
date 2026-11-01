using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WidgetScreen
{
    // A paged grid of Discord soundboard sounds as sampler pads (Sticker Sheet phase 5, docs/design/sticker-sheet.md):
    // tapping one plays it in the voice channel you're in, and the pad lights in the card's ink for 150 ms (not in Quiet
    // mode). The status sticker says where you are: the voice channel (filled, with a speaker), NOT IN VOICE, … while
    // connecting, UNAVAILABLE when Discord can't be reached and NOT SET UP before Discord is connected. Discord not
    // running, or no sounds yet, puts the reason in a dashed outline in place of the pads. Voice channel changes arrive
    // as Discord events; the widget only retries the connection while visible. The phone's SoundboardWidget.java
    // draws the same.
    public sealed class SoundboardWidget : IDisposable
    {
        private const double Gap = 8;
        private readonly Window window;
        private readonly DiscordService service;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private List<DiscordSound> sounds = new List<DiscordSound>();
        private DiscordVoiceChannel channel;
        private string soundsGuild;
        private bool active, loading, playing, loaded, disposed, refreshQueued, failed, quiet, sample;
        private string failure = "";
        private int page, generation, statusClearsAt = int.MaxValue, ticks;
        private DateTime nextAttempt = DateTime.MinValue;
        public Action OpenSetup;

        public SoundboardWidget(Window window, DiscordService service)
        {
            this.window = window; this.service = service;
            C<Button>("SoundboardPrevious").Click += (s, e) => { page--; Build(); };
            C<Button>("SoundboardNext").Click += (s, e) => { page++; Build(); };
            C<Button>("SoundboardRefresh").Click += (s, e) => { loaded = false; nextAttempt = DateTime.MinValue; Load(true); };
            C<Button>("SoundboardSetup").Click += (s, e) => { if (OpenSetup != null) OpenSetup(); };
            C<FrameworkElement>("SoundboardGrid").SizeChanged += (s, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 1 || Math.Abs(e.NewSize.Height - e.PreviousSize.Height) >= 1) Build(); };
            service.Changed += () => window.Dispatcher.BeginInvoke(new Action(() => { if (!disposed) { refreshQueued = true; if (active) Load(false); } }));
            AccountChanged();
        }
        private T C<T>(string name) where T : class { return (T)(object)window.FindName(name); }
        private void Status(string value, bool transient = false)
        {
            var status = C<TextBlock>("SoundboardStatus"); status.Text = value ?? "";
            status.Visibility = String.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            statusClearsAt = transient ? ticks + 6 : int.MaxValue;
        }

        public void AccountChanged()
        {
            generation++; loaded = false; failed = false; failure = ""; channel = null; sounds = new List<DiscordSound>(); soundsGuild = null; page = 0; nextAttempt = DateTime.MinValue;
            bool connected = service.Connected || sample;
            // Until Discord is connected, one empty state replaces the sounds, pager and status line.
            C<FrameworkElement>("SoundboardEmpty").Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            foreach (string name in new[] { "SoundboardGrid", "SoundboardRefresh" }) C<FrameworkElement>(name).Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            C<TextBlock>("SoundboardChannel").Text = "";
            Status(connected ? (service.StartupWarning ?? "") : "");
            Build();
            ShowChannel();
            if (connected && active && !sample) Load(true);
        }

        // Render mode's --render-soundboard-sample (and --render-soundboard-empty): labelled sample sounds, or none,
        // as if Discord were connected and you were in a voice channel called "General". They never play.
        public void UseSample(bool empty)
        {
            sample = true;
            AccountChanged();
            channel = new DiscordVoiceChannel { Name = "General", GuildId = "sample" };
            var names = empty ? new string[0] : new[] { "Airhorn", "Applause", "Ba dum tss", "Crickets", "Drum roll", "Sad trombone", "Quack", "Victory" };
            sounds = names.Select((name, i) => new DiscordSound { Id = "sample" + i, Name = name, GuildId = i < 3 ? "0" : "sample" }).ToList();
            loaded = true;
            Status("Labelled sample sounds.");
            ShowChannel(); Build();
        }
        public bool Sample { get { return sample; } }

        // The status sticker's words (the phone's SoundboardText.voice says the same).
        public static string Voice(bool connected, bool linked, bool failed, string channel)
        {
            if (!connected) return "NOT SET UP";
            if (failed) return "UNAVAILABLE";
            if (!String.IsNullOrEmpty(channel)) return channel;
            return linked ? "NOT IN VOICE" : "…";
        }
        // Pads are numbered through the whole list, like a sampler's banks: "01", "02", …
        public static string PadNumber(int index) { return (index + 1).ToString("00"); }
        // How long a tapped pad stays lit.
        public static readonly TimeSpan Flash = TimeSpan.FromMilliseconds(150);

        public void Tick(bool enabled, bool quiet)
        {
            ticks++; this.quiet = quiet;
            if (sample) return;
            if (ticks >= statusClearsAt) Status("");
            bool wasActive = active; active = enabled;
            if (!enabled || !service.Connected || disposed) return;
            // The first load happens on the first visible tick, never while hidden; after a failure, retry slowly.
            if (!wasActive || refreshQueued || (!service.Linked && DateTime.UtcNow >= nextAttempt)) Load(!loaded);
            else if (!loaded && DateTime.UtcNow >= nextAttempt) Load(true);
        }

        private async void Load(bool reloadSounds)
        {
            if (loading || disposed || !service.Connected) return;
            loading = true; refreshQueued = false; int version = generation;
            if (!service.Linked) { C<TextBlock>("SoundboardChannel").Text = ""; if (!loaded) Status("Connecting to Discord…"); }
            try
            {
                var current = await service.SelectedChannelAsync(lifetime.Token);
                if (disposed || version != generation) return;
                channel = current;
                string guild = current == null ? null : current.GuildId;
                if (reloadSounds || !loaded || guild != soundsGuild)
                {
                    var list = await service.SoundsAsync(lifetime.Token);
                    if (disposed || version != generation) return;
                    if (guild != soundsGuild) page = 0;
                    sounds = DiscordService.Order(list, guild); soundsGuild = guild; loaded = true;
                }
                failed = false; failure = "";
                ShowChannel();
                Status(sounds.Count == 0 ? "" : channel == null ? "Join a voice channel in Discord to play sounds." : "");
                Build();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (disposed || version != generation) return;
                nextAttempt = DateTime.UtcNow.AddSeconds(service.Linked ? 30 : 10);
                C<TextBlock>("SoundboardChannel").Text = "";
                if (!service.Connected) { AccountChanged(); return; }
                channel = null; failed = true; failure = Message(error);
                ShowChannel(); Status(sounds.Count == 0 ? "" : failure); Build();
            }
            finally { loading = false; if (!disposed && refreshQueued && active) Load(false); }
        }
        private void ShowChannel()
        {
            var label = C<TextBlock>("SoundboardChannel");
            bool connected = service.Connected || sample, inVoice = connected && !failed && channel != null;
            label.Text = sample ? "SAMPLE" : Voice(connected, service.Linked, failed, channel == null ? null : channel.Name);
            label.ToolTip = inVoice ? "In voice: " + channel.Name : null;
            var sticker = StatusSticker.For(label);
            if (sticker != null) { sticker.Filled = inVoice && !sample; sticker.Glyph = inVoice && !sample ? "" : null; }
        }
        public static string Message(Exception error)
        {
            if (error is DiscordNotRunningException) return "Open Discord on this PC to play sounds.";
            return error is InvalidOperationException ? error.Message : "Couldn't reach Discord. Retrying.";
        }

        // Six sounds a page that grow with the widget (CardSizing.SoundGrid), fewer when there are fewer.
        public static int Capacity(double width, double height, int count, out int columns, out int rows)
        {
            int[] shape = CardSizing.SoundGrid(width, height, count);
            columns = shape[0]; rows = shape[1];
            return columns * rows;
        }
        private void Build()
        {
            var grid = C<UniformGrid>("SoundboardGrid"); grid.Children.Clear();
            int columns, rows;
            int size = Capacity(grid.ActualWidth, grid.ActualHeight, sounds.Count, out columns, out rows);
            grid.Columns = columns; grid.Rows = rows;
            int pages = Math.Max(1, (sounds.Count + size - 1) / size);
            page = Math.Max(0, Math.Min(pages - 1, page));
            C<FrameworkElement>("SoundboardPages").Visibility = service.Connected && pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            C<Button>("SoundboardPrevious").IsEnabled = page > 0; C<Button>("SoundboardNext").IsEnabled = page < pages - 1;
            C<TextBlock>("SoundboardPageLabel").Text = (page + 1) + "/" + pages;
            bool ready = channel != null && (service.Linked || sample);
            // Discord not running with nothing loaded, or no sounds at all: the reason in place of the pads.
            bool connected = service.Connected || sample;
            string problem = !connected ? null : failed && sounds.Count == 0 ? failure : loaded && sounds.Count == 0 ? "No soundboard sounds yet. Add some to a server in Discord." : null;
            C<FrameworkElement>("SoundboardProblem").Visibility = problem == null ? Visibility.Collapsed : Visibility.Visible;
            C<TextBlock>("SoundboardProblemText").Text = problem ?? "";
            // Pads: Small shows the name alone; Medium adds the pad number over it; Large is a pad with the number and a
            // play mark at the top and the name and where the sound comes from at the bottom (CardSizing.Tile). Secondary
            // text follows the pad's text colour at lower opacity, so it stays readable when the pad lights.
            var design = CardSizing.Tile(grid.ActualWidth / Math.Max(1, columns) - Gap, grid.ActualHeight / Math.Max(1, rows) - Gap);
            int index = page * size;
            foreach (var sound in sounds.Skip(page * size).Take(size))
            {
                bool otherServer = !sound.IsDefault && channel != null && sound.GuildId != channel.GuildId;
                string from = sound.IsDefault ? "Discord sound" : otherServer ? "From another server" : "This server";
                var name = new TextBlock { Text = sound.Name, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                name.SetResourceReference(TextBlock.FontSizeProperty, design == CardSizing.Design.Large ? "BodySize" : "SmallSize");
                object content = name;
                bool large = design == CardSizing.Design.Large;
                if (design != CardSizing.Design.Small)
                {
                    var number = new TextBlock { Text = PadNumber(index), FontSize = 13, FontWeight = FontWeights.Bold, Opacity = 0.7, HorizontalAlignment = large ? HorizontalAlignment.Left : HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, large ? 0 : 4) };
                    System.Windows.Documents.Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
                    if (large)
                    {
                        var pad = new Grid();
                        pad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); pad.RowDefinitions.Add(new RowDefinition());
                        pad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); pad.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        var top = new DockPanel();
                        var mark = new TextBlock { Text = "", FontFamily = Stickers.Glyphs, FontSize = 16, Opacity = 0.7 }; DockPanel.SetDock(mark, Dock.Right);
                        top.Children.Add(mark); top.Children.Add(number); pad.Children.Add(top);
                        name.HorizontalAlignment = HorizontalAlignment.Left; name.TextAlignment = TextAlignment.Left; name.FontWeight = FontWeights.SemiBold;
                        Grid.SetRow(name, 2); pad.Children.Add(name);
                        var source = new TextBlock { Text = from, FontSize = 13, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
                        Grid.SetRow(source, 3); pad.Children.Add(source);
                        content = pad;
                    }
                    else { var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; stack.Children.Add(number); stack.Children.Add(name); content = stack; }
                }
                var button = new Button { Content = content, Style = (Style)window.FindResource("Pad"), Margin = new Thickness(Gap / 2), Padding = large ? new Thickness(14, 10, 12, 10) : new Thickness(8, 4, 8, 4), MinHeight = 48, IsEnabled = ready,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = large ? VerticalAlignment.Stretch : VerticalAlignment.Center,
                    ToolTip = sound.Name + (sound.IsDefault ? " · Discord sound" : otherServer ? " · From another server" : "") };
                AutomationProperties.SetName(button, "Play " + sound.Name + ", pad " + PadNumber(index));
                var chosen = sound; var lit = button; button.Click += (s, e) => { Light(lit); Play(chosen); };
                grid.Children.Add(button);
                index++;
            }
        }
        // A tapped pad lights for 150 ms, once; in Quiet mode it only shows while pressed.
        private void Light(Button pad)
        {
            if (quiet) return;
            pad.Tag = true;
            var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Normal, window.Dispatcher) { Interval = Flash };
            timer.Tick += (s, e) => { timer.Stop(); pad.Tag = null; };
            timer.Start();
        }
        private async void Play(DiscordSound sound)
        {
            if (playing || disposed || sample) return;
            playing = true; int version = generation;
            try { await service.PlayAsync(sound, lifetime.Token); if (!disposed && version == generation) Status("Played " + sound.Name + ".", true); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed && version == generation) { Status(Message(error), true); if (!service.Linked) refreshQueued = true; } }
            finally { playing = false; }
        }
        public void Dispose() { disposed = true; generation++; lifetime.Cancel(); service.Dispose(); }
    }
}
