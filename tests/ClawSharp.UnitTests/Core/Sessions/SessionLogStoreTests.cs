using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public class SessionLogStoreTests
{
    [Fact]
    public async Task LoadProjectLogsAsync_Reads_Custom_Title_And_First_User_Message()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-session-log-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var session = sessionFactory.Create();
            session.SetCustomTitle("Named Session");
            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello world"));
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var store = new DiskSessionLogStore();
            var logs = await store.LoadProjectLogsAsync(workspaceRoot);

            var log = Assert.Single(logs);
            Assert.Equal(session.Id, log.SessionId);
            Assert.Equal("Named Session", log.CustomTitle);
            Assert.Equal("hello world", log.FirstUserMessage);
            Assert.True(File.Exists(SessionStoragePaths.GetSessionLogMetadataPath(workspaceRoot, session.Id)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SearchProjectLogsByCustomTitleAsync_Matches_Exact_Title_Case_Insensitively()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-session-log-search-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);

            var matchingSession = sessionFactory.Create();
            matchingSession.SetCustomTitle("Quarterly Review");
            matchingSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "one"));
            await transcriptStore.RecordTranscriptAsync(matchingSession, matchingSession.Messages);

            var otherSession = sessionFactory.Create();
            otherSession.SetCustomTitle("Different Title");
            otherSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "two"));
            await transcriptStore.RecordTranscriptAsync(otherSession, otherSession.Messages);

            var store = new DiskSessionLogStore();
            var matches = await store.SearchProjectLogsByCustomTitleAsync(workspaceRoot, " quarterly review ");

            var match = Assert.Single(matches);
            Assert.Equal(matchingSession.Id, match.SessionId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadSameRepoLogsAsync_Loads_Logs_For_Matching_Worktrees()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-session-log-same-repo-tests", Guid.NewGuid().ToString("N"));
        var currentWorkspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"), "repo");
        var siblingWorkspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"), "repo-worktree");
        Directory.CreateDirectory(currentWorkspaceRoot);
        Directory.CreateDirectory(siblingWorkspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var currentSessionFactory = new DefaultSessionFactory(currentWorkspaceRoot, transcriptStore);
            var siblingSessionFactory = new DefaultSessionFactory(siblingWorkspaceRoot, transcriptStore);

            var currentSession = currentSessionFactory.Create();
            currentSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "current"));
            await transcriptStore.RecordTranscriptAsync(currentSession, currentSession.Messages);

            var siblingSession = siblingSessionFactory.Create();
            siblingSession.SetCustomTitle("Sibling Title");
            siblingSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "sibling"));
            await transcriptStore.RecordTranscriptAsync(siblingSession, siblingSession.Messages);

            var store = new DiskSessionLogStore();
            var logs = await store.LoadSameRepoLogsAsync([currentWorkspaceRoot, siblingWorkspaceRoot]);

            Assert.Equal(2, logs.Count);
            Assert.Contains(logs, log => log.SessionId == currentSession.Id && log.ProjectDirectory == currentWorkspaceRoot);
            Assert.Contains(logs, log => log.SessionId == siblingSession.Id && log.ProjectDirectory == siblingWorkspaceRoot && log.CustomTitle == "Sibling Title");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            var currentRootParent = Path.GetDirectoryName(currentWorkspaceRoot)!;
            if (Directory.Exists(currentRootParent))
            {
                Directory.Delete(currentRootParent, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LoadProjectLogsAsync_Backfills_Metadata_For_Legacy_Transcript_Only_Sessions()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-session-log-migration-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var session = sessionFactory.Create();
            session.SetCustomTitle("Migrated Session");
            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "legacy hello"));
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var metadataPath = SessionStoragePaths.GetSessionLogMetadataPath(workspaceRoot, session.Id);
            File.Delete(metadataPath);

            var store = new DiskSessionLogStore();
            var log = Assert.Single(await store.LoadProjectLogsAsync(workspaceRoot));

            Assert.Equal(session.Id, log.SessionId);
            Assert.Equal("Migrated Session", log.CustomTitle);
            Assert.Equal("legacy hello", log.FirstUserMessage);
            Assert.True(File.Exists(metadataPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RecordSessionMetadataAsync_Persists_Empty_Thread_Log_Metadata()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAWSHARP_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-session-log-empty-thread-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var session = sessionFactory.Create();

            var transcriptDirectory = Path.GetDirectoryName(session.TranscriptPath);
            Assert.NotNull(transcriptDirectory);
            Directory.CreateDirectory(transcriptDirectory!);
            await File.WriteAllTextAsync(session.TranscriptPath, string.Empty);

            await transcriptStore.RecordSessionMetadataAsync(session);

            var store = new DiskSessionLogStore();
            var log = Assert.Single(await store.LoadProjectLogsAsync(workspaceRoot));

            Assert.Equal(session.Id, log.SessionId);
            Assert.Null(log.CustomTitle);
            Assert.Null(log.FirstUserMessage);
            Assert.True(File.Exists(SessionStoragePaths.GetSessionLogMetadataPath(workspaceRoot, session.Id)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAWSHARP_CONFIG_DIR", originalConfigDir);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }

            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
    }
}
