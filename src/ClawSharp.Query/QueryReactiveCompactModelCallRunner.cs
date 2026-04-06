// TS parity status: ports the first live reactive-compact summary-generation call beneath the C# overflow recovery path by shaping the shared compact request, executing it through the existing model-call executor, and turning the completed assistant text into a compact boundary plus transcript-only summary message; the richer reactive-only source behavior, post-compact hooks, and attachment restoration still remain delegated to later ports.
// TS baseline note: because ./services/compact/reactiveCompact.ts is still a stub in this checkout, this runner follows the approved fallback baseline from ./services/compact/compact.ts plus ./services/compact/prompt.ts, including the conservative partial-compaction-style summary message semantics.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class QueryReactiveCompactModelCallRunner : IQueryReactiveCompactModelCallRunner
{
    private readonly IQueryModelCallExecutor _modelCallExecutor;

    public QueryReactiveCompactModelCallRunner(IQueryModelCallExecutor? modelCallExecutor = null)
    {
        _modelCallExecutor = modelCallExecutor ?? new NotImplementedQueryModelCallExecutor();
    }

    public async Task<QueryCompactionResult?> TryCompactAsync(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactPromptBuildResult prompt,
        QueryReactiveCompactHookRunResult hookResult,
        CancellationToken cancellationToken = default)
    {
        if (context.EstimatedPreCompactTokenCount is null || context.ResolvedMaxTokens is null)
        {
            return null;
        }

        var compactMessages = prompt.MessagesToCompact
            .Concat([ChatMessageFactory.CreateUserMessage(prompt.SummaryPrompt)])
            .ToArray();
        var compactState = QueryLoopStateFactory.CreateInitial(
            compactMessages,
            context.PriorState.ToolUseContext);
        var streamingRequest = BuildStreamingRequest(
            context,
            prompt,
            compactMessages);

        QueryModelCallAttemptResult? attemptResult = null;
        await foreach (var update in _modelCallExecutor.StreamAsync(
                           streamingRequest,
                           context.Request,
                           compactState,
                           context.Session,
                           context.Settings,
                           cancellationToken))
        {
            if (update.AttemptResult is not null)
            {
                attemptResult = update.AttemptResult;
            }
        }

        if (attemptResult?.Outcome != QueryModelCallAttemptOutcome.Completed ||
            attemptResult.IterationResult is not QueryTerminalIterationResult terminalResult)
        {
            return null;
        }

        var assistantMessage = TryGetLastAssistantMessage(terminalResult.State.Messages);
        if (assistantMessage is null || IsApiErrorMessage(assistantMessage))
        {
            return null;
        }

        var summary = TryGetAssistantText(assistantMessage);
        if (string.IsNullOrWhiteSpace(summary))
        {
            return null;
        }

        var boundaryMarker = ChatMessageFactory.CreateCompactBoundaryMessage(
            trigger: "manual",
            preTokens: context.EstimatedPreCompactTokenCount.Value,
            lastPreCompactMessageUuid: prompt.MessagesToCompact.LastOrDefault()?.Id,
            userContext: null,
            messagesSummarized: prompt.MessagesToCompact.Count);
        var summaryMessage = QueryCompactSummaryMessageFactory.Create(
            summary,
            suppressFollowUpQuestions: false,
            transcriptPath: context.Session.TranscriptPath,
            recentMessagesPreserved: false);

        return new QueryCompactionResult(
            boundaryMarker,
            [summaryMessage],
            [],
            [],
            prompt.MessagesToKeep,
            RawSummary: summary,
            PreCompactTokenCount: context.EstimatedPreCompactTokenCount,
            PostCompactTokenCount: attemptResult.TurnOutputTokens);
    }

    private static QueryModelHttpStreamingRequest BuildStreamingRequest(
        QueryReactiveCompactExecutionContext context,
        QueryReactiveCompactPromptBuildResult prompt,
        IReadOnlyList<ChatMessage> compactMessages)
    {
        var model =
            MainLoopModelResolver.Resolve(
                context.PriorState.ToolUseContext.MainLoopModel,
                context.Settings.Runtime.Model);

        var request = new QueryModelRequest(
            context.Request.SessionId,
            model,
            QueryRequestBuilder.BuildSystemPromptBlocks(
                prompt.SystemPrompt,
                enablePromptCaching: false,
                useGlobalCacheScope: false),
            QueryRequestBuilder.AddCacheBreakpoints(
                compactMessages,
                enablePromptCaching: false),
            context.Tools,
            new QueryRequestOutputConfig(),
            [],
            context.ResolvedMaxTokens,
            new QueryThinkingConfig("disabled"));

        return new QueryModelHttpStreamingRequest(request, "compact");
    }

    private static ChatMessage? TryGetLastAssistantMessage(IReadOnlyList<ChatMessage> messages)
    {
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            if (messages[index].Role == MessageRole.Assistant)
            {
                return messages[index];
            }
        }

        return null;
    }

    private static string? TryGetAssistantText(ChatMessage message)
    {
        var text = string.Join(
            "\n",
            message.ContentBlocks
                .Where(block => block.Kind == MessageContentKind.Text)
                .Select(block => block.Value)
                .Where(content => !string.IsNullOrWhiteSpace(content)))
            .Trim();

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool IsApiErrorMessage(ChatMessage message)
    {
        return message.ContentBlocks.Any(
            block =>
                block.Metadata is not null &&
                block.Metadata.TryGetValue("isApiErrorMessage", out var isApiError) &&
                string.Equals(isApiError, bool.TrueString, StringComparison.OrdinalIgnoreCase));
    }
}
