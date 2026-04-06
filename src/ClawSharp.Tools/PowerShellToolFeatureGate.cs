// TS origin: ./utils/shell/shellToolUtils.ts, ./utils/envUtils.ts
// TS parity status: ports the current PowerShell tool visibility gate for the existing C# PowerShell tool surface; other TS feature-gated tools remain blocked by their still-absent tool/runtime implementations.
namespace ClawSharp.Tools;

public static class PowerShellToolFeatureGate
{
    public static bool IsEnabled()
    {
        return IsEnabled(
            Environment.GetEnvironmentVariable("USER_TYPE"),
            Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_POWERSHELL_TOOL"),
            OperatingSystem.IsWindows());
    }

    public static bool IsEnabled(string? userType, string? configuredValue, bool isWindows)
    {
        if (!isWindows)
        {
            return false;
        }

        return string.Equals(userType, "ant", StringComparison.Ordinal)
            ? !IsEnvDefinedFalsy(configuredValue)
            : IsEnvTruthy(configuredValue);
    }

    private static bool IsEnvTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private static bool IsEnvDefinedFalsy(string? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value.Length == 0)
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() is "0" or "false" or "no" or "off";
    }
}
