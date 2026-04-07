using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using TaskStatus = ClawSharp.Tasks.TaskStatus;

namespace ClawSharp.Infrastructure;

public sealed class LocalAgentExecutionService : IAgentExecutionService
{
    private static readonly IReadOnlySet<string> BaseChildToolNames = new HashSet<string>(
        ["Read", "Edit", "Write", "Glob", "Grep", "Bash", "PowerShell"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlySet<string> AlwaysBlockedChildToolNames = new HashSet<string>(
        ["Agent", "SendMessage", "TaskOutput"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IEventSink _eventSink;
    private readonly ITranscriptStore _transcriptStore;
    private readonly AgentPersistenceService _agentPersistenceService;
    private readonly IQueuedCommandQueue _queuedCommandQueue;
    private readonly IQueryModelCallExecutor _modelCallExecutor;
    private readonly HookRegistry _hookRegistry;
    private readonly HookExecutor _hookExecutor;
    private readonly Func<ToolExecutionContext, ToolRegistry, QueryEngine> _queryEngineFactory;

    public LocalAgentExecutionService(
        IEventSink eventSink,
        ITranscriptStore transcriptStore,
        AgentPersistenceService agentPersistenceService,
        IQueuedCommandQueue queuedCommandQueue,
        IQueryModelCallExecutor modelCallExecutor,
        HookRegistry? hookRegistry = null,
        HookExecutor? hookExecutor = null,
        Func<ToolExecutionContext, ToolRegistry, QueryEngine>? queryEngineFactory = null)
    {
        _eventSink = eventSink;
        _transcriptStore = transcriptStore;
        _agentPersistenceService = agentPersistenceService;
        _queuedCommandQueue = queuedCommandQueue;
        _modelCallExecutor = modelCallExecutor;
        _hookRegistry = hookRegistry ?? new HookRegistry();
        _hookExecutor = hookExecutor ?? new HookExecutor();
        _queryEngineFactory = queryEngineFactory ?? CreateQueryEngine;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolExecutionContext context,
        AgentDefinition selectedAgent,
        AgentExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.Cwd))
        {
            return new ToolExecutionResult(
                false,
                "Agent 'cwd' overrides are not implemented in ClawSharp yet. The TypeScript runtime changes both filesystem roots and shell working directory for the child agent; ClawSharp needs an approved C# equivalent before enabling that behavior.");
        }

        if (string.Equals(request.Isolation, "worktree", StringComparison.Ordinal))
        {
            return new ToolExecutionResult(
                false,
                "Agent isolation 'worktree' is not implemented in ClawSharp yet. The TypeScript runtime creates and manages a real git worktree for the child agent; ClawSharp is intentionally blocked at that decision point until the C# equivalent is approved.");
        }

        var description = request.Description!.Trim();
        var prompt = request.Prompt!.Trim();
        var isForkPath = string.Equals(
            selectedAgent.AgentType,
            ForkSubagentFoundation.ForkSubagentType,
            StringComparison.Ordinal);
        if (isForkPath && (context.CurrentSystemPrompt is null || context.CurrentSystemPrompt.Count == 0))
        {
            return new ToolExecutionResult(
                false,
                "Forked-agent execution requires the parent rendered system prompt, but it was not available in this runtime path.");
        }

        var runInBackground = request.RunInBackground == true ||
                              selectedAgent.Background == true ||
                              ForkSubagentFoundation.IsForkSubagentEnabled();
        var resolvedModel = ResolveAgentModel(context, selectedAgent, request);
        var permissionContext = ResolvePermissionContext(context.ToolPermissionContext, selectedAgent, runInBackground);
        var childTools = CreateChildToolRegistry(
            context,
            selectedAgent,
            permissionContext,
            resolvedModel,
            runInBackground,
            isForkPath);
        var queryEngine = _queryEngineFactory(context, childTools);
        var backgroundCancellationSource = runInBackground ? new CancellationTokenSource() : null;

        var task = runInBackground
            ? await context.Tasks.CreateLocalAgentForSessionAsync(
                context.Session.Id,
                description,
                prompt,
                selectedAgent.AgentType,
                TaskStatus.Running,
                resolvedModel,
                isBackgrounded: true,
                cancellationSource: backgroundCancellationSource,
                cancellationToken: cancellationToken)
            : await context.Tasks.CreateForegroundLocalAgentForSessionAsync(
                context.Session.Id,
                description,
                prompt,
                selectedAgent.AgentType,
                TaskStatus.Running,
                resolvedModel,
                cancellationSource: null,
                cancellationToken: cancellationToken);

        var agentSession = _agentPersistenceService.CreateAgentSession(context.Session, task.Id);
        await _agentPersistenceService.WriteAgentMetadataAsync(
            context.Session,
            task.Id,
            new AgentMetadata(selectedAgent.AgentType, Description: description),
            cancellationToken);

        if (runInBackground)
        {
            _ = Task.Run(
                () => ExecuteBackgroundAsync(
                    context,
                    selectedAgent,
                    task,
                    agentSession,
                    prompt,
                    resolvedModel,
                    permissionContext,
                    queryEngine,
                    backgroundCancellationSource!.Token),
                CancellationToken.None);

            return new ToolExecutionResult(
                true,
                $"Launched agent {task.Id} in the background.",
                new JsonObject
                {
                    ["status"] = "async_launched",
                    ["agentId"] = task.Id,
                    ["description"] = description,
                    ["prompt"] = prompt,
                    ["outputFile"] = task.OutputFile,
                    ["canReadOutputFile"] = null
                });
        }

        return await ExecuteForegroundAsync(
            context.Tasks,
            context,
            selectedAgent,
            task,
            agentSession,
            prompt,
            resolvedModel,
            permissionContext,
            queryEngine,
            cancellationToken);
    }

    private QueryEngine CreateQueryEngine(ToolExecutionContext context, ToolRegistry childTools)
    {
        var toolOrchestrator = new ToolOrchestrator(childTools, _eventSink);
        var postSamplingHookRegistry = new PostSamplingHookRegistry();
        var reactiveCompactHookRunner = new QueryReactiveCompactHookRunner(
            childTools,
            _hookRegistry,
            _hookExecutor);
        var reactiveCompactModelCallRunner = new QueryReactiveCompactModelCallRunner(_modelCallExecutor);
        var reactiveCompactExecutor = new QueryReactiveCompactExecutor(
            toolCatalog: new ToolRegistryReactiveCompactToolCatalog(childTools),
            hookRunner: reactiveCompactHookRunner,
            modelCallRunner: reactiveCompactModelCallRunner);
        var promptOverflowRecoveryRunner = new CompositeQueryPromptOverflowRecoveryRunner(
            new CompactBoundaryPromptOverflowRecoveryRunner(),
            new ReactiveCompactPromptOverflowRecoveryRunner(reactiveCompactExecutor));
        var stopHookRunner = new QueryStopHookRunner(
            childTools,
            _hookRegistry,
            _hookExecutor);
        var iterationRequestBuilder = new QueryModelIterationRequestBuilder(
            availableTools: childTools.All);
        var modelBackedIterationRunner = new ModelBackedIterationRunner(
            postSamplingHookRegistry,
            iterationRequestBuilder: iterationRequestBuilder,
            modelCallExecutor: _modelCallExecutor,
            promptOverflowRecoveryRunner: promptOverflowRecoveryRunner,
            toolOrchestrator: toolOrchestrator,
            stopHookRunner: stopHookRunner);
        var queryTurnRunner = new ExplicitToolTurnRunner(
            toolOrchestrator,
            stopHookRunner,
            modelBackedIterationRunner,
            postSamplingHookRegistry,
            promptOverflowRecoveryRunner);

        return new QueryEngine(
            context.Settings,
            _eventSink,
            _transcriptStore,
            queryTurnRunner,
            new QueuedTaskNotificationDrainer(_queuedCommandQueue, _transcriptStore, context.Tasks),
            toolRegistry: childTools,
            appStateStore: childTools.AppStateStore);
    }

    private async Task<ToolExecutionResult> ExecuteForegroundAsync(
        TaskRegistry tasks,
        ToolExecutionContext context,
        AgentDefinition selectedAgent,
        LocalAgentTask task,
        ConversationSession agentSession,
        string prompt,
        string resolvedModel,
        ToolPermissionContext permissionContext,
        QueryEngine queryEngine,
        CancellationToken cancellationToken)
    {
        var progress = new AgentExecutionProgressTracker();

        try
        {
            var result = await RunAgentQueryAsync(
                tasks,
                context,
                selectedAgent,
                task,
                agentSession,
                prompt,
                resolvedModel,
                permissionContext,
                queryEngine,
                progress,
                cancellationToken);

            var finalMessage = GetFinalResultText(result);
            MarkAgentCompleted(tasks, task.Id, progress, finalMessage);

            return new ToolExecutionResult(
                true,
                finalMessage,
                new JsonObject
                {
                    ["status"] = "completed",
                    ["prompt"] = prompt,
                    ["result"] = finalMessage,
                    ["usage"] = null
                });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MarkAgentKilled(tasks, task.Id, progress, "Agent execution was interrupted.");
            throw;
        }
        catch (Exception exception)
        {
            MarkAgentFailed(tasks, task.Id, progress, exception.Message);
            return new ToolExecutionResult(
                false,
                $"Agent \"{task.Description}\" failed: {exception.Message}");
        }
    }

    private async Task ExecuteBackgroundAsync(
        ToolExecutionContext context,
        AgentDefinition selectedAgent,
        LocalAgentTask task,
        ConversationSession agentSession,
        string prompt,
        string resolvedModel,
        ToolPermissionContext permissionContext,
        QueryEngine queryEngine,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var progress = new AgentExecutionProgressTracker();

        try
        {
            var result = await RunAgentQueryAsync(
                context.Tasks,
                context,
                selectedAgent,
                task,
                agentSession,
                prompt,
                resolvedModel,
                permissionContext,
                queryEngine,
                progress,
                cancellationToken);

            stopwatch.Stop();
            var finalMessage = GetFinalResultText(result);
            MarkAgentCompleted(context.Tasks, task.Id, progress, finalMessage);
            context.Tasks.TryEnqueueLocalAgentNotification(
                task.Id,
                finalMessage: finalMessage,
                usage: BuildNotificationUsage(progress, stopwatch.Elapsed));
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            MarkAgentKilled(context.Tasks, task.Id, progress, "Agent execution was interrupted.");
            context.Tasks.TryEnqueueLocalAgentNotification(
                task.Id,
                error: "Agent execution was interrupted.",
                usage: BuildNotificationUsage(progress, stopwatch.Elapsed));
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            MarkAgentFailed(context.Tasks, task.Id, progress, exception.Message);
            context.Tasks.TryEnqueueLocalAgentNotification(
                task.Id,
                error: exception.Message,
                usage: BuildNotificationUsage(progress, stopwatch.Elapsed));
        }
        finally
        {
            context.Tasks.KillShellTasksForAgent(task.Id);
        }
    }

    private async Task<QueryResult> RunAgentQueryAsync(
        TaskRegistry tasks,
        ToolExecutionContext context,
        AgentDefinition selectedAgent,
        LocalAgentTask task,
        ConversationSession agentSession,
        string prompt,
        string resolvedModel,
        ToolPermissionContext permissionContext,
        QueryEngine queryEngine,
        AgentExecutionProgressTracker progress,
        CancellationToken cancellationToken)
    {
        var isForkPath = string.Equals(
            selectedAgent.AgentType,
            ForkSubagentFoundation.ForkSubagentType,
            StringComparison.Ordinal);
        var preparedMessages = isForkPath
            ? BuildForkInitialMessages(context.Session.Messages, prompt)
            : null;
        if (preparedMessages is not null)
        {
            foreach (var message in preparedMessages)
            {
                agentSession.Add(message);
            }
        }

        var request = QueryTurnRequest.Create(agentSession, prompt) with
        {
            ModelTurnContext = await BuildAgentTurnContextAsync(
                selectedAgent,
                agentSession.ProjectDirectory,
                context.CurrentSystemPrompt,
                cancellationToken),
            InitialToolUseContext = new QueryToolUseContextState(
                isForkPath
                    ? FileStateCache.Clone(context.ReadFileState)
                    : FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries),
                permissionContext,
                resolvedModel),
            MaxTurns = selectedAgent.MaxTurns
        };

        if (preparedMessages is not null)
        {
            return await queryEngine.RunPreparedTurnAsync(
                agentSession,
                request,
                onTextDelta: null,
                onMessage: (message, innerCancellationToken) =>
                {
                    progress.Observe(message);
                    ApplyProgressSnapshot(tasks, task.Id, progress);
                    return Task.CompletedTask;
                },
                cancellationToken: cancellationToken);
        }

        return await queryEngine.RunTurnAsync(
            agentSession,
            request,
            onTextDelta: null,
            onMessage: (message, innerCancellationToken) =>
            {
                progress.Observe(message);
                ApplyProgressSnapshot(tasks, task.Id, progress);
                return Task.CompletedTask;
            },
            cancellationToken);
    }

    private ToolRegistry CreateChildToolRegistry(
        ToolExecutionContext context,
        AgentDefinition selectedAgent,
        ToolPermissionContext permissionContext,
        string resolvedModel,
        bool runInBackground,
        bool isForkPath)
    {
        var childAppStateStore = new ClawSharpAppStateStore(
            context.AppState with
            {
                ToolPermissionContext = permissionContext,
                MainLoopModel = resolvedModel
            });
        var childReadFileState = isForkPath
            ? FileStateCache.Clone(context.ReadFileState)
            : FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries);

        return new ToolRegistry(
            context.WorkspaceRoot,
            context.Tasks,
            childReadFileState,
            permissionContext,
            context.FileUpdateNotifier,
            context.AgentDefinitions,
            childAppStateStore,
            runInBackground ? new NullPermissionPrompter() : context.PermissionPrompter,
            agentExecutionService: isForkPath ? this : new NullAgentExecutionService(),
            allowedToolNames: ResolveAllowedToolNames(selectedAgent, isForkPath, context.AvailableTools));
    }

    private static IReadOnlySet<string> ResolveAllowedToolNames(
        AgentDefinition selectedAgent,
        bool isForkPath,
        IReadOnlyList<ToolDescriptor>? parentAvailableTools)
    {
        if (isForkPath && parentAvailableTools is { Count: > 0 })
        {
            return new HashSet<string>(
                parentAvailableTools.Select(static tool => tool.Name),
                StringComparer.OrdinalIgnoreCase);
        }

        var allowed = new HashSet<string>(BaseChildToolNames, StringComparer.OrdinalIgnoreCase);

        if (selectedAgent.Tools is { Count: > 0 } toolSelectors &&
            !toolSelectors.Any(static selector => string.Equals(selector, "*", StringComparison.Ordinal)))
        {
            allowed.IntersectWith(
                toolSelectors
                    .Select(NormalizeToolSelector)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>());
        }

        if (selectedAgent.DisallowedTools is { Count: > 0 })
        {
            foreach (var toolName in selectedAgent.DisallowedTools
                         .Select(NormalizeToolSelector)
                         .OfType<string>())
            {
                allowed.Remove(toolName);
            }
        }

        allowed.ExceptWith(AlwaysBlockedChildToolNames);
        return allowed;
    }

    private static string? NormalizeToolSelector(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var trimmed = selector.Trim();
        var parenIndex = trimmed.IndexOf('(');
        if (parenIndex >= 0)
        {
            trimmed = trimmed[..parenIndex];
        }

        return trimmed.Trim();
    }

    private static ToolPermissionContext ResolvePermissionContext(
        ToolPermissionContext parentPermissionContext,
        AgentDefinition selectedAgent,
        bool runInBackground)
    {
        var mode = parentPermissionContext.Mode;
        if (selectedAgent.PermissionMode is not null &&
            parentPermissionContext.Mode is not PermissionMode.BypassPermissions and
            not PermissionMode.AcceptEdits and
            not PermissionMode.Auto)
        {
            mode = selectedAgent.PermissionMode.Value;
        }

        return parentPermissionContext with
        {
            Mode = mode,
            ShouldAvoidPermissionPrompts = runInBackground || parentPermissionContext.ShouldAvoidPermissionPrompts == true
        };
    }

    private static string ResolveAgentModel(
        ToolExecutionContext context,
        AgentDefinition selectedAgent,
        AgentExecutionRequest request)
    {
        var parentModel = context.AppState.MainLoopModel ?? context.Settings.Runtime.Model;
        var routedModel = AgentRoutingResolver.ResolveRoutedModel(
            context.Settings,
            request.Name,
            request.SubagentType ?? selectedAgent.AgentType);
        var preferredModel = NormalizeInheritedModel(request.Model)
            ?? NormalizeInheritedModel(routedModel)
            ?? NormalizeInheritedModel(selectedAgent.Model)
            ?? parentModel;

        return MainLoopModelResolver.Resolve(preferredModel, parentModel);
    }

    private async Task<QueryModelTurnContext> BuildAgentTurnContextAsync(
        AgentDefinition selectedAgent,
        string workspaceRoot,
        IReadOnlyList<string>? inheritedSystemPrompt,
        CancellationToken cancellationToken)
    {
        var systemContext = await BuildGitStatusContextAsync(workspaceRoot, cancellationToken);
        var systemPrompt = string.Equals(selectedAgent.AgentType, ForkSubagentFoundation.ForkSubagentType, StringComparison.Ordinal) &&
                           inheritedSystemPrompt is { Count: > 0 }
            ? inheritedSystemPrompt
            : [selectedAgent.SystemPrompt];
        return new QueryModelTurnContext(
            systemPrompt,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["currentDate"] = $"Today's date is {DateTimeOffset.Now:yyyy-MM-dd}.",
                ["platform"] = GetPlatformLabel()
            },
            systemContext,
            GetQuerySource(selectedAgent));
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildGitStatusContextAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var gitStatus = await TryBuildGitStatusSnapshotAsync(workspaceRoot, cancellationToken);
        if (string.IsNullOrWhiteSpace(gitStatus))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["gitStatus"] = gitStatus
        };
    }

    private async Task<string?> TryBuildGitStatusSnapshotAsync(
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        if (!await IsGitRepositoryAsync(workspaceRoot, cancellationToken))
        {
            return null;
        }

        var branchTask = RunProcessAsync(workspaceRoot, "git", "branch --show-current", cancellationToken);
        var statusTask = RunProcessAsync(workspaceRoot, "git", "--no-optional-locks status --short", cancellationToken);
        var logTask = RunProcessAsync(workspaceRoot, "git", "--no-optional-locks log --oneline -n 5", cancellationToken);
        await Task.WhenAll(branchTask, statusTask, logTask);

        var parts = new List<string>
        {
            "This is the git status snapshot for the delegated agent."
        };

        var branch = branchTask.Result.StandardOutput.Trim();
        if (!string.IsNullOrWhiteSpace(branch))
        {
            parts.Add($"Current branch: {branch}");
        }

        var status = statusTask.Result.StandardOutput.Trim();
        parts.Add($"Status:\n{(string.IsNullOrWhiteSpace(status) ? "(clean)" : status)}");

        var log = logTask.Result.StandardOutput.Trim();
        if (!string.IsNullOrWhiteSpace(log))
        {
            parts.Add($"Recent commits:\n{log}");
        }

        return string.Join("\n\n", parts);
    }

    private async Task<bool> IsGitRepositoryAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(
            workspaceRoot,
            "git",
            "rev-parse --is-inside-work-tree",
            cancellationToken);
        return result.ExitCode == 0 &&
               string.Equals(result.StandardOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetQuerySource(AgentDefinition selectedAgent)
    {
        return string.Equals(selectedAgent.Source, "built-in", StringComparison.Ordinal)
            ? $"agent:builtin:{selectedAgent.AgentType}"
            : "agent:custom";
    }

    private static IReadOnlyList<ChatMessage>? BuildForkInitialMessages(
        IReadOnlyList<ChatMessage> parentMessages,
        string prompt)
    {
        if (parentMessages.Count == 0)
        {
            return [ChatMessageFactory.CreateText(MessageRole.User, ForkSubagentFoundation.BuildChildMessage(prompt))];
        }

        var lastMessage = parentMessages[^1];
        if (lastMessage.Role == MessageRole.Assistant)
        {
            return parentMessages
                .Take(parentMessages.Count - 1)
                .Concat(ForkSubagentFoundation.BuildForkedMessages(prompt, lastMessage))
                .ToArray();
        }

        var lastAssistantMessage = parentMessages.LastOrDefault(static message => message.Role == MessageRole.Assistant);
        if (lastAssistantMessage is not null)
        {
            return parentMessages
                .Concat(ForkSubagentFoundation.BuildForkedMessages(prompt, lastAssistantMessage))
                .ToArray();
        }

        return parentMessages
            .Concat([ChatMessageFactory.CreateText(MessageRole.User, ForkSubagentFoundation.BuildChildMessage(prompt))])
            .ToArray();
    }

    private static string? NormalizeInheritedModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model) ||
            string.Equals(model.Trim(), "inherit", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return model.Trim();
    }

    private static string GetPlatformLabel()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "win32";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "darwin";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux";
        }

        return Environment.OSVersion.Platform.ToString().ToLowerInvariant();
    }

    private static void ApplyProgressSnapshot(
        TaskRegistry tasks,
        string taskId,
        AgentExecutionProgressTracker progress)
    {
        tasks.TryUpdateLocalAgentProgress(
            taskId,
            new AgentProgress(
                progress.ToolUseCount,
                progress.TokenCount,
                progress.LastActivity,
                progress.RecentActivities.ToArray(),
                progress.Summary));
        tasks.TryUpdateLocalAgentLastReportedCounts(taskId, progress.ToolUseCount, progress.TokenCount);
        if (!string.IsNullOrWhiteSpace(progress.Summary))
        {
            tasks.TryUpdateLocalAgentSummary(taskId, progress.Summary);
        }
    }

    private static void MarkAgentCompleted(
        TaskRegistry tasks,
        string taskId,
        AgentExecutionProgressTracker progress,
        string finalMessage)
    {
        var completedAt = DateTimeOffset.UtcNow;
        tasks.TryUpdate(
            taskId,
            task =>
            {
                if (task is not LocalAgentTask agentTask)
                {
                    return task;
                }

                return agentTask with
                {
                    Status = TaskStatus.Completed,
                    EndTime = completedAt,
                    Result = finalMessage,
                    Error = null,
                    CancellationSource = null,
                    EvictAfter = TaskRegistry.ResolveLocalAgentEvictAfter(agentTask.Retain, completedAt),
                    Progress = new AgentProgress(
                        progress.ToolUseCount,
                        progress.TokenCount,
                        progress.LastActivity,
                        progress.RecentActivities.ToArray(),
                        progress.Summary ?? finalMessage)
                };
            });
    }

    private static void MarkAgentFailed(
        TaskRegistry tasks,
        string taskId,
        AgentExecutionProgressTracker progress,
        string error)
    {
        var completedAt = DateTimeOffset.UtcNow;
        tasks.TryUpdate(
            taskId,
            task =>
            {
                if (task is not LocalAgentTask agentTask)
                {
                    return task;
                }

                return agentTask with
                {
                    Status = TaskStatus.Failed,
                    EndTime = completedAt,
                    Error = error,
                    CancellationSource = null,
                    EvictAfter = TaskRegistry.ResolveLocalAgentEvictAfter(agentTask.Retain, completedAt),
                    Progress = new AgentProgress(
                        progress.ToolUseCount,
                        progress.TokenCount,
                        progress.LastActivity,
                        progress.RecentActivities.ToArray(),
                        progress.Summary)
                };
            });
    }

    private static void MarkAgentKilled(
        TaskRegistry tasks,
        string taskId,
        AgentExecutionProgressTracker progress,
        string error)
    {
        var completedAt = DateTimeOffset.UtcNow;
        tasks.TryUpdate(
            taskId,
            task =>
            {
                if (task is not LocalAgentTask agentTask)
                {
                    return task;
                }

                return agentTask with
                {
                    Status = TaskStatus.Killed,
                    EndTime = completedAt,
                    Error = error,
                    CancellationSource = null,
                    EvictAfter = TaskRegistry.ResolveLocalAgentEvictAfter(agentTask.Retain, completedAt),
                    Progress = new AgentProgress(
                        progress.ToolUseCount,
                        progress.TokenCount,
                        progress.LastActivity,
                        progress.RecentActivities.ToArray(),
                        progress.Summary)
                };
            });
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

    private static TaskNotificationUsage BuildNotificationUsage(
        AgentExecutionProgressTracker progress,
        TimeSpan elapsed)
    {
        return new TaskNotificationUsage(
            progress.TokenCount,
            progress.ToolUseCount,
            (int)Math.Max(0, elapsed.TotalMilliseconds));
    }

    private async Task<ProcessResult> RunProcessAsync(
        string workspaceRoot,
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private sealed class AgentExecutionProgressTracker
    {
        private const int MaxRecentActivities = 5;

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

        private static ToolActivity CreateActivity(MessageContentBlock block)
        {
            var toolName = block.Name ?? string.Empty;
            var input = TryParseInput(block.Value);
            return new ToolActivity(
                toolName,
                input,
                ActivityDescription: null,
                IsSearch: string.Equals(toolName, "Glob", StringComparison.Ordinal) ||
                          string.Equals(toolName, "Grep", StringComparison.Ordinal),
                IsRead: string.Equals(toolName, "Read", StringComparison.Ordinal));
        }

        private static IReadOnlyDictionary<string, object?> TryParseInput(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal);
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(json) ??
                       new Dictionary<string, object?>(StringComparer.Ordinal);
            }
            catch (JsonException)
            {
                return new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["raw"] = json
                };
            }
        }
    }
}
