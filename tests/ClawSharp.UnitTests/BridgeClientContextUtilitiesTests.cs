using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class BridgeClientContextUtilitiesTests
{
    [Fact]
    public async Task CheckHasTrustDialogAcceptedAsync_Returns_True_For_Session_Trust()
    {
        var utilities = new BridgeClientContextUtilities(
            new BridgeClientContextDependencies(
                GetSessionTrustAccepted: () => true,
                GetCurrentDirectory: () => "D:\\repo"));

        var trusted = await utilities.CheckHasTrustDialogAcceptedAsync();

        Assert.True(trusted);
    }

    [Fact]
    public async Task CheckHasTrustDialogAcceptedAsync_Uses_Git_Root_Project_Key()
    {
        var globalConfigPath = CreateGlobalConfig(
            """
            {
              "projects": {
                "D:/repo": {
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
                    GetCurrentDirectory: () => "D:\\repo\\subdir",
                    ExecuteAsync: (_, _, _) => Task.FromResult(new ProcessExecutionResult(0, "D:\\repo\n", string.Empty))));

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
        var globalConfigPath = CreateGlobalConfig(
            """
            {
              "projects": {
                "D:/repo": {
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
                    GetCurrentDirectory: () => "D:\\repo\\nested\\deeper",
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
        var globalConfigPath = CreateGlobalConfig(
            """
            {
              "projects": {
                "D:/repo": {
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

            Assert.True(await utilities.IsPathTrustedAsync("D:\\repo\\child"));
            Assert.False(await utilities.IsPathTrustedAsync("D:\\other"));
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
