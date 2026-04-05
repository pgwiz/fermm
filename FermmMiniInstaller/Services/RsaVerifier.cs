using System;
using System.Security.Cryptography;
using System.Text;

namespace FermmMiniInstaller.Services;

public class RsaVerifier
{
    private const string RSA_PUBLIC_KEY = """
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAshVEgVth4GZlZleOEMX1
QZjt4Hch3a1m5W4pVVFvZnx1OyT74Dv7lD25YRrPyve+F0xrd+MmUv1UB0m8GVn4
IYTJRMZSH5GO/n4im0hwz9wqE9qm/SbxxXAtTZgHET1/Wx028O1NhTbbBrG3t5Ab
2ORfmsIX1f48Q8qZay5Ph5sMpxKW8vRjJQBpweFPqr+uf3ReYATNZm9p4fNQkIAO
Ay8XomeZHbGTEtBZfI4f2HOWb7IzIapPBX4L5voNJqHVlTUS7MvPdlUXw/PGP1aM
1zdm5gNa30qItZun/8gCnhMKTK95b10KKpD449+/g3u2VksVqrVKg3xzVt/dEpBM
9wIDAQAB
-----END PUBLIC KEY-----
""";

    public bool VerifySignature(string filePath, string signatureBase64)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            using (var rsa = RSA.Create())
            {
                rsa.ImportFromPem(RSA_PUBLIC_KEY.AsSpan());
                
                byte[] fileData = File.ReadAllBytes(filePath);
                byte[] signature = Convert.FromBase64String(signatureBase64);

                return rsa.VerifyData(fileData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Signature verification failed: {ex.Message}");
            return false;
        }
    }

    public string GetPublicKey()
    {
        return RSA_PUBLIC_KEY;
    }
}
