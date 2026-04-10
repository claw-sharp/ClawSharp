using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class QueryAutoCompactRunner : IQueryAutoCompactRunner
{
    public const string AutoCompactStatusMessage = "Compacting conversation to stay within the model's context window...";
    public const int CompactSummaryOutputReserve = 20_000;
    public const int AutoCompactBufferTokens = 13_000;
    public const int MaxConsecutiveAutoCompactFailures = 3;
    public const int DefaultContextWindowTokens = 200_000;
    public const int OneMillionContextWindowTokens = 1_000_000;

    private readonly IQueryCompactionTokenEstimator _tokenEstimator;
    private readonly IQueryReactiveCompactExecutor _executor;

    public QueryAutoCompactRunner(
        IQueryCompactionTokenEstimator? tokenEstimator = null,
        IQueryReactiveCompactExecutor? executor = null)
    {
        _tokenEstimator = tokenEstimator ?? new ApproximateQueryCompactionTokenEstimator();
        _executor = executor ?? new NoOpQueryReactiveCompactExecutor();
    }

    public async Task<QueryAutoCompactResult> TryCompactAsync(
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        Func<QueryRuntimeEvent, CancellationToken, Task> emitEvent,
        CancellationToken cancellationToken = default)
    {
        var tracking = AdvanceTracking(state.AutoCompactTracking);
        var stateWithTracking = state with { AutoCompactTracking = tracking };

        if (!IsAutoCompactEnabled() ||
            IsCompactionQuery(request) ||
            HasCircuitBreakerTripped(tracking))
        {
            return new QueryAutoCompactResult(stateWithTracking, Compacted: false);
        }

        var model = ResolveModel(stateWithTracking, settings);
        var messagesToCompact = QueryCompactBoundaryHelpers.GetMessagesAfterCompactBoundary(stateWithTracking.Messages);
        if (messagesToCompact.Count == 0)
        {
            return new QueryAutoCompactResult(stateWithTracking, Compacted: false);
        }

        var estimatedTokens = _tokenEstimator.Estimate(messagesToCompact);
        if (estimatedTokens < GetAutoCompactThreshold(model))
        {
            return new QueryAutoCompactResult(stateWithTracking, Compacted: false);
        }

        await emitEvent(
            new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateSystemMessage(AutoCompactStatusMessage, "info")),
            cancellationToken);

        var syntheticTerminal = new QueryTerminalIterationResult(
            new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
            stateWithTracking);
        var compacted = await _executor.TryReactiveCompactAsync(
            request,
            stateWithTracking,
            syntheticTerminal,
            session,
            settings,
            cancellationToken,
            trigger: "auto");

        if (compacted is null)
        {
            return new QueryAutoCompactResult(
                stateWithTracking with
                {
                    AutoCompactTracking = tracking is null
                        ? new QueryAutoCompactTrackingState(
                            Compacted: false,
                            TurnCounter: 0,
                            TurnId: string.Empty,
                            ConsecutiveFailures: 1)
                        : tracking with
                        {
                            ConsecutiveFailures = (tracking.ConsecutiveFailures ?? 0) + 1
                        }
                },
                Compacted: false);
        }

        var postCompactMessages = QueryPostCompactMessageBuilder.BuildPostCompactMessages(compacted);
        foreach (var message in postCompactMessages)
        {
            await emitEvent(new QueryMessageRuntimeEvent(message), cancellationToken);
        }

        return new QueryAutoCompactResult(
            stateWithTracking with
            {
                Messages = postCompactMessages,
                AutoCompactTracking = new QueryAutoCompactTrackingState(
                    Compacted: true,
                    TurnCounter: 0,
                    TurnId: Guid.NewGuid().ToString("N"),
                    ConsecutiveFailures: 0)
            },
            Compacted: true);
    }

    public static bool IsAutoCompactEnabled()
    {
        return !IsTruthy(Environment.GetEnvironmentVariable("DISABLE_COMPACT")) &&
               !IsTruthy(Environment.GetEnvironmentVariable("DISABLE_AUTO_COMPACT"));
    }

    private static bool IsCompactionQuery(QueryTurnRequest request)
    {
        var querySource = request.ModelTurnContext?.QuerySource;
        return string.Equals(querySource, "compact", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(querySource, "session_memory", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCircuitBreakerTripped(QueryAutoCompactTrackingState? tracking)
    {
        return tracking?.ConsecutiveFailures is >= MaxConsecutiveAutoCompactFailures;
    }

    private static QueryAutoCompactTrackingState? AdvanceTracking(QueryAutoCompactTrackingState? tracking)
    {
        return tracking?.Compacted == true
            ? tracking with { TurnCounter = tracking.TurnCounter + 1 }
            : tracking;
    }

    private static string ResolveModel(QueryLoopState state, ClawSharpSettings settings)
    {
        return MainLoopModelResolver.Resolve(
            state.ToolUseContext.MainLoopModel,
            settings.Runtime.Model);
    }

    public static int GetAutoCompactThreshold(string model)
    {
        return GetEffectiveContextWindowSize(model) - AutoCompactBufferTokens;
    }

    public static int GetEffectiveContextWindowSize(string model)
    {
        var reservedTokens = Math.Min(
            QueryMaxOutputTokensResolver.GetMaxOutputTokensForModel(model),
            CompactSummaryOutputReserve);
        return GetContextWindowForModel(model) - reservedTokens;
    }

    public static int GetContextWindowForModel(string model)
    {
        var maxContextOverride = Environment.GetEnvironmentVariable("CLAUDE_CODE_MAX_CONTEXT_TOKENS");
        if (int.TryParse(maxContextOverride, out var parsedOverride) && parsedOverride > 0)
        {
            return parsedOverride;
        }

        var resolvedModel = MainLoopModelResolver.Resolve(model);
        if (resolvedModel.Contains("[1m]", StringComparison.OrdinalIgnoreCase) ||
            resolvedModel.Contains("sonnet-4-6", StringComparison.OrdinalIgnoreCase) ||
            resolvedModel.Contains("opus-4-6", StringComparison.OrdinalIgnoreCase))
        {
            return OneMillionContextWindowTokens;
        }

        return DefaultContextWindowTokens;
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
