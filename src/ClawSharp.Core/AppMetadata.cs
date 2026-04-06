// TS origin: no direct 1:1 TS source yet; ClawSharp-specific application metadata scaffold.
using System.Reflection;

namespace ClawSharp.Core;

public static class AppMetadata
{
    public const string Name = "ClawSharp";
    public const string CommandName = "clawsharp";
    public const string NpmPackageName = "clawsharp";

    private static readonly Lazy<string> VersionText = new(ResolveVersion);

    public static string Version => VersionText.Value;

    public static string DisplayVersion => $"{Name} {Version}";

    public static string InstallCommand => $"npm install -g {NpmPackageName}";

    public static string UpdateInstallCommand => $"npm update -g {NpmPackageName}";

    public static string LatestInstallCommand => $"npm install -g {NpmPackageName}@latest";

    public static string RemoteControlCommand => $"{CommandName} remote-control";

    public static string UpdateCommand => $"{CommandName} update";

    private static string ResolveVersion()
    {
        var assembly = typeof(AppMetadata).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2, StringSplitOptions.TrimEntries)[0];
        }

        var assemblyVersion = assembly.GetName().Version;
        if (assemblyVersion is null)
        {
            return "0.0.0";
        }

        return $"{assemblyVersion.Major}.{Math.Max(assemblyVersion.Minor, 0)}.{Math.Max(assemblyVersion.Build, 0)}";
    }
}
