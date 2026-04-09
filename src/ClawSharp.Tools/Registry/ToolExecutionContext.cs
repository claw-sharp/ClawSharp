using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

public sealed record ToolExecutionContext(
    string Arguments,
    string WorkspaceRoot,
    ConversationSession Session,
    IClawSharpAppStateStore AppStateStore,
    TaskRegistry Tasks,
    ITaskAppStateStore TaskAppState,
    ClawSharpSettings Settings,
    FileStateCache ReadFileState,
    ToolPermissionContext ToolPermissionContext,
    IReadOnlyList<AgentDefinition> AgentDefinitions,
    IFileUpdateNotifier FileUpdateNotifier,
    IPermissionPrompter PermissionPrompter,
    ClawSharp.Core.Worktree.IWorktreeService WorktreeService,
    ClawSharp.Core.McpResourceCatalog McpResources,
    ClawSharp.Core.IMcpLifecycleManager? McpLifecycle,
    ClawSharp.Core.ISettingsStore? SettingsStore = null,
    Action<ToolProgressUpdate>? OnProgress = null,
    Action<ChatMessage>? OnMessage = null,
    string? QuerySource = null,
    string? AgentId = null,
    IReadOnlyList<string>? CurrentSystemPrompt = null,
    IReadOnlyList<ToolDescriptor>? AvailableTools = null,
    ToolRegistry? ToolRegistry = null)
{
    public ToolExecutionContext(
        string Arguments,
        string WorkspaceRoot,
        ConversationSession Session,
        IClawSharpAppStateStore AppStateStore,
        TaskRegistry Tasks,
        ITaskAppStateStore TaskAppState,
        ClawSharpSettings Settings,
        FileStateCache ReadFileState,
        ToolPermissionContext ToolPermissionContext,
        IReadOnlyList<AgentDefinition> AgentDefinitions,
        IFileUpdateNotifier FileUpdateNotifier,
        IPermissionPrompter PermissionPrompter,
        Action<ToolProgressUpdate>? OnProgress = null,
        Action<ChatMessage>? OnMessage = null,
        string? QuerySource = null,
        string? AgentId = null,
        IReadOnlyList<string>? CurrentSystemPrompt = null,
        IReadOnlyList<ToolDescriptor>? AvailableTools = null,
        ToolRegistry? ToolRegistry = null)
        : this(
            Arguments,
            WorkspaceRoot,
            Session,
            AppStateStore,
            Tasks,
            TaskAppState,
            Settings,
            ReadFileState,
            ToolPermissionContext,
            AgentDefinitions,
            FileUpdateNotifier,
            PermissionPrompter,
            new ClawSharp.Core.Worktree.NullWorktreeService(),
            new ClawSharp.Core.McpResourceCatalog(),
            null,
            null,
            OnProgress,
            OnMessage,
            QuerySource,
            AgentId,
            CurrentSystemPrompt,
            AvailableTools,
            ToolRegistry)
    {
    }

    public void ReportProgress(string toolUseId, JsonObject data)
    {
        OnProgress?.Invoke(new ToolProgressUpdate(toolUseId, data));
    }

    public void ReportMessage(ChatMessage message)
    {
        OnMessage?.Invoke(message);
    }

    public ClawSharpAppState AppState => AppStateStore.GetState();
}
