// TS origin: ./Tool.ts, ./tools.ts
using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

public sealed record ToolDescriptor(
    string Name,
    string Description,
    bool IsLongRunningCapable = false,
    IReadOnlyList<ToolParameter>? Parameters = null,
    IReadOnlyList<string>? Aliases = null,
    string? SearchHint = null,
    bool ShouldDefer = false,
    bool AlwaysLoad = false,
    JsonObject? InputSchema = null,
    JsonObject? OutputSchema = null,
    bool Strict = false);
