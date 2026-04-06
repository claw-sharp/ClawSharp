using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class BridgeClientContextUtilitiesTests
{
    [Fact]
    public async Task CheckHasTrustDialogAcceptedAsync_Returns_True_For_Session_Trust()
    {
        var repoRoot = OperatingSystem.IsWindows() ? @"D:\repo" : "/repo";
        var utilities = new BridgeClientContextUtilities(
            new BridgeClientContextDependencies(
                GetSessionTrustAccepted: () => true,
                GetCurrentDirectory: () => repoRoot));

        var trusted = await utilities.CheckHasTrustDialogAcceptedAsync();

        Assert.True(trusted);
    }

    [Fact]
    public async Task CheckHasTrustDialogAcceptedAsync_Uses_Git_Root_Project_Key()
    {
        var repoRoot = OperatingSystem.IsWindows() ? @"D:\repo" : "/repo";
        var repoRootJson = repoRoot.Replace('\\', '/');
        var globalConfigPath = CreateGlobalConfig(
            $$"""
            {
              "projects": {
                "{{repoRootJson}}": {
                  "hasTrustDialogAccepted": true
                }
              }
            }
            """);

        try
        {
            var utilities = new BridgeClientContextUtilities(
                new BridgeClientContextDependencies(
                    GlobalConfigPath: globalConfigPath,
                    GetCurrentDirectory: () => Path.Combine(repoRoot, "subdir"),
                    ExecuteAsync: (_, _, _) => Task.FromResult(new ProcessExecutionResult(0, $"{repoRoot}\n", string.Empty))));

            var trusted = await utilities.CheckHasTrustDialogAcceptedAsync();

            Assert.True(trusted);
        }
        finally
        {
            File.Delete(globalConfigPath);
        }
    }

    [Fact]
    public async Task CheckHasTrustDialogAcceptedAsync_Inherits_Trust_From_Parent_Path()
    {
        var repoRoot = OperatingSystem.IsWindows() ? @"D:\repo" : "/repo";
        var repoRootJson = repoRoot.Replace('\\', '/');
        var globalConfigPath = CreateGlobalConfig(
            $$"""
            {
              "projects": {
                "{{repoRootJson}}": {
                  "hasTrustDialogAccepted": true
                }
              }
            }
            """);

        try
        {
            var utilities = new BridgeClientContextUtilities(
                new BridgeClientContextDependencies(
                    GlobalConfigPath: globalConfigPath,
                    GetCurrentDirectory: () => Path.Combine(repoRoot, "nested", "deeper"),
                    ExecuteAsync: (_, _, _) => Task.FromResult(new ProcessExecutionResult(1, string.Empty, "not a repo"))));

            var trusted = await utilities.CheckHasTrustDialogAcceptedAsync();

            Assert.True(trusted);
        }
        finally
        {
            File.Delete(globalConfigPath);
        }
    }

    [Fact]
    public async Task IsPathTrustedAsync_Walks_Ancestor_Directories()
    {
        var repoRoot = OperatingSystem.IsWindows() ? @"D:\repo" : "/repo";
        var repoRootJson = repoRoot.Replace('\\', '/');
        var otherRoot = OperatingSystem.IsWindows() ? @"D:\other" : "/other";
        
        var globalConfigPath = CreateGlobalConfig(
            $$"""
            {
              "projects": {
                "{{repoRootJson}}": {
                  "hasTrustDialogAccepted": true
                }
              }
            }
            """);

        try
        {
            var utilities = new BridgeClientContextUtilities(
                new BridgeClientContextDependencies(
                    GlobalConfigPath: globalConfigPath));

            Assert.True(await utilities.IsPathTrustedAsync(Path.Combine(repoRoot, "child")));
            Assert.False(await utilities.IsPathTrustedAsync(otherRoot));
        }
        finally
        {
            File.Delete(globalConfigPath);
        }
    }

    [Fact]
    public async Task IsPathTrustedAsync_Normalizes_Msys_Windows_Paths_For_Config_Keys()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var globalConfigPath = CreateGlobalConfig(
            """
            {
              "projects": {
                "C:/repo": {
                  "hasTrustDialogAccepted": true
                }
              }
            }
            """);

        try
        {
            var utilities = new BridgeClientContextUtilities(
                new BridgeClientContextDependencies(
                    GlobalConfigPath: globalConfigPath));

            Assert.True(await utilities.IsPathTrustedAsync("/c/repo/child"));
        }
        finally
        {
            File.Delete(globalConfigPath);
        }
    }

    [Fact]
    public void GetCachedOrganizationUuid_Prefers_Environment_Then_Global_Config()
    {
        var globalConfigPath = CreateGlobalConfig(
            """
            {
              "oauthAccount": {
                "organizationUuid": "org-from-config"
              }
            }
            """);

        try
        {
            var fromEnv = new BridgeClientContextUtilities(
                new BridgeClientContextDependencies(
                    GlobalConfigPath: globalConfigPath,
                    GetEnvironmentVariable: name => name == "CLAUDE_CODE_ORGANIZATION_UUID" ? "org-from-env" : null));
            var fromConfig = new BridgeClientContextUtilities(
                new BridgeClientContextDependencies(
                    GlobalConfigPath: globalConfigPath,
                    GetEnvironmentVariable: _ => null));

            Assert.Equal("org-from-env", fromEnv.GetCachedOrganizationUuid());
            Assert.Equal("org-from-config", fromConfig.GetCachedOrganizationUuid());
        }
        finally
        {
            File.Delete(globalConfigPath);
        }
    }

    private static string CreateGlobalConfig(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
