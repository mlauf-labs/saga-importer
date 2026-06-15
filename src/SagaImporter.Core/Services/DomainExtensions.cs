using SagaImporter.Models;

namespace SagaImporter.Services;

/// <summary>
/// C# 14 extension members: instance extension properties that turn the raw,
/// clamped integer settings into ready-to-use <see cref="TimeSpan"/> values, and a
/// terminal-state check for <see cref="DocumentStatus"/>. Keeping the clamping in one
/// place avoids repeating Math.Max/TimeSpan conversions across the pipeline.
/// </summary>
public static class DomainExtensions
{
    extension(AppSettings settings)
    {
        /// <summary>Periodic full-rescan interval (at least one minute).</summary>
        public TimeSpan RescanInterval => TimeSpan.FromMinutes(Math.Max(1, settings.RescanIntervalMinutes));

        /// <summary>Maximum time to wait for a document to reach <c>ready</c>.</summary>
        public TimeSpan ReadyTimeout => TimeSpan.FromMinutes(Math.Max(1, settings.StatusPollTimeoutMinutes));

        /// <summary>Delay between status polls.</summary>
        public TimeSpan PollInterval => TimeSpan.FromSeconds(Math.Max(1, settings.StatusPollIntervalSeconds));

        /// <summary>Quiet period a file's size must hold before it is considered stable.</summary>
        public TimeSpan StabilityQuietPeriod => TimeSpan.FromSeconds(Math.Max(0, settings.StabilityDelaySeconds));
    }

    extension(DocumentStatus status)
    {
        /// <summary>True once ingestion has finished, whether successfully or not.</summary>
        public bool IsTerminal => status is DocumentStatus.Ready or DocumentStatus.Failed;
    }
}
