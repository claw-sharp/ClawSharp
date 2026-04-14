namespace ClawSharp.Core;

public sealed class McpPromptCommandRegistry
{
    private readonly Dictionary<string, IMcpPromptCommandHandler> _handlers =
        new(StringComparer.OrdinalIgnoreCase);

    public void RegisterOrReplace(IMcpPromptCommandHandler handler)
    {
        _handlers[handler.Descriptor.Name] = handler;
    }

    public void UnregisterWhere(Func<string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (var name in _handlers.Keys.Where(predicate).ToArray())
        {
            _handlers.Remove(name);
        }
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
