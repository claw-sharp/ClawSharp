// TS origin: ./services/api/logging.ts, ./services/api/claude.ts
// TS parity status: ports the server_tool_use usage counters carried through the current TypeScript query runtime usage model.
namespace ClawSharp.Query;

public sealed record QueryUsageServerToolUse(
    int WebSearchRequests,
    int WebFetchRequests);
