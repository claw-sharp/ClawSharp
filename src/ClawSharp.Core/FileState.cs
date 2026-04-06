// TS origin: ./utils/fileStateCache.ts
namespace ClawSharp.Core;

public sealed record FileState(
    string Content,
    long Timestamp,
    int? Offset,
    int? Limit,
    bool IsPartialView = false);
