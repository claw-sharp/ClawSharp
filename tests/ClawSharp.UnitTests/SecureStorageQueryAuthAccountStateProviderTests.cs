// TS origin: ./utils/auth.ts, ./services/api/withRetry.ts
// TS parity status: focused C# coverage for the Claude AI OAuth account-state provider that feeds the subscriber-gated query retry branches.
using ClawSharp.Core;
using ClawSharp.Infrastructure;
using ClawSharp.Query;

namespace ClawSharp.UnitTests;

public sealed class SecureStorageQueryAuthAccountStateProviderTests
{
    [Fact]
    public void GetState_Reads_Enterprise_Subscriber_Metadata_From_Secure_Storage()
    {
        var storage = new InMemorySecureStorage(
            new McpSecureStorageData(
                ClaudeAiOauth: new ClaudeAiOAuthEntry(
                    AccessToken: "oauth-token",
                    RefreshToken: "refresh-token",
                    ExpiresAt: 123,
                    Scopes: ["user:inference", "user:profile"],
                    SubscriptionType: "enterprise",
                    RateLimitTier: "default_enterprise")));
        var provider = new SecureStorageQueryAuthAccountStateProvider(storage);

        var state = provider.GetState(
            QueryTurnRequest.Create(new ConversationSession("session", "root", "path"), "hello"),
            QueryLoopStateFactory.CreateInitial([]),
            new ConversationSession("session", "root", "path"),
            new ClawSharpSettings());

        Assert.True(state.IsClaudeAiSubscriber);
        Assert.True(state.IsEnterpriseSubscriber);
        Assert.Equal("enterprise", state.SubscriptionType);
        Assert.Equal("default_enterprise", state.RateLimitTier);
    }

    [Fact]
    public void GetState_Treats_Env_OAuth_Token_As_NonEnterprise_Subscriber()
    {
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["CLAUDE_CODE_OAUTH_TOKEN"] = "token"
        };
        var provider = new SecureStorageQueryAuthAccountStateProvider(
            new InMemorySecureStorage(),
            getEnvironmentVariable: name => environment.GetValueOrDefault(name));

        var state = provider.GetState(
            QueryTurnRequest.Create(new ConversationSession("session", "root", "path"), "hello"),
            QueryLoopStateFactory.CreateInitial([]),
            new ConversationSession("session", "root", "path"),
            new ClawSharpSettings());

        Assert.True(state.IsClaudeAiSubscriber);
        Assert.False(state.IsEnterpriseSubscriber);
        Assert.Null(state.SubscriptionType);
    }

    [Fact]
    public void GetState_Treats_FileDescriptor_OAuth_Token_As_NonEnterprise_Subscriber()
    {
        var tokenFile = Path.Combine(Path.GetTempPath(), "clawsharp-query-auth-state-tests", Guid.NewGuid().ToString("N"), ".oauth_token");
        Directory.CreateDirectory(Path.GetDirectoryName(tokenFile)!);
        File.WriteAllText(tokenFile, "fd-token");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        var provider = new SecureStorageQueryAuthAccountStateProvider(
            new InMemorySecureStorage(),
            new ClaudeAiOAuthTokenSource(tokenFile, name => environment.GetValueOrDefault(name)),
            getEnvironmentVariable: name => environment.GetValueOrDefault(name));

        var state = provider.GetState(
            QueryTurnRequest.Create(new ConversationSession("session", "root", "path"), "hello"),
            QueryLoopStateFactory.CreateInitial([]),
            new ConversationSession("session", "root", "path"),
            new ClawSharpSettings());

        Assert.True(state.IsClaudeAiSubscriber);
        Assert.False(state.IsEnterpriseSubscriber);
        Assert.Null(state.SubscriptionType);
    }

    private sealed class InMemorySecureStorage : IMcpSecureStorage
    {
        private readonly McpSecureStorageData? _data;

        public InMemorySecureStorage(McpSecureStorageData? data = null)
        {
            _data = data;
        }

        public McpSecureStorageData? Read() => _data;

        public Task<McpSecureStorageData?> ReadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_data);
        }

        public void Update(McpSecureStorageData data)
        {
            throw new NotSupportedException();
        }

        public bool Delete()
        {
            throw new NotSupportedException();
        }
    }
}
