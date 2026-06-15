using System.IO;
using System.Net;
using System.Net.Http;
using SagaImporter.Services;
using Xunit;

namespace SagaImporter.Tests;

public class SagaClientTests
{
    [Fact]
    public async Task UploadAsync_ParsesAcceptedResponse()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/documents", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("tok", request.Headers.Authorization.Parameter);

            return Respond(HttpStatusCode.Accepted,
                """{ "document_id": "doc-1", "status": "pending", "title": "a.pdf" }""");
        });

        var client = new SagaClient(new HttpClient(handler));
        client.Configure("http://host:8000", "tok");

        string file = WriteTempFile("hello");
        try
        {
            UploadResult result = await client.UploadAsync(file, "a.pdf");
            Assert.Equal("doc-1", result.DocumentId);
            Assert.Equal(DocumentStatus.Pending, result.Status);
            Assert.Equal("a.pdf", result.Title);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task UploadAsync_Throws_OnUnauthorized()
    {
        var handler = new StubHandler((_, _) =>
            Respond(HttpStatusCode.Unauthorized, """{ "code": "auth_error" }"""));
        var client = new SagaClient(new HttpClient(handler));
        client.Configure("http://host:8000", "bad");

        string file = WriteTempFile("x");
        try
        {
            SagaException ex = await Assert.ThrowsAsync<SagaException>(
                () => client.UploadAsync(file, "a.pdf"));
            Assert.Equal(401, ex.StatusCode);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task GetStatusAsync_ParsesReady()
    {
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("/documents/doc-1/status", request.RequestUri!.AbsolutePath);
            return Respond(HttpStatusCode.OK,
                """{ "document_id": "doc-1", "status": "ready", "error": null }""");
        });

        var client = new SagaClient(new HttpClient(handler));
        client.Configure("http://host:8000", "tok");

        StatusResult status = await client.GetStatusAsync("doc-1");
        Assert.Equal(DocumentStatus.Ready, status.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_TrueOn200()
    {
        var handler = new StubHandler((_, _) => Respond(HttpStatusCode.OK, "{ \"status\": \"ok\" }"));
        var client = new SagaClient(new HttpClient(handler));
        client.Configure("http://host:8000", string.Empty);

        Assert.True(await client.CheckHealthAsync());
    }

    [Theory]
    [InlineData("ready", DocumentStatus.Ready)]
    [InlineData("FAILED", DocumentStatus.Failed)]
    [InlineData("converting", DocumentStatus.Converting)]
    [InlineData("weird", DocumentStatus.Unknown)]
    [InlineData(null, DocumentStatus.Unknown)]
    public void ParseStatus_MapsKnownValues(string? raw, DocumentStatus expected)
    {
        Assert.Equal(expected, SagaClient.ParseStatus(raw));
    }

    private static HttpResponseMessage Respond(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json) };

    private static string WriteTempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), "dsi-up-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responder;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responder) =>
            _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_responder(request, cancellationToken));
    }
}
