using System.Text.Json.Nodes;

namespace ClawSharp.Tools;

public sealed record ToolProgressUpdate(
    string ToolUseId,
    JsonObject Data);
