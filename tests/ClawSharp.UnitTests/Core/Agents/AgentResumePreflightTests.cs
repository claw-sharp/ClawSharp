using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Tools;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public sealed class AgentResumePreflightTests
{
    [Fact]
    public async Task PrepareAsync_Uses_Metadata_And_Falls_Back_When_Worktree_Is_Missing()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var persistence = new AgentPersistenceService(new JsonlTranscriptStore());
        var service = new AgentResumePreflightService();

        await persistence.RecordSidechainTranscriptAsync(
            session,
            "agent-1",
            [ChatMessageFactory.CreateText(MessageRole.Assistant, "done")]);
        await persistence.WriteAgentMetadataAsync(
            session,
            "agent-1",
            new AgentMetadata("Explore", WorktreePath: Path.Combine(tempDir, "missing-worktree"), Description: "explore repo"));

        var result = await service.PrepareAsync(session, BuiltInAgentDefinitions.GetBuiltInAgents(), "agent-1");

        Assert.Equal("Explore", result.SelectedAgent.AgentType);
        Assert.Equal("explore repo", result.Description);
        Assert.True(result.MissingWorktreePath);
        Assert.Null(result.ResolvedWorktreePath);
        Assert.EndsWith(Path.Combine("tasks", "agent-1.output"), result.OutputFile, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        Assert.Equal(1, result.TranscriptLineCount);
        Assert.EndsWith(Path.Combine(session.Id, "subagents", "agent-agent-1.jsonl"), result.TranscriptPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrepareAsync_Falls_Back_To_GeneralPurpose_When_Metadata_Is_Missing()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var persistence = new AgentPersistenceService(new JsonlTranscriptStore());
        var service = new AgentResumePreflightService();

        await persistence.RecordSidechainTranscriptAsync(
            session,
            "agent-2",
            [ChatMessageFactory.CreateText(MessageRole.User, "resume me")]);

        var result = await service.PrepareAsync(session, BuiltInAgentDefinitions.GetBuiltInAgents(), "agent-2");

        Assert.Equal("general-purpose", result.SelectedAgent.AgentType);
        Assert.Equal("(resumed)", result.Description);
        Assert.Null(result.Metadata);
        Assert.False(result.MissingWorktreePath);
    }

    [Fact]
    public async Task PrepareAsync_Uses_Synthetic_Fork_Agent_When_Metadata_Records_Fork_Type()
    {
        var tempDir = CreateTempDirectory();
        var session = new DefaultSessionFactory(tempDir).Create();
        var persistence = new AgentPersistenceService(new JsonlTranscriptStore());
        var service = new AgentResumePreflightService();

        await persistence.RecordSidechainTranscriptAsync(
            session,
            "agent-3",
            [ChatMessageFactory.CreateText(MessageRole.Assistant, "forked work")]);
        await persistence.WriteAgentMetadataAsync(
            session,
            "agent-3",
            new AgentMetadata(ForkSubagentFoundation.ForkSubagentType, Description: "forked worker"));

        var result = await service.PrepareAsync(session, BuiltInAgentDefinitions.GetBuiltInAgents(), "agent-3");

        Assert.Equal("fork", result.SelectedAgent.AgentType);
        Assert.Equal(PermissionMode.Bubble, result.SelectedAgent.PermissionMode);
        Assert.Equal("forked worker", result.Description);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "clawsharp-agent-resume-preflight-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
