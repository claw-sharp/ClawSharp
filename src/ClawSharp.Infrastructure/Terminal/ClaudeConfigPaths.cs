using System.Text;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static class ClaudeConfigPaths
{
    public static string GetUserSettingsFilePath()
    {
        return Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "settings.json");
    }

    public static string GetProjectSettingsFilePath(string workspaceRoot)
    {
        return Path.Combine(workspaceRoot, ".clawsharp", "settings.json");
    }

    public static string GetLocalSettingsFilePath(string workspaceRoot)
    {
        return Path.Combine(workspaceRoot, ".clawsharp", "settings.local.json");
    }

    public static string GetManagedSettingsFilePath()
    {
        return Path.Combine(GetManagedFilePath(), "managed-settings.json");
    }

    public static string GetUserSkillsDirectoryPath()
    {
        return Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "skills");
    }

    public static string GetManagedSkillsDirectoryPath()
    {
        return Path.Combine(GetManagedFilePath(), ".clawsharp", "skills");
    }

    public static string GetPluginsDirectoryPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("CLAUDE_CODE_PLUGIN_CACHE_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return ExpandTilde(overridePath);
        }

        var useCoworkPlugins = IsTruthy(Environment.GetEnvironmentVariable("CLAUDE_CODE_USE_COWORK_PLUGINS"));
        var directoryName = useCoworkPlugins ? "cowork_plugins" : "plugins";
        return Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), directoryName);
    }

    public static string GetInstalledPluginsFilePath()
    {
        return Path.Combine(GetPluginsDirectoryPath(), "installed_plugins.json");
    }

    public static string GetVersionedPluginCachePath(string pluginsDirectoryPath, string pluginId, string version)
    {
        var separatorIndex = pluginId.LastIndexOf('@');
        var pluginName = separatorIndex >= 0 ? pluginId[..separatorIndex] : pluginId;
        var marketplace = separatorIndex >= 0 && separatorIndex + 1 < pluginId.Length
            ? pluginId[(separatorIndex + 1)..]
            : "unknown";

        return Path.Combine(
            pluginsDirectoryPath,
            "cache",
            SanitizePluginSegment(marketplace),
            SanitizePluginSegment(pluginName),
            SanitizePluginVersion(version));
    }

    public static string GetStartupPerfLogPath(string runId)
    {
        return Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), "startup-perf", $"{runId}.txt");
    }

    public static string GetLegacyClaudeConfigPath()
    {
        return Path.Combine(SessionStoragePaths.GetClaudeConfigHomeDir(), ".config.json");
    }

    public static string GetGlobalClaudeFilePath()
    {
        var legacyPath = GetLegacyClaudeConfigPath();
        if (File.Exists(legacyPath))
        {
            return legacyPath;
        }

        var configDirectory = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var parentDirectory = string.IsNullOrWhiteSpace(configDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : configDirectory;

        return Path.Combine(parentDirectory, ".clawsharp.json");
    }

    public static string GetManagedFilePath()
    {
        if (OperatingSystem.IsWindows())
        {
            return @"C:\Program Files\ClaudeCode";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "/Library/Application Support/ClaudeCode";
        }

        return "/etc/claude-code";
    }

    public static string GetEnterpriseMcpFilePath()
    {
        return Path.Combine(GetManagedFilePath(), "managed-mcp.json");
    }

    public static string NormalizeProjectPathForConfigKey(string path)
    {
        var fullPath = Path.GetFullPath(path).Normalize(NormalizationForm.FormC);
        return fullPath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ExpandTilde(string path)
    {
        if (!path.StartsWith("~", StringComparison.Ordinal))
        {
            return path;
        }

        var homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.Length == 1)
        {
            return homeDirectory;
        }

        if (path[1] is '/' or '\\')
        {
            return Path.Combine(homeDirectory, path[2..]);
        }

        return path;
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private static string SanitizePluginSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(IsPluginSafeCharacter(character) ? character : '-');
        }

        return builder.ToString();
    }

    private static string SanitizePluginVersion(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(IsPluginVersionCharacter(character) ? character : '-');
        }

        return builder.ToString();
    }

    private static bool IsPluginSafeCharacter(char character)
    {
        return
            (character >= 'a' && character <= 'z') ||
            (character >= 'A' && character <= 'Z') ||
            (character >= '0' && character <= '9') ||
            character is '-' or '_';
    }

    private static bool IsPluginVersionCharacter(char character)
    {
        return IsPluginSafeCharacter(character) || character is '.';
    }
}
