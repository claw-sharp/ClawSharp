using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.Infrastructure;

public sealed class ClawSharpApplication
{
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
        ClawSharpSettings settings,
        ISettingsStore settingsStore,
        IEventSink eventSink,
        IQueuedCommandQueue queuedCommandQueue,
        CommandRegistry commands,
        ToolRegistry tools,
        TaskRegistry tasks,
        QueryEngine queryEngine,
        TerminalShell terminalShell,
        IQueryModelTurnContextProvider? modelTurnContextProvider = null)
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
        Settings = settings;
        SettingsStore = settingsStore;
        EventSink = eventSink;
        QueuedCommandQueue = queuedCommandQueue;
        Commands = commands;
        Tools = tools;
        Tasks = tasks;
        QueryEngine = queryEngine;
        TerminalShell = terminalShell;
        ModelTurnContextProvider = modelTurnContextProvider;
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
    public ClawSharpSettings Settings { get; }
    public ISettingsStore SettingsStore { get; }
    public IEventSink EventSink { get; }
    public IQueuedCommandQueue QueuedCommandQueue { get; }
    public CommandRegistry Commands { get; }
    public ToolRegistry Tools { get; }
    public TaskRegistry Tasks { get; }
    public QueryEngine QueryEngine { get; }
    public TerminalShell TerminalShell { get; }
    public IQueryModelTurnContextProvider? ModelTurnContextProvider { get; }
}
