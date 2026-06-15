using System.Security.Cryptography;
using System.Text;
using SagaImporter.Abstractions;

namespace SagaImporter.Windows.Platform;

/// <summary>
/// Encrypts/decrypts the API token using the Windows Data Protection API
/// (current-user scope) so it is never persisted in plaintext.
/// </summary>
public sealed class DpapiTokenProtector : ITokenProtector
{
    // Extra entropy ties the ciphertext to this application.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SagaImporter.v1");

    public string Protect(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        byte[] data = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public string Unprotect(string protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
        {
            return string.Empty;
        }

        try
        {
            byte[] encrypted = Convert.FromBase64String(protectedBase64);
            byte[] data = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            // Corrupted, tampered, or created under a different user/machine.
            return string.Empty;
        }
    }
}
