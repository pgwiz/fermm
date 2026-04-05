using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FermmAgent.Crypto;

/// <summary>
/// Handles RSA encryption/decryption for configuration files.
/// Uses OAEP padding with SHA-256 for secure encryption.
/// </summary>
public class ConfigEncryption
{
    private readonly RSA _privateKey;
    
    public ConfigEncryption(string privateKeyPath)
    {
        if (!File.Exists(privateKeyPath))
            throw new FileNotFoundException($"Private key not found: {privateKeyPath}");
        
        try
        {
            var keyText = File.ReadAllText(privateKeyPath);
            _privateKey = RSA.Create();
            _privateKey.ImportFromPem(keyText.ToCharArray());
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load RSA private key: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Encrypts plaintext using the public key derived from the private key.
    /// Returns base64-encoded ciphertext.
    /// </summary>
    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentNullException(nameof(plaintext));
        
        try
        {
            // Export and re-import public key to ensure we're using the same RSA instance
            var publicKeyBytes = _privateKey.ExportRSAPublicKey();
            var publicRsa = RSA.Create();
            publicRsa.ImportRSAPublicKey(publicKeyBytes, out _);
            
            var encrypted = publicRsa.Encrypt(
                Encoding.UTF8.GetBytes(plaintext),
                RSAEncryptionPadding.OaepSHA256
            );
            
            return Convert.ToBase64String(encrypted);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Encryption failed: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Decrypts base64-encoded ciphertext using the private key.
    /// Returns the plaintext string.
    /// </summary>
    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            throw new ArgumentNullException(nameof(ciphertext));
        
        try
        {
            var encrypted = Convert.FromBase64String(ciphertext);
            var decrypted = _privateKey.Decrypt(encrypted, RSAEncryptionPadding.OaepSHA256);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Decryption failed: {ex.Message}", ex);
        }
    }
}
