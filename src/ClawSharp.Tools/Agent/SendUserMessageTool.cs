using System.Text.Json;
using System.Text.Json.Nodes;
using ClawSharp.Core;

namespace ClawSharp.Tools;

internal sealed class SendUserMessageTool : BaseTool
{
    public SendUserMessageTool()
        : base(new ToolDescriptor(
            "SendUserMessage",
            "Send a message to the user",
            Parameters: [
                new ToolParameter("message", "The message for the user. Supports markdown formatting."),
                new ToolParameter("status", "Use 'proactive' for unsolicited updates, 'normal' for replies."),
                new ToolParameter("attachments", "Optional file paths to attach.", Required: false)
            ],
            InputSchema: SendUserMessageToolSchemas.InputSchema,
            OutputSchema: SendUserMessageToolSchemas.OutputSchema,
            Strict: true))
    {
    }

    public override bool IsConcurrencySafe(string arguments) => true;
    public override bool IsReadOnly(string arguments) => true;

    public override string? RenderToolUseMessage(string arguments) => string.Empty;

    public override string? RenderToolResultMessage(string content, JsonNode? structuredOutput, IReadOnlyList<ToolProgressUpdate> progressMessages)
    {
        if (structuredOutput is null) return null;

        var message = structuredOutput["message"]?.GetValue<string>();
        var attachments = structuredOutput["attachments"]?.AsArray();

        if (string.IsNullOrWhiteSpace(message) && (attachments == null || attachments.Count == 0))
        {
            return null;
        }

        // Default view: dropTextInBriefTurns hides redundant assistant text.
        // We'll mimic the spacing with Spectre.Console markup if possible, 
        // or just return the text. Terminal UI handles the Markdown.
        var result = new System.Text.StringBuilder();
        if (!string.IsNullOrWhiteSpace(message))
        {
            result.Append(message);
        }

        if (attachments != null && attachments.Count > 0)
        {
            result.AppendLine();
            foreach (var att in attachments)
            {
                var path = att?["path"]?.GetValue<string>();
                var isImage = att?["isImage"]?.GetValue<bool>() ?? false;
                var size = att?["size"]?.GetValue<long>() ?? 0;

                var typeLabel = isImage ? "[image]" : "[file]";
                var displaySize = FormatFileSize(size);
                
                result.AppendLine($"[dim]» {typeLabel} [/]{path} [dim]({displaySize})[/]");
            }
        }

        return result.ToString();
    }

    public override async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, CancellationToken cancellationToken = default)
    {
        var input = JsonNode.Parse(context.Arguments);
        var message = input?["message"]?.GetValue<string>() ?? "";
        var status = input?["status"]?.GetValue<string>() ?? "normal";
        var rawAttachments = input?["attachments"]?.AsArray();

        var sentAt = DateTime.UtcNow.ToString("O");

        if (rawAttachments == null || rawAttachments.Count == 0)
        {
            return Success("Message delivered to user.", new JsonObject
            {
                ["message"] = message,
                ["sentAt"] = sentAt
            });
        }

        var resolvedAttachments = new JsonArray();
        foreach (var node in rawAttachments)
        {
            var rawPath = node?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(rawPath)) continue;

            // In a real port, we'd use FileToolPermissionEvaluator here too if we want parity on permissions.
            // But BriefTool in source doesn't seem to check permissions via a prompter, it just stats the file.
            // However, ClawSharp's ReadTool does check permissions.
            // For now, let's keep it simple as in the TS source.
            
            var fullPath = Path.IsPathRooted(rawPath) ? rawPath : Path.Combine(context.WorkspaceRoot, rawPath);
            if (File.Exists(fullPath))
            {
                var fileInfo = new FileInfo(fullPath);
                resolvedAttachments.Add(new JsonObject
                {
                    ["path"] = fullPath,
                    ["size"] = fileInfo.Length,
                    ["isImage"] = ReadToolImageReader.IsSupportedImageExtension(fullPath)
                });
            }
        }

        var suffix = resolvedAttachments.Count == 0 ? "" : $" ({resolvedAttachments.Count} attachment(s) included)";
        return Success($"Message delivered to user.{suffix}", new JsonObject
        {
            ["message"] = message,
            ["attachments"] = resolvedAttachments,
            ["sentAt"] = sentAt
        });
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        var kb = bytes / 1024d;
        if (kb < 1024) return $"{kb:0.#} KB";
        return $"{kb / 1024d:0.#} MB";
    }
}
