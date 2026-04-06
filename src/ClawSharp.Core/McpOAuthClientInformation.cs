// TS origin: ./services/mcp/auth.ts
namespace ClawSharp.Core;

public sealed record McpOAuthClientInformation(
    string ClientId,
    string? ClientSecret = null);
