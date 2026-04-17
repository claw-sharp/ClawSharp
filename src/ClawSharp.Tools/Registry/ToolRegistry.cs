// TS parity status: alias resolution, built-in-first catalog assembly, and blanket deny-rule filtering are ported for the current C# tool surface; full 1:1 parity still depends on TS simple-mode/REPL-specific filtering, feature-gated tool surfaces such as ToolSearchTool, and separate MCP permission-check identity when SDK no-prefix mode is active.
using ClawSharp.Core;
using ClawSharp.Tasks;
using ClawSharp.Tools.REPL;
using ClawSharp.Tools.Notebook;
using ClawSharp.Tools.Skill;
using ClawSharp.Tools.Plan;
using ClawSharp.Tools.Search;
using ClawSharp.Tools.Tasks;
using ClawSharp.Tools.Worktree;
using ClawSharp.Tools.Mcp;
using ClawSharp.Tools.Agent;
using ClawSharp.Tools.Lsp;

namespace ClawSharp.Tools;

public sealed class ToolRegistry
{
    private readonly ToolPermissionContext _toolPermissionContext;
    private readonly bool _useLivePermissionContext;
    private readonly IReadOnlyList<AgentDefinition> _agentDefinitions;
    private readonly Dictionary<string, IClawSharpTool> _builtInToolsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IClawSharpTool> _builtInToolsByLookupName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IClawSharpTool> _dynamicToolsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IClawSharpTool> _dynamicToolsByLookupName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string>? _allowedToolNames;
    private readonly HashSet<string>? _excludedToolNames;

    public string? AgentId { get; }

    public ToolRegistry(
        string workspaceRoot,
        TaskRegistry tasks,
        FileStateCache? readFileState = null,
        ToolPermissionContext? toolPermissionContext = null,
        IFileUpdateNotifier? fileUpdateNotifier = null,
        IReadOnlyList<AgentDefinition>? agentDefinitions = null,
        IClawSharpAppStateStore? appStateStore = null,
        IPermissionPrompter? permissionPrompter = null,
        IAgentExecutionService? agentExecutionService = null,
        INativeWebSearchService? nativeWebSearchService = null,
        ClawSharp.Core.Worktree.IWorktreeService? worktreeService = null,
        ClawSharp.Core.McpResourceCatalog? mcpResources = null,
        ClawSharp.Core.IMcpLifecycleManager? mcpLifecycle = null,
        IMcpToolRuntimeCoordinator? mcpToolRuntimeCoordinator = null,
        ClawSharp.Core.ISettingsStore? settingsStore = null,
        IReadOnlySet<string>? allowedToolNames = null,
        IReadOnlySet<string>? excludedToolNames = null,
        string? agentId = null)
    {
        WorkspaceRoot = workspaceRoot;
        Tasks = tasks;
        AgentId = agentId;
        ReadFileState = readFileState ?? FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries);
        _toolPermissionContext = toolPermissionContext ?? ToolPermissionContexts.CreateEmpty();
        FileUpdateNotifier = fileUpdateNotifier ?? new NullFileUpdateNotifier();
        _agentDefinitions = agentDefinitions ?? BuiltInAgentDefinitions.GetBuiltInAgents();
        AppStateStore = appStateStore ?? new NullClawSharpAppStateStore(workspaceRoot);
        _useLivePermissionContext = appStateStore is not null && appStateStore is not NullClawSharpAppStateStore;
        PermissionPrompter = permissionPrompter ?? new NullPermissionPrompter();
        _allowedToolNames = allowedToolNames is null
            ? null
            : new HashSet<string>(allowedToolNames, StringComparer.OrdinalIgnoreCase);
        _excludedToolNames = excludedToolNames is null
            ? null
            : new HashSet<string>(excludedToolNames, StringComparer.OrdinalIgnoreCase);
        WorktreeService = worktreeService ?? new ClawSharp.Core.Worktree.NullWorktreeService();
        McpResources = mcpResources ?? new ClawSharp.Core.McpResourceCatalog();
        McpLifecycle = mcpLifecycle;
        McpToolRuntimeCoordinator = mcpToolRuntimeCoordinator;
        SettingsStore = settingsStore;

        RegisterBuiltIn(new ReadTool());
        RegisterBuiltIn(new EditTool());
        RegisterBuiltIn(new WriteTool());
        RegisterBuiltIn(new GlobTool());
        RegisterBuiltIn(new GrepTool());
        RegisterBuiltIn(new BashTool());
        RegisterBuiltIn(new PowerShellTool());
        RegisterBuiltIn(new AgentTool(agentExecutionService ?? new NullAgentExecutionService()));
        RegisterBuiltIn(new SendMessageTool());
        RegisterBuiltIn(new SendUserMessageTool());
        RegisterBuiltIn(new AskUserQuestionTool());
        RegisterBuiltIn(new TaskOutputTool());
        RegisterBuiltIn(new TodoWriteTool());
        RegisterBuiltIn(new StructuredOutputTool());
        RegisterBuiltIn(new SleepTool());
        RegisterBuiltIn(new WebFetchTool());
        RegisterBuiltIn(new WebSearchTool(nativeWebSearchService));
        RegisterBuiltIn(new WebBrowserTool());
        RegisterBuiltIn(new NotebookEditTool());
        RegisterBuiltIn(new EnterPlanModeTool());
        RegisterBuiltIn(new ExitPlanModeTool());
        RegisterBuiltIn(new VerifyPlanExecutionTool());
        RegisterBuiltIn(new EnterWorktreeTool());
        RegisterBuiltIn(new ExitWorktreeTool());
        RegisterBuiltIn(new ToolSearchTool());
        RegisterBuiltIn(new TaskCreateTool());
        RegisterBuiltIn(new TaskGetTool());
        RegisterBuiltIn(new TaskUpdateTool());
        RegisterBuiltIn(new TaskListTool());
        RegisterBuiltIn(new TaskStopTool());
        RegisterBuiltIn(new REPLTool());
        RegisterBuiltIn(new ConfigTool());
        RegisterBuiltIn(new SkillTool());
        RegisterBuiltIn(new CronCreateTool());
        RegisterBuiltIn(new CronListTool());
        RegisterBuiltIn(new CronDeleteTool());
        RegisterBuiltIn(new RemoteTriggerTool());
        RegisterBuiltIn(new LspTool());
    }

    private static readonly HashSet<string> REPL_ONLY_TOOLS = new(StringComparer.OrdinalIgnoreCase)
    {
        "FileRead",
        "FileWrite",
        "FileEdit",
        "Glob",
        "Grep",
        "Bash",
        "NotebookEdit",
        "Agent",
        "Read", // aliases
        "Edit",
        "Write"
    };

    public bool IsReplModeEnabled => 
        Environment.GetEnvironmentVariable("CLAUDE_CODE_REPL") == "1" ||
        Environment.GetEnvironmentVariable("CLAUDE_REPL_MODE") == "1";

    public string WorkspaceRoot { get; }

    public TaskRegistry Tasks { get; }

    public FileStateCache ReadFileState { get; }

    public ToolPermissionContext ToolPermissionContext =>
        _useLivePermissionContext
            ? AppStateStore.GetState().ToolPermissionContext
            : _toolPermissionContext;

    public IReadOnlyList<AgentDefinition> AgentDefinitions =>
        _useLivePermissionContext
            ? ResolveAgentDefinitionsFromLiveState()
            : _agentDefinitions;

    public IFileUpdateNotifier FileUpdateNotifier { get; }

    public IClawSharpAppStateStore AppStateStore { get; }

    public IPermissionPrompter PermissionPrompter { get; }

    public ClawSharp.Core.Worktree.IWorktreeService WorktreeService { get; }

    public ClawSharp.Core.McpResourceCatalog McpResources { get; }

    public ClawSharp.Core.IMcpLifecycleManager? McpLifecycle { get; }

    public IMcpToolRuntimeCoordinator? McpToolRuntimeCoordinator { get; }
    public ClawSharp.Core.ISettingsStore? SettingsStore { get; }

    private IReadOnlyList<AgentDefinition> ResolveAgentDefinitionsFromLiveState()
    {
        var liveAgentDefinitions = AppStateStore.GetState().AgentDefinitions;
        return liveAgentDefinitions.Count > 0 ? liveAgentDefinitions : _agentDefinitions;
    }

    public IReadOnlyList<ToolDescriptor> All =>
        BuildPublishedToolPool()
            .Select(static tool => tool.Descriptor)
            .ToArray();

    public void Register(IClawSharpTool tool)
    {
        RegisterInternal(tool, _dynamicToolsByName, _dynamicToolsByLookupName, _builtInToolsByLookupName, allowPrimaryNameCollisionWithOppositePartition: true);
    }

    public void RegisterOrReplace(IClawSharpTool tool)
    {
        RemoveExistingTool(tool.Descriptor.Name, _dynamicToolsByName, _dynamicToolsByLookupName);
        RemoveExistingTool(tool.Descriptor.Name, _builtInToolsByName, _builtInToolsByLookupName);
        RegisterInternal(tool, _dynamicToolsByName, _dynamicToolsByLookupName, _builtInToolsByLookupName, allowPrimaryNameCollisionWithOppositePartition: true);
    }

    public void UnregisterWhere(Func<string, bool> predicate, bool includeBuiltIn = false)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (var toolName in _dynamicToolsByName.Keys.Where(predicate).ToArray())
        {
            RemoveExistingTool(toolName, _dynamicToolsByName, _dynamicToolsByLookupName);
        }

        if (!includeBuiltIn)
        {
            return;
        }

        foreach (var toolName in _builtInToolsByName.Keys.Where(predicate).ToArray())
        {
            RemoveExistingTool(toolName, _builtInToolsByName, _builtInToolsByLookupName);
        }
    }

    private void RegisterBuiltIn(IClawSharpTool tool)
    {
        RegisterInternal(tool, _builtInToolsByName, _builtInToolsByLookupName, _dynamicToolsByLookupName, allowPrimaryNameCollisionWithOppositePartition: true);
    }

    private void RegisterInternal(
        IClawSharpTool tool,
        Dictionary<string, IClawSharpTool> toolsByName,
        Dictionary<string, IClawSharpTool> toolsByLookupName,
        Dictionary<string, IClawSharpTool> oppositePartitionLookupNames,
        bool allowPrimaryNameCollisionWithOppositePartition)
    {
        if (_allowedToolNames is not null &&
            !_allowedToolNames.Contains(tool.Descriptor.Name))
        {
            return;
        }

        if (_excludedToolNames is not null &&
            _excludedToolNames.Contains(tool.Descriptor.Name))
        {
            return;
        }

        toolsByName[tool.Descriptor.Name] = tool;
        RegisterLookupName(
            tool.Descriptor.Name,
            tool,
            toolsByLookupName,
            oppositePartitionLookupNames,
            allowPrimaryNameCollisionWithOppositePartition);

        if (tool.Descriptor.Aliases is null)
        {
            return;
        }

        foreach (var alias in tool.Descriptor.Aliases)
        {
            RegisterLookupName(alias, tool, toolsByLookupName, oppositePartitionLookupNames, allowCollisionWithOppositePartition: false);
        }
    }

    private void RemoveExistingTool(
        string primaryName,
        Dictionary<string, IClawSharpTool> toolsByName,
        Dictionary<string, IClawSharpTool> toolsByLookupName)
    {
        if (!toolsByName.TryGetValue(primaryName, out var existing))
        {
            return;
        }

        toolsByName.Remove(primaryName);
        RemoveLookupName(primaryName, existing, toolsByLookupName);

        if (existing.Descriptor.Aliases is null)
        {
            return;
        }

        foreach (var alias in existing.Descriptor.Aliases)
        {
            RemoveLookupName(alias, existing, toolsByLookupName);
        }
    }

    public bool TryResolve(string toolName, out IClawSharpTool? tool)
    {
        var publishedLookup = BuildPublishedLookup();
        if (!publishedLookup.TryGetValue(toolName, out tool) || tool is null || !tool.IsEnabled())
        {
            tool = null;
            return false;
        }

        return true;
    }

    public Task<ToolExecutionResult> ExecuteAsync(
        string toolName,
        string arguments,
        ConversationSession session,
        ClawSharpSettings settings,
        Action<ToolProgressUpdate>? onProgress = null,
        Action<ChatMessage>? onMessage = null,
        string? querySource = null,
        string? agentId = null,
        IReadOnlyList<string>? currentSystemPrompt = null,
        IReadOnlyList<ToolDescriptor>? availableTools = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolve(toolName, out var tool) || tool is null)
        {
            return Task.FromResult(new ToolExecutionResult(false, $"Unknown tool '{toolName}'.")); 
        }

        return ExecuteValidatedAsync(
            tool,
            arguments,
            session,
            settings,
            onProgress,
            onMessage,
            querySource,
            agentId,
            currentSystemPrompt,
            availableTools,
            cancellationToken);
    }

    private async Task<ToolExecutionResult> ExecuteValidatedAsync(
        IClawSharpTool tool,
        string arguments,
        ConversationSession session,
        ClawSharpSettings settings,
        Action<ToolProgressUpdate>? onProgress,
        Action<ChatMessage>? onMessage,
        string? querySource,
        string? agentId,
        IReadOnlyList<string>? currentSystemPrompt,
        IReadOnlyList<ToolDescriptor>? availableTools,
        CancellationToken cancellationToken)
    {
        var context = new ToolExecutionContext(
            arguments,
            WorkspaceRoot,
            session,
            AppStateStore,
            Tasks,
            Tasks,
            settings,
            ReadFileState,
            ToolPermissionContext,
            AgentDefinitions,
            FileUpdateNotifier,
            PermissionPrompter,
            WorktreeService,
            McpResources,
            McpLifecycle,
            McpToolRuntimeCoordinator,
            SettingsStore,
            onProgress,
            onMessage,
            querySource,
            agentId ?? AgentId,
            currentSystemPrompt,
            availableTools ?? All,
            this);
        var validation = await tool.ValidateAsync(context, cancellationToken);
        if (!validation.IsValid)
        {
            return new ToolExecutionResult(false, validation.ErrorMessage ?? "Tool validation failed.");
        }

        await EnsureReadStateForEditAsync(tool, context, cancellationToken);
        return await tool.ExecuteAsync(context, cancellationToken);
    }

    private static async Task EnsureReadStateForEditAsync(
        IClawSharpTool tool,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(tool.Descriptor.Name, "Edit", StringComparison.OrdinalIgnoreCase) ||
            !FileToolInputParser.TryParseEdit(context.Arguments, out var input, out _) ||
            input is null)
        {
            return;
        }

        var permissionResolution = await FileToolPermissionEvaluator.ResolveForWriteAsync(
            input.FilePath,
            context.WorkspaceRoot,
            context.ToolPermissionContext,
            context.PermissionPrompter,
            allowCreate: false,
            cancellationToken);
        if (!permissionResolution.Allowed || permissionResolution.ResolvedPath is null)
        {
            return;
        }

        var resolvedPath = permissionResolution.ResolvedPath;
        if (!File.Exists(resolvedPath))
        {
            return;
        }

        var existingState = context.ReadFileState.Get(resolvedPath);
        if (existingState is not null && !existingState.IsPartialView)
        {
            return;
        }

        var metadata = await Task.Run(() => FileTextOperations.ReadFileWithMetadata(resolvedPath), cancellationToken);
        context.ReadFileState.Set(
            resolvedPath,
            new FileState(
                metadata.Content,
                new DateTimeOffset(File.GetLastWriteTimeUtc(resolvedPath)).ToUnixTimeMilliseconds(),
                Offset: null,
                Limit: null,
                IsPartialView: false));
    }

    private IReadOnlyList<IClawSharpTool> BuildPublishedToolPool()
    {
        var builtInTools = ToolCatalogPermissionFilter.FilterDeniedTools(
                _builtInToolsByName.Values.Where(static tool => tool.IsEnabled()),
                ToolPermissionContext)
            .OrderBy(static tool => tool.Descriptor.Name, StringComparer.OrdinalIgnoreCase);
        var dynamicTools = ToolCatalogPermissionFilter.FilterDeniedTools(
                _dynamicToolsByName.Values.Where(static tool => tool.IsEnabled()),
                ToolPermissionContext)
            .OrderBy(static tool => tool.Descriptor.Name, StringComparer.OrdinalIgnoreCase);

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var publishedTools = new List<IClawSharpTool>();
        var isRepl = IsReplModeEnabled;

        foreach (var tool in builtInTools.Concat(dynamicTools))
        {
            if (isRepl && REPL_ONLY_TOOLS.Contains(tool.Descriptor.Name))
            {
                continue;
            }

            if (seenNames.Add(tool.Descriptor.Name))
            {
                publishedTools.Add(tool);
            }
        }

        // Always include REPL if enabled, even if it's not marked as primitive
        if (isRepl && !publishedTools.Any(t => t.Descriptor.Name == "REPL"))
        {
            if (TryResolve("REPL", out var replTool) && replTool != null)
            {
                publishedTools.Insert(0, replTool);
            }
        }

        return publishedTools;
    }

    private IReadOnlyDictionary<string, IClawSharpTool> BuildPublishedLookup()
    {
        var publishedLookup = new Dictionary<string, IClawSharpTool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in BuildPublishedToolPool())
        {
            RegisterPublishedLookupName(publishedLookup, tool.Descriptor.Name, tool);

            if (tool.Descriptor.Aliases is null)
            {
                continue;
            }

            foreach (var alias in tool.Descriptor.Aliases)
            {
                RegisterPublishedLookupName(publishedLookup, alias, tool);
            }
        }

        return publishedLookup;
    }

    private static void RegisterLookupName(
        string name,
        IClawSharpTool tool,
        Dictionary<string, IClawSharpTool> targetPartitionLookupNames,
        Dictionary<string, IClawSharpTool> oppositePartitionLookupNames,
        bool allowCollisionWithOppositePartition)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Tool lookup names must be non-empty.", nameof(name));
        }

        if (targetPartitionLookupNames.TryGetValue(name, out var existing) && !ReferenceEquals(existing, tool))
        {
            throw new InvalidOperationException($"Tool lookup name '{name}' is already registered.");
        }

        if (oppositePartitionLookupNames.TryGetValue(name, out var oppositeExisting) &&
            !ReferenceEquals(oppositeExisting, tool) &&
            !(allowCollisionWithOppositePartition &&
              string.Equals(oppositeExisting.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase) &&
              string.Equals(tool.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Tool lookup name '{name}' is already registered.");
        }

        targetPartitionLookupNames[name] = tool;
    }

    private static void RemoveLookupName(
        string name,
        IClawSharpTool tool,
        Dictionary<string, IClawSharpTool> lookupNames)
    {
        if (lookupNames.TryGetValue(name, out var existing) && ReferenceEquals(existing, tool))
        {
            lookupNames.Remove(name);
        }
    }

    private static void RegisterPublishedLookupName(
        Dictionary<string, IClawSharpTool> lookupNames,
        string name,
        IClawSharpTool tool)
    {
        if (lookupNames.TryGetValue(name, out var existing) && !ReferenceEquals(existing, tool))
        {
            throw new InvalidOperationException($"Published tool lookup name '{name}' is ambiguous.");
        }

        lookupNames[name] = tool;
    }
}
