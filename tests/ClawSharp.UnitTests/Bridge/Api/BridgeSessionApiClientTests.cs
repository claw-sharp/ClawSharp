using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeSessionApiClientTests
{
    [Fact]
    public async Task CreateBridgeSessionAsync_Returns_Session_Id_And_Sends_Ts_Shaped_Request()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("""{"id":"session_123"}""", Encoding.UTF8, "application/json")
            };
        }));

        var dependencies = CreateDependencies(httpClient) with
        {
            ParseGitRemote = static _ => new ParsedGitRemote("github.com", "owner", "repo"),
            GetDefaultBranchAsync = static () => Task.FromResult<string?>("main")
        };

        var sessionId = await BridgeSessionApiClient.CreateBridgeSessionAsync(
            dependencies,
            "env_123",
            [new BridgeSessionEvent("event", JsonNode.Parse("""{"type":"user","message":"hi"}""")!)],
            "git@github.com:owner/repo.git",
            "",
            "Title",
            "acceptEdits");

        Assert.Equal("session_123", sessionId);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("token", capturedRequest.Headers.Authorization!.Parameter);
        Assert.Equal(BridgeSessionApiClient.AnthropicVersion, capturedRequest.Headers.GetValues("anthropic-version").Single());
        Assert.Equal(BridgeSessionApiClient.AnthropicBeta, capturedRequest.Headers.GetValues("anthropic-beta").Single());
        Assert.Equal("org_123", capturedRequest.Headers.GetValues("x-organization-uuid").Single());
        Assert.NotNull(capturedBody);
        Assert.Contains(@"""title"":""Title""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""environment_id"":""env_123""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""source"":""remote-control""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""permission_mode"":""acceptEdits""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""url"":""https://github.com/owner/repo""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""revision"":""main""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""repo"":""owner/repo""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""branches"":[""claude/task""]", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""model"":""sonnet""", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateBridgeSessionAsync_Falls_Back_To_GitHub_Repository_Parser_And_Returns_Null_On_Request_Failure()
    {
        List<string> debug = [];
        var dependencies = CreateDependencies(new HttpClient(new ThrowingHandler(new HttpRequestException("boom"))), debug.Add) with
        {
            ParseGitRemote = static _ => null,
            ParseGitHubRepository = static _ => "owner/repo"
        };

        var sessionId = await BridgeSessionApiClient.CreateBridgeSessionAsync(
            dependencies,
            "env_123",
            [new BridgeSessionEvent("event", JsonNode.Parse("""{"type":"user"}""")!)],
            "owner/repo",
            "feature");

        Assert.Null(sessionId);
        Assert.Contains(debug, line => line.Contains("Session creation request failed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateBridgeSessionAsync_Returns_Null_When_Access_Token_Or_Org_Is_Missing_Or_Response_Lacks_Id()
    {
        List<string> debug = [];
        var missingToken = CreateDependencies(new HttpClient(new StubHandler(_ => throw new InvalidOperationException("should not execute"))), debug.Add) with
        {
            GetAccessToken = static () => null
        };
        var missingOrg = CreateDependencies(new HttpClient(new StubHandler(_ => throw new InvalidOperationException("should not execute"))), debug.Add) with
        {
            GetOrganizationUuid = static () => null
        };
        var malformed = CreateDependencies(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"title":"bad"}""", Encoding.UTF8, "application/json")
            })), debug.Add);

        Assert.Null(await BridgeSessionApiClient.CreateBridgeSessionAsync(missingToken, "env_123", [], null, ""));
        Assert.Null(await BridgeSessionApiClient.CreateBridgeSessionAsync(missingOrg, "env_123", [], null, ""));
        Assert.Null(await BridgeSessionApiClient.CreateBridgeSessionAsync(malformed, "env_123", [], null, ""));
        Assert.Contains(debug, line => line.Contains("No access token for session creation", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("No org UUID for session creation", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("No session ID in response", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetBridgeSessionAsync_Returns_Summary_And_Logs_Failures()
    {
        List<string> debug = [];
        var okDependencies = CreateDependencies(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"environment_id":"env_123","title":"Hello"}""", Encoding.UTF8, "application/json")
            })), debug.Add);
        var failDependencies = CreateDependencies(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"message":"missing"}""", Encoding.UTF8, "application/json")
            })), debug.Add);

        var summary = await BridgeSessionApiClient.GetBridgeSessionAsync(okDependencies, "session_123");
        var missing = await BridgeSessionApiClient.GetBridgeSessionAsync(failDependencies, "session_404");

        Assert.NotNull(summary);
        Assert.Equal("env_123", summary!.EnvironmentId);
        Assert.Equal("Hello", summary.Title);
        Assert.Null(missing);
        Assert.Contains(debug, line => line.Contains("Fetching session session_123", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("Session fetch failed with status 404: missing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArchiveBridgeSessionAsync_Uses_Post_And_Logs_Success_And_Failure()
    {
        List<string> debug = [];
        HttpRequestMessage? capturedRequest = null;
        var successDependencies = CreateDependencies(new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        })), debug.Add);
        var failureDependencies = CreateDependencies(new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent("""{"message":"already archived"}""", Encoding.UTF8, "application/json")
            })), debug.Add);

        await BridgeSessionApiClient.ArchiveBridgeSessionAsync(successDependencies, "session_123");
        await BridgeSessionApiClient.ArchiveBridgeSessionAsync(failureDependencies, "session_456");

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Contains("/v1/sessions/session_123/archive", capturedRequest.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.Contains(debug, line => line.Contains("Session session_123 archived successfully", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("Session archive failed with status 409: already archived", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateBridgeSessionTitleAsync_Uses_Compat_Id_And_Swallows_Request_Failures()
    {
        List<string> debug = [];
        HttpRequestMessage? capturedRequest = null;
        BridgeSessionIdCompat.SetCseShimGate(static () => true);
        try
        {
            var successDependencies = CreateDependencies(new HttpClient(new StubHandler(request =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            })), debug.Add);
            var failingDependencies = CreateDependencies(new HttpClient(new ThrowingHandler(new HttpRequestException("boom"))), debug.Add);

            await BridgeSessionApiClient.UpdateBridgeSessionTitleAsync(successDependencies, "cse_123", "Renamed");
            await BridgeSessionApiClient.UpdateBridgeSessionTitleAsync(failingDependencies, "session_456", "Renamed");

            Assert.NotNull(capturedRequest);
            Assert.Equal("PATCH", capturedRequest!.Method.Method);
            Assert.Contains("/v1/sessions/session_123", capturedRequest.RequestUri!.ToString(), StringComparison.Ordinal);
            Assert.Contains(debug, line => line.Contains("Updating session title: session_123", StringComparison.Ordinal));
            Assert.Contains(debug, line => line.Contains("Session title updated successfully", StringComparison.Ordinal));
            Assert.Contains(debug, line => line.Contains("Session title update request failed: boom", StringComparison.Ordinal));
        }
        finally
        {
            BridgeSessionIdCompat.ResetCseShimGate();
        }
    }

    private static BridgeSessionApiDependencies CreateDependencies(HttpClient httpClient, Action<string>? onDebug = null)
    {
        return new BridgeSessionApiDependencies(
            httpClient,
            static () => "token",
            static () => "org_123",
            static () => "https://api.example.com",
            static () => "sonnet",
            OnDebug: onDebug);
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
