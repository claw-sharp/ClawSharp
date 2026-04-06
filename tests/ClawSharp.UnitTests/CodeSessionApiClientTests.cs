using System.Net;
using System.Text;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class CodeSessionApiClientTests
{
    [Fact]
    public async Task CreateCodeSessionAsync_Returns_Session_Id_And_Sends_Ts_Headers()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"session":{"id":"cse_123"}}""", Encoding.UTF8, "application/json")
            };
        }));

        var sessionId = await CodeSessionApiClient.CreateCodeSessionAsync(
            new CodeSessionApiDependencies(httpClient),
            "https://api.example.com",
            "token",
            "Title",
            5000,
            ["tag-1"]);

        Assert.Equal("cse_123", sessionId);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("token", capturedRequest.Headers.Authorization!.Parameter);
        Assert.Equal(CodeSessionApiClient.AnthropicVersion, capturedRequest.Headers.GetValues("anthropic-version").Single());
        Assert.NotNull(capturedBody);
        Assert.Contains(@"""title"":""Title""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""bridge"":{}", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""tags"":[""tag-1""]", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateCodeSessionAsync_Returns_Null_On_Request_Failure_Or_Invalid_Response()
    {
        List<string> debug = [];
        var failingClient = new HttpClient(new ThrowingHandler(new HttpRequestException("boom")));
        var malformedClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"session":{"id":"bad"}}""", Encoding.UTF8, "application/json")
            }));

        var failed = await CodeSessionApiClient.CreateCodeSessionAsync(
            new CodeSessionApiDependencies(failingClient, debug.Add),
            "https://api.example.com",
            "token",
            "Title",
            5000);
        var malformed = await CodeSessionApiClient.CreateCodeSessionAsync(
            new CodeSessionApiDependencies(malformedClient, debug.Add),
            "https://api.example.com",
            "token",
            "Title",
            5000);

        Assert.Null(failed);
        Assert.Null(malformed);
        Assert.Contains(debug, line => line.Contains("Session create request failed", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("No session.id (cse_*)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchRemoteCredentialsAsync_Returns_Credentials_And_Accepts_String_Epoch()
    {
        HttpRequestMessage? capturedRequest = null;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"worker_jwt":"jwt","api_base_url":"https://worker.example.com","expires_in":3600,"worker_epoch":"12"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }));

        var credentials = await CodeSessionApiClient.FetchRemoteCredentialsAsync(
            new CodeSessionApiDependencies(httpClient),
            "cse_123",
            "https://api.example.com",
            "token",
            5000,
            "trusted-device");

        Assert.NotNull(credentials);
        Assert.Equal("jwt", credentials!.WorkerJwt);
        Assert.Equal("https://worker.example.com", credentials.ApiBaseUrl);
        Assert.Equal(3600, credentials.ExpiresIn);
        Assert.Equal(12, credentials.WorkerEpoch);
        Assert.NotNull(capturedRequest);
        Assert.Equal("trusted-device", capturedRequest!.Headers.GetValues("X-Trusted-Device-Token").Single());
    }

    [Fact]
    public async Task FetchRemoteCredentialsAsync_Returns_Null_On_Non200_And_Invalid_Epoch()
    {
        List<string> debug = [];
        var non200Client = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"error":{"message":"denied"}}""", Encoding.UTF8, "application/json")
            }));
        var invalidEpochClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"worker_jwt":"jwt","api_base_url":"https://worker.example.com","expires_in":3600,"worker_epoch":"12.5"}""",
                    Encoding.UTF8,
                    "application/json")
            }));

        var denied = await CodeSessionApiClient.FetchRemoteCredentialsAsync(
            new CodeSessionApiDependencies(non200Client, debug.Add),
            "cse_123",
            "https://api.example.com",
            "token",
            5000);
        var invalidEpoch = await CodeSessionApiClient.FetchRemoteCredentialsAsync(
            new CodeSessionApiDependencies(invalidEpochClient, debug.Add),
            "cse_123",
            "https://api.example.com",
            "token",
            5000);

        Assert.Null(denied);
        Assert.Null(invalidEpoch);
        Assert.Contains(debug, line => line.Contains("/bridge failed 403: denied", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("/bridge worker_epoch invalid", StringComparison.Ordinal));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class ThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw error;
        }
    }
}
