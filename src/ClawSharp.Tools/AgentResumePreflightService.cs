// TS origin: ./tools/AgentTool/resumeAgent.ts, ./utils/sessionStorage.ts
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Tools;

public sealed class AgentResumePreflightService
{
    private const string GeneralPurposeAgentType = "general-purpose";

    public async Task<AgentResumePreflightResult> PrepareAsync(
        ConversationSession parentSession,
        IReadOnlyList<AgentDefinition> agentDefinitions,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var transcriptPath = TaskOutputStoragePaths.GetAgentTranscriptPath(
            parentSession.ProjectDirectory,
            parentSession.Id,
            agentId);
        if (!File.Exists(transcriptPath))
        {
            throw new InvalidOperationException($"No transcript found for agent ID: {agentId}");
        }

        var transcriptLines = await File.ReadAllLinesAsync(transcriptPath, cancellationToken);
        var metadataPath = TaskOutputStoragePaths.GetAgentMetadataPath(
            parentSession.ProjectDirectory,
            parentSession.Id,
            agentId);
        var metadata = await ReadMetadataAsync(metadataPath, cancellationToken);

        var selectedAgent = ResolveAgent(agentDefinitions, metadata?.AgentType);
        var resolvedWorktreePath = metadata?.WorktreePath is not null && Directory.Exists(metadata.WorktreePath)
            ? metadata.WorktreePath
            : null;
        var missingWorktreePath = metadata?.WorktreePath is not null && resolvedWorktreePath is null;
        var description = string.IsNullOrWhiteSpace(metadata?.Description)
            ? "(resumed)"
            : metadata.Description!;
        var outputFile = TaskOutputStoragePaths.GetTaskOutputPath(
            parentSession.ProjectDirectory,
            parentSession.Id,
            agentId);

        return new AgentResumePreflightResult(
            agentId,
            transcriptPath,
            transcriptLines.Count(static line => !string.IsNullOrWhiteSpace(line)),
            metadata,
            selectedAgent,
            description,
            outputFile,
            resolvedWorktreePath,
            missingWorktreePath);
    }

    private static AgentDefinition ResolveAgent(IReadOnlyList<AgentDefinition> agentDefinitions, string? agentType)
    {
        if (string.Equals(agentType, ForkSubagentFoundation.ForkSubagentType, StringComparison.Ordinal))
        {
            return ForkSubagentFoundation.ForkAgentDefinition;
        }

        if (!string.IsNullOrWhiteSpace(agentType))
        {
            var matched = agentDefinitions.FirstOrDefault(
                definition => string.Equals(definition.AgentType, agentType, StringComparison.Ordinal));
            if (matched is not null)
            {
                return matched;
            }
        }

        return agentDefinitions.FirstOrDefault(
                   definition => string.Equals(definition.AgentType, GeneralPurposeAgentType, StringComparison.Ordinal))
               ?? BuiltInAgentDefinitions.GetBuiltInAgents().First(
                   definition => string.Equals(definition.AgentType, GeneralPurposeAgentType, StringComparison.Ordinal));
    }

    private static async Task<AgentMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var raw = await File.ReadAllTextAsync(path, cancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<AgentMetadata>(raw);
    }
}

public sealed record AgentResumePreflightResult(
    string AgentId,
    string TranscriptPath,
    int TranscriptLineCount,
    AgentMetadata? Metadata,
    AgentDefinition SelectedAgent,
    string Description,
    string OutputFile,
    string? ResolvedWorktreePath,
    bool MissingWorktreePath);
