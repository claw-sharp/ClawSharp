// TS parity status: ports the partial server_tool_use usage delta shape consumed by the current TypeScript streaming usage updater.
namespace ClawSharp.Query;

public sealed record QueryUsageDeltaServerToolUse(
    int? WebSearchRequests = null,
    int? WebFetchRequests = null);
