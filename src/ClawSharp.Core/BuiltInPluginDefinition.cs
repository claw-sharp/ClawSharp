// TS origin: ./plugins/builtinPlugins.ts, ./types/plugin.ts
namespace ClawSharp.Core;

public sealed record BuiltInPluginDefinition(
    string Name,
    string Description,
    string RootPath,
    string? Version = null,
    bool DefaultEnabled = true,
    Func<bool>? IsAvailable = null,
    IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>? Hooks = null,
    IReadOnlyDictionary<string, object?>? Settings = null,
    IReadOnlyList<string>? SkillDirectories = null);
