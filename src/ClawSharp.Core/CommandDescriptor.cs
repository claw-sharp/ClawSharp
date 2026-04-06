// TS origin: ./commands.ts
namespace ClawSharp.Core;

public sealed record CommandDescriptor(
    string Name,
    string Description,
    string Usage,
    bool IsInteractive = false,
    IReadOnlyList<string>? Aliases = null,
    IReadOnlyList<CommandAvailability>? Availability = null,
    bool IsEnabled = true,
    bool IsRemoteSafe = false,
    bool IsBridgeSafe = false);
