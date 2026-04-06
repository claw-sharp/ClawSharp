// TS origin: ./tasks/LocalAgentTask/LocalAgentTask.tsx, ./tasks/LocalMainSessionTask.ts
using ClawSharp.Core;

namespace ClawSharp.Tasks;

public sealed record LocalAgentTask(
    string Id,
    string Description,
    TaskStatus Status,
    DateTimeOffset StartTime,
    string OutputFile,
    string Prompt,
    string AgentType,
    string? Model = null,
    CancellationTokenSource? CancellationSource = null,
    bool Retrieved = false,
    bool IsBackgrounded = true,
    bool Retain = false,
    bool DiskLoaded = false,
    string? WorktreePath = null,
    string? WorktreeBranch = null,
    AgentProgress? Progress = null,
    int LastReportedToolCount = 0,
    int LastReportedTokenCount = 0,
    long OutputOffset = 0,
    bool Notified = false,
    string? ToolUseId = null,
    DateTimeOffset? EndTime = null,
    string? Result = null,
    string? Error = null,
    IReadOnlyList<ChatMessage>? Messages = null,
    DateTimeOffset? EvictAfter = null)
    : ClawSharpTask(
        Id,
        TaskType.LocalAgent,
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
