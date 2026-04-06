// TS origin: ./services/mcp/elicitationHandler.ts
namespace ClawSharp.Core;

public sealed record McpElicitationWaitingState(
    string ActionLabel,
    bool ShowCancel = false);
