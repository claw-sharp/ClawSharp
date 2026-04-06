// TS origin: ./utils/hooks.ts, ./types/hooks.ts
// TS parity status: ports the sync hook JSON parsing and stop-query-relevant result processing surface; async hook JSON remains intentionally unported until the background hook runtime exists in C#.
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

internal static class HookSyncJsonOutputProcessor
{
    public static HookSyncJsonParseResult ParseCommandOutput(string stdout)
    {
        var trimmed = stdout.Trim();
        if (!trimmed.StartsWith('{'))
        {
            return new HookSyncJsonParseResult(PlainText: stdout);
        }

        return ParseJsonText(trimmed);
    }

    public static HookSyncJsonParseResult ParseHttpOutput(string body)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
        {
            return new HookSyncJsonParseResult(
                Parsed: new HookSyncJsonOutput(
                    Continue: null,
                    SuppressOutput: null,
                    StopReason: null,
                    Decision: null,
                    Reason: null,
                    SystemMessage: null,
                    HookSpecificOutput: null));
        }

        if (!trimmed.StartsWith('{'))
        {
            return new HookSyncJsonParseResult(
                ValidationError: $"HTTP hook must return JSON, but got non-JSON response body: {TrimForError(trimmed)}");
        }

        return ParseJsonText(trimmed);
    }

public static IReadOnlyList<HookExecutionUpdate> ProcessSyncOutput(
        HookSyncJsonOutput json,
        string command,
        string hookName,
        string toolUseId,
        HookEvent hookEvent,
        string? stdout,
        string? stderr,
        int? exitCode)
    {
        var updates = new List<HookExecutionUpdate>();
        HookBlockingError? blockingError = null;
        PermissionBehavior? permissionBehavior = null;
        string? hookPermissionDecisionReason = null;

        if (json.Continue == false)
        {
            updates.Add(
                new HookExecutionUpdate(
                    PreventContinuation: true,
                    StopReason: json.StopReason));
        }

        if (json.Decision is not null)
        {
            switch (json.Decision)
            {
                case "approve":
                    permissionBehavior = PermissionBehavior.Allow;
                    break;
                case "block":
                    permissionBehavior = PermissionBehavior.Deny;
                    blockingError = new HookBlockingError(
                        json.Reason ?? "Blocked by hook",
                        command);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unknown hook decision type: {json.Decision}. Valid types are: approve, block");
            }
        }

        if (permissionBehavior is not null && !string.IsNullOrWhiteSpace(json.Reason))
        {
            hookPermissionDecisionReason = json.Reason;
        }

        if (json.HookSpecificOutput is not null)
        {
            if (!string.Equals(json.HookSpecificOutput.HookEventName, hookEvent.ToString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Hook returned incorrect event name: expected '{hookEvent}' but got '{json.HookSpecificOutput.HookEventName}'.");
            }

            switch (json.HookSpecificOutput.HookEventName)
            {
                case "PreToolUse":
                    if (json.HookSpecificOutput.PermissionDecision is not null)
                    {
                        switch (json.HookSpecificOutput.PermissionDecision)
                        {
                            case "allow":
                                permissionBehavior = PermissionBehavior.Allow;
                                break;
                            case "deny":
                                permissionBehavior = PermissionBehavior.Deny;
                                blockingError = new HookBlockingError(
                                    json.HookSpecificOutput.PermissionDecisionReason ??
                                    json.Reason ??
                                    "Blocked by hook",
                                    command);
                                break;
                            case "ask":
                                permissionBehavior = PermissionBehavior.Ask;
                                break;
                            default:
                                throw new InvalidOperationException(
                                    $"Unknown hook permissionDecision type: {json.HookSpecificOutput.PermissionDecision}. Valid types are: allow, deny, ask");
                        }
                    }

                    hookPermissionDecisionReason = json.HookSpecificOutput.PermissionDecisionReason ?? hookPermissionDecisionReason;
                    break;
                case "PostToolUse":
                case "PostToolUseFailure":
                case "UserPromptSubmit":
                case "SessionStart":
                case "Setup":
                case "SubagentStart":
                case "Notification":
                    break;
            }
        }

        if (blockingError is not null)
        {
            updates.Add(
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookBlockingErrorAttachmentMessage(
                        blockingError,
                        hookName,
                        toolUseId,
                        hookEvent),
                    BlockingError: blockingError,
                    PermissionBehavior: permissionBehavior,
                    HookPermissionDecisionReason: hookPermissionDecisionReason,
                    UpdatedInput: json.HookSpecificOutput?.UpdatedInput,
                    UpdatedMcpToolOutput: json.HookSpecificOutput?.UpdatedMcpToolOutput,
                    AdditionalContexts: string.IsNullOrWhiteSpace(json.HookSpecificOutput?.AdditionalContext)
                        ? null
                        : [json.HookSpecificOutput.AdditionalContext]));
        }
        else
        {
            updates.Add(
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookSuccessAttachmentMessage(
                        content: string.Empty,
                        hookName: hookName,
                        toolUseId: toolUseId,
                        hookEvent: hookEvent,
                        stdout: stdout,
                        stderr: stderr,
                        exitCode: exitCode,
                        command: command),
                    PermissionBehavior: permissionBehavior,
                    HookPermissionDecisionReason: hookPermissionDecisionReason,
                    UpdatedInput: json.HookSpecificOutput?.UpdatedInput,
                    UpdatedMcpToolOutput: json.HookSpecificOutput?.UpdatedMcpToolOutput,
                    AdditionalContexts: string.IsNullOrWhiteSpace(json.HookSpecificOutput?.AdditionalContext)
                        ? null
                        : [json.HookSpecificOutput.AdditionalContext]));
        }

        if (!string.IsNullOrWhiteSpace(json.SystemMessage))
        {
            updates.Add(
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookSystemMessageAttachmentMessage(
                        json.SystemMessage,
                        hookName,
                        toolUseId,
                        hookEvent)));
        }

        if (!string.IsNullOrWhiteSpace(json.HookSpecificOutput?.AdditionalContext))
        {
            updates.Add(
                new HookExecutionUpdate(
                    Message: ChatMessageFactory.CreateHookAdditionalContextAttachmentMessage(
                        [json.HookSpecificOutput.AdditionalContext],
                        hookName,
                        toolUseId,
                        hookEvent),
                    AdditionalContexts: [json.HookSpecificOutput.AdditionalContext]));
        }

        return updates;
    }

    private static HookSyncJsonParseResult ParseJsonText(string jsonText)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(jsonText);
        }
        catch
        {
            return new HookSyncJsonParseResult(PlainText: jsonText);
        }

        if (parsed is not JsonObject root)
        {
            return new HookSyncJsonParseResult(
                PlainText: jsonText,
                ValidationError: $"Hook JSON output validation failed:{Environment.NewLine}Expected a JSON object.");
        }

        if (TryGetBoolean(root, "async", out var asyncValue) && asyncValue)
        {
            return new HookSyncJsonParseResult(PlainText: jsonText);
        }

        if (!TryGetOptionalBoolean(root, "continue", out var continueValue, out var continueError))
        {
            return new HookSyncJsonParseResult(PlainText: jsonText, ValidationError: continueError);
        }

        if (!TryGetOptionalBoolean(root, "suppressOutput", out var suppressOutput, out var suppressOutputError))
        {
            return new HookSyncJsonParseResult(PlainText: jsonText, ValidationError: suppressOutputError);
        }

        if (!TryGetOptionalString(root, "stopReason", out var stopReason, out var stopReasonError))
        {
            return new HookSyncJsonParseResult(PlainText: jsonText, ValidationError: stopReasonError);
        }

        if (!TryGetOptionalString(root, "decision", out var decision, out var decisionError))
        {
            return new HookSyncJsonParseResult(PlainText: jsonText, ValidationError: decisionError);
        }

        if (decision is not null &&
            !string.Equals(decision, "approve", StringComparison.Ordinal) &&
            !string.Equals(decision, "block", StringComparison.Ordinal))
        {
            return new HookSyncJsonParseResult(
                PlainText: jsonText,
                ValidationError: $"Hook JSON output validation failed:{Environment.NewLine}decision must be 'approve' or 'block'.");
        }

        if (!TryGetOptionalString(root, "reason", out var reason, out var reasonError))
        {
            return new HookSyncJsonParseResult(PlainText: jsonText, ValidationError: reasonError);
        }

        if (!TryGetOptionalString(root, "systemMessage", out var systemMessage, out var systemMessageError))
        {
            return new HookSyncJsonParseResult(PlainText: jsonText, ValidationError: systemMessageError);
        }

        if (!TryGetOptionalHookSpecificOutput(root, out var hookSpecificOutput, out var hookSpecificOutputError))
        {
            return new HookSyncJsonParseResult(PlainText: jsonText, ValidationError: hookSpecificOutputError);
        }

        return new HookSyncJsonParseResult(
            Parsed: new HookSyncJsonOutput(
                continueValue,
                suppressOutput,
                stopReason,
                decision,
                reason,
                systemMessage,
                hookSpecificOutput));
    }

    private static bool TryGetOptionalBoolean(
        JsonObject root,
        string propertyName,
        out bool? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!root.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return true;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var boolValue))
        {
            value = boolValue;
            return true;
        }

        error = $"Hook JSON output validation failed:{Environment.NewLine}{propertyName} must be a boolean.";
        return false;
    }

    private static bool TryGetBoolean(JsonObject root, string propertyName, out bool value)
    {
        value = false;
        if (!root.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return false;
        }

        return node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value);
    }

    private static bool TryGetOptionalString(
        JsonObject root,
        string propertyName,
        out string? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!root.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return true;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            value = stringValue;
            return true;
        }

        error = $"Hook JSON output validation failed:{Environment.NewLine}{propertyName} must be a string.";
        return false;
    }

    private static bool TryGetOptionalHookSpecificOutput(
        JsonObject root,
        out HookSyncHookSpecificOutput? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!root.TryGetPropertyValue("hookSpecificOutput", out var node) || node is null)
        {
            return true;
        }

        if (node is not JsonObject hookSpecificRoot)
        {
            error = $"Hook JSON output validation failed:{Environment.NewLine}hookSpecificOutput must be an object.";
            return false;
        }

        if (!TryGetOptionalString(hookSpecificRoot, "hookEventName", out var hookEventName, out error) ||
            string.IsNullOrWhiteSpace(hookEventName))
        {
            error ??= $"Hook JSON output validation failed:{Environment.NewLine}hookSpecificOutput.hookEventName must be a string.";
            return false;
        }

        if (!TryGetOptionalString(hookSpecificRoot, "permissionDecision", out var permissionDecision, out error))
        {
            return false;
        }

        if (permissionDecision is not null &&
            permissionDecision is not ("allow" or "deny" or "ask"))
        {
            error = $"Hook JSON output validation failed:{Environment.NewLine}hookSpecificOutput.permissionDecision must be 'allow', 'deny', or 'ask'.";
            return false;
        }

        if (!TryGetOptionalString(hookSpecificRoot, "permissionDecisionReason", out var permissionDecisionReason, out error) ||
            !TryGetOptionalString(hookSpecificRoot, "additionalContext", out var additionalContext, out error))
        {
            return false;
        }

        JsonObject? updatedInput = null;
        if (hookSpecificRoot.TryGetPropertyValue("updatedInput", out var updatedInputNode) && updatedInputNode is not null)
        {
            if (updatedInputNode is not JsonObject updatedInputObject)
            {
                error = $"Hook JSON output validation failed:{Environment.NewLine}hookSpecificOutput.updatedInput must be an object.";
                return false;
            }

            updatedInput = updatedInputObject;
        }

        JsonNode? updatedMcpToolOutput = null;
        if (hookSpecificRoot.TryGetPropertyValue("updatedMCPToolOutput", out var updatedMcpToolOutputNode))
        {
            updatedMcpToolOutput = updatedMcpToolOutputNode?.DeepClone();
        }

        value = new HookSyncHookSpecificOutput(
            hookEventName,
            permissionDecision,
            permissionDecisionReason,
            updatedInput,
            additionalContext,
            updatedMcpToolOutput);
        return true;
    }

    private static string TrimForError(string value)
    {
        return value.Length > 200 ? value[..200] + "\u2026" : value;
    }
}

internal sealed record HookSyncJsonParseResult(
    HookSyncJsonOutput? Parsed = null,
    string? PlainText = null,
    string? ValidationError = null);

internal sealed record HookSyncJsonOutput(
    bool? Continue,
    bool? SuppressOutput,
    string? StopReason,
    string? Decision,
    string? Reason,
    string? SystemMessage,
    HookSyncHookSpecificOutput? HookSpecificOutput);

internal sealed record HookSyncHookSpecificOutput(
    string HookEventName,
    string? PermissionDecision,
    string? PermissionDecisionReason,
    JsonObject? UpdatedInput,
    string? AdditionalContext,
    JsonNode? UpdatedMcpToolOutput);
