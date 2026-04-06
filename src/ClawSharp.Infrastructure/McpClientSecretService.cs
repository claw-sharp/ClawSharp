namespace ClawSharp.Infrastructure;

public sealed class McpClientSecretService
{
    private readonly TextReader _input;
    private readonly TextWriter _error;
    private readonly Func<bool> _isInputInteractive;

    public McpClientSecretService(
        TextReader? input = null,
        TextWriter? error = null,
        Func<bool>? isInputInteractive = null)
    {
        _input = input ?? Console.In;
        _error = error ?? Console.Error;
        _isInputInteractive = isInputInteractive ?? (() => !Console.IsInputRedirected);
    }

    public async Task<string> ReadAsync(CancellationToken cancellationToken = default)
    {
        var envSecret = Environment.GetEnvironmentVariable("MCP_CLIENT_SECRET");
        if (!string.IsNullOrEmpty(envSecret))
        {
            return envSecret;
        }

        if (!_isInputInteractive())
        {
            throw new InvalidOperationException(
                "No TTY available to prompt for client secret. Set MCP_CLIENT_SECRET env var instead.");
        }

        await _error.WriteAsync("Enter OAuth client secret: ").ConfigureAwait(false);
        var secret = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        await _error.WriteLineAsync().ConfigureAwait(false);

        if (secret is null)
        {
            throw new InvalidOperationException("Cancelled");
        }

        return secret;
    }
}
