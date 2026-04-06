using System.Text;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class DeepLinkTerminalLaunchServiceTests
{
    [Fact]
    public async Task HandleUriAsync_Launches_Repl_With_Approved_Draft_Fallback_Arguments()
    {
        TerminalLaunchRequest? request = null;
        var service = new DeepLinkTerminalLaunchService(
            terminalLauncher: CreateTerminalLauncher(
                "win32",
                new TerminalInfo("Windows Terminal", "wt.exe"),
                (launchRequest, _) =>
                {
                    request = launchRequest;
                    return Task.FromResult(true);
                }));

        var result = await service.HandleUriAsync(
            "claude-cli://open?q=review%20this&cwd=C%3A%5Cwork",
            @"C:\ClawSharp\ClawSharp.exe");

        Assert.True(result.Success, result.Error);
        Assert.NotNull(request);
        Assert.Equal("wt.exe", request!.FileName);
        Assert.Null(request.WorkingDirectory);
        Assert.Equal(
            [
                "-d",
                @"C:\work",
                "--",
                @"C:\ClawSharp\ClawSharp.exe",
                "repl",
                "--deep-link-origin",
                "--deep-link-draft",
                "review this"
            ],
            request.Arguments);
    }

    [Fact]
    public async Task HandleUriAsync_Resolves_Repo_To_Tracked_Clone_Before_Launch()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "clawsharp-deeplink", Guid.NewGuid().ToString("N"));
        var trackedClone = Path.Combine(tempDirectory, "tracked-clone");
        var globalConfigPath = Path.Combine(tempDirectory, ".claude.json");
        Directory.CreateDirectory(trackedClone);
        await File.WriteAllTextAsync(
            globalConfigPath,
            """
            {
              "githubRepoPaths": {
                "owner/repo": [
                  "TRACKED"
                ]
              }
            }
            """.Replace("TRACKED", EscapeJson(trackedClone), StringComparison.Ordinal),
            Encoding.UTF8);

        TerminalLaunchRequest? request = null;

        try
        {
            var service = new DeepLinkTerminalLaunchService(
                repoPathMappingService: new GitHubRepoPathMappingService(globalConfigPath),
                terminalLauncher: CreateTerminalLauncher(
                    "win32",
                    new TerminalInfo("Windows Terminal", "wt.exe"),
                    (launchRequest, _) =>
                    {
                        request = launchRequest;
                        return Task.FromResult(true);
                    }));

            var result = await service.HandleUriAsync(
                "claude-cli://open?repo=owner%2Frepo",
                @"C:\ClawSharp\ClawSharp.exe");

            Assert.True(result.Success, result.Error);
            Assert.NotNull(request);
            Assert.Equal("wt.exe", request!.FileName);
            Assert.Equal(
                [
                    "-d",
                    trackedClone,
                    "--",
                    @"C:\ClawSharp\ClawSharp.exe",
                    "repl",
                    "--deep-link-origin",
                    "--deep-link-repo",
                    "owner/repo"
                ],
                request.Arguments);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal);
    }

    private static TerminalLauncher CreateTerminalLauncher(
        string platform,
        TerminalInfo terminal,
        Func<TerminalLaunchRequest, CancellationToken, Task<bool>> startDetachedAsync)
    {
        return new TerminalLauncher(
            detectTerminalAsync: _ => Task.FromResult<TerminalInfo?>(terminal),
            startDetachedAsync: startDetachedAsync,
            platform: platform);
    }
}
