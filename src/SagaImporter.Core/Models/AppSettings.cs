using System.Text.Json.Serialization;

namespace SagaImporter.Models;

/// <summary>
/// When a successfully uploaded file is removed from the watched folder.
/// </summary>
public enum DeleteTrigger
{
    /// <summary>Delete only after Saga reports the document as <c>ready</c> (safest).</summary>
    OnReady = 0,

    /// <summary>Delete right after the upload is accepted (HTTP 202).</summary>
    OnAccepted = 1,
}

/// <summary>
/// All user-configurable settings. Persisted as JSON; the API token is stored
/// separately in encrypted form (see <see cref="EncryptedApiToken"/>).
/// </summary>
public sealed class AppSettings
{
    public string WatchedFolder { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>
    /// Protected API token. On Windows: DPAPI-encrypted, Base64-encoded. On Linux:
    /// stored as plaintext — the settings file is chmod 600 and owned by the service
    /// user, which provides the equivalent access control.
    /// </summary>
    public string EncryptedApiToken { get; set; } = string.Empty;

    public DeleteTrigger DeleteTrigger { get; set; } = DeleteTrigger.OnReady;

    /// <summary>Maximum time to wait for a document to reach <c>ready</c>.</summary>
    public int StatusPollTimeoutMinutes { get; set; } = 10;

    /// <summary>Interval between status polls while waiting for <c>ready</c>.</summary>
    public int StatusPollIntervalSeconds { get; set; } = 3;

    /// <summary>Periodic full rescan of the watched folder, in minutes.</summary>
    public int RescanIntervalMinutes { get; set; } = 10;

    public string FailedSubfolderName { get; set; } = "failed";

    /// <summary>Comma-separated allowlist of extensions (e.g. ".pdf,.docx"). Empty = all.</summary>
    public string IncludeExtensions { get; set; } = string.Empty;

    /// <summary>Comma-separated blocklist of extensions.</summary>
    public string ExcludeExtensions { get; set; } = ".tmp,.crdownload,.part";

    /// <summary>Quiet period a file's size/lock state must hold before it is imported.</summary>
    public int StabilityDelaySeconds { get; set; } = 2;

    public bool StartWithWindows { get; set; }

    /// <summary>Default SMB share name suggested in the UI (Windows) or used by the init script (Linux).</summary>
    public string SmbShareName { get; set; } = "Inbox";

    [JsonIgnore]
    public bool HasRequiredFields =>
        !string.IsNullOrWhiteSpace(WatchedFolder)
        && !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(EncryptedApiToken);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
