namespace ClawSharp.Core;

public sealed class AgentModelConnection
{
    public string BaseUrl { get; init; } = string.Empty;
    public string? ApiKey { get; init; }
    public string? AuthToken { get; init; }
    public string? AccountId { get; init; }
    public string? ApiVersion { get; init; }
    public string? Provider { get; init; }
    public bool UseExternalCredential { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
