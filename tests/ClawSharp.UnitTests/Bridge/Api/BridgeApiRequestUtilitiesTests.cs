using ClawSharp.Bridge;

namespace ClawSharp.UnitTests;

public sealed class BridgeApiRequestUtilitiesTests
{
    [Fact]
    public void GetHeaders_Includes_Beta_Runner_And_Optional_Trusted_Device_Token()
    {
        var dependencies = new BridgeApiClientDependencies(
            BaseUrl: "https://api.example.com",
            GetAccessToken: () => "token",
            RunnerVersion: "1.2.3",
            GetTrustedDeviceToken: () => "device-token");

        var headers = BridgeApiRequestUtilities.GetHeaders(dependencies, "token");

        Assert.Equal("Bearer token", headers["Authorization"]);
        Assert.Equal("application/json", headers["Content-Type"]);
        Assert.Equal("2023-06-01", headers["anthropic-version"]);
        Assert.Equal(BridgeApiRequestUtilities.BetaHeader, headers["anthropic-beta"]);
        Assert.Equal("1.2.3", headers["x-environment-runner-version"]);
        Assert.Equal("device-token", headers["X-Trusted-Device-Token"]);
    }

    [Fact]
    public void ResolveAuth_Throws_Bridge_Login_Instruction_When_No_Token_Exists()
    {
        var dependencies = new BridgeApiClientDependencies(
            BaseUrl: "https://api.example.com",
            GetAccessToken: () => null,
            RunnerVersion: "1.2.3");

        var error = Assert.Throws<InvalidOperationException>(() =>
            BridgeApiRequestUtilities.ResolveAuth(dependencies));

        Assert.Equal(BridgeConstants.BridgeLoginInstruction, error.Message);
    }

    [Fact]
    public async Task WithOAuthRetryAsync_Returns_Immediate_Response_For_Non401()
    {
        var dependencies = new BridgeApiClientDependencies(
            BaseUrl: "https://api.example.com",
            GetAccessToken: () => "token",
            RunnerVersion: "1.2.3");
        var calls = 0;

        var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
            dependencies,
            _ =>
            {
                calls++;
                return Task.FromResult(new BridgeHttpLikeResponse<string>(200, "ok"));
            },
            "Poll");

        Assert.Equal(1, calls);
        Assert.Equal(200, response.Status);
        Assert.Equal("ok", response.Data);
    }

    [Fact]
    public async Task WithOAuthRetryAsync_Returns_Original_401_When_No_Refresh_Handler_Exists()
    {
        var debugMessages = new List<string>();
        var dependencies = new BridgeApiClientDependencies(
            BaseUrl: "https://api.example.com",
            GetAccessToken: () => "token",
            RunnerVersion: "1.2.3",
            OnDebug: debugMessages.Add);

        var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
            dependencies,
            _ => Task.FromResult(new BridgeHttpLikeResponse<string>(401, "unauthorized")),
            "Registration");

        Assert.Equal(401, response.Status);
        Assert.Single(debugMessages);
        Assert.Contains("401 received, no refresh handler", debugMessages[0]);
    }

    [Fact]
    public async Task WithOAuthRetryAsync_Retries_Once_After_Successful_Refresh()
    {
        var debugMessages = new List<string>();
        var currentToken = "stale-token";
        var dependencies = new BridgeApiClientDependencies(
            BaseUrl: "https://api.example.com",
            GetAccessToken: () => currentToken,
            RunnerVersion: "1.2.3",
            OnDebug: debugMessages.Add,
            OnAuth401: staleToken =>
            {
                Assert.Equal("stale-token", staleToken);
                currentToken = "fresh-token";
                return Task.FromResult(true);
            });

        var seenTokens = new List<string>();
        var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
            dependencies,
            token =>
            {
                seenTokens.Add(token);
                return Task.FromResult(
                    seenTokens.Count == 1
                        ? new BridgeHttpLikeResponse<string>(401, "unauthorized")
                        : new BridgeHttpLikeResponse<string>(200, "ok"));
            },
            "ReconnectSession");

        Assert.Equal(["stale-token", "fresh-token"], seenTokens);
        Assert.Equal(200, response.Status);
        Assert.Equal("ok", response.Data);
        Assert.Contains(debugMessages, message => message.Contains("attempting token refresh", StringComparison.Ordinal));
        Assert.Contains(debugMessages, message => message.Contains("Token refreshed, retrying request", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithOAuthRetryAsync_Returns_Original_401_When_Retry_Is_Still_401()
    {
        var currentToken = "stale-token";
        var dependencies = new BridgeApiClientDependencies(
            BaseUrl: "https://api.example.com",
            GetAccessToken: () => currentToken,
            RunnerVersion: "1.2.3",
            OnAuth401: _ =>
            {
                currentToken = "fresh-token";
                return Task.FromResult(true);
            });

        var seenTokens = new List<string>();
        var response = await BridgeApiRequestUtilities.WithOAuthRetryAsync(
            dependencies,
            token =>
            {
                seenTokens.Add(token);
                return Task.FromResult(new BridgeHttpLikeResponse<string>(401, token));
            },
            "ArchiveSession");

        Assert.Equal(["stale-token", "fresh-token"], seenTokens);
        Assert.Equal(401, response.Status);
        Assert.Equal("stale-token", response.Data);
    }
}
