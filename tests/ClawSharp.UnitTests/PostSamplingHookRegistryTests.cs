// TS origin: ./utils/hooks/postSamplingHooks.ts
// TS parity status: focused coverage for the internal post-sampling hook registry contract; these tests intentionally stay at the register/clear/execute boundary until live model-backed query execution is ported.
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class PostSamplingHookRegistryTests
{
    [Fact]
    public async Task ExecuteAsync_Invokes_Hooks_In_Registration_Order_With_Shared_Context()
    {
        var invocations = new List<string>();
        var registry = new PostSamplingHookRegistry();
        registry.Register(context =>
        {
            invocations.Add($"first:{context.QuerySource}:{context.Messages.Count}");
            return ValueTask.CompletedTask;
        });
        registry.Register(context =>
        {
            invocations.Add($"second:{context.SystemPrompt.Count}:{context.ToolUseContext.MainLoopModel}");
            return ValueTask.CompletedTask;
        });

        await registry.ExecuteAsync(CreateContext());

        Assert.Equal(
            ["first:repl:1", "second:2:claude-3-7-sonnet"],
            invocations);
    }

    [Fact]
    public async Task Clear_Removes_Previously_Registered_Hooks()
    {
        var invoked = false;
        var registry = new PostSamplingHookRegistry();
        registry.Register(_ =>
        {
            invoked = true;
            return ValueTask.CompletedTask;
        });
        registry.Clear();

        await registry.ExecuteAsync(CreateContext());

        Assert.False(invoked);
    }

    [Fact]
    public async Task ExecuteAsync_Swallows_Hook_Errors_And_Reports_Them()
    {
        var reported = new List<Exception>();
        var invocations = new List<string>();
        var registry = new PostSamplingHookRegistry(reported.Add);
        registry.Register(_ => throw new InvalidOperationException("first-failure"));
        registry.Register(context =>
        {
            invocations.Add(context.Messages[0].Content);
            return ValueTask.CompletedTask;
        });

        await registry.ExecuteAsync(CreateContext());

        var exception = Assert.Single(reported);
        Assert.Equal("first-failure", exception.Message);
        Assert.Equal(["assistant output"], invocations);
    }

    [Fact]
    public async Task ModelBackedIterationRunner_Preserves_NotImplemented_Boundary_With_PostSampling_Registry()
    {
        var runner = new ModelBackedIterationRunner(new PostSamplingHookRegistry());
        var sessionRoot = Path.Combine(Path.GetTempPath(), "clawsharp-post-sampling-runner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionRoot);
        var session = new ConversationSession("session-post-hook", sessionRoot, Path.Combine(sessionRoot, "session.jsonl"));

        await Assert.ThrowsAsync<QueryExecutionNotImplementedException>(() =>
            runner.RunAsync(
                QueryTurnRequest.Create(session, "hello"),
                QueryLoopStateFactory.CreateInitial([]),
                session,
                new ClawSharpSettings(),
                static (_, _) => Task.CompletedTask));
    }

    private static ReplHookContext CreateContext()
    {
        return new ReplHookContext(
            [ChatMessageFactory.CreateText(MessageRole.Assistant, "assistant output")],
            ["system-prefix", "system-dynamic"],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["cwd"] = "d:/Working/claude-code"
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["platform"] = "windows"
            },
            QueryToolUseContextState.Empty with { MainLoopModel = "claude-3-7-sonnet" },
            QuerySource: "repl");
    }
}
