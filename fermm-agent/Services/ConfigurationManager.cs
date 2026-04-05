using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using FermmAgent.Crypto;

namespace FermmAgent.Services;

/// <summary>
/// Manages agent configuration with optional RSA encryption.
/// Stores config at ~/.fermm/config.json
/// </summary>
public class ConfigurationManager
{
    private readonly string _configDir;
    private readonly string _configPath;
    private readonly string? _privateKeyPath;
    private ConfigEncryption? _encryption;
    private Dictionary<string, string> _config;
    
    public ConfigurationManager()
    {
        _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fermm");
        _configPath = Path.Combine(_configDir, "config.json");
        
        // Check for private key in repo root (for encryption)
        var repoKeyPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "private_rsa.key");
        if (File.Exists(repoKeyPath))
        {
            _privateKeyPath = Path.GetFullPath(repoKeyPath);
            try
            {
                _encryption = new ConfigEncryption(_privateKeyPath);
            }
            catch
            {
                _encryption = null;
            }
        }
        
        _config = LoadConfig();
    }
    
    private Dictionary<string, string> LoadConfig()
    {
        var config = new Dictionary<string, string>();
        
        if (!File.Exists(_configPath))
            return config;
        
        try
        {
            var content = File.ReadAllText(_configPath);
            
            // Try to decrypt if encryption is available
            if (_encryption != null)
            {
                try
                {
                    content = _encryption.Decrypt(content);
                }
                catch
                {
                    // If decryption fails, assume it's unencrypted
                }
            }
            
            config = JsonSerializer.Deserialize<Dictionary<string, string>>(content) ?? new();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to load config: {ex.Message}");
        }
        
        return config;
    }
    
    private void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(_configDir);
            
            var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
            
            // Encrypt if available
            if (_encryption != null)
            {
                json = _encryption.Encrypt(json);
            }
            
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error saving config: {ex.Message}");
        }
    }
    
    public void SetServerUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Server URL cannot be empty");
        
        _config["server_url"] = url;
        SaveConfig();
        Console.WriteLine($"✓ Server URL saved to {_configPath}");
    }
    
    public string? GetServerUrl()
    {
        return _config.TryGetValue("server_url", out var url) ? url : null;
    }
    
    public void Set(string key, string value)
    {
        _config[key] = value;
        SaveConfig();
    }
    
    public string? Get(string key)
    {
        return _config.TryGetValue(key, out var value) ? value : null;
    }
    
    public void PrintConfig()
    {
        Console.WriteLine("Current Configuration:");
        Console.WriteLine("=====================");
        
        if (_config.Count == 0)
        {
            Console.WriteLine("(No configuration set)");
            return;
        }
        
        foreach (var kvp in _config)
        {
            // Mask sensitive values
            var displayValue = kvp.Key.Contains("key") || kvp.Key.Contains("secret") || kvp.Key.Contains("token")
                ? "***"
                : kvp.Value;
            
            Console.WriteLine($"{kvp.Key}: {displayValue}");
        }
        
        Console.WriteLine();
        Console.WriteLine($"Encryption: {(_encryption != null ? "Enabled" : "Disabled")}");
        Console.WriteLine($"Config path: {_configPath}");
    }
}
