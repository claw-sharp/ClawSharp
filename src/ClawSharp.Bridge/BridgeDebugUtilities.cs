// TS origin: ./bridge/debugUtils.ts
using ClawSharp.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ClawSharp.Bridge;

public static class BridgeDebugUtilities
{
    private const int DebugMessageLimit = 2000;
    private const int RedactMinLength = 16;
    private static readonly string[] SecretFieldNames =
    [
        "session_ingress_token",
        "environment_secret",
        "access_token",
        "secret",
        "token"
    ];
    private static readonly Regex SecretPattern = new(
        $"\"({string.Join('|', SecretFieldNames.Select(Regex.Escape))})\"\\s*:\\s*\"([^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string RedactSecrets(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return SecretPattern.Replace(
            value,
            static match =>
            {
                var field = match.Groups[1].Value;
                var secretValue = match.Groups[2].Value;
                if (secretValue.Length < RedactMinLength)
                {
                    return $"\"{field}\":\"[REDACTED]\"";
                }

                var redacted = $"{secretValue[..8]}...{secretValue[^4..]}";
                return $"\"{field}\":\"{redacted}\"";
            });
    }

    public static string DebugTruncate(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var flattened = value.Replace("\n", "\\n", StringComparison.Ordinal);
        return flattened.Length <= DebugMessageLimit
            ? flattened
            : flattened[..DebugMessageLimit] + $"... ({flattened.Length} chars)";
    }

    public static string DebugBody(object? data)
    {
        var raw = data as string ?? JsonSerializer.Serialize(data);
        var redacted = RedactSecrets(raw);
        return redacted.Length <= DebugMessageLimit
            ? redacted
            : redacted[..DebugMessageLimit] + $"... ({redacted.Length} chars)";
    }

    public static string? ExtractErrorDetail(JsonNode? data)
    {
        if (data is not JsonObject obj)
        {
            return null;
        }

        if (obj["message"] is JsonValue messageValue &&
            messageValue.TryGetValue<string>(out var message))
        {
            return message;
        }

        if (obj["error"] is JsonObject errorObj &&
            errorObj["message"] is JsonValue errorMessageValue &&
            errorMessageValue.TryGetValue<string>(out var errorMessage))
        {
            return errorMessage;
        }

        return null;
    }

    public static void LogBridgeSkip(string reason, string? debugMessage = null, bool? v2 = null)
    {
        if (!string.IsNullOrWhiteSpace(debugMessage))
        {
            ClawSharpTelemetry.LogDebug(debugMessage);
        }

        ClawSharpTelemetry.LogEvent(
            "tengu_bridge_repl_skipped",
            new Dictionary<string, object?>
            {
                ["reason"] = reason,
                ["v2"] = v2
            });
        ClawSharpTelemetry.RecordMetric(
            "bridge.skip.count",
            1,
            new Dictionary<string, object?>
            {
                ["reason"] = reason
            });
    }
}
