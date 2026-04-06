// TS origin: ./skills/loadSkillsDir.ts, ./utils/markdownConfigLoader.ts, ./utils/plugins/pluginLoader.ts, ./utils/plugins/installedPluginsManager.ts, ./utils/plugins/loadPluginHooks.ts
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class ExtensionBootstrapper
{
    private static readonly IReadOnlySet<string> AllowedPluginSettingKeys =
        new HashSet<string>(["agent"], StringComparer.Ordinal);

    private readonly string _managedFilePath;
    private readonly string _userConfigHomeDir;
    private readonly string _pluginsDirectoryPath;
    private readonly string? _bundledSkillsDirectoryPath;
    private readonly string? _bundledPluginsDirectoryPath;
    private readonly BuiltInPluginRegistry _builtInPluginRegistry;

    public ExtensionBootstrapper(
        string? managedFilePath = null,
        string? userConfigHomeDir = null,
        string? pluginsDirectoryPath = null,
        string? bundledSkillsDirectoryPath = null,
        string? bundledPluginsDirectoryPath = null,
        BuiltInPluginRegistry? builtInPluginRegistry = null)
    {
        _managedFilePath = managedFilePath ?? ClaudeConfigPaths.GetManagedFilePath();
        _userConfigHomeDir = userConfigHomeDir ?? SessionStoragePaths.GetClaudeConfigHomeDir();
        _pluginsDirectoryPath = pluginsDirectoryPath ?? ClaudeConfigPaths.GetPluginsDirectoryPath();
        _bundledSkillsDirectoryPath = bundledSkillsDirectoryPath;
        _bundledPluginsDirectoryPath = bundledPluginsDirectoryPath;
        _builtInPluginRegistry = builtInPluginRegistry ?? new BuiltInPluginRegistry();
    }

    public Task<ExtensionBootstrapResult> LoadAsync(
        string workspaceRoot,
        StartupEnvironment startupEnvironment,
        ClawSharpSettings settings,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();

        var pluginInstallations = LoadInstalledPlugins();
        var plugins = LoadPlugins(pluginInstallations, settings, cancellationToken);
        var skills = LoadSkills(workspaceRoot, startupEnvironment, plugins);
        var hooks = LoadHooks(settings, plugins);
        stopwatch.Stop();

        PluginTelemetryUtilities.LogPluginsEnabledForSession(plugins);
        PluginTelemetryUtilities.LogPluginLoadFailures(plugins);
        PluginTelemetryUtilities.LogSkillsLoaded(skills);
        ClawSharpTelemetry.LogEvent(
            "tengu_extension_bootstrap",
            new Dictionary<string, object?>
            {
                ["plugin_installation_count"] = pluginInstallations.Count,
                ["plugin_count"] = plugins.Count,
                ["enabled_plugin_count"] = plugins.Count(static plugin => plugin.Enabled),
                ["skill_count"] = skills.Count,
                ["hook_count"] = hooks.Count,
                ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds
            });
        ClawSharpTelemetry.RecordMetric("extension.bootstrap.duration_ms", stopwatch.Elapsed.TotalMilliseconds);

        return Task.FromResult(new ExtensionBootstrapResult(pluginInstallations, plugins, skills, hooks));
    }

    private IReadOnlyList<DiscoveredPluginInstallation> LoadInstalledPlugins()
    {
        var installedPluginsPath = Path.Combine(_pluginsDirectoryPath, "installed_plugins.json");
        if (!File.Exists(installedPluginsPath))
        {
            return [];
        }

        using var document = JsonDocument.Parse(File.ReadAllText(installedPluginsPath, Encoding.UTF8));
        if (!document.RootElement.TryGetProperty("version", out var versionElement) ||
            versionElement.ValueKind != JsonValueKind.Number ||
            !versionElement.TryGetInt32(out var version) ||
            !document.RootElement.TryGetProperty("plugins", out var pluginsElement) ||
            pluginsElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var installations = new List<DiscoveredPluginInstallation>();
        foreach (var pluginProperty in pluginsElement.EnumerateObject())
        {
            if (version == 1)
            {
                AddVersion1Installation(installations, pluginProperty);
            }
            else if (version == 2)
            {
                AddVersion2Installations(installations, pluginProperty);
            }
        }

        return installations;
    }

    private IReadOnlyList<DiscoveredPlugin> LoadPlugins(
        IReadOnlyList<DiscoveredPluginInstallation> installations,
        ClawSharpSettings settings,
        CancellationToken cancellationToken)
    {
        var plugins = new List<DiscoveredPlugin>();
        var seenPluginIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var installation in installations.OrderBy(static item => item.PluginId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plugin = LoadPluginFromDirectory(installation.PluginId, installation.InstallPath, settings, isBundled: false);
            if (seenPluginIds.Add(plugin.PluginId))
            {
                plugins.Add(plugin);
            }
        }

        foreach (var definition in _builtInPluginRegistry.GetDefinitions().OrderBy(static item => item.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (definition.IsAvailable is not null && !definition.IsAvailable())
            {
                continue;
            }

            var plugin = LoadBuiltInPlugin(definition, settings);
            if (seenPluginIds.Add(plugin.PluginId))
            {
                plugins.Add(plugin);
            }
        }

        if (!string.IsNullOrWhiteSpace(_bundledPluginsDirectoryPath) && Directory.Exists(_bundledPluginsDirectoryPath))
        {
            foreach (var entryPath in Directory.GetDirectories(_bundledPluginsDirectoryPath).OrderBy(static path => path, GetPathComparer()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entryPath);
                var plugin = LoadPluginFromDirectory($"{name}@builtin", entryPath, settings, isBundled: true);
                if (seenPluginIds.Add(plugin.PluginId))
                {
                    plugins.Add(plugin);
                }
            }
        }

        return plugins;
    }

    private DiscoveredPlugin LoadPluginFromDirectory(
        string pluginId,
        string installPath,
        ClawSharpSettings settings,
        bool isBundled)
    {
        var issues = new List<PluginValidationIssue>();
        var manifestPath = Path.Combine(installPath, "plugin.json");
        PluginManifest? manifest = null;
        if (File.Exists(manifestPath))
        {
            manifest = ParsePluginManifest(manifestPath, issues);
        }
        else
        {
            issues.Add(new PluginValidationIssue("plugin.json", $"Plugin manifest was not found at '{manifestPath}'."));
        }

        var hooks = LoadPluginHooks(installPath, manifest, issues);
        var pluginSettings = LoadPluginSettings(installPath, manifest, issues);
        var enabled = !settings.EnabledPlugins.TryGetValue(pluginId, out var enabledSetting) || enabledSetting.Enabled != false;
        var name = manifest?.Name ?? GetPluginNameFromIdentifier(pluginId);
        return new DiscoveredPlugin(
            pluginId,
            name,
            installPath,
            enabled,
            isBundled,
            manifest,
            issues,
            hooks,
            pluginSettings,
            manifest?.UserConfig);
    }

    private DiscoveredPlugin LoadBuiltInPlugin(BuiltInPluginDefinition definition, ClawSharpSettings settings)
    {
        var issues = new List<PluginValidationIssue>();
        var manifestPath = Path.Combine(definition.RootPath, "plugin.json");
        var parsedManifest = File.Exists(manifestPath)
            ? ParsePluginManifest(manifestPath, issues)
            : null;

        var skillDirectories = parsedManifest?.Skills ?? [];
        if (definition.SkillDirectories is not null && definition.SkillDirectories.Count > 0)
        {
            skillDirectories = definition.SkillDirectories;
        }

        var manifest = new PluginManifest(
            definition.Name,
            parsedManifest?.Description ?? definition.Description,
            parsedManifest?.Version ?? definition.Version,
            parsedManifest?.Commands ?? [],
            parsedManifest?.Agents ?? [],
            skillDirectories,
            parsedManifest?.OutputStyles ?? [],
            parsedManifest?.HookFiles ?? [],
            parsedManifest?.Settings ?? ParsePluginSettings(definition.Settings),
            parsedManifest?.UserConfig);
        var hooks = MergeHooks(
            LoadPluginHooks(definition.RootPath, manifest, issues),
            definition.Hooks);
        var pluginSettings = LoadPluginSettings(definition.RootPath, manifest, issues) ?? ParsePluginSettings(definition.Settings);
        var pluginId = $"{definition.Name}@builtin";
        var enabled = IsBuiltInPluginEnabled(pluginId, definition.DefaultEnabled, settings);
        return new DiscoveredPlugin(
            pluginId,
            definition.Name,
            definition.RootPath,
            enabled,
            true,
            manifest,
            issues,
            hooks,
            pluginSettings,
            manifest.UserConfig);
    }

    private static PluginManifest ParsePluginManifest(string manifestPath, List<PluginValidationIssue> issues)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
            var root = document.RootElement;
            var name = TryGetString(root, "name") ?? Path.GetFileName(Path.GetDirectoryName(manifestPath) ?? manifestPath);
            var description = TryGetString(root, "description");
            var version = TryGetString(root, "version");
            var commands = ParseStringList(root, "commands", issues);
            var agents = ParseStringList(root, "agents", issues);
            var skills = ParseStringList(root, "skills", issues);
            var outputStyles = ParseStringList(root, "outputStyles", issues);
            var hookFiles = ParseHookFileList(root, issues);
            var pluginSettings = ParsePluginSettings(root);
            var userConfig = ParseUserConfig(root, issues);

            return new PluginManifest(name, description, version, commands, agents, skills, outputStyles, hookFiles, pluginSettings, userConfig);
        }
        catch (JsonException ex)
        {
            issues.Add(new PluginValidationIssue("plugin.json", ex.Message));
            return new PluginManifest(
                Path.GetFileName(Path.GetDirectoryName(manifestPath) ?? manifestPath),
                null,
                null,
                [],
                [],
                [],
                [],
                [],
                null,
                null);
        }
    }

    private static IReadOnlyDictionary<string, object?>? LoadPluginSettings(
        string installPath,
        PluginManifest? manifest,
        List<PluginValidationIssue> issues)
    {
        var settingsJsonPath = Path.Combine(installPath, "settings.json");
        if (File.Exists(settingsJsonPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(settingsJsonPath, Encoding.UTF8));
                var parsed = document.RootElement.ValueKind == JsonValueKind.Object
                    ? ParsePluginSettings(ParseJsonObject(document.RootElement))
                    : null;
                if (parsed is not null)
                {
                    return parsed;
                }
            }
            catch (JsonException ex)
            {
                issues.Add(new PluginValidationIssue("settings.json", ex.Message, IsWarning: true));
            }
        }

        return manifest?.Settings;
    }

    private static IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>> LoadPluginHooks(
        string installPath,
        PluginManifest? manifest,
        List<PluginValidationIssue> issues)
    {
        var combined = new Dictionary<HookEvent, List<HookMatcherDefinition>>();
        var defaultHookFile = Path.Combine(installPath, "hooks", "hooks.json");
        if (File.Exists(defaultHookFile))
        {
            MergeHookConfiguration(defaultHookFile, combined, issues, nestedPropertyName: "hooks");
        }

        if (manifest is not null)
        {
            foreach (var hookFile in manifest.HookFiles)
            {
                var hookPath = Path.GetFullPath(Path.Combine(installPath, hookFile));
                if (!IsWithinDirectory(hookPath, installPath))
                {
                    issues.Add(new PluginValidationIssue("hooks", $"Hook path '{hookFile}' escapes the plugin root."));
                    continue;
                }

                if (!File.Exists(hookPath))
                {
                    issues.Add(new PluginValidationIssue("hooks", $"Hook file '{hookPath}' was not found."));
                    continue;
                }

                MergeHookConfiguration(hookPath, combined, issues, nestedPropertyName: null);
            }
        }

        return combined.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<HookMatcherDefinition>)pair.Value,
            EqualityComparer<HookEvent>.Default);
    }

    private static void MergeHookConfiguration(
        string filePath,
        Dictionary<HookEvent, List<HookMatcherDefinition>> target,
        List<PluginValidationIssue> issues,
        string? nestedPropertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath, Encoding.UTF8));
            var root = nestedPropertyName is not null && document.RootElement.TryGetProperty(nestedPropertyName, out var nested)
                ? nested
                : document.RootElement;

            foreach (var pair in ParseHooks(root, issues))
            {
                target.TryAdd(pair.Key, []);
                target[pair.Key].AddRange(pair.Value);
            }
        }
        catch (JsonException ex)
        {
            issues.Add(new PluginValidationIssue(Path.GetFileName(filePath), ex.Message));
        }
    }

    private IReadOnlyList<DiscoveredSkill> LoadSkills(
        string workspaceRoot,
        StartupEnvironment startupEnvironment,
        IReadOnlyList<DiscoveredPlugin> plugins)
    {
        if (startupEnvironment.BareMode)
        {
            return [];
        }

        var discoveredSkills = new List<DiscoveredSkill>();
        var seenPaths = new HashSet<string>(GetPathComparer());

        if (!startupEnvironment.DisablePolicySkills)
        {
            AddSkillsFromDirectory(Path.Combine(_managedFilePath, ".claude", "skills"), "policySettings", discoveredSkills, seenPaths);
        }

        AddSkillsFromDirectory(Path.Combine(_userConfigHomeDir, "skills"), "userSettings", discoveredSkills, seenPaths);

        foreach (var projectSkillsDirectory in GetProjectSkillDirectoriesUpToHome(workspaceRoot))
        {
            AddSkillsFromDirectory(projectSkillsDirectory, "projectSettings", discoveredSkills, seenPaths);
        }

        if (!string.IsNullOrWhiteSpace(_bundledSkillsDirectoryPath))
        {
            AddSkillsFromDirectory(_bundledSkillsDirectoryPath, "bundled", discoveredSkills, seenPaths);
        }

        foreach (var plugin in plugins.Where(static plugin => plugin.Enabled))
        {
            AddSkillsFromDirectory(Path.Combine(plugin.InstallPath, "skills"), "plugin", discoveredSkills, seenPaths);
            if (plugin.Manifest is null)
            {
                continue;
            }

            foreach (var relativeSkillPath in plugin.Manifest.Skills)
            {
                var skillDirectory = Path.IsPathRooted(relativeSkillPath)
                    ? Path.GetFullPath(relativeSkillPath)
                    : Path.GetFullPath(Path.Combine(plugin.InstallPath, relativeSkillPath));
                if (IsWithinDirectory(skillDirectory, plugin.InstallPath))
                {
                    AddSkillsFromDirectory(skillDirectory, "plugin", discoveredSkills, seenPaths);
                }
                else if (Path.IsPathRooted(relativeSkillPath) && Directory.Exists(skillDirectory))
                {
                    AddSkillsFromDirectory(skillDirectory, "plugin", discoveredSkills, seenPaths);
                }
            }
        }

        return discoveredSkills;
    }

    private IReadOnlyList<HookDefinition> LoadHooks(
        ClawSharpSettings settings,
        IReadOnlyList<DiscoveredPlugin> plugins)
    {
        if (settings.DisableAllHooks)
        {
            return [];
        }

        var hooks = new List<HookDefinition>();
        AddHooks(hooks, settings.Hooks, "settings");

        foreach (var plugin in plugins.Where(static plugin => plugin.Enabled))
        {
            AddHooks(hooks, plugin.Hooks, "plugin", plugin.PluginId, plugin.InstallPath);
        }

        return hooks;
    }

    private static void AddHooks(
        List<HookDefinition> hooks,
        IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>> sourceHooks,
        string source,
        string? pluginId = null,
        string? pluginRoot = null)
    {
        foreach (var eventHooks in sourceHooks)
        {
            foreach (var matcher in eventHooks.Value)
            {
                foreach (var hook in matcher.Hooks)
                {
                    hooks.Add(new HookDefinition(eventHooks.Key, source, matcher.Matcher, hook, pluginId, pluginRoot));
                }
            }
        }
    }

    private void AddSkillsFromDirectory(
        string basePath,
        string source,
        List<DiscoveredSkill> discoveredSkills,
        HashSet<string> seenPaths)
    {
        if (!Directory.Exists(basePath))
        {
            return;
        }

        if (File.Exists(Path.Combine(basePath, "SKILL.md")))
        {
            AddSkillDirectory(basePath, source, discoveredSkills, seenPaths);
            return;
        }

        foreach (var entryPath in Directory.GetDirectories(basePath).OrderBy(static path => path, GetPathComparer()))
        {
            AddSkillDirectory(entryPath, source, discoveredSkills, seenPaths);
        }
    }

    private static void AddSkillDirectory(
        string entryPath,
        string source,
        List<DiscoveredSkill> discoveredSkills,
        HashSet<string> seenPaths)
    {
        var skillFilePath = Path.Combine(entryPath, "SKILL.md");
        if (!File.Exists(skillFilePath))
        {
            return;
        }

        var identity = Path.GetFullPath(skillFilePath);
        if (!seenPaths.Add(identity))
        {
            return;
        }

        discoveredSkills.Add(new DiscoveredSkill(Path.GetFileName(entryPath), identity, Path.GetFullPath(entryPath), source));
    }

    private IReadOnlyList<string> GetProjectSkillDirectoriesUpToHome(string workspaceRoot)
    {
        var homeDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
            .Normalize(NormalizationForm.FormC);
        var gitRoot = TryGetCanonicalGitRoot(workspaceRoot);
        var current = Path.GetFullPath(workspaceRoot);
        var directories = new List<string>();

        while (true)
        {
            if (PathsEqual(current, homeDirectory))
            {
                break;
            }

            var skillsDirectory = Path.Combine(current, ".claude", "skills");
            if (Directory.Exists(skillsDirectory))
            {
                directories.Add(skillsDirectory);
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

    private static IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>> ParseHooks(
        JsonElement root,
        List<PluginValidationIssue> issues)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>();
        }

        var hooks = new Dictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>();
        foreach (var eventProperty in root.EnumerateObject())
        {
            if (!Enum.TryParse<HookEvent>(eventProperty.Name, ignoreCase: true, out var hookEvent))
            {
                issues.Add(new PluginValidationIssue(eventProperty.Name, $"Unknown hook event '{eventProperty.Name}'.", IsWarning: true));
                continue;
            }

            if (eventProperty.Value.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new PluginValidationIssue(eventProperty.Name, "Hook matchers must be an array."));
                continue;
            }

            var matchers = new List<HookMatcherDefinition>();
            foreach (var matcherElement in eventProperty.Value.EnumerateArray())
            {
                if (matcherElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var matcher = TryGetString(matcherElement, "matcher");
                if (!matcherElement.TryGetProperty("hooks", out var hooksElement) || hooksElement.ValueKind != JsonValueKind.Array)
                {
                    issues.Add(new PluginValidationIssue(eventProperty.Name, "Each hook matcher must include a hooks array."));
                    continue;
                }

                var commands = new List<HookCommandDefinition>();
                foreach (var hookElement in hooksElement.EnumerateArray())
                {
                    try
                    {
                        var hook = JsonSerializer.Deserialize<HookCommandDefinition>(hookElement.GetRawText(), CreateHookSerializerOptions());
                        if (hook is null)
                        {
                            continue;
                        }

                        commands.Add(hook);
                    }
                    catch (JsonException ex)
                    {
                        issues.Add(new PluginValidationIssue(eventProperty.Name, ex.Message));
                    }
                }

                matchers.Add(new HookMatcherDefinition(matcher, commands));
            }

            hooks[hookEvent] = matchers;
        }

        return hooks;
    }

    private static IReadOnlyList<string> ParseStringList(JsonElement root, string propertyName, List<PluginValidationIssue> issues)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return [];
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => ValidateRelativePaths([property.GetString()!], propertyName, issues),
            JsonValueKind.Array => ValidateRelativePaths(
                property.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString()!)
                    .ToArray(),
                propertyName,
                issues),
            _ => []
        };
    }

    private static IReadOnlyList<string> ParseHookFileList(JsonElement root, List<PluginValidationIssue> issues)
    {
        if (!root.TryGetProperty("hooks", out var property))
        {
            return [];
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => ValidateRelativePaths([property.GetString()!], "hooks", issues),
            JsonValueKind.Array => ValidateRelativePaths(
                property.EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(static item => item.GetString()!)
                    .ToArray(),
                "hooks",
                issues),
            JsonValueKind.Object => [],
            _ => []
        };
    }

    private static IReadOnlyDictionary<string, object?>? ParsePluginSettings(JsonElement root)
    {
        if (!root.TryGetProperty("settings", out var settingsElement) || settingsElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return ParsePluginSettings(ParseJsonObject(settingsElement));
    }

    private static IReadOnlyDictionary<string, object?>? ParsePluginSettings(IReadOnlyDictionary<string, object?>? rawSettings)
    {
        if (rawSettings is null)
        {
            return null;
        }

        var filtered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in rawSettings)
        {
            if (!AllowedPluginSettingKeys.Contains(pair.Key))
            {
                continue;
            }

            filtered[pair.Key] = pair.Value;
        }

        return filtered.Count == 0 ? null : filtered;
    }

    private static IReadOnlyDictionary<string, PluginOptionDefinition>? ParseUserConfig(
        JsonElement root,
        List<PluginValidationIssue> issues)
    {
        if (!root.TryGetProperty("userConfig", out var userConfigElement) || userConfigElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var definitions = new Dictionary<string, PluginOptionDefinition>(StringComparer.Ordinal);
        foreach (var optionProperty in userConfigElement.EnumerateObject())
        {
            if (optionProperty.Value.ValueKind != JsonValueKind.Object)
            {
                issues.Add(new PluginValidationIssue("userConfig", $"Option '{optionProperty.Name}' must be an object."));
                continue;
            }

            if (!TryParseUserConfigOption(optionProperty.Name, optionProperty.Value, out var definition))
            {
                issues.Add(new PluginValidationIssue("userConfig", $"Option '{optionProperty.Name}' is invalid."));
                continue;
            }

            definitions[optionProperty.Name] = definition!;
        }

        return definitions.Count == 0 ? null : definitions;
    }

    private static bool TryParseUserConfigOption(
        string optionName,
        JsonElement optionElement,
        out PluginOptionDefinition? definition)
    {
        definition = null;
        var typeName = TryGetString(optionElement, "type");
        var title = TryGetString(optionElement, "title");
        var description = TryGetString(optionElement, "description");
        if (string.IsNullOrWhiteSpace(typeName) ||
            string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(description) ||
            !TryParseOptionType(typeName, out var optionType))
        {
            return false;
        }

        definition = new PluginOptionDefinition(
            optionType,
            title,
            description,
            Required: TryGetBoolean(optionElement, "required"),
            DefaultValue: TryGetObject(optionElement, "default"),
            Multiple: TryGetBoolean(optionElement, "multiple"),
            Sensitive: TryGetBoolean(optionElement, "sensitive"),
            Min: TryGetDouble(optionElement, "min"),
            Max: TryGetDouble(optionElement, "max"));
        return true;
    }

    private static bool TryParseOptionType(string value, out PluginOptionType optionType)
    {
        switch (value)
        {
            case "string":
                optionType = PluginOptionType.String;
                return true;
            case "number":
                optionType = PluginOptionType.Number;
                return true;
            case "boolean":
                optionType = PluginOptionType.Boolean;
                return true;
            case "directory":
                optionType = PluginOptionType.Directory;
                return true;
            case "file":
                optionType = PluginOptionType.File;
                return true;
            default:
                optionType = default;
                return false;
        }
    }

    private static IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>> MergeHooks(
        IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>> baseHooks,
        IReadOnlyDictionary<HookEvent, IReadOnlyList<HookMatcherDefinition>>? additionalHooks)
    {
        if (additionalHooks is null || additionalHooks.Count == 0)
        {
            return baseHooks;
        }

        var merged = baseHooks.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value,
            EqualityComparer<HookEvent>.Default);
        foreach (var pair in additionalHooks)
        {
            if (!merged.TryGetValue(pair.Key, out var existing))
            {
                merged[pair.Key] = pair.Value;
                continue;
            }

            merged[pair.Key] = existing.Concat(pair.Value).ToArray();
        }

        return merged;
    }

    private static IReadOnlyList<string> ValidateRelativePaths(
        IReadOnlyList<string> values,
        string propertyName,
        List<PluginValidationIssue> issues)
    {
        var accepted = new List<string>(values.Count);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.Contains("..", StringComparison.Ordinal))
            {
                issues.Add(new PluginValidationIssue(propertyName, $"Path contains '..': {value}."));
                continue;
            }

            accepted.Add(value);
        }

        return accepted;
    }

    private static string GetPluginNameFromIdentifier(string pluginId)
    {
        var separatorIndex = pluginId.LastIndexOf('@');
        return separatorIndex > 0 ? pluginId[..separatorIndex] : pluginId;
    }

    private static bool IsWithinDirectory(string candidatePath, string rootPath)
    {
        var fullCandidate = Path.GetFullPath(candidatePath).Normalize(NormalizationForm.FormC);
        var fullRoot = Path.GetFullPath(rootPath).Normalize(NormalizationForm.FormC);
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.Equals(fullRoot, GetPathComparison()) || fullCandidate.StartsWith(prefix, GetPathComparison());
    }

    private static PluginInstallationScope? TryParseScope(string? scope)
    {
        return scope switch
        {
            "managed" => PluginInstallationScope.Managed,
            "user" => PluginInstallationScope.User,
            "project" => PluginInstallationScope.Project,
            "local" => PluginInstallationScope.Local,
            _ => null
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return property.GetString();
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               property.GetBoolean();
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out var value)
            ? value
            : null;
    }

    private static object? TryGetObject(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? ConvertJsonValue(property)
            : null;
    }

    private static IReadOnlyDictionary<string, object?> ParseJsonObject(JsonElement element)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            result[property.Name] = ConvertJsonValue(property.Value);
        }

        return result;
    }

    private static object? ConvertJsonValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => ParseJsonObject(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToArray(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number when element.TryGetInt64(out var longValue) => longValue,
            JsonValueKind.Number when element.TryGetDouble(out var doubleValue) => doubleValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
    }

    private static bool IsBuiltInPluginEnabled(
        string pluginId,
        bool defaultEnabled,
        ClawSharpSettings settings)
    {
        if (!settings.EnabledPlugins.TryGetValue(pluginId, out var enabledSetting))
        {
            return defaultEnabled;
        }

        return enabledSetting.Enabled == true;
    }

    private static JsonSerializerOptions CreateHookSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new HookCommandDefinitionJsonConverter());
        return options;
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
        return string.IsNullOrWhiteSpace(output)
            ? null
            : Path.GetFullPath(output).Normalize(NormalizationForm.FormC);
    }

    private void AddVersion1Installation(List<DiscoveredPluginInstallation> installations, JsonProperty pluginProperty)
    {
        if (pluginProperty.Value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var version = TryGetString(pluginProperty.Value, "version");
        if (string.IsNullOrWhiteSpace(version))
        {
            return;
        }

        installations.Add(new DiscoveredPluginInstallation(
            pluginProperty.Name,
            PluginInstallationScope.User,
            ClaudeConfigPaths.GetVersionedPluginCachePath(_pluginsDirectoryPath, pluginProperty.Name, version),
            ProjectPath: null,
            Version: version,
            InstalledAt: TryGetString(pluginProperty.Value, "installedAt"),
            LastUpdated: TryGetString(pluginProperty.Value, "lastUpdated"),
            GitCommitSha: TryGetString(pluginProperty.Value, "gitCommitSha")));
    }

    private static void AddVersion2Installations(List<DiscoveredPluginInstallation> installations, JsonProperty pluginProperty)
    {
        if (pluginProperty.Value.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var installationElement in pluginProperty.Value.EnumerateArray())
        {
            if (installationElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var scope = TryParseScope(TryGetString(installationElement, "scope"));
            var installPath = TryGetString(installationElement, "installPath");
            if (scope is null || string.IsNullOrWhiteSpace(installPath))
            {
                continue;
            }

            installations.Add(new DiscoveredPluginInstallation(
                pluginProperty.Name,
                scope.Value,
                installPath,
                ProjectPath: TryGetString(installationElement, "projectPath"),
                Version: TryGetString(installationElement, "version"),
                InstalledAt: TryGetString(installationElement, "installedAt"),
                LastUpdated: TryGetString(installationElement, "lastUpdated"),
                GitCommitSha: TryGetString(installationElement, "gitCommitSha")));
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).Normalize(NormalizationForm.FormC),
            Path.GetFullPath(right).Normalize(NormalizationForm.FormC),
            GetPathComparison());
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

public sealed record ExtensionBootstrapResult(
    IReadOnlyList<DiscoveredPluginInstallation> PluginInstallations,
    IReadOnlyList<DiscoveredPlugin> Plugins,
    IReadOnlyList<DiscoveredSkill> Skills,
    IReadOnlyList<HookDefinition> Hooks);
