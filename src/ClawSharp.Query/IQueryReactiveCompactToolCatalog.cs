// TS origin: ./services/compact/compact.ts, ./utils/toolSearch.ts
// TS parity status: ports the reactive-compact tool-catalog seam beneath the C# overflow recovery path so the compact-summary request can carry the currently representable compact tool subset instead of inventing a hidden no-tools path; ToolSearch and exact MCP filtering remain delegated to later ports.
using ClawSharp.Core;
using ClawSharp.Tools;

namespace ClawSharp.Query;

public interface IQueryReactiveCompactToolCatalog
{
    IReadOnlyList<QueryRequestTool> GetTools(
        QueryTurnRequest request,
        QueryLoopState priorState,
        ConversationSession session,
        ClawSharpSettings settings);
}

public sealed class NoOpQueryReactiveCompactToolCatalog : IQueryReactiveCompactToolCatalog
{
    public IReadOnlyList<QueryRequestTool> GetTools(
        QueryTurnRequest request,
        QueryLoopState priorState,
        ConversationSession session,
        ClawSharpSettings settings)
    {
        return [];
    }
}

public sealed class ToolRegistryReactiveCompactToolCatalog : IQueryReactiveCompactToolCatalog
{
    private readonly ToolRegistry _toolRegistry;

    public ToolRegistryReactiveCompactToolCatalog(ToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
    }

    public IReadOnlyList<QueryRequestTool> GetTools(
        QueryTurnRequest request,
        QueryLoopState priorState,
        ConversationSession session,
        ClawSharpSettings settings)
    {
        return _toolRegistry.All
            .Where(tool => string.Equals(tool.Name, "Read", StringComparison.Ordinal))
            .Select(QueryRequestBuilder.ToRequestTool)
            .ToArray();
    }
}
