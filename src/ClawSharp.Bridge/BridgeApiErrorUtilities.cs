// TS origin: ./bridge/bridgeApi.ts, ./bridge/bridgeDebug.ts
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed class BridgeFatalError : Exception
{
    public BridgeFatalError(string message, int status, string? errorType = null)
        : base(message)
    {
        Status = status;
        ErrorType = errorType;
    }

    public int Status { get; }

    public string? ErrorType { get; }
}

public static partial class BridgeApiErrorUtilities
{
    public static string ValidateBridgeId(string id, string label)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(label);

        if (string.IsNullOrEmpty(id) || !SafeIdPattern().IsMatch(id))
        {
            throw new InvalidOperationException($"Invalid {label}: contains unsafe characters");
        }

        return id;
    }

    public static void HandleErrorStatus(int status, object? data, string context)
    {
        if (status is 200 or 204)
        {
            return;
        }

        var detail = BridgeDebugUtilities.ExtractErrorDetail(ToJsonNode(data));
        var errorType = ExtractErrorTypeFromData(data);
        switch (status)
        {
            case 401:
                throw new BridgeFatalError(
                    $"{context}: Authentication failed (401){(detail is null ? string.Empty : $": {detail}")}. {BridgeConstants.BridgeLoginInstruction}",
                    401,
                    errorType);
            case 403:
                throw new BridgeFatalError(
                    IsExpiredErrorType(errorType)
                        ? $"Remote Control session has expired. Please restart with `{AppMetadata.RemoteControlCommand}` or /remote-control."
                        : $"{context}: Access denied (403){(detail is null ? string.Empty : $": {detail}")}. Check your organization permissions.",
                    403,
                    errorType);
            case 404:
                throw new BridgeFatalError(
                    detail ?? $"{context}: Not found (404). Remote Control may not be available for this organization.",
                    404,
                    errorType);
            case 410:
                throw new BridgeFatalError(
                    detail ?? $"Remote Control session has expired. Please restart with `{AppMetadata.RemoteControlCommand}` or /remote-control.",
                    410,
                    errorType ?? "environment_expired");
            case 429:
                throw new InvalidOperationException($"{context}: Rate limited (429). Polling too frequently.");
            default:
                throw new InvalidOperationException(
                    $"{context}: Failed with status {status}{(detail is null ? string.Empty : $": {detail}")}");
        }
    }

    public static bool IsExpiredErrorType(string? errorType)
    {
        return !string.IsNullOrWhiteSpace(errorType) &&
               (errorType.Contains("expired", StringComparison.Ordinal) ||
                errorType.Contains("lifetime", StringComparison.Ordinal));
    }

    public static bool IsSuppressible403(BridgeFatalError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return error.Status == 403 &&
               (error.Message.Contains("external_poll_sessions", StringComparison.Ordinal) ||
                error.Message.Contains("environments:manage", StringComparison.Ordinal));
    }

    public static string? ExtractErrorTypeFromData(object? data)
    {
        if (ToJsonNode(data) is not JsonObject obj)
        {
            return null;
        }

        return obj["error"]?["type"]?.GetValue<string>();
    }

    private static JsonNode? ToJsonNode(object? data)
    {
        return data switch
        {
            null => null,
            JsonNode node => node,
            _ => JsonSerializer.SerializeToNode(data)
        };
    }

    [GeneratedRegex("^[a-zA-Z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeIdPattern();
}
