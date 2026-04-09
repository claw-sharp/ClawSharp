// TS parity status: simplified foundation only, not a 1:1 translation yet.
using ClawSharp.Core;
using ClawSharp.Query;
using ClawSharp.Tasks;
using ClawSharp.Tools;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.Infrastructure;

public static class ClawSharpApplicationFactory
{
    public static async Task<ClawSharpApplication> CreateDefaultAsync(CancellationToken cancellationToken = default)
    {
        return await CreateForWorkspaceAsync(Directory.GetCurrentDirectory(), cancellationToken);
    }

    public static async Task<ClawSharpApplication> CreateDefaultAsync(
        ClawSharpApplicationFactoryOptions options,
        CancellationToken cancellationToken = default)
    {
        return await CreateForWorkspaceAsync(Directory.GetCurrentDirectory(), cancellationToken, options);
    }

    public static async Task<ClawSharpApplication> CreateForWorkspaceAsync(
        string workspaceRoot,
        CancellationToken cancellationToken = default)
    {
        return await CreateForWorkspaceAsync(
            workspaceRoot,
            cancellationToken,
            options: null);
    }

    public static async Task<ClawSharpApplication> CreateForWorkspaceAsync(
        string workspaceRoot,
        CancellationToken cancellationToken,
        ClawSharpApplicationFactoryOptions? options)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        StartupProfiler.Checkpoint("create_default_application_start");
        workspaceRoot = Path.GetFullPath(workspaceRoot);
        ClawSharpTelemetry.Initialize(workspaceRoot);
        var startupEnvironment = StartupEnvironment.Capture();
        WindowsShellEnvironmentBootstrapper.Initialize();
        var transcriptStore = new JsonlTranscriptStore();
        var agentPersistenceService = new AgentPersistenceService(transcriptStore);
        var sessionLogStore = new DiskSessionLogStore();
        var worktreePathResolver = new GitWorktreePathResolver();
        var gitHubRepoPathMappingService = new GitHubRepoPathMappingService();
        var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
        var eventSink = new InMemoryEventSink();
        var mcpConfigService = new McpConfigService(workspaceRoot);
        var mcpSecureStorage = McpSecureStorageFactory.CreateDefault();
        var mcpAuthStateService = new McpAuthStateService(mcpSecureStorage);
        var mcpNeedsAuthCache = new McpNeedsAuthCache();
        var mcpSdkHttpTransportFactory = new McpSdkHttpTransportFactory(mcpSecureStorage, mcpAuthStateService);
        var mcpElicitationService = new McpElicitationService(eventSink);
        var mcpClientConnector = new SdkMcpClientConnector(mcpSdkHttpTransportFactory, mcpNeedsAuthCache, mcpElicitationService);
        var mcpLifecycleManager = new McpLifecycleManager(mcpClientConnector, mcpAuthStateService, mcpNeedsAuthCache);
        var mcpToolRegistrationService = new McpToolRegistrationService(mcpLifecycleManager);
        var mcpPromptCommands = new McpPromptCommandRegistry();
        var mcpResourceCatalog = new McpResourceCatalog();
        var mcpCommandResourceRegistrationService = new McpCommandResourceRegistrationService(
            mcpLifecycleManager,
            mcpPromptCommands,
            mcpResourceCatalog);
        var replSessionBootstrapper = new ReplSessionBootstrapper(
            sessionFactory,
            sessionLogStore,
            transcriptStore,
            worktreePathResolver,
            workspaceRoot);
        StartupProfiler.Checkpoint("settings_bootstrap_start");
        var settingsBootstrapper = new SettingsBootstrapper();
        var settingsResult = await settingsBootstrapper.LoadAsync(workspaceRoot, cancellationToken);
        StartupProfiler.Checkpoint("settings_bootstrap_end");
        var settingsStore = new JsonSettingsStore(ClaudeConfigPaths.GetUserSettingsFilePath());
        var settings = settingsResult.Settings;
        var autoModeGateProvider = new LocalOnlyAutoModeGateProvider();
        var permissionContextBootstrapper = new PermissionContextBootstrapper(autoModeGateProvider);
        var toolPermissionContext = permissionContextBootstrapper.Load(
            workspaceRoot,
            settings,
            settingsResult.SourcePreferences);
        var extensionBootstrapper = new ExtensionBootstrapper();
        var extensionBootstrapResult = await extensionBootstrapper.LoadAsync(workspaceRoot, startupEnvironment, settings, cancellationToken);
        var agentBootstrapper = new AgentBootstrapper();
        var agentDefinitions = await agentBootstrapper.LoadAsync(workspaceRoot, startupEnvironment, cancellationToken);
        var appState = ClawSharpAppState.CreateDefault(
            workspaceRoot,
            startupEnvironment,
            settings,
            settingsResult.Issues,
            extensionBootstrapResult.PluginInstallations,
            extensionBootstrapResult.Plugins,
            extensionBootstrapResult.Skills,
            agentDefinitions.ActiveAgents,
            extensionBootstrapResult.Hooks,
            toolPermissionContext);
        var onChangeAppState = new OnChangeClawSharpAppState(settingsStore);
        var appStateStore = new ClawSharpAppStateStore(appState, onChangeAppState.Handle);
        var queuedCommandQueue = new InMemoryQueuedCommandQueue();
        _ = gitHubRepoPathMappingService.UpdateAsync(workspaceRoot, cancellationToken);
        
        var oauthTokenSource = new ClaudeAiOAuthTokenSource();
        var authService = new AuthService(oauthTokenSource, settings);
        var availabilityService = new CommandAvailabilityService(authService);
        var ideIntegrationService = new IdeIntegrationService(workspaceRoot);
        var ideMcpServerConfigResolver = new IdeMcpServerConfigResolver(ideIntegrationService);
        var diagnosticTrackingService = new DiagnosticTrackingService(mcpLifecycleManager, ideMcpServerConfigResolver);
        var desktopDeepLinkService = new DesktopDeepLinkService();
        
        var memoryPathResolver = new DefaultMemoryPathResolver(settings, workspaceRoot);
        var memoryStorageService = new MemoryStorageService(memoryPathResolver);
        
        var commands = new CommandRegistry(availabilityService);
        var tasks = new TaskRegistry(workspaceRoot, queuedCommandQueue: queuedCommandQueue, eventSink: eventSink, appStateStore: appStateStore);
        var readFileState = FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries);
        toolPermissionContext = appStateStore.GetState().ToolPermissionContext;
        var fileUpdateNotifier = new CompositeFileUpdateNotifier(
            new EventSinkFileUpdateNotifier(eventSink),
            new DiagnosticTrackingFileUpdateNotifier(diagnosticTrackingService),
            new VscodeSdkFileUpdateNotifier(mcpConfigService, mcpLifecycleManager));
        var permissionPrompter = options?.PermissionPrompter ?? new SpectrePermissionPrompter();
        var postSamplingHookRegistry = new PostSamplingHookRegistry();
        var modelConfigProvider = new EnvironmentQueryModelHttpClientConfigProvider(mcpSecureStorage);
        var authAccountStateProvider = new SecureStorageQueryAuthAccountStateProvider(mcpSecureStorage);
        var authFailureRecoveryRunner = new NoOpQueryAuthFailureRecoveryRunner();
        var modelStreamingClient = new QueryModelSseStreamingClient();
        var modelStreamUpdateParser = new QueryModelAnthropicStreamUpdateParser();
        var modelCallExecutor = new QueryModelHttpCallExecutor(
            modelConfigProvider,
            modelStreamingClient,
            modelStreamUpdateParser,
            authAccountStateProvider,
            authFailureRecoveryRunner);
        var nativeWebSearchService = new NativeWebSearchService(modelCallExecutor);
        var agentExecutionService = new LocalAgentExecutionService(
            eventSink,
            transcriptStore,
            agentPersistenceService,
            queuedCommandQueue,
            modelCallExecutor,
            nativeWebSearchService: nativeWebSearchService);
        var tools = new ToolRegistry(
            workspaceRoot,
            tasks,
            readFileState,
            toolPermissionContext,
            fileUpdateNotifier,
            agentDefinitions.ActiveAgents,
            appStateStore,
            permissionPrompter,
            agentExecutionService,
            nativeWebSearchService: nativeWebSearchService,
            worktreeService: new ClawSharp.Core.Worktree.NullWorktreeService(), 
            mcpResources: mcpResourceCatalog,
            mcpLifecycle: mcpLifecycleManager,
            settingsStore: settingsStore);
        var toolOrchestrator = new ToolOrchestrator(tools, eventSink);
        var reactiveCompactHookRunner = new QueryReactiveCompactHookRunner(tools);
        var reactiveCompactModelCallRunner = new QueryReactiveCompactModelCallRunner(modelCallExecutor);
        var reactiveCompactExecutor = new QueryReactiveCompactExecutor(
            toolCatalog: new ToolRegistryReactiveCompactToolCatalog(tools),
            hookRunner: reactiveCompactHookRunner,
            modelCallRunner: reactiveCompactModelCallRunner);
        var promptOverflowRecoveryRunner = new CompositeQueryPromptOverflowRecoveryRunner(
            new CompactBoundaryPromptOverflowRecoveryRunner(),
            new ReactiveCompactPromptOverflowRecoveryRunner(reactiveCompactExecutor));
        var stopHookRunner = new QueryStopHookRunner(tools);
        var iterationRequestBuilder = new QueryModelIterationRequestBuilder(
            availableTools: tools.All);
        var modelBackedIterationRunner = new ModelBackedIterationRunner(
            postSamplingHookRegistry,
            iterationRequestBuilder: iterationRequestBuilder,
            modelCallExecutor: modelCallExecutor,
            promptOverflowRecoveryRunner: promptOverflowRecoveryRunner,
            toolOrchestrator: toolOrchestrator,
            stopHookRunner: stopHookRunner);
        var queryTurnRunner = new ExplicitToolTurnRunner(
            toolOrchestrator,
            stopHookRunner,
            modelBackedIterationRunner,
            postSamplingHookRegistry: postSamplingHookRegistry,
            promptOverflowRecoveryRunner: promptOverflowRecoveryRunner);
        var queuedTaskNotificationDrainer = new QueuedTaskNotificationDrainer(queuedCommandQueue, transcriptStore, tasks);
        var terminalProgressIndicatorRenderer = new TerminalProgressIndicatorRenderer();
        var toolUseMessageRenderer = new ToolUseMessageRenderer(tools);
        var toolProgressMessageRenderer = new ToolProgressMessageRenderer(tools, terminalProgressIndicatorRenderer);
        var toolResultMessageRenderer = new ToolResultMessageRenderer(tools);
        var transcriptMessageRenderer = new TranscriptMessageRenderer();
        var backgroundTasksCommandRenderer = new BackgroundTasksCommandRenderer();
        commands.Register(new AddClaudeKeyCommandHandler());
        commands.Register(new HelpCommandHandler(commands));
        commands.Register(new RenameCommandHandler());
        commands.Register(new ResumeCommandHandler(sessionLogStore, worktreePathResolver));
        commands.Register(new TasksCommandHandler(backgroundTasksCommandRenderer));
        commands.Register(new IdeCommandHandler(ideIntegrationService));
        commands.Register(new ChromeCommandHandler());
        commands.Register(new DesktopCommandHandler(desktopDeepLinkService));
        commands.Register(new VersionCommandHandler());
        commands.Register(new SettingsCommandHandler());
        commands.Register(new ProviderCommandHandler());
        commands.Register(new ClearCommandHandler());
        var modelTurnContextProvider = new ReplMainThreadTurnContextProvider(
            workspaceRoot,
            settings,
            tools,
            memoryStorageService,
            startupEnvironment,
            appStateStore);
        var queryEngine = new QueryEngine(
            settings,
            eventSink,
            transcriptStore,
            queryTurnRunner,
            queuedTaskNotificationDrainer,
            fileUpdateNotifier: fileUpdateNotifier,
            toolRegistry: tools,
            modelTurnContextProvider: modelTurnContextProvider,
            appStateStore: appStateStore);
        var localMainSessionTaskService = new LocalMainSessionTaskService(
            tasks,
            queuedCommandQueue,
            queryEngine,
            appStateStore,
            transcriptStore);
        var cronSchedulerService = new CronSchedulerService(workspaceRoot);
        var terminalShell = new TerminalShell(
            queryEngine,
            queuedTaskNotificationDrainer,
            toolUseMessageRenderer,
            toolProgressMessageRenderer,
            toolResultMessageRenderer,
            commands,
            settings,
            eventSink,
            sessionFactory,
            transcriptStore,
            transcriptMessageRenderer: transcriptMessageRenderer,
            appStateStore: appStateStore,
            readFileState: readFileState,
            toolRegistry: tools,
            modelTurnContextProvider: modelTurnContextProvider,
            localMainSessionTaskService: localMainSessionTaskService,
            cronSchedulerService: cronSchedulerService);

        var application = new ClawSharpApplication(
            sessionFactory,
            sessionLogStore,
            transcriptStore,
            agentPersistenceService,
            replSessionBootstrapper,
            mcpConfigService,
            mcpAuthStateService,
            mcpNeedsAuthCache,
            mcpElicitationService,
            mcpLifecycleManager,
            mcpToolRegistrationService,
            mcpPromptCommands,
            mcpResourceCatalog,
            mcpCommandResourceRegistrationService,
            appStateStore,
            settings,
            settingsStore,
            eventSink,
            queuedCommandQueue,
            commands,
            tools,
            tasks,
            queryEngine,
            terminalShell,
            modelTurnContextProvider);
        StartupProfiler.Checkpoint("create_default_application_end");
        stopwatch.Stop();
        ClawSharpTelemetry.LogEvent(
            "tengu_application_factory_complete",
            new Dictionary<string, object?>
            {
                ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                ["plugin_count"] = extensionBootstrapResult.Plugins.Count,
                ["skill_count"] = extensionBootstrapResult.Skills.Count,
                ["agent_count"] = agentDefinitions.ActiveAgents.Count
            });
        ClawSharpTelemetry.RecordMetric("application_factory.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
        return application;
    }
}
