// TS origin: ./services/compact/compact.ts, ./services/compact/prompt.ts, ./services/api/claude.ts
// TS parity status: focused C# coverage for the first live reactive-compact summary-generation runner beneath the overflow recovery path; richer reactive-only retry behavior, post-compact hooks, and attachment restoration remain delegated to later runtime ports.
using System.Text.Json;
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryReactiveCompactModelCallRunnerTests
{
    [Fact]
    public async Task TryCompactAsync_Builds_Compact_Request_And_Returns_Boundary_And_Summary_Message()
    {
        var executor = new CapturingCompletedCompactModelCallExecutor();
        var runner = new QueryReactiveCompactModelCallRunner(executor);
        var session = CreateSession();
        var priorBoundary = ChatMessageFactory.CreateCompactBoundaryMessage("auto", 100);
        var priorUser = ChatMessageFactory.CreateText(MessageRole.User, "user detail");
        var priorAssistant = ChatMessageFactory.CreateText(MessageRole.Assistant, "assistant detail");
        var priorState = QueryLoopStateFactory.CreateInitial(
            [priorBoundary, priorUser, priorAssistant],
            QueryToolUseContextState.Empty with { MainLoopModel = "compact-model" });
        var context = new QueryReactiveCompactExecutionContext(
            QueryTurnRequest.Create(session, "hello"),
            priorState,
            new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
                priorState),
            session,
            new ClawSharpSettings
            {
                Runtime = new RuntimeSettings
                {
                    Model = "settings-model"
                }
            },
            [new QueryRequestTool("Read", "Read files")],
            EstimatedPreCompactTokenCount: 321,
            ResolvedMaxTokens: 12345);
        var prompt = new QueryReactiveCompactPromptBuildResult(
            QueryCompactBoundaryHelpers.GetMessagesAfterCompactBoundary(priorState.Messages),
            ["compact system prompt"],
            "compact summary prompt");

        var compacted = await runner.TryCompactAsync(
            context,
            prompt,
            QueryReactiveCompactHookRunResult.Empty);

        Assert.NotNull(compacted);
        Assert.NotNull(executor.CapturedRequest);
        Assert.Equal("compact", executor.CapturedRequest!.QuerySource);
        Assert.Equal("compact-model", executor.CapturedRequest.Request.Model);
        Assert.Equal(12345, executor.CapturedRequest.Request.MaxTokens);
        Assert.Equal("disabled", executor.CapturedRequest.Request.Thinking!.Type);
        Assert.Equal("compact system prompt", Assert.Single(executor.CapturedRequest.Request.System).Text);
        Assert.Equal("Read", Assert.Single(executor.CapturedRequest.Request.Tools).Name);
        Assert.Equal(["user", "assistant", "user"], executor.CapturedRequest.Request.Messages.Select(message => message.Role).ToArray());
        Assert.Equal("user detail", executor.CapturedRequest.Request.Messages[0].Content[0].Text);
        Assert.Equal("assistant detail", executor.CapturedRequest.Request.Messages[1].Content[0].Text);
        Assert.Equal("compact summary prompt", executor.CapturedRequest.Request.Messages[2].Content[0].Text);

        var boundaryBlock = Assert.Single(compacted!.BoundaryMarker.ContentBlocks);
        Assert.Equal("Conversation compacted", boundaryBlock.Value);
        Assert.NotNull(boundaryBlock.Metadata);
        var boundaryMetadata = JsonSerializer.Deserialize<CompactBoundaryMetadata>(boundaryBlock.Metadata!["compactMetadata"]);
        Assert.NotNull(boundaryMetadata);
        Assert.Equal("manual", boundaryMetadata!.Trigger);
        Assert.Equal(321, boundaryMetadata.PreTokens);
        Assert.Equal(prompt.MessagesToCompact.Count, boundaryMetadata.MessagesSummarized);
        Assert.Equal(priorAssistant.Id, boundaryBlock.Metadata["logicalParentUuid"]);

        var summaryMessage = Assert.Single(compacted.SummaryMessages);
        Assert.Contains("Summary:", summaryMessage.Content, StringComparison.Ordinal);
        Assert.Contains("summary result", summaryMessage.Content, StringComparison.Ordinal);
        Assert.Contains(session.TranscriptPath, summaryMessage.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent messages are preserved verbatim.", summaryMessage.Content, StringComparison.Ordinal);
        Assert.Equal("<analysis>internal</analysis><summary>summary result</summary>", compacted.RawSummary);
        Assert.Equal(321, compacted.PreCompactTokenCount);
        Assert.Equal(222, compacted.PostCompactTokenCount);
    }

    [Fact]
    public async Task TryCompactAsync_Returns_Null_When_Completed_Attempt_Only_Contains_Api_Error_Assistant_Message()
    {
        var runner = new QueryReactiveCompactModelCallRunner(new ApiErrorCompactModelCallExecutor());
        var session = CreateSession();
        var priorState = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var context = new QueryReactiveCompactExecutionContext(
            QueryTurnRequest.Create(session, "hello"),
            priorState,
            new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
                priorState),
            session,
            new ClawSharpSettings(),
            [],
            EstimatedPreCompactTokenCount: 100,
            ResolvedMaxTokens: 20000);
        var prompt = new QueryReactiveCompactPromptBuildResult(
            priorState.Messages,
            ["system"],
            "compact summary prompt");

        var compacted = await runner.TryCompactAsync(
            context,
            prompt,
            QueryReactiveCompactHookRunResult.Empty);

        Assert.Null(compacted);
    }

    private static ConversationSession CreateSession()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-reactive-compact-model-call-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        return new ConversationSession("session-reactive-compact-model", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
    }

    private sealed class CapturingCompletedCompactModelCallExecutor : IQueryModelCallExecutor
    {
        public QueryModelHttpStreamingRequest? CapturedRequest { get; private set; }

        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            CapturedRequest = streamingRequest;
            var assistantMessage = ChatMessageFactory.CreateText(
                MessageRole.Assistant,
                "<analysis>internal</analysis><summary>summary result</summary>");
            var completedState = state with
            {
                Messages = state.Messages.Concat([assistantMessage]).ToArray()
            };
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState),
                    turnOutputTokens: 222));
            await Task.CompletedTask;
        }
    }

    private sealed class ApiErrorCompactModelCallExecutor : IQueryModelCallExecutor
    {
        public async IAsyncEnumerable<QueryModelCallUpdate> StreamAsync(
            QueryModelHttpStreamingRequest streamingRequest,
            QueryTurnRequest request,
            QueryLoopState state,
            ConversationSession session,
            ClawSharpSettings settings,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var assistantMessage = ChatMessageFactory.CreateAssistantApiErrorMessage("Request was aborted.");
            var completedState = state with
            {
                Messages = state.Messages.Concat([assistantMessage]).ToArray()
            };
            yield return new QueryModelCallUpdate(
                AttemptResult: QueryModelCallAttemptResult.Completed(
                    new QueryTerminalIterationResult(
                        new QueryLoopTerminal(QueryTerminalReason.Completed),
                        completedState)));
            await Task.CompletedTask;
        }
    }
}
