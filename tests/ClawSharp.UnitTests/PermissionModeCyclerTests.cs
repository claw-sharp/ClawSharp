using ClawSharp.Core;

namespace ClawSharp.UnitTests;

public sealed class PermissionModeCyclerTests
{
    [Fact]
    public void GetNextPermissionMode_Default_User_Follows_Default_AcceptEdits_Plan_Order()
    {
        var originalUserType = Environment.GetEnvironmentVariable("USER_TYPE");
        Environment.SetEnvironmentVariable("USER_TYPE", null);

        try
        {
            Assert.Equal(
                PermissionMode.AcceptEdits,
                PermissionModeCycler.GetNextPermissionMode(CreateContext(PermissionMode.Default)));
            Assert.Equal(
                PermissionMode.Plan,
                PermissionModeCycler.GetNextPermissionMode(CreateContext(PermissionMode.AcceptEdits)));
            Assert.Equal(
                PermissionMode.Default,
                PermissionModeCycler.GetNextPermissionMode(CreateContext(PermissionMode.Plan)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("USER_TYPE", originalUserType);
        }
    }

    [Fact]
    public void GetNextPermissionMode_Plan_Prefers_Bypass_Before_Auto()
    {
        var context = CreateContext(PermissionMode.Plan) with
        {
            IsBypassPermissionsModeAvailable = true,
            IsAutoModeAvailable = true
        };

        var nextMode = PermissionModeCycler.GetNextPermissionMode(context);

        Assert.Equal(PermissionMode.BypassPermissions, nextMode);
    }

    [Fact]
    public void GetNextPermissionMode_Bypass_Falls_Through_To_Auto_When_Available()
    {
        var context = CreateContext(PermissionMode.BypassPermissions) with
        {
            IsAutoModeAvailable = true
        };

        var nextMode = PermissionModeCycler.GetNextPermissionMode(context);

        Assert.Equal(PermissionMode.Auto, nextMode);
    }

    [Fact]
    public void GetNextPermissionMode_Ant_User_Skips_AcceptEdits_And_Plan()
    {
        var originalUserType = Environment.GetEnvironmentVariable("USER_TYPE");
        Environment.SetEnvironmentVariable("USER_TYPE", "ant");

        try
        {
            Assert.Equal(
                PermissionMode.Auto,
                PermissionModeCycler.GetNextPermissionMode(
                    CreateContext(PermissionMode.Default) with
                    {
                        IsAutoModeAvailable = true
                    }));

            Assert.Equal(
                PermissionMode.BypassPermissions,
                PermissionModeCycler.GetNextPermissionMode(
                    CreateContext(PermissionMode.Default) with
                    {
                        IsBypassPermissionsModeAvailable = true,
                        IsAutoModeAvailable = true
                    }));
        }
        finally
        {
            Environment.SetEnvironmentVariable("USER_TYPE", originalUserType);
        }
    }

    [Fact]
    public void CyclePermissionMode_Uses_Transition_Helper_For_Context_Changes()
    {
        var context = CreateContext(PermissionMode.Plan) with
        {
            PrePlanMode = PermissionMode.AcceptEdits
        };

        var (nextMode, updatedContext) = PermissionModeCycler.CyclePermissionMode(context);

        Assert.Equal(PermissionMode.Default, nextMode);
        Assert.Equal(PermissionMode.AcceptEdits, updatedContext.Mode);
        Assert.Null(updatedContext.PrePlanMode);
    }

    private static ToolPermissionContext CreateContext(PermissionMode mode)
    {
        return new ToolPermissionContext(
            mode,
            new Dictionary<string, AdditionalWorkingDirectory>(StringComparer.Ordinal),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            new Dictionary<PermissionRuleSource, IReadOnlyList<string>>(),
            IsBypassPermissionsModeAvailable: false);
    }
}
