// TS parity status: ports the overloaded-response detection contract used by the TypeScript retry and fallback path; broader retry classification remains intentionally scoped to the currently ported 529/overloaded branch.
using System.Net;

namespace ClawSharp.Query;

public sealed class QueryModelOverloadedException : QueryModelApiException
{
    public QueryModelOverloadedException(
        HttpStatusCode statusCode,
        IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        string? responseBody = null)
        : base(statusCode, headers, responseBody)
    {
    }

    public static bool IsOverloaded(HttpStatusCode statusCode, string? responseBody)
    {
        return statusCode == (HttpStatusCode)529 ||
               (!string.IsNullOrWhiteSpace(responseBody) &&
                responseBody.Contains("\"type\":\"overloaded_error\"", StringComparison.Ordinal));
    }
}
