namespace ClawSharp.AgentHost.Ipc;

public sealed class AgentHostException : Exception
{
    public AgentHostException(string code, string message, string? details = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    public string? Details { get; }
}
