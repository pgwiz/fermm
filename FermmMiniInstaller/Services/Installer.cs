using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;
using FermmMiniInstaller.Models;

namespace FermmMiniInstaller.Services
{
    public class Installer
    {
        public event Action<string>? ProgressChanged;

        private readonly string _installPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microlens"
        );

        public async Task InstallAsync(string agentExePath)
        {
            try
            {
                // Create installation directory
                Directory.CreateDirectory(_installPath);
                ProgressChanged?.Invoke("Creating directory...");

                string logsPath = Path.Combine(_installPath, "logs");
                Directory.CreateDirectory(logsPath);

                // Copy agent executable
                ProgressChanged?.Invoke("Installing agent...");
                string targetAgentPath = Path.Combine(_installPath, "fermm-agent.exe");
                File.Copy(agentExePath, targetAgentPath, overwrite: true);

                // Copy this installer for future updates
                ProgressChanged?.Invoke("Setting up updater...");
                string installerPath = Path.Combine(_installPath, "FermmMiniInstaller.exe");
                string currentInstallerPath = AppContext.BaseDirectory;
                string currentExePath = Path.Combine(currentInstallerPath, "FermmMiniInstaller.exe");
                if (File.Exists(currentExePath))
                {
                    File.Copy(currentExePath, installerPath, overwrite: true);
                }

                // Write configuration
                ProgressChanged?.Invoke("Writing config...");
                await WriteConfigAsync();

                // Set Windows startup registry key
                ProgressChanged?.Invoke("Setting auto-start...");
                SetAutoStart(targetAgentPath);

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
                System.Diagnostics.Debug.WriteLine($"Failed to set auto-start: {ex.Message}");
            }
        }
    }
}
