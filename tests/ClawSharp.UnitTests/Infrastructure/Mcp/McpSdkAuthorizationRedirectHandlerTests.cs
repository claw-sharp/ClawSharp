using System.Net;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpSdkAuthorizationRedirectHandlerTests
{
    [Fact]
    public async Task HandleAsync_NotifiesAuthorizationUrlAndReturnsCallbackCode()
    {
        string? notifiedUrl = null;
        Uri? openedUrl = null;
        var handler = new McpSdkAuthorizationRedirectHandler(
            onAuthorizationUrl: url => notifiedUrl = url,
            openBrowserAsync: (uri, cancellationToken) =>
            {
                openedUrl = uri;
                return Task.CompletedTask;
            },
            skipBrowserOpen: false);

        var redirectPort = await McpOAuthPort.FindAvailablePortAsync();
        var redirectUri = new Uri(McpOAuthPort.BuildRedirectUri(redirectPort), UriKind.Absolute);
        var authorizationUri = new Uri("https://auth.example.test/authorize?client_id=client");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var handleTask = handler.HandleAsync(authorizationUri, redirectUri, timeout.Token);

        using var client = new HttpClient();
        using var response = await SendWithRetryAsync(
            client,
            new UriBuilder(redirectUri) { Query = "code=test-code" }.Uri,
            timeout.Token);
        var code = await handleTask;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(authorizationUri.AbsoluteUri, notifiedUrl);
        Assert.Equal(authorizationUri, openedUrl);
        Assert.Equal("test-code", code);
    }

    private static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Uri requestUri,
        CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException error)
            {
                lastError = error;
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("Failed to reach local OAuth callback listener.", lastError);
    }
}
