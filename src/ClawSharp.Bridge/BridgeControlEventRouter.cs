using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public sealed record BridgePermissionModeVerdict(
    bool Ok,
    string? Error = null);

public interface IBridgeControlEventTransport
{
    Task WriteAsync(JsonObject message, CancellationToken cancellationToken = default);
}

public sealed record BridgeServerControlRequestHandlers(
    IBridgeControlEventTransport? Transport,
    string SessionId,
    bool OutboundOnly = false,
    Action? OnInterrupt = null,
    Action<string?>? OnSetModel = null,
    Action<int?>? OnSetMaxThinkingTokens = null,
    Func<string, BridgePermissionModeVerdict>? OnSetPermissionMode = null,
    Action<string>? OnDebug = null);

public sealed record BridgeOutboundControlEventHandlers(
    IBridgeControlEventTransport? Transport,
    string SessionId,
    bool AuthRecoveryInFlight = false,
    bool IsRemoteBridge = false,
    Action<string>? ReportState = null,
    Action<string>? OnDebug = null);

public static class BridgeControlEventRouter
{
    private const string OutboundOnlyError =
        "This session is outbound-only. Enable Remote Control locally to allow inbound control.";

    public static async Task HandleServerControlRequestAsync(
        JsonObject request,
        BridgeServerControlRequestHandlers handlers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handlers);

        if (handlers.Transport is null)
        {
            handlers.OnDebug?.Invoke(
                "[bridge:repl] Cannot respond to control_request: transport not configured");
            return;
        }

        var requestId = request["request_id"]?.GetValue<string>();
        var subtype = request["request"]?["subtype"]?.GetValue<string>();
        if (requestId is null || subtype is null)
        {
            return;
        }

        JsonObject response;
        if (handlers.OutboundOnly && !string.Equals(subtype, "initialize", StringComparison.Ordinal))
        {
            response = CreateErrorControlResponse(requestId, OutboundOnlyError);
            await handlers.Transport.WriteAsync(
                AttachSessionId(response, handlers.SessionId),
                cancellationToken);
            handlers.OnDebug?.Invoke(
                $"[bridge:repl] Rejected {subtype} (outbound-only) request_id={requestId}");
            return;
        }

        switch (subtype)
        {
            case "initialize":
                response = new JsonObject
                {
                    ["type"] = "control_response",
                    ["response"] = new JsonObject
                    {
                        ["subtype"] = "success",
                        ["request_id"] = requestId,
                        ["response"] = new JsonObject
                        {
                            ["commands"] = new JsonArray(),
                            ["output_style"] = "normal",
                            ["available_output_styles"] = new JsonArray("normal"),
                            ["models"] = new JsonArray(),
                            ["account"] = new JsonObject(),
                            ["pid"] = Environment.ProcessId
                        }
                    }
                };
                break;

            case "set_model":
                handlers.OnSetModel?.Invoke(request["request"]?["model"]?.GetValue<string>());
                response = CreateSuccessControlResponse(requestId);
                break;

            case "set_max_thinking_tokens":
                handlers.OnSetMaxThinkingTokens?.Invoke(request["request"]?["max_thinking_tokens"]?.GetValue<int?>());
                response = CreateSuccessControlResponse(requestId);
                break;

            case "set_permission_mode":
            {
                var mode = request["request"]?["mode"]?.GetValue<string>() ?? string.Empty;
                var verdict = handlers.OnSetPermissionMode?.Invoke(mode)
                              ?? new BridgePermissionModeVerdict(
                                  false,
                                  "set_permission_mode is not supported in this context (onSetPermissionMode callback not registered)");
                response = verdict.Ok
                    ? CreateSuccessControlResponse(requestId)
                    : CreateErrorControlResponse(requestId, verdict.Error ?? string.Empty);
                break;
            }

            case "interrupt":
                handlers.OnInterrupt?.Invoke();
                response = CreateSuccessControlResponse(requestId);
                break;

            default:
                response = CreateErrorControlResponse(
                    requestId,
                    $"REPL bridge does not handle control_request subtype: {subtype}");
                break;
        }

        await handlers.Transport.WriteAsync(
            AttachSessionId(response, handlers.SessionId),
            cancellationToken);
        handlers.OnDebug?.Invoke(
            $"[bridge:repl] Sent control_response for {subtype} request_id={requestId} result={response["response"]?["subtype"]?.GetValue<string>()}");
    }

    public static async Task SendControlRequestAsync(
        JsonObject request,
        BridgeOutboundControlEventHandlers handlers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handlers);

        if (handlers.Transport is null)
        {
            handlers.OnDebug?.Invoke(handlers.IsRemoteBridge
                ? "[remote-bridge] Transport not configured, skipping control_request"
                : "[bridge:repl] Transport not configured, skipping control_request");
            return;
        }

        var requestId = request["request_id"]?.GetValue<string>();
        if (handlers.AuthRecoveryInFlight)
        {
            handlers.OnDebug?.Invoke(
                $"[remote-bridge] Dropping control_request during 401 recovery: {requestId}");
            return;
        }

        if (string.Equals(request["request"]?["subtype"]?.GetValue<string>(), "can_use_tool", StringComparison.Ordinal))
        {
            handlers.ReportState?.Invoke("requires_action");
        }

        await handlers.Transport.WriteAsync(AttachSessionId(request, handlers.SessionId), cancellationToken);
        handlers.OnDebug?.Invoke(handlers.IsRemoteBridge
            ? $"[remote-bridge] Sent control_request request_id={requestId}"
            : $"[bridge:repl] Sent control_request request_id={requestId}");
    }

    public static async Task SendControlResponseAsync(
        JsonObject response,
        BridgeOutboundControlEventHandlers handlers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(handlers);

        if (handlers.Transport is null)
        {
            handlers.OnDebug?.Invoke(handlers.IsRemoteBridge
                ? "[remote-bridge] Transport not configured, skipping control_response"
                : "[bridge:repl] Transport not configured, skipping control_response");
            return;
        }

        if (handlers.AuthRecoveryInFlight)
        {
            handlers.OnDebug?.Invoke("[remote-bridge] Dropping control_response during 401 recovery");
            return;
        }

        if (handlers.IsRemoteBridge)
        {
            handlers.ReportState?.Invoke("running");
        }

        await handlers.Transport.WriteAsync(AttachSessionId(response, handlers.SessionId), cancellationToken);
        handlers.OnDebug?.Invoke(handlers.IsRemoteBridge
            ? "[remote-bridge] Sent control_response"
            : "[bridge:repl] Sent control_response");
    }

    public static async Task SendControlCancelRequestAsync(
        string requestId,
        BridgeOutboundControlEventHandlers handlers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestId);
        ArgumentNullException.ThrowIfNull(handlers);

        if (handlers.Transport is null)
        {
            handlers.OnDebug?.Invoke(handlers.IsRemoteBridge
                ? "[remote-bridge] Transport not configured, skipping control_cancel_request"
                : "[bridge:repl] Transport not configured, skipping control_cancel_request");
            return;
        }

        if (handlers.AuthRecoveryInFlight)
        {
            handlers.OnDebug?.Invoke(
                $"[remote-bridge] Dropping control_cancel_request during 401 recovery: {requestId}");
            return;
        }

        if (handlers.IsRemoteBridge)
        {
            handlers.ReportState?.Invoke("running");
        }

        var message = new JsonObject
        {
            ["type"] = "control_cancel_request",
            ["request_id"] = requestId
        };

        await handlers.Transport.WriteAsync(AttachSessionId(message, handlers.SessionId), cancellationToken);
        handlers.OnDebug?.Invoke(handlers.IsRemoteBridge
            ? $"[remote-bridge] Sent control_cancel_request request_id={requestId}"
            : $"[bridge:repl] Sent control_cancel_request request_id={requestId}");
    }

    public static async Task SendResultAsync(
        BridgeOutboundControlEventHandlers handlers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handlers);

        if (handlers.Transport is null)
        {
            handlers.OnDebug?.Invoke(handlers.IsRemoteBridge
                ? "[remote-bridge] Transport not configured, skipping result"
                : "[bridge:repl] Transport not configured, skipping result");
            return;
        }

        if (handlers.AuthRecoveryInFlight)
        {
            handlers.OnDebug?.Invoke("[remote-bridge] Dropping result during 401 recovery");
            return;
        }

        if (handlers.IsRemoteBridge)
        {
            handlers.ReportState?.Invoke("idle");
        }

        var result = BridgeMessagingUtilities.MakeResultMessageNode(handlers.SessionId);
        await handlers.Transport.WriteAsync(result, cancellationToken);
        handlers.OnDebug?.Invoke(handlers.IsRemoteBridge
            ? "[remote-bridge] Sent result"
            : "[bridge:repl] Sent result");
    }

    private static JsonObject CreateSuccessControlResponse(string requestId)
    {
        return new JsonObject
        {
            ["type"] = "control_response",
            ["response"] = new JsonObject
            {
                ["subtype"] = "success",
                ["request_id"] = requestId
            }
        };
    }

    private static JsonObject CreateErrorControlResponse(string requestId, string error)
    {
        return new JsonObject
        {
            ["type"] = "control_response",
            ["response"] = new JsonObject
            {
                ["subtype"] = "error",
                ["request_id"] = requestId,
                ["error"] = error
            }
        };
    }

    private static JsonObject AttachSessionId(JsonObject message, string sessionId)
    {
        var clone = message.DeepClone() as JsonObject ?? new JsonObject();
        clone["session_id"] = sessionId;
        return clone;
    }
}
