using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpLoggingSafeUrlTests
{
    [Fact]
    public void GetLoggingSafeMcpBaseUrl_StripsQueryAndTrailingSlash_ForHttp()
    {
        var config = new McpHttpServerConfig(
            "https://example.test/api/?token=secret&x=1",
            null,
            null,
            null);

        var result = McpLoggingSafeUrl.GetLoggingSafeMcpBaseUrl(config);

        Assert.Equal("https://example.test/api", result);
    }

    [Fact]
    public void GetLoggingSafeMcpBaseUrl_StripsQueryAndTrailingSlash_ForSse()
    {
        var config = new McpSseServerConfig(
            "https://example.test/sse/?token=secret",
            null,
            null,
            null);

        var result = McpLoggingSafeUrl.GetLoggingSafeMcpBaseUrl(config);

        Assert.Equal("https://example.test/sse", result);
    }

    [Fact]
    public void GetLoggingSafeMcpBaseUrl_ReturnsNull_ForNonUrlConfigs()
    {
        var config = new McpStdioServerConfig("node", [], null);

        var result = McpLoggingSafeUrl.GetLoggingSafeMcpBaseUrl(config);

        Assert.Null(result);
    }

    [Fact]
    public void GetLoggingSafeMcpBaseUrl_ReturnsNull_ForInvalidUrls()
    {
        var config = new McpHttpServerConfig(
            "not a valid url",
            null,
            null,
            null);

        var result = McpLoggingSafeUrl.GetLoggingSafeMcpBaseUrl(config);

        Assert.Null(result);
    }
}
