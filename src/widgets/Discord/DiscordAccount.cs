using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    // Settings → Discord: the user's own Discord app details and the one-time approval in Discord.
    public sealed class DiscordAccount : IDisposable
    {
        private readonly Window window;
        private readonly DiscordService service;
        private readonly Action changed;
        private CancellationTokenSource signingIn;
        private bool disposed;
        public DiscordAccount(Window window, DiscordService service, Action changed)
        {
            this.window = window; this.service = service; this.changed = changed;
            C<Button>("DiscordSaveApp").Click += (s, e) => { try { SaveApp(); Status("App details saved on this PC."); } catch (Exception error) { Status(Message(error)); } Update(); };
            C<Button>("DiscordConnectButton").Click += (s, e) => Connect();
            C<Button>("DiscordDisconnect").Click += (s, e) => {
                try { service.Disconnect(); changed(); Status("Disconnected. Your app details are still saved."); }
                catch (Exception) { Status("Could not clear the saved connection. Try again."); }
                Update();
            };
            C<Button>("DiscordCancelConnect").Click += (s, e) => Cancel();
            C<Button>("DiscordDeveloper").Click += (s, e) => { try { Launcher.Open("https://discord.com/developers/applications"); } catch (Exception) { Status("Could not open your browser."); } };
            Prepare();
        }
        private T C<T>(string name) where T : class { return window.FindName(name) as T; }
        private void Status(string message) { C<TextBlock>("DiscordConnectionStatus").Text = message; }
        public void Prepare()
        {
            C<TextBox>("DiscordClientId").Text = service.ClientId;
            C<PasswordBox>("DiscordClientSecret").Password = "";
            if (signingIn == null) Status(service.StartupWarning ?? (service.Connected ? "Connected" + (String.IsNullOrEmpty(service.UserName) ? "" : " as " + service.UserName) + ". Your sign-in is saved on this PC." : DiscordService.ValidClientId(service.ClientId) ? "Not connected. Tap Connect Discord, then approve it in Discord." : "Not connected. Save your Discord app details, then connect."));
            Update();
        }
        private void Update()
        {
            bool busy = signingIn != null;
            C<Button>("DiscordSaveApp").IsEnabled = C<Button>("DiscordConnectButton").IsEnabled = C<TextBox>("DiscordClientId").IsEnabled = C<PasswordBox>("DiscordClientSecret").IsEnabled = !busy;
            C<Button>("DiscordConnectButton").Content = service.Connected ? "Reconnect Discord" : "Connect Discord";
            C<Button>("DiscordDisconnect").IsEnabled = service.Connected && !busy;
            C<Button>("DiscordCancelConnect").Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            C<TextBlock>("DiscordSecretHint").Text = service.HasSecret ? "Client Secret (saved; leave blank to keep it)" : "Client Secret";
        }
        // Saves typed app details. Returns false when nothing was typed or nothing changed.
        public bool SaveApp()
        {
            string id = C<TextBox>("DiscordClientId").Text.Trim(), secret = C<PasswordBox>("DiscordClientSecret").Password.Trim();
            if (id.Length == 0 && secret.Length == 0) return false;
            if (id == service.ClientId && secret.Length == 0) return false;
            try { service.SaveApp(id, secret); }
            catch (InvalidOperationException) { throw; }
            catch (Exception) { throw new InvalidOperationException("The Discord app details could not be saved. Check that your settings folder is writable and retry."); }
            C<PasswordBox>("DiscordClientSecret").Password = "";
            changed();
            return true;
        }
        // Used by Save changes: typed app details are saved too, or the error is returned.
        public string SaveDraft()
        {
            try { SaveApp(); Update(); return null; } catch (Exception error) { return "Discord: " + Message(error); }
        }
        private async void Connect()
        {
            if (signingIn != null || disposed) return;
            CancellationTokenSource attempt = null;
            try
            {
                SaveApp();
                attempt = new CancellationTokenSource(); signingIn = attempt; Update();
                Status("Opening Discord…");
                await service.ConnectAsync(attempt.Token, message => window.Dispatcher.BeginInvoke(new Action(() => { if (!disposed && signingIn == attempt) Status(message); })));
                if (!disposed) { changed(); Status("Connected" + (String.IsNullOrEmpty(service.UserName) ? "" : " as " + service.UserName) + " and saved. Turn on the Soundboard widget in Edit layout."); window.Activate(); }
            }
            catch (OperationCanceledException) { if (!disposed) Status("Sign-in cancelled. Your app details are saved; connect again when ready."); }
            catch (Exception error) { if (!disposed) Status(Message(error)); }
            finally { if (attempt != null) attempt.Dispose(); signingIn = null; if (!disposed) Update(); }
        }
        public void Cancel() { if (signingIn != null) signingIn.Cancel(); }
        public static string Message(Exception error)
        {
            if (error is DiscordNotRunningException) return "Open the Discord app on this PC and sign in, then connect again.";
            return error is InvalidOperationException ? error.Message : error is OperationCanceledException ? "Discord request cancelled or timed out. Try again." : "Could not reach Discord. Try again.";
        }
        public void Dispose() { disposed = true; Cancel(); }
    }
}
