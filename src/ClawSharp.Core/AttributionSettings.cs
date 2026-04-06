// TS origin: ./utils/config.ts
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

public sealed record AttributionSettings(
    [property: JsonPropertyName("commit")] string? Commit,
    [property: JsonPropertyName("pr")] string? Pr);
