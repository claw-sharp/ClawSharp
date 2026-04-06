namespace ClawSharp.Core;

public sealed class NullCommandAvailabilityService : ICommandAvailabilityService
{
    public bool IsAvailable(CommandDescriptor descriptor) => true;
}
