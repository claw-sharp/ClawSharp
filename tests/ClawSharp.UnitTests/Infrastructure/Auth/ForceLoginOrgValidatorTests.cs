using ClawSharp.Core;
using ClawSharp.Infrastructure;

namespace ClawSharp.UnitTests;

public sealed class ForceLoginOrgValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ReturnsSuccess_WhenManagedOrgRequirementIsAbsent()
    {
        var settings = new ClawSharpSettings();
        var result = await ForceLoginOrgValidator.ValidateAsync(
            settings,
            CreateDependencies(
                getTokens: () => null,
                getProfileAsync: (_, _) => Task.FromResult<ForceLoginOrgProfile?>(null)));

        Assert.True(result.Valid);
        Assert.Null(result.Message);
    }

    [Fact]
    public async Task ValidateAsync_FailsClosed_WhenProfileCannotBeFetched()
    {
        var result = await ForceLoginOrgValidator.ValidateAsync(
            new ClawSharpSettings { ForceLoginOrgUUID = "required-org" },
            CreateDependencies(
                getTokens: () => new ForceLoginOrgOAuthTokens("access-token"),
                getProfileAsync: (_, _) => Task.FromResult<ForceLoginOrgProfile?>(null)));

        Assert.False(result.Valid);
        Assert.Contains("This machine requires organization required-org", result.Message, StringComparison.Ordinal);
        Assert.Contains("claude auth login", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsSuccess_WhenTokenMatchesRequiredOrg()
    {
        var result = await ForceLoginOrgValidator.ValidateAsync(
            new ClawSharpSettings { ForceLoginOrgUUID = "required-org" },
            CreateDependencies(
                getTokens: () => new ForceLoginOrgOAuthTokens("access-token"),
                getProfileAsync: (_, _) => Task.FromResult<ForceLoginOrgProfile?>(new ForceLoginOrgProfile("required-org"))));

        Assert.True(result.Valid);
    }

    [Fact]
    public async Task ValidateAsync_UsesEnvironmentSpecificMessage_ForEnvVarTokens()
    {
        var result = await ForceLoginOrgValidator.ValidateAsync(
            new ClawSharpSettings { ForceLoginOrgUUID = "required-org" },
            CreateDependencies(
                getAuthTokenSource: () => new ForceLoginOrgAuthTokenSourceInfo("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", true),
                getTokens: () => new ForceLoginOrgOAuthTokens("access-token"),
                getProfileAsync: (_, _) => Task.FromResult<ForceLoginOrgProfile?>(new ForceLoginOrgProfile("other-org"))));

        Assert.False(result.Valid);
        Assert.Contains("CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", result.Message, StringComparison.Ordinal);
        Assert.Contains("Required organization: required-org", result.Message, StringComparison.Ordinal);
        Assert.Contains("Token organization:   other-org", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_UsesInteractiveReloginMessage_ForNonEnvVarTokens()
    {
        var result = await ForceLoginOrgValidator.ValidateAsync(
            new ClawSharpSettings { ForceLoginOrgUUID = "required-org" },
            CreateDependencies(
                getAuthTokenSource: () => new ForceLoginOrgAuthTokenSourceInfo("claude.ai", true),
                getTokens: () => new ForceLoginOrgOAuthTokens("access-token"),
                getProfileAsync: (_, _) => Task.FromResult<ForceLoginOrgProfile?>(new ForceLoginOrgProfile("other-org"))));

        Assert.False(result.Valid);
        Assert.Contains("Your authentication token belongs to organization other-org", result.Message, StringComparison.Ordinal);
        Assert.Contains("Please log in with the correct organization: claude auth login", result.Message, StringComparison.Ordinal);
    }

    private static ForceLoginOrgValidationDependencies CreateDependencies(
        Func<ForceLoginOrgAuthTokenSourceInfo>? getAuthTokenSource = null,
        Func<ForceLoginOrgOAuthTokens?>? getTokens = null,
        Func<string, CancellationToken, Task<ForceLoginOrgProfile?>>? getProfileAsync = null)
    {
        return new ForceLoginOrgValidationDependencies(
            IsAnthropicAuthEnabled: () => true,
            CheckAndRefreshOAuthTokenIfNeededAsync: _ => Task.CompletedTask,
            GetAuthTokenSource: getAuthTokenSource ?? (() => new ForceLoginOrgAuthTokenSourceInfo("claude.ai", true)),
            GetOAuthTokens: getTokens ?? (() => new ForceLoginOrgOAuthTokens("access-token")),
            GetOauthProfileFromOauthTokenAsync: getProfileAsync ?? ((_, _) => Task.FromResult<ForceLoginOrgProfile?>(new ForceLoginOrgProfile("required-org"))));
    }
}
