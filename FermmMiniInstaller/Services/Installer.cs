using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
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

                // Stop running service before touching files
                await EnsureServiceStoppedForInstallAsync(targetAgentPath);

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

                // Initialize agent with server URL
                ProgressChanged?.Invoke("Configuring agent...");
                await InitializeAgentConfigAsync(targetAgentPath);

                // Prefer Windows service for auto-start
                var serviceStarted = TryEnsureService(targetAgentPath);
                if (serviceStarted)
                {
                    RemoveAutoStart();
                }
                else
                {
                    ProgressChanged?.Invoke("Service not started. Run installer as administrator to install/start.");
                }

                ProgressChanged?.Invoke("Complete!");
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                throw new Exception($"Installation failed: {ex.Message}", ex);
            }
        }

        private async Task InitializeAgentConfigAsync(string agentPath)
        {
            const string DefaultServerUrl = "https://rmm.bware.systems";
            const string VercelConfigUrl = "https://linkify-ten-sable.vercel.app"; // Vercel endpoint for HOST_URL
            string? resolvedUrl = null;

            try
            {
                resolvedUrl = await TryFetchHostUrlAsync(VercelConfigUrl);
            }
            catch (Exception ex)
            {
                ProgressChanged?.Invoke($"Warning: Failed to fetch host URL: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(resolvedUrl))
            {
                resolvedUrl = DefaultServerUrl;
            }

            try
            {
                await SaveConfigDatAsync(resolvedUrl, VercelConfigUrl);
                ProgressChanged?.Invoke($"Server URL configured: {resolvedUrl}");
            }
            catch (Exception ex)
            {
                ProgressChanged?.Invoke($"Warning: Could not write config.dat: {ex.Message}");
                // Non-fatal, agent will prompt for config on first run
            }
        }

        private async Task SaveConfigDatAsync(string serverUrl, string confirmUrl)
        {
            string configDatPath = Path.Combine(_installPath, "config.dat");
            var configData = new
            {
                ServerUrl = serverUrl,
                ConfirmUrl = confirmUrl,
                LastUpdated = DateTime.UtcNow.ToString("O")
            };

            string json = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configDatPath, json);
        }

        private async Task<string?> TryFetchHostUrlAsync(string confirmUrl)
        {
            var privateKeyPath = Path.Combine(_installPath, "private_rsa.key");
            if (!File.Exists(privateKeyPath))
            {
                ProgressChanged?.Invoke("private_rsa.key not found. Using default server.");
                return null;
            }

            var privateKeyPem = await File.ReadAllTextAsync(privateKeyPath);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem.ToCharArray());

            var publicKeyPem = ExportPublicKeyPkcs1Pem(rsa);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var baseUrl = confirmUrl.TrimEnd('/');

            // Step 1: GET /api/public-key
            var pkResponse = await http.GetAsync($"{baseUrl}/api/public-key");
            if (!pkResponse.IsSuccessStatusCode)
            {
                ProgressChanged?.Invoke($"Confirm URL public-key failed: {pkResponse.StatusCode}");
                return null;
            }

            var pkJson = await pkResponse.Content.ReadAsStringAsync();
            using var pkDoc = JsonDocument.Parse(pkJson);
            var serverPublicKey = pkDoc.RootElement.GetProperty("publicKey").GetString();

            var localFingerprint = GetKeyFingerprint(publicKeyPem);
            var serverFingerprint = GetKeyFingerprint(serverPublicKey!);
            if (localFingerprint != serverFingerprint)
            {
                ProgressChanged?.Invoke("Confirm URL key fingerprint mismatch.");
                return null;
            }

            // Step 2: POST /api/data
            var postContent = new StringContent(
                JsonSerializer.Serialize(new { publicKey = publicKeyPem }),
                Encoding.UTF8,
                "application/json"
            );

            var dataResponse = await http.PostAsync($"{baseUrl}/api/data", postContent);
            var dataJson = await dataResponse.Content.ReadAsStringAsync();
            using var dataDoc = JsonDocument.Parse(dataJson);

            if (!dataDoc.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
            {
                ProgressChanged?.Invoke("Confirm URL rejected request.");
                return null;
            }

            var encryptedKey = Convert.FromBase64String(dataDoc.RootElement.GetProperty("encryptedKey").GetString()!);
            var iv = Convert.FromBase64String(dataDoc.RootElement.GetProperty("iv").GetString()!);
            var ciphertext = Convert.FromBase64String(dataDoc.RootElement.GetProperty("ciphertext").GetString()!);
            var authTag = Convert.FromBase64String(dataDoc.RootElement.GetProperty("authTag").GetString()!);

            var aesKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA1);
            using var aesGcm = new AesGcm(aesKey, 16);
            var plaintext = new byte[ciphertext.Length];
            aesGcm.Decrypt(iv, ciphertext, authTag, plaintext);

            var payloadJson = Encoding.UTF8.GetString(plaintext);
            using var payloadDoc = JsonDocument.Parse(payloadJson);

            if (payloadDoc.RootElement.TryGetProperty("HOST_URL", out var hostUrlProp))
            {
                var hostUrl = hostUrlProp.GetString();
                return string.IsNullOrWhiteSpace(hostUrl) ? null : hostUrl;
            }

            return null;
        }

        private static string ExportPublicKeyPkcs1Pem(RSA rsa)
        {
            var publicKeyBytes = rsa.ExportRSAPublicKey();
            var base64 = Convert.ToBase64String(publicKeyBytes);

            var sb = new StringBuilder();
            sb.AppendLine("-----BEGIN RSA PUBLIC KEY-----");
            for (int i = 0; i < base64.Length; i += 64)
            {
                sb.AppendLine(base64.Substring(i, Math.Min(64, base64.Length - i)));
            }
            sb.AppendLine("-----END RSA PUBLIC KEY-----");

            return sb.ToString();
        }

        private static string GetKeyFingerprint(string publicKeyPem)
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem.ToCharArray());
            var derBytes = rsa.ExportRSAPublicKey();
            var hash = SHA256.HashData(derBytes);
            return Convert.ToHexString(hash).ToLower();
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

        private async Task EnsureServiceStoppedForInstallAsync(string agentPath)
        {
            var state = GetServiceState();
            if (state != ServiceState.Running)
            {
                return;
            }

            if (!IsRunningAsAdmin())
            {
                throw new Exception("FERMM service is running. Run installer as administrator to update.");
            }

            ProgressChanged?.Invoke("Stopping service...");
            TryRunProcess(agentPath, "stop-service");

            // Wait for stop (up to 10s)
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(1000);
                if (GetServiceState() != ServiceState.Running)
                {
                    return;
                }
            }

            throw new Exception("Service did not stop. Please retry.");
        }

        private bool TryEnsureService(string agentPath)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var state = GetServiceState();

            if (IsRunningAsAdmin())
            {
                ProgressChanged?.Invoke("Ensuring Windows service...");

                // Stop/uninstall if present (ignore failures)
                TryRunProcess(agentPath, "stop-service");
                TryRunProcess(agentPath, "uninstall");

                if (!TryRunProcess(agentPath, "install"))
                {
                    ProgressChanged?.Invoke("Service install failed.");
                    return false;
                }

                if (!TryRunProcess(agentPath, "start-service"))
                {
                    ProgressChanged?.Invoke("Service start failed.");
                    return false;
                }

                return true;
            }

            // Non-admin: only attempt to start if already installed
            if (state == ServiceState.Running)
            {
                ProgressChanged?.Invoke("Service already running.");
                return true;
            }

            if (state == ServiceState.Stopped)
            {
                ProgressChanged?.Invoke("Attempting to start service...");
                if (TryRunProcess(agentPath, "start-service"))
                {
                    return true;
                }

                ProgressChanged?.Invoke("Service start failed. Run installer as administrator.");
                return false;
            }

            ProgressChanged?.Invoke("Service not installed. Run installer as administrator.");
            return false;
        }

        private enum ServiceState
        {
            Running,
            Stopped,
            Unknown
        }

        private ServiceState? GetServiceState()
        {
            if (!TryRunProcess("sc", $"query \"{ServiceName}\"", out var output, out _))
            {
                return null;
            }

            if (output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceState.Running;
            }

            if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceState.Stopped;
            }

            return ServiceState.Unknown;
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
            return TryRunProcess(fileName, arguments, out _, out _);
        }

        private bool TryRunProcess(string fileName, string arguments, out string output, out string error)
        {
            output = "";
            error = "";

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

                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    return true;
                }

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
