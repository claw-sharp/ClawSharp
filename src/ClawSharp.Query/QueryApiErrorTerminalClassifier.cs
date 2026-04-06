// TS origin: ./query.ts, ./services/api/errors.ts
// TS parity status: ports the current TypeScript terminal classification for streamed assistant API-error messages that end the loop without further recovery; the broader collapse-drain, reactive-compact, and stop-hook failure side effects remain blocked.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryApiErrorTerminalClassifier
{
    public const string PromptTooLongErrorMessage = "Prompt is too long";

    public static QueryLoopTerminal? Classify(ChatMessage? message, QueryLoopState? priorState = null)
    {
        if (!IsAssistantApiErrorMessage(message))
        {
            return null;
        }

        if (IsPromptTooLongMessage(message!))
        {
            if (priorState?.Transition?.Reason == QueryContinueReason.CollapseDrainRetry)
            {
                return new QueryLoopTerminal(QueryTerminalReason.BlockingLimit, ErrorMessage: message!.Content);
            }

            return new QueryLoopTerminal(QueryTerminalReason.PromptTooLong, ErrorMessage: message!.Content);
        }

        if (IsMediaSizeErrorMessage(message!))
        {
            return new QueryLoopTerminal(QueryTerminalReason.ImageError, ErrorMessage: message!.Content);
        }

        return null;
    }

    private static bool IsAssistantApiErrorMessage(ChatMessage? message)
    {
        if (message?.Role != MessageRole.Assistant)
        {
            return false;
        }

        var block = message.ContentBlocks.FirstOrDefault();
        return block?.Metadata is not null &&
               block.Metadata.TryGetValue("isApiErrorMessage", out var isApiError) &&
               string.Equals(isApiError, bool.TrueString, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPromptTooLongMessage(ChatMessage message)
    {
        return message.ContentBlocks.Any(
            block => block.Kind == MessageContentKind.Text &&
                     block.Value.StartsWith(PromptTooLongErrorMessage, StringComparison.Ordinal));
    }

    private static bool IsMediaSizeErrorMessage(ChatMessage message)
    {
        var block = message.ContentBlocks.FirstOrDefault();
        var rawErrorDetails = block?.Metadata is not null &&
                              block.Metadata.TryGetValue("errorDetails", out var errorDetails)
            ? errorDetails
            : null;

        if (string.IsNullOrWhiteSpace(rawErrorDetails))
        {
            return false;
        }

        return (rawErrorDetails.Contains("image exceeds", StringComparison.Ordinal) &&
                rawErrorDetails.Contains("maximum", StringComparison.Ordinal)) ||
               (rawErrorDetails.Contains("image dimensions exceed", StringComparison.Ordinal) &&
                rawErrorDetails.Contains("many-image", StringComparison.Ordinal)) ||
               System.Text.RegularExpressions.Regex.IsMatch(rawErrorDetails, @"maximum of \d+ PDF pages");
    }
}
