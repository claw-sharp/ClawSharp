// TS origin: ./utils/auth.ts
using ClawSharp.Core;

namespace ClawSharp.Infrastructure;

public static class ForceLoginOrgValidator
{
    public static async Task<ForceLoginOrgValidationResult> ValidateAsync(
        ClawSharpSettings settings,
        ForceLoginOrgValidationDependencies dependencies,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dependencies);

        cancellationToken.ThrowIfCancellationRequested();

        if (dependencies.IsUnixSocketMode)
        {
            return ForceLoginOrgValidationResult.Success();
        }

        if (!dependencies.IsAnthropicAuthEnabled())
        {
            return ForceLoginOrgValidationResult.Success();
        }

        var requiredOrgUuid = settings.ForceLoginOrgUUID;
        if (string.IsNullOrWhiteSpace(requiredOrgUuid))
        {
            return ForceLoginOrgValidationResult.Success();
        }

        await dependencies.CheckAndRefreshOAuthTokenIfNeededAsync(cancellationToken).ConfigureAwait(false);
        var tokens = dependencies.GetOAuthTokens();
        if (tokens is null || string.IsNullOrWhiteSpace(tokens.AccessToken))
        {
            return ForceLoginOrgValidationResult.Success();
        }

        var source = dependencies.GetAuthTokenSource();
        var isEnvVarToken =
            string.Equals(source.Source, "CLAUDE_CODE_OAUTH_TOKEN", StringComparison.Ordinal) ||
            string.Equals(source.Source, "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR", StringComparison.Ordinal);

        var profile = await dependencies.GetOauthProfileFromOauthTokenAsync(tokens.AccessToken, cancellationToken)
            .ConfigureAwait(false);
        if (profile is null || string.IsNullOrWhiteSpace(profile.OrganizationUuid))
        {
            return ForceLoginOrgValidationResult.Failure(
                "Unable to verify organization for the current authentication token.\n" +
                $"This machine requires organization {requiredOrgUuid} but the profile could not be fetched.\n" +
                "This may be a network error, or the token may lack the user:profile scope required for\n" +
                "verification (tokens from 'claude setup-token' do not include this scope).\n" +
                "Try again, or obtain a full-scope token via 'claude auth login'.");
        }

        var tokenOrgUuid = profile.OrganizationUuid;
        if (string.Equals(tokenOrgUuid, requiredOrgUuid, StringComparison.Ordinal))
        {
            return ForceLoginOrgValidationResult.Success();
        }

        if (isEnvVarToken)
        {
            var envVarName = string.Equals(source.Source, "CLAUDE_CODE_OAUTH_TOKEN", StringComparison.Ordinal)
                ? "CLAUDE_CODE_OAUTH_TOKEN"
                : "CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR";
            return ForceLoginOrgValidationResult.Failure(
                $"The {envVarName} environment variable provides a token for a\n" +
                "different organization than required by this machine's managed settings.\n\n" +
                $"Required organization: {requiredOrgUuid}\n" +
                $"Token organization:   {tokenOrgUuid}\n\n" +
                "Remove the environment variable or obtain a token for the correct organization.");
        }

        return ForceLoginOrgValidationResult.Failure(
            $"Your authentication token belongs to organization {tokenOrgUuid},\n" +
            $"but this machine requires organization {requiredOrgUuid}.\n\n" +
            "Please log in with the correct organization: claude auth login");
    }
}

public sealed record ForceLoginOrgValidationDependencies(
    Func<bool> IsAnthropicAuthEnabled,
    Func<CancellationToken, Task> CheckAndRefreshOAuthTokenIfNeededAsync,
    Func<ForceLoginOrgAuthTokenSourceInfo> GetAuthTokenSource,
    Func<ForceLoginOrgOAuthTokens?> GetOAuthTokens,
    Func<string, CancellationToken, Task<ForceLoginOrgProfile?>> GetOauthProfileFromOauthTokenAsync,
    bool IsUnixSocketMode = false);

public sealed record ForceLoginOrgAuthTokenSourceInfo(
    string Source,
    bool HasToken);

public sealed record ForceLoginOrgOAuthTokens(
    string AccessToken);

public sealed record ForceLoginOrgProfile(
    string OrganizationUuid);

public sealed record ForceLoginOrgValidationResult(
    bool Valid,
    string? Message = null)
{
    public static ForceLoginOrgValidationResult Success()
    {
        return new ForceLoginOrgValidationResult(true);
    }

    public static ForceLoginOrgValidationResult Failure(string message)
    {
        return new ForceLoginOrgValidationResult(false, message);
    }
}
