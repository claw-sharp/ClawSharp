using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class ProviderCommandHandlerTests
{
    [Fact]
    public async Task ExecuteAsync_Configures_OpenAi_Compatible_Provider_In_Settings()
    {
        using var openAiScope = new EnvironmentVariableScope("CLAUDE_CODE_USE_OPENAI", null);
        using var geminiScope = new EnvironmentVariableScope("CLAUDE_CODE_USE_GEMINI", null);
        using var githubScope = new EnvironmentVariableScope("CLAUDE_CODE_USE_GITHUB", null);
        var handler = new ProviderCommandHandler();
        var settings = new ClawSharpSettings();
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                "D:\\repo",
                new StartupEnvironment("D:\\config"),
                settings,
                [],
                [],
                [],
                [],
                [],
                []));

        var result = await handler.ExecuteAsync(
            "/provider openai gpt-4o https://api.openai.com/v1 sk-openai",
            CreateContext(appStateStore, settings));

        var updatedSettings = appStateStore.GetState().Settings;

        Assert.True(result.Success);
        Assert.Equal("gpt-4o", updatedSettings.Runtime.Model);
        Assert.Equal("https://api.openai.com/v1", updatedSettings.AgentModels["gpt-4o"].BaseUrl);
        Assert.Equal("sk-openai", updatedSettings.AgentModels["gpt-4o"].ApiKey);
        Assert.Equal("gpt-4o", updatedSettings.AgentRouting["default"]);
    }

    [Fact]
    public async Task ExecuteAsync_Reports_Current_Provider_When_No_Arguments_Are_Passed()
    {
        using var openAiScope = new EnvironmentVariableScope("CLAUDE_CODE_USE_OPENAI", null);
        using var geminiScope = new EnvironmentVariableScope("CLAUDE_CODE_USE_GEMINI", null);
        using var githubScope = new EnvironmentVariableScope("CLAUDE_CODE_USE_GITHUB", null);
        var handler = new ProviderCommandHandler();
        var settings = new ClawSharpSettings();
        var appStateStore = new ClawSharpAppStateStore(
            ClawSharpAppState.CreateDefault(
                "D:\\repo",
                new StartupEnvironment("D:\\config"),
                settings,
                [],
                [],
                [],
                [],
                [],
                []));

        var result = await handler.ExecuteAsync(
            "/provider",
            CreateContext(appStateStore, settings));

        Assert.True(result.Success);
        Assert.Contains("Provider: Anthropic", result.Output, StringComparison.Ordinal);
        Assert.Contains("Usage: /provider", result.Output, StringComparison.Ordinal);
    }

    private static CommandExecutionContext CreateContext(
        IClawSharpAppStateStore appStateStore,
        ClawSharpSettings settings)
    {
        return new CommandExecutionContext
        {
            AppStateStore = appStateStore,
            Session = new ConversationSession("session-provider", "D:\\repo"),
            SessionFactory = new DefaultSessionFactory("D:\\repo"),
            TranscriptStore = new JsonlTranscriptStore(),
            Settings = settings
        };
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previousValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _previousValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previousValue);
        }
    }
}
