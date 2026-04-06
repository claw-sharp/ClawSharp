// TS parity status: ports the direct C#-equivalent transport retry subset from the TypeScript query model retry loop, including subscriber-gated 429 and `x-should-retry:true` behavior plus the unattended persistent-capacity retry backoff path.
using System.Net;

namespace ClawSharp.Query;

public static class QueryModelTransportRetryPolicy
{
    private const int DefaultMaxRetries = 10;
    private const int BaseDelayMs = 500;
    private const int MaxDelayMs = 32000;
    private const int PersistentMaxBackoffMs = 5 * 60 * 1000;
    private const int PersistentResetCapMs = 6 * 60 * 60 * 1000;

    public static int GetMaxRetries()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CODE_MAX_RETRIES");
        return int.TryParse(configured, out var parsed) ? parsed : DefaultMaxRetries;
    }

    public static bool IsPersistentRetryEnabled()
    {
        return IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_UNATTENDED_RETRY"));
    }

    public static bool ShouldRetry(QueryModelApiException exception, QueryAuthAccountState accountState)
    {
        if (IsPersistentRetryEnabled() && IsTransientCapacityError(exception))
        {
            return true;
        }

        if (IsRemoteModeEnabled() &&
            (exception.StatusCode == HttpStatusCode.Unauthorized || exception.StatusCode == HttpStatusCode.Forbidden))
        {
            return true;
        }

        var shouldRetryHeader = exception.GetHeaderValue("x-should-retry");
        if (string.Equals(shouldRetryHeader, "true", StringComparison.OrdinalIgnoreCase))
        {
            return !accountState.IsClaudeAiSubscriber || accountState.IsEnterpriseSubscriber;
        }

        if (string.Equals(shouldRetryHeader, "false", StringComparison.OrdinalIgnoreCase))
        {
            return IsAntUser() && (int)exception.StatusCode >= 500;
        }

        if (exception.StatusCode == HttpStatusCode.RequestTimeout ||
            exception.StatusCode == HttpStatusCode.Conflict ||
            exception.StatusCode == HttpStatusCode.Unauthorized ||
            IsOAuthTokenRevokedError(exception) ||
            (int)exception.StatusCode >= 500)
        {
            return true;
        }

        if (exception.StatusCode == (HttpStatusCode)429)
        {
            return !accountState.IsClaudeAiSubscriber || accountState.IsEnterpriseSubscriber;
        }

        return false;
    }

    public static bool ShouldRetry(HttpRequestException exception)
    {
        return exception.HttpRequestError is not HttpRequestError.NameResolutionError;
    }

    public static bool IsTransientCapacityError(QueryModelApiException exception)
    {
        return exception is QueryModelOverloadedException ||
               exception.StatusCode == (HttpStatusCode)429;
    }

    public static TimeSpan GetRetryDelay(int attempt, string? retryAfterHeader, int maxDelayMs = MaxDelayMs)
    {
        if (int.TryParse(retryAfterHeader, out var retryAfterSeconds))
        {
            return TimeSpan.FromSeconds(retryAfterSeconds);
        }

        var baseDelay = Math.Min(BaseDelayMs * Math.Pow(2, attempt - 1), maxDelayMs);
        var jitter = Random.Shared.NextDouble() * 0.25 * baseDelay;
        return TimeSpan.FromMilliseconds(baseDelay + jitter);
    }

    public static TimeSpan GetPersistentRetryDelay(int persistentAttempt, QueryModelApiException exception)
    {
        if (exception.StatusCode == (HttpStatusCode)429)
        {
            var resetDelay = GetRateLimitResetDelay(exception.GetHeaderValue("anthropic-ratelimit-unified-reset"));
            if (resetDelay is not null)
            {
                return resetDelay.Value;
            }
        }

        var delay = GetRetryDelay(
            persistentAttempt,
            exception.GetHeaderValue("retry-after"),
            PersistentMaxBackoffMs);
        return delay > TimeSpan.FromMilliseconds(PersistentResetCapMs)
            ? TimeSpan.FromMilliseconds(PersistentResetCapMs)
            : delay;
    }

    private static TimeSpan? GetRateLimitResetDelay(string? resetHeader)
    {
        if (!long.TryParse(resetHeader, out var resetUnixSeconds))
        {
            return null;
        }

        var delay = DateTimeOffset.FromUnixTimeSeconds(resetUnixSeconds) - DateTimeOffset.UtcNow;
        if (delay <= TimeSpan.Zero)
        {
            return null;
        }

        var max = TimeSpan.FromMilliseconds(PersistentResetCapMs);
        return delay > max ? max : delay;
    }

    private static bool IsAntUser()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("USER_TYPE"),
            "ant",
            StringComparison.Ordinal);
    }

    private static bool IsRemoteModeEnabled()
    {
        return IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_REMOTE"));
    }

    public static bool IsOAuthTokenRevokedError(QueryModelApiException exception)
    {
        return exception.StatusCode == HttpStatusCode.Forbidden &&
               ((exception.ResponseBody?.Contains("OAuth token has been revoked", StringComparison.Ordinal) ?? false) ||
                exception.Message.Contains("OAuth token has been revoked", StringComparison.Ordinal));
    }

    private static bool IsTruthy(string? value)
    {
        return string.Equals(value, "1", StringComparison.Ordinal) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);
    }
}
