// TS origin: ./commands/ide/index.ts, ./commands/ide/ide.tsx
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public sealed class IdeCommandHandler : ICommandHandler
{
    private readonly IdeIntegrationService _ideIntegrationService;

    public IdeCommandHandler(IdeIntegrationService ideIntegrationService)
    {
        _ideIntegrationService = ideIntegrationService;
    }

    public CommandDescriptor Descriptor { get; } =
        new(
            "ide",
            "Manage IDE integrations and show status",
            "/ide [open [path]]",
            IsInteractive: true);

    public async Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var arguments = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (arguments.Length > 1 && string.Equals(arguments[1], "open", StringComparison.OrdinalIgnoreCase))
        {
            var targetPath = arguments.Length > 2
                ? ResolveTargetPath(context.Session.ProjectDirectory, arguments[2])
                : context.Session.ProjectDirectory;
            var ides = await _ideIntegrationService.DetectIdesAsync(includeInvalid: false, cancellationToken).ConfigureAwait(false);
            if (ides.Count == 0)
            {
                return new CommandResult(true, "No IDEs with Claude Code integration detected.");
            }

            var selected = ides[0];
            var result = await _ideIntegrationService.OpenInIdeAsync(targetPath, selected, cancellationToken).ConfigureAwait(false);
            if (ides.Count > 1)
            {
                return new CommandResult(
                    true,
                    $"{result.Message}{Environment.NewLine}Multiple IDEs were detected; selected the most recent match ({selected.Name} on port {selected.Port}).");
            }

            return new CommandResult(true, result.Message);
        }

        var detected = await _ideIntegrationService.DetectIdesAsync(includeInvalid: true, cancellationToken).ConfigureAwait(false);
        if (detected.Count == 0)
        {
            return new CommandResult(
                true,
                "No IDEs with Claude Code integration detected. Use /ide open once the extension or plugin is running.");
        }

        var lines = new List<string> { "Detected IDE integrations:" };
        foreach (var ide in detected)
        {
            var status = ide.IsValid ? "valid" : "workspace-mismatch";
            lines.Add($"- {ide.Name} [{status}] port={ide.Port} url={ide.Url}");
            if (ide.WorkspaceFolders.Count > 0)
            {
                lines.Add($"  workspaces: {string.Join(", ", ide.WorkspaceFolders)}");
            }
        }

        return new CommandResult(true, string.Join(Environment.NewLine, lines));
    }

    private static string ResolveTargetPath(string root, string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(root, path));
    }
}
