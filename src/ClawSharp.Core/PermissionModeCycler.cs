// TS origin: ./utils/permissions/getNextPermissionMode.ts
namespace ClawSharp.Core;

public static class PermissionModeCycler
{
    public static PermissionMode GetNextPermissionMode(ToolPermissionContext context)
    {
        return context.Mode switch
        {
            PermissionMode.Default => GetNextFromDefault(context),
            PermissionMode.AcceptEdits => PermissionMode.Plan,
            PermissionMode.Plan => GetNextFromPlan(context),
            PermissionMode.BypassPermissions => CanCycleToAuto(context)
                ? PermissionMode.Auto
                : PermissionMode.Default,
            PermissionMode.DontAsk => PermissionMode.Default,
            _ => PermissionMode.Default
        };
    }

    public static (PermissionMode NextMode, ToolPermissionContext Context) CyclePermissionMode(
        ToolPermissionContext context,
        bool useAutoModeDuringPlan = false)
    {
        var nextMode = GetNextPermissionMode(context);
        var effectiveUseAutoModeDuringPlan = useAutoModeDuringPlan || context.UseAutoModeDuringPlan;
        return (
            nextMode,
            PermissionModeTransition.Transition(context, nextMode, effectiveUseAutoModeDuringPlan));
    }

    private static PermissionMode GetNextFromDefault(ToolPermissionContext context)
    {
        if (IsAntUser())
        {
            if (context.IsBypassPermissionsModeAvailable)
            {
                return PermissionMode.BypassPermissions;
            }

            if (CanCycleToAuto(context))
            {
                return PermissionMode.Auto;
            }

            return PermissionMode.Default;
        }

        return PermissionMode.AcceptEdits;
    }

    private static PermissionMode GetNextFromPlan(ToolPermissionContext context)
    {
        if (context.IsBypassPermissionsModeAvailable)
        {
            return PermissionMode.BypassPermissions;
        }

        if (CanCycleToAuto(context))
        {
            return PermissionMode.Auto;
        }

        return PermissionMode.Default;
    }

    private static bool CanCycleToAuto(ToolPermissionContext context)
    {
        return context.IsAutoModeAvailable == true;
    }

    private static bool IsAntUser()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("USER_TYPE"),
            "ant",
            StringComparison.Ordinal);
    }
}
