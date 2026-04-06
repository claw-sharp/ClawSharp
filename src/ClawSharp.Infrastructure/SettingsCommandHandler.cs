// TS origin: ./commands/config/index.ts, ./utils/config.ts
using ClawSharp.Core;
using ClawSharp.Ui.Terminal;

namespace ClawSharp.Infrastructure;

public sealed class SettingsCommandHandler : ICommandHandler
{
    private readonly SettingsCommandRenderer _renderer;

    public SettingsCommandHandler(SettingsCommandRenderer? renderer = null)
    {
        _renderer = renderer ?? new SettingsCommandRenderer();
    }

    public CommandDescriptor Descriptor { get; } =
        new("config", "Open config panel", "/config", IsInteractive: true, Aliases: ["settings"]);

    public Task<CommandResult> ExecuteAsync(
        string input,
        CommandExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new CommandResult(true, _renderer.Render(context)));
    }
}
