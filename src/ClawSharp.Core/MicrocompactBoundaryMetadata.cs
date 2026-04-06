// TS parity status: ports the TypeScript microcompact_boundary payload shape for metadata-backed system messages; the live microcompact runtime remains unported.
namespace ClawSharp.Core;

public sealed record MicrocompactBoundaryMetadata(
    string Trigger,
    int PreTokens,
    int TokensSaved,
    IReadOnlyList<string> CompactedToolIds,
    IReadOnlyList<string> ClearedAttachmentUuids);
