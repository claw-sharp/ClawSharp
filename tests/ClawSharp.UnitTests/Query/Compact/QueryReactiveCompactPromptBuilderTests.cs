// TS parity status: focused C# coverage for the reactive-compact prompt-build step; later compact-summary request execution and preserved-tail shaping remain intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class QueryReactiveCompactPromptBuilderTests
{
    [Fact]
    public async Task BuildAsync_Returns_Null_When_No_Messages_Are_Available()
    {
        var builder = new QueryReactiveCompactPromptBuilder();
        var session = CreateSession();

        var result = await builder.BuildAsync(
            new QueryReactiveCompactExecutionContext(
                QueryTurnRequest.Create(session, "hello"),
                QueryLoopStateFactory.CreateInitial([]),
                new QueryTerminalIterationResult(
                    new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
                    QueryLoopStateFactory.CreateInitial([])),
                session,
                new ClawSharpSettings(),
                []),
            QueryReactiveCompactHookRunResult.Empty);

        Assert.Null(result);
    }

    [Fact]
    public async Task BuildAsync_Uses_Messages_After_Last_Compact_Boundary()
    {
        var builder = new QueryReactiveCompactPromptBuilder();
        var session = CreateSession();
        var boundary = ChatMessageFactory.CreateCompactBoundaryMessage("auto", 100);
        var priorState = QueryLoopStateFactory.CreateInitial(
            [
                ChatMessageFactory.CreateText(MessageRole.User, "old"),
                boundary,
                ChatMessageFactory.CreateText(MessageRole.User, "newer")
            ]);

        var result = await builder.BuildAsync(
            new QueryReactiveCompactExecutionContext(
                QueryTurnRequest.Create(session, "hello"),
                priorState,
                new QueryTerminalIterationResult(
                    new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
                    priorState),
                session,
                new ClawSharpSettings(),
                []),
            QueryReactiveCompactHookRunResult.Empty);

        Assert.NotNull(result);
        Assert.Equal(2, result!.MessagesToCompact.Count);
        Assert.Equal("Conversation compacted", result.MessagesToCompact[0].Content);
        Assert.Equal("newer", result.MessagesToCompact[1].Content);
        Assert.Equal("You are a helpful AI assistant tasked with summarizing conversations.", Assert.Single(result.SystemPrompt));
        Assert.Contains("CRITICAL: Respond with TEXT ONLY. Do NOT call any tools.", result.SummaryPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Additional Instructions:", result.SummaryPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_Appends_Custom_Instructions_From_PreCompact_Hooks()
    {
        var builder = new QueryReactiveCompactPromptBuilder();
        var session = CreateSession();
        var priorState = QueryLoopStateFactory.CreateInitial(
            [ChatMessageFactory.CreateText(MessageRole.User, "newer")]);

        var result = await builder.BuildAsync(
            new QueryReactiveCompactExecutionContext(
                QueryTurnRequest.Create(session, "hello"),
                priorState,
                new QueryTerminalIterationResult(
                    new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
                    priorState),
                session,
                new ClawSharpSettings(),
                []),
            new QueryReactiveCompactHookRunResult(CustomInstructions: "Focus on code changes."));

        Assert.NotNull(result);
        Assert.Contains("Additional Instructions:\nFocus on code changes.", result!.SummaryPrompt, StringComparison.Ordinal);
    }

    private static ConversationSession CreateSession()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-reactive-compact-prompt-builder-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        return new ConversationSession("session-reactive-compact-prompt", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
    }
}
