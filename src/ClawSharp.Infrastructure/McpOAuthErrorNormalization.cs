// TS origin: ./services/mcp/auth.ts
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClawSharp.Infrastructure;

public static class McpOAuthErrorNormalization
{
    private static readonly HashSet<string> NonstandardInvalidGrantAliases = new(StringComparer.Ordinal)
    {
        "invalid_refresh_token",
        "expired_refresh_token",
        "token_expired"
    };

    public static McpOAuthNormalizedResponse NormalizeSuccessResponseBody(int statusCode, string responseBody)
    {
        if (statusCode < 200 || statusCode >= 300)
        {
            return new McpOAuthNormalizedResponse(statusCode, responseBody);
        }

        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(responseBody);
        }
        catch (JsonException)
        {
            return new McpOAuthNormalizedResponse(statusCode, responseBody);
        }

        if (parsed is not JsonObject jsonObject)
        {
            return new McpOAuthNormalizedResponse(statusCode, responseBody);
        }

        if (LooksLikeOAuthTokens(jsonObject))
        {
            return new McpOAuthNormalizedResponse(statusCode, responseBody);
        }

        if (!jsonObject.TryGetPropertyValue("error", out var errorNode) ||
            errorNode is null ||
            errorNode.GetValueKind() != JsonValueKind.String)
        {
            return new McpOAuthNormalizedResponse(statusCode, responseBody);
        }

        var error = errorNode.GetValue<string>();
        var normalizedObject = NonstandardInvalidGrantAliases.Contains(error)
            ? new JsonObject
            {
                ["error"] = "invalid_grant",
                ["error_description"] = jsonObject["error_description"]?.GetValue<string>() ??
                                        $"Server returned non-standard error code: {error}"
            }
            : jsonObject;

        return new McpOAuthNormalizedResponse(400, normalizedObject.ToJsonString());
    }

    private static bool LooksLikeOAuthTokens(JsonObject jsonObject)
    {
        return jsonObject.ContainsKey("access_token") ||
               jsonObject.ContainsKey("refresh_token") ||
               jsonObject.ContainsKey("expires_in") ||
               jsonObject.ContainsKey("token_type");
    }
}

public sealed record McpOAuthNormalizedResponse(int StatusCode, string Body);
