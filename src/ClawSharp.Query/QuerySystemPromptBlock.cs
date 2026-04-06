// TS parity status: ports the split system-prompt block shape used for prompt caching and boundary handling in the current TypeScript request-construction path.
namespace ClawSharp.Query;

public sealed record QuerySystemPromptBlock(
    string Text,
    string? CacheScope = null,
    QueryRequestCacheControl? CacheControl = null);
