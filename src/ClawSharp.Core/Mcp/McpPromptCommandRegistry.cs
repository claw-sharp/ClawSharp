namespace ClawSharp.Core;

public sealed class McpPromptCommandRegistry
{
    private readonly Dictionary<string, IMcpPromptCommandHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public void RegisterOrReplace(IMcpPromptCommandHandler handler)
    {
        _handlers[handler.Descriptor.Name] = handler;
    }

    public IReadOnlyList<CommandDescriptor> GetAllDescriptors()
    {
        return _handlers.Values
            .Select(handler => handler.Descriptor)
            .Distinct()
            .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public bool TryResolve(string commandName, out IMcpPromptCommandHandler? handler)
    {
        return _handlers.TryGetValue(commandName, out handler);
    }
}
