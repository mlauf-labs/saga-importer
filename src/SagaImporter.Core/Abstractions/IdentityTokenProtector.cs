namespace SagaImporter.Abstractions;

/// <summary>
/// Passthrough implementation: stores the token as-is without encryption. Used in
/// unit tests (via <see cref="FakeTokenProtector"/>) and as the Linux default where
/// file-system permissions (chmod 600) are the protection mechanism.
/// </summary>
public sealed class IdentityTokenProtector : ITokenProtector
{
    public static readonly IdentityTokenProtector Instance = new();

    public string Protect(string plaintext) => plaintext;

    public string Unprotect(string protectedValue) => protectedValue;
}
