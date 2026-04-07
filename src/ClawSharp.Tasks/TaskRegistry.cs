using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ClawSharp.Core;

namespace ClawSharp.Tasks;

public sealed class TaskRegistry : ITaskAppStateStore
{
    public static readonly TimeSpan PanelGracePeriod = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan DefaultStallCheckInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultStallThreshold = TimeSpan.FromSeconds(45);
    private const int DefaultStallTailBytes = 1024;
    private static readonly Regex[] PromptPatterns =
    [
        new(@"\(y/n\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\[y/n\]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\(yes/no\)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\b(?:Do you|Would you|Shall I|Are you sure|Ready to)\b.*\?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Press (any key|Enter)", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Continue\?", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"Overwrite\?", RegexOptions.IgnoreCase | RegexOptions.Compiled)
    ];

    private readonly Lock _stateLock = new();
    private ConcurrentDictionary<string, ClawSharpTask> _tasks = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, IReadOnlyList<TodoItem>> _todos = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, IReadOnlyDictionary<string, BoardTask>> _boardTasks = new(StringComparer.Ordinal);
    private readonly string _workspaceRoot;
    private readonly DiskTaskOutputStore _taskOutputStore;
    private readonly IQueuedCommandQueue _queuedCommandQueue;
    private readonly IEventSink _eventSink;
    private readonly IClawSharpAppStateStore? _appStateStore;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _stallWatchdogs = new(StringComparer.Ordinal);
    private readonly TimeSpan _stallCheckInterval;
    private readonly TimeSpan _stallThreshold;
    private readonly int _stallTailBytes;

    public TaskRegistry(
        string? workspaceRoot = null,
        DiskTaskOutputStore? taskOutputStore = null,
        IQueuedCommandQueue? queuedCommandQueue = null,
        IEventSink? eventSink = null,
        IClawSharpAppStateStore? appStateStore = null,
        TimeSpan? stallCheckInterval = null,
        TimeSpan? stallThreshold = null,
        int stallTailBytes = DefaultStallTailBytes)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot ?? Environment.CurrentDirectory);
        _taskOutputStore = taskOutputStore ?? new DiskTaskOutputStore();
        _queuedCommandQueue = queuedCommandQueue ?? new NullQueuedCommandQueue();
        _eventSink = eventSink ?? new NullEventSink();
        _appStateStore = appStateStore;
        _stallCheckInterval = stallCheckInterval ?? DefaultStallCheckInterval;
        _stallThreshold = stallThreshold ?? DefaultStallThreshold;
        _stallTailBytes = stallTailBytes;
    }

    public IReadOnlyCollection<ClawSharpTask> GetAll()
    {
        lock (_stateLock)
        {
            return _tasks.Values
                .OrderBy(task => task.StartTime)
                .ToArray();
        }
    }

    public TaskAppState GetAppState()
    {
        lock (_stateLock)
        {
            return new TaskAppState(
                new Dictionary<string, ClawSharpTask>(_tasks, StringComparer.Ordinal),
                new Dictionary<string, IReadOnlyList<TodoItem>>(_todos, StringComparer.Ordinal),
                new Dictionary<string, IReadOnlyDictionary<string, BoardTask>>(_boardTasks, StringComparer.Ordinal));
        }
    }

    public void SetAppState(Func<TaskAppState, TaskAppState> updater)
    {
        lock (_stateLock)
        {
            var previousState = new TaskAppState(
                new Dictionary<string, ClawSharpTask>(_tasks, StringComparer.Ordinal),
                new Dictionary<string, IReadOnlyList<TodoItem>>(_todos, StringComparer.Ordinal),
                new Dictionary<string, IReadOnlyDictionary<string, BoardTask>>(_boardTasks, StringComparer.Ordinal));
            var updatedState = updater(previousState);
            
            _tasks = new ConcurrentDictionary<string, ClawSharpTask>(updatedState.Tasks, StringComparer.Ordinal);
            _todos = new ConcurrentDictionary<string, IReadOnlyList<TodoItem>>(updatedState.Todos, StringComparer.Ordinal);
            _boardTasks = new ConcurrentDictionary<string, IReadOnlyDictionary<string, BoardTask>>(updatedState.BoardTasks, StringComparer.Ordinal);
            SyncAppStateLocked();
        }
    }

    public ClawSharpTask Create(string description, TaskStatus status = TaskStatus.Pending)
    {
        var task = new ClawSharpTask(
            Id: $"task-{Guid.NewGuid():N}"[..13],
            Type: TaskType.LocalBash,
            Description: description,
            Status: status,
            StartTime: DateTimeOffset.UtcNow,
            OutputFile: string.Empty);

        lock (_stateLock)
        {
            _tasks[task.Id] = task;
            SyncAppStateLocked();
        }

        return task;
    }

    public async Task<ClawSharpTask> CreateForSessionAsync(
        string sessionId,
        string description,
        TaskType type = TaskType.LocalBash,
        TaskStatus status = TaskStatus.Pending,
        string? toolUseId = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = TaskIdGenerator.Generate(type);
        var outputFile = TaskOutputStoragePaths.GetTaskOutputPath(_workspaceRoot, sessionId, taskId);
        await InitializeTaskOutputAsync(type, sessionId, taskId, outputFile, cancellationToken);

        var task = new ClawSharpTask(
            Id: taskId,
            Type: type,
            Description: description,
            Status: status,
            StartTime: DateTimeOffset.UtcNow,
            OutputFile: outputFile,
            ToolUseId: toolUseId);

        lock (_stateLock)
        {
            _tasks[task.Id] = task;
            SyncAppStateLocked();
        }

        return task;
    }

    public async Task<LocalBashTask> CreateLocalBashForSessionAsync(
        string sessionId,
        string description,
        string command,
        TaskStatus status = TaskStatus.Pending,
        string? toolUseId = null,
        BashTaskKind kind = BashTaskKind.Bash,
        bool isBackgrounded = true,
        string? agentId = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = TaskIdGenerator.Generate(TaskType.LocalBash);
        var outputFile = TaskOutputStoragePaths.GetTaskOutputPath(_workspaceRoot, sessionId, taskId);
        await _taskOutputStore.InitTaskOutputAsync(outputFile, cancellationToken);

        var task = new LocalBashTask(
            Id: taskId,
            Description: description,
            Status: status,
            StartTime: DateTimeOffset.UtcNow,
            OutputFile: outputFile,
            Command: command,
            Kind: kind,
            IsBackgrounded: isBackgrounded,
            AgentId: agentId,
            ToolUseId: toolUseId);

        lock (_stateLock)
        {
            _tasks[task.Id] = task;
            SyncAppStateLocked();
        }

        return task;
    }

    public Task<LocalBashTask> CreateLocalBashForExistingOutputAsync(
        string sessionId,
        string description,
        string command,
        string outputFile,
        TaskStatus status = TaskStatus.Pending,
        string? toolUseId = null,
        BashTaskKind kind = BashTaskKind.Bash,
        bool isBackgrounded = true,
        string? agentId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var taskId = TaskIdGenerator.Generate(TaskType.LocalBash);
        var task = new LocalBashTask(
            Id: taskId,
            Description: description,
            Status: status,
            StartTime: DateTimeOffset.UtcNow,
            OutputFile: outputFile,
            Command: command,
            Kind: kind,
            IsBackgrounded: isBackgrounded,
            AgentId: agentId,
            ToolUseId: toolUseId);

        lock (_stateLock)
        {
            _tasks[task.Id] = task;
            SyncAppStateLocked();
        }

        return Task.FromResult(task);
    }

    public async Task<LocalAgentTask> CreateLocalAgentForSessionAsync(
        string sessionId,
        string description,
        string? taskId,
        string prompt,
        string agentType,
        TaskStatus status = TaskStatus.Pending,
        string? model = null,
        bool isBackgrounded = true,
        string? worktreePath = null,
        string? worktreeBranch = null,
        string? toolUseId = null,
        CancellationTokenSource? cancellationSource = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedTaskId = taskId ?? TaskIdGenerator.Generate(TaskType.LocalAgent);
        var outputFile = TaskOutputStoragePaths.GetTaskOutputPath(_workspaceRoot, sessionId, resolvedTaskId);
        await InitializeTaskOutputAsync(TaskType.LocalAgent, sessionId, resolvedTaskId, outputFile, cancellationToken);

        var task = new LocalAgentTask(
            Id: resolvedTaskId,
            Description: description,
            Status: status,
            StartTime: DateTimeOffset.UtcNow,
            OutputFile: outputFile,
            Prompt: prompt,
            AgentType: agentType,
            Model: model,
            CancellationSource: cancellationSource,
            IsBackgrounded: isBackgrounded,
            WorktreePath: worktreePath,
            WorktreeBranch: worktreeBranch,
            ToolUseId: toolUseId);

        lock (_stateLock)
        {
            _tasks[task.Id] = task;
            SyncAppStateLocked();
        }

        return task;
    }

    public async Task<LocalAgentTask> CreateMainSessionTaskForSessionAsync(
        string sessionId,
        string description,
        string? model = null,
        CancellationTokenSource? cancellationSource = null,
        IReadOnlyList<ChatMessage>? messages = null,
        string? toolUseId = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = TaskIdGenerator.GenerateMainSession();
        var outputFile = TaskOutputStoragePaths.GetTaskOutputPath(_workspaceRoot, sessionId, taskId);
        await InitializeTaskOutputAsync(TaskType.LocalAgent, sessionId, taskId, outputFile, cancellationToken);

        var task = new LocalAgentTask(
            Id: taskId,
            Description: description,
            Status: TaskStatus.Running,
            StartTime: DateTimeOffset.UtcNow,
            OutputFile: outputFile,
            Prompt: description,
            AgentType: "main-session",
            Model: model,
            CancellationSource: cancellationSource,
            IsBackgrounded: true,
            Retain: false,
            DiskLoaded: false,
            ToolUseId: toolUseId,
            Messages: messages);

        lock (_stateLock)
        {
            _tasks[task.Id] = task;
            SyncAppStateLocked();
        }

        return task;
    }

    public Task<LocalAgentTask> CreateForegroundLocalAgentForSessionAsync(
        string sessionId,
        string description,
        string? taskId,
        string prompt,
        string agentType,
        TaskStatus status = TaskStatus.Running,
        string? model = null,
        string? worktreePath = null,
        string? worktreeBranch = null,
        string? toolUseId = null,
        CancellationTokenSource? cancellationSource = null,
        CancellationToken cancellationToken = default)
    {
        return CreateLocalAgentForSessionAsync(
            sessionId,
            description,
            taskId,
            prompt,
            agentType,
            status,
            model,
            isBackgrounded: false,
            worktreePath,
            worktreeBranch,
            toolUseId,
            cancellationSource,
            cancellationToken);
    }

    public async Task<RemoteAgentTask> CreateRemoteAgentForSessionAsync(
        string sessionId,
        string description,
        string remoteSessionId,
        string command,
        string title,
        TaskStatus status = TaskStatus.Pending,
        string? toolUseId = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = TaskIdGenerator.Generate(TaskType.RemoteAgent);
        var outputFile = TaskOutputStoragePaths.GetTaskOutputPath(_workspaceRoot, sessionId, taskId);
        await _taskOutputStore.InitTaskOutputAsync(outputFile, cancellationToken);

        var task = new RemoteAgentTask(
            Id: taskId,
            Description: description,
            Status: status,
            StartTime: DateTimeOffset.UtcNow,
            OutputFile: outputFile,
            SessionId: remoteSessionId,
            Command: command,
            Title: title,
            ToolUseId: toolUseId);

        lock (_stateLock)
        {
            _tasks[task.Id] = task;
            SyncAppStateLocked();
        }

        return task;
    }

    public async Task<InProcessTeammateTask> CreateInProcessTeammateForSessionAsync(
        string sessionId,
        string description,
        TeammateIdentity identity,
        string prompt,
        TaskStatus status = TaskStatus.Running,
        string? model = null,
        AgentDefinition? selectedAgent = null,
        string? toolUseId = null,
        CancellationToken cancellationToken = default)
    {
        var taskId = TaskIdGenerator.Generate(TaskType.InProcessTeammate);
        var outputFile = TaskOutputStoragePaths.GetTaskOutputPath(_workspaceRoot, sessionId, taskId);
        await _taskOutputStore.InitTaskOutputAsync(outputFile, cancellationToken);

        var task = new InProcessTeammateTask(
            Id: taskId,
            Description: description,
            Status: status,
            StartTime: DateTimeOffset.UtcNow,
            OutputFile: outputFile,
            Identity: identity,
            Prompt: prompt,
            Model: model,
            SelectedAgent: selectedAgent,
            AwaitingPlanApproval: false,
            PermissionMode: identity.PlanModeRequired ? PermissionMode.Plan : PermissionMode.Default,
            PendingUserMessages: [],
            Messages: [],
            IsIdle: false,
            ShutdownRequested: false,
            ToolUseId: toolUseId);

        lock (_stateLock)
        {
            _tasks[task.Id] = task;
            SyncAppStateLocked();
        }

        return task;
    }

    private Task InitializeTaskOutputAsync(
        TaskType type,
        string sessionId,
        string taskId,
        string outputFile,
        CancellationToken cancellationToken)
    {
        if (type == TaskType.LocalAgent)
        {
            var transcriptPath = TaskOutputStoragePaths.GetAgentTranscriptPath(_workspaceRoot, sessionId, taskId);
            return _taskOutputStore.InitTaskOutputAsSymlinkAsync(outputFile, transcriptPath, cancellationToken);
        }

        return _taskOutputStore.InitTaskOutputAsync(outputFile, cancellationToken);
    }

    public bool TryUpdateStatus(string id, TaskStatus status)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing))
            {
                return false;
            }

            _tasks[id] = existing with
            {
                Status = status,
                EndTime = status.IsTerminal() ? DateTimeOffset.UtcNow : existing.EndTime
            };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryGet(string id, out ClawSharpTask? task)
    {
        lock (_stateLock)
        {
            return _tasks.TryGetValue(id, out task);
        }
    }

    public bool TryUpdate(string id, Func<ClawSharpTask, ClawSharpTask> updater)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing))
            {
                return false;
            }

            var updated = updater(existing);
            if (ReferenceEquals(updated, existing))
            {
                return true;
            }

            _tasks[id] = updated;
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryMarkNotified(string id)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing))
            {
                return false;
            }

            if (existing.Notified)
            {
                return false;
            }

            _tasks[id] = existing with { Notified = true };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryAttachLocalShellCommand(string id, LocalShellCommand shellCommand)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalBashTask bashTask)
            {
                return false;
            }

            _tasks[id] = bashTask with
            {
                Status = TaskStatus.Running,
                ShellCommand = shellCommand,
                ExitCode = null,
                EndTime = null
            };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryBackgroundLocalBashTask(string id)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalBashTask bashTask || bashTask.ShellCommand is null)
            {
                return false;
            }

            if (!bashTask.ShellCommand.Background(id))
            {
                return false;
            }

            var updatedTask = bashTask with { IsBackgrounded = true };
            _tasks[id] = updatedTask;
            SyncAppStateLocked();
            StartLocalBashStallWatchdog(id, updatedTask);
            return true;
        }
    }

    public async Task TrackLocalBashCommandAsync(
        string id,
        LocalShellCommand shellCommand,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await shellCommand.Result.WaitAsync(cancellationToken);
            var shouldNotify = false;

            lock (_stateLock)
            {
                if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalBashTask bashTask)
                {
                    return;
                }

                var nextStatus = bashTask.Status == TaskStatus.Killed
                    ? TaskStatus.Killed
                    : result.Interrupted
                        ? TaskStatus.Killed
                        : result.Code == 0
                            ? TaskStatus.Completed
                            : TaskStatus.Failed;

                var updatedTask = bashTask with
                {
                    Status = nextStatus,
                    ShellCommand = null,
                    EndTime = DateTimeOffset.UtcNow,
                    ExitCode = result.Code
                };

                _tasks[id] = updatedTask;
                SyncAppStateLocked();
                shouldNotify = updatedTask.IsBackgrounded && !updatedTask.Notified;
            }

            if (shouldNotify)
            {
                TryEnqueueLocalBashNotification(id);
            }
        }
        finally
        {
            StopLocalBashStallWatchdog(id);
            shellCommand.Cleanup();
        }
    }

    public bool TryEnqueueLocalBashNotification(string id, QueuePriority priority = QueuePriority.Later)
    {
        return TryEnqueueNotification(
            id,
            static task => task is LocalBashTask,
            static task => TaskNotificationFormatter.FormatLocalBash((LocalBashTask)task, task.Status),
            priority);
    }

    public bool TryEnqueueLocalAgentNotification(
        string id,
        string? error = null,
        string? finalMessage = null,
        TaskNotificationUsage? usage = null,
        string? worktreePath = null,
        string? worktreeBranch = null,
        QueuePriority priority = QueuePriority.Later)
    {
        return TryEnqueueNotification(
            id,
            static task => task is LocalAgentTask,
            task => TaskNotificationFormatter.FormatLocalAgent(
                (LocalAgentTask)task,
                task.Status,
                error,
                finalMessage,
                usage,
                worktreePath ?? ((LocalAgentTask)task).WorktreePath,
                worktreeBranch ?? ((LocalAgentTask)task).WorktreeBranch),
            priority);
    }

    public bool TryEnqueueRemoteAgentNotification(string id, QueuePriority priority = QueuePriority.Later)
    {
        return TryEnqueueNotification(
            id,
            static task => task is RemoteAgentTask,
            static task => TaskNotificationFormatter.FormatRemoteAgent((RemoteAgentTask)task, task.Status),
            priority);
    }

    public bool TryUpdateOutputOffset(string id, long outputOffset)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing))
            {
                return false;
            }

            _tasks[id] = existing with { OutputOffset = outputOffset };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryUpdateLocalBashLastReportedTotalLines(string id, int lastReportedTotalLines)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalBashTask bashTask)
            {
                return false;
            }

            _tasks[id] = bashTask with { LastReportedTotalLines = lastReportedTotalLines };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryUpdateLocalAgentProgress(string id, AgentProgress progress)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalAgentTask agentTask)
            {
                return false;
            }

            if (agentTask.Status != TaskStatus.Running)
            {
                return true;
            }

            var existingSummary = agentTask.Progress?.Summary;
            var updatedProgress = string.IsNullOrWhiteSpace(existingSummary)
                ? progress
                : progress with { Summary = existingSummary };

            _tasks[id] = agentTask with { Progress = updatedProgress };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryUpdateLocalAgentSummary(string id, string summary)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalAgentTask agentTask)
            {
                return false;
            }

            if (agentTask.Status != TaskStatus.Running)
            {
                return true;
            }

            var existingProgress = agentTask.Progress;
            var updatedProgress = new AgentProgress(
                existingProgress?.ToolUseCount ?? 0,
                existingProgress?.TokenCount ?? 0,
                existingProgress?.LastActivity,
                existingProgress?.RecentActivities,
                summary);

            _tasks[id] = agentTask with { Progress = updatedProgress };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryUpdateLocalAgentLastReportedCounts(string id, int lastReportedToolCount, int lastReportedTokenCount)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalAgentTask agentTask)
            {
                return false;
            }

            _tasks[id] = agentTask with
            {
                LastReportedToolCount = lastReportedToolCount,
                LastReportedTokenCount = lastReportedTokenCount
            };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryUpdateLocalAgentWorktree(string id, string? worktreePath, string? worktreeBranch)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalAgentTask agentTask)
            {
                return false;
            }

            _tasks[id] = agentTask with
            {
                WorktreePath = worktreePath,
                WorktreeBranch = worktreeBranch
            };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryBackgroundLocalAgentTask(string id)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not LocalAgentTask agentTask || agentTask.IsBackgrounded)
            {
                return false;
            }

            _tasks[id] = agentTask with { IsBackgrounded = true };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryStopTask(
        string id,
        out string? errorCode,
        out string? taskType,
        out string? commandOrDescription)
    {
        lock (_stateLock)
        {
            errorCode = null;
            taskType = null;
            commandOrDescription = null;

            if (!_tasks.TryGetValue(id, out var existing))
            {
                errorCode = "not_found";
                return false;
            }

            if (existing.Status != TaskStatus.Running)
            {
                errorCode = "not_running";
                return false;
            }

            taskType = existing.Type.ToSerializedName();
            switch (existing)
            {
                case LocalBashTask bashTask:
                    commandOrDescription = bashTask.Command;
                    StopLocalBashTaskLocked(id, bashTask);
                    return true;
                case LocalAgentTask agentTask:
                    commandOrDescription = agentTask.Description;
                    StopLocalAgentTaskLocked(id, agentTask);
                    return true;
                default:
                    errorCode = "unsupported_type";
                    return false;
            }
        }
    }

    public void KillShellTasksForAgent(string agentId)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            return;
        }

        lock (_stateLock)
        {
            foreach (var pair in _tasks)
            {
                if (pair.Value is LocalBashTask bashTask &&
                    bashTask.Status == TaskStatus.Running &&
                    string.Equals(bashTask.AgentId, agentId, StringComparison.Ordinal))
                {
                    StopLocalBashTaskLocked(pair.Key, bashTask);
                }
            }
        }
    }

    public bool TryStartLocalBashStallWatchdog(string id)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) ||
                existing is not LocalBashTask bashTask ||
                bashTask.Status != TaskStatus.Running)
            {
                return false;
            }

            StartLocalBashStallWatchdog(id, bashTask);
            return true;
        }
    }

    public int SweepEvictableTerminalTasks(DateTimeOffset? now = null)
    {
        lock (_stateLock)
        {
            var resolvedNow = now ?? DateTimeOffset.UtcNow;
            List<(string TaskId, string OutputFile)> evicted = [];

            foreach (var pair in _tasks)
            {
                if (!CanEvictTerminalTask(pair.Value, resolvedNow))
                {
                    continue;
                }

                evicted.Add((pair.Key, pair.Value.OutputFile));
            }

            if (evicted.Count == 0)
            {
                return 0;
            }

            foreach (var item in evicted)
            {
                _tasks.TryRemove(item.TaskId, out _);
                _taskOutputStore.EvictOutputState(item.OutputFile);
            }

            SyncAppStateLocked();
            return evicted.Count;
        }
    }

    public bool TryRequestInProcessTeammateShutdown(string id)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not InProcessTeammateTask teammateTask)
            {
                return false;
            }

            if (teammateTask.Status != TaskStatus.Running || teammateTask.ShutdownRequested)
            {
                return false;
            }

            _tasks[id] = teammateTask with { ShutdownRequested = true };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryAppendInProcessTeammateMessage(string id, ChatMessage message)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not InProcessTeammateTask teammateTask)
            {
                return false;
            }

            if (teammateTask.Status != TaskStatus.Running)
            {
                return false;
            }

            _tasks[id] = teammateTask with
            {
                Messages = InProcessTeammateTasks.AppendCappedMessage(teammateTask.Messages, message)
            };
            SyncAppStateLocked();
            return true;
        }
    }

    public bool TryInjectUserMessageToInProcessTeammate(string id, string message)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || existing is not InProcessTeammateTask teammateTask)
            {
                return false;
            }

            if (teammateTask.Status.IsTerminal())
            {
                return false;
            }

            _tasks[id] = teammateTask with
            {
                PendingUserMessages = [.. teammateTask.PendingUserMessages ?? [], message],
                Messages = InProcessTeammateTasks.AppendCappedMessage(
                    teammateTask.Messages,
                    ChatMessageFactory.CreateText(MessageRole.User, message))
            };
            SyncAppStateLocked();
            return true;
        }
    }

    public Task AppendOutputAsync(string id, string content, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(id, out var existing) || string.IsNullOrWhiteSpace(existing.OutputFile))
        {
            return Task.CompletedTask;
        }

        return _taskOutputStore.AppendAsync(existing.OutputFile, content, cancellationToken);
    }

    public async Task<string> GetOutputAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(id, out var existing) || string.IsNullOrWhiteSpace(existing.OutputFile))
        {
            return string.Empty;
        }

        return await _taskOutputStore.GetOutputAsync(existing.OutputFile, cancellationToken: cancellationToken);
    }

    public async Task<(string Content, long NewOffset)> GetOutputDeltaAsync(
        string id,
        long fromOffset,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(id, out var existing) || string.IsNullOrWhiteSpace(existing.OutputFile))
        {
            return (string.Empty, fromOffset);
        }

        return await _taskOutputStore.GetOutputDeltaAsync(existing.OutputFile, fromOffset, cancellationToken: cancellationToken);
    }

    public async Task<long> GetOutputSizeAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(id, out var existing) || string.IsNullOrWhiteSpace(existing.OutputFile))
        {
            return 0;
        }

        return await _taskOutputStore.GetOutputSizeAsync(existing.OutputFile, cancellationToken);
    }

    public bool TryEvictOutputState(string id)
    {
        lock (_stateLock)
        {
            if (!_tasks.TryGetValue(id, out var existing) || string.IsNullOrWhiteSpace(existing.OutputFile))
            {
                return false;
            }

            _taskOutputStore.EvictOutputState(existing.OutputFile);
            return true;
        }
    }

    private void StopLocalBashTaskLocked(string id, LocalBashTask bashTask)
    {
        StopLocalBashStallWatchdog(id);

        try
        {
            bashTask.ShellCommand?.Kill();
        }
        catch
        {
        }

        _tasks[id] = bashTask with
        {
            Status = TaskStatus.Killed,
            Notified = true,
            ShellCommand = null,
            EndTime = DateTimeOffset.UtcNow
        };
        SyncAppStateLocked();
    }

    private void StopLocalAgentTaskLocked(string id, LocalAgentTask agentTask)
    {
        try
        {
            agentTask.CancellationSource?.Cancel();
        }
        catch
        {
        }

        var terminalAt = DateTimeOffset.UtcNow;
        _tasks[id] = agentTask with
        {
            Status = TaskStatus.Killed,
            EndTime = terminalAt,
            CancellationSource = null,
            EvictAfter = ResolveLocalAgentEvictAfter(agentTask.Retain, terminalAt)
        };
        SyncAppStateLocked();
    }

    private void StartLocalBashStallWatchdog(string id, LocalBashTask task)
    {
        if (task.Kind == BashTaskKind.Monitor || !task.IsBackgrounded)
        {
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        if (!_stallWatchdogs.TryAdd(id, cancellationSource))
        {
            cancellationSource.Dispose();
            return;
        }

        _ = Task.Run(
            () => RunLocalBashStallWatchdogAsync(
                id,
                task.Description,
                task.OutputFile,
                task.ToolUseId,
                cancellationSource),
            CancellationToken.None);
    }

    private void StopLocalBashStallWatchdog(string id)
    {
        if (_stallWatchdogs.TryRemove(id, out var cancellationSource))
        {
            try
            {
                cancellationSource.Cancel();
            }
            catch
            {
            }
            finally
            {
                cancellationSource.Dispose();
            }
        }
    }

    private async Task RunLocalBashStallWatchdogAsync(
        string taskId,
        string description,
        string outputFile,
        string? toolUseId,
        CancellationTokenSource cancellationSource)
    {
        var lastSize = 0L;
        var lastGrowth = DateTimeOffset.UtcNow;

        try
        {
            while (!cancellationSource.IsCancellationRequested)
            {
                await Task.Delay(_stallCheckInterval, cancellationSource.Token);

                if (!TryGet(taskId, out var currentTask) ||
                    currentTask is not LocalBashTask bashTask ||
                    bashTask.Status != TaskStatus.Running)
                {
                    return;
                }

                var size = await _taskOutputStore.GetOutputSizeAsync(outputFile, cancellationSource.Token);
                if (size > lastSize)
                {
                    lastSize = size;
                    lastGrowth = DateTimeOffset.UtcNow;
                    continue;
                }

                if (DateTimeOffset.UtcNow - lastGrowth < _stallThreshold)
                {
                    continue;
                }

                var tail = await _taskOutputStore.GetOutputAsync(outputFile, _stallTailBytes, cancellationSource.Token);
                if (!LooksLikePrompt(tail))
                {
                    lastGrowth = DateTimeOffset.UtcNow;
                    continue;
                }

                _queuedCommandQueue.EnqueuePendingNotification(
                    new QueuedCommand(
                        FormatStalledLocalBashNotification(taskId, description, outputFile, toolUseId, tail),
                        PromptInputMode.TaskNotification,
                        QueuePriority.Next));
                return;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (_stallWatchdogs.TryGetValue(taskId, out var registeredSource) &&
                ReferenceEquals(registeredSource, cancellationSource))
            {
                _stallWatchdogs.TryRemove(taskId, out _);
            }

            cancellationSource.Dispose();
        }
    }

    private static bool LooksLikePrompt(string tail)
    {
        var lastLine = tail.TrimEnd().Split('\n').LastOrDefault() ?? string.Empty;
        return PromptPatterns.Any(pattern => pattern.IsMatch(lastLine));
    }

    private static string FormatStalledLocalBashNotification(
        string taskId,
        string description,
        string outputFile,
        string? toolUseId,
        string tail)
    {
        var toolUseIdLine = string.IsNullOrWhiteSpace(toolUseId)
            ? string.Empty
            : $"\n<tool-use-id>{toolUseId}</tool-use-id>";
        var summary = $"Background command \"{description}\" appears to be waiting for interactive input";
        return
            $"<task-notification>\n<task-id>{taskId}</task-id>{toolUseIdLine}\n<output-file>{outputFile}</output-file>\n<summary>{EscapeXmlText(summary)}</summary>\n</task-notification>\nLast output:\n{tail.TrimEnd()}\n\nThe command is likely blocked on an interactive prompt. Kill this task and re-run with piped input (for example, `echo y | command`) or a non-interactive flag if one exists.";
    }

    private static string EscapeXmlText(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
    }

    private bool TryEnqueueNotification(
        string id,
        Func<ClawSharpTask, bool> predicate,
        Func<ClawSharpTask, string> formatter,
        QueuePriority priority)
    {
        if (!_tasks.TryGetValue(id, out var existing) || !predicate(existing))
        {
            return false;
        }

        if (!TryMarkNotified(id))
        {
            return false;
        }

        var payload = formatter(existing);
        _queuedCommandQueue.EnqueuePendingNotification(new QueuedCommand(payload, PromptInputMode.TaskNotification, priority));
        _eventSink.Publish(
            new AppEvent(
                AppEventType.NotificationRaised,
                $"Task notification enqueued: {id}",
                DateTimeOffset.UtcNow,
                new Dictionary<string, string>
                {
                    ["taskId"] = id,
                    ["taskType"] = existing.Type.ToSerializedName(),
                    ["priority"] = priority.ToString().ToLowerInvariant()
                }));
        return true;
    }

    public static DateTimeOffset? ResolveLocalAgentEvictAfter(bool retain, DateTimeOffset now)
    {
        return retain ? null : now + PanelGracePeriod;
    }

    private static bool CanEvictTerminalTask(ClawSharpTask task, DateTimeOffset now)
    {
        if (!task.Status.IsTerminal() || !task.Notified)
        {
            return false;
        }

        if (task is LocalAgentTask agentTask && (agentTask.EvictAfter ?? DateTimeOffset.MaxValue) > now)
        {
            return false;
        }

        return true;
    }

    private void SyncAppStateLocked()
    {
        if (_appStateStore is null)
        {
            return;
        }

        var tasks = new Dictionary<string, ClawSharpTask>(_tasks, StringComparer.Ordinal);
        _appStateStore.SetState(state => ClawSharpAppStateMutations.WithTasks(state, tasks));
    }
}
