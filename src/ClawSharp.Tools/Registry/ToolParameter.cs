namespace ClawSharp.Tools;

public sealed record ToolParameter(
    string Name,
    string Description,
    bool Required = true);
