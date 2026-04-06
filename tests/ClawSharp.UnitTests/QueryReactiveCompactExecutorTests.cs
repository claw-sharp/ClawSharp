// TS parity status: focused C# coverage for the reactive-compact runtime orchestration layer beneath the overflow recovery seam; the concrete compact-summary API call, hook execution, and attachment restoration runtimes remain intentionally unported.
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

public sealed class QueryReactiveCompactExecutorTests
{
    [Fact]
    public async Task TryReactiveCompactAsync_Returns_Null_When_Prompt_Cannot_Be_Built()
    {
        var executor = new QueryReactiveCompactExecutor();
        var session = CreateSession();

        var compacted = await executor.TryReactiveCompactAsync(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]),
            new QueryTerminalIterationResult(
                new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
                QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")])),
            session,
            new ClawSharpSettings());

        Assert.Null(compacted);
    }

    [Fact]
    public async Task TryReactiveCompactAsync_Composes_Hooks_Model_Call_And_Attachments()
    {
        var steps = new List<string>();
        var executor = new QueryReactiveCompactExecutor(
            new RecordingTokenEstimator(),
            new RecordingMaxTokensResolver(),
            new RecordingToolCatalog(),
            new RecordingPromptBuilder(steps),
            new RecordingHookRunner(steps),
            new RecordingModelCallRunner(steps),
            new RecordingAttachmentBuilder(steps));
        var session = CreateSession();
        var priorState = QueryLoopStateFactory.CreateInitial([ChatMessageFactory.CreateText(MessageRole.User, "hello")]);
        var terminalResult = new QueryTerminalIterationResult(
            new QueryLoopTerminal(QueryTerminalReason.PromptTooLong),
            priorState with
            {
                Messages = priorState.Messages.Concat(
                    [ChatMessageFactory.CreateAssistantApiErrorMessage("Prompt too long", errorDetails: "prompt is too long")]).ToArray()
            });

        var compacted = await executor.TryReactiveCompactAsync(
            QueryTurnRequest.Create(session, "hello"),
            priorState,
            terminalResult,
            session,
            new ClawSharpSettings());

        Assert.NotNull(compacted);
        Assert.Equal(["hook", "prompt", "model", "post-hook", "attachments"], steps);
        Assert.Equal("Conversation compacted", compacted!.BoundaryMarker.Content);
        Assert.Equal("summary", Assert.Single(compacted.SummaryMessages).Content);
        Assert.Equal("plan-attachment", Assert.Single(compacted.Attachments).Content);
        Assert.Equal("hook-result", Assert.Single(compacted.HookResults).Content);
        Assert.Equal("raw-summary", compacted.RawSummary);
        Assert.Equal("hook-display\npost-hook-display:raw-summary", compacted.UserDisplayMessage);
    }

    [Fact]
    public void ToolRegistryReactiveCompactToolCatalog_Returns_Read_Tool_Subset()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-reactive-compact-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var taskRegistry = new TaskRegistry(sessionRoot);
        var toolRegistry = new ToolRegistry(sessionRoot, taskRegistry);
        var catalog = new ToolRegistryReactiveCompactToolCatalog(toolRegistry);
        var session = new ConversationSession("session-reactive-compact-tools", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));

        var tools = catalog.GetTools(
            QueryTurnRequest.Create(session, "hello"),
            QueryLoopStateFactory.CreateInitial([]),
            session,
            new ClawSharpSettings());

        var readTool = Assert.Single(tools);
        Assert.Equal("Read", readTool.Name);
        Assert.True(readTool.Strict);
    }

    private static ConversationSession CreateSession()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-reactive-compact-executor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        return new ConversationSession("session-reactive-compact", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));
    }

    private sealed class RecordingPromptBuilder : IQueryReactiveCompactPromptBuilder
    {
        private readonly List<string> _steps;

        public RecordingPromptBuilder(List<string> steps)
        {
            _steps = steps;
        }

        public Task<QueryReactiveCompactPromptBuildResult?> BuildAsync(
            QueryReactiveCompactExecutionContext context,
            QueryReactiveCompactHookRunResult hookResult,
            CancellationToken cancellationToken = default)
        {
            _steps.Add("prompt");
            return Task.FromResult<QueryReactiveCompactPromptBuildResult?>(
                new QueryReactiveCompactPromptBuildResult(
                    context.PriorState.Messages,
                    ["system"],
                    hookResult.CustomInstructions is null ? "compact prompt" : $"compact prompt:{hookResult.CustomInstructions}"));
        }
    }

    private sealed class RecordingToolCatalog : IQueryReactiveCompactToolCatalog
    {
        public IReadOnlyList<QueryRequestTool> GetTools(
            QueryTurnRequest request,
            QueryLoopState priorState,
            ConversationSession session,
            ClawSharpSettings settings)
        {
            return [new QueryRequestTool("Read", "Read files")];
        }
    }

    private sealed class RecordingTokenEstimator : IQueryCompactionTokenEstimator
    {
        public int Estimate(IReadOnlyList<ChatMessage> messages)
        {
            Assert.NotEmpty(messages);
            return 321;
        }
    }

    private sealed class RecordingMaxTokensResolver : IQueryCompactMaxTokensResolver
    {
        public int Resolve(
            QueryTurnRequest request,
            QueryLoopState priorState,
            ConversationSession session,
            ClawSharpSettings settings)
        {
            return 12345;
        }
    }

    private sealed class RecordingHookRunner : IQueryReactiveCompactHookRunner
    {
        private readonly List<string> _steps;

        public RecordingHookRunner(List<string> steps)
        {
            _steps = steps;
        }

        public Task<QueryReactiveCompactHookRunResult> RunPreCompactAsync(
            QueryReactiveCompactExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _steps.Add("hook");
            return Task.FromResult(
                new QueryReactiveCompactHookRunResult(
                    CustomInstructions: "custom",
                    HookResults: [ChatMessageFactory.CreateText(MessageRole.System, "hook-result")],
                    UserDisplayMessage: "hook-display"));
        }

        public Task<QueryReactiveCompactPostCompactHookRunResult> RunPostCompactAsync(
            QueryReactiveCompactExecutionContext context,
            string compactSummary,
            CancellationToken cancellationToken = default)
        {
            _steps.Add("post-hook");
            return Task.FromResult(
                new QueryReactiveCompactPostCompactHookRunResult(
                    UserDisplayMessage: $"post-hook-display:{compactSummary}"));
        }
    }

    private sealed class RecordingModelCallRunner : IQueryReactiveCompactModelCallRunner
    {
        private readonly List<string> _steps;

        public RecordingModelCallRunner(List<string> steps)
        {
            _steps = steps;
        }

        public Task<QueryCompactionResult?> TryCompactAsync(
            QueryReactiveCompactExecutionContext context,
            QueryReactiveCompactPromptBuildResult prompt,
            QueryReactiveCompactHookRunResult hookResult,
            CancellationToken cancellationToken = default)
        {
            _steps.Add("model");
            var compactTool = Assert.Single(context.Tools);
            Assert.Equal("Read", compactTool.Name);
            Assert.Equal(321, context.EstimatedPreCompactTokenCount);
            Assert.Equal(12345, context.ResolvedMaxTokens);
            return Task.FromResult<QueryCompactionResult?>(
                new QueryCompactionResult(
                    ChatMessageFactory.CreateCompactBoundaryMessage("auto", 500),
                    [ChatMessageFactory.CreateUserMessage("summary", isMeta: true)],
                    [],
                    [],
                    RawSummary: "raw-summary"));
        }
    }

    private sealed class RecordingAttachmentBuilder : IQueryReactiveCompactPostCompactAttachmentBuilder
    {
        private readonly List<string> _steps;

        public RecordingAttachmentBuilder(List<string> steps)
        {
            _steps = steps;
        }

        public Task<IReadOnlyList<ChatMessage>> BuildAsync(
            QueryReactiveCompactExecutionContext context,
            QueryReactiveCompactPromptBuildResult prompt,
            QueryReactiveCompactHookRunResult hookResult,
            QueryCompactionResult compacted,
            CancellationToken cancellationToken = default)
        {
            _steps.Add("attachments");
            return Task.FromResult<IReadOnlyList<ChatMessage>>(
                [ChatMessageFactory.CreateText(MessageRole.System, "plan-attachment")]);
        }
    }
}
