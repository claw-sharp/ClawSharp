using System.Net;
using System.Net.Sockets;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpOAuthPortTests
{
    [Fact]
    public void BuildRedirectUri_UsesFallbackPortByDefault()
    {
        Assert.Equal("http://localhost:3118/callback", McpOAuthPort.BuildRedirectUri());
    }

    [Fact]
    public void BuildRedirectUri_UsesProvidedPort()
    {
        Assert.Equal("http://localhost:4567/callback", McpOAuthPort.BuildRedirectUri(4567));
    }

    [Fact]
    public async Task FindAvailablePortAsync_ReturnsConfiguredPortWhenSet()
    {
        var original = Environment.GetEnvironmentVariable("MCP_OAUTH_CALLBACK_PORT");
        Environment.SetEnvironmentVariable("MCP_OAUTH_CALLBACK_PORT", "45678");

        try
        {
            Assert.Equal(45678, await McpOAuthPort.FindAvailablePortAsync());
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_OAUTH_CALLBACK_PORT", original);
        }
    }

    [Fact]
    public async Task FindAvailablePortAsync_ReturnsBindablePortWhenEnvUnset()
    {
        var original = Environment.GetEnvironmentVariable("MCP_OAUTH_CALLBACK_PORT");
        Environment.SetEnvironmentVariable("MCP_OAUTH_CALLBACK_PORT", null);

        try
        {
            var port = await McpOAuthPort.FindAvailablePortAsync();

            Assert.True(port > 0);

            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_OAUTH_CALLBACK_PORT", original);
        }
    }
}
