// TS origin: ./tools/ReadMcpResourceTool/ReadMcpResourceTool.ts
namespace ClawSharp.Core;

public sealed record McpReadResourceResult(
    IReadOnlyList<McpResourceContent> Contents);
