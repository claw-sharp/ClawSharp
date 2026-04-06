namespace ClawSharp.Core;

public enum PluginOptionType
{
    String,
    Number,
    Boolean,
    Directory,
    File
}

public sealed record PluginOptionDefinition(
    PluginOptionType Type,
    string Title,
    string Description,
    bool Required = false,
    object? DefaultValue = null,
    bool Multiple = false,
    bool Sensitive = false,
    double? Min = null,
    double? Max = null);
