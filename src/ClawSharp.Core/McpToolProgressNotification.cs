namespace ClawSharp.Core;

public sealed record McpToolProgressNotification(
    double? Progress = null,
    double? Total = null,
    string? ProgressMessage = null);
