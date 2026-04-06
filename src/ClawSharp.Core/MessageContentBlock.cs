namespace ClawSharp.Core;

public sealed record MessageContentBlock(
    MessageContentKind Kind,
    string Value,
    string? Name = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
