namespace SagaImporter.Abstractions;

/// <summary>
/// Encrypts/decrypts the API token at rest. Implementations are platform-specific:
/// Windows uses DPAPI; Linux relies on filesystem permissions (chmod 600) and returns
/// the token as-is.
/// </summary>
public interface ITokenProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedValue);
}
