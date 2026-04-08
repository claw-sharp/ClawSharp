using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;
using ClawSharp.AgentHost.Services;
using ClawSharp.Core;
using ClawSharp.Query;

namespace ClawSharp.AgentHost.Runs;

public sealed class RunCoordinator
{
    private readonly WorkspaceApplicationRegistry _applicationRegistry;
    private readonly RecentProjectStore _recentProjectStore;
    private readonly AgentHostEventDispatcher _eventDispatcher;
    private readonly ConcurrentDictionary<string, RunExecution> _runs = new(StringComparer.Ordinal);

    public RunCoordinator(
        WorkspaceApplicationRegistry applicationRegistry,
        RecentProjectStore recentProjectStore,
        AgentHostEventDispatcher eventDispatcher)
    {
        _applicationRegistry = applicationRegistry;
        _recentProjectStore = recentProjectStore;
        _eventDispatcher = eventDispatcher;
    }

    public async Task<StartRunResponse> StartRunAsync(
        StartRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ThreadId))
        {
            throw new AgentHostException("invalid_request", "threadId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ProjectId))
        {
            throw new AgentHostException("invalid_request", "projectId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            throw new AgentHostException("invalid_request", "prompt is required.");
        }

        var project = await _recentProjectStore.FindByIdAsync(request.ProjectId, cancellationToken);
        if (project is null)
        {
            throw new AgentHostException(
                "project_not_found",
                $"Project '{request.ProjectId}' is not known to AgentHost yet. Open the project first.");
        }

        var app = await _applicationRegistry.GetOrCreateAsync(project.Path, cancellationToken);
        var session = await app.SessionFactory.ResumeAsync(request.ThreadId, cancellationToken);
        if (session is null)
        {
            throw new AgentHostException(
                "thread_not_found",
                $"Thread '{request.ThreadId}' was not found in project '{request.ProjectId}'.");
        }

        app.AppStateStore.SetState(state => ClawSharpAppStateMutations.WithActiveSession(state, session));

        var runId = Guid.NewGuid().ToString("N");
        var runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_runs.TryAdd(runId, new RunExecution(runId, request.ProjectId, request.ThreadId, runCts)))
        {
            runCts.Dispose();
            throw new AgentHostException("run_conflict", "Failed to allocate run id.");
        }

        _ = Task.Run(
            () => ExecuteRunAsync(runId, request.ProjectId, session, request.Prompt.Trim(), app, runCts),
            CancellationToken.None);

        return new StartRunResponse(runId, request.ThreadId, DateTimeOffset.UtcNow);
    }

    public async Task<StartRunResponse> RetryRunAsync(
        RetryRunRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ThreadId))
        {
            throw new AgentHostException("invalid_request", "threadId is required.");
        }

        var (projectId, session) = await ResolveSessionAsync(request.ProjectId, request.ThreadId, cancellationToken);
        var prompt = ResolveRetryPrompt(session, request.FromMessageId);
        return await StartRunAsync(
            new StartRunRequest
            {
                ThreadId = request.ThreadId,
                ProjectId = projectId,
                Prompt = prompt
            },
            cancellationToken);
    }

    public Task<CancelRunResponse> CancelRunAsync(CancelRunRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            throw new AgentHostException("invalid_request", "runId is required.");
        }

        if (_runs.TryGetValue(request.RunId, out var run))
        {
            run.Cancellation.Cancel();
            return Task.FromResult(new CancelRunResponse(request.RunId, true, DateTimeOffset.UtcNow));
        }

        return Task.FromResult(new CancelRunResponse(request.RunId, false, DateTimeOffset.UtcNow));
    }

    private async Task<(string ProjectId, ConversationSession Session)> ResolveSessionAsync(
        string? projectId,
        string threadId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
            if (project is null)
            {
                throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
            }

            var app = await _applicationRegistry.GetOrCreateAsync(project.Path, cancellationToken);
            var session = await app.SessionFactory.ResumeAsync(threadId, cancellationToken);
            if (session is null)
            {
                throw new AgentHostException("thread_not_found", $"Thread '{threadId}' was not found in project '{projectId}'.");
            }

            return (projectId, session);
        }

        var projects = await _recentProjectStore.ListAsync(cancellationToken);
        foreach (var project in projects)
        {
            var app = await _applicationRegistry.GetOrCreateAsync(project.Path, cancellationToken);
            var session = await app.SessionFactory.ResumeAsync(threadId, cancellationToken);
            if (session is not null)
            {
                return (project.ProjectId, session);
            }
        }

        throw new AgentHostException("thread_not_found", $"Thread '{threadId}' was not found.");
    }

    private static string ResolveRetryPrompt(ConversationSession session, string? fromMessageId)
    {
        var messages = session.Messages;
        ChatMessage? userMessage = null;

        if (!string.IsNullOrWhiteSpace(fromMessageId))
        {
            var index = Array.FindLastIndex(messages.ToArray(), message => string.Equals(message.Id, fromMessageId, StringComparison.Ordinal));
            if (index >= 0)
            {
                for (var cursor = index; cursor >= 0; cursor--)
                {
                    if (messages[cursor].Role == MessageRole.User)
                    {
                        userMessage = messages[cursor];
                        break;
                    }
                }
            }
        }

        userMessage ??= messages.LastOrDefault(message => message.Role == MessageRole.User);
        if (userMessage is null || string.IsNullOrWhiteSpace(userMessage.Content))
        {
            throw new AgentHostException("retry_unavailable", $"Thread '{session.Id}' does not contain a retryable user prompt.");
        }

        return userMessage.Content;
    }

    private async Task ExecuteRunAsync(
        string runId,
        string projectId,
        ConversationSession session,
        string prompt,
        Infrastructure.ClawSharpApplication app,
        CancellationTokenSource runCts)
    {
        var toolNamesByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            await _eventDispatcher.PublishAsync(
                "RunStarted",
                new RunStartedEvent(runId, session.Id, projectId, prompt, DateTimeOffset.UtcNow),
                CancellationToken.None);

            var turnRequest = QueryTurnRequest.Create(session, prompt);
            var result = await app.QueryEngine.RunTurnAsync(
                session,
                turnRequest,
                onTextDelta: (delta, token) =>
                    _eventDispatcher.PublishAsync(
                        "RunTextDelta",
                        new RunTextDeltaEvent(runId, session.Id, delta, DateTimeOffset.UtcNow),
                        token),
                onMessage: null,
                onEvent: (consumerEvent, token) => HandleConsumerEventAsync(
                    runId,
                    session,
                    toolNamesByToolUseId,
                    consumerEvent,
                    token),
                cancellationToken: runCts.Token);

            await _eventDispatcher.PublishAsync(
                "RunCompleted",
                new RunCompletedEvent(
                    runId,
                    session.Id,
                    result.Terminal.Reason.ToString(),
                    result.Terminal.ErrorMessage,
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            await _eventDispatcher.PublishAsync(
                "RunCompleted",
                new RunCompletedEvent(
                    runId,
                    session.Id,
                    QueryTerminalReason.AbortedStreaming.ToString(),
                    "Run cancelled.",
                    DateTimeOffset.UtcNow),
                CancellationToken.None);
        }
        catch (Exception error)
        {
            await _eventDispatcher.PublishAsync(
                "RunFailed",
                new RunFailedEvent(runId, session.Id, error.Message, DateTimeOffset.UtcNow),
                CancellationToken.None);
        }
        finally
        {
            _runs.TryRemove(runId, out _);
            runCts.Dispose();
        }
    }

    private Task HandleConsumerEventAsync(
        string runId,
        ConversationSession session,
        IDictionary<string, string> toolNamesByToolUseId,
        QueryConsumerEvent consumerEvent,
        CancellationToken cancellationToken)
    {
        return consumerEvent switch
        {
            QueryMessageConsumerEvent messageEvent => HandleMessageAsync(runId, session, toolNamesByToolUseId, messageEvent.Message, cancellationToken),
            QueryLoopTerminalConsumerEvent => Task.CompletedTask,
            _ => Task.CompletedTask,
        };
    }

    private Task HandleMessageAsync(
        string runId,
        ConversationSession session,
        IDictionary<string, string> toolNamesByToolUseId,
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        foreach (var block in message.ContentBlocks)
        {
            switch (block.Kind)
            {
                case MessageContentKind.ToolUse:
                {
                    var toolUseId = GetMetadata(block, "toolUseId") ?? Guid.NewGuid().ToString("N");
                    var toolName = block.Name ?? "tool";
                    toolNamesByToolUseId[toolUseId] = toolName;
                    var detail = RenderToolUseDetail(block.Value);
                    _ = _eventDispatcher.PublishAsync(
                        "RunToolProgress",
                        new RunToolProgressEvent(
                            runId,
                            session.Id,
                            toolUseId,
                            null,
                            toolName,
                            toolName,
                            detail,
                            "requested",
                            message.Timestamp),
                        cancellationToken);
                    break;
                }
                case MessageContentKind.Progress:
                {
                    var toolUseId = GetMetadata(block, "toolUseId") ?? Guid.NewGuid().ToString("N");
                    var parentToolUseId = GetMetadata(block, "parentToolUseId");
                    var toolName = parentToolUseId is not null && toolNamesByToolUseId.TryGetValue(parentToolUseId, out var knownToolName)
                        ? knownToolName
                        : "tool";
                    var (label, detail, stage) = RenderProgress(block.Value, toolName);
                    _ = _eventDispatcher.PublishAsync(
                        "RunToolProgress",
                        new RunToolProgressEvent(
                            runId,
                            session.Id,
                            toolUseId,
                            parentToolUseId,
                            toolName,
                            label,
                            detail,
                            stage,
                            message.Timestamp),
                        cancellationToken);
                    break;
                }
                case MessageContentKind.ToolResult:
                {
                    var toolUseId = GetMetadata(block, "toolUseId") ?? Guid.NewGuid().ToString("N");
                    var toolName = block.Name
                                   ?? (toolNamesByToolUseId.TryGetValue(toolUseId, out var knownToolName) ? knownToolName : "tool");
                    _ = _eventDispatcher.PublishAsync(
                        "RunToolResult",
                        new RunToolResultEvent(
                            runId,
                            session.Id,
                            toolUseId,
                            toolName,
                            block.Value,
                            message.Timestamp),
                        cancellationToken);
                    break;
                }
            }
        }

        var hasRenderableText = message.ContentBlocks.Any(static block =>
            block.Kind == MessageContentKind.Text ||
            block.Kind == MessageContentKind.ToolResult);
        if (!hasRenderableText)
        {
            return Task.CompletedTask;
        }

        return _eventDispatcher.PublishAsync(
            "RunMessageCompleted",
            new RunMessageCompletedEvent(
                runId,
                session.Id,
                new ThreadMessageDto(
                    message.Id,
                    session.Id,
                    message.Role.ToString().ToLowerInvariant(),
                    message.Content,
                    message.Timestamp),
                message.Timestamp),
            cancellationToken);
    }

    private static string? GetMetadata(MessageContentBlock block, string key)
    {
        return block.Metadata is not null && block.Metadata.TryGetValue(key, out var value)
            ? value
            : null;
    }

    private static string? RenderToolUseDetail(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        try
        {
            var json = JsonNode.Parse(arguments);
            return json?.ToJsonString();
        }
        catch
        {
            return arguments;
        }
    }

    private static (string Label, string? Detail, string Stage) RenderProgress(string payload, string toolName)
    {
        try
        {
            if (JsonNode.Parse(payload) is not JsonObject progress)
            {
                return ($"{toolName} progress", payload, "running");
            }

            var progressType = progress["type"]?.GetValue<string>();
            var label = progress["taskDescription"]?.GetValue<string>()
                        ?? progress["label"]?.GetValue<string>()
                        ?? progress["status"]?.GetValue<string>()
                        ?? progressType
                        ?? $"{toolName} progress";
            var detail = progress["output"]?.ToJsonString()
                         ?? progress["fullOutput"]?.ToJsonString()
                         ?? progress["message"]?.GetValue<string>()
                         ?? progress["taskType"]?.GetValue<string>();
            return (label, detail, NormalizeStage(progressType));
        }
        catch
        {
            return ($"{toolName} progress", payload, "running");
        }
    }

    private static string NormalizeStage(string? progressType)
    {
        if (string.IsNullOrWhiteSpace(progressType))
        {
            return "running";
        }

        return progressType switch
        {
            "waiting_for_task" => "waiting",
            "bash_progress" => "running",
            "shell_progress" => "running",
            _ => progressType,
        };
    }

    private sealed record RunExecution(
        string RunId,
        string ProjectId,
        string ThreadId,
        CancellationTokenSource Cancellation);
}
