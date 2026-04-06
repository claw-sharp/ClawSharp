// TS origin: ./bridge/bridgeMessaging.ts
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Bridge;

public sealed record BridgeControlRequestMessage(
    string Type,
    string RequestId,
    object? Request);

public sealed record BridgeControlResponseMessage(
    string Type,
    object? Response);

public sealed record BridgeResultSuccessMessage(
    string Type,
    string Subtype,
    long DurationMs,
    long DurationApiMs,
    bool IsError,
    int NumTurns,
    string Result,
    string? StopReason,
    double TotalCostUsd,
    BridgeEmptyUsage Usage,
    IReadOnlyDictionary<string, object?> ModelUsage,
    IReadOnlyList<string> PermissionDenials,
    string SessionId,
    string Uuid);

public sealed record BridgeEmptyUsage(
    int InputTokens,
    int CacheCreationInputTokens,
    int CacheReadInputTokens,
    int OutputTokens,
    int CacheDeletedInputTokens);

public sealed record BridgeMessageEnvelope(
    string Type,
    bool IsVirtual = false,
    string? Subtype = null);

public sealed record BridgeMessageOrigin(
    string Kind);

public sealed record BridgeTitleContentBlock(
    string Type,
    string? Text = null);

public sealed record BridgeTitleMessage(
    string Type,
    bool IsMeta = false,
    bool ToolUseResult = false,
    bool IsCompactSummary = false,
    BridgeMessageOrigin? Origin = null,
    string? StringContent = null,
    IReadOnlyList<BridgeTitleContentBlock>? ContentBlocks = null);

public static class BridgeMessagingUtilities
{
    public static JsonNode? NormalizeControlMessageKeys(JsonNode? node)
    {
        if (node is not JsonObject record)
        {
            return node;
        }

        if (record["requestId"] is not null && record["request_id"] is null)
        {
            record["request_id"] = record["requestId"]!.DeepClone();
            record.Remove("requestId");
        }

        if (record["response"] is JsonObject response &&
            response["requestId"] is not null &&
            response["request_id"] is null)
        {
            response["request_id"] = response["requestId"]!.DeepClone();
            response.Remove("requestId");
        }

        return node;
    }

    public static bool IsSdkMessage(object? value)
    {
        return TryGetType(value) is not null;
    }

    public static bool IsSdkControlResponse(object? value)
    {
        return string.Equals(TryGetType(value), "control_response", StringComparison.Ordinal) &&
               HasProperty(value, "response");
    }

    public static bool IsSdkControlRequest(object? value)
    {
        return string.Equals(TryGetType(value), "control_request", StringComparison.Ordinal) &&
               HasProperty(value, "request_id") &&
               HasProperty(value, "request");
    }

    public static bool IsEligibleBridgeMessage(BridgeMessageEnvelope message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if ((message.Type is "user" or "assistant") && message.IsVirtual)
        {
            return false;
        }

        return message.Type is "user" or "assistant" ||
               (message.Type == "system" && message.Subtype == "local_command");
    }

    public static void HandleIngressMessage(
        string data,
        BoundedUuidSet recentPostedUuids,
        BoundedUuidSet recentInboundUuids,
        Func<JsonObject, Task>? onInboundMessage,
        Action<JsonObject>? onPermissionResponse = null,
        Action<JsonObject>? onControlRequest = null,
        Action<string>? onDebug = null,
        Action? onBridgeMessageReceived = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(recentPostedUuids);
        ArgumentNullException.ThrowIfNull(recentInboundUuids);

        try
        {
            var parsed = NormalizeControlMessageKeys(JsonNode.Parse(data));
            if (parsed is not JsonObject parsedObject)
            {
                return;
            }

            if (string.Equals(parsedObject["type"]?.GetValue<string>(), "control_response", StringComparison.Ordinal) &&
                parsedObject["response"] is not null)
            {
                onDebug?.Invoke("[bridge:repl] Ingress message type=control_response");
                onPermissionResponse?.Invoke(parsedObject);
                return;
            }

            if (string.Equals(parsedObject["type"]?.GetValue<string>(), "control_request", StringComparison.Ordinal) &&
                parsedObject["request_id"] is not null &&
                parsedObject["request"] is not null)
            {
                var subtype = parsedObject["request"]?["subtype"]?.GetValue<string>();
                onDebug?.Invoke($"[bridge:repl] Inbound control_request subtype={subtype}");
                onControlRequest?.Invoke(parsedObject);
                return;
            }

            var type = parsedObject["type"]?.GetValue<string>();
            if (type is null)
            {
                return;
            }

            var uuid = parsedObject["uuid"]?.GetValue<string>();
            if (uuid is not null && recentPostedUuids.Contains(uuid))
            {
                onDebug?.Invoke($"[bridge:repl] Ignoring echo: type={type} uuid={uuid}");
                return;
            }

            if (uuid is not null && recentInboundUuids.Contains(uuid))
            {
                onDebug?.Invoke($"[bridge:repl] Ignoring re-delivered inbound: type={type} uuid={uuid}");
                return;
            }

            onDebug?.Invoke($"[bridge:repl] Ingress message type={type}{(uuid is null ? string.Empty : $" uuid={uuid}")}");

            if (type == "user")
            {
                if (uuid is not null)
                {
                    recentInboundUuids.Add(uuid);
                }

                onBridgeMessageReceived?.Invoke();
                _ = onInboundMessage?.Invoke(parsedObject);
            }
            else
            {
                onDebug?.Invoke($"[bridge:repl] Ignoring non-user inbound message: type={type}");
            }
        }
        catch (Exception error)
        {
            onDebug?.Invoke($"[bridge:repl] Failed to parse ingress message: {error.Message}");
        }
    }

    public static string? ExtractTitleText(BridgeTitleMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Type != "user" || message.IsMeta || message.ToolUseResult || message.IsCompactSummary)
        {
            return null;
        }

        if (message.Origin is not null && message.Origin.Kind != "human")
        {
            return null;
        }

        string? raw = message.StringContent;
        if (raw is null && message.ContentBlocks is not null)
        {
            foreach (var block in message.ContentBlocks)
            {
                if (block.Type == "text")
                {
                    raw = block.Text;
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(raw))
        {
            return null;
        }

        var clean = BridgeDisplayTagUtilities.StripDisplayTagsAllowEmpty(raw);
        return string.IsNullOrEmpty(clean) ? null : clean;
    }

    public static BridgeResultSuccessMessage MakeResultMessage(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        return new BridgeResultSuccessMessage(
            "result",
            "success",
            0,
            0,
            false,
            0,
            string.Empty,
            null,
            0,
            new BridgeEmptyUsage(0, 0, 0, 0, 0),
            new Dictionary<string, object?>(StringComparer.Ordinal),
            Array.Empty<string>(),
            sessionId,
            Guid.NewGuid().ToString());
    }

    public static JsonObject MakeResultMessageNode(string sessionId)
    {
        ArgumentNullException.ThrowIfNull(sessionId);

        var message = MakeResultMessage(sessionId);
        return new JsonObject
        {
            ["type"] = message.Type,
            ["subtype"] = message.Subtype,
            ["duration_ms"] = message.DurationMs,
            ["duration_api_ms"] = message.DurationApiMs,
            ["is_error"] = message.IsError,
            ["num_turns"] = message.NumTurns,
            ["result"] = message.Result,
            ["stop_reason"] = message.StopReason,
            ["total_cost_usd"] = message.TotalCostUsd,
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = message.Usage.InputTokens,
                ["cache_creation_input_tokens"] = message.Usage.CacheCreationInputTokens,
                ["cache_read_input_tokens"] = message.Usage.CacheReadInputTokens,
                ["output_tokens"] = message.Usage.OutputTokens,
                ["cache_deleted_input_tokens"] = message.Usage.CacheDeletedInputTokens
            },
            ["model_usage"] = new JsonObject(),
            ["permission_denials"] = new JsonArray(),
            ["session_id"] = message.SessionId,
            ["uuid"] = message.Uuid
        };
    }

    private static string? TryGetType(object? value)
    {
        return TryGetPropertyValue(value, "type") as string;
    }

    private static bool HasProperty(object? value, string propertyName)
    {
        return TryGetPropertyValue(value, propertyName) is not null;
    }

    private static object? TryGetPropertyValue(object? value, string propertyName)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonObject jsonObject)
        {
            return jsonObject[propertyName] switch
            {
                JsonValue jsonValue when jsonValue.TryGetValue<string>(out var stringValue) => stringValue,
                JsonNode node => node,
                _ => null
            };
        }

        if (value is IReadOnlyDictionary<string, object?> readOnlyDictionary &&
            readOnlyDictionary.TryGetValue(propertyName, out var readOnlyValue))
        {
            return readOnlyValue;
        }

        if (value is IDictionary<string, object?> dictionary &&
            dictionary.TryGetValue(propertyName, out var dictionaryValue))
        {
            return dictionaryValue;
        }

        var propertyNameVariants = new[]
        {
            propertyName,
            ToPascalCase(propertyName)
        };

        foreach (var variant in propertyNameVariants)
        {
            var property = value.GetType().GetProperty(variant);
            if (property is not null)
            {
                return property.GetValue(value);
            }
        }

        return null;
    }

    private static string ToPascalCase(string propertyName)
    {
        return string.Concat(
            propertyName
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
