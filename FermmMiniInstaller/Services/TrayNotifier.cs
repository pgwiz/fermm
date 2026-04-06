using System;
using System.Windows.Forms;

namespace FermmMiniInstaller.Services
{
    public class TrayNotifier : IDisposable
    {
        // Tray is completely disabled - kept for API compatibility
        public TrayNotifier(bool silent = false) { }
        public void ShowIcon(string message, int percentComplete) { }
        public void ShowSummary(string message) { }
        public void Dispose() { }
    }
}
