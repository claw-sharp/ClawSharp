// TS origin: ./main.tsx, ./commands/resume/resume.tsx, ./utils/sessionStorage.ts
using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

[Collection("SessionStorage")]
public class ReplSessionBootstrapperTests
{
    [Fact]
    public async Task ResolveAsync_Applies_Name_To_Fresh_Session_Without_Creating_Metadata_Only_File()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-bootstrapper-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var sessionLogStore = new DiskSessionLogStore();
            var bootstrapper = new ReplSessionBootstrapper(
                sessionFactory,
                sessionLogStore,
                transcriptStore,
                new FixedWorktreePathResolver([workspaceRoot]),
                workspaceRoot);

            var result = await bootstrapper.ResolveAsync(["--name", "Named Session"]);

            Assert.True(result.Success);
            Assert.NotNull(result.Session);
            Assert.Equal("Named Session", result.Session!.CustomTitle);
            Assert.False(File.Exists(result.Session.TranscriptPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
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
    public async Task ResolveAsync_Resumes_By_Exact_Custom_Title()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-bootstrapper-resume-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var sessionLogStore = new DiskSessionLogStore();
            var bootstrapper = new ReplSessionBootstrapper(
                sessionFactory,
                sessionLogStore,
                transcriptStore,
                new FixedWorktreePathResolver([workspaceRoot]),
                workspaceRoot);

            var session = sessionFactory.Create();
            session.SetCustomTitle("Searchable Session");
            session.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello"));
            await transcriptStore.RecordTranscriptAsync(session, session.Messages);

            var result = await bootstrapper.ResolveAsync(["--resume", "Searchable Session"]);

            Assert.True(result.Success);
            Assert.NotNull(result.Session);
            Assert.Equal(session.Id, result.Session!.Id);
            Assert.Equal("hello", result.Session.Messages[0].Content);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
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
    public async Task ResolveAsync_Resumes_By_Same_Repo_Worktree_Title()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-bootstrapper-same-repo-title-tests", Guid.NewGuid().ToString("N"));
        var currentWorkspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"), "repo");
        var siblingWorkspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"), "repo-worktree");
        Directory.CreateDirectory(currentWorkspaceRoot);
        Directory.CreateDirectory(siblingWorkspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var currentSessionFactory = new DefaultSessionFactory(currentWorkspaceRoot, transcriptStore);
            var siblingSessionFactory = new DefaultSessionFactory(siblingWorkspaceRoot, transcriptStore);
            var sessionLogStore = new DiskSessionLogStore();
            var bootstrapper = new ReplSessionBootstrapper(
                currentSessionFactory,
                sessionLogStore,
                transcriptStore,
                new FixedWorktreePathResolver([currentWorkspaceRoot, siblingWorkspaceRoot]),
                currentWorkspaceRoot);

            var siblingSession = siblingSessionFactory.Create();
            siblingSession.SetCustomTitle("Sibling Session");
            siblingSession.Add(ChatMessageFactory.CreateText(MessageRole.User, "hello"));
            await transcriptStore.RecordTranscriptAsync(siblingSession, siblingSession.Messages);

            var result = await bootstrapper.ResolveAsync(["--resume", "Sibling Session"]);

            Assert.True(result.Success);
            Assert.NotNull(result.Session);
            Assert.Equal(siblingSession.Id, result.Session!.Id);
            Assert.Equal(siblingWorkspaceRoot, result.Session.ProjectDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
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
    public async Task ResolveAsync_Allows_Debug_Passthrough_Arguments()
    {
        var originalConfigDir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var tempConfigDir = Path.Combine(Path.GetTempPath(), "clawsharp-bootstrapper-debug-args-tests", Guid.NewGuid().ToString("N"));
        var workspaceRoot = Path.Combine(Path.GetTempPath(), "clawsharp-workspace", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspaceRoot);
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", tempConfigDir);

        try
        {
            var transcriptStore = new JsonlTranscriptStore();
            var sessionFactory = new DefaultSessionFactory(workspaceRoot, transcriptStore);
            var sessionLogStore = new DiskSessionLogStore();
            var bootstrapper = new ReplSessionBootstrapper(
                sessionFactory,
                sessionLogStore,
                transcriptStore,
                new FixedWorktreePathResolver([workspaceRoot]),
                workspaceRoot);

            var acceptedArgumentSets = new[]
            {
                new[] { "--debug" },
                new[] { "-d" },
                new[] { "--debug=bridge" },
                new[] { "--debug-to-stderr" },
                new[] { "-d2e" },
                new[] { "--debug-file", Path.Combine(tempConfigDir, "debug.log") },
                new[] { $"--debug-file={Path.Combine(tempConfigDir, "debug-inline.log")}" },
                new[] { "--name", "Named Session", "--debug" }
            };

            foreach (var arguments in acceptedArgumentSets)
            {
                var result = await bootstrapper.ResolveAsync(arguments);
                Assert.True(result.Success, string.Join(' ', arguments));

                if (arguments.Contains("--name", StringComparer.Ordinal))
                {
                    Assert.NotNull(result.Session);
                    Assert.Equal("Named Session", result.Session!.CustomTitle);
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", originalConfigDir);
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

    private sealed class FixedWorktreePathResolver : IWorktreePathResolver
    {
        private readonly IReadOnlyList<string> _paths;

        public FixedWorktreePathResolver(IReadOnlyList<string> paths)
        {
            _paths = paths;
        }

        public Task<IReadOnlyList<string>> GetWorktreePathsAsync(
            string workspaceRoot,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_paths);
        }
    }
}
