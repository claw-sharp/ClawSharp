using System.Text;
using ClawSharp.Core;
using ClawSharp.Tasks;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.Query;

public sealed class LocalMainSessionTaskService
{
    public const string MainSessionAgentType = "main-session";

    private const int MaxRecentActivities = 5;
    private const string NotificationDescription = "Background session";

    private readonly TaskRegistry _tasks;
    private readonly IQueuedCommandQueue _queuedCommandQueue;
    private readonly QueryEngine _queryEngine;
    private readonly IClawSharpAppStateStore _appStateStore;
    private readonly ITranscriptStore _transcriptStore;

    public LocalMainSessionTaskService(
        TaskRegistry tasks,
        IQueuedCommandQueue queuedCommandQueue,
        QueryEngine queryEngine,
        IClawSharpAppStateStore appStateStore,
        ITranscriptStore transcriptStore)
    {
        _tasks = tasks;
        _queuedCommandQueue = queuedCommandQueue;
        _queryEngine = queryEngine;
        _appStateStore = appStateStore;
        _transcriptStore = transcriptStore;
    }

    public async Task<LocalAgentTask> StartBackgroundSessionAsync(
        ConversationSession parentSession,
        QueryTurnRequest request,
        string description,
        CancellationToken cancellationToken = default)
    {
        var seedMessages = parentSession.Messages.ToArray();
        var backgroundCancellationSource = new CancellationTokenSource();
        var appState = _appStateStore.GetState();
        var task = await _tasks.CreateMainSessionTaskForSessionAsync(
            parentSession.Id,
            description,
            model: request.InitialToolUseContext?.MainLoopModel ?? appState.MainLoopModel,
            cancellationSource: backgroundCancellationSource,
            messages: seedMessages,
            cancellationToken: cancellationToken);

        await WriteAgentMetadataAsync(parentSession, task.Id, description, cancellationToken);
        var backgroundSession = CreateAgentSession(parentSession, task.Id, seedMessages);
        await _transcriptStore.RecordTranscriptAsync(backgroundSession, backgroundSession.Messages, cancellationToken);

        _ = Task.Run(
            () => RunBackgroundSessionAsync(
                parentSession,
                task,
                seedMessages,
                request,
                backgroundCancellationSource.Token),
            CancellationToken.None);

        return task;
    }

    public IReadOnlyList<ChatMessage>? ForegroundMainSessionTask(string taskId)
    {
        IReadOnlyList<ChatMessage>? taskMessages = null;
        var previousForegroundedTaskId = _appStateStore.GetState().ForegroundedTaskId;

        _tasks.SetAppState(
            state =>
            {
                if (!state.Tasks.TryGetValue(taskId, out var candidate) ||
                    candidate is not LocalAgentTask agentTask ||
                    !IsMainSessionTask(agentTask))
                {
                    return state;
                }

                taskMessages = agentTask.Messages;
                var nextTasks = new Dictionary<string, ClawSharpTask>(state.Tasks, StringComparer.Ordinal)
                {
                    [taskId] = agentTask with { IsBackgrounded = false }
                };

                if (!string.IsNullOrWhiteSpace(previousForegroundedTaskId) &&
                    !string.Equals(previousForegroundedTaskId, taskId, StringComparison.Ordinal) &&
                    state.Tasks.TryGetValue(previousForegroundedTaskId, out var previous) &&
                    previous is LocalAgentTask previousAgentTask &&
                    IsMainSessionTask(previousAgentTask))
                {
                    nextTasks[previousForegroundedTaskId] = previousAgentTask with { IsBackgrounded = true };
                }

                return new TaskAppState(nextTasks);
            });

        if (taskMessages is null)
        {
            return null;
        }

        _appStateStore.SetState(state => ClawSharpAppStateMutations.WithForegroundedTaskId(state, taskId));
        return taskMessages;
    }

    public bool TryBackgroundForegroundedTask()
    {
        var foregroundedTaskId = _appStateStore.GetState().ForegroundedTaskId;
        if (string.IsNullOrWhiteSpace(foregroundedTaskId))
        {
            return false;
        }

        var updated = _tasks.TryUpdate(
            foregroundedTaskId,
            task =>
            {
                if (task is not LocalAgentTask agentTask || !IsMainSessionTask(agentTask))
                {
                    return task;
                }

                return agentTask with { IsBackgrounded = true };
            });
        if (!updated)
        {
            return false;
        }

        _appStateStore.SetState(state => ClawSharpAppStateMutations.WithForegroundedTaskId(state, null));
        return true;
    }

    private async Task RunBackgroundSessionAsync(
        ConversationSession parentSession,
        LocalAgentTask task,
        IReadOnlyList<ChatMessage> seedMessages,
        QueryTurnRequest request,
        CancellationToken cancellationToken)
    {
        var backgroundSession = CreateAgentSession(parentSession, task.Id, seedMessages);
        var progress = new MainSessionProgressTracker();
        var backgroundRequest = request with
        {
            SessionId = backgroundSession.Id,
            AbortReason = QueryAbortReason.Interrupt
        };

        try
        {
            var result = await _queryEngine.RunPreparedTurnAsync(
                backgroundSession,
                backgroundRequest,
                onTextDelta: null,
                onMessage: (message, innerCancellationToken) =>
                {
                    progress.Observe(message);
                    _tasks.TryUpdate(
                        task.Id,
                        currentTask =>
                        {
                            if (currentTask is not LocalAgentTask agentTask || agentTask.Status != TaskStatus.Running)
                            {
                                return currentTask;
                            }

                            return agentTask with
                            {
                                Messages = AppendMessage(agentTask.Messages, message),
                                Progress = progress.ToProgress()
                            };
                        });
                    return Task.CompletedTask;
                },
                cancellationToken: cancellationToken);

            CompleteMainSessionTask(task.Id, success: true, GetFinalResultText(result), progress);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _tasks.TryUpdate(
                task.Id,
                currentTask =>
                {
                    if (currentTask is not LocalAgentTask agentTask || !IsMainSessionTask(agentTask))
                    {
                        return currentTask;
                    }

                    return agentTask with
                    {
                        Notified = true,
                        CancellationSource = null,
                        Messages = KeepLastMessage(agentTask.Messages)
                    };
                });
        }
        catch (Exception exception)
        {
            CompleteMainSessionTask(task.Id, success: false, exception.Message, progress);
        }
    }

    private void CompleteMainSessionTask(
        string taskId,
        bool success,
        string? resultText,
        MainSessionProgressTracker progress)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var finalStatus = success ? TaskStatus.Completed : TaskStatus.Failed;
        var wasBackgrounded = true;
        string? toolUseId = null;

        _tasks.TryUpdate(
            taskId,
            currentTask =>
            {
                if (currentTask is not LocalAgentTask agentTask || agentTask.Status != TaskStatus.Running)
                {
                    return currentTask;
                }

                wasBackgrounded = agentTask.IsBackgrounded;
                toolUseId = agentTask.ToolUseId;
                return agentTask with
                {
                    Status = finalStatus,
                    EndTime = completedAt,
                    Result = success ? resultText : null,
                    Error = success ? null : resultText,
                    CancellationSource = null,
                    Progress = progress.ToProgress(),
                    Messages = KeepLastMessage(agentTask.Messages),
                    EvictAfter = TaskRegistry.ResolveLocalAgentEvictAfter(agentTask.Retain, completedAt)
                };
            });

        _tasks.TryEvictOutputState(taskId);

        if (wasBackgrounded)
        {
            EnqueueMainSessionNotification(taskId, success ? "completed" : "failed", toolUseId);
            return;
        }

        _tasks.TryMarkNotified(taskId);
    }

    private void EnqueueMainSessionNotification(string taskId, string status, string? toolUseId)
    {
        if (!_tasks.TryMarkNotified(taskId) || !_tasks.TryGet(taskId, out var currentTask) || currentTask is not LocalAgentTask agentTask)
        {
            return;
        }

        var toolUseIdLine = string.IsNullOrWhiteSpace(toolUseId)
            ? string.Empty
            : $"{Environment.NewLine}<tool-use-id>{EscapeXml(toolUseId)}</tool-use-id>";
        var summary = status == "completed"
            ? $"Background session \"{NotificationDescription}\" completed"
            : $"Background session \"{NotificationDescription}\" failed";

        var message = new StringBuilder()
            .AppendLine("<task-notification>")
            .Append("<task-id>").Append(EscapeXml(taskId)).AppendLine("</task-id>")
            .Append(toolUseIdLine)
            .AppendLine()
            .Append("<output-file>").Append(EscapeXml(agentTask.OutputFile)).AppendLine("</output-file>")
            .Append("<status>").Append(EscapeXml(status)).AppendLine("</status>")
            .Append("<summary>").Append(EscapeXml(summary)).AppendLine("</summary>")
            .Append("</task-notification>")
            .ToString();

        _queuedCommandQueue.EnqueuePendingNotification(new QueuedCommand(message, PromptInputMode.TaskNotification));
    }

    private static ConversationSession CreateAgentSession(
        ConversationSession parentSession,
        string taskId,
        IEnumerable<ChatMessage> seedMessages)
    {
        var transcriptPath = TaskOutputStoragePaths.GetAgentTranscriptPath(
            parentSession.ProjectDirectory,
            parentSession.Id,
            taskId);
        var session = new ConversationSession(taskId, parentSession.ProjectDirectory, transcriptPath);
        foreach (var message in seedMessages)
        {
            session.Add(message);
        }

        return session;
    }

    private static async Task WriteAgentMetadataAsync(
        ConversationSession parentSession,
        string taskId,
        string description,
        CancellationToken cancellationToken)
    {
        var path = TaskOutputStoragePaths.GetAgentMetadataPath(
            parentSession.ProjectDirectory,
            parentSession.Id,
            taskId);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(
            path,
            System.Text.Json.JsonSerializer.Serialize(new AgentMetadata(MainSessionAgentType, Description: description)),
            cancellationToken);
    }

    private static IReadOnlyList<ChatMessage> AppendMessage(IReadOnlyList<ChatMessage>? messages, ChatMessage message)
    {
        if (messages is null || messages.Count == 0)
        {
            return [message];
        }

        return [.. messages, message];
    }

    private static IReadOnlyList<ChatMessage>? KeepLastMessage(IReadOnlyList<ChatMessage>? messages)
    {
        if (messages is null || messages.Count == 0)
        {
            return messages;
        }

        return [messages[^1]];
    }

    private static bool IsMainSessionTask(LocalAgentTask task)
    {
        return string.Equals(task.AgentType, MainSessionAgentType, StringComparison.Ordinal);
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private static string GetFinalResultText(QueryResult result)
    {
        var assistantText = result.AssistantMessage?.Content?.Trim();
        if (!string.IsNullOrWhiteSpace(assistantText))
        {
            return assistantText;
        }

        for (var index = result.State.Messages.Count - 1; index >= 0; index--)
        {
            var text = string.Join(
                "\n",
                result.State.Messages[index].ContentBlocks
                    .Where(static block => block.Kind == MessageContentKind.Text)
                    .Select(static block => block.Value)
                    .Where(static value => !string.IsNullOrWhiteSpace(value)))
                .Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private sealed class MainSessionProgressTracker
    {
        public int ToolUseCount { get; private set; }

        public int TokenCount { get; private set; }

        public ToolActivity? LastActivity { get; private set; }

        public List<ToolActivity> RecentActivities { get; } = [];

        public string? Summary { get; private set; }

        public void Observe(ChatMessage message)
        {
            if (QueryAttachmentHelpers.TryGetOutputTokenUsageAttachment(message, out var usageAttachment) &&
                usageAttachment is not null)
            {
                TokenCount = usageAttachment.Session;
            }

            if (message.Role == MessageRole.Assistant)
            {
                foreach (var block in message.ContentBlocks.Where(static block => block.Kind == MessageContentKind.ToolUse))
                {
                    ToolUseCount++;
                    var activity = CreateActivity(block);
                    LastActivity = activity;
                    RecentActivities.Add(activity);
                    if (RecentActivities.Count > MaxRecentActivities)
                    {
                        RecentActivities.RemoveAt(0);
                    }
                }
            }

            var text = string.Join(
                "\n",
                message.ContentBlocks
                    .Where(static block => block.Kind == MessageContentKind.Text)
                    .Select(static block => block.Value)
                    .Where(static value => !string.IsNullOrWhiteSpace(value)))
                .Trim();
            if (!string.IsNullOrWhiteSpace(text) && message.Role == MessageRole.Assistant)
            {
                Summary = text;
            }
        }

        public AgentProgress ToProgress()
        {
            return new AgentProgress(
                ToolUseCount,
                TokenCount,
                LastActivity,
                RecentActivities.ToArray(),
                Summary);
        }

        private static ToolActivity CreateActivity(MessageContentBlock block)
        {
            return new ToolActivity(
                block.Name ?? string.Empty,
                new Dictionary<string, object?>(StringComparer.Ordinal),
                ActivityDescription: null,
                IsSearch: string.Equals(block.Name, "Glob", StringComparison.Ordinal) ||
                          string.Equals(block.Name, "Grep", StringComparison.Ordinal),
                IsRead: string.Equals(block.Name, "Read", StringComparison.Ordinal));
        }
    }
}
