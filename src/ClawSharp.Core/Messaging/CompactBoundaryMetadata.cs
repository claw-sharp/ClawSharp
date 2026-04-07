// TS parity status: ports the TypeScript compact_boundary payload shape for metadata-backed system messages; the live compaction runtime remains unported.
namespace ClawSharp.Core;

public sealed record CompactBoundaryMetadata(
    string Trigger,
    int PreTokens,
    string? UserContext = null,
    int? MessagesSummarized = null);
