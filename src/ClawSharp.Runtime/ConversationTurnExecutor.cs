using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tools;

namespace ClawSharp.Runtime;

public sealed class ConversationTurnExecutor
{
    private readonly QueryEngine _queryEngine;
    private readonly ITranscriptStore _transcriptStore;
    private readonly ToolRegistry? _toolRegistry;
    private readonly IQueryModelTurnContextProvider? _modelTurnContextProvider;

    public ConversationTurnExecutor(
        QueryEngine queryEngine,
        ITranscriptStore transcriptStore,
        ToolRegistry? toolRegistry = null,
        IQueryModelTurnContextProvider? modelTurnContextProvider = null)
    {
        _queryEngine = queryEngine;
        _transcriptStore = transcriptStore;
        _toolRegistry = toolRegistry;
        _modelTurnContextProvider = modelTurnContextProvider;
    }

    public async Task<ConversationTurnExecutionResult> ExecuteAsync(
        ConversationSession session,
        string prompt,
        Func<string, CancellationToken, Task>? onTextDelta = null,
        Func<ChatMessage, CancellationToken, Task>? onMessage = null,
        Func<QueryConsumerEvent, CancellationToken, Task>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        ClawSharpTelemetry.LogDebug(
            $"[ConversationTurnExecutor] build-start sessionId={session.Id} promptLength={prompt.Length}");
        var turnRequest = await MainThreadTurnRequestBuilder.BuildAsync(
            session,
            prompt,
            _toolRegistry,
            _modelTurnContextProvider,
            cancellationToken);
        ClawSharpTelemetry.LogDebug(
            $"[ConversationTurnExecutor] build-complete sessionId={session.Id} querySource={turnRequest.ModelTurnContext?.QuerySource ?? "none"}");

        var finalAssistantMessage = default(ChatMessage?);
        ClawSharpTelemetry.LogDebug(
            $"[ConversationTurnExecutor] query-start sessionId={session.Id}");
        var queryResult = await _queryEngine.RunTurnAsync(
            session,
            turnRequest,
            onTextDelta,
            async (message, token) =>
            {
                if (message.Role == MessageRole.Assistant &&
                    message.ContentBlocks.Any(static block => block.Kind == MessageContentKind.Text))
                {
                    finalAssistantMessage = message;
                }

                if (onMessage is not null)
                {
                    await onMessage(message, token);
                }
            },
            async (consumerEvent, token) =>
            {
                if (consumerEvent is QueryMessageConsumerEvent messageEvent &&
                    messageEvent.Message.Role == MessageRole.Assistant &&
                    messageEvent.Message.ContentBlocks.Any(static block => block.Kind == MessageContentKind.Text))
                {
                    finalAssistantMessage = messageEvent.Message;
                }

                if (onEvent is not null)
                {
                    await onEvent(consumerEvent, token);
                }
            },
            cancellationToken);
        ClawSharpTelemetry.LogDebug(
            $"[ConversationTurnExecutor] query-complete sessionId={session.Id} terminal={queryResult.Terminal.Reason}");

        if (finalAssistantMessage is null)
        {
            var streamedContent = string.Concat(queryResult.StreamedChunks);
            if (!string.IsNullOrWhiteSpace(streamedContent))
            {
                finalAssistantMessage = ChatMessageFactory.CreateText(MessageRole.Assistant, streamedContent);
                session.Add(finalAssistantMessage);
                await _transcriptStore.RecordTranscriptAsync(session, session.Messages, cancellationToken);

                if (onMessage is not null)
                {
                    await onMessage(finalAssistantMessage, cancellationToken);
                }

                if (onEvent is not null)
                {
                    await onEvent(new QueryMessageConsumerEvent(finalAssistantMessage), cancellationToken);
                }
            }
            else
            {
                finalAssistantMessage = queryResult.AssistantMessage;
            }
        }

        return new ConversationTurnExecutionResult(queryResult, finalAssistantMessage);
    }
}
