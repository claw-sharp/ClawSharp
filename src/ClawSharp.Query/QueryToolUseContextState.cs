// TS parity status: ports the query-loop-carried subset of TypeScript ToolUseContext that current C# query iterations can actually preserve across turns; broader abort-controller, JSX/UI, hook, and notification surfaces remain blocked.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryToolUseContextState(
    FileStateCache ReadFileState,
    ToolPermissionContext ToolPermissionContext,
    string? MainLoopModel = null)
{
    public static QueryToolUseContextState Empty { get; } = new(
        FileStateCache.CreateWithSizeLimit(FileStateCache.DefaultMaxEntries),
        ToolPermissionContexts.CreateEmpty());
}
