// TS parity status: queued task-notification drain is ported for the current REPL loop; full 1:1 parity still depends on the real REPL state model and queue processor.
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Runtime;
using ClawSharp.Tools;
using System.Text;
using System.Threading;

namespace ClawSharp.Ui.Terminal;

public sealed class TerminalShell
{
    private readonly QueryEngine _queryEngine;
    private readonly QueuedTaskNotificationDrainer _queuedTaskNotificationDrainer;
    private readonly ToolUseMessageRenderer _toolUseMessageRenderer;
    private readonly ToolProgressMessageRenderer _toolProgressMessageRenderer;
    private readonly ToolResultMessageRenderer _toolResultMessageRenderer;
    private readonly CommandRegistry _commands;
    private readonly ClawSharpSettings _settings;
    private readonly IEventSink _eventSink;
    private readonly ISessionFactory _sessionFactory;
    private readonly ITranscriptStore _transcriptStore;
    private readonly TranscriptMessageRenderer _transcriptMessageRenderer;
    private readonly TerminalFooterRenderer _terminalFooterRenderer;
    private readonly ConversationTurnExecutor _turnExecutor;
    private readonly IClawSharpAppStateStore _appStateStore;
    private readonly PromptInputReader _promptInputReader;
    private readonly IInteractionService _interactionService;
    private readonly FileStateCache _readFileState;
    private readonly ToolRegistry? _toolRegistry;
    private readonly IQueryModelTurnContextProvider? _modelTurnContextProvider;
    private readonly LocalMainSessionTaskService? _localMainSessionTaskService;
    private readonly ISessionBackgroundKeyMonitor _sessionBackgroundKeyMonitor;
    private readonly IPreventSleepService _preventSleepService;
    private readonly CronSchedulerService? _cronSchedulerService;
    private readonly SemaphoreSlim _sessionTurnGate = new(1, 1);
    private int _mainTurnInFlight;

    public TerminalShell(
        QueryEngine queryEngine,
        QueuedTaskNotificationDrainer queuedTaskNotificationDrainer,
        ToolUseMessageRenderer toolUseMessageRenderer,
        ToolProgressMessageRenderer toolProgressMessageRenderer,
        ToolResultMessageRenderer toolResultMessageRenderer,
        CommandRegistry commands,
        ClawSharpSettings settings,
        IEventSink eventSink,
        ISessionFactory sessionFactory,
        ITranscriptStore transcriptStore,
        TranscriptMessageRenderer? transcriptMessageRenderer = null,
        TerminalFooterRenderer? terminalFooterRenderer = null,
        IClawSharpAppStateStore? appStateStore = null,
        PromptInputReader? promptInputReader = null,
        FileStateCache? readFileState = null,
        ToolRegistry? toolRegistry = null,
        IQueryModelTurnContextProvider? modelTurnContextProvider = null,
        LocalMainSessionTaskService? localMainSessionTaskService = null,
        ISessionBackgroundKeyMonitor? sessionBackgroundKeyMonitor = null,
        IPreventSleepService? preventSleepService = null,
        CronSchedulerService? cronSchedulerService = null)
    {
        _queryEngine = queryEngine;
        _queuedTaskNotificationDrainer = queuedTaskNotificationDrainer;
        _toolUseMessageRenderer = toolUseMessageRenderer;
        _toolProgressMessageRenderer = toolProgressMessageRenderer;
        _toolResultMessageRenderer = toolResultMessageRenderer;
        _commands = commands;
        _settings = settings;
        _eventSink = eventSink;
        _sessionFactory = sessionFactory;
        _transcriptStore = transcriptStore;
        _transcriptMessageRenderer = transcriptMessageRenderer ?? new TranscriptMessageRenderer();
        _terminalFooterRenderer = terminalFooterRenderer ?? new TerminalFooterRenderer();
        _turnExecutor = new ConversationTurnExecutor(
            queryEngine,
            transcriptStore,
            toolRegistry,
            modelTurnContextProvider);
        _appStateStore = appStateStore ?? new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                Directory.GetCurrentDirectory(),
                StartupEnvironment.Capture(),
                settings,
                [],
                [],
                [],
                [],
                [],
                []));
        _promptInputReader = promptInputReader ?? new PromptInputReader();
        _readFileState = readFileState ?? FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries);
        _interactionService = new SpectreInteractionService();
        _toolRegistry = toolRegistry;
        _modelTurnContextProvider = modelTurnContextProvider;
        _localMainSessionTaskService = localMainSessionTaskService;
        _sessionBackgroundKeyMonitor = sessionBackgroundKeyMonitor ?? new ConsoleSessionBackgroundKeyMonitor();
        _preventSleepService = preventSleepService ?? new MacOsPreventSleepService();
        _cronSchedulerService = cronSchedulerService;
    }

    public async Task<int> RunReplAsync(
        TextReader input,
        TextWriter output,
        ConversationSession? initialSession = null,
        DeepLinkRuntimeContext? deepLinkRuntimeContext = null,
        CancellationToken cancellationToken = default)
    {
        var session = initialSession ?? _sessionFactory.Create();
        _appStateStore.SetState(state => ClawSharpAppStateMutations.WithActiveSession(state, session));
        _eventSink.Publish(new AppEvent(AppEventType.SessionStarted, "Session started", DateTimeOffset.UtcNow));

        try
        {
            _cronSchedulerService?.Start(
                isIdle: () => Volatile.Read(ref _mainTurnInFlight) == 0,
                onFireAsync: (job, innerCancellationToken) => ExecuteScheduledCronAsync(session, job, innerCancellationToken));

            await output.WriteLineAsync($"{AppMetadata.DisplayVersion}");
            await output.WriteLineAsync("Interactive foundation ready. Type /help for commands.");
            if (deepLinkRuntimeContext?.IsDeepLinkOrigin == true)
            {
                foreach (var line in BuildDeepLinkNoticeLines(session, deepLinkRuntimeContext))
                {
                    await output.WriteLineAsync(line);
                }
            }

            if (initialSession is not null)
            {
                await ReplaySessionAsync(initialSession, output, cancellationToken);
            }

            var lastFooterLines = Array.Empty<string>();
            while (!cancellationToken.IsCancellationRequested)
            {
                var drainedNotifications = await _queuedTaskNotificationDrainer.DrainAsync(session, cancellationToken);
                foreach (var notificationMessage in drainedNotifications)
                {
                    await WriteTranscriptMessageAsync(notificationMessage, output, previousRole: null);
                }

                lastFooterLines = await WriteFooterIfChangedAsync(output, lastFooterLines);
                var submission = await _promptInputReader.ReadSubmissionAsync(input, output, cancellationToken);
                if (submission is null)
                {
                    break;
                }

                var trimmed = submission.Value.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (submission.Mode == PromptInputMode.Bash)
                {
                    await output.WriteLineAsync("Bash input mode is not implemented yet.");
                    continue;
                }

                if (trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                {
                    await output.WriteLineAsync("Exiting ClawSharp.");
                    return 0;
                }

                if (trimmed.StartsWith("/", StringComparison.Ordinal))
                {
                    var commandName = trimmed[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
                    if (_commands.TryResolve(commandName, out var handler) && handler is not null)
                    {
                        var commandResult = await handler.ExecuteAsync(
                            trimmed,
                            new CommandExecutionContext
                            {
                                AppStateStore = _appStateStore,
                                Session = session,
                                SessionFactory = _sessionFactory,
                                TranscriptStore = _transcriptStore,
                                Settings = _settings,
                                ReadFileState = _readFileState,
                                InteractionService = _interactionService
                            },
                            cancellationToken);

                        _eventSink.Publish(
                            new AppEvent(
                                AppEventType.CommandExecuted,
                                $"Command executed: {commandName}",
                                DateTimeOffset.UtcNow));

                        ClawSharpTelemetry.LogEvent("tengu_input_command", new Dictionary<string, object?>
                        {
                            ["input"] = commandName,
                            ["invocation_trigger"] = "user-slash"
                        });

                        if (!string.IsNullOrWhiteSpace(commandResult.Output))
                        {
                            await output.WriteLineAsync(commandResult.Output);
                        }

                        if (commandResult.SessionOverride is not null)
                        {
                            session = commandResult.SessionOverride;
                            _appStateStore.SetState(state => ClawSharpAppStateMutations.WithActiveSession(state, session));
                            await ReplaySessionAsync(session, output, cancellationToken);
                        }

                        continue;
                    }

                    await output.WriteLineAsync($"Unknown command: {commandName}");
                    continue;
                }

                try
                {
                    var turnResult = await RunPromptTurnAsync(session, submission, input, output, cancellationToken);
                    if (turnResult == PromptTurnResult.Backgrounded)
                    {
                        continue;
                    }
                }
                catch (QueryExecutionNotImplementedException exception)
                {
                    await output.WriteLineAsync(exception.Message);
                }
            }

            return 0;
        }
        finally
        {
            if (_cronSchedulerService is not null)
            {
                await _cronSchedulerService.StopAsync();
            }
        }
    }

    private async Task ReplaySessionAsync(
        ConversationSession session,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var toolNamesByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        var progressMessagesByParentToolUseId = new Dictionary<string, List<ToolProgressUpdate>>(StringComparer.Ordinal);
        var renderedProgressByParentToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        MessageRole? previousTranscriptRole = null;

        foreach (var message in session.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _toolProgressMessageRenderer.TrackToolUses(message, toolNamesByToolUseId);
            var renderedToolUses = _toolUseMessageRenderer.TryRender(message);
            if (renderedToolUses.Count > 0)
            {
                foreach (var renderedToolUse in renderedToolUses)
                {
                    await output.WriteLineAsync(renderedToolUse.Content);
                }

                continue;
            }

            var renderedProgress = _toolProgressMessageRenderer.TryRender(
                message,
                toolNamesByToolUseId,
                progressMessagesByParentToolUseId);
            if (renderedProgress is not null)
            {
                if (renderedProgressByParentToolUseId.TryGetValue(renderedProgress.ParentToolUseId, out var existing) &&
                    string.Equals(existing, renderedProgress.Content, StringComparison.Ordinal))
                {
                    continue;
                }

                renderedProgressByParentToolUseId[renderedProgress.ParentToolUseId] = renderedProgress.Content;
                await output.WriteLineAsync(renderedProgress.Content);
                continue;
            }

            var renderedResult = _toolResultMessageRenderer.TryRender(
                message,
                toolNamesByToolUseId,
                progressMessagesByParentToolUseId);
            if (renderedResult is not null)
            {
                await output.WriteLineAsync(renderedResult.Content);
                continue;
            }

            previousTranscriptRole = await WriteTranscriptMessageAsync(message, output, previousTranscriptRole);
        }
    }

    private async Task<PromptTurnResult> RunPromptTurnAsync(
        ConversationSession session,
        PromptSubmission submission,
        TextReader input,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        await _sessionTurnGate.WaitAsync(cancellationToken);
        Interlocked.Exchange(ref _mainTurnInFlight, 1);
        ClawSharpTelemetry.LogEvent("tengu_input_prompt", new Dictionary<string, object?>
        {
            ["prompt_length"] = submission.Value.Length
        });

        try
        {
            var request = await BuildTerminalTurnRequestAsync(session, submission.Value, cancellationToken);
            _preventSleepService.StartPreventSleep();
            using var turnCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var queryTask = ExecutePromptTurnCoreAsync(session, request, output, turnCancellationSource.Token);

            if (_localMainSessionTaskService is null || !_sessionBackgroundKeyMonitor.CanMonitor(input))
            {
                await queryTask;
                return PromptTurnResult.Completed;
            }

            var backgroundRequestTask = _sessionBackgroundKeyMonitor.WaitForBackgroundRequestAsync(
                input,
                output,
                turnCancellationSource.Token);
            var completedTask = await Task.WhenAny(queryTask, backgroundRequestTask);
            if (ReferenceEquals(completedTask, backgroundRequestTask) && await backgroundRequestTask)
            {
                turnCancellationSource.Cancel();
                try
                {
                    await queryTask;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }

                var backgroundTask = await _localMainSessionTaskService.StartBackgroundSessionAsync(
                    session,
                    request,
                    ResolveMainSessionTaskDescription(session, submission),
                    cancellationToken);
                await output.WriteLineAsync($"Backgrounded session as task {backgroundTask.Id}.");
                return PromptTurnResult.Backgrounded;
            }

            turnCancellationSource.Cancel();
            await queryTask;
            return PromptTurnResult.Completed;
        }
        finally
        {
            _preventSleepService.StopPreventSleep();
            Interlocked.Exchange(ref _mainTurnInFlight, 0);
            _sessionTurnGate.Release();
        }
    }

    private async Task<bool> ExecuteScheduledCronAsync(
        ConversationSession session,
        CronFireRequest job,
        CancellationToken cancellationToken)
    {
        if (_localMainSessionTaskService is null || string.IsNullOrWhiteSpace(job.Prompt))
        {
            return false;
        }

        if (!await _sessionTurnGate.WaitAsync(0, cancellationToken))
        {
            return false;
        }

        try
        {
            if (Volatile.Read(ref _mainTurnInFlight) != 0)
            {
                return false;
            }

            var scheduledPrompt = job.Prompt.Trim();
            if (scheduledPrompt.Length == 0)
            {
                return true;
            }

            var userMessage = ChatMessageFactory.CreateText(MessageRole.User, scheduledPrompt);
            session.Add(userMessage);
            await _transcriptStore.RecordTranscriptAsync(session, session.Messages, cancellationToken);

            var request = await BuildTerminalTurnRequestAsync(session, scheduledPrompt, cancellationToken);
            var description = $"Scheduled job {job.JobId}";
            await _localMainSessionTaskService.StartBackgroundSessionAsync(
                session,
                request,
                description,
                cancellationToken);
            return true;
        }
        finally
        {
            _sessionTurnGate.Release();
        }
    }

    private async Task ExecutePromptTurnCoreAsync(
        ConversationSession session,
        QueryTurnRequest request,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var renderedToolResultAny = false;
        var toolNamesByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        var progressMessagesByParentToolUseId = new Dictionary<string, List<ToolProgressUpdate>>(StringComparer.Ordinal);
        var renderedProgressByParentToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);

        var execution = await _turnExecutor.ExecuteAsync(
            session,
            request.UserInput,
            (chunk, _) =>
            {
                return Task.CompletedTask;
            },
            async (message, _) =>
            {
                _toolProgressMessageRenderer.TrackToolUses(message, toolNamesByToolUseId);
                var renderedToolUses = _toolUseMessageRenderer.TryRender(message);
                if (renderedToolUses.Count > 0)
                {
                    foreach (var renderedToolUse in renderedToolUses)
                    {
                        await output.WriteLineAsync(renderedToolUse.Content);
                    }

                    return;
                }

                var renderedProgress = _toolProgressMessageRenderer.TryRender(
                    message,
                    toolNamesByToolUseId,
                    progressMessagesByParentToolUseId);
                if (renderedProgress is not null)
                {
                    if (renderedProgressByParentToolUseId.TryGetValue(renderedProgress.ParentToolUseId, out var existing) &&
                        string.Equals(existing, renderedProgress.Content, StringComparison.Ordinal))
                    {
                        return;
                    }

                    renderedProgressByParentToolUseId[renderedProgress.ParentToolUseId] = renderedProgress.Content;
                    await output.WriteLineAsync(renderedProgress.Content);
                    return;
                }

                var renderedResult = _toolResultMessageRenderer.TryRender(
                    message,
                    toolNamesByToolUseId,
                    progressMessagesByParentToolUseId);
                if (renderedResult is null)
                {
                    return;
                }

                renderedToolResultAny = true;
                await output.WriteLineAsync(renderedResult.Content);
            },
            cancellationToken: cancellationToken);

        if (renderedToolResultAny)
        {
            return;
        }

        if (execution.FinalAssistantMessage is not null)
        {
            await WriteTranscriptMessageAsync(
                execution.FinalAssistantMessage,
                output,
                previousRole: null);
        }
    }

    private async Task<QueryTurnRequest> BuildTerminalTurnRequestAsync(
        ConversationSession session,
        string userInput,
        CancellationToken cancellationToken)
    {
        return await MainThreadTurnRequestBuilder.BuildAsync(
            session,
            userInput,
            _toolRegistry,
            _modelTurnContextProvider,
            cancellationToken);
    }

    private static string ResolveMainSessionTaskDescription(
        ConversationSession session,
        PromptSubmission submission)
    {
        if (!string.IsNullOrWhiteSpace(session.CustomTitle))
        {
            return session.CustomTitle!;
        }

        return submission.Value.Trim();
    }

    private async Task<string[]> WriteFooterIfChangedAsync(TextWriter output, string[] lastFooterLines)
    {
        var footerLines = _terminalFooterRenderer.Render(_appStateStore.GetState()).ToArray();
        if (footerLines.SequenceEqual(lastFooterLines, StringComparer.Ordinal))
        {
            return lastFooterLines;
        }

        foreach (var line in footerLines)
        {
            await output.WriteLineAsync(line);
        }

        return footerLines;
    }

    private async Task<MessageRole?> WriteTranscriptMessageAsync(
        ChatMessage message,
        TextWriter output,
        MessageRole? previousRole)
    {
        var renderedLines = _transcriptMessageRenderer.Render(message, previousRole);
        if (renderedLines.Count == 0)
        {
            return previousRole;
        }

        foreach (var line in renderedLines)
        {
            await output.WriteLineAsync(line);
        }

        return message.Role;
    }

    private static IReadOnlyList<string> BuildDeepLinkNoticeLines(
        ConversationSession session,
        DeepLinkRuntimeContext context)
    {
        var lines = new List<string>
        {
            $"This session was opened by an external deep link in {TildifyPath(session.ProjectDirectory)}"
        };

        if (!string.IsNullOrWhiteSpace(context.Repo))
        {
            lines.Add($"Resolved {context.Repo} from local clones.");
        }

        if (!string.IsNullOrWhiteSpace(context.DraftPrompt))
        {
            lines.Add("Draft prompt from the link is printed below because the current C# REPL cannot prefill editable input without submitting it.");
            lines.Add("Review it, then submit it manually.");
            lines.Add(context.DraftPrompt!);
        }

        return lines;
    }

    private static string TildifyPath(string path)
    {
        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(homeDirectory))
        {
            return path;
        }

        if (string.Equals(path, homeDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return "~";
        }

        if (path.StartsWith(homeDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(homeDirectory + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return "~" + path[homeDirectory.Length..];
        }

        return path;
    }

    private enum PromptTurnResult
    {
        Completed,
        Backgrounded
    }
}
