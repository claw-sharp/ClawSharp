using System.Net;
using System.Text;
using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class CodeSessionWorkerRegistrarTests
{
    [Fact]
    public async Task RegisterWorkerAsync_Fetches_Remote_Credentials_From_Sdk_Url()
    {
        HttpRequestMessage? capturedRequest = null;
        var httpClient = new HttpClient(new StubHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"worker_jwt":"jwt","api_base_url":"https://worker.example.com","expires_in":3600,"worker_epoch":"42"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }));

        var credentials = await CodeSessionWorkerRegistrar.RegisterWorkerAsync(
            new CodeSessionWorkerRegistrarDependencies(
                HttpClient: httpClient,
                GetAccessToken: () => "oauth-token",
                GetTrustedDeviceToken: () => "trusted-device"),
            "https://api.example.com/v1/code/sessions/cse_123");

        Assert.Equal("jwt", credentials.WorkerJwt);
        Assert.Equal("https://worker.example.com", credentials.ApiBaseUrl);
        Assert.Equal(42L, credentials.WorkerEpoch);
        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://api.example.com/v1/code/sessions/cse_123/bridge", capturedRequest.RequestUri!.ToString());
        Assert.Equal("trusted-device", capturedRequest.Headers.GetValues("X-Trusted-Device-Token").Single());
    }

    [Fact]
    public async Task RegisterWorkerAsync_Rejects_Invalid_Sdk_Url_And_Missing_Remote_Credentials()
    {
        var httpClient = new HttpClient(new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"error":{"message":"denied"}}""", Encoding.UTF8, "application/json")
            }));
        var dependencies = new CodeSessionWorkerRegistrarDependencies(
            HttpClient: httpClient,
            GetAccessToken: () => "oauth-token");

        var invalidUrlError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CodeSessionWorkerRegistrar.RegisterWorkerAsync(
                dependencies,
                "not-a-valid-sdk-url"));
        var registrationError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CodeSessionWorkerRegistrar.RegisterWorkerAsync(
                dependencies,
                "https://api.example.com/v1/code/sessions/cse_123"));

        Assert.Contains("Invalid code-session sdk URL", invalidUrlError.Message, StringComparison.Ordinal);
        Assert.Contains("Worker registration failed for code session cse_123.", registrationError.Message, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
