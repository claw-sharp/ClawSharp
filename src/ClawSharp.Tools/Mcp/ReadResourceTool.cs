using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;
using ClawSharp.Core.Storage;
using ClawSharp.Tools.Registry;

namespace ClawSharp.Tools.Mcp;

public sealed class ReadMcpResourceTool : BaseTool
{
    public const string ToolName = "ReadMcpResource";
    private const int MaxSessionRetries = 1;

    public ReadMcpResourceTool()
        : base(new ToolDescriptor(
            ToolName,
            "Reads the content of a specific MCP resource by its URI. Resources can be configuration files, log snippets, or other non-executable data provided by the server.",
            SearchHint: "read a specific MCP resource by URI",
            ShouldDefer: true,
            InputSchema: ReadResourceToolSchemas.InputSchema,
            OutputSchema: ReadResourceToolSchemas.OutputSchema,
            Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;
    public override bool IsReadOnly(string arguments) => true;

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(context.Arguments, out var serverName, out var uri))
        {
            return Failure("Invalid input.");
        }

        if (context.McpLifecycle == null)
        {
            return Failure("MCP lifecycle manager is not available.");
        }

        if (context.McpResources == null)
        {
            return Failure("MCP resource catalog is not available.");
        }

        if (!context.McpResources.TryGetServer(serverName, out var resourceSet) || resourceSet == null)
        {
            return Failure($"Server \"{serverName}\" not found.");
        }

        try
        {
            McpReadResourceResult result;
            for (var attempt = 0; ; attempt++)
            {
                var connection = await context.McpLifecycle.ConnectToServerAsync(serverName, resourceSet.Config, cancellationToken: cancellationToken);
                if (connection is not ConnectedMcpServerConnection connected)
                {
                    return Failure($"Server \"{serverName}\" is not connected (Status: {connection.Status}).");
                }

                if (!connected.Capabilities.ContainsKey("resources"))
                {
                    return Failure($"Server \"{serverName}\" does not support resources.");
                }

                try
                {
                    result = await connected.Client.ReadResourceAsync(uri, cancellationToken);
                    break;
                }
                catch (Exception exception) when (attempt < MaxSessionRetries && ShouldRetry(exception, resourceSet.Config))
                {
                    await context.McpLifecycle.ClearServerCacheAsync(serverName, resourceSet.Config, cancellationToken);
                }
            }
            
            var contents = await Task.WhenAll(
                result.Contents.Select((content, index) => ConvertContentAsync(content, serverName, context.Session, index, cancellationToken)));

            var structured = new JsonObject
            {
                ["contents"] = new JsonArray(contents.Cast<JsonNode?>().ToArray())
            };

            return Success(structured.ToJsonString(), structured);
        }
        catch (Exception ex)
        {
            return Failure($"Failed to read resource from {serverName}: {ex.Message}");
        }
    }

    private static async Task<JsonObject> ConvertContentAsync(
        McpResourceContent content,
        string serverName,
        ConversationSession session,
        int index,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new JsonObject
        {
            ["uri"] = content.Uri
        };

        if (!string.IsNullOrWhiteSpace(content.MimeType))
        {
            result["mimeType"] = content.MimeType;
        }

        if (content.Text is not null)
        {
            result["text"] = content.Text;
            return result;
        }

        if (content.Blob is not null)
        {
            var persisted = await PersistBinaryContentAsync(content.Blob, content.MimeType, session, serverName, index, cancellationToken);
            result["text"] = persisted.Message;
            if (persisted.FilePath is not null)
            {
                result["blobSavedTo"] = persisted.FilePath;
            }
        }

        return result;
    }

    private static async Task<(string Message, string? FilePath)> PersistBinaryContentAsync(
        byte[] blob,
        string? mimeType,
        ConversationSession session,
        string serverName,
        int index,
        CancellationToken cancellationToken)
    {
        var toolResultsDir = GetToolResultsDir(session);
        Directory.CreateDirectory(toolResultsDir);
        var extension = GetExtensionForMimeType(mimeType);
        var filePath = Path.Combine(toolResultsDir, $"mcp-resource-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{index}-{Guid.NewGuid():N}.{extension}");
        try
        {
            await File.WriteAllBytesAsync(filePath, blob, cancellationToken);
            return ($"[Resource from {serverName}] Binary content ({mimeType ?? "unknown type"}, {FormatFileSize(blob.Length)}) saved to {filePath}", filePath);
        }
        catch (Exception exception)
        {
            return ($"Binary content could not be saved to disk: {exception.Message}", null);
        }
    }

    private static string GetToolResultsDir(ConversationSession session)
    {
        return Path.Combine(
            SessionStoragePaths.GetProjectDir(session.ProjectDirectory),
            session.Id,
            "tool-results");
    }

    private static string GetExtensionForMimeType(string? mimeType)
    {
        var normalized = mimeType?.Split(';')[0].Trim().ToLowerInvariant();
        return normalized switch
        {
            "application/pdf" => "pdf",
            "application/json" => "json",
            "text/csv" => "csv",
            "text/plain" => "txt",
            "text/html" => "html",
            "text/markdown" => "md",
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            _ => "bin"
        };
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        var kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:0.#} KB";
        }

        return $"{kilobytes / 1024d:0.#} MB";
    }

    private static bool TryParseArguments(string arguments, out string serverName, out string uri)
    {
        serverName = string.Empty;
        uri = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(arguments);
            if (doc.RootElement.TryGetProperty("server", out var serverProp))
            {
                serverName = serverProp.GetString() ?? string.Empty;
            }
            if (doc.RootElement.TryGetProperty("uri", out var uriProp))
            {
                uri = uriProp.GetString() ?? string.Empty;
            }
            return !string.IsNullOrEmpty(serverName) && !string.IsNullOrEmpty(uri);
        }
        catch
        {
            return false;
        }
    }

    private static bool ShouldRetry(Exception exception, ScopedMcpServerConfig config)
    {
        return McpReconnectClassifier.IsSessionExpiredError(exception) ||
               McpReconnectClassifier.IsConnectionClosedOnHttp(exception, config);
    }
}
