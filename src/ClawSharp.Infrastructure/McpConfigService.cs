// TS origin: ./services/mcp/types.ts, ./services/mcp/config.ts, ./services/mcp/utils.ts, ./services/mcp/envExpansion.ts, ./utils/config.ts, ./utils/env.ts
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class McpConfigService
{
    private readonly string _workspaceRoot;
    private readonly string _globalClaudeFilePath;
    private readonly string _enterpriseMcpFilePath;

    public McpConfigService(
        string workspaceRoot,
        string? globalClaudeFilePath = null,
        string? enterpriseMcpFilePath = null)
    {
        _workspaceRoot = workspaceRoot;
        _globalClaudeFilePath = globalClaudeFilePath ?? ClaudeConfigPaths.GetGlobalClaudeFilePath();
        _enterpriseMcpFilePath = enterpriseMcpFilePath ?? ClaudeConfigPaths.GetEnterpriseMcpFilePath();
    }

    public static McpConfigScope EnsureConfigScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return McpConfigScope.Local;
        }

        return scope.Trim().ToLowerInvariant() switch
        {
            "local" => McpConfigScope.Local,
            "user" => McpConfigScope.User,
            "project" => McpConfigScope.Project,
            "dynamic" => McpConfigScope.Dynamic,
            "enterprise" => McpConfigScope.Enterprise,
            "claudeai" => McpConfigScope.ClaudeAi,
            "managed" => McpConfigScope.Managed,
            _ => throw new InvalidOperationException(
                $"Invalid scope: {scope}. Must be one of: local, user, project, dynamic, enterprise, claudeai, managed")
        };
    }

    public static string DescribeMcpConfigFilePath(McpConfigScope scope, string workspaceRoot)
    {
        return scope switch
        {
            McpConfigScope.User => ClaudeConfigPaths.GetGlobalClaudeFilePath(),
            McpConfigScope.Project => Path.Combine(workspaceRoot, ".mcp.json"),
            McpConfigScope.Local => $"{ClaudeConfigPaths.GetGlobalClaudeFilePath()} [project: {workspaceRoot}]",
            McpConfigScope.Dynamic => "Dynamically configured",
            McpConfigScope.Enterprise => ClaudeConfigPaths.GetEnterpriseMcpFilePath(),
            McpConfigScope.ClaudeAi => "claude.ai",
            _ => scope.ToString()
        };
    }

    public static string GetScopeLabel(McpConfigScope scope)
    {
        return scope switch
        {
            McpConfigScope.Local => "Local config (private to you in this project)",
            McpConfigScope.Project => "Project config (shared via .mcp.json)",
            McpConfigScope.User => "User config (available in all your projects)",
            McpConfigScope.Dynamic => "Dynamic config (from command line)",
            McpConfigScope.Enterprise => "Enterprise config (managed by your organization)",
            McpConfigScope.ClaudeAi => "claude.ai config",
            _ => scope.ToString()
        };
    }

    public (McpJsonConfig? Config, IReadOnlyList<McpConfigError> Errors) ParseConfig(
        object? configObject,
        bool expandVars,
        McpConfigScope scope,
        string? filePath = null)
    {
        if (configObject is null)
        {
            return (null, [CreateSchemaError(filePath, string.Empty, scope)]);
        }

        JsonElement rootElement;
        if (configObject is JsonElement jsonElement)
        {
            rootElement = jsonElement;
        }
        else
        {
            rootElement = JsonSerializer.SerializeToElement(configObject);
        }

        return ParseConfigElement(rootElement, expandVars, scope, filePath);
    }

    public (McpJsonConfig? Config, IReadOnlyList<McpConfigError> Errors) ParseConfigFromFilePath(
        string filePath,
        bool expandVars,
        McpConfigScope scope)
    {
        string configContent;
        try
        {
            configContent = File.ReadAllText(filePath, Encoding.UTF8);
        }
        catch (FileNotFoundException)
        {
            return (null, [new McpConfigError(
                filePath,
                string.Empty,
                $"MCP config file not found: {filePath}",
                "Check that the file path is correct",
                new McpConfigErrorMetadata(scope, null, McpConfigErrorSeverity.Fatal))]);
        }
        catch (DirectoryNotFoundException)
        {
            return (null, [new McpConfigError(
                filePath,
                string.Empty,
                $"MCP config file not found: {filePath}",
                "Check that the file path is correct",
                new McpConfigErrorMetadata(scope, null, McpConfigErrorSeverity.Fatal))]);
        }
        catch (Exception exception)
        {
            return (null, [new McpConfigError(
                filePath,
                string.Empty,
                $"Failed to read file: {exception.Message}",
                "Check file permissions and ensure the file exists",
                new McpConfigErrorMetadata(scope, null, McpConfigErrorSeverity.Fatal))]);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(configContent);
        }
        catch (JsonException)
        {
            return (null, [new McpConfigError(
                filePath,
                string.Empty,
                "MCP config is not a valid JSON",
                "Fix the JSON syntax errors in the file",
                new McpConfigErrorMetadata(scope, null, McpConfigErrorSeverity.Fatal))]);
        }

        using (document)
        {
            return ParseConfigElement(document.RootElement, expandVars, scope, filePath);
        }
    }

    public (IReadOnlyDictionary<string, ScopedMcpServerConfig> Servers, IReadOnlyList<McpConfigError> Errors) GetProjectMcpConfigsFromCurrentDirectory()
    {
        var projectConfigPath = Path.Combine(_workspaceRoot, ".mcp.json");
        var (config, errors) = ParseConfigFromFilePath(projectConfigPath, expandVars: true, McpConfigScope.Project);
        if (config is null)
        {
            return (new Dictionary<string, ScopedMcpServerConfig>(), errors.Where(error => !error.Message.StartsWith("MCP config file not found:", StringComparison.Ordinal)).ToArray());
        }

        return (AddScopeToServers(config.McpServers, McpConfigScope.Project), errors);
    }

    public (IReadOnlyDictionary<string, ScopedMcpServerConfig> Servers, IReadOnlyList<McpConfigError> Errors) GetConfigsByScope(McpConfigScope scope)
    {
        return scope switch
        {
            McpConfigScope.Project => GetProjectScopedConfigs(),
            McpConfigScope.User => GetGlobalScopeServers(McpConfigScope.User, GetUserMcpServers),
            McpConfigScope.Local => GetGlobalScopeServers(McpConfigScope.Local, GetCurrentProjectConfigDocument),
            McpConfigScope.Enterprise => GetEnterpriseScopedConfigs(),
            _ => (new Dictionary<string, ScopedMcpServerConfig>(), Array.Empty<McpConfigError>())
        };
    }

    public ScopedMcpServerConfig? GetConfigByName(string name)
    {
        var enterpriseServers = GetConfigsByScope(McpConfigScope.Enterprise).Servers;
        var userServers = GetConfigsByScope(McpConfigScope.User).Servers;
        var projectServers = GetConfigsByScope(McpConfigScope.Project).Servers;
        var localServers = GetConfigsByScope(McpConfigScope.Local).Servers;

        if (enterpriseServers.TryGetValue(name, out var enterpriseConfig))
        {
            return enterpriseConfig;
        }

        if (localServers.TryGetValue(name, out var localConfig))
        {
            return localConfig;
        }

        if (projectServers.TryGetValue(name, out var projectConfig))
        {
            return projectConfig;
        }

        return userServers.TryGetValue(name, out var userConfig) ? userConfig : null;
    }

    private (IReadOnlyDictionary<string, ScopedMcpServerConfig> Servers, IReadOnlyList<McpConfigError> Errors) GetProjectScopedConfigs()
    {
        var allServers = new Dictionary<string, ScopedMcpServerConfig>(GetPathComparer());
        var allErrors = new List<McpConfigError>();
        var currentDirectory = Path.GetFullPath(_workspaceRoot);
        var root = Path.GetPathRoot(currentDirectory);
        if (string.IsNullOrWhiteSpace(root))
        {
            return (allServers, allErrors);
        }

        var directories = new List<string>();
        while (!PathEquals(currentDirectory, root))
        {
            directories.Add(currentDirectory);
            var parentDirectory = Directory.GetParent(currentDirectory);
            if (parentDirectory is null)
            {
                break;
            }

            currentDirectory = parentDirectory.FullName;
        }

        directories.Reverse();
        foreach (var directory in directories)
        {
            var projectConfigPath = Path.Combine(directory, ".mcp.json");
            var (config, errors) = ParseConfigFromFilePath(projectConfigPath, expandVars: true, McpConfigScope.Project);
            if (config is null)
            {
                allErrors.AddRange(errors.Where(error => !error.Message.StartsWith("MCP config file not found:", StringComparison.Ordinal)));
                continue;
            }

            foreach (var (name, configValue) in AddScopeToServers(config.McpServers, McpConfigScope.Project))
            {
                allServers[name] = configValue;
            }

            allErrors.AddRange(errors);
        }

        return (allServers, allErrors);
    }

    private (IReadOnlyDictionary<string, ScopedMcpServerConfig> Servers, IReadOnlyList<McpConfigError> Errors) GetGlobalScopeServers(
        McpConfigScope scope,
        Func<JsonElement, JsonElement?> selector)
    {
        var globalConfigRoot = TryReadGlobalConfigRootElement();
        if (globalConfigRoot is null)
        {
            return (new Dictionary<string, ScopedMcpServerConfig>(), Array.Empty<McpConfigError>());
        }

        var serversElement = selector(globalConfigRoot.Value);
        if (serversElement is null || serversElement.Value.ValueKind != JsonValueKind.Object)
        {
            return (new Dictionary<string, ScopedMcpServerConfig>(), Array.Empty<McpConfigError>());
        }

        var (config, errors) = ParseConfig(WrapMcpServersElement(serversElement.Value), expandVars: true, scope);
        return (AddScopeToServers(config?.McpServers, scope), errors);
    }

    private (IReadOnlyDictionary<string, ScopedMcpServerConfig> Servers, IReadOnlyList<McpConfigError> Errors) GetEnterpriseScopedConfigs()
    {
        var (config, errors) = ParseConfigFromFilePath(_enterpriseMcpFilePath, expandVars: true, McpConfigScope.Enterprise);
        if (config is null)
        {
            return (new Dictionary<string, ScopedMcpServerConfig>(), errors.Where(error => !error.Message.StartsWith("MCP config file not found:", StringComparison.Ordinal)).ToArray());
        }

        return (AddScopeToServers(config.McpServers, McpConfigScope.Enterprise), errors);
    }

    private JsonElement? TryReadGlobalConfigRootElement()
    {
        if (!File.Exists(_globalClaudeFilePath))
        {
            return null;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(_globalClaudeFilePath, Encoding.UTF8));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return document.RootElement.Clone();
    }

    private JsonElement? GetUserMcpServers(JsonElement globalConfig)
    {
        return globalConfig.TryGetProperty("mcpServers", out var mcpServersElement) &&
               mcpServersElement.ValueKind == JsonValueKind.Object
            ? mcpServersElement.Clone()
            : null;
    }

    private JsonElement? GetCurrentProjectConfigDocument(JsonElement globalConfig)
    {
        var projectKey = GetProjectPathForConfig();
        if (!globalConfig.TryGetProperty("projects", out var projectsElement) ||
            projectsElement.ValueKind != JsonValueKind.Object ||
            !projectsElement.TryGetProperty(projectKey, out var projectElement) ||
            projectElement.ValueKind != JsonValueKind.Object ||
            !projectElement.TryGetProperty("mcpServers", out var mcpServersElement) ||
            mcpServersElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return mcpServersElement.Clone();
    }

    private string GetProjectPathForConfig()
    {
        var gitRoot = TryGetCanonicalGitRoot(_workspaceRoot);
        var path = gitRoot ?? Path.GetFullPath(_workspaceRoot);
        return ClaudeConfigPaths.NormalizeProjectPathForConfigKey(path);
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

    private static (McpJsonConfig? Config, IReadOnlyList<McpConfigError> Errors) ParseConfigElement(
        JsonElement rootElement,
        bool expandVars,
        McpConfigScope scope,
        string? filePath)
    {
        if (rootElement.ValueKind != JsonValueKind.Object ||
            !rootElement.TryGetProperty("mcpServers", out var mcpServersElement) ||
            mcpServersElement.ValueKind != JsonValueKind.Object)
        {
            return (null, [CreateSchemaError(filePath, string.Empty, scope)]);
        }

        var schemaErrors = new List<McpConfigError>();
        var servers = new Dictionary<string, McpServerConfig>(GetPathComparer());
        foreach (var serverProperty in mcpServersElement.EnumerateObject())
        {
            var parsed = TryParseServerConfig(serverProperty.Value);
            if (parsed is null)
            {
                schemaErrors.Add(CreateSchemaError(filePath, $"mcpServers.{serverProperty.Name}", scope, serverProperty.Name));
                continue;
            }

            servers[serverProperty.Name] = parsed;
        }

        if (schemaErrors.Count > 0)
        {
            return (null, schemaErrors);
        }

        var errors = new List<McpConfigError>();
        var expandedServers = new Dictionary<string, McpServerConfig>(GetPathComparer());
        foreach (var (name, config) in servers)
        {
            var configToUse = config;
            if (expandVars)
            {
                var (expanded, missingVars) = ExpandEnvVars(config);
                configToUse = expanded;
                if (missingVars.Count > 0)
                {
                    errors.Add(new McpConfigError(
                        filePath,
                        $"mcpServers.{name}",
                        $"Missing environment variables: {string.Join(", ", missingVars)}",
                        $"Set the following environment variables: {string.Join(", ", missingVars)}",
                        new McpConfigErrorMetadata(scope, name, McpConfigErrorSeverity.Warning)));
                }
            }

            if (OperatingSystem.IsWindows() &&
                configToUse is McpStdioServerConfig stdioConfig &&
                IsWindowsNpxWithoutCmdWrapper(stdioConfig.Command))
            {
                errors.Add(new McpConfigError(
                    filePath,
                    $"mcpServers.{name}",
                    "Windows requires 'cmd /c' wrapper to execute npx",
                    "Change command to \"cmd\" with args [\"/c\", \"npx\", ...]. See: https://code.claude.com/docs/en/mcp#configure-mcp-servers",
                    new McpConfigErrorMetadata(scope, name, McpConfigErrorSeverity.Warning)));
            }

            expandedServers[name] = configToUse;
        }

        return (new McpJsonConfig(expandedServers), errors);
    }

    private static McpConfigError CreateSchemaError(string? filePath, string path, McpConfigScope scope, string? serverName = null)
    {
        return new McpConfigError(
            filePath,
            path,
            "Does not adhere to MCP server configuration schema",
            null,
            new McpConfigErrorMetadata(scope, serverName, McpConfigErrorSeverity.Fatal));
    }

    private static McpServerConfig? TryParseServerConfig(JsonElement configElement)
    {
        if (configElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var type = configElement.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String
            ? typeElement.GetString()
            : "stdio";

        return type switch
        {
            "stdio" => TryParseStdioConfig(configElement),
            "sse" => TryParseSseConfig(configElement),
            "sse-ide" => TryParseSseIdeConfig(configElement),
            "ws-ide" => TryParseWebSocketIdeConfig(configElement),
            "http" => TryParseHttpConfig(configElement),
            "ws" => TryParseWebSocketConfig(configElement),
            "sdk" => TryParseSdkConfig(configElement),
            "claudeai-proxy" => TryParseClaudeAiProxyConfig(configElement),
            _ => null
        };
    }

    private static McpServerConfig? TryParseStdioConfig(JsonElement configElement)
    {
        if (!TryGetRequiredString(configElement, "command", out var command))
        {
            return null;
        }

        if (!TryGetStringArray(configElement, "args", out var args, allowMissing: true))
        {
            return null;
        }

        if (!TryGetStringDictionary(configElement, "env", out var env, allowMissing: true))
        {
            return null;
        }

        return new McpStdioServerConfig(command!, args ?? [], env);
    }

    private static McpServerConfig? TryParseSseConfig(JsonElement configElement)
    {
        return TryParseRemoteConfig(configElement, static (url, headers, headersHelper, oauth) => new McpSseServerConfig(url, headers, headersHelper, oauth));
    }

    private static McpServerConfig? TryParseHttpConfig(JsonElement configElement)
    {
        return TryParseRemoteConfig(configElement, static (url, headers, headersHelper, oauth) => new McpHttpServerConfig(url, headers, headersHelper, oauth));
    }

    private static McpServerConfig? TryParseRemoteConfig(
        JsonElement configElement,
        Func<string, IReadOnlyDictionary<string, string>?, string?, McpOAuthConfig?, McpServerConfig> factory)
    {
        if (!TryGetRequiredString(configElement, "url", out var url) ||
            !TryGetStringDictionary(configElement, "headers", out var headers, allowMissing: true))
        {
            return null;
        }

        return factory(url!, headers, TryGetOptionalString(configElement, "headersHelper"), TryParseOAuth(configElement));
    }

    private static McpServerConfig? TryParseWebSocketConfig(JsonElement configElement)
    {
        if (!TryGetRequiredString(configElement, "url", out var url) ||
            !TryGetStringDictionary(configElement, "headers", out var headers, allowMissing: true))
        {
            return null;
        }

        return new McpWebSocketServerConfig(url!, headers, TryGetOptionalString(configElement, "headersHelper"));
    }

    private static McpServerConfig? TryParseSseIdeConfig(JsonElement configElement)
    {
        if (!TryGetRequiredString(configElement, "url", out var url) ||
            !TryGetRequiredString(configElement, "ideName", out var ideName))
        {
            return null;
        }

        return new McpSseIdeServerConfig(url!, ideName!, TryGetOptionalBoolean(configElement, "ideRunningInWindows"));
    }

    private static McpServerConfig? TryParseWebSocketIdeConfig(JsonElement configElement)
    {
        if (!TryGetRequiredString(configElement, "url", out var url) ||
            !TryGetRequiredString(configElement, "ideName", out var ideName))
        {
            return null;
        }

        return new McpWebSocketIdeServerConfig(
            url!,
            ideName!,
            TryGetOptionalString(configElement, "authToken"),
            TryGetOptionalBoolean(configElement, "ideRunningInWindows"));
    }

    private static McpServerConfig? TryParseSdkConfig(JsonElement configElement)
    {
        return !TryGetRequiredString(configElement, "name", out var name)
            ? null
            : new McpSdkServerConfig(name!);
    }

    private static McpServerConfig? TryParseClaudeAiProxyConfig(JsonElement configElement)
    {
        return !TryGetRequiredString(configElement, "url", out var url) ||
               !TryGetRequiredString(configElement, "id", out var id)
            ? null
            : new McpClaudeAiProxyServerConfig(url!, id!);
    }

    private static McpOAuthConfig? TryParseOAuth(JsonElement configElement)
    {
        if (!configElement.TryGetProperty("oauth", out var oauthElement) || oauthElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new McpOAuthConfig(
            TryGetOptionalString(oauthElement, "clientId"),
            TryGetOptionalInt32(oauthElement, "callbackPort"),
            TryGetOptionalString(oauthElement, "authServerMetadataUrl"),
            TryGetOptionalBoolean(oauthElement, "xaa"));
    }

    private static (McpServerConfig Expanded, IReadOnlyList<string> MissingVars) ExpandEnvVars(McpServerConfig config)
    {
        var missingVars = new HashSet<string>(StringComparer.Ordinal);

        string ExpandString(string value)
        {
            var (expanded, missing) = ExpandEnvVarsInString(value);
            foreach (var variable in missing)
            {
                missingVars.Add(variable);
            }

            return expanded;
        }

        McpServerConfig expanded = config switch
        {
            McpStdioServerConfig stdioConfig => stdioConfig with
            {
                Command = ExpandString(stdioConfig.Command),
                Args = stdioConfig.Args.Select(ExpandString).ToArray(),
                Env = stdioConfig.Env?.ToDictionary(pair => pair.Key, pair => ExpandString(pair.Value), StringComparer.Ordinal)
            },
            McpSseServerConfig sseConfig => sseConfig with
            {
                Url = ExpandString(sseConfig.Url),
                Headers = ExpandDictionaryValues(sseConfig.Headers, ExpandString)
            },
            McpHttpServerConfig httpConfig => httpConfig with
            {
                Url = ExpandString(httpConfig.Url),
                Headers = ExpandDictionaryValues(httpConfig.Headers, ExpandString)
            },
            McpWebSocketServerConfig webSocketConfig => webSocketConfig with
            {
                Url = ExpandString(webSocketConfig.Url),
                Headers = ExpandDictionaryValues(webSocketConfig.Headers, ExpandString)
            },
            _ => config
        };

        return (expanded, missingVars.ToArray());
    }

    private static IReadOnlyDictionary<string, string>? ExpandDictionaryValues(
        IReadOnlyDictionary<string, string>? values,
        Func<string, string> expander)
    {
        return values?.ToDictionary(pair => pair.Key, pair => expander(pair.Value), StringComparer.Ordinal);
    }

    private static (string Expanded, IReadOnlyList<string> MissingVars) ExpandEnvVarsInString(string value)
    {
        var missingVars = new List<string>();
        var builder = new StringBuilder();
        for (var index = 0; index < value.Length;)
        {
            if (index + 1 < value.Length && value[index] == '$' && value[index + 1] == '{')
            {
                var endIndex = value.IndexOf('}', index + 2);
                if (endIndex < 0)
                {
                    builder.Append(value[index]);
                    index++;
                    continue;
                }

                var variableContent = value[(index + 2)..endIndex];
                var splitIndex = variableContent.IndexOf(":-", StringComparison.Ordinal);
                var variableName = splitIndex >= 0 ? variableContent[..splitIndex] : variableContent;
                var defaultValue = splitIndex >= 0 ? variableContent[(splitIndex + 2)..] : null;
                var envValue = Environment.GetEnvironmentVariable(variableName);
                if (envValue is not null)
                {
                    builder.Append(envValue);
                }
                else if (defaultValue is not null)
                {
                    builder.Append(defaultValue);
                }
                else
                {
                    missingVars.Add(variableName);
                    builder.Append(value[index..(endIndex + 1)]);
                }

                index = endIndex + 1;
                continue;
            }

            builder.Append(value[index]);
            index++;
        }

        return (builder.ToString(), missingVars.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static bool IsWindowsNpxWithoutCmdWrapper(string command)
    {
        return string.Equals(command, "npx", StringComparison.Ordinal) ||
               command.EndsWith(@"\npx", StringComparison.Ordinal) ||
               command.EndsWith("/npx", StringComparison.Ordinal);
    }

    private static Dictionary<string, ScopedMcpServerConfig> AddScopeToServers(
        IReadOnlyDictionary<string, McpServerConfig>? servers,
        McpConfigScope scope)
    {
        var scopedServers = new Dictionary<string, ScopedMcpServerConfig>(GetPathComparer());
        if (servers is null)
        {
            return scopedServers;
        }

        foreach (var (name, config) in servers)
        {
            scopedServers[name] = new ScopedMcpServerConfig(name, config, scope);
        }

        return scopedServers;
    }

    private static JsonElement WrapMcpServersElement(JsonElement serversElement)
    {
        using var wrappedDocument = JsonDocument.Parse($$"""{"mcpServers":{{serversElement.GetRawText()}}}""");
        return wrappedDocument.RootElement.Clone();
    }

    private static bool TryGetRequiredString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        return element.TryGetProperty(propertyName, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.String &&
               !string.IsNullOrEmpty(value = propertyElement.GetString());
    }

    private static string? TryGetOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var propertyElement) && propertyElement.ValueKind == JsonValueKind.String
            ? propertyElement.GetString()
            : null;
    }

    private static int? TryGetOptionalInt32(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var propertyElement) &&
               propertyElement.ValueKind == JsonValueKind.Number &&
               propertyElement.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool? TryGetOptionalBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var propertyElement) &&
               (propertyElement.ValueKind == JsonValueKind.True || propertyElement.ValueKind == JsonValueKind.False)
            ? propertyElement.GetBoolean()
            : null;
    }

    private static bool TryGetStringArray(JsonElement element, string propertyName, out IReadOnlyList<string>? values, bool allowMissing)
    {
        values = null;
        if (!element.TryGetProperty(propertyName, out var propertyElement))
        {
            return allowMissing;
        }

        if (propertyElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var list = new List<string>();
        foreach (var item in propertyElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            list.Add(item.GetString()!);
        }

        values = list;
        return true;
    }

    private static bool TryGetStringDictionary(JsonElement element, string propertyName, out IReadOnlyDictionary<string, string>? values, bool allowMissing)
    {
        values = null;
        if (!element.TryGetProperty(propertyName, out var propertyElement))
        {
            return allowMissing;
        }

        if (propertyElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var dictionary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in propertyElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            dictionary[property.Name] = property.Value.GetString()!;
        }

        values = dictionary;
        return true;
    }

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
