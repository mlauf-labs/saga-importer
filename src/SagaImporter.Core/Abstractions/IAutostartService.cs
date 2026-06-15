namespace SagaImporter.Abstractions;

/// <summary>
/// Registers or unregisters the importer to start automatically at login/boot.
/// Windows: HKCU\...\Run registry key. Linux: managed externally by systemd
/// (NoopAutostartService).
/// </summary>
public interface IAutostartService
{
    bool IsEnabled();

    /// <summary>Enables or disables autostart. May throw; callers should log on failure.</summary>
    void Apply(bool enabled);
}
