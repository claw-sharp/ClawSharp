using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeApiClientTests
{
    [Fact]
    public async Task RegisterBridgeEnvironmentAsync_Posts_Ts_Shaped_Body_And_Parses_Response()
    {
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"environment_id":"env_123","environment_secret":"secret_123"}""", Encoding.UTF8, "application/json")
            };
        }));
        var api = BridgeApiClient.Create(
            new BridgeApiClientDependencies(
                BaseUrl: "https://api.example.com",
                GetAccessToken: () => "token",
                RunnerVersion: "1.2.3",
                GetTrustedDeviceToken: () => "device-token"),
            httpClient);

        var response = await api.RegisterBridgeEnvironmentAsync(
            new BridgeConfig(
                Dir: "D:\\repo",
                MachineName: "machine",
                Branch: "main",
                GitRepoUrl: "https://example.com/repo.git",
                MaxSessions: 4,
                SpawnMode: SpawnMode.Worktree,
                Verbose: false,
                Sandbox: false,
                BridgeId: "bridge_123",
                WorkerType: "claude_code",
                EnvironmentId: "ignored",
                ApiBaseUrl: "https://api.example.com",
                SessionIngressUrl: "wss://ingress.example.com",
                ReuseEnvironmentId: "env_existing"));

        Assert.Equal("env_123", response.EnvironmentId);
        Assert.Equal("secret_123", response.EnvironmentSecret);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("device-token", capturedRequest.Headers.GetValues("X-Trusted-Device-Token").Single());
        Assert.Contains(@"""machine_name"":""machine""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""directory"":""D:\\repo""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""git_repo_url"":""https://example.com/repo.git""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""max_sessions"":4", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""worker_type"":""claude_code""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""environment_id"":""env_existing""", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PollForWorkAsync_Returns_Null_For_Empty_Response_And_Parses_Work_Payload()
    {
        var responses = new Queue<HttpResponseMessage>([
            new(HttpStatusCode.OK)
            {
                Content = new StringContent("null", Encoding.UTF8, "application/json")
            },
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"work_123","type":"session","environment_id":"env_123","state":"queued","secret":"secret","created_at":"2026-04-02T00:00:00Z","data":{"type":"session","id":"session_123"}}""",
                    Encoding.UTF8,
                    "application/json")
            }
        ]);
        var debug = new List<string>();
        var api = BridgeApiClient.Create(
            new BridgeApiClientDependencies(
                BaseUrl: "https://api.example.com",
                GetAccessToken: () => "token",
                RunnerVersion: "1.2.3",
                OnDebug: debug.Add),
            new HttpClient(new StubHandler(_ => responses.Dequeue())));

        var empty = await api.PollForWorkAsync("env_123", "secret");
        var work = await api.PollForWorkAsync("env_123", "secret", reclaimOlderThanMs: 2500);

        Assert.Null(empty);
        Assert.NotNull(work);
        Assert.Equal("work_123", work!.Id);
        Assert.Equal("env_123", work.EnvironmentId);
        Assert.Equal(BridgeWorkDataType.Session, work.Data.Type);
        Assert.Equal("session_123", work.Data.Id);
        Assert.Contains(debug, line => line.Contains("no work, 1 consecutive empty polls", StringComparison.Ordinal));
        Assert.Contains(debug, line => line.Contains("workId=work_123", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArchiveSessionAsync_Treats_409_As_Idempotent()
    {
        var debug = new List<string>();
        var api = BridgeApiClient.Create(
            new BridgeApiClientDependencies(
                BaseUrl: "https://api.example.com",
                GetAccessToken: () => "token",
                RunnerVersion: "1.2.3",
                OnDebug: debug.Add),
            new HttpClient(new StubHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("""{"message":"already archived"}""", Encoding.UTF8, "application/json")
                })));

        await api.ArchiveSessionAsync("session_123");

        Assert.Contains(debug, line => line.Contains("409 (already archived)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RegisterBridgeEnvironmentAsync_Retries_Once_After_401_Refresh()
    {
        var currentToken = "stale";
        var seenTokens = new List<string>();
        var api = BridgeApiClient.Create(
            new BridgeApiClientDependencies(
                BaseUrl: "https://api.example.com",
                GetAccessToken: () => currentToken,
                RunnerVersion: "1.2.3",
                OnAuth401: _ =>
                {
                    currentToken = "fresh";
                    return Task.FromResult(true);
                }),
            new HttpClient(new StubHandler(request =>
            {
                seenTokens.Add(request.Headers.Authorization!.Parameter!);
                return seenTokens.Count == 1
                    ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("""{"message":"bad token"}""", Encoding.UTF8, "application/json")
                    }
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"environment_id":"env_123","environment_secret":"secret_123"}""", Encoding.UTF8, "application/json")
                    };
            })));

        var response = await api.RegisterBridgeEnvironmentAsync(
            new BridgeConfig(
                Dir: "D:\\repo",
                MachineName: "machine",
                Branch: "main",
                GitRepoUrl: null,
                MaxSessions: 1,
                SpawnMode: SpawnMode.SingleSession,
                Verbose: false,
                Sandbox: false,
                BridgeId: "bridge_123",
                WorkerType: "claude_code",
                EnvironmentId: "env",
                ApiBaseUrl: "https://api.example.com",
                SessionIngressUrl: "wss://ingress.example.com"));

        Assert.Equal(["stale", "fresh"], seenTokens);
        Assert.Equal("env_123", response.EnvironmentId);
    }

    [Fact]
    public async Task SendPermissionResponseEventAsync_Uses_Ts_Response_Shape()
    {
        string? capturedBody = null;
        var api = BridgeApiClient.Create(
            new BridgeApiClientDependencies(
                BaseUrl: "https://api.example.com",
                GetAccessToken: () => "token",
                RunnerVersion: "1.2.3"),
            new HttpClient(new StubHandler(request =>
            {
                capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}", Encoding.UTF8, "application/json")
                };
            })));

        await api.SendPermissionResponseEventAsync(
            "session_123",
            new PermissionResponseEvent(
                "permission_response",
                new PermissionResponsePayload(
                    "tool_permission",
                    "req_123",
                    new Dictionary<string, object?> { ["decision"] = "allow" })),
            "secret");

        Assert.NotNull(capturedBody);
        Assert.Contains(@"""request_id"":""req_123""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""subtype"":""tool_permission""", capturedBody, StringComparison.Ordinal);
        Assert.Contains(@"""decision"":""allow""", capturedBody, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
