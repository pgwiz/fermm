using System;
using System.IO;
using System.Threading.Tasks;
using FermmMiniInstaller.Services;
using FermmMiniInstaller.Models;

namespace FermmMiniInstaller
{
    class Program
    {
        private static string _logFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microlens",
            "logs",
            $"install-{DateTime.Now:yyyy-MM-dd-HHmmss}.log"
        );

        private static bool _silentMode = false;
        private static TrayNotifier? _tray;

        [STAThread]
        static async Task Main(string[] args)
        {
            try
            {
                // Parse arguments
                _silentMode = args.Contains("/silent") || args.Contains("-s");
                
                if (args.Any(a => a.StartsWith("/log", StringComparison.OrdinalIgnoreCase)))
                {
                    var logArg = args.FirstOrDefault(a => a.StartsWith("/log", StringComparison.OrdinalIgnoreCase));
                    if (logArg?.Contains("=") == true)
                    {
                        _logFile = logArg.Split('=')[1];
                    }
                }

                // Initialize logger
                await Log("========================================");
                await Log($"FERMM Mini Installer Started");
                await Log($"Silent Mode: {_silentMode}");
                await Log($"Log File: {_logFile}");
                await Log($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                await Log("========================================");

                // Show tray if not silent (no balloon notifications, just icon)
                if (!_silentMode)
                {
                    _tray = new TrayNotifier(silent: false);
                    _tray.ShowIcon("Initializing...", 0);
                }

                // Check for updates (skip if not installed yet)
                string installRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microlens"
                );
                string configPath = Path.Combine(installRoot, "config.json");
                string existingAgentPath = Path.Combine(installRoot, "fermm-agent.exe");
                bool isInstalled = File.Exists(configPath) && File.Exists(existingAgentPath);
                bool needsRepair = false;

                if (File.Exists(existingAgentPath))
                {
                    long agentSize = new FileInfo(existingAgentPath).Length;
                    // If agent is too small, it likely lacks required bundled files
                    if (agentSize < 50 * 1024 * 1024)
                    {
                        needsRepair = true;
                        await Log($"Existing agent appears incomplete (size {agentSize} bytes).");
                    }
                }

                string appsettingsPath = Path.Combine(installRoot, "appsettings.json");
                string privateKeyPath = Path.Combine(installRoot, "private_rsa.key");
                if (!File.Exists(appsettingsPath) || !File.Exists(privateKeyPath))
                {
                    needsRepair = true;
                    await Log("Missing required agent files (appsettings.json or private_rsa.key).");
                }

                UpdateInfo updateInfo;
                if (!isInstalled || needsRepair)
                {
                    await Log("Initial install or repair detected. Skipping update check.");
                    updateInfo = new UpdateInfo { NeedsUpdate = true };
                }
                else
                {
                    await Log("Checking for updates from Vercel...");
                    var verifier = new VerificationService();
                    updateInfo = await verifier.CheckForUpdateAsync();
                }

                if (!updateInfo.NeedsUpdate)
                {
                    await Log("Installation is up to date. No update needed.");
                    _tray?.ShowSummary("✓ Up to date");
                    await Task.Delay(3000);
                    return;
                }

                await Log($"Update available: {updateInfo.NewDate}");
                
                // Download agent
                await Log("Starting agent bundle download...");
                var downloader = new HttpDownloader();
                var downloadProgress = new DownloadProgress();
                
                downloader.ProgressChanged += (progress) =>
                {
                    _tray?.ShowIcon($"Downloading... {progress.PercentComplete}%", progress.PercentComplete);
                };

                string agentPath = await downloader.DownloadAgentAsync(
                    "https://rmm.bware.systems/xs",
                    downloadProgress,
                    ".zip"
                );
                await Log($"Download complete: {agentPath}");

                // Verify signature (skip for MVP - no signing system yet)
                await Log("Skipping RSA signature verification (MVP phase)");
                // TODO: Implement signature generation during build
                // rsaVerifier.VerifySignature(agentPath, signatureFromServer);

                // Install
                await Log("Starting installation...");
                var installer = new Installer();
                installer.ProgressChanged += (message) =>
                {
                    _tray?.ShowIcon($"Installing... {message}", 75);
                };

                await installer.InstallAsync(agentPath);
                await Log("Installation complete");

                // Show success
                _tray?.ShowSummary("✓ Installation Complete");
                await Log("Installation finished successfully");
                await Task.Delay(5000);
            }
            catch (Exception ex)
            {
                await Log($"ERROR: {ex.Message}");
                await Log($"Stack: {ex.StackTrace}");
                
                if (!_silentMode)
                {
                    _tray?.ShowSummary($"✗ Error: {ex.Message}");
                    await Task.Delay(5000);
                }
            }
            finally
            {
                _tray?.Dispose();
            }
        }

        private static async Task Log(string message)
        {
            string logMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            
            // Console output
            if (!_silentMode)
                Console.WriteLine(logMessage);

            // File logging
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                await File.AppendAllTextAsync(_logFile, logMessage + Environment.NewLine);
            }
            catch { }
        }
    }
}
