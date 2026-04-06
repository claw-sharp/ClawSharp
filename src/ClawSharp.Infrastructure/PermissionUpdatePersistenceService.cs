// TS origin: ./utils/permissions/PermissionUpdate.ts, ./utils/permissions/permissionsLoader.ts, ./utils/settings/settings.ts
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class PermissionUpdatePersistenceService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        }
    };

    public bool PersistPermissionUpdate(
        string workspaceRoot,
        ClawSharpSettings settings,
        PermissionUpdate update)
    {
        if (!PermissionUpdateApplier.SupportsPersistence(update.Destination))
        {
            return false;
        }

        var filePath = GetSettingsFilePath(workspaceRoot, update.Destination);
        var root = LoadEditableSettings(filePath);
        var permissions = GetOrCreateObject(root, "permissions");

        switch (update)
        {
            case AddPermissionRulesUpdate addRules:
                return PersistAddRules(settings, root, permissions, filePath, addRules);
            case ReplacePermissionRulesUpdate replaceRules:
                SetRules(permissions, replaceRules.Behavior, replaceRules.Rules.Select(PermissionRuleParser.PermissionRuleValueToString));
                Save(filePath, root);
                return true;
            case RemovePermissionRulesUpdate removeRules:
                RemoveRules(permissions, removeRules.Behavior, removeRules.Rules);
                Save(filePath, root);
                return true;
            case SetPermissionModeUpdate setMode:
                permissions["defaultMode"] = JsonSerializer.SerializeToNode(setMode.Mode, SerializerOptions);
                Save(filePath, root);
                return true;
            case AddPermissionDirectoriesUpdate addDirectories:
                AddDirectories(permissions, addDirectories.Directories);
                Save(filePath, root);
                return true;
            case RemovePermissionDirectoriesUpdate removeDirectories:
                RemoveDirectories(permissions, removeDirectories.Directories);
                Save(filePath, root);
                return true;
            default:
                return false;
        }
    }

    public void PersistPermissionUpdates(
        string workspaceRoot,
        ClawSharpSettings settings,
        IReadOnlyList<PermissionUpdate> updates)
    {
        foreach (var update in updates)
        {
            PersistPermissionUpdate(workspaceRoot, settings, update);
        }
    }

    private static bool PersistAddRules(
        ClawSharpSettings settings,
        JsonObject root,
        JsonObject permissions,
        string filePath,
        AddPermissionRulesUpdate update)
    {
        if (settings.AllowManagedPermissionRulesOnly)
        {
            return false;
        }

        var propertyName = GetBehaviorPropertyName(update.Behavior);
        var existingRules = permissions[propertyName] as JsonArray ?? [];
        var normalizedExisting = existingRules
            .Select(node => node?.GetValue<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => PermissionRuleParser.PermissionRuleValueToString(PermissionRuleParser.PermissionRuleValueFromString(value!)))
            .ToHashSet(StringComparer.Ordinal);
        var newRules = update.Rules
            .Select(PermissionRuleParser.PermissionRuleValueToString)
            .Where(rule => !normalizedExisting.Contains(rule))
            .ToArray();

        if (newRules.Length == 0)
        {
            return true;
        }

        var mergedRules = new JsonArray();
        foreach (var node in existingRules)
        {
            mergedRules.Add(node?.DeepClone());
        }

        foreach (var rule in newRules)
        {
            mergedRules.Add(rule);
        }

        permissions[propertyName] = mergedRules;
        Save(filePath, root);
        return true;
    }

    private static void SetRules(
        JsonObject permissions,
        PermissionBehavior behavior,
        IEnumerable<string> rules)
    {
        var array = new JsonArray();
        foreach (var rule in rules)
        {
            array.Add(rule);
        }

        permissions[GetBehaviorPropertyName(behavior)] = array;
    }

    private static void RemoveRules(
        JsonObject permissions,
        PermissionBehavior behavior,
        IReadOnlyList<PermissionRuleValue> rules)
    {
        var propertyName = GetBehaviorPropertyName(behavior);
        var existingRules = permissions[propertyName] as JsonArray ?? [];
        var rulesToRemove = rules
            .Select(PermissionRuleParser.PermissionRuleValueToString)
            .ToHashSet(StringComparer.Ordinal);
        var filtered = new JsonArray();

        foreach (var node in existingRules)
        {
            var raw = node?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var normalized = PermissionRuleParser.PermissionRuleValueToString(
                PermissionRuleParser.PermissionRuleValueFromString(raw));
            if (!rulesToRemove.Contains(normalized))
            {
                filtered.Add(raw);
            }
        }

        permissions[propertyName] = filtered;
    }

    private static void AddDirectories(JsonObject permissions, IReadOnlyList<string> directories)
    {
        var existingDirectories = permissions["additionalDirectories"] as JsonArray ?? [];
        var seen = existingDirectories
            .Select(node => node?.GetValue<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
        var merged = new JsonArray();

        foreach (var node in existingDirectories)
        {
            merged.Add(node?.DeepClone());
        }

        foreach (var directory in directories)
        {
            if (seen.Add(directory))
            {
                merged.Add(directory);
            }
        }

        permissions["additionalDirectories"] = merged;
    }

    private static void RemoveDirectories(JsonObject permissions, IReadOnlyList<string> directories)
    {
        var existingDirectories = permissions["additionalDirectories"] as JsonArray ?? [];
        var directoriesToRemove = directories.ToHashSet(StringComparer.Ordinal);
        var filtered = new JsonArray();

        foreach (var node in existingDirectories)
        {
            var directory = node?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(directory) && !directoriesToRemove.Contains(directory))
            {
                filtered.Add(directory);
            }
        }

        permissions["additionalDirectories"] = filtered;
    }

    private static JsonObject LoadEditableSettings(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new JsonObject();
        }

        var content = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(content) as JsonObject ?? new JsonObject();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Invalid JSON syntax in settings file at {filePath}", exception);
        }
    }

    private static JsonObject GetOrCreateObject(JsonObject root, string propertyName)
    {
        if (root[propertyName] is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        root[propertyName] = created;
        return created;
    }

    private static string GetSettingsFilePath(string workspaceRoot, PermissionUpdateDestination destination)
    {
        return destination switch
        {
            PermissionUpdateDestination.UserSettings => ClaudeConfigPaths.GetUserSettingsFilePath(),
            PermissionUpdateDestination.ProjectSettings => ClaudeConfigPaths.GetProjectSettingsFilePath(workspaceRoot),
            PermissionUpdateDestination.LocalSettings => ClaudeConfigPaths.GetLocalSettingsFilePath(workspaceRoot),
            _ => throw new ArgumentOutOfRangeException(nameof(destination), destination, null)
        };
    }

    private static string GetBehaviorPropertyName(PermissionBehavior behavior)
    {
        return behavior switch
        {
            PermissionBehavior.Allow => "allow",
            PermissionBehavior.Deny => "deny",
            PermissionBehavior.Ask => "ask",
            _ => throw new ArgumentOutOfRangeException(nameof(behavior), behavior, null)
        };
    }

    private static void Save(string filePath, JsonObject root)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, root.ToJsonString(SerializerOptions));
    }
}
