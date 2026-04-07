using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ClawSharp.Bridge;

public sealed record BridgeSessionEvent(
    string Type,
    JsonNode Data);

public sealed record ParsedGitRemote(
    string Host,
    string Owner,
    string Name);

public sealed record BridgeSessionSummary(
    string? EnvironmentId,
    string? Title);

public sealed record BridgeSessionApiDependencies(
    HttpClient HttpClient,
    Func<string?> GetAccessToken,
    Func<string?> GetOrganizationUuid,
    Func<string> GetBaseUrl,
    Func<string> GetMainLoopModel,
    Func<string, ParsedGitRemote?>? ParseGitRemote = null,
    Func<string, string?>? ParseGitHubRepository = null,
    Func<Task<string?>>? GetDefaultBranchAsync = null,
    Action<string>? OnDebug = null);

public static class BridgeSessionApiClient
{
    public const string AnthropicVersion = "2023-06-01";
    public const string AnthropicBeta = "ccr-byoc-2025-07-29";
    private const int DefaultTimeoutMs = 10_000;

    public static async Task<string?> CreateBridgeSessionAsync(
        BridgeSessionApiDependencies dependencies,
        string environmentId,
        IReadOnlyList<BridgeSessionEvent> events,
        string? gitRepoUrl,
        string branch,
        string? title = null,
        string? permissionMode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(environmentId);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(branch);

        var accessToken = dependencies.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            dependencies.OnDebug?.Invoke("[bridge] No access token for session creation");
            return null;
        }

        var organizationUuid = dependencies.GetOrganizationUuid();
        if (string.IsNullOrEmpty(organizationUuid))
        {
            dependencies.OnDebug?.Invoke("[bridge] No org UUID for session creation");
            return null;
        }

        var sessionContext = await BuildSessionContextAsync(dependencies, gitRepoUrl, branch);
        var requestBody = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["events"] = events.Select(static item => new Dictionary<string, object?>
            {
                ["type"] = item.Type,
                ["data"] = item.Data
            }).ToArray(),
            ["session_context"] = sessionContext,
            ["environment_id"] = environmentId,
            ["source"] = "remote-control",
            ["permission_mode"] = permissionMode
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{dependencies.GetBaseUrl()}/v1/sessions")
        {
            Content = CreateJsonContent(requestBody)
        };
        ApplyOauthHeaders(request, accessToken, organizationUuid);

        HttpResponseMessage response;
        try
        {
            response = await dependencies.HttpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception error)
        {
            dependencies.OnDebug?.Invoke($"[bridge] Session creation request failed: {error.Message}");
            return null;
        }

        using (response)
        {
            var body = await ReadJsonSafelyAsync(response, cancellationToken);
            var isSuccess = response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created;
            if (!isSuccess)
            {
                var detail = BridgeDebugUtilities.ExtractErrorDetail(body);
                dependencies.OnDebug?.Invoke(
                    $"[bridge] Session creation failed with status {(int)response.StatusCode}{(detail is null ? string.Empty : $": {detail}")}");
                return null;
            }

            var sessionId = body?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(sessionId))
            {
                dependencies.OnDebug?.Invoke("[bridge] No session ID in response");
                return null;
            }

            return sessionId;
        }
    }

    public static async Task<BridgeSessionSummary?> GetBridgeSessionAsync(
        BridgeSessionApiDependencies dependencies,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(sessionId);

        var accessToken = dependencies.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            dependencies.OnDebug?.Invoke("[bridge] No access token for session fetch");
            return null;
        }

        var organizationUuid = dependencies.GetOrganizationUuid();
        if (string.IsNullOrEmpty(organizationUuid))
        {
            dependencies.OnDebug?.Invoke("[bridge] No org UUID for session fetch");
            return null;
        }

        dependencies.OnDebug?.Invoke($"[bridge] Fetching session {sessionId}");
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(DefaultTimeoutMs);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{dependencies.GetBaseUrl()}/v1/sessions/{sessionId}");
        ApplyOauthHeaders(request, accessToken, organizationUuid);

        HttpResponseMessage response;
        try
        {
            response = await dependencies.HttpClient.SendAsync(request, timeoutCancellation.Token);
        }
        catch (Exception error)
        {
            dependencies.OnDebug?.Invoke($"[bridge] Session fetch request failed: {error.Message}");
            return null;
        }

        using (response)
        {
            var body = await ReadJsonSafelyAsync(response, timeoutCancellation.Token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                var detail = BridgeDebugUtilities.ExtractErrorDetail(body);
                dependencies.OnDebug?.Invoke(
                    $"[bridge] Session fetch failed with status {(int)response.StatusCode}{(detail is null ? string.Empty : $": {detail}")}");
                return null;
            }

            return new BridgeSessionSummary(
                body?["environment_id"]?.GetValue<string>(),
                body?["title"]?.GetValue<string>());
        }
    }

    public static async Task ArchiveBridgeSessionAsync(
        BridgeSessionApiDependencies dependencies,
        string sessionId,
        int timeoutMs = DefaultTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(sessionId);

        var accessToken = dependencies.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            dependencies.OnDebug?.Invoke("[bridge] No access token for session archive");
            return;
        }

        var organizationUuid = dependencies.GetOrganizationUuid();
        if (string.IsNullOrEmpty(organizationUuid))
        {
            dependencies.OnDebug?.Invoke("[bridge] No org UUID for session archive");
            return;
        }

        dependencies.OnDebug?.Invoke($"[bridge] Archiving session {sessionId}");
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeoutMs);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{dependencies.GetBaseUrl()}/v1/sessions/{sessionId}/archive")
        {
            Content = CreateJsonContent(new Dictionary<string, object?>())
        };
        ApplyOauthHeaders(request, accessToken, organizationUuid);

        using var response = await dependencies.HttpClient.SendAsync(request, timeoutCancellation.Token);
        var body = await ReadJsonSafelyAsync(response, timeoutCancellation.Token);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            dependencies.OnDebug?.Invoke($"[bridge] Session {sessionId} archived successfully");
            return;
        }

        var detail = BridgeDebugUtilities.ExtractErrorDetail(body);
        dependencies.OnDebug?.Invoke(
            $"[bridge] Session archive failed with status {(int)response.StatusCode}{(detail is null ? string.Empty : $": {detail}")}");
    }

    public static async Task UpdateBridgeSessionTitleAsync(
        BridgeSessionApiDependencies dependencies,
        string sessionId,
        string title,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(title);

        var accessToken = dependencies.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            dependencies.OnDebug?.Invoke("[bridge] No access token for session title update");
            return;
        }

        var organizationUuid = dependencies.GetOrganizationUuid();
        if (string.IsNullOrEmpty(organizationUuid))
        {
            dependencies.OnDebug?.Invoke("[bridge] No org UUID for session title update");
            return;
        }

        var compatId = BridgeSessionIdCompat.ToCompatSessionId(sessionId);
        dependencies.OnDebug?.Invoke($"[bridge] Updating session title: {compatId} → {title}");
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(DefaultTimeoutMs);

        using var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"{dependencies.GetBaseUrl()}/v1/sessions/{compatId}")
        {
            Content = CreateJsonContent(new Dictionary<string, object?> { ["title"] = title })
        };
        ApplyOauthHeaders(request, accessToken, organizationUuid);

        try
        {
            using var response = await dependencies.HttpClient.SendAsync(request, timeoutCancellation.Token);
            var body = await ReadJsonSafelyAsync(response, timeoutCancellation.Token);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                dependencies.OnDebug?.Invoke("[bridge] Session title updated successfully");
                return;
            }

            var detail = BridgeDebugUtilities.ExtractErrorDetail(body);
            dependencies.OnDebug?.Invoke(
                $"[bridge] Session title update failed with status {(int)response.StatusCode}{(detail is null ? string.Empty : $": {detail}")}");
        }
        catch (Exception error)
        {
            dependencies.OnDebug?.Invoke($"[bridge] Session title update request failed: {error.Message}");
        }
    }

    private static async Task<Dictionary<string, object?>> BuildSessionContextAsync(
        BridgeSessionApiDependencies dependencies,
        string? gitRepoUrl,
        string branch)
    {
        Dictionary<string, object?>? gitSource = null;
        Dictionary<string, object?>? gitOutcome = null;

        if (!string.IsNullOrEmpty(gitRepoUrl))
        {
            var parsed = dependencies.ParseGitRemote?.Invoke(gitRepoUrl);
            if (parsed is not null)
            {
                var revision = await GetRevisionAsync(dependencies, branch);
                gitSource = new Dictionary<string, object?>
                {
                    ["type"] = "git_repository",
                    ["url"] = $"https://{parsed.Host}/{parsed.Owner}/{parsed.Name}",
                    ["revision"] = revision
                };
                gitOutcome = new Dictionary<string, object?>
                {
                    ["type"] = "git_repository",
                    ["git_info"] = new Dictionary<string, object?>
                    {
                        ["type"] = "github",
                        ["repo"] = $"{parsed.Owner}/{parsed.Name}",
                        ["branches"] = new[] { $"claude/{GetBranchOrTask(branch)}" }
                    }
                };
            }
            else
            {
                var ownerRepo = dependencies.ParseGitHubRepository?.Invoke(gitRepoUrl);
                if (!string.IsNullOrEmpty(ownerRepo))
                {
                    var parts = ownerRepo.Split('/');
                    if (parts.Length >= 2 &&
                        !string.IsNullOrEmpty(parts[0]) &&
                        !string.IsNullOrEmpty(parts[1]))
                    {
                        var revision = await GetRevisionAsync(dependencies, branch);
                        gitSource = new Dictionary<string, object?>
                        {
                            ["type"] = "git_repository",
                            ["url"] = $"https://github.com/{parts[0]}/{parts[1]}",
                            ["revision"] = revision
                        };
                        gitOutcome = new Dictionary<string, object?>
                        {
                            ["type"] = "git_repository",
                            ["git_info"] = new Dictionary<string, object?>
                            {
                                ["type"] = "github",
                                ["repo"] = $"{parts[0]}/{parts[1]}",
                                ["branches"] = new[] { $"claude/{GetBranchOrTask(branch)}" }
                            }
                        };
                    }
                }
            }
        }

        return new Dictionary<string, object?>
        {
            ["sources"] = gitSource is null ? Array.Empty<object>() : new object[] { gitSource },
            ["outcomes"] = gitOutcome is null ? Array.Empty<object>() : new object[] { gitOutcome },
            ["model"] = dependencies.GetMainLoopModel()
        };
    }

    private static async Task<string?> GetRevisionAsync(BridgeSessionApiDependencies dependencies, string branch)
    {
        if (!string.IsNullOrEmpty(branch))
        {
            return branch;
        }

        if (dependencies.GetDefaultBranchAsync is null)
        {
            return null;
        }

        return await dependencies.GetDefaultBranchAsync();
    }

    private static string GetBranchOrTask(string branch)
    {
        return string.IsNullOrEmpty(branch) ? "task" : branch;
    }

    private static void ApplyOauthHeaders(HttpRequestMessage request, string accessToken, string organizationUuid)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        request.Headers.TryAddWithoutValidation("anthropic-beta", AnthropicBeta);
        request.Headers.TryAddWithoutValidation("x-organization-uuid", organizationUuid);
        request.Content ??= CreateJsonContent(new Dictionary<string, object?>());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    }

    private static StringContent CreateJsonContent(object body)
    {
        return new StringContent(
            JsonSerializer.Serialize(
                body,
                new JsonSerializerOptions
                {
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }),
            Encoding.UTF8,
            "application/json");
    }

    private static async Task<JsonNode?> ReadJsonSafelyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(text);
        }
        catch
        {
            return null;
        }
    }
}
