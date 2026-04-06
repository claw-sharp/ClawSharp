// TS origin: ./bridge/jwtUtils.ts
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClawSharp.Bridge;

public static class BridgeJwtUtilities
{
    private const string SessionIngressPrefix = "sk-ant-si-";

    public static JsonNode? DecodeJwtPayload(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        var jwt = token.StartsWith(SessionIngressPrefix, StringComparison.Ordinal)
            ? token[SessionIngressPrefix.Length..]
            : token;
        var parts = jwt.Split('.', StringSplitOptions.None);
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(parts[1]))
        {
            return null;
        }

        try
        {
            var decoded = DecodeBase64Url(parts[1]);
            return JsonNode.Parse(decoded);
        }
        catch
        {
            return null;
        }
    }

    public static long? DecodeJwtExpiry(string token)
    {
        var payload = DecodeJwtPayload(token);
        if (payload is JsonObject obj &&
            obj["exp"] is JsonValue expValue &&
            expValue.TryGetValue<long>(out var expiry))
        {
            return expiry;
        }

        return null;
    }

    private static string DecodeBase64Url(string value)
    {
        var padded = value
            .Replace('-', '+')
            .Replace('_', '/');

        var paddingLength = 4 - (padded.Length % 4);
        if (paddingLength is > 0 and < 4)
        {
            padded = padded.PadRight(padded.Length + paddingLength, '=');
        }

        var bytes = Convert.FromBase64String(padded);
        return Encoding.UTF8.GetString(bytes);
    }
}
