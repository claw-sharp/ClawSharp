namespace ClawSharp.Core;

public sealed class McpSessionExpiredError : Exception
{
    public McpSessionExpiredError(string serverName)
        : base($"MCP server \"{serverName}\" session expired")
    {
        ServerName = serverName;
    }

    public string ServerName { get; }
}
