// TS origin: ./utils/commitAttribution.ts
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace ClawSharp.Core;

/// <summary>
/// Attribution state for tracking Claude's contributions to files across a session.
/// </summary>
public sealed record AttributionState(
    [property: JsonPropertyName("fileStates")] IReadOnlyDictionary<string, FileAttributionState> FileStates,
    [property: JsonPropertyName("sessionBaselines")] IReadOnlyDictionary<string, SessionBaseline> SessionBaselines,
    [property: JsonPropertyName("surface")] string Surface,
    [property: JsonPropertyName("startingHeadSha")] string? StartingHeadSha,
    [property: JsonPropertyName("promptCount")] int PromptCount,
    [property: JsonPropertyName("promptCountAtLastCommit")] int PromptCountAtLastCommit,
    [property: JsonPropertyName("permissionPromptCount")] int PermissionPromptCount,
    [property: JsonPropertyName("permissionPromptCountAtLastCommit")] int PermissionPromptCountAtLastCommit,
    [property: JsonPropertyName("escapeCount")] int EscapeCount,
    [property: JsonPropertyName("escapeCountAtLastCommit")] int EscapeCountAtLastCommit)
{
    public static AttributionState CreateEmpty(string surface) =>
        new AttributionState(
            ImmutableDictionary<string, FileAttributionState>.Empty,
            ImmutableDictionary<string, SessionBaseline>.Empty,
            surface,
            null,
            0, 0, 0, 0, 0, 0);
}

public sealed record SessionBaseline(
    [property: JsonPropertyName("contentHash")] string ContentHash,
    [property: JsonPropertyName("mtime")] double Mtime);
