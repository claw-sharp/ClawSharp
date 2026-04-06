// TS origin: ./tools/AgentTool/loadAgentsDir.ts, ./tools/AgentTool/builtInAgents.ts, ./tools/AgentTool/built-in/*.ts
namespace ClawSharp.Core;

public sealed record AgentDefinition(
    string AgentType,
    string WhenToUse,
    string Source,
    string BaseDirectory,
    string SystemPrompt,
    IReadOnlyList<string>? Tools = null,
    IReadOnlyList<string>? DisallowedTools = null,
    IReadOnlyList<string>? Skills = null,
    string? Color = null,
    string? Model = null,
    PermissionMode? PermissionMode = null,
    int? MaxTurns = null,
    string? Filename = null,
    bool? Background = null,
    string? InitialPrompt = null,
    string? Memory = null,
    string? Isolation = null,
    bool OmitClaudeMd = false);

public sealed record AgentLoadFailure(
    string Path,
    string Error);

public sealed record AgentDefinitionsCatalog(
    IReadOnlyList<AgentDefinition> ActiveAgents,
    IReadOnlyList<AgentDefinition> AllAgents,
    IReadOnlyList<AgentLoadFailure>? FailedFiles = null);
