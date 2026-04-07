using System.Reflection;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

internal static class McpReconnectClassifier
{
    public static bool IsSessionExpiredError(Exception error)
    {
        var code = TryGetErrorCode(error);
        if (code != 404)
        {
            return false;
        }

        return error.Message.Contains("\"code\":-32001", StringComparison.Ordinal) ||
               error.Message.Contains("\"code\": -32001", StringComparison.Ordinal);
    }

    public static bool IsConnectionClosedOnHttp(Exception error, ScopedMcpServerConfig config)
    {
        if (config.Type is not "http" and not "claudeai-proxy")
        {
            return false;
        }

        var code = TryGetErrorCode(error);
        return code == -32000 &&
               error.Message.Contains("Connection closed", StringComparison.Ordinal);
    }

    private static int? TryGetErrorCode(Exception error)
    {
        if (error.Data.Contains("code"))
        {
            var value = error.Data["code"];
            if (value is int intValue)
            {
                return intValue;
            }

            if (value is long longValue && longValue is >= int.MinValue and <= int.MaxValue)
            {
                return (int)longValue;
            }

            if (value is string stringValue && int.TryParse(stringValue, out var parsed))
            {
                return parsed;
            }
        }

        var property = error.GetType().GetProperty("Code", BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
        {
            return null;
        }

        var propertyValue = property.GetValue(error);
        return propertyValue switch
        {
            int intValue => intValue,
            long longValue when longValue is >= int.MinValue and <= int.MaxValue => (int)longValue,
            string stringValue when int.TryParse(stringValue, out var parsed) => parsed,
            _ => null
        };
    }
}
