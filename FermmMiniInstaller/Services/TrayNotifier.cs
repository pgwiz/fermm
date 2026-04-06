using System;
using System.Windows.Forms;

namespace FermmMiniInstaller.Services
{
    public class TrayNotifier : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;
        private bool _silentMode;

        public TrayNotifier(bool silent = false)
        {
            _silentMode = silent;
            
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
            if (_notifyIcon == null || _silentMode) return;

            _notifyIcon.Visible = true;
            _notifyIcon.Text = $"FERMM: {message}".Length > 63 
                ? $"FERMM: {message}".Substring(0, 63) 
                : $"FERMM: {message}";
        }

        public void ShowSummary(string message)
        {
            if (_notifyIcon == null || _silentMode) return;

            _notifyIcon.Visible = true;
            _notifyIcon.Text = $"FERMM: {message}".Length > 63
                ? $"FERMM: {message}".Substring(0, 63)
                : $"FERMM: {message}";

            // Auto-hide after 3 seconds
            Task.Run(async () =>
            {
                await Task.Delay(3000);
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
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            _contextMenu?.Dispose();
        }
    }
}
