namespace ClawSharp.Core;

public sealed record McpOAuthTokens(
    string AccessToken,
    string? RefreshToken,
    double? ExpiresIn,
    string? Scope,
    string TokenType = "Bearer");
