// TS origin: ./utils/plugins/pluginOptionsStorage.ts, ./utils/path.ts
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class PluginOptionServiceTests
{
    [Fact]
    public void SubstitutePluginVariables_Uses_Shared_ConfigKey_Path_Normalization()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "clawsharp-plugin-options", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var pluginRoot = Path.Combine(tempRoot, "plugins", "..", "plugin-root");
            Directory.CreateDirectory(Path.GetFullPath(pluginRoot));
            var service = new PluginOptionService(new InMemorySecureStorage(), Path.Combine(tempRoot, "plugin-state"));

            var result = service.SubstitutePluginVariables(
                "${CLAUDE_PLUGIN_ROOT}|${CLAUDE_PLUGIN_DATA}",
                pluginRoot,
                "reviewer@anthropic-tools");

            var parts = result.Split('|');
            Assert.Equal(PathUtilities.NormalizePathForConfigKey(pluginRoot), parts[0]);
            Assert.Equal(
                PathUtilities.NormalizePathForConfigKey(service.GetPluginDataDirectory("reviewer@anthropic-tools")),
                parts[1]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private sealed class InMemorySecureStorage : IMcpSecureStorage
    {
        private McpSecureStorageData? _data;

        public McpSecureStorageData? Read() => _data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_data);
        }

        public void Update(McpSecureStorageData data)
        {
            _data = data;
        }

        public bool Delete()
        {
            _data = null;
            return true;
        }
    }
}
