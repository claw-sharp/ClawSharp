using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpElicitationServiceTests
{
    [Fact]
    public async Task HandleRequestAsync_QueuesRequestAndResolvesWhenResponded()
    {
        var eventSink = new InMemoryEventSink();
        var service = new McpElicitationService(eventSink);
        using var cts = new CancellationTokenSource();

        var pending = service.HandleRequestAsync(
            "server-one",
            new McpElicitationRequestContext(
                "request-1",
                new McpUrlElicitRequestParams("Open this URL", "https://example.test", "elicitation-1"),
                cts.Token));

        var queued = Assert.Single(service.SnapshotQueue());
        Assert.Equal("server-one", queued.ServerName);
        Assert.NotNull(queued.WaitingState);
        Assert.Equal("Skip confirmation", queued.WaitingState!.ActionLabel);

        Assert.True(service.TryRespond("server-one", "request-1", new McpElicitResult("accept")));
        var result = await pending;

        Assert.Equal("accept", result.Action);
        Assert.Empty(service.SnapshotQueue());
        Assert.Contains(eventSink.Events, e => e.Message.Contains("MCP elicitation queued", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleRequestAsync_ReturnsCancelWhenTokenIsCancelled()
    {
        var service = new McpElicitationService(new InMemoryEventSink());
        using var cts = new CancellationTokenSource();
        var pending = service.HandleRequestAsync(
            "server-one",
            new McpElicitationRequestContext(
                "request-1",
                new McpFormElicitRequestParams("Provide input"),
                cts.Token));

        cts.Cancel();
        var result = await pending;

        Assert.Equal("cancel", result.Action);
        Assert.Empty(service.SnapshotQueue());
    }

    [Fact]
    public void MarkCompleted_SetsCompletedFlagForMatchingUrlRequest()
    {
        var service = new McpElicitationService(new InMemoryEventSink());
        using var cts = new CancellationTokenSource();
        _ = service.HandleRequestAsync(
            "server-one",
            new McpElicitationRequestContext(
                "request-1",
                new McpUrlElicitRequestParams("Open this URL", "https://example.test", "elicitation-1"),
                cts.Token));

        service.MarkCompleted("server-one", "elicitation-1");

        var queued = Assert.Single(service.SnapshotQueue());
        Assert.True(queued.Completed);
    }

    [Fact]
    public async Task RegisterHandlers_BindsSessionCallbacksToService()
    {
        var service = new McpElicitationService(new InMemoryEventSink());
        var session = new RecordingElicitationSession();
        service.RegisterHandlers(session, "server-one");

        Assert.NotNull(session.RequestHandler);
        Assert.NotNull(session.CompletionHandler);

        using var cts = new CancellationTokenSource();
        var pending = session.RequestHandler!(
            new McpElicitationRequestContext(
                "request-1",
                new McpFormElicitRequestParams("Provide input"),
                cts.Token));
        Assert.True(service.TryRespond("server-one", "request-1", new McpElicitResult("decline")));
        var result = await pending;

        Assert.Equal("decline", result.Action);
        session.CompletionHandler!("ignored");
    }

    private sealed class RecordingElicitationSession : IMcpElicitationSession
    {
        public Func<McpElicitationRequestContext, Task<McpElicitResult>>? RequestHandler { get; private set; }
        public Action<string>? CompletionHandler { get; private set; }

        public void SetElicitationRequestHandler(Func<McpElicitationRequestContext, Task<McpElicitResult>> handler)
        {
            RequestHandler = handler;
        }

        public void SetElicitationCompletionHandler(Action<string> handler)
        {
            CompletionHandler = handler;
        }
    }
}
