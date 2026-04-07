// TS parity status: ports the typed API-error boundary used by the TypeScript model retry loop so HTTP status, headers, and response-body retry signals stay available to the C# transport executor.
using System.Net;

namespace ClawSharp.Query;

public class QueryModelApiException : Exception
{
    public QueryModelApiException(
        HttpStatusCode statusCode,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string? responseBody = null)
        : base(CreateMessage(statusCode, responseBody))
    {
        StatusCode = statusCode;
        Headers = headers;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }

    public string? ResponseBody { get; }

    public string? GetHeaderValue(string name)
    {
        return Headers.TryGetValue(name, out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static string CreateMessage(HttpStatusCode statusCode, string? responseBody)
    {
        return string.IsNullOrWhiteSpace(responseBody)
            ? $"Query model request failed with API response ({(int)statusCode})."
            : $"Query model request failed with API response ({(int)statusCode}): {responseBody}";
    }
}
