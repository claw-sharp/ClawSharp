# ClawSharp Authentication

This document explains how the current ClawSharp C# runtime authenticates a user in two supported modes:

- API authentication via `ANTHROPIC_API_KEY`
- Claude login authentication via Claude.ai OAuth-style tokens already available to the process or secure storage

This describes the current implementation, not the final intended parity state.

## Summary

ClawSharp currently supports two practical authentication paths:

1. API key mode
2. Claude login token mode

There is also now a REPL convenience command for the API-key path:

- `/add-claude-key <api-key>`

Important current limitation:

- the C# runtime consumes Claude login credentials, but does not currently expose a first-class `/login` command in the REPL
- some bridge and parity messages still refer to `/login`, but the current runtime path is mainly about reading existing tokens rather than launching the interactive login flow itself

## Authentication Decision Order

The current model HTTP config provider resolves credentials in this order:

1. `ANTHROPIC_API_KEY`
2. `ANTHROPIC_AUTH_TOKEN`
3. `CLAUDE_CODE_OAUTH_TOKEN`
4. token from `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR`
5. token from the well-known OAuth token file
6. persisted Claude AI OAuth token from secure storage

Important behavior:

- if `ANTHROPIC_API_KEY` is set, ClawSharp uses API key mode
- when API key mode is active, OAuth-style auth token fallbacks are ignored for the model transport

Implementation source:

- `ClawSharp/src/ClawSharp.Infrastructure/EnvironmentQueryModelHttpClientConfigProvider.cs`

## Mode 1: API Authentication

Use API authentication when you want ClawSharp to call the model with an Anthropic API key.

### Required Environment Variable

- `ANTHROPIC_API_KEY`

### Optional Environment Variable

- `ANTHROPIC_BASE_URL`

If `ANTHROPIC_BASE_URL` is not set, ClawSharp defaults to:

- `https://api.anthropic.com`

### PowerShell Example

```powershell
$env:ANTHROPIC_API_KEY = "your-api-key"
dotnet run --project .\ClawSharp\src\ClawSharp.Cli -- repl
```

### REPL Command Example

Inside the ClawSharp REPL:

```text
/add-claude-key your-api-key
```

What this does in the current implementation:

- sets `ANTHROPIC_API_KEY` in the current process so the same REPL session can use it immediately
- stores the key in ClawSharp user settings as `claudeApiKey` so it can be reused on the next launch

With custom base URL:

```powershell
$env:ANTHROPIC_API_KEY = "your-api-key"
$env:ANTHROPIC_BASE_URL = "https://your-proxy-or-gateway.example.com"
dotnet run --project .\ClawSharp\src\ClawSharp.Cli -- repl
```

### What Happens Internally

When `ANTHROPIC_API_KEY` is present:

- `EnvironmentQueryModelHttpClientConfigProvider` returns `ApiKey = <value>`
- `AuthToken` is left `null`
- OAuth token fallbacks are not used

### When To Use This Mode

Use API mode when:

- you are integrating against Anthropic API billing
- you want explicit environment-driven credentials
- you do not need Claude.ai subscription-specific behavior

## Mode 2: Claude Login Authentication

Use Claude login authentication when you want ClawSharp to consume a Claude.ai login token instead of an API key.

### Important Current Reality

Today, ClawSharp mainly reads Claude login credentials from existing token sources. In practice, that usually means:

- you already logged in with the upstream Claude CLI
- or a parent/runtime environment injected a Claude OAuth token
- or a persisted secure-storage token is already available

This is a consume-existing-token flow more than a start-login flow.

## Claude Login Token Sources

If `ANTHROPIC_API_KEY` is not set, ClawSharp checks these sources.

### 1. `ANTHROPIC_AUTH_TOKEN`

This is the first auth-token fallback after API key mode.

PowerShell example:

```powershell
$env:ANTHROPIC_AUTH_TOKEN = "your-auth-token"
dotnet run --project .\ClawSharp\src\ClawSharp.Cli -- repl
```

### 2. `CLAUDE_CODE_OAUTH_TOKEN`

This is the next fallback.

PowerShell example:

```powershell
$env:CLAUDE_CODE_OAUTH_TOKEN = "your-claude-oauth-token"
dotnet run --project .\ClawSharp\src\ClawSharp.Cli -- repl
```

### 3. `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR`

On supported Unix-like environments, ClawSharp can read a token from a file descriptor path.

Implementation source:

- `ClawSharp/src/ClawSharp.Infrastructure/ClaudeAiOAuthTokenSource.cs`

Notes:

- this is mainly relevant for Linux/macOS-style process environments
- on Windows, this descriptor path mechanism is generally not the path you should expect to use

### 4. Well-Known OAuth Token File

If file descriptor lookup does not produce a token, ClawSharp also checks a well-known token file path used by the Claude remote runtime flow.

Current constant:

- `/home/claude/.claude/remote/.oauth_token`

This is mainly relevant for remote/containerized Claude environments, not a typical Windows local workflow.

### 5. Persisted Secure Storage

If no env/file token is found, ClawSharp falls back to persisted Claude AI OAuth data from secure storage and uses:

- `ClaudeAiOauth.AccessToken`

Implementation sources:

- `ClawSharp/src/ClawSharp.Infrastructure/EnvironmentQueryModelHttpClientConfigProvider.cs`
- `ClawSharp/src/ClawSharp.Infrastructure/SecureStorageQueryAuthAccountStateProvider.cs`

## How To Use Claude Login In Practice

For a developer workflow, the practical path is:

1. log in with the upstream Claude CLI so a valid Claude token exists
2. start ClawSharp in an environment where that token is available
3. do not set `ANTHROPIC_API_KEY`, otherwise API mode wins

Example practical flow:

```powershell
claude auth login
dotnet run --project .\ClawSharp\src\ClawSharp.Cli -- repl
```

Important caveat:

- the current ClawSharp C# runtime does not itself guarantee the full interactive login flow end to end
- the most reliable interpretation is that it consumes tokens produced by `claude auth login`

## How ClawSharp Detects Claude Login State

`SecureStorageQueryAuthAccountStateProvider` treats Claude login auth as enabled only when all of the following are true:

- `CLAUDE_CODE_USE_BEDROCK` is not enabled
- `CLAUDE_CODE_USE_VERTEX` is not enabled
- `CLAUDE_CODE_USE_FOUNDRY` is not enabled
- `ANTHROPIC_API_KEY` is not set
- `ANTHROPIC_AUTH_TOKEN` is not set

Then it checks:

- `CLAUDE_CODE_OAUTH_TOKEN`
- file-descriptor / well-known-file token source
- secure storage OAuth entry

That means API-key mode and Claude-login mode are treated as mutually exclusive at this layer.

## Organization-Restricted Login

The codebase also contains organization validation logic for managed environments via:

- `ForceLoginOrgUUID` in settings
- `ForceLoginOrgValidator`

This logic can reject a token when:

- the token lacks the required organization
- the token lacks enough scope to fetch the profile
- the token came from an env var for the wrong organization

Implementation source:

- `ClawSharp/src/ClawSharp.Infrastructure/ForceLoginOrgValidator.cs`

Notable behavior:

- tokens from `claude setup-token` may not include the `user:profile` scope required for organization verification
- the validator explicitly recommends `claude auth login` when a full-scope token is required

## Feature Implications

Some features are conceptually tied to Claude.ai login rather than raw API key auth.

Example:

- bridge/remote control messages state that remote control requires a Claude.ai subscription login

Implementation source:

- `ClawSharp/src/ClawSharp.Bridge/BridgeContracts.cs`

So if a feature depends on Claude.ai account state rather than simple model access, API key mode may not be sufficient.

## Recommended Developer Recipes

### Recipe A: API Key Local Dev

Use this when you only need model access.

```powershell
$env:ANTHROPIC_API_KEY = "your-api-key"
dotnet run --project .\ClawSharp\src\ClawSharp.Cli -- repl
```

Or after the REPL starts:

```text
/add-claude-key your-api-key
```

### Recipe B: Claude Login Local Dev

Use this when you want Claude.ai-backed auth behavior.

```powershell
claude auth login
Remove-Item Env:ANTHROPIC_API_KEY -ErrorAction SilentlyContinue
dotnet run --project .\ClawSharp\src\ClawSharp.Cli -- repl
```

### Recipe C: Explicit Token Injection

Use this when a parent tool or test harness already has a Claude token.

```powershell
$env:CLAUDE_CODE_OAUTH_TOKEN = "your-oauth-token"
Remove-Item Env:ANTHROPIC_API_KEY -ErrorAction SilentlyContinue
dotnet run --project .\ClawSharp\src\ClawSharp.Cli -- repl
```

## Troubleshooting

### ClawSharp is using the wrong auth mode

Check whether `ANTHROPIC_API_KEY` is set.

If it is set, API key mode wins.

### Claude login does not seem to be picked up

Check in this order:

1. `ANTHROPIC_API_KEY` is not set
2. `ANTHROPIC_AUTH_TOKEN` is not set unless you intend to use it
3. `CLAUDE_CODE_OAUTH_TOKEN` is present, or
4. a persisted Claude OAuth token exists in secure storage

### Managed organization validation fails

Possible causes:

- token belongs to the wrong organization
- token lacks `user:profile` scope
- an env var token is overriding the expected logged-in token

In those cases, the code explicitly points developers toward:

```powershell
claude auth login
```

### Authentication looks correct but requests still fail

Authentication is only one part of startup. Also verify:

- the runtime model is configured appropriately
- the base URL is correct
- the target provider endpoint matches the auth mode you are using

Current note:

- `RuntimeSettings.Model` still defaults to `foundation-placeholder` in the current C# runtime unless overridden elsewhere

## Relevant Source Files

- `ClawSharp/src/ClawSharp.Infrastructure/AddClaudeKeyCommandHandler.cs`
- `ClawSharp/src/ClawSharp.Infrastructure/EnvironmentQueryModelHttpClientConfigProvider.cs`
- `ClawSharp/src/ClawSharp.Infrastructure/SecureStorageQueryAuthAccountStateProvider.cs`
- `ClawSharp/src/ClawSharp.Infrastructure/ClaudeAiOAuthTokenSource.cs`
- `ClawSharp/src/ClawSharp.Infrastructure/ForceLoginOrgValidator.cs`
- `ClawSharp/src/ClawSharp.Bridge/BridgeContracts.cs`
