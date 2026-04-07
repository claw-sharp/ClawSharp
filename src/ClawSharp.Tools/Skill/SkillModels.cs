using System.Text.Json.Nodes;

namespace ClawSharp.Tools.Skill;

public record SkillDefinition(
    string Name,
    string Description,
    string Prompt,
    string? WhenToUse = null,
    string? ArgumentHint = null,
    IReadOnlyList<string>? AllowedTools = null,
    string? Model = null,
    bool DisableModelInvocation = false,
    bool UserInvocable = true,
    string Context = "inline", // "inline" or "fork"
    string? Agent = null,
    string? SkillRoot = null);

public record SkillOutput(
    bool Success,
    string CommandName,
    string? Result = null,
    string? AgentId = null,
    string? Status = null,
    IReadOnlyList<string>? AllowedTools = null,
    string? Model = null);
