namespace ClawSharp.Core;

public sealed class McpAuthError : Exception
{
    public McpAuthError(string serverName, string message)
        : base(message)
    {
        ServerName = serverName;
    }

    public string ServerName { get; }
}

public sealed class AuthenticationCancelledError : Exception
{
    public AuthenticationCancelledError()
        : base("Authentication was cancelled")
    {
    }
}
