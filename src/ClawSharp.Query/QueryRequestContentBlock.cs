// TS origin: ./types/message.ts, ./utils/messages.ts, ./services/api/claude.ts
// TS parity status: ports the current API request content-block shape needed for text, tool_use, and tool_result message construction; thinking and connector-text variants remain blocked with the model transport path.
namespace ClawSharp.Query;

public sealed record QueryRequestContentBlock(
    string Type,
    string? Text = null,
    string? Name = null,
    string? ToolUseId = null,
    string? Input = null,
    string? StructuredOutput = null,
    QueryRequestCacheControl? CacheControl = null);
