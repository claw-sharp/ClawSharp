using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClawSharp.AgentHost.Ipc;

public static class AgentHostProtocol
{
    public const string ProtocolVersion = "2026-04-07";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static readonly IReadOnlyList<string> SupportedCommands =
    [
        "health",
        "openProject",
        "listRecentProjects",
        "listThreads",
        "createThread",
        "getThread",
        "renameThread",
        "archiveThread",
        "startRun",
        "cancelRun",
        "retryRun",
        "listChangedFiles",
        "getDiff",
        "openExternalEditor",
        "listDiagnostics",
        "listPlugins",
        "listSkills",
        "createSkill",
        "listWorkspaceFiles",
        "getSettings",
        "updateSettings",
        "setPluginEnabled",
        "savePluginOptions",
        "deletePluginOptions",
        "refreshPlugins",
        "listProviders",
        "validateProviderConfig",
        "listPendingApprovals",
        "resolveApproval"
    ];
}

public sealed record AgentHostRequestEnvelope(
    string RequestId,
    string Command,
    JsonElement? Payload,
    string? ProtocolVersion = null);

public sealed record AgentHostResponseEnvelope(
    string RequestId,
    string Command,
    bool Success,
    DateTimeOffset Timestamp,
    object? Payload = null,
    AgentHostErrorDto? Error = null);

public sealed record AgentHostEventEnvelope(
    string Event,
    DateTimeOffset Timestamp,
    object Payload);

public sealed record AgentHostErrorDto(
    string Code,
    string Message,
    string? Details = null);
