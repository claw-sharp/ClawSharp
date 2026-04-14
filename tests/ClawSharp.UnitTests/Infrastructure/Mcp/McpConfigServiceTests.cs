using System.Text;
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class McpConfigServiceTests
{
    [Fact]
    public void ParseConfig_ExpandsEnvironmentVariables_AndReportsMissingVariables()
    {
        var service = new McpConfigService(Path.GetTempPath());
        Environment.SetEnvironmentVariable("CLAWSHARP_MCP_TEST_VAR", "expanded-value");

        try
        {
            var payload = new
            {
                mcpServers = new Dictionary<string, object?>
                {
                    ["stdio-server"] = new
                    {
                        command = "${CLAWSHARP_MCP_TEST_VAR}",
                        args = new[] { "${MISSING_VAR:-fallback}", "${MISSING_VAR_2}" }
                    }
                }
            };

            var (config, errors) = service.ParseConfig(payload, expandVars: true, McpConfigScope.User);

            var server = Assert.IsType<McpStdioServerConfig>(Assert.Single(config!.McpServers.Values));
            Assert.Equal("expanded-value", server.Command);
            Assert.Equal(["fallback", "${MISSING_VAR_2}"], server.Args);

            var error = Assert.Single(errors);
            Assert.Equal(McpConfigErrorSeverity.Warning, error.Metadata.Severity);
            Assert.Equal("stdio-server", error.Metadata.ServerName);
            Assert.Contains("MISSING_VAR_2", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_MCP_TEST_VAR", null);
        }
    }

    [Fact]
    public void ParseConfigFromFilePath_InvalidJson_ReturnsFatalError()
    {
        var tempDir = CreateTempDirectory();
        var filePath = Path.Combine(tempDir, ".mcp.json");
        File.WriteAllText(filePath, "{ invalid json", Encoding.UTF8);
        var service = new McpConfigService(tempDir);

        var (config, errors) = service.ParseConfigFromFilePath(filePath, expandVars: true, McpConfigScope.Project);

        Assert.Null(config);
        var error = Assert.Single(errors);
        Assert.Equal(McpConfigErrorSeverity.Fatal, error.Metadata.Severity);
        Assert.Equal("MCP config is not a valid JSON", error.Message);
    }

    [Fact]
    public void GetConfigsByScope_Project_MergesParentAndChildMcpJsonFiles()
    {
        var rootDir = CreateTempDirectory();
        var childDir = Path.Combine(rootDir, "child");
        Directory.CreateDirectory(childDir);

        File.WriteAllText(
            Path.Combine(rootDir, ".mcp.json"),
            """
            {
              "mcpServers": {
                "shared": { "command": "parent-command" },
                "parentOnly": { "command": "parent-only" }
              }
            }
            """,
            Encoding.UTF8);

        File.WriteAllText(
            Path.Combine(childDir, ".mcp.json"),
            """
            {
              "mcpServers": {
                "shared": { "command": "child-command" },
                "childOnly": { "command": "child-only" }
              }
            }
            """,
            Encoding.UTF8);

        var service = new McpConfigService(childDir);
        var (servers, errors) = service.GetConfigsByScope(McpConfigScope.Project);

        Assert.Empty(errors);
        Assert.Equal(3, servers.Count);
        Assert.Equal("child-command", Assert.IsType<McpStdioServerConfig>(servers["shared"].Config).Command);
        Assert.Equal("parent-only", Assert.IsType<McpStdioServerConfig>(servers["parentOnly"].Config).Command);
        Assert.Equal("child-only", Assert.IsType<McpStdioServerConfig>(servers["childOnly"].Config).Command);
    }

    [Fact]
    public void GetConfigByName_UsesEnterpriseThenLocalThenProjectThenUserPrecedence()
    {
        var workspaceRoot = CreateTempDirectory();
        var globalConfigPath = Path.Combine(workspaceRoot, ".clawsharp.json");
        var enterprisePath = Path.Combine(workspaceRoot, "managed-mcp.json");

        File.WriteAllText(
            Path.Combine(workspaceRoot, ".mcp.json"),
            """
            {
              "mcpServers": {
                "precedence": { "command": "project-command" },
                "projectOnly": { "command": "project-only" }
              }
            }
            """,
            Encoding.UTF8);

        var projectKey = ClaudeConfigPaths.NormalizeProjectPathForConfigKey(workspaceRoot);
        File.WriteAllText(
            globalConfigPath,
            $$"""
            {
              "mcpServers": {
                "precedence": { "command": "user-command" },
                "userOnly": { "command": "user-only" }
              },
              "projects": {
                "{{projectKey}}": {
                  "mcpServers": {
                    "precedence": { "command": "local-command" },
                    "localOnly": { "command": "local-only" }
                  }
                }
              }
            }
            """,
            Encoding.UTF8);

        File.WriteAllText(
            enterprisePath,
            """
            {
              "mcpServers": {
                "precedence": { "command": "enterprise-command" },
                "enterpriseOnly": { "command": "enterprise-only" }
              }
            }
            """,
            Encoding.UTF8);

        var service = new McpConfigService(workspaceRoot, globalConfigPath, enterprisePath);

        Assert.Equal("enterprise-command", Assert.IsType<McpStdioServerConfig>(service.GetConfigByName("precedence")!.Config).Command);
        Assert.Equal(McpConfigScope.Local, service.GetConfigByName("localOnly")!.Scope);
        Assert.Equal(McpConfigScope.Project, service.GetConfigByName("projectOnly")!.Scope);
        Assert.Equal(McpConfigScope.User, service.GetConfigByName("userOnly")!.Scope);
        Assert.Equal(McpConfigScope.Enterprise, service.GetConfigByName("enterpriseOnly")!.Scope);
    }

    [Fact]
    public void GetAllConfigs_MergesAdditionalServers_Below_FileScopes()
    {
        var workspaceRoot = CreateTempDirectory();
        var globalConfigPath = Path.Combine(workspaceRoot, ".clawsharp.json");

        File.WriteAllText(
            Path.Combine(workspaceRoot, ".mcp.json"),
            """
            {
              "mcpServers": {
                "projectOnly": { "command": "project-command" },
                "shared": { "command": "project-shared" }
              }
            }
            """,
            Encoding.UTF8);

        File.WriteAllText(
            globalConfigPath,
            """
            {
              "mcpServers": {
                "userOnly": { "command": "user-command" },
                "shared": { "command": "user-shared" }
              }
            }
            """,
            Encoding.UTF8);

        var service = new McpConfigService(workspaceRoot, globalConfigPath);
        var pluginServers = new Dictionary<string, ScopedMcpServerConfig>(StringComparer.Ordinal)
        {
            ["pluginOnly"] = new("pluginOnly", new McpHttpServerConfig("https://mcp.linear.app/mcp", null, null, null), McpConfigScope.Dynamic, "linear@builtin"),
            ["shared"] = new("shared", new McpHttpServerConfig("https://plugin.example.test/mcp", null, null, null), McpConfigScope.Dynamic, "linear@builtin")
        };

        var (servers, errors) = service.GetAllConfigs(pluginServers);

        Assert.Empty(errors);
        Assert.Equal(4, servers.Count);
        Assert.Equal("https://mcp.linear.app/mcp", Assert.IsType<McpHttpServerConfig>(servers["pluginOnly"].Config).Url);
        Assert.Equal("user-command", Assert.IsType<McpStdioServerConfig>(servers["userOnly"].Config).Command);
        Assert.Equal("project-command", Assert.IsType<McpStdioServerConfig>(servers["projectOnly"].Config).Command);
        Assert.Equal("project-shared", Assert.IsType<McpStdioServerConfig>(servers["shared"].Config).Command);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
