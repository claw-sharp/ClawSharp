// TS parity status: ports the reactive-compact runtime orchestration layer beneath the C# overflow recovery path by composing prompt-build, pre-compact hook, summary-generation, and post-compact attachment seams; the concrete compaction runtime remains delegated to later ports behind those seams.
// TS baseline note: the local ./services/compact/reactiveCompact.ts is currently only an auto-generated stub in this checkout, so this C# slice is intentionally baselined on the shared behavior visible in ./services/compact/compact.ts and ./services/compact/prompt.ts until the real reactive runtime source is available.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class QueryReactiveCompactExecutor : IQueryReactiveCompactExecutor
{
    private readonly IQueryCompactionTokenEstimator _tokenEstimator;
    private readonly IQueryCompactMaxTokensResolver _maxTokensResolver;
    private readonly IQueryReactiveCompactToolCatalog _toolCatalog;
    private readonly IQueryReactiveCompactPromptBuilder _promptBuilder;
    private readonly IQueryReactiveCompactHookRunner _hookRunner;
    private readonly IQueryReactiveCompactModelCallRunner _modelCallRunner;
    private readonly IQueryReactiveCompactPostCompactAttachmentBuilder _postCompactAttachmentBuilder;

    public QueryReactiveCompactExecutor(
        IQueryCompactionTokenEstimator? tokenEstimator = null,
        IQueryCompactMaxTokensResolver? maxTokensResolver = null,
        IQueryReactiveCompactToolCatalog? toolCatalog = null,
        IQueryReactiveCompactPromptBuilder? promptBuilder = null,
        IQueryReactiveCompactHookRunner? hookRunner = null,
        IQueryReactiveCompactModelCallRunner? modelCallRunner = null,
        IQueryReactiveCompactPostCompactAttachmentBuilder? postCompactAttachmentBuilder = null)
    {
        _tokenEstimator = tokenEstimator ?? new ApproximateQueryCompactionTokenEstimator();
        _maxTokensResolver = maxTokensResolver ?? new ConservativeQueryCompactMaxTokensResolver();
        _toolCatalog = toolCatalog ?? new NoOpQueryReactiveCompactToolCatalog();
        _promptBuilder = promptBuilder ?? new QueryReactiveCompactPromptBuilder();
        _hookRunner = hookRunner ?? new NoOpQueryReactiveCompactHookRunner();
        _modelCallRunner = modelCallRunner ?? new NoOpQueryReactiveCompactModelCallRunner();
        _postCompactAttachmentBuilder = postCompactAttachmentBuilder ?? new NoOpQueryReactiveCompactPostCompactAttachmentBuilder();
    }

    public async Task<QueryCompactionResult?> TryReactiveCompactAsync(
        QueryTurnRequest request,
        QueryLoopState priorState,
        QueryTerminalIterationResult terminalResult,
        ConversationSession session,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default)
    {
        var context = new QueryReactiveCompactExecutionContext(
            request,
            priorState,
            terminalResult,
            session,
            settings,
            _toolCatalog.GetTools(request, priorState, session, settings));

        var hookResult = await _hookRunner.RunPreCompactAsync(
            context,
            cancellationToken);

        var prompt = await _promptBuilder.BuildAsync(context, hookResult, cancellationToken);
        if (prompt is null)
        {
            return null;
        }

        context = context with
        {
            EstimatedPreCompactTokenCount = _tokenEstimator.Estimate(prompt.MessagesToCompact),
            ResolvedMaxTokens = _maxTokensResolver.Resolve(request, priorState, session, settings)
        };

        var compacted = await _modelCallRunner.TryCompactAsync(
            context,
            prompt,
            hookResult,
            cancellationToken);
        if (compacted is null)
        {
            return null;
        }

        var postCompactHookResult =
            !string.IsNullOrWhiteSpace(compacted.RawSummary)
                ? await _hookRunner.RunPostCompactAsync(
                    context,
                    compacted.RawSummary,
                    cancellationToken)
                : QueryReactiveCompactPostCompactHookRunResult.Empty;

        var attachments = await _postCompactAttachmentBuilder.BuildAsync(
            context,
            prompt,
            hookResult,
            compacted,
            cancellationToken);

        var combinedUserDisplayMessage = string.Join(
                "\n",
                new[]
                {
                    hookResult.UserDisplayMessage,
                    postCompactHookResult.UserDisplayMessage
                }.Where(message => !string.IsNullOrWhiteSpace(message)))
            .Trim();

        return compacted with
        {
            Attachments = compacted.Attachments.Concat(attachments).ToArray(),
            HookResults = compacted.HookResults.Concat(hookResult.HookResults ?? []).ToArray(),
            UserDisplayMessage = string.IsNullOrWhiteSpace(combinedUserDisplayMessage)
                ? compacted.UserDisplayMessage
                : combinedUserDisplayMessage
        };
    }
}
