// TS parity status: ports the streamed hook-result surface that the TypeScript stop-hook path consumes, including sync JSON-derived continuation and attachment updates; broader hook-output mutations still depend on later query-loop integration work.
using System.Text.Json.Nodes;

namespace ClawSharp.Core;

public sealed record HookExecutionUpdate(
    ChatMessage? Message = null,
    HookBlockingError? BlockingError = null,
    HookExecutionTrace? Trace = null,
    bool PreventContinuation = false,
    string? StopReason = null,
    PermissionBehavior? PermissionBehavior = null,
    string? HookPermissionDecisionReason = null,
    JsonObject? UpdatedInput = null,
    JsonNode? UpdatedMcpToolOutput = null,
    IReadOnlyList<string>? AdditionalContexts = null);
