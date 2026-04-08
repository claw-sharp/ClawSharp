using System.Diagnostics;
using ClawSharp.AgentHost.Contracts;
using ClawSharp.AgentHost.Ipc;
using ClawSharp.AgentHost.Projects;

namespace ClawSharp.AgentHost.Services;

public sealed class ExternalEditorService
{
    private readonly RecentProjectStore _recentProjectStore;

    public ExternalEditorService(RecentProjectStore recentProjectStore)
    {
        _recentProjectStore = recentProjectStore;
    }

    public async Task<OpenExternalEditorResponse> OpenAsync(
        OpenExternalEditorRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var command = string.IsNullOrWhiteSpace(request.EditorCommand)
            ? "code"
            : request.EditorCommand.Trim();
        var arguments = await BuildArgumentsAsync(request, cancellationToken);
        var launch = await LaunchAsync(command, arguments, cancellationToken);
        return new OpenExternalEditorResponse(launch);
    }

    private async Task<IReadOnlyList<string>> BuildArgumentsAsync(
        OpenExternalEditorRequest request,
        CancellationToken cancellationToken)
    {
        return request.Kind.Trim().ToLowerInvariant() switch
        {
            "project" => [await ResolveProjectPathAsync(request.ProjectId, cancellationToken)],
            "file" => [ResolveRequiredPath(request.Path)],
            "position" => BuildGotoArguments(request),
            "diff" => BuildDiffArguments(request),
            _ => throw new AgentHostException("invalid_request", $"Unsupported external editor target '{request.Kind}'.")
        };
    }

    private static IReadOnlyList<string> BuildGotoArguments(OpenExternalEditorRequest request)
    {
        var path = ResolveRequiredPath(request.Path);
        var line = request.Line.GetValueOrDefault(1);
        var column = request.Column.GetValueOrDefault(1);
        return ["--goto", $"{path}:{Math.Max(1, line)}:{Math.Max(1, column)}"];
    }

    private static IReadOnlyList<string> BuildDiffArguments(OpenExternalEditorRequest request)
    {
        var leftPath = ResolveRequiredPath(request.LeftPath);
        var rightPath = ResolveRequiredPath(request.RightPath);
        return ["--diff", leftPath, rightPath];
    }

    private static string ResolveRequiredPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new AgentHostException("invalid_request", "A path is required for this editor action.");
        }

        return Path.GetFullPath(path);
    }

    private async Task<string> ResolveProjectPathAsync(string? projectId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new AgentHostException("invalid_request", "projectId is required for project editor actions.");
        }

        var project = await _recentProjectStore.FindByIdAsync(projectId, cancellationToken);
        if (project is null)
        {
            throw new AgentHostException("project_not_found", $"Project '{projectId}' is not known to AgentHost yet.");
        }

        return project.Path;
    }

    private static Task<ExternalEditorLaunchDto> LaunchAsync(
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return Task.FromResult(new ExternalEditorLaunchDto(false, command, arguments, $"Failed to start '{command}'."));
            }

            return Task.FromResult(new ExternalEditorLaunchDto(
                true,
                command,
                arguments,
                $"Launched {command} {string.Join(' ', arguments)}"));
        }
        catch (Exception error)
        {
            return Task.FromResult(new ExternalEditorLaunchDto(false, command, arguments, error.Message));
        }
    }
}
