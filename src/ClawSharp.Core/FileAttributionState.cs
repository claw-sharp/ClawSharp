// TS origin: ./utils/commitAttribution.ts
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

/// <summary>
/// Attribution state for tracking Claude's contributions to a single file.
/// </summary>
public sealed record FileAttributionState(
    [property: JsonPropertyName("contentHash")] string ContentHash,
    [property: JsonPropertyName("claudeContribution")] int ClaudeContribution,
    [property: JsonPropertyName("mtime")] double Mtime);
