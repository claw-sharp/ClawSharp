// TS parity status: alias resolution, built-in-first catalog assembly, and blanket deny-rule filtering are ported for the current C# tool surface; full 1:1 parity still depends on TS simple-mode/REPL-specific filtering, feature-gated tool surfaces such as ToolSearchTool, and separate MCP permission-check identity when SDK no-prefix mode is active.
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

public sealed class ToolRegistry
{
    private readonly ToolPermissionContext _toolPermissionContext;
    private readonly bool _useLivePermissionContext;
    private readonly Dictionary<string, IClawSharpTool> _builtInToolsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IClawSharpTool> _builtInToolsByLookupName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IClawSharpTool> _dynamicToolsByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IClawSharpTool> _dynamicToolsByLookupName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string>? _allowedToolNames;

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
        IReadOnlySet<string>? allowedToolNames = null)
    {
        WorkspaceRoot = workspaceRoot;
        Tasks = tasks;
        ReadFileState = readFileState ?? FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries);
        _toolPermissionContext = toolPermissionContext ?? ToolPermissionContexts.CreateEmpty();
        FileUpdateNotifier = fileUpdateNotifier ?? new NullFileUpdateNotifier();
        AgentDefinitions = agentDefinitions ?? BuiltInAgentDefinitions.GetBuiltInAgents();
        AppStateStore = appStateStore ?? new NullClawSharpAppStateStore(workspaceRoot);
        _useLivePermissionContext = appStateStore is not null && appStateStore is not NullClawSharpAppStateStore;
        PermissionPrompter = permissionPrompter ?? new NullPermissionPrompter();
        _allowedToolNames = allowedToolNames is null
            ? null
            : new HashSet<string>(allowedToolNames, StringComparer.OrdinalIgnoreCase);

        RegisterBuiltIn(new ReadTool());
        RegisterBuiltIn(new EditTool());
        RegisterBuiltIn(new WriteTool());
        RegisterBuiltIn(new GlobTool());
        RegisterBuiltIn(new GrepTool());
        RegisterBuiltIn(new BashTool());
        RegisterBuiltIn(new PowerShellTool());
        RegisterBuiltIn(new AgentTool(AgentDefinitions, agentExecutionService ?? new NullAgentExecutionService()));
        RegisterBuiltIn(new SendMessageTool());
        RegisterBuiltIn(new TaskOutputTool());
        RegisterBuiltIn(new TaskStopTool());
    }

    public string WorkspaceRoot { get; }

    public TaskRegistry Tasks { get; }

    public FileStateCache ReadFileState { get; }

    public ToolPermissionContext ToolPermissionContext =>
        _useLivePermissionContext
            ? AppStateStore.GetState().ToolPermissionContext
            : _toolPermissionContext;

    public IReadOnlyList<AgentDefinition> AgentDefinitions { get; }

    public IFileUpdateNotifier FileUpdateNotifier { get; }

    public IClawSharpAppStateStore AppStateStore { get; }

    public IPermissionPrompter PermissionPrompter { get; }

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
            onProgress,
            onMessage,
            querySource,
            currentSystemPrompt,
            availableTools ?? All);
        var validation = await tool.ValidateAsync(context, cancellationToken);
        if (!validation.IsValid)
        {
            return new ToolExecutionResult(false, validation.ErrorMessage ?? "Tool validation failed.");
        }

        return await tool.ExecuteAsync(context, cancellationToken);
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
        foreach (var tool in builtInTools.Concat(dynamicTools))
        {
            if (seenNames.Add(tool.Descriptor.Name))
            {
                publishedTools.Add(tool);
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
