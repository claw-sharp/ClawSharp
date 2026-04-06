// TS origin: ./services/api/claude.ts
// TS parity status: ports the configuration contract for the main query-model HTTP client boundary, including the first-party auth-token vs API-key split used by the TypeScript model runtime.
using ClawSharp.Core;

namespace ClawSharp.Query;

public sealed record QueryModelHttpClientConfig(
    string BaseUrl,
    string? ApiKey = null,
    string? AuthToken = null,
    ModelTransportKind TransportKind = ModelTransportKind.AnthropicMessages,
    ApiProviderKind ProviderKind = ApiProviderKind.Anthropic,
    string? AccountId = null,
    string? ApiVersion = null,
    IReadOnlyDictionary<string, string>? AdditionalHeaders = null);
