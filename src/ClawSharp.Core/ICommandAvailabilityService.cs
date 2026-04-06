namespace ClawSharp.Core;

/// <summary>
/// Service to check if a command is available based on current authentication and environment state.
/// TS origin: meetsAvailabilityRequirement in commands.ts
/// </summary>
public interface ICommandAvailabilityService
{
    bool IsAvailable(CommandDescriptor descriptor);
}
