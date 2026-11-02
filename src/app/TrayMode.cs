using System;
using System.Reflection;
using System.Windows;

namespace WidgetScreen
{
    // Tray mode (decision 009): the dashboard window hides and leaves the taskbar, and an icon in the notification
    // area brings it back. The icon exists only while hidden. Used by the Hide to tray button, and automatically
    // while a paired phone is showing the dashboard.
    public sealed class TrayMode : IDisposable
    {
        private readonly Window window;
        private readonly Action exit;
        private System.Windows.Forms.NotifyIcon icon;
        private WindowState restoreState = WindowState.Normal;
        public bool Hidden { get; private set; }
        // True when the phone put the window in the tray, so the phone leaving may bring it back.
        public bool Automatic { get; private set; }
        public event Action Changed;
        // Tests turn the one-time tip off so nothing pops up on the desktop.
        public bool ShowTips = true;

        public TrayMode(Window window, Action exit) { this.window = window; this.exit = exit; }

        public void Hide(bool automatic)
        {
            if (Hidden) return;
            Hidden = true; Automatic = automatic;
            if (window.WindowState != WindowState.Minimized) restoreState = window.WindowState;
            ShowIcon(automatic);
            window.ShowInTaskbar = false;
            window.Hide();
            if (Changed != null) Changed();
        }

        // activate is false when the window comes back by itself, so it never takes the focus from a game.
        public void Show(bool activate = true)
        {
            if (!Hidden) return;
            Hidden = false; Automatic = false;
            window.ShowInTaskbar = true;
            bool showActivated = window.ShowActivated;
            if (!activate) window.ShowActivated = false;
            if (window.IsLoaded) window.Show();
            if (window.WindowState == WindowState.Minimized) window.WindowState = restoreState;
            window.ShowActivated = showActivated;
            if (activate) window.Activate();
            HideIcon();
            if (Changed != null) Changed();
        }

        // A second start of the app (SingleInstance) brings this one back: out of the tray, or up from the taskbar.
        // Like the phone's return, it doesn't take the focus from a game.
        public void Bring()
        {
            if (Hidden) { Show(false); return; }
            if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        }

        private void ShowIcon(bool automatic)
        {
            if (icon == null)
            {
                icon = new System.Windows.Forms.NotifyIcon { Text = "Widget Screen" };
                try { icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location); }
                catch (Exception) { icon.Icon = System.Drawing.SystemIcons.Application; }
                var menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Show Widget Screen", null, (s, e) => Show());
                menu.Items.Add("Close Widget Screen", null, (s, e) => { Show(); exit(); });
                icon.ContextMenuStrip = menu;
                icon.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Show(); };
            }
            icon.Visible = true;
            if (automatic && ShowTips) icon.ShowBalloonTip(4000, "Widget Screen", "Your phone is showing the dashboard, so it moved to the tray. Click the icon to bring it back.", System.Windows.Forms.ToolTipIcon.None);
        }

        private void HideIcon() { if (icon != null) icon.Visible = false; }

        public void Dispose()
        {
            if (icon == null) return;
            icon.Visible = false; icon.Dispose(); icon = null;
        }
    }
}
