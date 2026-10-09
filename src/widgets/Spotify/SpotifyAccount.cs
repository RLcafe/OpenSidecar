using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace WidgetScreen
{
    public sealed class SpotifyAccount : IDisposable
    {
        private readonly Window window;
        private readonly SpotifyService service;
        private readonly Action<string> saveId;
        private readonly Action changed;
        private CancellationTokenSource signingIn;
        private bool disposed;
        public SpotifyAccount(Window window, SpotifyService service, string clientId, Action<string> saveId, Action changed)
        {
            this.window = window; this.service = service; this.saveId = saveId; this.changed = changed;
            C<TextBox>("SpotifyClientId").Text = String.IsNullOrEmpty(clientId) ? service.ClientId : clientId;
            C<Button>("SpotifySaveId").Click += (s, e) => { try { SaveId(); Status("Client ID saved on this PC."); } catch (Exception error) { Status(Message(error)); } };
            C<Button>("SpotifyConnectButton").Click += (s, e) => Connect();
            C<Button>("SpotifyDisconnect").Click += (s, e) => {
                try { service.Disconnect(); changed(); Status("Disconnected. Your Client ID is still saved."); Update(); }
                catch (Exception) { Status("Could not clear the saved connection. Try again."); }
            };
            C<Button>("SpotifyCancelConnect").Click += (s, e) => Cancel();
            C<Button>("SpotifyDeveloper").Click += (s, e) => { try { Launcher.Open("https://developer.spotify.com/dashboard"); } catch (Exception) { Status("Could not open your browser."); } };
            Prepare();
        }
        private T C<T>(string name) where T : class { return window.FindName(name) as T; }
        private void Status(string message) { C<TextBlock>("SpotifyConnectionStatus").Text = message; }
        private string SaveId()
        {
            string value = C<TextBox>("SpotifyClientId").Text.Trim();
            if (!SpotifyService.ValidClientId(value)) throw new InvalidOperationException("Paste the 32-character Client ID from your Spotify Developer app.");
            try { saveId(value); } catch (Exception) { throw new InvalidOperationException("The Client ID could not be saved. Check that your settings folder is writable and retry."); }
            return value;
        }
        public void Prepare()
        {
            if (signingIn == null) Status(service.StartupWarning ?? (service.Connected ? service.CanControl ? "Connected. Your login is saved on this PC." : "Connected. Reconnect once to enable playback and seeking." : "Not connected. Save your Client ID, then connect."));
            Update();
        }
        private void Update()
        {
            bool busy = signingIn != null;
            C<Button>("SpotifySaveId").IsEnabled = C<Button>("SpotifyConnectButton").IsEnabled = C<TextBox>("SpotifyClientId").IsEnabled = !busy;
            C<Button>("SpotifyConnectButton").Content = service.Connected ? "Reconnect Spotify" : "Connect Spotify";
            C<Button>("SpotifyDisconnect").IsEnabled = service.Connected && !busy;
            C<Button>("SpotifyCancelConnect").Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }
        private async void Connect()
        {
            if (signingIn != null || disposed) return;
            CancellationTokenSource attempt = null;
            try
            {
                string id = SaveId();
                attempt = new CancellationTokenSource(); signingIn = attempt; Update();
                Status("Finish signing in in your browser, then return here.");
                await service.ConnectAsync(id, attempt.Token, message => window.Dispatcher.BeginInvoke(new Action(() => { if (!disposed && signingIn == attempt) Status(message); })));
                if (!disposed) { changed(); Status("Connected and saved. Your account will restore when you reopen the app."); window.Activate(); }
            }
            catch (OperationCanceledException) { if (!disposed) Status("Sign-in cancelled or timed out. Your Client ID is saved; try connecting again."); }
            catch (System.Net.Sockets.SocketException) { if (!disposed) Status("The local sign-in port is unavailable. Close other Widget Screen instances, then reconnect."); }
            catch (Exception error) { if (!disposed) Status(Message(error)); }
            finally { if (attempt != null) attempt.Dispose(); signingIn = null; if (!disposed) Update(); }
        }
        public void Cancel() { if (signingIn != null) signingIn.Cancel(); }
        public static string Message(Exception error) { return error is InvalidOperationException ? error.Message : error is OperationCanceledException ? "Spotify request cancelled or timed out. Try again." : "Could not reach Spotify. Check your connection and try again."; }
        public void Dispose() { disposed = true; Cancel(); }
    }
}
