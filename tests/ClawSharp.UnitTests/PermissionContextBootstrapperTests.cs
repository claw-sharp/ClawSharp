using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class PermissionContextBootstrapperTests
{
    [Fact]
    public void Load_Propagates_Sandbox_Policy_Inputs_From_Settings()
    {
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-permission-context", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);

        try
        {
            var settings = new ClawSharpSettings
            {
                Sandbox = new SandboxSettings
                {
                    Enabled = true,
                    AllowUnsandboxedCommands = false,
                    FailIfUnavailable = true
                }
            };

            var context = new PermissionContextBootstrapper().Load(workspaceRoot, settings);

            Assert.True(context.IsSandboxEnabledInSettings);
            Assert.False(context.AreUnsandboxedCommandsAllowed);
        }
        finally
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }
}
