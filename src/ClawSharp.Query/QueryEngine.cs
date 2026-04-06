// TS origin: ./QueryEngine.ts, ./query.ts
// TS parity status: queued task-notification drain, transcript persistence, runtime-event consumption, and completed-turn result shaping are ported; full 1:1 parity still depends on the remaining model-backed query-loop branches.
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Query;

public sealed class QueryEngine
{
    private readonly ClawSharpSettings _settings;
    private readonly IEventSink _eventSink;
    private readonly ITranscriptStore _transcriptStore;
    private readonly IQueryTurnRunner _queryTurnRunner;
    private readonly QueuedTaskNotificationDrainer _queuedTaskNotificationDrainer;
    private readonly IFileUpdateNotifier _fileUpdateNotifier;
    private readonly ToolRegistry? _toolRegistry;
    private readonly QueryRequestBuilder _queryRequestBuilder;
    private readonly IQueryModelTurnContextProvider? _modelTurnContextProvider;

    public QueryEngine(
        ClawSharpSettings settings,
        IEventSink eventSink,
        ITranscriptStore transcriptStore,
        IQueryTurnRunner queryTurnRunner,
        QueuedTaskNotificationDrainer queuedTaskNotificationDrainer,
        IFileUpdateNotifier? fileUpdateNotifier = null,
        ToolRegistry? toolRegistry = null,
        QueryRequestBuilder? queryRequestBuilder = null,
        IQueryModelTurnContextProvider? modelTurnContextProvider = null)
    {
        _settings = settings;
        _eventSink = eventSink;
        _transcriptStore = transcriptStore;
        _queryTurnRunner = queryTurnRunner;
        _queuedTaskNotificationDrainer = queuedTaskNotificationDrainer;
        _fileUpdateNotifier = fileUpdateNotifier ?? new NullFileUpdateNotifier();
        _toolRegistry = toolRegistry;
        _queryRequestBuilder = queryRequestBuilder ?? new QueryRequestBuilder();
        _modelTurnContextProvider = modelTurnContextProvider;
    }

    public async Task<QueryResult> RunTurnAsync(
        ConversationSession session,
        string userInput,
        Func<string, CancellationToken, Task>? onTextDelta = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await RunTurnAsync(
            session,
            QueryTurnRequest.Create(session, userInput),
            onTextDelta,
            onMessage: null,
            onEvent: null,
            cancellationToken);
    }

    public async Task<QueryResult> RunTurnAsync(
        ConversationSession session,
        string userInput,
        Func<string, CancellationToken, Task>? onTextDelta,
        Func<ChatMessage, CancellationToken, Task>? onMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await RunTurnAsync(
            session,
            QueryTurnRequest.Create(session, userInput),
            onTextDelta,
            onMessage,
            onEvent: null,
            cancellationToken);
    }

    public async Task<QueryResult> RunTurnAsync(
        ConversationSession session,
        QueryTurnRequest request,
        Func<string, CancellationToken, Task>? onTextDelta = null,
        CancellationToken cancellationToken = default)
    {
        return await RunTurnAsync(
            session,
            request,
            onTextDelta,
            onMessage: null,
            onEvent: null,
            cancellationToken);
    }

    public async Task<QueryResult> RunTurnAsync(
        ConversationSession session,
        QueryTurnRequest request,
        Func<string, CancellationToken, Task>? onTextDelta,
        Func<ChatMessage, CancellationToken, Task>? onMessage,
        CancellationToken cancellationToken = default)
    {
        return await RunTurnAsync(
            session,
            request,
            onTextDelta,
            onMessage,
            onEvent: null,
            cancellationToken);
    }

    public async Task<QueryResult> RunTurnAsync(
        ConversationSession session,
        QueryTurnRequest request,
        Func<string, CancellationToken, Task>? onTextDelta,
        Func<ChatMessage, CancellationToken, Task>? onMessage,
        Func<QueryConsumerEvent, CancellationToken, Task>? onEvent,
        CancellationToken cancellationToken = default)
    {
        return await RunTurnCoreAsync(
            session,
            request,
            appendUserInputMessage: true,
            onTextDelta,
            onMessage,
            onEvent,
            cancellationToken);
    }

    public async Task<QueryResult> RunPreparedTurnAsync(
        ConversationSession session,
        QueryTurnRequest request,
        Func<string, CancellationToken, Task>? onTextDelta = null,
        Func<ChatMessage, CancellationToken, Task>? onMessage = null,
        Func<QueryConsumerEvent, CancellationToken, Task>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        return await RunTurnCoreAsync(
            session,
            request,
            appendUserInputMessage: false,
            onTextDelta,
            onMessage,
            onEvent,
            cancellationToken);
    }

    private async Task<QueryResult> RunTurnCoreAsync(
        ConversationSession session,
        QueryTurnRequest request,
        bool appendUserInputMessage,
        Func<string, CancellationToken, Task>? onTextDelta,
        Func<ChatMessage, CancellationToken, Task>? onMessage,
        Func<QueryConsumerEvent, CancellationToken, Task>? onEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var interactionSpan = ClawSharpTelemetry.StartInteractionSpan(request.UserInput);
        await _queuedTaskNotificationDrainer.DrainAsync(session, cancellationToken);
        await _fileUpdateNotifier.HandleQueryStartAsync(cancellationToken).ConfigureAwait(false);

        _eventSink.Publish(
            new AppEvent(
                AppEventType.QueryReceived,
                "User prompt received",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string> { ["sessionId"] = request.SessionId }));

        ChatMessage? userMessage = null;
        if (appendUserInputMessage)
        {
            userMessage = ChatMessageFactory.CreateText(MessageRole.User, request.UserInput);
            session.Add(userMessage);
            await FileHistoryService.MakeSnapshotAsync(session, _settings, userMessage.Id, _fileUpdateNotifier, cancellationToken);
            await _transcriptStore.RecordTranscriptAsync(session, session.Messages, cancellationToken);
        }
        else if (session.Messages.Count > 0)
        {
            await _transcriptStore.RecordTranscriptAsync(session, session.Messages, cancellationToken);
        }
        request = request with
        {
            ModelTurnContext = request.ModelTurnContext ?? QueryModelTurnContext.ReplMainThread
        };
        request = request with
        {
            FallbackModel = request.FallbackModel ?? _settings.Runtime.FallbackModel
        };
        request = request with
        {
            InitialToolUseContext = request.InitialToolUseContext ?? (
                _toolRegistry is null
                    ? null
                    : QueryToolUseContextStateFactory.CreateFromToolRegistry(_toolRegistry))
        };
        if (ShouldPopulateDefaultModelTurnContext(request.ModelTurnContext) &&
            _modelTurnContextProvider is not null)
        {
            request = request with
            {
                ModelTurnContext = await _modelTurnContextProvider.GetReplMainThreadContextAsync(cancellationToken)
            };
        }

        var modelRequest = _queryRequestBuilder.BuildFromMessages(
            request,
            session.Messages,
            _settings,
            _toolRegistry?.All ?? [],
            new QueryRequestBuildOptions(
                SystemPrompt: request.ModelTurnContext.SystemPrompt,
                SystemContext: request.ModelTurnContext.SystemContext,
                UserContext: request.ModelTurnContext.UserContext));

        var streamedChunks = new List<string>();
        var turnMessages = new List<ChatMessage>();
        var seedMessages = userMessage is null ? session.Messages.ToArray() : [userMessage];
        var loopState = QueryLoopStateFactory.CreateInitial(seedMessages, request.InitialToolUseContext);
        QueryLoopTerminal? terminal = null;

        try
        {
            await foreach (var runtimeEvent in _queryTurnRunner.RunAsync(request, session, _settings, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();

                switch (runtimeEvent)
                {
                    case QueryRequestStartRuntimeEvent:
                        if (onEvent is not null)
                        {
                            await onEvent(new QueryRequestStartConsumerEvent(), cancellationToken);
                        }

                        break;
                    case QueryMessageRuntimeEvent messageEvent:
                        turnMessages.Add(messageEvent.Message);
                        session.Add(messageEvent.Message);
                        await _transcriptStore.RecordTranscriptAsync(session, session.Messages, cancellationToken);
                        if (onMessage is not null)
                        {
                            await onMessage(messageEvent.Message, cancellationToken);
                        }

                        if (onEvent is not null)
                        {
                            await onEvent(new QueryMessageConsumerEvent(messageEvent.Message), cancellationToken);
                        }

                        loopState = loopState with { Messages = session.Messages.ToArray() };

                        break;
                    case QueryTombstoneRuntimeEvent tombstoneEvent:
                        if (onEvent is not null)
                        {
                            await onEvent(new QueryTombstoneConsumerEvent(tombstoneEvent.Message), cancellationToken);
                        }

                        break;
                    case QueryToolUseSummaryRuntimeEvent toolUseSummaryEvent:
                        if (onEvent is not null)
                        {
                            await onEvent(new QueryToolUseSummaryConsumerEvent(toolUseSummaryEvent.Message), cancellationToken);
                        }

                        break;
                    case QueryStreamEventRuntimeEvent streamEvent:
                        if (onEvent is not null)
                        {
                            await onEvent(new QueryStreamEventConsumerEvent(streamEvent.Event), cancellationToken);
                        }

                        break;
                    case QueryStreamDeltaRuntimeEvent streamDeltaEvent:
                        streamedChunks.Add(streamDeltaEvent.Delta);
                        ClawSharpTelemetry.RecordMetric("query.stream.delta.count");
                        _eventSink.Publish(
                            new AppEvent(
                                AppEventType.QueryStreaming,
                                "Streaming assistant output",
                                DateTimeOffset.UtcNow,
                                new Dictionary<string, string>
                                {
                                    ["sessionId"] = session.Id,
                                    ["chunkLength"] = streamDeltaEvent.Delta.Length.ToString()
                                }));

                        if (onTextDelta is not null)
                        {
                            await onTextDelta(streamDeltaEvent.Delta, cancellationToken);
                        }

                        if (onEvent is not null)
                        {
                            await onEvent(new QueryStreamDeltaConsumerEvent(streamDeltaEvent.Delta), cancellationToken);
                        }

                        break;
                    case QueryLoopTransitionRuntimeEvent transitionEvent:
                        loopState = transitionEvent.State with { Transition = transitionEvent.Transition };
                        if (onEvent is not null)
                        {
                            await onEvent(new QueryLoopTransitionConsumerEvent(transitionEvent.Transition), cancellationToken);
                        }

                        break;
                    case QueryLoopTerminalRuntimeEvent terminalEvent:
                        terminal = terminalEvent.Terminal;
                        loopState = terminalEvent.State;
                        if (onEvent is not null)
                        {
                            await onEvent(new QueryLoopTerminalConsumerEvent(terminalEvent.Terminal), cancellationToken);
                        }

                        break;
                }
            }
        }
        catch (Exception exception)
        {
            interactionSpan.RecordException(exception);
            ClawSharpTelemetry.CaptureException(
                exception,
                "query.turn",
                new Dictionary<string, object?>
                {
                    ["session_id"] = session.Id
                });
            var recoveredMessages = QueryMissingToolResultRecovery.CreateMissingToolResultMessages(
                turnMessages.Where(message => message.Role == MessageRole.Assistant).ToArray(),
                "Tool execution interrupted because the query runtime failed before emitting tool results.");

            foreach (var recoveredMessage in recoveredMessages)
            {
                turnMessages.Add(recoveredMessage);
                session.Add(recoveredMessage);
                await _transcriptStore.RecordTranscriptAsync(session, session.Messages, cancellationToken);

                if (onMessage is not null)
                {
                    await onMessage(recoveredMessage, cancellationToken);
                }

                if (onEvent is not null)
                {
                    await onEvent(new QueryMessageConsumerEvent(recoveredMessage), cancellationToken);
                }
            }

            loopState = loopState with { Messages = session.Messages.ToArray() };
            throw;
        }

        terminal ??= new QueryLoopTerminal(QueryTerminalReason.Completed);
        var terminalMessage = turnMessages.FindLast(IsSuccessfulTerminalMessage);
        ChatMessage? assistantMessage = null;
        if (terminalMessage is not null && terminalMessage.Role == MessageRole.Assistant)
        {
            assistantMessage = terminalMessage;
        }

        if (terminal.Reason == QueryTerminalReason.Completed && terminalMessage is null)
        {
            throw new QueryExecutionFailedException();
        }

        var toolNames = CollectToolNames(turnMessages);
        var usedTool = toolNames.Count > 0;
        interactionSpan.SetAttribute("used_tool", usedTool);
        interactionSpan.SetAttribute("tool_count", toolNames.Count);
        ClawSharpTelemetry.RecordMetric("query.turn.count");
        ClawSharpTelemetry.RecordMetric("query.turn.tool_count", toolNames.Count);
        if (usedTool)
        {
            ClawSharpTelemetry.LogEvent(
                "tengu_query_used_tools",
                new Dictionary<string, object?>
                {
                    ["tool_count"] = toolNames.Count,
                    ["session_id"] = session.Id
                });
        }

        PublishQueryCompleted(
            session.Id,
            usedTool,
            toolNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        var result = new QueryResult(
            assistantMessage,
            UsedTool: usedTool,
            ToolNames: toolNames,
            streamedChunks,
            terminal,
            loopState with { Messages = session.Messages.ToArray() },
            modelRequest);

        if (onEvent is not null)
        {
            await onEvent(new QueryResultConsumerEvent(result), cancellationToken);
        }

        return result;
    }

    private void PublishQueryCompleted(string sessionId, bool usedTool, IReadOnlyList<string> toolNames)
    {
        var metadata = new Dictionary<string, string>
        {
            ["sessionId"] = sessionId,
            ["usedTool"] = usedTool.ToString()
        };

        if (toolNames.Count > 0)
        {
            metadata["toolNames"] = string.Join(",", toolNames);
        }

        _eventSink.Publish(
            new AppEvent(
                AppEventType.QueryCompleted,
                "Assistant response generated",
                DateTimeOffset.UtcNow,
                metadata));
    }

    private static bool IsSuccessfulTerminalMessage(ChatMessage message)
    {
        if (message.Role == MessageRole.Assistant)
        {
            return message.ContentBlocks.Any(block => block.Kind == MessageContentKind.Text);
        }

        if (message.Role == MessageRole.User)
        {
            return
                message.ContentBlocks.Count > 0 &&
                message.ContentBlocks.All(block => block.Kind == MessageContentKind.ToolResult);
        }

        return false;
    }

    private static IReadOnlyList<string> CollectToolNames(IReadOnlyList<ChatMessage> messages)
    {
        List<string> toolUseNames = [];
        foreach (var message in messages)
        {
            if (message.Role != MessageRole.Assistant)
            {
                continue;
            }

            foreach (var block in message.ContentBlocks.Where(block => block.Kind == MessageContentKind.ToolUse))
            {
                if (!string.IsNullOrWhiteSpace(block.Name))
                {
                    toolUseNames.Add(block.Name);
                }
            }
        }

        if (toolUseNames.Count > 0)
        {
            return toolUseNames;
        }

        List<string> toolResultNames = [];
        foreach (var message in messages)
        {
            if (message.Role != MessageRole.User)
            {
                continue;
            }

            foreach (var block in message.ContentBlocks.Where(block => block.Kind == MessageContentKind.ToolResult))
            {
                if (!string.IsNullOrWhiteSpace(block.Name))
                {
                    toolResultNames.Add(block.Name);
                }
            }
        }

        return toolResultNames;
    }

    private static bool ShouldPopulateDefaultModelTurnContext(QueryModelTurnContext? modelTurnContext)
    {
        if (modelTurnContext is null)
        {
            return true;
        }

        return string.Equals(modelTurnContext.QuerySource, QueryModelTurnContext.ReplMainThread.QuerySource, StringComparison.Ordinal) &&
               modelTurnContext.SystemPrompt.Count == 0 &&
               modelTurnContext.UserContext.Count == 0 &&
               modelTurnContext.SystemContext.Count == 0;
    }
}
