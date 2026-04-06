// TS origin: ./utils/deepLink/banner.ts, ./utils/deepLink/protocolHandler.ts
namespace ClawSharp.Core;

public sealed record DeepLinkRuntimeContext(
    bool IsDeepLinkOrigin,
    string? DraftPrompt = null,
    string? Repo = null);
