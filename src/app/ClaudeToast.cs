using System;
using System.Reflection;

namespace WidgetScreen
{
    // The Windows notification for a Claude Code session (package 3, decision 025): a notification-area icon shows it as
    // a Windows notification (Windows 10 and 11 show a balloon tip as one), so nothing needs a package, a shortcut's
    // app id or WinRT. The icon stays while a session needs you, and a click on the notification or the icon brings
    // that session's window forward. Settings → Claude → Notify me turns it off, and Quiet mode keeps it silent.
    public sealed class ClaudeToast : IDisposable
    {
        private readonly Action<string> open;
        private System.Windows.Forms.NotifyIcon icon;
        private string session;
        // The checks turn this off so nothing pops up on the desktop.
        public static bool Enabled = true;
        public int Shown { get; private set; }

        public ClaudeToast(Action<string> open) { this.open = open; }

        public void Show(string title, string text, string sessionId, bool warning)
        {
            Shown++;
            session = sessionId;
            if (!Enabled) return;
            try
            {
                if (icon == null)
                {
                    icon = new System.Windows.Forms.NotifyIcon { Text = "Claude Code needs you" };
                    try { icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location); }
                    catch (Exception) { icon.Icon = System.Drawing.SystemIcons.Application; }
                    icon.BalloonTipClicked += (s, e) => Open();
                    icon.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) Open(); };
                }
                icon.Text = Short(title, 63);
                icon.Visible = true;
                icon.ShowBalloonTip(10000, Short(title, 63), Short(text.Length == 0 ? " " : text, 255), warning ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
            }
            catch (Exception) { }
        }

        // Nothing needs you any more: the icon goes.
        public void Clear() { if (icon != null) { try { icon.Visible = false; } catch (Exception) { } } }

        private void Open() { if (session != null && open != null) open(session); Clear(); }

        private static string Short(string text, int most) { text = text ?? ""; return text.Length <= most ? text : text.Substring(0, most - 1) + "…"; }

        public void Dispose() { if (icon == null) return; try { icon.Visible = false; icon.Dispose(); } catch (Exception) { } icon = null; }
    }
}
