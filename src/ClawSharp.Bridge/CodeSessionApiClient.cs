using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public sealed record RemoteCredentials(
    string WorkerJwt,
    string ApiBaseUrl,
    double ExpiresIn,
    long WorkerEpoch);

public sealed record CodeSessionApiDependencies(
    HttpClient HttpClient,
    Action<string>? OnDebug = null);

public static class CodeSessionApiClient
{
    public const string AnthropicVersion = "2023-06-01";

    public static async Task<string?> CreateCodeSessionAsync(
        CodeSessionApiDependencies dependencies,
        string baseUrl,
        string accessToken,
        string title,
        int timeoutMs,
        IReadOnlyList<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(accessToken);
        ArgumentNullException.ThrowIfNull(title);

        var url = $"{baseUrl}/v1/code/sessions";
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(timeoutMs);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = CreateJsonContent(
                new Dictionary<string, object?>
                {
                    ["title"] = title,
                    ["bridge"] = new Dictionary<string, object?>(),
                    ["tags"] = tags is { Count: > 0 } ? tags : null
                })
        };
        ApplyOauthHeaders(request, accessToken);

        HttpResponseMessage response;
        try
        {
            response = await dependencies.HttpClient.SendAsync(request, linkedCancellation.Token);
        }
        catch (Exception error)
        {
            dependencies.OnDebug?.Invoke($"[code-session] Session create request failed: {error.Message}");
            return null;
        }

        using (response)
        {
            var body = await ReadJsonSafelyAsync(response, linkedCancellation.Token);
            if (response.StatusCode is not System.Net.HttpStatusCode.OK and not System.Net.HttpStatusCode.Created)
            {
                var detail = BridgeDebugUtilities.ExtractErrorDetail(body);
                dependencies.OnDebug?.Invoke(
                    $"[code-session] Session create failed {(int)response.StatusCode}{(detail is null ? string.Empty : $": {detail}")}");
                return null;
            }

            var sessionId = body?["session"]?["id"]?.GetValue<string>();
            if (string.IsNullOrEmpty(sessionId) || !sessionId.StartsWith("cse_", StringComparison.Ordinal))
            {
                dependencies.OnDebug?.Invoke(
                    $"[code-session] No session.id (cse_*) in response: {Truncate(body?.ToJsonString() ?? "null", 200)}");
                return null;
            }

            return sessionId;
        }
    }

    public static async Task<RemoteCredentials?> FetchRemoteCredentialsAsync(
        CodeSessionApiDependencies dependencies,
        string sessionId,
        string baseUrl,
        string accessToken,
        int timeoutMs,
        string? trustedDeviceToken = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(baseUrl);
        ArgumentNullException.ThrowIfNull(accessToken);

        var url = $"{baseUrl}/v1/code/sessions/{sessionId}/bridge";
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCancellation.CancelAfter(timeoutMs);

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = CreateJsonContent(new Dictionary<string, object?>())
        };
        ApplyOauthHeaders(request, accessToken);
        if (!string.IsNullOrEmpty(trustedDeviceToken))
        {
            request.Headers.TryAddWithoutValidation("X-Trusted-Device-Token", trustedDeviceToken);
        }

        HttpResponseMessage response;
        try
        {
            response = await dependencies.HttpClient.SendAsync(request, linkedCancellation.Token);
        }
        catch (Exception error)
        {
            dependencies.OnDebug?.Invoke($"[code-session] /bridge request failed: {error.Message}");
            return null;
        }

        using (response)
        {
            var body = await ReadJsonSafelyAsync(response, linkedCancellation.Token);
            if (response.StatusCode != System.Net.HttpStatusCode.OK)
            {
                var detail = BridgeDebugUtilities.ExtractErrorDetail(body);
                dependencies.OnDebug?.Invoke(
                    $"[code-session] /bridge failed {(int)response.StatusCode}{(detail is null ? string.Empty : $": {detail}")}");
                return null;
            }

            var workerJwt = body?["worker_jwt"]?.GetValue<string>();
            var expiresIn = body?["expires_in"]?.GetValue<double?>();
            var apiBaseUrl = body?["api_base_url"]?.GetValue<string>();
            var rawEpoch = body?["worker_epoch"];
            if (string.IsNullOrEmpty(workerJwt) ||
                expiresIn is null ||
                string.IsNullOrEmpty(apiBaseUrl) ||
                rawEpoch is null)
            {
                dependencies.OnDebug?.Invoke(
                    $"[code-session] /bridge response malformed (need worker_jwt, expires_in, api_base_url, worker_epoch): {Truncate(body?.ToJsonString() ?? "null", 200)}");
                return null;
            }

            var epoch = ParseWorkerEpoch(rawEpoch);
            if (epoch is null)
            {
                dependencies.OnDebug?.Invoke(
                    $"[code-session] /bridge worker_epoch invalid: {rawEpoch.ToJsonString()}");
                return null;
            }

            return new RemoteCredentials(workerJwt, apiBaseUrl, expiresIn.Value, epoch.Value);
        }
    }

    private static void ApplyOauthHeaders(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);
        request.Content ??= CreateJsonContent(new Dictionary<string, object?>());
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
    }

    private static StringContent CreateJsonContent(object body)
    {
        return new StringContent(
            JsonSerializer.Serialize(body, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }),
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

    private static long? ParseWorkerEpoch(JsonNode rawEpoch)
    {
        if (rawEpoch is JsonValue value)
        {
            if (value.TryGetValue<long>(out var longEpoch))
            {
                return longEpoch;
            }

            if (value.TryGetValue<string>(out var stringEpoch) &&
                long.TryParse(stringEpoch, out var parsedStringEpoch))
            {
                return parsedStringEpoch;
            }

            if (value.TryGetValue<double>(out var doubleEpoch) &&
                double.IsFinite(doubleEpoch) &&
                doubleEpoch == Math.Truncate(doubleEpoch) &&
                doubleEpoch <= long.MaxValue &&
                doubleEpoch >= long.MinValue)
            {
                return (long)doubleEpoch;
            }
        }

        return null;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
