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
