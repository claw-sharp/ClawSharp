namespace ClawSharp.Core;

public sealed record DeepLinkRuntimeContext(
    bool IsDeepLinkOrigin,
    string? DraftPrompt = null,
    string? Repo = null);
