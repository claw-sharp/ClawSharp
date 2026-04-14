// TS parity status: simplified foundation only, not a 1:1 translation yet.
using System.Diagnostics;
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
        var stopwatch = Stopwatch.StartNew();

        static T LogSyncPhase<T>(string workspaceRoot, string phase, Func<T> action)
        {
            var phaseStopwatch = Stopwatch.StartNew();
            ClawSharpTelemetry.LogDebug($"[AppFactory:{phase}] start workspace={workspaceRoot}", DebugLogLevel.Info);
            try
            {
                var result = action();
                phaseStopwatch.Stop();
                ClawSharpTelemetry.LogDebug($"[AppFactory:{phase}] complete workspace={workspaceRoot} elapsedMs={phaseStopwatch.ElapsedMilliseconds}", DebugLogLevel.Info);
                return result;
            }
            catch (Exception ex)
            {
                phaseStopwatch.Stop();
                ClawSharpTelemetry.LogDebug($"[AppFactory:{phase}] failed workspace={workspaceRoot} elapsedMs={phaseStopwatch.ElapsedMilliseconds} error={ex.GetType().Name}: {ex.Message}", DebugLogLevel.Warn);
                throw;
            }
        }

        static async Task<T> LogAsyncPhase<T>(string workspaceRoot, string phase, Func<Task<T>> action)
        {
            var phaseStopwatch = Stopwatch.StartNew();
            ClawSharpTelemetry.LogDebug($"[AppFactory:{phase}] start workspace={workspaceRoot}", DebugLogLevel.Info);
            try
            {
                var result = await action().ConfigureAwait(false);
                phaseStopwatch.Stop();
                ClawSharpTelemetry.LogDebug($"[AppFactory:{phase}] complete workspace={workspaceRoot} elapsedMs={phaseStopwatch.ElapsedMilliseconds}", DebugLogLevel.Info);
                return result;
            }
            catch (Exception ex)
            {
                phaseStopwatch.Stop();
                ClawSharpTelemetry.LogDebug($"[AppFactory:{phase}] failed workspace={workspaceRoot} elapsedMs={phaseStopwatch.ElapsedMilliseconds} error={ex.GetType().Name}: {ex.Message}", DebugLogLevel.Warn);
                throw;
            }
        }

        StartupProfiler.Checkpoint("create_default_application_start");
        workspaceRoot = Path.GetFullPath(workspaceRoot);
        ClawSharpTelemetry.Initialize(workspaceRoot);
        var startupEnvironment = LogSyncPhase(workspaceRoot, "startup-environment", StartupEnvironment.Capture);
        LogSyncPhase(workspaceRoot, "windows-shell-bootstrap", () =>
        {
            WindowsShellEnvironmentBootstrapper.Initialize();
            return true;
        });

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
        var settingsTask = LogAsyncPhase(
            workspaceRoot,
            "settings-bootstrap",
            () => settingsBootstrapper.LoadAsync(workspaceRoot, cancellationToken));

        var agentBootstrapper = new AgentBootstrapper();
        var agentTask = LogAsyncPhase(
            workspaceRoot,
            "agent-bootstrap",
            () => agentBootstrapper.LoadAsync(workspaceRoot, startupEnvironment, cancellationToken));

        var settingsResult = await settingsTask.ConfigureAwait(false);
        StartupProfiler.Checkpoint("settings_bootstrap_end");

        var settingsStore = new JsonSettingsStore(ClaudeConfigPaths.GetUserSettingsFilePath());
        var autoModeGateProvider = new LocalOnlyAutoModeGateProvider();
        var permissionContextBootstrapper = new PermissionContextBootstrapper(autoModeGateProvider);
        var toolPermissionContext = LogSyncPhase(
            workspaceRoot,
            "permission-context",
            () => permissionContextBootstrapper.Load(
                workspaceRoot,
                settingsResult.Settings,
                settingsResult.SourcePreferences));

        var extensionBootstrapper = new ExtensionBootstrapper(
            builtInPluginRegistry: BuiltInPluginCatalog.CreateRegistry());
        var extensionBootstrapResult = await LogAsyncPhase(
            workspaceRoot,
            "extension-bootstrap",
            () => extensionBootstrapper.LoadAsync(workspaceRoot, startupEnvironment, settingsResult.Settings, cancellationToken)).ConfigureAwait(false);
        var agentDefinitions = await agentTask.ConfigureAwait(false);

        var appState = ClawSharpAppState.CreateDefault(
            workspaceRoot,
            startupEnvironment,
            settingsResult.Settings,
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

        ClawSharpApplicationRuntime BuildRuntime(CancellationToken runtimeCancellationToken)
        {
            runtimeCancellationToken.ThrowIfCancellationRequested();
            var runtimeStopwatch = Stopwatch.StartNew();
            ClawSharpTelemetry.LogDebug($"[AppFactory:runtime-bootstrap] start workspace={workspaceRoot}", DebugLogLevel.Info);

            try
            {
                var currentState = appStateStore.GetState();
                var currentSettings = currentState.Settings;
                var currentToolPermissionContext = currentState.ToolPermissionContext;
                var oauthTokenSource = new ClaudeAiOAuthTokenSource();
                var authService = new AuthService(oauthTokenSource, currentSettings);
                var availabilityService = new CommandAvailabilityService(authService);
                var ideIntegrationService = new IdeIntegrationService(workspaceRoot);
                var ideMcpServerConfigResolver = new IdeMcpServerConfigResolver(ideIntegrationService);
                var diagnosticTrackingService = new DiagnosticTrackingService(mcpLifecycleManager, ideMcpServerConfigResolver);
                var desktopDeepLinkService = new DesktopDeepLinkService();
                var memoryPathResolver = new DefaultMemoryPathResolver(currentSettings, workspaceRoot);
                var memoryStorageService = new MemoryStorageService(memoryPathResolver);
                var claudeMdPromptService = new ClaudeMdPromptService();
                var commands = LogSyncPhase(
                    workspaceRoot,
                    "command-registry",
                    () => CreateCommandRegistry(
                        availabilityService,
                        ideIntegrationService,
                        desktopDeepLinkService,
                        worktreePathResolver,
                        sessionLogStore));
                var tasks = LogSyncPhase(
                    workspaceRoot,
                    "task-registry",
                    () => new TaskRegistry(
                        workspaceRoot,
                        queuedCommandQueue: queuedCommandQueue,
                        eventSink: eventSink,
                        appStateStore: appStateStore));
                var readFileState = FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries);
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
                var tools = LogSyncPhase(
                    workspaceRoot,
                    "tool-registry",
                    () =>
                    {
                        var mcpToolRuntimeCoordinator = new McpToolRuntimeCoordinator(
                            mcpLifecycleManager,
                            mcpToolRegistrationService);

                        return new ToolRegistry(
                            workspaceRoot,
                            tasks,
                            readFileState,
                            currentToolPermissionContext,
                            fileUpdateNotifier,
                            agentDefinitions.ActiveAgents,
                            appStateStore,
                            permissionPrompter,
                            agentExecutionService,
                            nativeWebSearchService: nativeWebSearchService,
                            worktreeService: new ClawSharp.Core.Worktree.NullWorktreeService(),
                            mcpResources: mcpResourceCatalog,
                            mcpLifecycle: mcpLifecycleManager,
                            mcpToolRuntimeCoordinator: mcpToolRuntimeCoordinator,
                            settingsStore: settingsStore,
                            excludedToolNames: options?.ExcludedToolNames);
                    });
                var pluginMcpResolver = new PluginMcpServerResolver(mcpSecureStorage);
                var (pluginMcpServers, pluginMcpErrors) = pluginMcpResolver.Resolve(currentState.Plugins, currentSettings);
                var (configuredMcpServers, mcpConfigErrors) = mcpConfigService.GetAllConfigs(pluginMcpServers);

                foreach (var error in pluginMcpErrors.Concat(mcpConfigErrors))
                {
                    ClawSharpTelemetry.LogDebug(
                        $"[AppFactory:mcp] scope={error.Metadata.Scope} path={error.Path} message={error.Message}",
                        error.Metadata.Severity == McpConfigErrorSeverity.Fatal ? DebugLogLevel.Warn : DebugLogLevel.Info);
                }

                if (configuredMcpServers.Count > 0)
                {
                    var connections = LogSyncPhase(
                        workspaceRoot,
                        "mcp-connect",
                        () => mcpLifecycleManager
                            .ConnectServersAsync(configuredMcpServers, cancellationToken: runtimeCancellationToken)
                            .GetAwaiter()
                            .GetResult());

                    LogSyncPhase(
                        workspaceRoot,
                        "mcp-register",
                        () =>
                        {
                            mcpToolRegistrationService
                                .RegisterToolsAsync(tools, connections, runtimeCancellationToken)
                                .GetAwaiter()
                                .GetResult();
                            mcpCommandResourceRegistrationService
                                .RegisterForConnectionsAsync(tools, connections, runtimeCancellationToken)
                                .GetAwaiter()
                                .GetResult();
                            return true;
                        });
                }
                var toolOrchestrator = new ToolOrchestrator(tools, eventSink);
                var reactiveCompactHookRunner = new QueryReactiveCompactHookRunner(tools);
                var reactiveCompactModelCallRunner = new QueryReactiveCompactModelCallRunner(modelCallExecutor);
                var reactiveCompactExecutor = new QueryReactiveCompactExecutor(
                    toolCatalog: new ToolRegistryReactiveCompactToolCatalog(tools),
                    hookRunner: reactiveCompactHookRunner,
                    modelCallRunner: reactiveCompactModelCallRunner);
                var autoCompactRunner = new QueryAutoCompactRunner(
                    executor: reactiveCompactExecutor);
                var promptOverflowRecoveryRunner = new CompositeQueryPromptOverflowRecoveryRunner(
                    new CompactBoundaryPromptOverflowRecoveryRunner(),
                    new ReactiveCompactPromptOverflowRecoveryRunner(reactiveCompactExecutor));
                var stopHookRunner = new QueryStopHookRunner(tools);
                var iterationRequestBuilder = new QueryModelIterationRequestBuilder(
                    availableToolsProvider: () => tools.All);
                var modelBackedIterationRunner = new ModelBackedIterationRunner(
                    postSamplingHookRegistry,
                    iterationRequestBuilder: iterationRequestBuilder,
                    modelCallExecutor: modelCallExecutor,
                    promptOverflowRecoveryRunner: promptOverflowRecoveryRunner,
                    autoCompactRunner: autoCompactRunner,
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
                var modelTurnContextProvider = new ReplMainThreadTurnContextProvider(
                    workspaceRoot,
                    currentSettings,
                    tools,
                    memoryStorageService,
                    claudeMdPromptService,
                    startupEnvironment,
                    appStateStore);
                var queryEngine = LogSyncPhase(
                    workspaceRoot,
                    "query-engine",
                    () => new QueryEngine(
                        currentSettings,
                        eventSink,
                        transcriptStore,
                        queryTurnRunner,
                        queuedTaskNotificationDrainer,
                        fileUpdateNotifier: fileUpdateNotifier,
                        toolRegistry: tools,
                        modelTurnContextProvider: modelTurnContextProvider,
                        appStateStore: appStateStore));
                var localMainSessionTaskService = new LocalMainSessionTaskService(
                    tasks,
                    queuedCommandQueue,
                    queryEngine,
                    appStateStore,
                    transcriptStore);
                var cronSchedulerService = new CronSchedulerService(workspaceRoot);
                var terminalShell = LogSyncPhase(
                    workspaceRoot,
                    "terminal-shell",
                    () => new TerminalShell(
                        queryEngine,
                        queuedTaskNotificationDrainer,
                        toolUseMessageRenderer,
                        toolProgressMessageRenderer,
                        toolResultMessageRenderer,
                        commands,
                        currentSettings,
                        eventSink,
                        sessionFactory,
                        transcriptStore,
                        transcriptMessageRenderer: transcriptMessageRenderer,
                        appStateStore: appStateStore,
                        readFileState: readFileState,
                        toolRegistry: tools,
                        modelTurnContextProvider: modelTurnContextProvider,
                        localMainSessionTaskService: localMainSessionTaskService,
                        cronSchedulerService: cronSchedulerService));
                var runtime = new ClawSharpApplicationRuntime(
                    commands,
                    tools,
                    tasks,
                    queryEngine,
                    terminalShell,
                    modelTurnContextProvider);
                runtimeStopwatch.Stop();
                ClawSharpTelemetry.LogDebug($"[AppFactory:runtime-bootstrap] complete workspace={workspaceRoot} elapsedMs={runtimeStopwatch.ElapsedMilliseconds}", DebugLogLevel.Info);
                return runtime;
            }
            catch (Exception ex)
            {
                runtimeStopwatch.Stop();
                ClawSharpTelemetry.LogDebug($"[AppFactory:runtime-bootstrap] failed workspace={workspaceRoot} elapsedMs={runtimeStopwatch.ElapsedMilliseconds} error={ex.GetType().Name}: {ex.Message}", DebugLogLevel.Warn);
                throw;
            }
        }

        var initializationMode = options?.InitializationMode ?? ClawSharpApplicationInitializationMode.Eager;
        var eagerRuntime = initializationMode == ClawSharpApplicationInitializationMode.Eager
            ? BuildRuntime(cancellationToken)
            : null;

        var application = LogSyncPhase(
            workspaceRoot,
            "application-compose",
            () => new ClawSharpApplication(
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
                settingsStore,
                eventSink,
                queuedCommandQueue,
                runtime: eagerRuntime,
                runtimeFactory: cancellationToken => Task.FromResult(BuildRuntime(cancellationToken))));
        StartupProfiler.Checkpoint("create_default_application_end");
        stopwatch.Stop();
        ClawSharpTelemetry.LogEvent(
            "tengu_application_factory_complete",
            new Dictionary<string, object?>
            {
                ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                ["plugin_count"] = extensionBootstrapResult.Plugins.Count,
                ["skill_count"] = extensionBootstrapResult.Skills.Count,
                ["agent_count"] = agentDefinitions.ActiveAgents.Count,
                ["runtime_initialization_mode"] = initializationMode.ToString()
            });
        ClawSharpTelemetry.RecordMetric("application_factory.duration_ms", stopwatch.Elapsed.TotalMilliseconds);
        return application;
    }

    private static CommandRegistry CreateCommandRegistry(
        CommandAvailabilityService availabilityService,
        IdeIntegrationService ideIntegrationService,
        DesktopDeepLinkService desktopDeepLinkService,
        GitWorktreePathResolver worktreePathResolver,
        ISessionLogStore sessionLogStore)
    {
        var commands = new CommandRegistry(availabilityService);
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
        return commands;
    }
}
