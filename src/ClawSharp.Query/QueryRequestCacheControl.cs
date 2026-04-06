// TS parity status: ports the request-side cache_control wire shape used by system prompt and message blocks in the current TypeScript API construction path.
namespace ClawSharp.Query;

public sealed record QueryRequestCacheControl(
    string Type,
    string? Scope = null);
