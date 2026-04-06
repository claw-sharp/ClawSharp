namespace ClawSharp.Core;

/// <summary>
/// Service to check if a command is available based on current authentication and environment state.
/// </summary>
public interface ICommandAvailabilityService
{
    bool IsAvailable(CommandDescriptor descriptor);
}
