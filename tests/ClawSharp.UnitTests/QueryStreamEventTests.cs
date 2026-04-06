// TS parity status: focused C# coverage for the raw stream_event runtime and consumer event contract; the live model-backed stream producer remains intentionally unported.
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public class QueryStreamEventTests
{
    [Fact]
    public async Task QueryEngine_Forwards_Raw_Stream_Events_To_Consumer_Callback()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-stream-event-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var transcriptStore = new InMemoryTranscriptStore();
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            new StreamEventQueryTurnRunner(),
            new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();
        var observedEvents = new List<QueryConsumerEvent>();
        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var result = await queryEngine.RunTurnAsync(
            session,
            request,
            onTextDelta: null,
            onMessage: null,
            onEvent: (queryEvent, _) =>
            {
                observedEvents.Add(queryEvent);
                return Task.CompletedTask;
            },
            cancellationToken: CancellationToken.None);

        var streamEvent = Assert.IsType<QueryStreamEventConsumerEvent>(
            observedEvents.Single(queryEvent => queryEvent is QueryStreamEventConsumerEvent));
        var payload = Assert.IsType<JsonObject>(streamEvent.Event.Event);
        Assert.Equal("content_block_delta", payload["type"]?.GetValue<string>());
        Assert.Equal("hello", payload["delta"]?["text"]?.GetValue<string>());

        var resultEvent = Assert.IsType<QueryResultConsumerEvent>(observedEvents[^1]);
        Assert.NotNull(result.AssistantMessage);
        Assert.NotNull(resultEvent.Result.AssistantMessage);
        Assert.Equal(result.AssistantMessage!.Id, resultEvent.Result.AssistantMessage!.Id);
    }

    private sealed class StreamEventQueryTurnRunner : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();
            yield return new QueryStreamEventRuntimeEvent(
                new QueryStreamEvent(
                    new JsonObject
                    {
                        ["type"] = "content_block_delta",
                        ["delta"] = new JsonObject
                        {
                            ["text"] = "hello"
                        }
                    }));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateToolUse(
                    request.RequestedTools
                        .Select(toolCall => (toolCall.ToolUseId, toolCall.ToolName, toolCall.Arguments))
                        .ToArray()));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateToolResult("tooluse-note", "Read", "hello"));
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateText(MessageRole.Assistant, "Done."));
            yield return new QueryLoopTerminalRuntimeEvent(
                new QueryLoopTerminal(QueryTerminalReason.Completed),
                QueryLoopStateFactory.CreateInitial(session.Messages.ToArray()));
            await Task.CompletedTask;
        }
    }

    private sealed class InMemoryTranscriptStore : ITranscriptStore
    {
        private readonly Dictionary<string, TranscriptReadResult> _transcripts = new(StringComparer.Ordinal);

        public Task RecordTranscriptAsync(
            ConversationSession session,
            IReadOnlyList<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            _transcripts[session.TranscriptPath] = new TranscriptReadResult(
                messages.ToArray(),
                session.CustomTitle,
                FileHistoryState.Empty);
            return Task.CompletedTask;
        }

        public Task<TranscriptReadResult> ReadTranscriptAsync(
            string transcriptPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                _transcripts.TryGetValue(transcriptPath, out var transcript)
                    ? transcript
                    : new TranscriptReadResult([], null, FileHistoryState.Empty));
        }

        public Task RecordSessionMetadataAsync(
            ConversationSession session,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task FlushAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
