using System;
using System.Windows.Forms;
using FermmAgent.UI;

namespace FermmAgent.Overlay;

internal static class OverlayProgram
{
    [STAThread]
    static void Main(string[] args)
    {
        string deviceId = "unknown";
        
        // Parse --device-id argument
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--device-id" && i + 1 < args.Length)
            {
                deviceId = args[i + 1];
                break;
            }
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new OverlayForm(deviceId));
    }
}
