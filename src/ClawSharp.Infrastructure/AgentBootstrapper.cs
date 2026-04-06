// TS origin: ./tools/AgentTool/loadAgentsDir.ts, ./utils/markdownConfigLoader.ts
using System.Diagnostics;
using System.Text;
using ClawSharp.Core;
using YamlDotNet.Serialization;

namespace ClawSharp.Infrastructure;

public sealed class AgentBootstrapper
{
    private readonly string _managedFilePath;
    private readonly string _userConfigHomeDir;
    private readonly IDeserializer _yamlDeserializer;

    public AgentBootstrapper(
        string? managedFilePath = null,
        string? userConfigHomeDir = null)
    {
        _managedFilePath = managedFilePath ?? ClaudeConfigPaths.GetManagedFilePath();
        _userConfigHomeDir = userConfigHomeDir ?? SessionStoragePaths.GetClaudeConfigHomeDir();
        _yamlDeserializer = new DeserializerBuilder().Build();
    }

    public Task<AgentDefinitionsCatalog> LoadAsync(
        string workspaceRoot,
        StartupEnvironment startupEnvironment,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var builtInAgents = BuiltInAgentDefinitions.GetBuiltInAgents();
        if (startupEnvironment.BareMode)
        {
            return Task.FromResult(new AgentDefinitionsCatalog(builtInAgents, builtInAgents));
        }

        var failures = new List<AgentLoadFailure>();
        var customAgents = new List<AgentDefinition>();

        AddAgentsFromDirectory(Path.Combine(_managedFilePath, ".claude", "agents"), "policySettings", customAgents, failures);
        AddAgentsFromDirectory(Path.Combine(_userConfigHomeDir, "agents"), "userSettings", customAgents, failures);

        foreach (var projectAgentsDirectory in GetProjectAgentDirectoriesUpToHome(workspaceRoot))
        {
            AddAgentsFromDirectory(projectAgentsDirectory, "projectSettings", customAgents, failures);
        }

        var allAgents = builtInAgents.Concat(customAgents).ToArray();
        var activeAgents = GetActiveAgentsFromList(allAgents);
        return Task.FromResult(
            new AgentDefinitionsCatalog(
                activeAgents,
                allAgents,
                failures.Count == 0 ? null : failures));
    }

    private void AddAgentsFromDirectory(
        string basePath,
        string source,
        List<AgentDefinition> discoveredAgents,
        List<AgentLoadFailure> failures)
    {
        if (!Directory.Exists(basePath))
        {
            return;
        }

        foreach (var filePath in Directory.GetFiles(basePath, "*.md", SearchOption.TopDirectoryOnly).OrderBy(static path => path, GetPathComparer()))
        {
            var agent = TryParseAgent(filePath, basePath, source, out var error);
            if (agent is not null)
            {
                discoveredAgents.Add(agent);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                failures.Add(new AgentLoadFailure(filePath, error));
            }
        }
    }

    private AgentDefinition? TryParseAgent(
        string filePath,
        string baseDirectory,
        string source,
        out string? error)
    {
        error = null;

        var text = File.ReadAllText(filePath, Encoding.UTF8);
        var (frontmatter, content) = SplitFrontmatter(text);
        if (frontmatter is null || frontmatter.Count == 0)
        {
            return null;
        }

        if (!TryGetString(frontmatter, "name", out var agentType))
        {
            return null;
        }

        if (!TryGetString(frontmatter, "description", out var whenToUse))
        {
            error = "Missing required \"description\" field in frontmatter";
            return null;
        }

        return new AgentDefinition(
            AgentType: agentType,
            WhenToUse: whenToUse.Replace("\\n", "\n", StringComparison.Ordinal),
            Source: source,
            BaseDirectory: Path.GetFullPath(baseDirectory),
            SystemPrompt: content.Trim(),
            Tools: ParseAgentToolList(TryGetValue(frontmatter, "tools")),
            DisallowedTools: ParseAgentToolList(TryGetValue(frontmatter, "disallowedTools")),
            Skills: ParseStringList(TryGetValue(frontmatter, "skills")),
            Color: TryGetOptionalString(frontmatter, "color"),
            Model: TryGetOptionalString(frontmatter, "model"),
            PermissionMode: TryParsePermissionMode(TryGetOptionalString(frontmatter, "permissionMode")),
            MaxTurns: TryParsePositiveInt(TryGetValue(frontmatter, "maxTurns")),
            Filename: Path.GetFileNameWithoutExtension(filePath),
            Background: TryParseBoolean(TryGetValue(frontmatter, "background")),
            InitialPrompt: TryGetOptionalString(frontmatter, "initialPrompt"),
            Memory: TryGetOptionalString(frontmatter, "memory"),
            Isolation: TryGetOptionalString(frontmatter, "isolation"),
            OmitClaudeMd: TryParseBoolean(TryGetValue(frontmatter, "omitClaudeMd")) ?? false);
    }

    private static IReadOnlyList<AgentDefinition> GetActiveAgentsFromList(
        IReadOnlyList<AgentDefinition> allAgents)
    {
        var agentMap = new Dictionary<string, AgentDefinition>(StringComparer.Ordinal);
        foreach (var group in new[]
                 {
                     allAgents.Where(static agent => agent.Source == "built-in"),
                     allAgents.Where(static agent => agent.Source == "userSettings"),
                     allAgents.Where(static agent => agent.Source == "projectSettings"),
                     allAgents.Where(static agent => agent.Source == "policySettings")
                 })
        {
            foreach (var agent in group)
            {
                agentMap[agent.AgentType] = agent;
            }
        }

        return agentMap.Values.ToArray();
    }

    private (Dictionary<string, object?>? Frontmatter, string Content) SplitFrontmatter(string text)
    {
        if (!text.StartsWith("---", StringComparison.Ordinal))
        {
            return (null, text);
        }

        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        {
            return (null, text);
        }

        var frontmatterBuilder = new StringBuilder();
        string? line;
        var foundClosing = false;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.Equals(line, "---", StringComparison.Ordinal))
            {
                foundClosing = true;
                break;
            }

            frontmatterBuilder.AppendLine(line);
        }

        if (!foundClosing)
        {
            return (null, text);
        }

        var content = reader.ReadToEnd();
        var parsed = _yamlDeserializer.Deserialize<Dictionary<object, object?>>(frontmatterBuilder.ToString()) ?? [];
        var normalized = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in parsed)
        {
            if (pair.Key is null)
            {
                continue;
            }

            normalized[pair.Key.ToString() ?? string.Empty] = pair.Value;
        }

        return (normalized, content);
    }

    private IReadOnlyList<string> GetProjectAgentDirectoriesUpToHome(string workspaceRoot)
    {
        var homeDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var gitRoot = TryGetCanonicalGitRoot(workspaceRoot);
        var current = Path.GetFullPath(workspaceRoot);
        var directories = new List<string>();

        while (true)
        {
            if (PathsEqual(current, homeDirectory))
            {
                break;
            }

            var agentsDirectory = Path.Combine(current, ".claude", "agents");
            if (Directory.Exists(agentsDirectory))
            {
                directories.Add(agentsDirectory);
            }

            if (gitRoot is not null && PathsEqual(current, gitRoot))
            {
                break;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
            {
                break;
            }

            current = parent;
        }

        return directories;
    }

    private static IReadOnlyList<string>? ParseAgentToolList(object? value)
    {
        var parsed = ParseStringList(value);
        if (value is null)
        {
            return null;
        }

        if (parsed is null || parsed.Count == 0)
        {
            return [];
        }

        return parsed.Contains("*", StringComparer.Ordinal) ? null : parsed;
    }

    private static IReadOnlyList<string>? ParseStringList(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string stringValue)
        {
            return string.IsNullOrWhiteSpace(stringValue) ? [] : [stringValue.Trim()];
        }

        if (value is IEnumerable<object?> sequence)
        {
            return sequence
                .Select(static item => item?.ToString()?.Trim())
                .Where(static item => !string.IsNullOrWhiteSpace(item))
                .Cast<string>()
                .ToArray();
        }

        return [];
    }

    private static PermissionMode? TryParsePermissionMode(string? value)
    {
        return value?.Trim() switch
        {
            "default" => PermissionMode.Default,
            "acceptEdits" => PermissionMode.AcceptEdits,
            "bypassPermissions" => PermissionMode.BypassPermissions,
            "dontAsk" => PermissionMode.DontAsk,
            "plan" => PermissionMode.Plan,
            "auto" => PermissionMode.Auto,
            "bubble" => PermissionMode.Bubble,
            _ => null
        };
    }

    private static int? TryParsePositiveInt(object? value)
    {
        if (value is not null && int.TryParse(value.ToString(), out var parsed) && parsed > 0)
        {
            return parsed;
        }

        return null;
    }

    private static bool? TryParseBoolean(object? value)
    {
        if (value is bool boolValue)
        {
            return boolValue;
        }

        if (value is not null && bool.TryParse(value.ToString(), out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool TryGetString(Dictionary<string, object?> values, string key, out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        value = raw.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static string? TryGetOptionalString(Dictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var raw) && raw is not null
            ? raw.ToString()
            : null;
    }

    private static object? TryGetValue(Dictionary<string, object?> values, string key)
    {
        return values.TryGetValue(key, out var raw) ? raw : null;
    }

    private static string? TryGetCanonicalGitRoot(string workspaceRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = "rev-parse --show-toplevel",
            WorkingDirectory = workspaceRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch
        {
            return null;
        }

        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd().Trim();
        return string.IsNullOrWhiteSpace(output) ? null : Path.GetFullPath(output);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), GetPathComparison());
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }
}
