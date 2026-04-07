using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class McpElicitationService
{
    private readonly IEventSink _eventSink;
    private readonly object _lock = new();
    private readonly List<McpElicitationRequestEvent> _queue = [];

    public McpElicitationService(IEventSink eventSink)
    {
        _eventSink = eventSink;
    }

    public IReadOnlyList<McpElicitationRequestEvent> SnapshotQueue()
    {
        lock (_lock)
        {
            return _queue.ToArray();
        }
    }

    public void RegisterHandlers(IMcpElicitationSession session, string serverName)
    {
        session.SetElicitationRequestHandler(
            context => HandleRequestAsync(serverName, context));
        session.SetElicitationCompletionHandler(
            elicitationId => MarkCompleted(serverName, elicitationId));
    }

    public Task<McpElicitResult> HandleRequestAsync(
        string serverName,
        McpElicitationRequestContext context)
    {
        var tcs = new TaskCompletionSource<McpElicitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration registration = default;

        bool Respond(McpElicitResult result)
        {
            registration.Dispose();
            var resolved = tcs.TrySetResult(result);
            if (resolved)
            {
                Remove(serverName, context.RequestId);
            }

            return resolved;
        }

        if (context.CancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new McpElicitResult("cancel"));
        }

        registration = context.CancellationToken.Register(
            () => Respond(new McpElicitResult("cancel")));

        var waitingState = context.Params is McpUrlElicitRequestParams
            ? new McpElicitationWaitingState("Skip confirmation")
            : null;

        var requestEvent = new McpElicitationRequestEvent(
            serverName,
            context.RequestId,
            context.Params,
            context.CancellationToken,
            Respond,
            waitingState);

        lock (_lock)
        {
            _queue.Add(requestEvent);
        }

        PublishNotification(
            $"MCP elicitation queued for server \"{serverName}\"",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["serverName"] = serverName,
                ["requestId"] = context.RequestId,
                ["mode"] = context.Params.Mode
            });

        return tcs.Task;
    }

    public bool TryRespond(string serverName, string requestId, McpElicitResult result)
    {
        lock (_lock)
        {
            var index = _queue.FindIndex(
                queued => string.Equals(queued.ServerName, serverName, StringComparison.Ordinal) &&
                          string.Equals(queued.RequestId, requestId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            return _queue[index].Respond(result);
        }
    }

    public bool TryDismissWaitingState(string serverName, string requestId, string action)
    {
        lock (_lock)
        {
            var index = _queue.FindIndex(
                queued => string.Equals(queued.ServerName, serverName, StringComparison.Ordinal) &&
                          string.Equals(queued.RequestId, requestId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            _queue[index].OnWaitingDismiss?.Invoke(action);
            return true;
        }
    }

    public void MarkCompleted(string serverName, string elicitationId)
    {
        lock (_lock)
        {
            var index = _queue.FindIndex(
                queued => string.Equals(queued.ServerName, serverName, StringComparison.Ordinal) &&
                          queued.Params is McpUrlElicitRequestParams url &&
                          string.Equals(url.ElicitationId, elicitationId, StringComparison.Ordinal));
            if (index < 0)
            {
                return;
            }

            _queue[index] = _queue[index] with { Completed = true };
        }

        PublishNotification(
            $"MCP server \"{serverName}\" confirmed elicitation {elicitationId} complete",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["serverName"] = serverName,
                ["elicitationId"] = elicitationId
            });
    }

    private void Remove(string serverName, string requestId)
    {
        lock (_lock)
        {
            _queue.RemoveAll(
                queued => string.Equals(queued.ServerName, serverName, StringComparison.Ordinal) &&
                          string.Equals(queued.RequestId, requestId, StringComparison.Ordinal));
        }
    }

    private void PublishNotification(string message, IReadOnlyDictionary<string, string> metadata)
    {
        _eventSink.Publish(
            new AppEvent(
                AppEventType.NotificationRaised,
                message,
                DateTimeOffset.UtcNow,
                metadata));
    }
}
