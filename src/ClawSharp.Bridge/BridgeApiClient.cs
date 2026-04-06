// TS origin: ./bridge/bridgeApi.ts
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public static class BridgeApiClient
{
    public static IBridgeApiClient Create(BridgeApiClientDependencies dependencies, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(httpClient);

        return new HttpBridgeApiClient(dependencies, httpClient);
    }

    private sealed class HttpBridgeApiClient : IBridgeApiClient
    {
        private readonly BridgeApiClientDependencies _dependencies;
        private readonly HttpClient _httpClient;
        private int _consecutiveEmptyPolls;
        private const int EmptyPollLogInterval = 100;

        public HttpBridgeApiClient(BridgeApiClientDependencies dependencies, HttpClient httpClient)
        {
            _dependencies = dependencies;
            _httpClient = httpClient;
        }

        public async Task<BridgeRegisterEnvironmentResponse> RegisterBridgeEnvironmentAsync(
            BridgeConfig config,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);

            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] POST /v1/environments/bridge bridgeId={config.BridgeId}");

            var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
                _dependencies,
                async accessToken =>
                {
                    using var request = CreateRequest(
                        HttpMethod.Post,
                        $"{_dependencies.BaseUrl}/v1/environments/bridge",
                        accessToken,
                        new Dictionary<string, object?>
                        {
                            ["machine_name"] = config.MachineName,
                            ["directory"] = config.Dir,
                            ["branch"] = config.Branch,
                            ["git_repo_url"] = config.GitRepoUrl,
                            ["max_sessions"] = config.MaxSessions,
                            ["metadata"] = new Dictionary<string, object?>
                            {
                                ["worker_type"] = config.WorkerType
                            },
                            ["environment_id"] = config.ReuseEnvironmentId
                        });

                    return await SendJsonAsync(request, cancellationToken);
                },
                "Registration");

            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "Registration");
            var environmentId = response.Data?["environment_id"]?.GetValue<string>();
            var environmentSecret = response.Data?["environment_secret"]?.GetValue<string>();
            if (string.IsNullOrEmpty(environmentId) || string.IsNullOrEmpty(environmentSecret))
            {
                throw new InvalidOperationException("Registration: Response missing environment_id or environment_secret");
            }

            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] POST /v1/environments/bridge -> {response.Status} environment_id={environmentId}");
            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] >>> {BridgeDebugUtilities.DebugBody(new Dictionary<string, object?>
                {
                    ["machine_name"] = config.MachineName,
                    ["directory"] = config.Dir,
                    ["branch"] = config.Branch,
                    ["git_repo_url"] = config.GitRepoUrl,
                    ["max_sessions"] = config.MaxSessions,
                    ["metadata"] = new Dictionary<string, object?> { ["worker_type"] = config.WorkerType }
                })}");
            _dependencies.OnDebug?.Invoke($"[bridge:api] <<< {BridgeDebugUtilities.DebugBody(response.Data)}");

            return new BridgeRegisterEnvironmentResponse(environmentId, environmentSecret);
        }

        public async Task<BridgeWorkResponse?> PollForWorkAsync(
            string environmentId,
            string environmentSecret,
            CancellationToken cancellationToken = default,
            int? reclaimOlderThanMs = null)
        {
            BridgeApiErrorUtilities.ValidateBridgeId(environmentId, "environmentId");
            ArgumentNullException.ThrowIfNull(environmentSecret);

            var previousEmptyPolls = _consecutiveEmptyPolls;
            _consecutiveEmptyPolls = 0;

            var url = reclaimOlderThanMs is null
                ? $"{_dependencies.BaseUrl}/v1/environments/{environmentId}/work/poll"
                : $"{_dependencies.BaseUrl}/v1/environments/{environmentId}/work/poll?reclaim_older_than_ms={reclaimOlderThanMs.Value}";
            using var request = CreateRequest(HttpMethod.Get, url, environmentSecret, body: null);
            var response = await SendJsonAsync(request, cancellationToken, timeoutMs: 10_000);
            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "Poll");

            if (response.Data is null || response.Data.GetValueKind() == JsonValueKind.Null)
            {
                _consecutiveEmptyPolls = previousEmptyPolls + 1;
                if (_consecutiveEmptyPolls == 1 || _consecutiveEmptyPolls % EmptyPollLogInterval == 0)
                {
                    _dependencies.OnDebug?.Invoke(
                        $"[bridge:api] GET .../work/poll -> {response.Status} (no work, {_consecutiveEmptyPolls} consecutive empty polls)");
                }

                return null;
            }

            var work = ParseWorkResponse(response.Data);
            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] GET .../work/poll -> {response.Status} workId={work.Id} type={work.Data.Type.ToString().ToLowerInvariant()}{(string.IsNullOrEmpty(work.Data.Id) ? string.Empty : $" sessionId={work.Data.Id}")}");
            _dependencies.OnDebug?.Invoke($"[bridge:api] <<< {BridgeDebugUtilities.DebugBody(response.Data)}");
            return work;
        }

        public async Task AcknowledgeWorkAsync(
            string environmentId,
            string workId,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            BridgeApiErrorUtilities.ValidateBridgeId(environmentId, "environmentId");
            BridgeApiErrorUtilities.ValidateBridgeId(workId, "workId");

            _dependencies.OnDebug?.Invoke($"[bridge:api] POST .../work/{workId}/ack");
            using var request = CreateRequest(
                HttpMethod.Post,
                $"{_dependencies.BaseUrl}/v1/environments/{environmentId}/work/{workId}/ack",
                sessionToken,
                new Dictionary<string, object?>());
            var response = await SendJsonAsync(request, cancellationToken, timeoutMs: 10_000);
            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "Acknowledge");
            _dependencies.OnDebug?.Invoke($"[bridge:api] POST .../work/{workId}/ack -> {response.Status}");
        }

        public async Task StopWorkAsync(
            string environmentId,
            string workId,
            bool force,
            CancellationToken cancellationToken = default)
        {
            BridgeApiErrorUtilities.ValidateBridgeId(environmentId, "environmentId");
            BridgeApiErrorUtilities.ValidateBridgeId(workId, "workId");

            _dependencies.OnDebug?.Invoke($"[bridge:api] POST .../work/{workId}/stop force={force.ToString().ToLowerInvariant()}");
            var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
                _dependencies,
                async accessToken =>
                {
                    using var request = CreateRequest(
                        HttpMethod.Post,
                        $"{_dependencies.BaseUrl}/v1/environments/{environmentId}/work/{workId}/stop",
                        accessToken,
                        new Dictionary<string, object?> { ["force"] = force });
                    return await SendJsonAsync(request, cancellationToken, timeoutMs: 10_000);
                },
                "StopWork");

            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "StopWork");
            _dependencies.OnDebug?.Invoke($"[bridge:api] POST .../work/{workId}/stop -> {response.Status}");
        }

        public async Task DeregisterEnvironmentAsync(
            string environmentId,
            CancellationToken cancellationToken = default)
        {
            BridgeApiErrorUtilities.ValidateBridgeId(environmentId, "environmentId");

            _dependencies.OnDebug?.Invoke($"[bridge:api] DELETE /v1/environments/bridge/{environmentId}");
            var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
                _dependencies,
                async accessToken =>
                {
                    using var request = CreateRequest(
                        HttpMethod.Delete,
                        $"{_dependencies.BaseUrl}/v1/environments/bridge/{environmentId}",
                        accessToken,
                        body: null);
                    return await SendJsonAsync(request, cancellationToken, timeoutMs: 10_000);
                },
                "Deregister");

            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "Deregister");
            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] DELETE /v1/environments/bridge/{environmentId} -> {response.Status}");
        }

        public async Task SendPermissionResponseEventAsync(
            string sessionId,
            PermissionResponseEvent @event,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            BridgeApiErrorUtilities.ValidateBridgeId(sessionId, "sessionId");
            ArgumentNullException.ThrowIfNull(@event);
            ArgumentNullException.ThrowIfNull(sessionToken);

            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] POST /v1/sessions/{sessionId}/events type={@event.Type}");
            var requestBody = new Dictionary<string, object?>
            {
                ["events"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["type"] = @event.Type,
                        ["response"] = new Dictionary<string, object?>
                        {
                            ["subtype"] = @event.Response.Subtype,
                            ["request_id"] = @event.Response.RequestId,
                            ["response"] = @event.Response.Response
                        }
                    }
                }
            };

            using var request = CreateRequest(
                HttpMethod.Post,
                $"{_dependencies.BaseUrl}/v1/sessions/{sessionId}/events",
                sessionToken,
                requestBody);
            var response = await SendJsonAsync(request, cancellationToken, timeoutMs: 10_000);
            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "SendPermissionResponseEvent");
            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] POST /v1/sessions/{sessionId}/events -> {response.Status}");
            _dependencies.OnDebug?.Invoke($"[bridge:api] >>> {BridgeDebugUtilities.DebugBody(requestBody)}");
            _dependencies.OnDebug?.Invoke($"[bridge:api] <<< {BridgeDebugUtilities.DebugBody(response.Data)}");
        }

        public async Task ArchiveSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            BridgeApiErrorUtilities.ValidateBridgeId(sessionId, "sessionId");

            _dependencies.OnDebug?.Invoke($"[bridge:api] POST /v1/sessions/{sessionId}/archive");
            var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
                _dependencies,
                async accessToken =>
                {
                    using var request = CreateRequest(
                        HttpMethod.Post,
                        $"{_dependencies.BaseUrl}/v1/sessions/{sessionId}/archive",
                        accessToken,
                        new Dictionary<string, object?>());
                    return await SendJsonAsync(request, cancellationToken, timeoutMs: 10_000);
                },
                "ArchiveSession");

            if (response.Status == 409)
            {
                _dependencies.OnDebug?.Invoke(
                    $"[bridge:api] POST /v1/sessions/{sessionId}/archive -> 409 (already archived)");
                return;
            }

            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "ArchiveSession");
            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] POST /v1/sessions/{sessionId}/archive -> {response.Status}");
        }

        public async Task ReconnectSessionAsync(
            string environmentId,
            string sessionId,
            CancellationToken cancellationToken = default)
        {
            BridgeApiErrorUtilities.ValidateBridgeId(environmentId, "environmentId");
            BridgeApiErrorUtilities.ValidateBridgeId(sessionId, "sessionId");

            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] POST /v1/environments/{environmentId}/bridge/reconnect session_id={sessionId}");
            var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
                _dependencies,
                async accessToken =>
                {
                    using var request = CreateRequest(
                        HttpMethod.Post,
                        $"{_dependencies.BaseUrl}/v1/environments/{environmentId}/bridge/reconnect",
                        accessToken,
                        new Dictionary<string, object?> { ["session_id"] = sessionId });
                    return await SendJsonAsync(request, cancellationToken, timeoutMs: 10_000);
                },
                "ReconnectSession");

            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "ReconnectSession");
            _dependencies.OnDebug?.Invoke("[bridge:api] POST .../bridge/reconnect -> " + response.Status);
        }

        public async Task<BridgeHeartbeatResponse> HeartbeatWorkAsync(
            string environmentId,
            string workId,
            string sessionToken,
            CancellationToken cancellationToken = default)
        {
            BridgeApiErrorUtilities.ValidateBridgeId(environmentId, "environmentId");
            BridgeApiErrorUtilities.ValidateBridgeId(workId, "workId");

            _dependencies.OnDebug?.Invoke($"[bridge:api] POST .../work/{workId}/heartbeat");
            using var request = CreateRequest(
                HttpMethod.Post,
                $"{_dependencies.BaseUrl}/v1/environments/{environmentId}/work/{workId}/heartbeat",
                sessionToken,
                new Dictionary<string, object?>());
            var response = await SendJsonAsync(request, cancellationToken, timeoutMs: 10_000);
            BridgeApiErrorUtilities.HandleErrorStatus(response.Status, response.Data, "Heartbeat");

            var leaseExtended = response.Data?["lease_extended"]?.GetValue<bool?>() ?? false;
            var state = response.Data?["state"]?.GetValue<string>();
            if (string.IsNullOrEmpty(state))
            {
                throw new InvalidOperationException("Heartbeat: Response missing state");
            }

            _dependencies.OnDebug?.Invoke(
                $"[bridge:api] POST .../work/{workId}/heartbeat -> {response.Status} lease_extended={leaseExtended.ToString().ToLowerInvariant()} state={state}");
            return new BridgeHeartbeatResponse(leaseExtended, state);
        }

        private HttpRequestMessage CreateRequest(
            HttpMethod method,
            string url,
            string accessToken,
            object? body)
        {
            var request = new HttpRequestMessage(method, url);
            var headers = BridgeApiRequestUtilities.GetHeaders(_dependencies, accessToken);
            request.Headers.Authorization = AuthenticationHeaderValue.Parse(headers["Authorization"]);
            request.Headers.TryAddWithoutValidation("anthropic-version", headers["anthropic-version"]);
            request.Headers.TryAddWithoutValidation("anthropic-beta", headers["anthropic-beta"]);
            request.Headers.TryAddWithoutValidation("x-environment-runner-version", headers["x-environment-runner-version"]);
            if (headers.TryGetValue("X-Trusted-Device-Token", out var trustedDeviceToken))
            {
                request.Headers.TryAddWithoutValidation("X-Trusted-Device-Token", trustedDeviceToken);
            }

            if (body is not null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
                    }),
                    Encoding.UTF8,
                    "application/json");
            }

            return request;
        }

        private async Task<BridgeHttpLikeResponse<JsonNode?>> SendJsonAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken,
            int timeoutMs = 15_000)
        {
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedCancellation.CancelAfter(timeoutMs);
            using var response = await _httpClient.SendAsync(request, linkedCancellation.Token);
            var data = await ReadJsonSafelyAsync(response, linkedCancellation.Token);
            return new BridgeHttpLikeResponse<JsonNode?>((int)response.StatusCode, data);
        }

        private static async Task<JsonNode?> ReadJsonSafelyAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            if (response.Content is null)
            {
                return null;
            }

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

        private static BridgeWorkResponse ParseWorkResponse(JsonNode? node)
        {
            if (node is not JsonObject obj)
            {
                throw new InvalidOperationException("Poll: Response missing work payload");
            }

            var id = obj["id"]?.GetValue<string>();
            var type = obj["type"]?.GetValue<string>();
            var environmentId = obj["environment_id"]?.GetValue<string>();
            var state = obj["state"]?.GetValue<string>();
            var secret = obj["secret"]?.GetValue<string>();
            var createdAt = obj["created_at"]?.GetValue<string>();
            var dataObject = obj["data"] as JsonObject;
            var dataType = dataObject?["type"]?.GetValue<string>();
            var dataId = dataObject?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(id) ||
                string.IsNullOrEmpty(type) ||
                string.IsNullOrEmpty(environmentId) ||
                string.IsNullOrEmpty(state) ||
                string.IsNullOrEmpty(secret) ||
                string.IsNullOrEmpty(createdAt) ||
                string.IsNullOrEmpty(dataType))
            {
                throw new InvalidOperationException("Poll: Response missing required work fields");
            }

            var parsedDataType = dataType switch
            {
                "session" => BridgeWorkDataType.Session,
                "healthcheck" => BridgeWorkDataType.Healthcheck,
                _ => throw new InvalidOperationException($"Poll: Unknown work data type {dataType}")
            };

            return new BridgeWorkResponse(
                id,
                type,
                environmentId,
                state,
                new BridgeWorkData(parsedDataType, dataId ?? string.Empty),
                secret,
                createdAt);
        }
    }
}
