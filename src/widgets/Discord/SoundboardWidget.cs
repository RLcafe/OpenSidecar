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
    // A paged grid of Discord soundboard sounds. Tapping one plays it in the voice channel you're in.
    // Voice channel changes arrive as Discord events; the widget only retries the connection while visible.
    public sealed class SoundboardWidget : IDisposable
    {
        private const double ButtonWidth = 148, ButtonHeight = 56, Gap = 8;
        private readonly Window window;
        private readonly DiscordService service;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private List<DiscordSound> sounds = new List<DiscordSound>();
        private DiscordVoiceChannel channel;
        private string soundsGuild;
        private bool active, loading, playing, loaded, disposed, refreshQueued;
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
            generation++; loaded = false; channel = null; sounds = new List<DiscordSound>(); soundsGuild = null; page = 0; nextAttempt = DateTime.MinValue;
            bool connected = service.Connected;
            // Until Discord is connected, one empty state replaces the sounds, pager and status line.
            C<FrameworkElement>("SoundboardEmpty").Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            foreach (string name in new[] { "SoundboardGrid", "SoundboardRefresh", "SoundboardChannel" }) C<FrameworkElement>(name).Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            C<TextBlock>("SoundboardChannel").Text = "";
            Status(connected ? (service.StartupWarning ?? "") : "");
            Build();
            if (connected && active) Load(true);
        }

        public void Tick(bool enabled, bool quiet)
        {
            ticks++;
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
                ShowChannel();
                Status(sounds.Count == 0 ? "No soundboard sounds yet. Add some to a server in Discord." : channel == null ? "Join a voice channel in Discord to play sounds." : "");
                Build();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (disposed || version != generation) return;
                nextAttempt = DateTime.UtcNow.AddSeconds(service.Linked ? 30 : 10);
                C<TextBlock>("SoundboardChannel").Text = "";
                if (!service.Connected) { AccountChanged(); return; }
                channel = null; Status(Message(error)); Build();
            }
            finally { loading = false; if (!disposed && refreshQueued && active) Load(false); }
        }
        private void ShowChannel()
        {
            var label = C<TextBlock>("SoundboardChannel");
            label.Text = channel == null ? "" : channel.Name;
            label.ToolTip = channel == null ? null : "In voice: " + channel.Name;
        }
        public static string Message(Exception error)
        {
            if (error is DiscordNotRunningException) return "Open Discord on this PC to play sounds.";
            return error is InvalidOperationException ? error.Message : "Couldn't reach Discord. Retrying.";
        }

        // Page size follows the widget: as many 148 × 56 buttons as fit.
        public static int Capacity(double width, double height, out int columns, out int rows)
        {
            columns = Math.Max(1, (int)Math.Floor((Math.Max(0, width) + Gap) / (ButtonWidth + Gap)));
            rows = Math.Max(1, (int)Math.Floor((Math.Max(0, height) + Gap) / (ButtonHeight + Gap)));
            return columns * rows;
        }
        private void Build()
        {
            var grid = C<UniformGrid>("SoundboardGrid"); grid.Children.Clear();
            int columns, rows;
            int size = Capacity(grid.ActualWidth, grid.ActualHeight, out columns, out rows);
            grid.Columns = columns; grid.Rows = rows;
            int pages = Math.Max(1, (sounds.Count + size - 1) / size);
            page = Math.Max(0, Math.Min(pages - 1, page));
            C<FrameworkElement>("SoundboardPages").Visibility = service.Connected && pages > 1 ? Visibility.Visible : Visibility.Collapsed;
            C<Button>("SoundboardPrevious").IsEnabled = page > 0; C<Button>("SoundboardNext").IsEnabled = page < pages - 1;
            C<TextBlock>("SoundboardPageLabel").Text = (page + 1) + "/" + pages;
            bool ready = channel != null && service.Linked;
            foreach (var sound in sounds.Skip(page * size).Take(size))
            {
                bool otherServer = !sound.IsDefault && channel != null && sound.GuildId != channel.GuildId;
                var name = new TextBlock { Text = sound.Name, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                name.SetResourceReference(TextBlock.FontSizeProperty, "SmallSize");
                var button = new Button { Content = name, Margin = new Thickness(Gap / 2), Padding = new Thickness(8, 4, 8, 4), MinHeight = 48, IsEnabled = ready, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    ToolTip = sound.Name + (sound.IsDefault ? " · Discord sound" : otherServer ? " · From another server" : "") };
                AutomationProperties.SetName(button, "Play " + sound.Name);
                var chosen = sound; button.Click += (s, e) => Play(chosen);
                grid.Children.Add(button);
            }
        }
        private async void Play(DiscordSound sound)
        {
            if (playing || disposed) return;
            playing = true; int version = generation;
            try { await service.PlayAsync(sound, lifetime.Token); if (!disposed && version == generation) Status("Played " + sound.Name + ".", true); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed && version == generation) { Status(Message(error), true); if (!service.Linked) refreshQueued = true; } }
            finally { playing = false; }
        }
        public void Dispose() { disposed = true; generation++; lifetime.Cancel(); service.Dispose(); }
    }
}
