// TS origin: ./services/mcp/auth.ts, ./services/mcp/oauthPort.ts
using System.Diagnostics;
using System.Net;
using System.Text;
using ModelContextProtocol.Authentication;

namespace ClawSharp.Infrastructure;

public sealed class McpSdkAuthorizationRedirectHandler
{
    private const string SuccessResponseBody = """
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>Authentication complete</title></head>
<body>You can return to ClawSharp.</body>
</html>
""";

    private const string FailureResponseBody = """
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><title>Authentication failed</title></head>
<body>Authentication callback did not include a code.</body>
</html>
""";

    private readonly Action<string>? _onAuthorizationUrl;
    private readonly Func<Uri, CancellationToken, Task> _openBrowserAsync;
    private readonly bool _skipBrowserOpen;

    public McpSdkAuthorizationRedirectHandler(
        Action<string>? onAuthorizationUrl = null,
        Func<Uri, CancellationToken, Task>? openBrowserAsync = null,
        bool skipBrowserOpen = false)
    {
        _onAuthorizationUrl = onAuthorizationUrl;
        _openBrowserAsync = openBrowserAsync ?? OpenBrowserAsync;
        _skipBrowserOpen = skipBrowserOpen;
    }

    public AuthorizationRedirectDelegate CreateDelegate()
    {
        return HandleAsync;
    }

    public async Task<string?> HandleAsync(
        Uri authorizationUri,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorizationUri);
        ArgumentNullException.ThrowIfNull(redirectUri);

        _onAuthorizationUrl?.Invoke(authorizationUri.AbsoluteUri);

        if (!_skipBrowserOpen)
        {
            await _openBrowserAsync(authorizationUri, cancellationToken).ConfigureAwait(false);
        }

        var prefix = BuildHttpListenerPrefix(redirectUri);
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        using var registration = cancellationToken.Register(static state =>
        {
            var activeListener = (HttpListener)state!;
            if (activeListener.IsListening)
            {
                activeListener.Stop();
            }
        }, listener);

        try
        {
            var context = await listener.GetContextAsync().ConfigureAwait(false);
            var code = context.Request.QueryString["code"];
            await WriteResponseAsync(
                context.Response,
                string.IsNullOrWhiteSpace(code) ? FailureResponseBody : SuccessResponseBody,
                string.IsNullOrWhiteSpace(code) ? HttpStatusCode.BadRequest : HttpStatusCode.OK,
                cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(code) ? null : code;
        }
        catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string BuildHttpListenerPrefix(Uri redirectUri)
    {
        var path = redirectUri.AbsolutePath.Trim('/');
        return $"{redirectUri.Scheme}://{redirectUri.Host}:{redirectUri.Port}/{path}/";
    }

    private static async Task WriteResponseAsync(
        HttpListenerResponse response,
        string body,
        HttpStatusCode statusCode,
        CancellationToken cancellationToken)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "text/html; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        response.Close();
    }

    private static Task OpenBrowserAsync(Uri uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }
}
