// TS origin: ./Tool.ts, ./query.ts
// TS parity status: ports the current query-loop snapshotting boundary for the subset of ToolUseContext state ClawSharp can read from the C# tool registry and app state today.
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Query;

public static class QueryToolUseContextStateFactory
{
    public static QueryToolUseContextState CreateFromToolRegistry(ToolRegistry toolRegistry)
    {
        var appState = toolRegistry.AppStateStore.GetState();
        return new QueryToolUseContextState(
            FileStateCache.Clone(toolRegistry.ReadFileState),
            appState.ToolPermissionContext,
            appState.MainLoopModel);
    }
}
