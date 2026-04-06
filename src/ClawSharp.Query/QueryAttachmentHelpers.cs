// TS parity status: ports the focused max_turns_reached attachment contract and detection helper; live recursive turn continuation remains blocked on the missing model-backed query loop.
using System.Text.Json.Nodes;
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryAttachmentHelpers
{
    public static bool TryGetMaxTurnsReachedNotification(
        ChatMessage? message,
        out QueryMaxTurnsReachedNotification? notification)
    {
        notification = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "max_turns_reached", StringComparison.Ordinal) ||
            !int.TryParse(GetMetadataValue(block, "maxTurns"), out var maxTurns) ||
            !int.TryParse(GetMetadataValue(block, "turnCount"), out var turnCount))
        {
            return false;
        }

        notification = new QueryMaxTurnsReachedNotification(maxTurns, turnCount);
        return true;
    }

    public static bool TryGetHookStoppedContinuationAttachment(
        ChatMessage? message,
        out QueryHookStoppedContinuationAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_stopped_continuation", StringComparison.Ordinal))
        {
            return false;
        }

        var reasonMessage = GetMetadataValue(block, "message");
        var hookName = GetMetadataValue(block, "hookName");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if (string.IsNullOrWhiteSpace(reasonMessage) ||
            string.IsNullOrWhiteSpace(hookName) ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent))
        {
            return false;
        }

        attachment = new QueryHookStoppedContinuationAttachment(
            reasonMessage,
            hookName,
            toolUseId,
            hookEvent);
        return true;
    }

    public static bool TryGetStructuredOutputAttachment(
        ChatMessage? message,
        out QueryStructuredOutputAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "structured_output", StringComparison.Ordinal))
        {
            return false;
        }

        var dataJson = GetMetadataValue(block, "data");
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return false;
        }

        JsonNode? data;
        try
        {
            data = JsonNode.Parse(dataJson);
        }
        catch
        {
            return false;
        }

        if (data is null)
        {
            return false;
        }

        attachment = new QueryStructuredOutputAttachment(data);
        return true;
    }

    public static bool TryGetOutputTokenUsageAttachment(
        ChatMessage? message,
        out QueryOutputTokenUsageAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "output_token_usage", StringComparison.Ordinal) ||
            !int.TryParse(GetMetadataValue(block, "turn"), out var turn) ||
            !int.TryParse(GetMetadataValue(block, "session"), out var session))
        {
            return false;
        }

        var budgetValue = GetMetadataValue(block, "budget");
        int? budget = null;
        if (!string.IsNullOrWhiteSpace(budgetValue))
        {
            if (!int.TryParse(budgetValue, out var parsedBudget))
            {
                return false;
            }

            budget = parsedBudget;
        }

        attachment = new QueryOutputTokenUsageAttachment(turn, session, budget);
        return true;
    }

    public static bool TryGetHookSystemMessageAttachment(
        ChatMessage? message,
        out QueryHookSystemMessageAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_system_message", StringComparison.Ordinal))
        {
            return false;
        }

        var content = GetMetadataValue(block, "content");
        var hookName = GetMetadataValue(block, "hookName");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if (string.IsNullOrWhiteSpace(content) ||
            string.IsNullOrWhiteSpace(hookName) ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent))
        {
            return false;
        }

        attachment = new QueryHookSystemMessageAttachment(
            content,
            hookName,
            toolUseId,
            hookEvent);
        return true;
    }

    public static bool TryGetHookPermissionDecisionAttachment(
        ChatMessage? message,
        out QueryHookPermissionDecisionAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_permission_decision", StringComparison.Ordinal))
        {
            return false;
        }

        var decision = GetMetadataValue(block, "decision");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if ((decision is not "allow" and not "deny") ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent))
        {
            return false;
        }

        attachment = new QueryHookPermissionDecisionAttachment(
            decision,
            toolUseId,
            hookEvent);
        return true;
    }

    public static bool TryGetHookAdditionalContextAttachment(
        ChatMessage? message,
        out QueryHookAdditionalContextAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_additional_context", StringComparison.Ordinal))
        {
            return false;
        }

        var contentJson = GetMetadataValue(block, "content");
        var hookName = GetMetadataValue(block, "hookName");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if (string.IsNullOrWhiteSpace(contentJson) ||
            string.IsNullOrWhiteSpace(hookName) ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent))
        {
            return false;
        }

        IReadOnlyList<string>? content;
        try
        {
            content = JsonSerializer.Deserialize<string[]>(contentJson);
        }
        catch
        {
            return false;
        }

        if (content is null)
        {
            return false;
        }

        attachment = new QueryHookAdditionalContextAttachment(
            content,
            hookName,
            toolUseId,
            hookEvent);
        return true;
    }

    public static bool TryGetHookBlockingErrorAttachment(
        ChatMessage? message,
        out QueryHookBlockingErrorAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_blocking_error", StringComparison.Ordinal))
        {
            return false;
        }

        var blockingErrorJson = GetMetadataValue(block, "blockingError");
        var hookName = GetMetadataValue(block, "hookName");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if (string.IsNullOrWhiteSpace(blockingErrorJson) ||
            string.IsNullOrWhiteSpace(hookName) ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent))
        {
            return false;
        }

        HookBlockingError? blockingError;
        try
        {
            blockingError = JsonSerializer.Deserialize<HookBlockingError>(blockingErrorJson);
        }
        catch
        {
            return false;
        }

        if (blockingError is null ||
            string.IsNullOrWhiteSpace(blockingError.BlockingError) ||
            string.IsNullOrWhiteSpace(blockingError.Command))
        {
            return false;
        }

        attachment = new QueryHookBlockingErrorAttachment(
            blockingError,
            hookName,
            toolUseId,
            hookEvent);
        return true;
    }

    public static bool TryGetHookSuccessAttachment(
        ChatMessage? message,
        out QueryHookSuccessAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_success", StringComparison.Ordinal))
        {
            return false;
        }

        var content = GetMetadataValue(block, "content");
        var hookName = GetMetadataValue(block, "hookName");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if (content is null ||
            string.IsNullOrWhiteSpace(hookName) ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent))
        {
            return false;
        }

        int? exitCode = null;
        var exitCodeValue = GetMetadataValue(block, "exitCode");
        if (!string.IsNullOrWhiteSpace(exitCodeValue))
        {
            if (!int.TryParse(exitCodeValue, out var parsedExitCode))
            {
                return false;
            }

            exitCode = parsedExitCode;
        }

        int? durationMs = null;
        var durationMsValue = GetMetadataValue(block, "durationMs");
        if (!string.IsNullOrWhiteSpace(durationMsValue))
        {
            if (!int.TryParse(durationMsValue, out var parsedDurationMs))
            {
                return false;
            }

            durationMs = parsedDurationMs;
        }

        attachment = new QueryHookSuccessAttachment(
            content,
            hookName,
            toolUseId,
            hookEvent,
            GetMetadataValue(block, "stdout"),
            GetMetadataValue(block, "stderr"),
            exitCode,
            GetMetadataValue(block, "command"),
            durationMs);
        return true;
    }

    public static bool TryGetHookCancelledAttachment(
        ChatMessage? message,
        out QueryHookCancelledAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_cancelled", StringComparison.Ordinal))
        {
            return false;
        }

        var hookName = GetMetadataValue(block, "hookName");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if (string.IsNullOrWhiteSpace(hookName) ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent))
        {
            return false;
        }

        int? durationMs = null;
        var durationMsValue = GetMetadataValue(block, "durationMs");
        if (!string.IsNullOrWhiteSpace(durationMsValue))
        {
            if (!int.TryParse(durationMsValue, out var parsedDurationMs))
            {
                return false;
            }

            durationMs = parsedDurationMs;
        }

        attachment = new QueryHookCancelledAttachment(
            hookName,
            toolUseId,
            hookEvent,
            GetMetadataValue(block, "command"),
            durationMs);
        return true;
    }

    public static bool TryGetHookErrorDuringExecutionAttachment(
        ChatMessage? message,
        out QueryHookErrorDuringExecutionAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_error_during_execution", StringComparison.Ordinal))
        {
            return false;
        }

        var content = GetMetadataValue(block, "content");
        var hookName = GetMetadataValue(block, "hookName");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if (string.IsNullOrWhiteSpace(content) ||
            string.IsNullOrWhiteSpace(hookName) ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent))
        {
            return false;
        }

        int? durationMs = null;
        var durationMsValue = GetMetadataValue(block, "durationMs");
        if (!string.IsNullOrWhiteSpace(durationMsValue))
        {
            if (!int.TryParse(durationMsValue, out var parsedDurationMs))
            {
                return false;
            }

            durationMs = parsedDurationMs;
        }

        attachment = new QueryHookErrorDuringExecutionAttachment(
            content,
            hookName,
            toolUseId,
            hookEvent,
            GetMetadataValue(block, "command"),
            durationMs);
        return true;
    }

    public static bool TryGetHookNonBlockingErrorAttachment(
        ChatMessage? message,
        out QueryHookNonBlockingErrorAttachment? attachment)
    {
        attachment = null;
        if (message?.ContentBlocks.Count != 1)
        {
            return false;
        }

        var block = message.ContentBlocks[0];
        if (block.Kind != MessageContentKind.Attachment ||
            block.Metadata is null ||
            !string.Equals(GetMetadataValue(block, "attachmentType"), "hook_non_blocking_error", StringComparison.Ordinal))
        {
            return false;
        }

        var hookName = GetMetadataValue(block, "hookName");
        var stderr = GetMetadataValue(block, "stderr");
        var stdout = GetMetadataValue(block, "stdout");
        var toolUseId = GetMetadataValue(block, "toolUseID");
        var hookEventValue = GetMetadataValue(block, "hookEvent");

        if (string.IsNullOrWhiteSpace(hookName) ||
            stderr is null ||
            stdout is null ||
            string.IsNullOrWhiteSpace(toolUseId) ||
            !Enum.TryParse<HookEvent>(hookEventValue, ignoreCase: false, out var hookEvent) ||
            !int.TryParse(GetMetadataValue(block, "exitCode"), out var exitCode))
        {
            return false;
        }

        int? durationMs = null;
        var durationMsValue = GetMetadataValue(block, "durationMs");
        if (!string.IsNullOrWhiteSpace(durationMsValue))
        {
            if (!int.TryParse(durationMsValue, out var parsedDurationMs))
            {
                return false;
            }

            durationMs = parsedDurationMs;
        }

        attachment = new QueryHookNonBlockingErrorAttachment(
            hookName,
            stderr,
            stdout,
            exitCode,
            toolUseId,
            hookEvent,
            GetMetadataValue(block, "command"),
            durationMs);
        return true;
    }

    private static string? GetMetadataValue(MessageContentBlock block, string key)
    {
        return block.Metadata is not null && block.Metadata.TryGetValue(key, out var value)
            ? value
            : null;
    }
}
