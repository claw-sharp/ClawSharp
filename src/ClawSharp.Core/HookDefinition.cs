// TS origin: ./schemas/hooks.ts, ./types/hooks.ts, ./utils/hooks/hooksSettings.ts
namespace ClawSharp.Core;

public enum HookKind
{
    Command,
    Prompt,
    Agent,
    Http
}

public enum HookShell
{
    Bash,
    PowerShell
}

public sealed record HookCommandDefinition(
    HookKind Type,
    string? Command = null,
    string? Prompt = null,
    string? Url = null,
    string? If = null,
    HookShell? Shell = null,
    int? TimeoutSeconds = null,
    string? StatusMessage = null,
    bool Once = false,
    bool Async = false,
    bool AsyncRewake = false,
    IReadOnlyDictionary<string, string>? Headers = null,
    IReadOnlyList<string>? AllowedEnvVars = null,
    string? Model = null);

public sealed record HookMatcherDefinition(
    string? Matcher,
    IReadOnlyList<HookCommandDefinition> Hooks);

public sealed record HookDefinition(
    HookEvent Event,
    string Source,
    string? Matcher,
    HookCommandDefinition Command,
    string? PluginId = null,
    string? PluginRoot = null,
    string? SkillName = null,
    string? SkillRoot = null);

public sealed record HookExecutionRequest(
    HookEvent Event,
    string WorkingDirectory,
    string InputJson,
    string? MatcherValue = null,
    IReadOnlyDictionary<string, string>? EnvironmentVariables = null,
    ClawSharpSettings? Settings = null,
    string? HookName = null,
    string? ToolUseId = null);

public sealed record HookExecutionTrace(
    HookDefinition Hook,
    bool Matched,
    bool Succeeded,
    int? ExitCode,
    string Stdout,
    string Stderr,
    string? Error);

public sealed record HookExecutionBatchResult(
    IReadOnlyList<HookExecutionTrace> Traces,
    bool HasBlockingFailure);
