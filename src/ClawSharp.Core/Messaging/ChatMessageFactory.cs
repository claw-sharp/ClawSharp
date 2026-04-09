using System.Text.Json.Nodes;
using System.Text.Json;

namespace ClawSharp.Core;

public static class ChatMessageFactory
{
    public const string NoContentMessage = "(no content)";
    public const string InterruptMessage = "[Request interrupted by user]";
    public const string InterruptMessageForToolUse = "[Request interrupted by user for tool use]";

    public static ChatMessage CreateText(MessageRole role, string content)
    {
        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            role,
            [new MessageContentBlock(MessageContentKind.Text, content)],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateUserInterruptionMessage(bool toolUse = false)
    {
        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.User,
            [
                new MessageContentBlock(
                    MessageContentKind.Text,
                    toolUse ? InterruptMessageForToolUse : InterruptMessage)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateUserMessage(
        string content,
        bool isMeta = false)
    {
        var metadata = isMeta
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["isMeta"] = true.ToString()
            }
            : null;

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.User,
            [
                new MessageContentBlock(
                    MessageContentKind.Text,
                    string.IsNullOrEmpty(content) ? NoContentMessage : content,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateAssistantApiErrorMessage(
        string content,
        string? apiError = null,
        string? error = null,
        string? errorDetails = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["isApiErrorMessage"] = true.ToString()
        };

        AddMetadataWhenPresent(metadata, "apiError", apiError);
        AddMetadataWhenPresent(metadata, "error", error);
        AddMetadataWhenPresent(metadata, "errorDetails", errorDetails);

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.Assistant,
            [
                new MessageContentBlock(
                    MessageContentKind.Text,
                    string.IsNullOrEmpty(content) ? NoContentMessage : content,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateToolUse(IReadOnlyList<(string ToolUseId, string ToolName, string Arguments)> toolCalls)
    {
        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.Assistant,
            toolCalls
                .Select(
                    toolCall =>
                        new MessageContentBlock(
                            MessageContentKind.ToolUse,
                            toolCall.Arguments,
                            toolCall.ToolName,
                            new Dictionary<string, string>
                            {
                                ["toolUseId"] = toolCall.ToolUseId
                            }))
                .ToArray(),
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateToolResult(
        string toolUseId,
        string toolName,
        string content,
        JsonNode? structuredOutput = null,
        bool? success = null)
    {
        Dictionary<string, string> metadata = new()
        {
            ["toolUseId"] = toolUseId
        };

        if (structuredOutput is not null)
        {
            metadata["structuredOutput"] = structuredOutput.ToJsonString();
        }

        if (success is not null)
        {
            metadata["success"] = success.Value ? bool.TrueString : bool.FalseString;
        }

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.User,
            [
                new MessageContentBlock(
                    MessageContentKind.ToolResult,
                    content,
                    toolName,
                    metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateProgress(string toolUseId, string parentToolUseId, JsonObject data)
    {
        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Progress,
                    data.ToJsonString(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["toolUseId"] = toolUseId,
                        ["parentToolUseId"] = parentToolUseId
                    })
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateMaxTurnsReachedAttachmentMessage(
        int maxTurns,
        int turnCount)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "max_turns_reached",
            ["maxTurns"] = maxTurns.ToString(),
            ["turnCount"] = turnCount.ToString()
        };

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookStoppedContinuationAttachmentMessage(
        string message,
        string hookName,
        string toolUseId,
        HookEvent hookEvent)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_stopped_continuation",
            ["message"] = message,
            ["hookName"] = hookName,
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateStructuredOutputAttachmentMessage(JsonNode data)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "structured_output",
            ["data"] = data.ToJsonString()
        };

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateOutputTokenUsageAttachmentMessage(
        int turn,
        int session,
        int? budget)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "output_token_usage",
            ["turn"] = turn.ToString(),
            ["session"] = session.ToString()
        };

        AddMetadataWhenPresent(metadata, "budget", budget?.ToString());

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookSystemMessageAttachmentMessage(
        string content,
        string hookName,
        string toolUseId,
        HookEvent hookEvent)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_system_message",
            ["content"] = content,
            ["hookName"] = hookName,
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookPermissionDecisionAttachmentMessage(
        string decision,
        string toolUseId,
        HookEvent hookEvent)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_permission_decision",
            ["decision"] = decision,
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookAdditionalContextAttachmentMessage(
        IReadOnlyList<string> content,
        string hookName,
        string toolUseId,
        HookEvent hookEvent)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_additional_context",
            ["content"] = JsonSerializer.Serialize(content),
            ["hookName"] = hookName,
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookBlockingErrorAttachmentMessage(
        HookBlockingError blockingError,
        string hookName,
        string toolUseId,
        HookEvent hookEvent)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_blocking_error",
            ["blockingError"] = JsonSerializer.Serialize(blockingError),
            ["hookName"] = hookName,
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookSuccessAttachmentMessage(
        string content,
        string hookName,
        string toolUseId,
        HookEvent hookEvent,
        string? stdout = null,
        string? stderr = null,
        int? exitCode = null,
        string? command = null,
        int? durationMs = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_success",
            ["content"] = content,
            ["hookName"] = hookName,
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        AddMetadataWhenPresent(metadata, "stdout", stdout);
        AddMetadataWhenPresent(metadata, "stderr", stderr);
        AddMetadataWhenPresent(metadata, "exitCode", exitCode?.ToString());
        AddMetadataWhenPresent(metadata, "command", command);
        AddMetadataWhenPresent(metadata, "durationMs", durationMs?.ToString());

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookCancelledAttachmentMessage(
        string hookName,
        string toolUseId,
        HookEvent hookEvent,
        string? command = null,
        int? durationMs = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_cancelled",
            ["hookName"] = hookName,
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        AddMetadataWhenPresent(metadata, "command", command);
        AddMetadataWhenPresent(metadata, "durationMs", durationMs?.ToString());

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookErrorDuringExecutionAttachmentMessage(
        string content,
        string hookName,
        string toolUseId,
        HookEvent hookEvent,
        string? command = null,
        int? durationMs = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_error_during_execution",
            ["content"] = content,
            ["hookName"] = hookName,
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        AddMetadataWhenPresent(metadata, "command", command);
        AddMetadataWhenPresent(metadata, "durationMs", durationMs?.ToString());

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateHookNonBlockingErrorAttachmentMessage(
        string hookName,
        string stderr,
        string stdout,
        int exitCode,
        string toolUseId,
        HookEvent hookEvent,
        string? command = null,
        int? durationMs = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["attachmentType"] = "hook_non_blocking_error",
            ["hookName"] = hookName,
            ["stderr"] = stderr,
            ["stdout"] = stdout,
            ["exitCode"] = exitCode.ToString(),
            ["toolUseID"] = toolUseId,
            ["hookEvent"] = hookEvent.ToString()
        };

        AddMetadataWhenPresent(metadata, "command", command);
        AddMetadataWhenPresent(metadata, "durationMs", durationMs?.ToString());

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Attachment,
                    string.Empty,
                    Metadata: metadata)
            ],
            DateTimeOffset.UtcNow);
    }

    public static ChatMessage CreateStopHookSummaryMessage(
        int hookCount,
        IReadOnlyList<StopHookInfo> hookInfos,
        IReadOnlyList<string> hookErrors,
        bool preventedContinuation,
        string? stopReason,
        bool hasOutput,
        string level,
        string? toolUseId = null,
        string? hookLabel = null,
        int? totalDurationMs = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["subtype"] = "stop_hook_summary",
            ["hookCount"] = hookCount.ToString(),
            ["hookInfos"] = JsonSerializer.Serialize(hookInfos),
            ["hookErrors"] = JsonSerializer.Serialize(hookErrors),
            ["preventedContinuation"] = preventedContinuation.ToString(),
            ["hasOutput"] = hasOutput.ToString(),
            ["level"] = level
        };

        AddMetadataWhenPresent(metadata, "stopReason", stopReason);
        AddMetadataWhenPresent(metadata, "toolUseID", toolUseId);
        AddMetadataWhenPresent(metadata, "hookLabel", hookLabel);
        AddMetadataWhenPresent(metadata, "totalDurationMs", totalDurationMs?.ToString());

        return CreateMetadataBackedSystemMessage(metadata);
    }

    public static ChatMessage CreateTurnDurationMessage(
        int durationMs,
        TurnDurationBudget? budget = null,
        int? messageCount = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["subtype"] = "turn_duration",
            ["durationMs"] = durationMs.ToString(),
            ["isMeta"] = false.ToString()
        };

        AddMetadataWhenPresent(metadata, "budgetTokens", budget?.Tokens.ToString());
        AddMetadataWhenPresent(metadata, "budgetLimit", budget?.Limit.ToString());
        AddMetadataWhenPresent(metadata, "budgetNudges", budget?.Nudges.ToString());
        AddMetadataWhenPresent(metadata, "messageCount", messageCount?.ToString());

        return CreateMetadataBackedSystemMessage(metadata);
    }

    public static ChatMessage CreateSystemApiErrorMessage(
        JsonNode error,
        int retryInMs,
        int retryAttempt,
        int maxRetries,
        JsonNode? cause = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["subtype"] = "api_error",
            ["level"] = "error",
            ["retryInMs"] = retryInMs.ToString(),
            ["retryAttempt"] = retryAttempt.ToString(),
            ["maxRetries"] = maxRetries.ToString(),
            ["error"] = error.ToJsonString()
        };

        AddMetadataWhenPresent(metadata, "cause", cause?.ToJsonString());

        var errorMessage = error?["message"]?.GetValue<string>() ?? "API error.";
        return CreateMetadataBackedSystemMessage(
            metadata,
            $"Model API Error: {errorMessage} Retrying in {retryInMs / 1000}s (Attempt {retryAttempt} of {maxRetries})...");
    }

    public static ChatMessage CreateSystemMessage(
        string content,
        string level,
        string? toolUseId = null,
        bool preventContinuation = false)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["subtype"] = "informational",
            ["isMeta"] = false.ToString(),
            ["level"] = level
        };

        AddMetadataWhenPresent(metadata, "toolUseID", toolUseId);
        if (preventContinuation)
        {
            metadata["preventContinuation"] = true.ToString();
        }

        return CreateMetadataBackedSystemMessage(metadata, content);
    }

    public static ChatMessage CreateCompactBoundaryMessage(
        string trigger,
        int preTokens,
        string? lastPreCompactMessageUuid = null,
        string? userContext = null,
        int? messagesSummarized = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["subtype"] = "compact_boundary",
            ["level"] = "info",
            ["isMeta"] = false.ToString(),
            ["compactMetadata"] = JsonSerializer.Serialize(
                new CompactBoundaryMetadata(
                    trigger,
                    preTokens,
                    userContext,
                    messagesSummarized))
        };

        AddMetadataWhenPresent(metadata, "logicalParentUuid", lastPreCompactMessageUuid);

        return CreateMetadataBackedSystemMessage(
            metadata,
            "Conversation compacted");
    }

    public static ChatMessage CreateMicrocompactBoundaryMessage(
        string trigger,
        int preTokens,
        int tokensSaved,
        IReadOnlyList<string> compactedToolIds,
        IReadOnlyList<string> clearedAttachmentUuids)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["subtype"] = "microcompact_boundary",
            ["level"] = "info",
            ["isMeta"] = false.ToString(),
            ["microcompactMetadata"] = JsonSerializer.Serialize(
                new MicrocompactBoundaryMetadata(
                    trigger,
                    preTokens,
                    tokensSaved,
                    compactedToolIds.ToArray(),
                    clearedAttachmentUuids.ToArray()))
        };

        return CreateMetadataBackedSystemMessage(
            metadata,
            "Context microcompacted");
    }

    private static ChatMessage CreateMetadataBackedSystemMessage(
        IReadOnlyDictionary<string, string> metadata,
        string content = "")
    {
        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.System,
            [
                new MessageContentBlock(
                    MessageContentKind.Text,
                    content,
                    Metadata: new Dictionary<string, string>(metadata, StringComparer.Ordinal))
            ],
            DateTimeOffset.UtcNow);
    }

    private static void AddMetadataWhenPresent(
        IDictionary<string, string> metadata,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }
}
