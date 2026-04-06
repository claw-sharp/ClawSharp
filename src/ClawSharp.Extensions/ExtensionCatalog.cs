// TS origin: no direct 1:1 source yet; extension catalog scaffold for parity with ./utils/plugins/pluginLoader.ts, ./tools/SkillTool/SkillTool.ts, ./utils/hooks.ts, and ./services/mcp/config.ts.
namespace ClawSharp.Extensions;

public sealed class ExtensionCatalog
{
    public IReadOnlyList<string> RegisteredExtensionPoints { get; } =
    [
        "Plugins",
        "Skills",
        "Hooks",
        "MCP"
    ];
}
