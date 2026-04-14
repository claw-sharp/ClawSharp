using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class PluginMcpServerResolverTests
{
    [Fact]
    public void Resolve_Substitutes_UserConfig_Into_Bundled_Mcp_Server()
    {
        var resolver = new PluginMcpServerResolver(new InMemorySecureStorage());
        var plugin = new DiscoveredPlugin(
            "linear@builtin",
            "linear",
            "/builtin/linear",
            Enabled: true,
            IsBundled: true,
            Manifest: new PluginManifest(
                "linear",
                "Linear MCP",
                "1.0.0",
                [],
                [],
                [],
                [],
                [],
                [
                    new PluginMcpServerDefinition(
                        "linear",
                        new McpHttpServerConfig("https://${user_config.host}/mcp", null, null, null))
                ],
                UserConfig: new Dictionary<string, PluginOptionDefinition>(StringComparer.Ordinal)
                {
                    ["host"] = new(PluginOptionType.String, "Host", "Host name", Required: true)
                }),
            ValidationIssues: [],
            Hooks: new Dictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>());
        var settings = new ClawSharpSettings
        {
            PluginConfigs = new Dictionary<string, PluginConfigSettings>(StringComparer.Ordinal)
            {
                ["linear@builtin"] = new()
                {
                    Options = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["host"] = "mcp.linear.app"
                    }
                }
            }
        };

        var (servers, errors) = resolver.Resolve([plugin], settings);

        Assert.Empty(errors);
        var resolved = Assert.Single(servers);
        Assert.Equal("linear", resolved.Key);
        Assert.Equal("linear@builtin", resolved.Value.PluginSource);
        Assert.Equal("https://mcp.linear.app/mcp", Assert.IsType<McpHttpServerConfig>(resolved.Value.Config).Url);
    }

    private sealed class InMemorySecureStorage : IMcpSecureStorage
    {
        private McpSecureStorageData? _data;

        public McpSecureStorageData? Read() => _data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(_data);

        public void Update(McpSecureStorageData data) => _data = data;

        public bool Delete()
        {
            _data = null;
            return true;
        }
    }
}
