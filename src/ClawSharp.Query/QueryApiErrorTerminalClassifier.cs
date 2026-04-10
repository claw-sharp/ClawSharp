// TS parity status: ports the current TypeScript terminal classification for streamed assistant API-error messages that end the loop without further recovery; the broader collapse-drain, reactive-compact, and stop-hook failure side effects remain blocked.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryApiErrorTerminalClassifier
{
    public const string PromptTooLongErrorMessage = "Prompt is too long";
    public const string InputExceedsContextWindowErrorMessage = "input exceeds the context window";

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
        var blockText = string.Join(
            "\n",
            message.ContentBlocks
                .Where(block => block.Kind == MessageContentKind.Text)
                .Select(block => block.Value));

        return ContainsPromptOverflowIndicator(blockText) ||
               ContainsPromptOverflowIndicator(GetErrorDetails(message));
    }

    private static bool IsMediaSizeErrorMessage(ChatMessage message)
    {
        var rawErrorDetails = GetErrorDetails(message);

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

    private static string? GetErrorDetails(ChatMessage message)
    {
        var block = message.ContentBlocks.FirstOrDefault();
        return block?.Metadata is not null &&
               block.Metadata.TryGetValue("errorDetails", out var errorDetails)
            ? errorDetails
            : null;
    }

    private static bool ContainsPromptOverflowIndicator(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return text.Contains(PromptTooLongErrorMessage, StringComparison.OrdinalIgnoreCase) ||
               text.Contains(InputExceedsContextWindowErrorMessage, StringComparison.OrdinalIgnoreCase) ||
               (text.Contains("context window", StringComparison.OrdinalIgnoreCase) &&
                (text.Contains("exceed", StringComparison.OrdinalIgnoreCase) ||
                 text.Contains("limit", StringComparison.OrdinalIgnoreCase)));
    }
}
