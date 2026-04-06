// TS parity status: queued task-notification drain is ported for the current REPL loop; full 1:1 parity still depends on the real REPL state model and queue processor.
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tools;
using System.Text;

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
    private readonly IClawSharpAppStateStore _appStateStore;
    private readonly PromptInputReader _promptInputReader;
    private readonly IInteractionService _interactionService;
    private readonly FileStateCache _readFileState;
    private readonly ToolRegistry? _toolRegistry;
    private readonly IQueryModelTurnContextProvider? _modelTurnContextProvider;
    private readonly LocalMainSessionTaskService? _localMainSessionTaskService;
    private readonly ISessionBackgroundKeyMonitor _sessionBackgroundKeyMonitor;
    private readonly IPreventSleepService _preventSleepService;

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
        IPreventSleepService? preventSleepService = null)
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
        ClawSharpTelemetry.LogEvent("tengu_input_prompt", new Dictionary<string, object?>
        {
            ["prompt_length"] = submission.Value.Length
        });

        var request = await BuildTerminalTurnRequestAsync(session, submission.Value, cancellationToken);
        _preventSleepService.StartPreventSleep();
        try
        {
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
        }
    }

    private async Task ExecutePromptTurnCoreAsync(
        ConversationSession session,
        QueryTurnRequest request,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var streamedChunks = new StringBuilder();
        var renderedToolResultAny = false;
        var toolNamesByToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);
        var progressMessagesByParentToolUseId = new Dictionary<string, List<ToolProgressUpdate>>(StringComparer.Ordinal);
        var renderedProgressByParentToolUseId = new Dictionary<string, string>(StringComparer.Ordinal);

        var queryResult = await _queryEngine.RunTurnAsync(
            session,
            request,
            (chunk, _) =>
            {
                streamedChunks.Append(chunk);
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
            cancellationToken);

        if (renderedToolResultAny)
        {
            return;
        }

        if (streamedChunks.Length > 0)
        {
            await WriteTranscriptMessageAsync(
                new ChatMessage(
                    Guid.NewGuid().ToString("N"),
                    MessageRole.Assistant,
                    [new MessageContentBlock(MessageContentKind.Text, streamedChunks.ToString())],
                    DateTimeOffset.UtcNow),
                output,
                previousRole: null);
            return;
        }

        if (queryResult.AssistantMessage is not null)
        {
            await WriteTranscriptMessageAsync(
                queryResult.AssistantMessage,
                output,
                previousRole: null);
        }
    }

    private async Task<QueryTurnRequest> BuildTerminalTurnRequestAsync(
        ConversationSession session,
        string userInput,
        CancellationToken cancellationToken)
    {
        var request = QueryTurnRequest.Create(session, userInput) with
        {
            AbortReason = QueryAbortReason.Interrupt
        };

        if (_toolRegistry is not null)
        {
            request = request with
            {
                InitialToolUseContext = QueryToolUseContextStateFactory.CreateFromToolRegistry(_toolRegistry)
            };
        }

        if (_modelTurnContextProvider is not null)
        {
            request = request with
            {
                ModelTurnContext = await _modelTurnContextProvider.GetReplMainThreadContextAsync(cancellationToken)
            };
        }

        return request;
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
