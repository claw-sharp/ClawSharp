using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

public sealed class ConfigTool : BaseTool
{
    public const string ToolName = "Config";

    public ConfigTool()
        : base(new ToolDescriptor(
            ToolName,
            "Get or set ClawSharp settings (model, iterations, permissions).",
            SearchHint: "get or set ClawSharp settings",
            ShouldDefer: true,
            InputSchema: new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["setting"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The setting key (e.g., \"Runtime.Model\", \"Permissions.DefaultMode\")"
                    },
                    ["value"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "The new value. Omit to get current value."
                    }
                },
                ["required"] = new JsonArray("setting"),
                ["additionalProperties"] = false
            },
            OutputSchema: new JsonObject
            {
                ["type"] = "object"
            },
            Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;
    public override bool IsReadOnly(string arguments)
    {
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            return !doc.RootElement.TryGetProperty("value", out _);
        }
        catch
        {
            return true;
        }
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var setting, out var value))
        {
            return Failure("Invalid input.");
        }

        if (context.SettingsStore == null)
        {
            return Failure("Settings store is not available.");
        }

        var settings = await context.SettingsStore.LoadAsync(cancellationToken);

        if (value == null)
        {
            // GET
            var currentValue = GetSettingValue(settings, setting);
            if (currentValue == null && !IsKnownSetting(setting))
            {
                return Failure($"Unknown setting: \"{setting}\"");
            }

            return Success($"{setting} = {currentValue ?? "null"}", new JsonObject
            {
                ["success"] = true,
                ["operation"] = "get",
                ["setting"] = setting,
                ["value"] = currentValue
            });
        }

        // SET
        // For now, we only support a few settings to avoid complex reflection/mapping
        if (!IsKnownSetting(setting))
        {
            return Failure($"Unknown or unsupported setting: \"{setting}\"");
        }

        try
        {
            var newSettings = ApplySetting(settings, setting, value);
            await context.SettingsStore.SaveAsync(newSettings, cancellationToken);

            return Success($"Set {setting} to {value}", new JsonObject
            {
                ["success"] = true,
                ["operation"] = "set",
                ["setting"] = setting,
                ["newValue"] = value
            });
        }
        catch (Exception ex)
        {
            return Failure($"Failed to set {setting}: {ex.Message}");
        }
    }

    private static bool IsKnownSetting(string setting)
    {
        return setting switch
        {
            "Runtime.Model" => true,
            "Permissions.DefaultMode" => true,
            "ClaudeApiKey" => true,
            _ => false
        };
    }

    private static string? GetSettingValue(ClawSharpSettings settings, string setting)
    {
        return setting switch
        {
            "Runtime.Model" => settings.Runtime.Model,
            "Permissions.DefaultMode" => settings.Permissions.DefaultMode?.ToString(),
            "ClaudeApiKey" => settings.ClaudeApiKey != null ? "****" : null,
            _ => null
        };
    }

    private static ClawSharpSettings ApplySetting(ClawSharpSettings settings, string setting, string value)
    {
        switch (setting)
        {
            case "Runtime.Model":
                return CloneSettings(settings, CloneRuntimeSettings(settings.Runtime, model: value));
            case "Permissions.DefaultMode":
                if (!Enum.TryParse<PermissionMode>(value, ignoreCase: true, out var mode))
                {
                    throw new ArgumentException("Value must be a valid permission mode.");
                }

                return CloneSettings(settings, settings.Runtime, permissionDefaultMode: mode);
            case "ClaudeApiKey":
                return CloneSettings(settings, settings.Runtime, claudeApiKeyOverride: value);
            default:
                throw new ArgumentException($"Unsupported setting: {setting}");
        }
    }

    private static RuntimeSettings CloneRuntimeSettings(
        RuntimeSettings source,
        string? model = null)
    {
        return new RuntimeSettings
        {
            PermissionMode = source.PermissionMode,
            Model = model ?? source.Model,
            FallbackModel = source.FallbackModel,
            EnableTelemetry = source.EnableTelemetry,
            FileCheckpointingEnabled = source.FileCheckpointingEnabled,
            AutoMemoryEnabled = source.AutoMemoryEnabled,
            AutoMemoryDirectory = source.AutoMemoryDirectory
        };
    }

    private static ClawSharpSettings CloneSettings(
        ClawSharpSettings source,
        RuntimeSettings runtime,
        string? claudeApiKeyOverride = null,
        PermissionMode? permissionDefaultMode = null)
    {
        return new ClawSharpSettings
        {
            Runtime = runtime,
            Terminal = source.Terminal,
            Sandbox = source.Sandbox,
            ClaudeApiKey = claudeApiKeyOverride ?? source.ClaudeApiKey,
            SkipAutoPermissionPrompt = source.SkipAutoPermissionPrompt,
            UseAutoModeDuringPlan = source.UseAutoModeDuringPlan,
            ApiKeyHelper = source.ApiKeyHelper,
            AwsCredentialExport = source.AwsCredentialExport,
            AwsAuthRefresh = source.AwsAuthRefresh,
            Agent = source.Agent,
            Attribution = source.Attribution,
            Permissions = new PermissionSettings
            {
                Allow = source.Permissions.Allow,
                Deny = source.Permissions.Deny,
                Ask = source.Permissions.Ask,
                DefaultMode = permissionDefaultMode ?? source.Permissions.DefaultMode,
                DisableBypassPermissionsMode = source.Permissions.DisableBypassPermissionsMode,
                DisableAutoMode = source.Permissions.DisableAutoMode,
                AdditionalDirectories = source.Permissions.AdditionalDirectories
            },
            AllowManagedPermissionRulesOnly = source.AllowManagedPermissionRulesOnly,
            Hooks = source.Hooks,
            DisableAllHooks = source.DisableAllHooks,
            AllowManagedHooksOnly = source.AllowManagedHooksOnly,
            ForceLoginOrgUUID = source.ForceLoginOrgUUID,
            OtelHeadersHelper = source.OtelHeadersHelper,
            EnabledPlugins = source.EnabledPlugins,
            PluginConfigs = source.PluginConfigs,
            AgentModels = source.AgentModels,
            AgentRouting = source.AgentRouting
        };
    }

    private static bool TryParseArguments(string arguments, out string setting, out string? value)
    {
        setting = string.Empty;
        value = null;
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.TryGetProperty("setting", out var sProp))
            {
                setting = sProp.GetString() ?? string.Empty;
            }
            if (doc.RootElement.TryGetProperty("value", out var vProp))
            {
                value = vProp.GetString();
            }
            return !string.IsNullOrEmpty(setting);
        }
        catch
        {
            return false;
        }
    }
}
