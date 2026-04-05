using System;
using System.Windows.Forms;

namespace FermmMiniInstaller.Services
{
    public class TrayNotifier : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;

        public TrayNotifier()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = false,
                Text = "FERMM Installer"
            };

            _contextMenu = new ContextMenuStrip();
            _contextMenu.Items.Add("Show Log", null, (s, e) => ShowLog());
            _contextMenu.Items.Add("-");
            _contextMenu.Items.Add("Exit", null, (s, e) => Application.Exit());

            _notifyIcon.ContextMenuStrip = _contextMenu;
        }

        public void ShowIcon(string message, int percentComplete)
        {
            if (_notifyIcon == null) return;

            _notifyIcon.Visible = true;
            _notifyIcon.Text = $"FERMM: {message} ({percentComplete}%)";
            
            // Show balloon tip
            try
            {
                _notifyIcon.ShowBalloonTip(100, "FERMM Installer", message, ToolTipIcon.Info);
            }
            catch { }
        }

        public void ShowSummary(string message)
        {
            if (_notifyIcon == null) return;

            _notifyIcon.Visible = true;
            _notifyIcon.Text = $"FERMM: {message}";

            try
            {
                var tipIcon = message.Contains("✓") ? ToolTipIcon.Info : ToolTipIcon.Error;
                _notifyIcon.ShowBalloonTip(3000, "FERMM Installer", message, tipIcon);
            }
            catch { }

            // Auto-hide after 5 seconds
            Task.Run(async () =>
            {
                await Task.Delay(5000);
                _notifyIcon.Visible = false;
            });
        }

        private void ShowLog()
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microlens", "logs"
            );
            
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", logPath);
            }
            catch { }
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
            _contextMenu?.Dispose();
        }
    }
}
