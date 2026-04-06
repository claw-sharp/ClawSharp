namespace ClawSharp.Core;

public sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommandHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ICommandAvailabilityService _availabilityService;

    public CommandRegistry()
        : this(new NullCommandAvailabilityService())
    {
    }

    public CommandRegistry(ICommandAvailabilityService availabilityService)
    {
        _availabilityService = availabilityService;
    }

    public void Register(ICommandHandler handler)
    {
        _handlers[handler.Descriptor.Name] = handler;
        foreach (var alias in handler.Descriptor.Aliases ?? [])
        {
            _handlers[alias] = handler;
        }
    }

    public IReadOnlyList<CommandDescriptor> GetAllDescriptors()
    {
        return _handlers.Values
            .Select(handler => handler.Descriptor)
            .Distinct()
            .Where(static descriptor => descriptor.IsEnabled)
            .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<CommandDescriptor> GetAvailableDescriptors()
    {
        return _handlers.Values
            .Select(handler => handler.Descriptor)
            .Distinct()
            .Where(descriptor => descriptor.IsEnabled && _availabilityService.IsAvailable(descriptor))
            .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryResolve(string commandName, out ICommandHandler? handler)
    {
        if (_handlers.TryGetValue(commandName, out handler) &&
            handler is not null &&
            handler.Descriptor.IsEnabled)
        {
            return true;
        }

        handler = null;
        return false;
    }
}
