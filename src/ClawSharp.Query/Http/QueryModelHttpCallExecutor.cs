// TS parity status: ports the HTTP-backed model-call executor seam under the model-backed iteration path, including the direct TypeScript retry/fallback branches that can be represented in the current C# runtime; auth refresh and some provider-specific recovery branches remain delegated or unported.
using System.Net;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed class QueryModelHttpCallExecutor : IQueryModelCallExecutor
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);
    private const int MaxOverloadedRetriesBeforeFallback = 3;
    private static readonly TimeSpan OverloadedRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly HashSet<string> ForegroundOverloadedRetrySources = new(StringComparer.Ordinal)
    {
        "repl_main_thread",
        "repl_main_thread:outputStyle:custom",
        "repl_main_thread:outputStyle:Explanatory",
        "repl_main_thread:outputStyle:Learning",
        "sdk",
        "agent:custom",
        "agent:default",
        "agent:builtin",
        "compact",
        "hook_agent",
        "hook_prompt",
        "verification_agent",
        "side_question",
        "auto_mode",
        "bash_classifier"
    };

    private readonly IQueryModelHttpClientConfigProvider _configProvider;
    private readonly IQueryModelHttpStreamingClient _streamingClient;
    private readonly IQueryModelStreamUpdateParser _streamUpdateParser;
    private readonly IQueryAuthAccountStateProvider _authAccountStateProvider;
    private readonly IQueryAuthFailureRecoveryRunner _authFailureRecoveryRunner;

    public QueryModelHttpCallExecutor(
        IQueryModelHttpClientConfigProvider configProvider,
        IQueryModelHttpStreamingClient streamingClient,
        IQueryModelStreamUpdateParser streamUpdateParser,
        IQueryAuthAccountStateProvider? authAccountStateProvider = null,
        IQueryAuthFailureRecoveryRunner? authFailureRecoveryRunner = null)
    {
        _configProvider = configProvider;
        _streamingClient = streamingClient;
        _streamUpdateParser = streamUpdateParser;
        _authAccountStateProvider = authAccountStateProvider ?? new UnknownQueryAuthAccountStateProvider();
        _authFailureRecoveryRunner = authFailureRecoveryRunner ?? new NoOpQueryAuthFailureRecoveryRunner();
    }

    public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
        QueryModelHttpStreamingRequest streamingRequest,
        QueryTurnRequest request,
        QueryLoopState state,
        ConversationSession session,
        ClawSharpSettings settings,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        QueryModelHttpClientConfig? clientConfig = null;
        QueryAuthAccountState accountState = QueryAuthAccountState.None;
        var maxRetries = QueryModelTransportRetryPolicy.GetMaxRetries();
        var overloadedAttempts = 0;
        var retryAttempt = 0;
        var persistentAttempt = 0;
        var currentStreamingRequest = streamingRequest;
        var querySource = currentStreamingRequest.QuerySource;
        var refreshClientConfig = true;

        while (true)
        {
            retryAttempt++;
            if (refreshClientConfig || clientConfig is null)
            {
                clientConfig = _configProvider.GetConfig(request, state, session, settings);
                accountState = _authAccountStateProvider.GetState(request, state, session, settings);
                refreshClientConfig = false;
                ClawSharpTelemetry.LogDebug(
                    $"[QueryModelHttpCallExecutor] config sessionId={session.Id} provider={clientConfig.ProviderKind} transport={clientConfig.TransportKind} baseUrl={clientConfig.BaseUrl} model={currentStreamingRequest.Request.Model} apiKeyPresent={!string.IsNullOrWhiteSpace(clientConfig.ApiKey)} authTokenPresent={!string.IsNullOrWhiteSpace(clientConfig.AuthToken)}");
            }

            _streamUpdateParser.Reset();
            var shouldRetryOverloaded = false;
            var shouldEmitFallback = false;
            var shouldRetryTransport = false;
            var usePersistentRetry = false;
            var shouldRunAuthFailureRecovery = false;
            TimeSpan transportRetryDelay = TimeSpan.Zero;
            QueryModelOverloadedException? overloadedException = null;
            QueryModelApiException? apiException = null;
            HttpRequestException? connectionException = null;
            ClawSharpTelemetry.LogDebug(
                $"[QueryModelHttpCallExecutor] stream-attempt sessionId={session.Id} retryAttempt={retryAttempt} persistentAttempt={persistentAttempt} model={currentStreamingRequest.Request.Model}");
            await using var payloads = _streamingClient.StreamAsync(
                clientConfig,
                currentStreamingRequest,
                cancellationToken).GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                JsonNode payload;
                try
                {
                    if (!await payloads.MoveNextAsync())
                    {
                        ClawSharpTelemetry.LogDebug(
                            $"[QueryModelHttpCallExecutor] stream-complete sessionId={session.Id} retryAttempt={retryAttempt}");
                        break;
                    }

                    payload = payloads.Current;
                    ClawSharpTelemetry.LogDebug(
                        $"[QueryModelHttpCallExecutor] payload sessionId={session.Id} kind={payload?["type"]?.ToString() ?? "unknown"}");
                }
                catch (QueryModelOverloadedException exception)
                    when (ShouldHandleOverloadedRetry(querySource))
                {
                    overloadedAttempts++;
                    overloadedException = exception;
                    if (overloadedAttempts >= MaxOverloadedRetriesBeforeFallback &&
                        !string.IsNullOrWhiteSpace(request.FallbackModel))
                    {
                        shouldEmitFallback = true;
                        break;
                    }

                    usePersistentRetry = QueryModelTransportRetryPolicy.IsPersistentRetryEnabled();
                    shouldRetryOverloaded = usePersistentRetry || retryAttempt <= maxRetries;
                    transportRetryDelay = usePersistentRetry
                        ? QueryModelTransportRetryPolicy.GetPersistentRetryDelay(++persistentAttempt, exception)
                        : OverloadedRetryDelay;
                    break;
                }
                catch (QueryModelApiException exception)
                    when (exception is not QueryModelOverloadedException &&
                          QueryModelTransportRetryPolicy.ShouldRetry(exception, accountState) &&
                          (QueryModelTransportRetryPolicy.IsPersistentRetryEnabled() || retryAttempt <= maxRetries))
                {
                    apiException = exception;
                    shouldRetryTransport = true;
                    refreshClientConfig = ShouldRefreshClientConfigOnRetry(exception);
                    shouldRunAuthFailureRecovery = refreshClientConfig;
                    usePersistentRetry = QueryModelTransportRetryPolicy.IsPersistentRetryEnabled() &&
                                         QueryModelTransportRetryPolicy.IsTransientCapacityError(exception);
                    transportRetryDelay = usePersistentRetry
                        ? QueryModelTransportRetryPolicy.GetPersistentRetryDelay(++persistentAttempt, exception)
                        : QueryModelTransportRetryPolicy.GetRetryDelay(
                            retryAttempt,
                            exception.GetHeaderValue("retry-after"));
                    break;
                }
                catch (QueryModelApiException exception)
                    when (exception is not QueryModelOverloadedException &&
                          TryApplyMaxTokensOverflowRetry(exception, currentStreamingRequest, out var adjustedStreamingRequest))
                {
                    currentStreamingRequest = adjustedStreamingRequest;
                    shouldRetryTransport = true;
                    transportRetryDelay = TimeSpan.Zero;
                    break;
                }
                catch (HttpRequestException exception)
                    when (QueryModelTransportRetryPolicy.ShouldRetry(exception) &&
                          retryAttempt <= maxRetries)
                {
                    connectionException = exception;
                    shouldRetryTransport = true;
                    refreshClientConfig = IsStaleConnectionError(exception);
                    transportRetryDelay = QueryModelTransportRetryPolicy.GetRetryDelay(
                        retryAttempt,
                        retryAfterHeader: null);
                    break;
                }
                catch (Exception exception)
                {
                    ClawSharpTelemetry.LogDebug(
                        $"[QueryModelHttpCallExecutor] stream-failed sessionId={session.Id} error={exception.GetType().Name}: {exception.Message}",
                        DebugLogLevel.Error);
                    throw;
                }

                foreach (var update in _streamUpdateParser.Parse(payload))
                {
                    if (update.RuntimeEvent is not null)
                    {
                        ClawSharpTelemetry.LogDebug(
                            $"[QueryModelHttpCallExecutor] parsed-runtime-event sessionId={session.Id} type={update.RuntimeEvent.GetType().Name}");
                    }

                    if (update.AttemptResult is not null)
                    {
                        ClawSharpTelemetry.LogDebug(
                            $"[QueryModelHttpCallExecutor] parsed-attempt-result sessionId={session.Id} outcome={update.AttemptResult.Outcome}");
                    }

                    yield return update;
                }
            }

            if (shouldRetryOverloaded)
            {
                if (usePersistentRetry)
                {
                    await foreach (var update in EmitPersistentRetryUpdates(
                                       overloadedException!,
                                       transportRetryDelay,
                                       persistentAttempt,
                                       maxRetries,
                                       cancellationToken))
                    {
                        yield return update;
                    }
                }
                else
                {
                    yield return new QueryModelCallUpdate(
                        new QueryMessageRuntimeEvent(
                            CreateRetryMessage(
                                overloadedException!,
                                transportRetryDelay,
                                retryAttempt,
                                maxRetries)));
                    await Task.Delay(transportRetryDelay, cancellationToken);
                }

                continue;
            }

            if (shouldRetryTransport)
            {
                if (shouldRunAuthFailureRecovery && apiException is not null)
                {
                    await _authFailureRecoveryRunner.RunAsync(
                        apiException,
                        request,
                        state,
                        session,
                        settings,
                        cancellationToken);
                }

                if (usePersistentRetry)
                {
                    await foreach (var update in EmitPersistentRetryUpdates(
                                       apiException ?? (Exception?)connectionException ?? throw new InvalidOperationException(),
                                       transportRetryDelay,
                                       persistentAttempt,
                                       maxRetries,
                                       cancellationToken))
                    {
                        yield return update;
                    }
                }
                else
                {
                    if (transportRetryDelay > TimeSpan.Zero)
                    {
                        yield return new QueryModelCallUpdate(
                            new QueryMessageRuntimeEvent(
                                CreateRetryMessage(
                                    apiException ?? (Exception?)connectionException ?? throw new InvalidOperationException(),
                                    transportRetryDelay,
                                    retryAttempt,
                                    maxRetries)));
                    }

                    await Task.Delay(transportRetryDelay, cancellationToken);
                }

                continue;
            }

            if (shouldEmitFallback)
            {
                yield return new QueryModelCallUpdate(
                    AttemptResult: QueryModelCallAttemptResult.FallbackRequested(
                        streamingRequest.Request.Model,
                        request.FallbackModel!));
                yield break;
            }

            if (overloadedException is not null)
            {
                throw overloadedException;
            }

            if (apiException is not null)
            {
                throw apiException;
            }

            if (connectionException is not null)
            {
                throw connectionException;
            }

            foreach (var update in _streamUpdateParser.Complete(state))
            {
                yield return update;
            }

            yield break;
        }
    }

    private static bool ShouldHandleOverloadedRetry(string? querySource)
    {
        return querySource is null || ForegroundOverloadedRetrySources.Contains(querySource);
    }

    private static ChatMessage CreateRetryMessage(
        Exception exception,
        TimeSpan retryDelay,
        int retryAttempt,
        int maxRetries)
    {
        return ChatMessageFactory.CreateSystemApiErrorMessage(
            BuildErrorPayload(exception),
            retryInMs: (int)Math.Ceiling(retryDelay.TotalMilliseconds),
            retryAttempt,
            maxRetries);
    }

    private static bool ShouldRefreshClientConfigOnRetry(QueryModelApiException exception)
    {
        return exception.StatusCode == HttpStatusCode.Unauthorized ||
               QueryModelTransportRetryPolicy.IsOAuthTokenRevokedError(exception);
    }

    private static bool IsStaleConnectionError(HttpRequestException exception)
    {
        return ContainsStaleConnectionMarker(exception.Message) ||
               ContainsStaleConnectionMarker(exception.InnerException?.Message);
    }

    private static bool ContainsStaleConnectionMarker(string? message)
    {
        return (message?.Contains("ECONNRESET", StringComparison.OrdinalIgnoreCase) ?? false) ||
               (message?.Contains("EPIPE", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static async IAsyncEnumerable<QueryModelCallUpdate> EmitPersistentRetryUpdates(
        Exception exception,
        TimeSpan retryDelay,
        int retryAttempt,
        int maxRetries,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var remaining = retryDelay;
        while (remaining > TimeSpan.Zero)
        {
            yield return new QueryModelCallUpdate(
                new QueryMessageRuntimeEvent(
                    CreateRetryMessage(
                        exception,
                        remaining,
                        retryAttempt,
                        maxRetries)));

            var chunk = remaining < HeartbeatInterval
                ? remaining
                : HeartbeatInterval;
            await Task.Delay(chunk, cancellationToken);
            remaining -= chunk;
        }
    }

    private static bool TryApplyMaxTokensOverflowRetry(
        QueryModelApiException exception,
        QueryModelHttpStreamingRequest currentStreamingRequest,
        out QueryModelHttpStreamingRequest adjustedStreamingRequest)
    {
        adjustedStreamingRequest = currentStreamingRequest;

        var overflow = QueryMaxTokensContextOverflowParser.TryParse(exception);
        if (overflow is null)
        {
            return false;
        }

        var adjustedMaxTokens = QueryMaxTokensContextOverflowParser.TryCalculateAdjustedMaxTokens(
            overflow,
            currentStreamingRequest.Request.Thinking);
        if (adjustedMaxTokens is null ||
            currentStreamingRequest.Request.MaxTokens == adjustedMaxTokens.Value)
        {
            return false;
        }

        adjustedStreamingRequest = currentStreamingRequest with
        {
            Request = currentStreamingRequest.Request with
            {
                MaxTokens = adjustedMaxTokens.Value
            }
        };
        return true;
    }

    private static JsonNode BuildErrorPayload(Exception exception)
    {
        if (exception is QueryModelApiException apiException)
        {
            if (!string.IsNullOrWhiteSpace(apiException.ResponseBody))
            {
                try
                {
                    var parsed = JsonNode.Parse(apiException.ResponseBody);
                    var errorNode = parsed?["error"];
                    if (errorNode is not null)
                    {
                        return errorNode;
                    }
                }
                catch
                {
                }
            }

            return new JsonObject
            {
                ["name"] = apiException.GetType().Name,
                ["message"] = apiException.Message,
                ["status"] = (int)apiException.StatusCode
            };
        }

        return new JsonObject
        {
            ["name"] = exception.GetType().Name,
            ["message"] = exception.Message
        };
    }
}
