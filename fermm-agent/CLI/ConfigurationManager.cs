using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using FermmAgent.Crypto;

namespace FermmAgent.CLI;

/// <summary>
/// Configuration data structure for FERMM Agent.
/// Stored encrypted at ~/.fermm/config.json
/// </summary>
public class ConfigData
{
    [JsonPropertyName("server_url")]
    public string ServerUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("device_id")]
    public string DeviceId { get; set; } = string.Empty;
    
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
    
    [JsonPropertyName("poll_interval_seconds")]
    public int PollIntervalSeconds { get; set; } = 15;
}

/// <summary>
/// Manages FERMM Agent configuration persistence with RSA encryption.
/// Stores configuration in ~/.fermm/config.json encrypted with private_rsa.key
/// </summary>
public class ConfigurationManager
{
    private readonly string _configDir;
    private readonly string _configPath;
    private readonly string? _keyPath;
    private ConfigEncryption? _encryption;
    
    public ConfigurationManager()
    {
        _configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".fermm"
        );
        _configPath = Path.Combine(_configDir, "config.json");
        
        // Try to find private key in multiple locations
        _keyPath = FindPrivateKey();
        
        Directory.CreateDirectory(_configDir);
        
        // Initialize encryption if key exists
        if (!string.IsNullOrEmpty(_keyPath) && File.Exists(_keyPath))
        {
            try
            {
                _encryption = new ConfigEncryption(_keyPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"⚠ Warning: Failed to initialize encryption: {ex.Message}");
                _encryption = null;
            }
        }
    }
    
    /// <summary>
    /// Attempts to locate the private RSA key in standard locations.
    /// </summary>
    private string? FindPrivateKey()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "private_rsa.key"),
            Path.Combine(AppContext.BaseDirectory, "..", "private_rsa.key"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "private_rsa.key"),
            "private_rsa.key",
            Path.Combine(Environment.CurrentDirectory, "private_rsa.key"),
        };
        
        foreach (var path in candidates)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                if (File.Exists(fullPath))
                    return fullPath;
            }
            catch { }
        }
        
        return null;
    }
    
    public void SetServerUrl(string url)
    {
        var config = LoadConfig() ?? new ConfigData 
        { 
            DeviceId = Guid.NewGuid().ToString() 
        };
        config.ServerUrl = url;
        SaveConfig(config);
        Console.WriteLine($"✓ Server URL saved: {url}");
    }
    
    public void SetToken(string token)
    {
        var config = LoadConfig() ?? new ConfigData 
        { 
            DeviceId = Guid.NewGuid().ToString() 
        };
        config.Token = token;
        SaveConfig(config);
        Console.WriteLine("✓ Token saved");
    }
    
    public ConfigData? LoadConfig()
    {
        if (!File.Exists(_configPath))
            return null;
        
        try
        {
            var fileContent = File.ReadAllText(_configPath);
            
            // Try to decrypt if encryption is available
            string json = fileContent;
            if (_encryption != null)
            {
                try
                {
                    json = _encryption.Decrypt(fileContent);
                }
                catch
                {
                    // If decryption fails, assume it's unencrypted (for backward compatibility)
                    json = fileContent;
                }
            }
            
            return JsonSerializer.Deserialize<ConfigData>(json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"✗ Error loading config: {ex.Message}");
            return null;
        }
    }
    
    public void SaveConfig(ConfigData config)
    {
        if (string.IsNullOrEmpty(config.DeviceId))
            config.DeviceId = Guid.NewGuid().ToString();
        
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        
        // Encrypt if encryption is available
        string contentToWrite = json;
        if (_encryption != null)
        {
            contentToWrite = _encryption.Encrypt(json);
        }
        
        File.WriteAllText(_configPath, contentToWrite);
        
        // Set file permissions to restrict access on Windows
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var fileInfo = new FileInfo(_configPath);
                var fileSecurity = fileInfo.GetAccessControl();
                fileInfo.SetAccessControl(fileSecurity);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"⚠ Warning: Could not set file permissions: {ex.Message}");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                // Set permissions to 600 (read/write for owner only)
                var chmod = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "/bin/sh",
                        Arguments = $"-c \"chmod 600 '{_configPath}'\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    }
                };
                chmod.Start();
                chmod.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"⚠ Warning: Could not set file permissions: {ex.Message}");
            }
        }
    }
    
    public void ShowConfig()
    {
        var config = LoadConfig();
        if (config == null)
        {
            Console.WriteLine("No configuration found.");
            Console.WriteLine("Run: fermm -s <server-url>");
            return;
        }
        
        Console.WriteLine("Current Configuration:");
        Console.WriteLine($"  Server URL:   {(string.IsNullOrEmpty(config.ServerUrl) ? "(not set)" : config.ServerUrl)}");
        Console.WriteLine($"  Device ID:    {config.DeviceId}");
        Console.WriteLine($"  Token:        {(string.IsNullOrEmpty(config.Token) ? "(not set)" : "***")}");
        Console.WriteLine($"  Poll Interval: {config.PollIntervalSeconds}s");
        
        if (string.IsNullOrEmpty(_keyPath))
        {
            Console.WriteLine("\n⚠ Note: Encryption key not found. Config will be stored unencrypted.");
        }
    }
}
