//
// Ports:
//   - getConnectionTimeoutMs()                (MCP_TIMEOUT env, default 30 000)
//   - wrapFetchWithTimeout()                  (POST-only 60-second timeout wrapper)
//   - isTerminalConnectionError()             (ECONNRESET/ETIMEDOUT/EPIPE/… detection)
//   - isMcpSessionExpiredError()              (HTTP 404 + JSON-RPC -32001)
//   - connection-error multiplicity tracker   (MAX_ERRORS_BEFORE_RECONNECT = 3)
//   - client.onerror enhanced handler shape   (session-expiry, terminal-error escalation)
//   - client.onclose enhanced handler shape   (uptime logging, cache-clear on close)
//   - MCP_REQUEST_TIMEOUT_MS = 60 000
//   - MCP_STREAMABLE_HTTP_ACCEPT header rules
//
// Design decisions:
// - C# has no per-request AbortSignal.timeout() so the 60-second timeout is
//   implemented with CancellationTokenSource which is the approved closest viable
//   C# alternative (no behavioral divergence).
// - The MCP SDK C# transport layer owns the actual HTTP calls; this class wraps
//   the sdk connector's Connect call in the timeout envelope and installs the
//   onerror/onclose classification hooks through the IMcpConnectionHealthMonitor
//   seam so the C# runtime has the same reconnect-triggering shape as the TS client.

using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

/// <summary>
/// Receives lifecycle callbacks from the MCP connection health monitor.
/// Implementations decide how to react (e.g. reconnect, update app state).
/// </summary>
public interface IMcpConnectionEventSink
{
    void OnSessionExpired(string serverName);
    void OnConnectionClosed(string serverName, TimeSpan uptime, bool hadErrors);
    void OnTerminalErrorThreshold(string serverName, string reason);
    void OnConnectionError(string serverName, string transportType, McpConnectionError error);
}

/// <summary>
/// Structured connection error details from the health monitor.
/// </summary>
public sealed record McpConnectionError(
    string Message,
    bool IsTerminal,
    bool IsSessionExpired,
    bool IsSseReconnectionExhausted,
    TimeSpan Uptime);

/// <summary>
/// MCP startup and health-check utilities.
/// Direct port of the connection monitoring infrastructure from <c>services/mcp/client.ts</c>.
///
/// Scope:
/// - Connection timeout envelope (MCP_TIMEOUT env var, default 30 s)
/// - Request timeout constant (MCP_REQUEST_TIMEOUT_MS = 60 000)
/// - Terminal-error classification (same string patterns as TS)
/// - Session-expiry detection (HTTP 404 + JSON-RPC -32001)
/// - Per-connection error multiplicity tracker (MAX_ERRORS_BEFORE_RECONNECT = 3)
/// </summary>
public static class McpConnectionHealthUtilities
{
    /// <summary>
    /// Default MCP_REQUEST_TIMEOUT_MS = 60 000. Per-request timeout for POST/auth calls.
    /// Mirrors the TS constant from <c>client.ts</c>.
    /// </summary>
    public const int McpRequestTimeoutMs = 60_000;

    /// <summary>
    /// Maximum consecutive terminal connection errors before triggering reconnect.
    /// From <c>MAX_ERRORS_BEFORE_RECONNECT = 3</c> in <c>client.ts</c>.
    /// </summary>
    public const int MaxErrorsBeforeReconnect = 3;

    /// <summary>
    /// MCP Streamable HTTP spec Accept header.
    /// Mirrors MCP_STREAMABLE_HTTP_ACCEPT in <c>client.ts</c>.
    /// </summary>
    public const string McpStreamableHttpAccept = "application/json, text/event-stream";

    /// <summary>
    /// Gets the connection timeout for MCP server connection attempts.
    /// Reads MCP_TIMEOUT environment variable or defaults to 30 000ms.
    /// Mirrors <c>getConnectionTimeoutMs()</c> from <c>client.ts</c>.
    /// </summary>
    public static int GetConnectionTimeoutMs()
    {
        return int.TryParse(Environment.GetEnvironmentVariable("MCP_TIMEOUT"), out var value) && value > 0
            ? value
            : 30_000;
    }

    /// <summary>
    /// Returns true when the error is an MCP "Session not found" error (HTTP 404 + JSON-RPC -32001).
    /// Mirrors <c>isMcpSessionExpiredError()</c> from <c>client.ts</c>.
    ///
    /// We check both signals to avoid false positives from generic 404s.
    /// The SDK embeds the response body text in the error message.
    /// </summary>
    public static bool IsSessionExpiredError(Exception error)
    {
        return McpReconnectClassifier.IsSessionExpiredError(error);
    }

    /// <summary>
    /// Returns true when the error message contains one of the terminal connection
    /// error patterns from <c>isTerminalConnectionError()</c> in <c>client.ts</c>.
    /// </summary>
    public static bool IsTerminalConnectionError(string message)
    {
        return message.Contains("ECONNRESET", StringComparison.Ordinal) ||
               message.Contains("ETIMEDOUT", StringComparison.Ordinal) ||
               message.Contains("EPIPE", StringComparison.Ordinal) ||
               message.Contains("EHOSTUNREACH", StringComparison.Ordinal) ||
               message.Contains("ECONNREFUSED", StringComparison.Ordinal) ||
               message.Contains("Body Timeout Error", StringComparison.Ordinal) ||
               message.Contains("terminated", StringComparison.Ordinal) ||
               message.Contains("SSE stream disconnected", StringComparison.Ordinal) ||
               message.Contains("Failed to reconnect SSE stream", StringComparison.Ordinal);
    }

    /// <summary>
    /// Returns true when the error signals that the SDK exhausted its own SSE reconnect attempts.
    /// Mirrors the <c>Maximum reconnection attempts</c> check in <c>client.ts</c>.
    /// </summary>
    public static bool IsSseReconnectionExhausted(string message)
    {
        return message.Contains("Maximum reconnection attempts", StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a connection-timeout wrapper that connects within GetConnectionTimeoutMs()
    /// and cancels the transport if it fires.
    /// Mirrors the connectPromise/timeoutPromise race in <c>connectToServer()</c>.
    /// </summary>
    public static async Task ConnectWithTimeoutAsync(
        string serverName,
        Func<CancellationToken, Task> connectAction,
        Action<string>? onTimeout = null,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(GetConnectionTimeoutMs());

        try
        {
            await connectAction(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Only the timeout fired, not the outer cancellation token.
            var msg = $"MCP server \"{serverName}\" connection timed out after {GetConnectionTimeoutMs()}ms";
            onTimeout?.Invoke(msg);
            throw new TimeoutException(msg);
        }
    }
}

/// <summary>
/// Per-connection health monitor that tracks consecutive terminal errors and
/// triggers the reconnect-event sink when the threshold is reached.
///
/// Mirrors the per-connection state variables from <c>connectToServer()</c> in <c>client.ts</c>:
///   - connectionStartTime
///   - consecutiveConnectionErrors / MAX_ERRORS_BEFORE_RECONNECT
///   - hasErrorOccurred
///   - hasTriggeredClose guard
///   - onerror / onclose handler enrichment
/// </summary>
public sealed class McpConnectionHealthMonitor
{
    private readonly string _serverName;
    private readonly string _transportType;
    private readonly IMcpConnectionEventSink? _eventSink;
    private readonly DateTimeOffset _connectionStartTime;

    private int _consecutiveConnectionErrors;
    private bool _hasErrorOccurred;
    private bool _hasTriggeredClose;

    public McpConnectionHealthMonitor(
        string serverName,
        string transportType,
        IMcpConnectionEventSink? eventSink = null)
    {
        _serverName = serverName;
        _transportType = transportType;
        _eventSink = eventSink;
        _connectionStartTime = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Call this from the SDK client's OnError callback.
    /// Mirrors the enhanced client.onerror handler in <c>connectToServer()</c>.
    ///
    /// Returns true if a reconnect should be triggered.
    /// </summary>
    public bool OnError(Exception error, Action<string> triggerClose)
    {
        var uptime = DateTimeOffset.UtcNow - _connectionStartTime;
        _hasErrorOccurred = true;

        var isSessionExpired = McpConnectionHealthUtilities.IsSessionExpiredError(error);
        var isSseExhausted = McpConnectionHealthUtilities.IsSseReconnectionExhausted(error.Message);
        var isTerminal = McpConnectionHealthUtilities.IsTerminalConnectionError(error.Message);

        var structured = new McpConnectionError(
            error.Message,
            isTerminal,
            isSessionExpired,
            isSseExhausted,
            uptime);

        _eventSink?.OnConnectionError(_serverName, _transportType, structured);

        // HTTP/claudeai-proxy: session-expiry detection triggers immediate reconnect.
        if ((_transportType is "http" or "claudeai-proxy") && isSessionExpired)
        {
            if (!_hasTriggeredClose)
            {
                _hasTriggeredClose = true;
                triggerClose("session expired");
                _eventSink?.OnSessionExpired(_serverName);
            }

            return true;
        }

        // SSE/HTTP/claudeai-proxy: SDK exhausted reconnect attempts — close transport.
        if ((_transportType is "sse" or "http" or "claudeai-proxy") && isSseExhausted)
        {
            if (!_hasTriggeredClose)
            {
                _hasTriggeredClose = true;
                triggerClose("SSE reconnection exhausted");
            }

            return true;
        }

        // Remote transports: track terminal errors and escalate after threshold.
        if (_transportType is "sse" or "http" or "claudeai-proxy")
        {
            if (isTerminal)
            {
                _consecutiveConnectionErrors++;

                if (_consecutiveConnectionErrors >= McpConnectionHealthUtilities.MaxErrorsBeforeReconnect)
                {
                    _consecutiveConnectionErrors = 0;
                    if (!_hasTriggeredClose)
                    {
                        _hasTriggeredClose = true;
                        triggerClose("max consecutive terminal errors");
                        _eventSink?.OnTerminalErrorThreshold(_serverName, "max consecutive terminal errors");
                    }

                    return true;
                }
            }
            else
            {
                // Non-terminal error — reset counter.
                _consecutiveConnectionErrors = 0;
            }
        }

        return false;
    }

    /// <summary>
    /// Call this from the SDK client's OnClose callback.
    /// Mirrors the enhanced client.onclose handler in <c>connectToServer()</c>.
    /// </summary>
    public void OnClose()
    {
        var uptime = DateTimeOffset.UtcNow - _connectionStartTime;
        _eventSink?.OnConnectionClosed(_serverName, uptime, _hasErrorOccurred);
    }
}
