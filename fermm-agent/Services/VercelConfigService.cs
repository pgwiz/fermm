using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FermmAgent.Services;

/// <summary>
/// Handles fetching HOST_URL from a Vercel endpoint using RSA encryption.
/// The flow mirrors unlock.py:
/// 1. Load private_rsa.key from app directory
/// 2. GET /api/public-key from the confirm server
/// 3. Verify fingerprints match
/// 4. POST /api/data with our public key
/// 5. Decrypt hybrid RSA+AES encrypted response
/// 6. Extract HOST_URL from the decrypted JSON
/// </summary>
public class VercelConfigService
{
    private readonly string _privateKeyPath;
    private readonly string _configDatPath;
    
    public VercelConfigService()
    {
        var baseDir = AppContext.BaseDirectory;
        _privateKeyPath = Path.Combine(baseDir, "private_rsa.key");
        _configDatPath = Path.Combine(baseDir, "config.dat");
    }
    
    /// <summary>
    /// Fetches the HOST_URL from the Vercel endpoint and saves it to config.dat
    /// </summary>
    public async Task<string?> FetchHostUrlAsync(string confirmUrl)
    {
        Console.WriteLine($"📡 Fetching config from: {confirmUrl}");
        
        if (!File.Exists(_privateKeyPath))
        {
            Console.WriteLine($"✗ Private key not found: {_privateKeyPath}");
            return null;
        }
        
        try
        {
            // Load private key
            var privateKeyPem = await File.ReadAllTextAsync(_privateKeyPath);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem.ToCharArray());
            
            // Derive public key in PKCS1 PEM format
            var publicKeyPem = ExportPublicKeyPkcs1Pem(rsa);
            
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            
            // Step 1: GET /api/public-key to verify fingerprints
            Console.WriteLine($"  GET {confirmUrl}/api/public-key ...");
            var pkResponse = await http.GetAsync($"{confirmUrl.TrimEnd('/')}/api/public-key");
            if (!pkResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"✗ Failed to get public key: {pkResponse.StatusCode}");
                return null;
            }
            
            var pkJson = await pkResponse.Content.ReadAsStringAsync();
            using var pkDoc = JsonDocument.Parse(pkJson);
            var serverPublicKey = pkDoc.RootElement.GetProperty("publicKey").GetString();
            
            // Verify fingerprints match
            var localFingerprint = GetKeyFingerprint(publicKeyPem);
            var serverFingerprint = GetKeyFingerprint(serverPublicKey!);
            
            if (localFingerprint != serverFingerprint)
            {
                Console.WriteLine("✗ Key fingerprint mismatch!");
                Console.WriteLine($"  Local:  {localFingerprint}");
                Console.WriteLine($"  Server: {serverFingerprint}");
                return null;
            }
            
            Console.WriteLine("✓ Key fingerprints match");
            
            // Step 2: POST /api/data with our public key
            Console.WriteLine($"  POST {confirmUrl}/api/data ...");
            var postContent = new StringContent(
                JsonSerializer.Serialize(new { publicKey = publicKeyPem }),
                Encoding.UTF8,
                "application/json"
            );
            
            var dataResponse = await http.PostAsync($"{confirmUrl.TrimEnd('/')}/api/data", postContent);
            var dataJson = await dataResponse.Content.ReadAsStringAsync();
            using var dataDoc = JsonDocument.Parse(dataJson);
            
            if (!dataDoc.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
            {
                Console.WriteLine($"✗ Server rejected request: {dataJson}");
                return null;
            }
            
            // Step 3: Decrypt hybrid RSA+AES encrypted response
            var encryptedKey = Convert.FromBase64String(dataDoc.RootElement.GetProperty("encryptedKey").GetString()!);
            var iv = Convert.FromBase64String(dataDoc.RootElement.GetProperty("iv").GetString()!);
            var ciphertext = Convert.FromBase64String(dataDoc.RootElement.GetProperty("ciphertext").GetString()!);
            var authTag = Convert.FromBase64String(dataDoc.RootElement.GetProperty("authTag").GetString()!);
            
            // RSA-OAEP decrypt the AES key (using SHA1 to match Node.js default)
            var aesKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA1);
            
            // AES-256-GCM decrypt the payload
            using var aesGcm = new AesGcm(aesKey, 16);
            var plaintext = new byte[ciphertext.Length];
            aesGcm.Decrypt(iv, ciphertext, authTag, plaintext);
            
            var payloadJson = Encoding.UTF8.GetString(plaintext);
            using var payloadDoc = JsonDocument.Parse(payloadJson);
            
            Console.WriteLine("✓ Payload decrypted successfully");
            
            // Extract HOST_URL
            if (payloadDoc.RootElement.TryGetProperty("HOST_URL", out var hostUrlProp))
            {
                var hostUrl = hostUrlProp.GetString();
                Console.WriteLine($"✓ HOST_URL: {hostUrl}");
                
                // Save to config.dat
                await SaveConfigAsync(hostUrl!, confirmUrl);
                
                return hostUrl;
            }
            else
            {
                Console.WriteLine("✗ HOST_URL not found in payload");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error fetching config: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Loads saved config from config.dat
    /// </summary>
    public ConfigData? LoadConfig()
    {
        if (!File.Exists(_configDatPath))
            return null;
        
        try
        {
            var json = File.ReadAllText(_configDatPath);
            return JsonSerializer.Deserialize<ConfigData>(json);
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// Saves config to config.dat
    /// </summary>
    private async Task SaveConfigAsync(string serverUrl, string confirmUrl)
    {
        var config = new ConfigData
        {
            ServerUrl = serverUrl,
            ConfirmUrl = confirmUrl,
            LastUpdated = DateTime.UtcNow.ToString("o")
        };
        
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_configDatPath, json);
        Console.WriteLine($"✓ Config saved to: {_configDatPath}");
    }
    
    /// <summary>
    /// Exports RSA public key in PKCS#1 PEM format (matches Python's PKCS1)
    /// </summary>
    private string ExportPublicKeyPkcs1Pem(RSA rsa)
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
    
    /// <summary>
    /// Computes SHA-256 fingerprint of a public key (DER form)
    /// </summary>
    private string GetKeyFingerprint(string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem.ToCharArray());
        var derBytes = rsa.ExportRSAPublicKey();
        var hash = SHA256.HashData(derBytes);
        return Convert.ToHexString(hash).ToLower();
    }
    
    public class ConfigData
    {
        public string ServerUrl { get; set; } = "";
        public string ConfirmUrl { get; set; } = "";
        public string LastUpdated { get; set; } = "";
    }
}
