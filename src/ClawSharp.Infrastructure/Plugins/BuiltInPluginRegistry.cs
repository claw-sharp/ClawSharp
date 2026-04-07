using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class BuiltInPluginRegistry
{
    private readonly IReadOnlyList<BuiltInPluginDefinition> _definitions;

    public BuiltInPluginRegistry(IReadOnlyList<BuiltInPluginDefinition>? definitions = null)
    {
        _definitions = definitions ?? [];
    }

    public IReadOnlyList<BuiltInPluginDefinition> GetDefinitions()
    {
        return _definitions;
    }
}
