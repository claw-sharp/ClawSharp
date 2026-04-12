using System.Text.Json;
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static class BuiltInPluginCatalog
{
    private const string CatalogVersion = "v1";

    public static BuiltInPluginRegistry CreateRegistry(string? configHomeDir = null, string? sourceRoot = null)
    {
        var cacheRoot = Path.Combine(
            configHomeDir ?? SessionStoragePaths.GetClaudeConfigHomeDir(),
            "cache",
            "builtin-plugins",
            CatalogVersion);
        Directory.CreateDirectory(cacheRoot);

        var catalogSourceRoot = sourceRoot ?? ResolveSourceRoot();
        var definitions = Directory.GetDirectories(catalogSourceRoot)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .Select(pluginSourcePath => MaterializePlugin(pluginSourcePath, cacheRoot))
            .ToArray();

        return new BuiltInPluginRegistry(definitions);
    }

    private static BuiltInPluginDefinition MaterializePlugin(string pluginSourcePath, string cacheRoot)
    {
        var manifestPath = Path.Combine(pluginSourcePath, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"Built-in plugin is missing plugin.json: {pluginSourcePath}");
        }

        var manifest = ParseManifest(manifestPath);
        var pluginRoot = Path.Combine(cacheRoot, manifest.Name);
        SynchronizeDirectory(pluginSourcePath, pluginRoot);

        return new BuiltInPluginDefinition(
            manifest.Name,
            manifest.Description,
            pluginRoot,
            Version: manifest.Version,
            DefaultEnabled: manifest.DefaultEnabled);
    }

    private static BuiltInPluginManifest ParseManifest(string manifestPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"Built-in plugin manifest must be a JSON object: {manifestPath}");
        }

        var name = root.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
        var description = root.TryGetProperty("description", out var descriptionElement) ? descriptionElement.GetString() : null;
        var version = root.TryGetProperty("version", out var versionElement) ? versionElement.GetString() : null;
        var defaultEnabled = root.TryGetProperty("defaultEnabled", out var defaultEnabledElement) &&
                             defaultEnabledElement.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? defaultEnabledElement.GetBoolean()
            : true;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"Built-in plugin manifest is missing name: {manifestPath}");
        }

        return new BuiltInPluginManifest(
            name.Trim(),
            string.IsNullOrWhiteSpace(description) ? name.Trim() : description.Trim(),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim(),
            defaultEnabled);
    }

    private static void SynchronizeDirectory(string sourcePath, string destinationPath)
    {
        if (Directory.Exists(destinationPath))
        {
            Directory.Delete(destinationPath, recursive: true);
        }

        DirectoryCopy(sourcePath, destinationPath);
    }

    private static void DirectoryCopy(string sourcePath, string destinationPath)
    {
        Directory.CreateDirectory(destinationPath);

        foreach (var filePath in Directory.GetFiles(sourcePath))
        {
            var fileName = Path.GetFileName(filePath);
            File.Copy(filePath, Path.Combine(destinationPath, fileName), overwrite: true);
        }

        foreach (var childDirectory in Directory.GetDirectories(sourcePath))
        {
            var directoryName = Path.GetFileName(childDirectory);
            DirectoryCopy(childDirectory, Path.Combine(destinationPath, directoryName));
        }
    }

    private static string ResolveSourceRoot()
    {
        var assemblyBaseDirectory = AppContext.BaseDirectory;
        var directCandidate = Path.Combine(assemblyBaseDirectory, "BuiltinPlugins");
        if (Directory.Exists(directCandidate))
        {
            return directCandidate;
        }

        var current = new DirectoryInfo(assemblyBaseDirectory);
        while (current is not null)
        {
            var repoCandidate = Path.Combine(current.FullName, "src", "ClawSharp.Infrastructure", "BuiltinPlugins");
            if (Directory.Exists(repoCandidate))
            {
                return repoCandidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not find built-in plugin source directory. Expected BuiltinPlugins next to the app or under src/ClawSharp.Infrastructure/BuiltinPlugins.");
    }

    private sealed record BuiltInPluginManifest(
        string Name,
        string Description,
        string? Version,
        bool DefaultEnabled);
}
