// TS parity status: focused C# unit coverage for the current synthetic missing-tool_result recovery helper and query-engine failure recovery hook.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public class QueryMissingToolResultRecoveryTests
{
    [Fact]
    public void CreateMissingToolResultMessages_Emits_Error_Tool_Result_For_Each_Tool_Use_Block()
    {
        var assistant = ChatMessageFactory.CreateToolUse(
            [
                ("tooluse-read", "Read", "file.txt"),
                ("tooluse-bash", "Bash", "echo hello")
            ]);

        var recovered = QueryMissingToolResultRecovery.CreateMissingToolResultMessages(
            [assistant],
            "runtime failed");

        Assert.Equal(2, recovered.Count);
        Assert.All(recovered, message => Assert.Equal(MessageRole.User, message.Role));
        Assert.All(
            recovered,
            message =>
            {
                var block = Assert.Single(message.ContentBlocks);
                Assert.Equal(MessageContentKind.ToolResult, block.Kind);
                Assert.Equal("runtime failed", block.Value);
            });
        Assert.Equal("tooluse-read", recovered[0].ContentBlocks[0].Metadata?["toolUseId"]);
        Assert.Equal("tooluse-bash", recovered[1].ContentBlocks[0].Metadata?["toolUseId"]);
    }

    [Fact]
    public async Task QueryEngine_Recovers_Missing_Tool_Result_Messages_When_Runtime_Fails_After_Tool_Use()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "clawsharp-query-recovery-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var settings = new ClawSharpSettings();
        var eventSink = new InMemoryEventSink();
        var transcriptStore = new InMemoryTranscriptStore();
        var queue = new InMemoryQueuedCommandQueue();
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            new ThrowingQueryTurnRunner(),
            new QueuedTaskNotificationDrainer(queue, transcriptStore));
        var session = new DefaultSessionFactory(tempDir).Create();
        var request = QueryTurnRequest.Create(
            session,
            "Read note.txt",
            [
                new ToolCallRequest("tooluse-note", "Read", "note.txt")
            ]);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => queryEngine.RunTurnAsync(session, request));

        Assert.Equal("simulated query failure", thrown.Message);
        Assert.Contains(
            session.Messages,
            message => message.Role == MessageRole.Assistant &&
                       message.ContentBlocks.Any(block => block.Kind == MessageContentKind.ToolUse));
        var recovered = Assert.Single(
            session.Messages,
            message =>
                message.Role == MessageRole.User &&
                message.ContentBlocks.Count == 1 &&
                message.ContentBlocks[0].Kind == MessageContentKind.ToolResult);
        Assert.Equal("tooluse-note", recovered.ContentBlocks[0].Metadata?["toolUseId"]);
        Assert.Equal(
            "Tool execution interrupted because the query runtime failed before emitting tool results.",
            recovered.ContentBlocks[0].Value);

        var transcript = await transcriptStore.ReadTranscriptAsync(session.TranscriptPath, CancellationToken.None);
        Assert.Contains(
            transcript.Messages,
            message => message.Role == MessageRole.User &&
                       message.ContentBlocks.Any(
                           block =>
                               block.Kind == MessageContentKind.ToolResult &&
                               block.Metadata?["toolUseId"] == "tooluse-note"));
    }

    private sealed class ThrowingQueryTurnRunner : IQueryTurnRunner
    {
        public async IAsyncEnumerable<QueryRuntimeEvent> RunAsync(
            QueryTurnRequest request,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return new QueryMessageRuntimeEvent(
                ChatMessageFactory.CreateToolUse(
                    request.RequestedTools
                        .Select(toolCall => (toolCall.ToolUseId, toolCall.ToolName, toolCall.Arguments))
                        .ToArray()));

            await Task.Yield();
            throw new InvalidOperationException("simulated query failure");
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
