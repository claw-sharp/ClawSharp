using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpClientSecretServiceTests
{
    [Fact]
    public async Task ReadAsync_ReturnsEnvironmentValueFirst()
    {
        var original = Environment.GetEnvironmentVariable("MCP_CLIENT_SECRET");
        Environment.SetEnvironmentVariable("MCP_CLIENT_SECRET", "env-secret");

        try
        {
            var service = new McpClientSecretService(new StringReader(string.Empty), new StringWriter(), () => false);

            var secret = await service.ReadAsync();

            Assert.Equal("env-secret", secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_CLIENT_SECRET", original);
        }
    }

    [Fact]
    public async Task ReadAsync_ThrowsWhenInputIsNotInteractiveAndNoEnvironmentSecretExists()
    {
        var original = Environment.GetEnvironmentVariable("MCP_CLIENT_SECRET");
        Environment.SetEnvironmentVariable("MCP_CLIENT_SECRET", null);

        try
        {
            var service = new McpClientSecretService(new StringReader(string.Empty), new StringWriter(), () => false);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ReadAsync());

            Assert.Equal("No TTY available to prompt for client secret. Set MCP_CLIENT_SECRET env var instead.", exception.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_CLIENT_SECRET", original);
        }
    }

    [Fact]
    public async Task ReadAsync_ReadsFromInteractiveInput()
    {
        var original = Environment.GetEnvironmentVariable("MCP_CLIENT_SECRET");
        Environment.SetEnvironmentVariable("MCP_CLIENT_SECRET", null);

        try
        {
            using var input = new StringReader("typed-secret" + Environment.NewLine);
            using var error = new StringWriter();
            var service = new McpClientSecretService(input, error, () => true);

            var secret = await service.ReadAsync();

            Assert.Equal("typed-secret", secret);
            Assert.Contains("Enter OAuth client secret: ", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_CLIENT_SECRET", original);
        }
    }
}
