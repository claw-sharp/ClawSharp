using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;

namespace ClawSharp.AgentHost.Files;

public sealed class WorkspaceFileCatalogService
{
    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".next",
        ".turbo",
        "node_modules",
        "bin",
        "obj",
        "dist",
        "build",
        "coverage"
    };

    private readonly RecentProjectStore _recentProjectStore;

    public WorkspaceFileCatalogService(RecentProjectStore recentProjectStore)
    {
        _recentProjectStore = recentProjectStore;
    }

    public async Task<ListWorkspaceFilesResponse> ListAsync(
        ListWorkspaceFilesRequest request,
        CancellationToken cancellationToken = default)
    {
        var project = await ResolveProjectAsync(request.ProjectId, cancellationToken);
        var files = EnumerateWorkspaceFiles(project.Path, cancellationToken)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ListWorkspaceFilesResponse(project.ProjectId, project.Path, files);
    }

    private async Task<RecentProjectEntry> ResolveProjectAsync(string? projectId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(projectId))
        {
            var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
            if (project is null)
            {
                throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
            }

            return project;
        }

        var recentProject = (await _recentProjectStore.ListAsync(cancellationToken)).FirstOrDefault();
        if (recentProject is null)
        {
            throw new AgentHostException("project_not_found", "No project is currently open.");
        }

        return recentProject;
    }

    private static IEnumerable<string> EnumerateWorkspaceFiles(string workspaceRoot, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(workspaceRoot);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentDirectory = pending.Pop();

            IEnumerable<string> childDirectories;
            try
            {
                childDirectories = Directory.EnumerateDirectories(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (var childDirectory in childDirectories.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                var directoryName = Path.GetFileName(childDirectory);
                if (IgnoredDirectoryNames.Contains(directoryName))
                {
                    continue;
                }

                pending.Push(childDirectory);
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDirectory);
            }
            catch
            {
                continue;
            }

            foreach (var filePath in files)
            {
                var relativePath = Path.GetRelativePath(workspaceRoot, filePath).Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                yield return relativePath;
            }
        }
    }
}
