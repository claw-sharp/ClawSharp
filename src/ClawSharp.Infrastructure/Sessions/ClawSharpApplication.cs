using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.Infrastructure;

public sealed class ClawSharpApplication
{
    private readonly Func<CancellationToken, Task<ClawSharpApplicationRuntime>>? _runtimeFactory;
    private readonly object _runtimeSyncRoot = new();
    private Task<ClawSharpApplicationRuntime>? _runtimeTask;

    public ClawSharpApplication(
        ISessionFactory sessionFactory,
        ISessionLogStore sessionLogStore,
        ITranscriptStore transcriptStore,
        AgentPersistenceService agentPersistenceService,
        ReplSessionBootstrapper replSessionBootstrapper,
        McpConfigService mcpConfigService,
        McpAuthStateService mcpAuthStateService,
        McpNeedsAuthCache mcpNeedsAuthCache,
        McpElicitationService mcpElicitationService,
        McpLifecycleManager mcpLifecycleManager,
        McpToolRegistrationService mcpToolRegistrationService,
        McpPromptCommandRegistry mcpPromptCommands,
        McpResourceCatalog mcpResourceCatalog,
        McpCommandResourceRegistrationService mcpCommandResourceRegistrationService,
        IClawSharpAppStateStore appStateStore,
        ISettingsStore settingsStore,
        IEventSink eventSink,
        IQueuedCommandQueue queuedCommandQueue,
        ClawSharpApplicationRuntime? runtime = null,
        Func<CancellationToken, Task<ClawSharpApplicationRuntime>>? runtimeFactory = null)
    {
        SessionFactory = sessionFactory;
        SessionLogStore = sessionLogStore;
        TranscriptStore = transcriptStore;
        AgentPersistenceService = agentPersistenceService;
        ReplSessionBootstrapper = replSessionBootstrapper;
        McpConfigService = mcpConfigService;
        McpAuthStateService = mcpAuthStateService;
        McpNeedsAuthCache = mcpNeedsAuthCache;
        McpElicitationService = mcpElicitationService;
        McpLifecycleManager = mcpLifecycleManager;
        McpToolRegistrationService = mcpToolRegistrationService;
        McpPromptCommands = mcpPromptCommands;
        McpResourceCatalog = mcpResourceCatalog;
        McpCommandResourceRegistrationService = mcpCommandResourceRegistrationService;
        AppStateStore = appStateStore;
        SettingsStore = settingsStore;
        EventSink = eventSink;
        QueuedCommandQueue = queuedCommandQueue;

        if (runtime is not null)
        {
            _runtimeTask = Task.FromResult(runtime);
        }

        _runtimeFactory = runtimeFactory;
    }

    public ISessionFactory SessionFactory { get; }
    public ISessionLogStore SessionLogStore { get; }
    public ITranscriptStore TranscriptStore { get; }
    public AgentPersistenceService AgentPersistenceService { get; }
    public ReplSessionBootstrapper ReplSessionBootstrapper { get; }
    public McpConfigService McpConfigService { get; }
    public McpAuthStateService McpAuthStateService { get; }
    public McpNeedsAuthCache McpNeedsAuthCache { get; }
    public McpElicitationService McpElicitationService { get; }
    public McpLifecycleManager McpLifecycleManager { get; }
    public McpToolRegistrationService McpToolRegistrationService { get; }
    public McpPromptCommandRegistry McpPromptCommands { get; }
    public McpResourceCatalog McpResourceCatalog { get; }
    public McpCommandResourceRegistrationService McpCommandResourceRegistrationService { get; }
    public IClawSharpAppStateStore AppStateStore { get; }
    public ClawSharpAppState AppState => AppStateStore.GetState();
    public ClawSharpSettings Settings => AppStateStore.GetState().Settings;
    public ISettingsStore SettingsStore { get; }
    public IEventSink EventSink { get; }
    public IQueuedCommandQueue QueuedCommandQueue { get; }
    public bool IsRuntimeInitialized
    {
        get
        {
            lock (_runtimeSyncRoot)
            {
                return _runtimeTask?.IsCompletedSuccessfully == true;
            }
        }
    }

    public CommandRegistry Commands => GetRuntimeValue(static runtime => runtime.Commands);
    public ToolRegistry Tools => GetRuntimeValue(static runtime => runtime.Tools);
    public TaskRegistry Tasks => GetRuntimeValue(static runtime => runtime.Tasks);
    public QueryEngine QueryEngine => GetRuntimeValue(static runtime => runtime.QueryEngine);
    public TerminalShell TerminalShell => GetRuntimeValue(static runtime => runtime.TerminalShell);
    public IQueryModelTurnContextProvider? ModelTurnContextProvider => GetRuntimeValue(static runtime => runtime.ModelTurnContextProvider);

    public async Task<ClawSharpApplicationRuntime> EnsureRuntimeAsync(CancellationToken cancellationToken = default)
    {
        Task<ClawSharpApplicationRuntime> runtimeTask;
        lock (_runtimeSyncRoot)
        {
            _runtimeTask ??= _runtimeFactory?.Invoke(CancellationToken.None)
                ?? throw new InvalidOperationException("Application runtime is not configured.");
            runtimeTask = _runtimeTask;
        }

        try
        {
            return await runtimeTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (runtimeTask.IsFaulted || runtimeTask.IsCanceled)
            {
                lock (_runtimeSyncRoot)
                {
                    if (ReferenceEquals(_runtimeTask, runtimeTask))
                    {
                        _runtimeTask = null;
                    }
                }
            }

            throw;
        }
    }

    private T GetRuntimeValue<T>(Func<ClawSharpApplicationRuntime, T> selector)
    {
        return selector(EnsureRuntimeAsync().GetAwaiter().GetResult());
    }
}
