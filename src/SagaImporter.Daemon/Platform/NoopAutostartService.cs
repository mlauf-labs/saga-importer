using SagaImporter.Abstractions;

namespace SagaImporter.Daemon.Platform;

/// <summary>
/// Autostart is managed externally by systemd (the init script registers and enables
/// the unit). There is nothing for the daemon to do here.
/// </summary>
public sealed class NoopAutostartService : IAutostartService
{
    public bool IsEnabled() => false;

    public void Apply(bool enabled) { }
}
