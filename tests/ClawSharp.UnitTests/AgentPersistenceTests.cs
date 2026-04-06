// TS origin: ./utils/sessionStorage.ts
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class AgentPersistenceTests
{
    [Fact]
    public async Task AgentPersistenceService_Records_And_Reads_Sidechain_Transcript()
    {
        var workspaceRoot = CreateTempDirectory();
        var parentSession = new DefaultSessionFactory(workspaceRoot).Create();
        var transcriptStore = new JsonlTranscriptStore();
        var persistence = new AgentPersistenceService(transcriptStore);

        var messages = new[]
        {
            ChatMessageFactory.CreateText(MessageRole.User, "subagent prompt"),
            ChatMessageFactory.CreateText(MessageRole.Assistant, "subagent result")
        };

        await persistence.RecordSidechainTranscriptAsync(parentSession, "agent-1", messages);
        var transcript = await persistence.ReadAgentTranscriptAsync(parentSession, "agent-1");

        Assert.NotNull(transcript);
        Assert.Equal(2, transcript!.Messages.Count);
        Assert.Equal("subagent prompt", transcript.Messages[0].Content);
        Assert.Equal("subagent result", transcript.Messages[1].Content);
        Assert.EndsWith(
            Path.Combine(parentSession.Id, "subagents", "agent-agent-1.jsonl"),
            persistence.GetAgentTranscriptPath(parentSession, "agent-1"),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    [Fact]
    public async Task AgentPersistenceService_Uses_Registered_Subdirectory_For_Transcript_And_Metadata()
    {
        var workspaceRoot = CreateTempDirectory();
        var parentSession = new DefaultSessionFactory(workspaceRoot).Create();
        var transcriptStore = new JsonlTranscriptStore();
        var persistence = new AgentPersistenceService(transcriptStore);
        persistence.SetAgentTranscriptSubdir("agent-2", Path.Combine("workflows", "run-1"));

        var transcriptPath = persistence.GetAgentTranscriptPath(parentSession, "agent-2");
        var metadata = new AgentMetadata("Explore", WorktreePath: "d:\\wt", Description: "workflow child");

        await persistence.RecordSidechainTranscriptAsync(
            parentSession,
            "agent-2",
            [ChatMessageFactory.CreateText(MessageRole.Assistant, "child output")]);
        await persistence.WriteAgentMetadataAsync(parentSession, "agent-2", metadata);
        var restoredMetadata = await persistence.ReadAgentMetadataAsync(parentSession, "agent-2");

        Assert.Contains(
            Path.Combine("subagents", "workflows", "run-1", "agent-agent-2.jsonl"),
            transcriptPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        Assert.NotNull(restoredMetadata);
        Assert.Equal(metadata, restoredMetadata);
        Assert.True(File.Exists(persistence.GetAgentMetadataPath(parentSession, "agent-2")));

        persistence.ClearAgentTranscriptSubdir("agent-2");
        var clearedPath = persistence.GetAgentTranscriptPath(parentSession, "agent-2");
        Assert.DoesNotContain(
            Path.Combine("workflows", "run-1"),
            clearedPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-agent-persistence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
