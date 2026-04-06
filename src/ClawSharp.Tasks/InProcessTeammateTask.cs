using ClawSharp.Core;

namespace ClawSharp.Tasks;

public sealed record TeammateIdentity(
    string AgentId,
    string AgentName,
    string TeamName,
    bool PlanModeRequired,
    string ParentSessionId,
    string? Color = null);

public sealed record InProcessTeammateTask(
    string Id,
    string Description,
    TaskStatus Status,
    DateTimeOffset StartTime,
    string OutputFile,
    TeammateIdentity Identity,
    string Prompt,
    string? Model = null,
    AgentDefinition? SelectedAgent = null,
    bool AwaitingPlanApproval = false,
    PermissionMode PermissionMode = PermissionMode.Default,
    AgentProgress? Progress = null,
    IReadOnlyList<ChatMessage>? Messages = null,
    IReadOnlyList<string>? PendingUserMessages = null,
    string? SpinnerVerb = null,
    string? PastTenseVerb = null,
    bool IsIdle = false,
    bool ShutdownRequested = false,
    int LastReportedToolCount = 0,
    int LastReportedTokenCount = 0,
    long OutputOffset = 0,
    bool Notified = false,
    string? ToolUseId = null,
    DateTimeOffset? EndTime = null,
    string? Result = null,
    string? Error = null)
    : ClawSharpTask(
        Id,
        TaskType.InProcessTeammate,
        Description,
        Status,
        StartTime,
        OutputFile,
        OutputOffset,
        Notified,
        ToolUseId,
        EndTime,
        ExitCode: null,
        Prompt,
        Result,
        Error);

public static class InProcessTeammateTasks
{
    public const int MessagesUiCap = 50;

    public static IReadOnlyList<ChatMessage> AppendCappedMessage(
        IReadOnlyList<ChatMessage>? previous,
        ChatMessage message)
    {
        if (previous is null || previous.Count == 0)
        {
            return [message];
        }

        if (previous.Count >= MessagesUiCap)
        {
            return [.. previous.Skip(previous.Count - (MessagesUiCap - 1)), message];
        }

        return [.. previous, message];
    }

    public static InProcessTeammateTask? FindByAgentId(
        string agentId,
        IReadOnlyDictionary<string, ClawSharpTask> tasks)
    {
        InProcessTeammateTask? fallback = null;

        foreach (var task in tasks.Values)
        {
            if (task is not InProcessTeammateTask teammateTask ||
                !string.Equals(teammateTask.Identity.AgentId, agentId, StringComparison.Ordinal))
            {
                continue;
            }

            if (teammateTask.Status == TaskStatus.Running)
            {
                return teammateTask;
            }

            fallback ??= teammateTask;
        }

        return fallback;
    }

    public static IReadOnlyList<InProcessTeammateTask> GetAll(IReadOnlyDictionary<string, ClawSharpTask> tasks)
    {
        return tasks.Values.OfType<InProcessTeammateTask>().ToArray();
    }

    public static IReadOnlyList<InProcessTeammateTask> GetRunningSorted(IReadOnlyDictionary<string, ClawSharpTask> tasks)
    {
        return GetAll(tasks)
            .Where(static task => task.Status == TaskStatus.Running)
            .OrderBy(static task => task.Identity.AgentName, StringComparer.Ordinal)
            .ToArray();
    }
}
