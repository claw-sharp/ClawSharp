// TS parity status: ports the current TypeScript compact-summary user-message contract by formatting the summary text, appending transcript and preserved-tail hints, and preserving the compact-summary transcript-only metadata on the emitted query message.
using ClawSharp.Core;

namespace ClawSharp.Query;

public static class QueryCompactSummaryMessageFactory
{
    public static ChatMessage Create(
        string summary,
        bool suppressFollowUpQuestions = false,
        string? transcriptPath = null,
        bool recentMessagesPreserved = false)
    {
        var formattedSummary = QueryCompactSummaryFormatter.Format(summary);

        var content =
            "This session is being continued from a previous conversation that ran out of context. The summary below covers the earlier portion of the conversation." +
            $"{Environment.NewLine}{Environment.NewLine}" +
            formattedSummary;

        if (!string.IsNullOrWhiteSpace(transcriptPath))
        {
            content +=
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"If you need specific details from before compaction (like exact code snippets, error messages, or content you generated), read the full transcript at: {transcriptPath}";
        }

        if (recentMessagesPreserved)
        {
            content += $"{Environment.NewLine}{Environment.NewLine}Recent messages are preserved verbatim.";
        }

        if (suppressFollowUpQuestions)
        {
            content +=
                $"{Environment.NewLine}" +
                "Continue the conversation from where it left off without asking the user any further questions. Resume directly - do not acknowledge the summary, do not recap what was happening, do not preface with \"I'll continue\" or similar. Pick up the last task as if the break never happened.";
        }

        return new ChatMessage(
            Guid.NewGuid().ToString("N"),
            MessageRole.User,
            [
                new MessageContentBlock(
                    MessageContentKind.Text,
                    content,
                    Metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["isCompactSummary"] = true.ToString(),
                        ["isVisibleInTranscriptOnly"] = true.ToString(),
                        ["isMeta"] = false.ToString()
                    })
            ],
            DateTimeOffset.UtcNow);
    }
}
