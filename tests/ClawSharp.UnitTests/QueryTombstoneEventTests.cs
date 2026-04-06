// TS origin: ./QueryEngine.ts, ./query.ts, ./utils/messages.ts
// TS parity status: focused C# coverage for the tombstone runtime and consumer event contract; transcript rewrite/removal semantics remain blocked on the unported TS session-storage tombstone path.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public class QueryTombstoneEventTests
{
    [Fact]
    public async Task QueryEngine_Forwards_Tombstone_Runtime_Events_Without_Persisting_Them()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-tombstone-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var transcriptStore = new InMemoryTranscriptStore();
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            new TombstoneQueryTurnRunner(),
            new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();
        var observedEvents = new List<QueryConsumerEvent>();
        var observedMessages = new List<ChatMessage>();
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
            onMessage: (message, _) =>
            {
                observedMessages.Add(message);
                return Task.CompletedTask;
            },
            onEvent: (queryEvent, _) =>
            {
                observedEvents.Add(queryEvent);
                return Task.CompletedTask;
            },
            cancellationToken: CancellationToken.None);

        var tombstoneEvent = Assert.IsType<QueryTombstoneConsumerEvent>(
            observedEvents.Single(queryEvent => queryEvent is QueryTombstoneConsumerEvent));
        Assert.Equal("partial response", tombstoneEvent.Message.Content);

        Assert.DoesNotContain(session.Messages, message => message.Content == "partial response");
        Assert.DoesNotContain(observedMessages, message => message.Content == "partial response");

        var transcript = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath, CancellationToken.None);
        Assert.DoesNotContain(transcript.Messages, message => message.Content == "partial response");

        Assert.NotNull(result.AssistantMessage);
        Assert.Equal(result.AssistantMessage!.Id, observedMessages[^1].Id);
    }

    private sealed class TombstoneQueryTurnRunner : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryRequestStartRuntimeEvent();
            yield return new QueryTombstoneRuntimeEvent(
                ChatMessageFactory.CreateText(MessageRole.Assistant, "partial response"));
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
