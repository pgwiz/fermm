using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Security.Principal;
using FermmMiniInstaller.Models;

namespace FermmMiniInstaller.Services
{
    public class Installer
    {
        public event Action<string>? ProgressChanged;

        private const string ServiceName = "FERMMAgent";
        private readonly string _installPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microlens"
        );

        public async Task InstallAsync(string agentBundlePath)
        {
            try
            {
                // Create installation directory
                Directory.CreateDirectory(_installPath);
                ProgressChanged?.Invoke("Creating directory...");

                string logsPath = Path.Combine(_installPath, "logs");
                Directory.CreateDirectory(logsPath);

                string targetAgentPath = Path.Combine(_installPath, "fermm-agent.exe");

                if (string.Equals(Path.GetExtension(agentBundlePath), ".zip", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract agent bundle
                    ProgressChanged?.Invoke("Extracting agent bundle...");
                    ZipFile.ExtractToDirectory(agentBundlePath, _installPath, overwriteFiles: true);

                    if (!File.Exists(targetAgentPath))
                    {
                        string? foundAgent = Directory
                            .GetFiles(_installPath, "fermm-agent.exe", SearchOption.AllDirectories)
                            .FirstOrDefault();

                        if (foundAgent != null && !string.Equals(foundAgent, targetAgentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            File.Copy(foundAgent, targetAgentPath, overwrite: true);
                        }
                    }
                }
                else
                {
                    // Copy agent executable
                    ProgressChanged?.Invoke("Installing agent...");
                    File.Copy(agentBundlePath, targetAgentPath, overwrite: true);
                }

                // Copy this installer for future updates
                ProgressChanged?.Invoke("Setting up updater...");
                string installerPath = Path.Combine(_installPath, "FermmMiniInstaller.exe");
                string currentExePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(currentExePath) && File.Exists(currentExePath) && 
                    !string.Equals(currentExePath, installerPath, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(currentExePath, installerPath, overwrite: true);
                }

                // Write configuration
                ProgressChanged?.Invoke("Writing config...");
                await WriteConfigAsync();

                // Prefer Windows service for auto-start (falls back to Run key)
                var serviceInstalled = TryEnsureService(targetAgentPath);
                if (!serviceInstalled)
                {
                    ProgressChanged?.Invoke("Setting auto-start...");
                    SetAutoStart(targetAgentPath);
                }
                else
                {
                    RemoveAutoStart();
                }

                ProgressChanged?.Invoke("Complete!");
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                throw new Exception($"Installation failed: {ex.Message}", ex);
            }
        }

        private async Task WriteConfigAsync()
        {
            string configPath = Path.Combine(_installPath, "config.json");

            var config = new
            {
                version = "1.0.0",
                install_date = DateTime.Now.ToString("dd/MM/yy"),
                install_path = _installPath,
                installed_at = DateTime.UtcNow.ToString("O")
            };

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, json);
        }

        private void SetAutoStart(string agentPath)
        {
            try
            {
                // HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                {
                    if (key != null)
                    {
                        key.SetValue("FermmAgent", agentPath);
                    }
                }
            }
            catch (Exception ex)
            {
                ProgressChanged?.Invoke($"Failed to set auto-start: {ex.Message}");
            }
        }

        private void RemoveAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true))
                {
                    if (key?.GetValue("FermmAgent") != null)
                    {
                        key.DeleteValue("FermmAgent", throwOnMissingValue: false);
                    }
                }
            }
            catch (Exception ex)
            {
                ProgressChanged?.Invoke($"Failed to remove auto-start key: {ex.Message}");
            }
        }

        private bool TryEnsureService(string agentPath)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            if (IsServiceInstalled())
            {
                ProgressChanged?.Invoke("Service already installed. Ensuring auto-start...");
                TryRunProcess("sc", $"config \"{ServiceName}\" start= auto");
                TryStartService();
                return true;
            }

            if (!IsRunningAsAdmin())
            {
                ProgressChanged?.Invoke("Service install requires admin. Falling back to user startup.");
                return false;
            }

            ProgressChanged?.Invoke("Installing Windows service...");
            if (!TryRunProcess(agentPath, "install"))
            {
                ProgressChanged?.Invoke("Service install failed. Falling back to user startup.");
                return false;
            }

            TryStartService();
            return true;
        }

        private bool IsServiceInstalled()
        {
            return TryRunProcess("sc", $"query \"{ServiceName}\"");
        }

        private void TryStartService()
        {
            if (!IsRunningAsAdmin())
            {
                ProgressChanged?.Invoke("Service installed, but admin is required to start it now.");
                return;
            }

            TryRunProcess("sc", $"start \"{ServiceName}\"");
        }

        private bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception ex)
            {
                ProgressChanged?.Invoke($"Failed to check admin rights: {ex.Message}");
                return false;
            }
        }

        private bool TryRunProcess(string fileName, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    ProgressChanged?.Invoke($"Failed to start process: {fileName}");
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    return true;
                }

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();
                var message = string.Join(" ", new[] { output, error }
                    .Where(s => !string.IsNullOrWhiteSpace(s)))
                    .Trim();

                ProgressChanged?.Invoke(string.IsNullOrWhiteSpace(message)
                    ? $"Command failed: {fileName} {arguments}"
                    : message);

                return false;
            }
            catch (Exception ex)
            {
                ProgressChanged?.Invoke($"Process error: {ex.Message}");
                return false;
            }
        }
    }
}
