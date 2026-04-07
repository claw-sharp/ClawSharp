namespace ClawSharp.Core;

public sealed class PluginEnabledSetting
{
    public bool? Enabled { get; init; }
    public IReadOnlyList<string>? VersionConstraints { get; init; }
}

public sealed class PluginConfigSettings
{
    public IReadOnlyDictionary<string, object?> Options { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}
