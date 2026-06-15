namespace SagaImporter.Services;

/// <summary>Raised when a Saga API call fails in a way the pipeline should handle.</summary>
public sealed class SagaException : Exception
{
    public SagaException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code, if the failure came from an HTTP response.</summary>
    public int? StatusCode { get; }
}
