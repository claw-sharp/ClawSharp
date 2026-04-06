namespace ClawSharp.Core;

public sealed record McpElicitResult(
    string Action,
    IReadOnlyDictionary<string, object?>? Content = null);
