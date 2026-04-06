// TS origin: ./utils/sessionStorage.ts
using System.Text.Json;
using ClawSharp.Core;
using ClawSharp.Tasks;

namespace ClawSharp.Infrastructure;

public sealed class AgentPersistenceService
{
    private readonly ITranscriptStore _transcriptStore;
    private readonly AgentTranscriptSubdirRegistry _subdirRegistry;

    public AgentPersistenceService(
        ITranscriptStore transcriptStore,
        AgentTranscriptSubdirRegistry? subdirRegistry = null)
    {
        _transcriptStore = transcriptStore;
        _subdirRegistry = subdirRegistry ?? new AgentTranscriptSubdirRegistry();
    }

    public void SetAgentTranscriptSubdir(string agentId, string subdir)
    {
        _subdirRegistry.Set(agentId, subdir);
    }

    public void ClearAgentTranscriptSubdir(string agentId)
    {
        _subdirRegistry.Clear(agentId);
    }

    public string GetAgentTranscriptPath(ConversationSession parentSession, string agentId)
    {
        return TaskOutputStoragePaths.GetAgentTranscriptPath(
            parentSession.ProjectDirectory,
            parentSession.Id,
            agentId,
            _subdirRegistry.Get(agentId));
    }

    public string GetAgentMetadataPath(ConversationSession parentSession, string agentId)
    {
        return TaskOutputStoragePaths.GetAgentMetadataPath(
            parentSession.ProjectDirectory,
            parentSession.Id,
            agentId,
            _subdirRegistry.Get(agentId));
    }

    public ConversationSession CreateAgentSession(ConversationSession parentSession, string agentId)
    {
        return new ConversationSession(
            agentId,
            parentSession.ProjectDirectory,
            GetAgentTranscriptPath(parentSession, agentId));
    }

    public async Task RecordSidechainTranscriptAsync(
        ConversationSession parentSession,
        string agentId,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        var agentSession = CreateAgentSession(parentSession, agentId);
        foreach (var message in messages)
        {
            agentSession.Add(message);
        }

        await _transcriptStore.RecordTranscriptAsync(agentSession, agentSession.Messages, cancellationToken);
    }

    public async Task<TranscriptReadResult?> ReadAgentTranscriptAsync(
        ConversationSession parentSession,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var path = GetAgentTranscriptPath(parentSession, agentId);
        if (!File.Exists(path))
        {
            return null;
        }

        return await _transcriptStore.ReadTranscriptAsync(path, cancellationToken);
    }

    public async Task WriteAgentMetadataAsync(
        ConversationSession parentSession,
        string agentId,
        AgentMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var path = GetAgentMetadataPath(parentSession, agentId);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(metadata), cancellationToken);
    }

    public async Task<AgentMetadata?> ReadAgentMetadataAsync(
        ConversationSession parentSession,
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var path = GetAgentMetadataPath(parentSession, agentId);
        if (!File.Exists(path))
        {
            return null;
        }

        var raw = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<AgentMetadata>(raw);
    }
}
