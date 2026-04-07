using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ClawSharp.Infrastructure;

public static class McpOAuthPort
{
    public const int RedirectPortFallback = 3118;

    public static string BuildRedirectUri(int port = RedirectPortFallback)
    {
        return $"http://localhost:{port}/callback";
    }

    public static Task<int> FindAvailablePortAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var configuredPort = GetConfiguredCallbackPort();
        if (configuredPort.HasValue)
        {
            return Task.FromResult(configuredPort.Value);
        }

        var (min, max) = GetRedirectPortRange();
        var range = max - min + 1;
        var maxAttempts = Math.Min(range, 100);

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var port = min + Random.Shared.Next(range);
            if (CanBind(port))
            {
                return Task.FromResult(port);
            }
        }

        if (CanBind(RedirectPortFallback))
        {
            return Task.FromResult(RedirectPortFallback);
        }

        throw new InvalidOperationException("No available ports for OAuth redirect");
    }

    private static int? GetConfiguredCallbackPort()
    {
        var raw = Environment.GetEnvironmentVariable("MCP_OAUTH_CALLBACK_PORT");
        return int.TryParse(raw, out var port) && port > 0 ? port : null;
    }

    private static (int Min, int Max) GetRedirectPortRange()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? (39152, 49151)
            : (49152, 65535);
    }

    private static bool CanBind(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
}
