namespace ClawSharp.AgentHost.Contracts;

public sealed record ListProvidersResponse(
    IReadOnlyList<ProviderOptionDto> Providers);
