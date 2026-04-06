// TS origin: ./utils/commitAttribution.ts
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

public sealed record AttributionData(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("summary")] AttributionSummary Summary,
    [property: JsonPropertyName("files")] IReadOnlyDictionary<string, FileAttribution> Files,
    [property: JsonPropertyName("surfaceBreakdown")] IReadOnlyDictionary<string, SurfaceBreakdownEntry> SurfaceBreakdown,
    [property: JsonPropertyName("excludedGenerated")] IReadOnlyList<string> ExcludedGenerated,
    [property: JsonPropertyName("sessions")] IReadOnlyList<string> Sessions);

public sealed record AttributionSummary(
    [property: JsonPropertyName("claudePercent")] int ClaudePercent,
    [property: JsonPropertyName("claudeChars")] int ClaudeChars,
    [property: JsonPropertyName("humanChars")] int HumanChars,
    [property: JsonPropertyName("surfaces")] IReadOnlyList<string> Surfaces);

public sealed record FileAttribution(
    [property: JsonPropertyName("claudeChars")] int ClaudeChars,
    [property: JsonPropertyName("humanChars")] int HumanChars,
    [property: JsonPropertyName("percent")] int Percent,
    [property: JsonPropertyName("surface")] string Surface);

public sealed record SurfaceBreakdownEntry(
    [property: JsonPropertyName("claudeChars")] int ClaudeChars,
    [property: JsonPropertyName("percent")] int Percent);
