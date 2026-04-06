// TS origin: ./bridge/workSecret.ts
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ClawSharp.Bridge;

public static partial class BridgeWorkSecretUtilities
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static BridgeWorkSecret DecodeWorkSecret(string secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        var json = DecodeBase64Url(secret);
        var parsed = JsonNode.Parse(json) as JsonObject;
        var versionNode = parsed?["version"];
        var version = versionNode?.GetValue<int?>();
        if (parsed is null || version != 1)
        {
            var versionValue = versionNode is null ? "unknown" : versionNode.ToJsonString();
            throw new InvalidOperationException($"Unsupported work secret version: {versionValue}");
        }

        var sessionIngressToken = parsed["session_ingress_token"]?.GetValue<string>();
        if (string.IsNullOrEmpty(sessionIngressToken))
        {
            throw new InvalidOperationException("Invalid work secret: missing or empty session_ingress_token");
        }

        var apiBaseUrl = parsed["api_base_url"]?.GetValue<string>();
        if (apiBaseUrl is null)
        {
            throw new InvalidOperationException("Invalid work secret: missing api_base_url");
        }

        var dto = parsed.Deserialize<WorkSecretDto>(SerializerOptions)
                  ?? throw new InvalidOperationException("Invalid work secret: failed to parse work secret payload");

        return new BridgeWorkSecret(
            dto.Version,
            sessionIngressToken,
            apiBaseUrl,
            dto.Sources.Select(source => new BridgeWorkSecretSource(
                source.Type,
                source.GitInfo is null
                    ? null
                    : new BridgeWorkSecretSourceGitInfo(
                        source.GitInfo.Type,
                        source.GitInfo.Repo,
                        source.GitInfo.Ref,
                        source.GitInfo.Token))).ToArray(),
            dto.Auth.Select(auth => new BridgeWorkSecretAuth(auth.Type, auth.Token)).ToArray(),
            dto.ClaudeCodeArgs,
            dto.McpConfig,
            dto.EnvironmentVariables,
            dto.UseCodeSessions);
    }

    public static string BuildSdkUrl(string apiBaseUrl, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUrl);
        ArgumentNullException.ThrowIfNull(sessionId);

        var isLocalhost = apiBaseUrl.Contains("localhost", StringComparison.Ordinal) ||
                          apiBaseUrl.Contains("127.0.0.1", StringComparison.Ordinal);
        var protocol = isLocalhost ? "ws" : "wss";
        var version = isLocalhost ? "v2" : "v1";
        var host = TrailingSlashRegex().Replace(HttpProtocolRegex().Replace(apiBaseUrl, string.Empty), string.Empty);
        return $"{protocol}://{host}/{version}/session_ingress/ws/{sessionId}";
    }

    public static bool SameSessionId(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        if (a == b)
        {
            return true;
        }

        var aBody = a[(a.LastIndexOf('_') + 1)..];
        var bBody = b[(b.LastIndexOf('_') + 1)..];
        return aBody.Length >= 4 && aBody == bBody;
    }

    public static string BuildCcrV2SdkUrl(string apiBaseUrl, string sessionId)
    {
        ArgumentNullException.ThrowIfNull(apiBaseUrl);
        ArgumentNullException.ThrowIfNull(sessionId);

        var baseUrl = TrailingSlashRegex().Replace(apiBaseUrl, string.Empty);
        return $"{baseUrl}/v1/code/sessions/{sessionId}";
    }

    private static string DecodeBase64Url(string value)
    {
        var padded = value
            .Replace('-', '+')
            .Replace('_', '/');

        var paddingLength = 4 - (padded.Length % 4);
        if (paddingLength is > 0 and < 4)
        {
            padded = padded.PadRight(padded.Length + paddingLength, '=');
        }

        var bytes = Convert.FromBase64String(padded);
        return Encoding.UTF8.GetString(bytes);
    }

    [GeneratedRegex("^https?://", RegexOptions.CultureInvariant)]
    private static partial Regex HttpProtocolRegex();

    [GeneratedRegex("/+$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingSlashRegex();

    private sealed record WorkSecretDto(
        [property: JsonPropertyName("version")] int Version,
        [property: JsonPropertyName("session_ingress_token")] string SessionIngressToken,
        [property: JsonPropertyName("api_base_url")] string ApiBaseUrl,
        [property: JsonPropertyName("sources")] IReadOnlyList<WorkSecretSourceDto> Sources,
        [property: JsonPropertyName("auth")] IReadOnlyList<WorkSecretAuthDto> Auth,
        [property: JsonPropertyName("claude_code_args")] IReadOnlyDictionary<string, string>? ClaudeCodeArgs = null,
        [property: JsonPropertyName("mcp_config")] object? McpConfig = null,
        [property: JsonPropertyName("environment_variables")] IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
        [property: JsonPropertyName("use_code_sessions")] bool? UseCodeSessions = null);

    private sealed record WorkSecretSourceDto(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("gitInfo")] WorkSecretSourceGitInfoDto? GitInfo = null);

    private sealed record WorkSecretSourceGitInfoDto(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("repo")] string Repo,
        [property: JsonPropertyName("ref")] string? Ref = null,
        [property: JsonPropertyName("token")] string? Token = null);

    private sealed record WorkSecretAuthDto(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("token")] string Token);
}
