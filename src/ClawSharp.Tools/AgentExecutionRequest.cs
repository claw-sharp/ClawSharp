// TS origin: ./tools/AgentTool/AgentTool.tsx
using System.Text.Json.Serialization;

namespace ClawSharp.Tools;

public sealed record AgentExecutionRequest(
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("prompt")] string? Prompt,
    [property: JsonPropertyName("subagent_type")] string? SubagentType,
    [property: JsonPropertyName("model")] string? Model,
    [property: JsonPropertyName("run_in_background")] bool? RunInBackground,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("team_name")] string? TeamName,
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("isolation")] string? Isolation,
    [property: JsonPropertyName("cwd")] string? Cwd);
