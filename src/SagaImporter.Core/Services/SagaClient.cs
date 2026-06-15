using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SagaImporter.Services;

/// <summary>Lifecycle status of a document in Saga.</summary>
public enum DocumentStatus
{
    Unknown,
    Pending,
    Converting,
    Analyzing,
    Indexing,
    Ready,
    Failed,
}

public sealed record UploadResult(string DocumentId, DocumentStatus Status, string? Title);

public sealed record StatusResult(string DocumentId, DocumentStatus Status, string? Error);

/// <summary>
/// Thin async client for the Saga REST API. Performs HTTP only and translates
/// failures into <see cref="SagaException"/>. UI-agnostic and unit-testable via an
/// injected <see cref="HttpMessageHandler"/>.
/// </summary>
public sealed class SagaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    // C# 14 'field' keyword: backing-field properties that normalize on assignment
    // without an explicit backing field.
    private string BaseUrl
    {
        get => field;
        set => field = (value ?? string.Empty).TrimEnd('/');
    } = "http://localhost:8000";

    private string Token
    {
        get => field;
        set => field = value ?? string.Empty;
    } = string.Empty;

    public SagaClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
    }

    /// <summary>Updates the base URL and bearer token used for subsequent requests.</summary>
    public void Configure(string baseUrl, string token)
    {
        BaseUrl = baseUrl;
        Token = token;
    }

    /// <summary>Calls <c>GET /health</c> (no auth). Returns true on HTTP 200.</summary>
    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri("/health"));
            using HttpResponseMessage response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Uploads a file via <c>POST /documents</c> (multipart/form-data, field "file").</summary>
    public async Task<UploadResult> UploadAsync(
        string filePath,
        string fileName,
        CancellationToken ct = default)
    {
        await using var fileStream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType =
            new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/documents"))
        {
            Content = content,
        };
        AddAuth(request);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SagaException($"Could not reach Saga: {ex.Message}", inner: ex);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new SagaException(
                    $"Upload failed ({(int)response.StatusCode}): {Describe(response.StatusCode, body)}",
                    (int)response.StatusCode);
            }

            UploadDto? dto = Deserialize<UploadDto>(body);
            if (dto is null || string.IsNullOrEmpty(dto.DocumentId))
            {
                throw new SagaException("Upload response did not contain a document id.");
            }

            return new UploadResult(dto.DocumentId, ParseStatus(dto.Status), dto.Title);
        }
    }

    /// <summary>Fetches a document's processing status via <c>GET /documents/{id}/status</c>.</summary>
    public async Task<StatusResult> GetStatusAsync(string documentId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, BuildUri($"/documents/{Uri.EscapeDataString(documentId)}/status"));
        AddAuth(request);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new SagaException($"Could not reach Saga: {ex.Message}", inner: ex);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new SagaException(
                    $"Status check failed ({(int)response.StatusCode}): {Describe(response.StatusCode, body)}",
                    (int)response.StatusCode);
            }

            StatusDto? dto = Deserialize<StatusDto>(body);
            if (dto is null)
            {
                throw new SagaException("Status response could not be parsed.");
            }

            return new StatusResult(dto.DocumentId ?? documentId, ParseStatus(dto.Status), dto.Error);
        }
    }

    private void AddAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }
    }

    private Uri BuildUri(string path) => new($"{BaseUrl}{path}");

    private static T? Deserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static DocumentStatus ParseStatus(string? raw) => raw?.ToLowerInvariant() switch
    {
        "pending" => DocumentStatus.Pending,
        "converting" => DocumentStatus.Converting,
        "analyzing" => DocumentStatus.Analyzing,
        "indexing" => DocumentStatus.Indexing,
        "ready" => DocumentStatus.Ready,
        "failed" => DocumentStatus.Failed,
        _ => DocumentStatus.Unknown,
    };

    private static string Describe(HttpStatusCode code, string body)
    {
        string trimmed = body.Length > 300 ? body[..300] : body;
        return code switch
        {
            HttpStatusCode.Unauthorized => "invalid or missing API token.",
            HttpStatusCode.BadRequest => $"rejected by server. {trimmed}",
            HttpStatusCode.Conflict => "duplicate document (dedup policy = reject).",
            _ => string.IsNullOrWhiteSpace(trimmed) ? code.ToString() : trimmed,
        };
    }

    private sealed class UploadDto
    {
        [JsonPropertyName("document_id")]
        public string? DocumentId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }
    }

    private sealed class StatusDto
    {
        [JsonPropertyName("document_id")]
        public string? DocumentId { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }
}
