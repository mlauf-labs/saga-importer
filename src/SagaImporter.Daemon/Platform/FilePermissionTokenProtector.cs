using SagaImporter.Abstractions;

namespace SagaImporter.Daemon.Platform;

/// <summary>
/// On Linux the API token is stored as plaintext in the settings file, which is
/// protected by filesystem permissions (chmod 600, owned by the service user).
/// No additional encryption layer is needed; this protector is an identity transform.
/// </summary>
public sealed class FilePermissionTokenProtector : ITokenProtector
{
    public string Protect(string plaintext) => plaintext;

    public string Unprotect(string protectedValue) => protectedValue;
}
