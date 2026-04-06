# ClawSharp Authentication And Provider Setup

This document explains how the current ClawSharp runtime selects a provider, which credentials it reads, and how to configure each supported provider family.

This reflects the current C# implementation in the repo today. It is much broader than the old Anthropic-only path, but it is still not perfect `openclaude` parity in every edge case.

## Summary

ClawSharp now supports these provider families at the runtime/config layer:

- Anthropic direct API
- Claude.ai OAuth-style Anthropic login token consumption
- OpenAI-compatible providers
- Gemini
- GitHub Models
- Codex
- Bedrock
- Vertex
- Foundry
- Ollama through the OpenAI-compatible path

There are now three main ways to choose the active provider:

1. environment variables
2. CLI flags: `--provider` and `--model`
3. REPL command: `/provider`

Important current behavior:

- provider selection is process-local unless you persist it through settings
- `/provider` updates the live app state and persisted settings for future turns
- direct provider selection is implemented; full profile-management parity with `openclaude` is not yet complete

## Provider Selection Order

At runtime, ClawSharp resolves the effective provider in this order:

1. a custom `agentModels` entry matching the selected model
2. explicit provider flags in the environment:
   - `CLAUDE_CODE_USE_GEMINI`
   - `CLAUDE_CODE_USE_GITHUB`
   - `CLAUDE_CODE_USE_OPENAI`
   - `CLAUDE_CODE_USE_BEDROCK`
   - `CLAUDE_CODE_USE_VERTEX`
   - `CLAUDE_CODE_USE_FOUNDRY`
3. Anthropic default

When `CLAUDE_CODE_USE_OPENAI=1`, ClawSharp further distinguishes:

- normal OpenAI-compatible chat-completions transport
- Codex `responses` transport when:
  - `OPENAI_BASE_URL` points at the Codex endpoint, or
  - the model is a Codex alias such as `codexplan` and no custom base URL overrides it

Implementation source:

- [ProviderRuntimeResolver.cs](/D:/Working/ClawSharp/src/ClawSharp.Core/ProviderRuntimeResolver.cs)

## Quick Start Recipes

### Anthropic API Key

```powershell
$env:ANTHROPIC_API_KEY = "your-api-key"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

### OpenAI

```powershell
$env:CLAUDE_CODE_USE_OPENAI = "1"
$env:OPENAI_API_KEY = "sk-openai"
$env:OPENAI_MODEL = "gpt-4o"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

### Gemini

```powershell
$env:CLAUDE_CODE_USE_GEMINI = "1"
$env:GEMINI_API_KEY = "AIza..."
$env:GEMINI_MODEL = "gemini-2.0-flash"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

### GitHub Models

```powershell
$env:CLAUDE_CODE_USE_GITHUB = "1"
$env:GITHUB_TOKEN = "ghp_..."
$env:OPENAI_MODEL = "github:copilot"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

### Codex

```powershell
$env:CLAUDE_CODE_USE_OPENAI = "1"
$env:OPENAI_MODEL = "codexplan"
$env:CODEX_API_KEY = "your-codex-token"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

### Ollama

```powershell
dotnet run --project .\src\ClawSharp.Cli -- --provider ollama --model llama3.2
```

## CLI And REPL Selection

### CLI Flags

You can choose the provider directly when launching the CLI:

```powershell
clawsharp --provider openai --model gpt-4o
clawsharp --provider gemini --model gemini-2.0-flash
clawsharp --provider codex --model codexplan
clawsharp --provider ollama --model llama3.2
```

Currently supported provider names:

- `anthropic`
- `openai`
- `gemini`
- `github`
- `bedrock`
- `vertex`
- `foundry`
- `codex`
- `ollama`

Important behavior:

- `--provider codex` sets the OpenAI-family switch plus the Codex base URL and model
- `--provider ollama` sets an OpenAI-compatible local endpoint and a placeholder API key of `ollama`
- `--model` overrides the active model for the launched session

Implementation source:

- [ProviderFlagUtilities.cs](/D:/Working/ClawSharp/src/ClawSharp.Infrastructure/ProviderFlagUtilities.cs)

### REPL Command

Inside the REPL:

```text
/provider
```

You can also provide arguments directly:

```text
/provider openai gpt-4o https://api.openai.com/v1 sk-openai
/provider codex codexplan https://chatgpt.com/backend-api/codex your-token account-id
/provider gemini gemini-2.0-flash https://generativelanguage.googleapis.com/v1beta/openai AIza...
```

What `/provider` does:

- updates live app state for subsequent turns
- updates persisted settings
- updates the current process provider environment for the current session

Implementation source:

- [ProviderCommandHandler.cs](/D:/Working/ClawSharp/src/ClawSharp.Infrastructure/ProviderCommandHandler.cs)

## Provider Matrix

### Anthropic Direct API

Primary variables:

- `ANTHROPIC_API_KEY`
- `ANTHROPIC_BASE_URL`
- `ANTHROPIC_MODEL`
- `CLAUDE_MODEL`

Behavior:

- if `ANTHROPIC_API_KEY` is set, ClawSharp uses API-key auth for Anthropic
- if `ANTHROPIC_BASE_URL` is absent, ClawSharp defaults to `https://api.anthropic.com`

### Claude.ai Login Token Consumption

If `ANTHROPIC_API_KEY` is not set, ClawSharp falls back to token sources in this order:

1. `ANTHROPIC_AUTH_TOKEN`
2. `CLAUDE_CODE_OAUTH_TOKEN`
3. token from `CLAUDE_CODE_OAUTH_TOKEN_FILE_DESCRIPTOR`
4. token from the well-known OAuth token file
5. persisted Claude AI OAuth token from secure storage

Notes:

- ClawSharp currently consumes existing login tokens; it does not yet expose full first-class `/login` parity in the REPL
- if an Anthropic API key is set, token fallback is skipped

### OpenAI-Compatible Providers

Primary variables:

- `CLAUDE_CODE_USE_OPENAI=1`
- `OPENAI_API_KEY`
- `OPENAI_MODEL`
- `OPENAI_BASE_URL`
- `OPENAI_API_BASE`
- `AZURE_OPENAI_API_VERSION`
- `OPENAI_API_VERSION`

Examples of providers that fit this path:

- OpenAI
- OpenRouter
- DeepSeek
- Groq
- Mistral
- Together
- LM Studio
- Ollama
- Azure OpenAI
- custom `/v1` compatible gateways

Behavior:

- transport is `/chat/completions`
- if the base URL is Azure-style, ClawSharp switches to Azure deployment URL shaping and uses `api-key` auth header
- local endpoints are treated as local-provider URLs and skip some cloud-only request decorations

### Gemini

Primary variables:

- `CLAUDE_CODE_USE_GEMINI=1`
- `GEMINI_MODEL`
- `GEMINI_BASE_URL`
- `GEMINI_API_KEY`
- `GOOGLE_API_KEY`
- `GEMINI_ACCESS_TOKEN`
- `GEMINI_AUTH_MODE`
- `GOOGLE_CLOUD_PROJECT`
- `GCLOUD_PROJECT`
- `GOOGLE_PROJECT_ID`

Behavior:

- Gemini currently rides the OpenAI-compatible transport
- `GEMINI_AUTH_MODE` can steer key vs access-token selection
- if a project id is present, ClawSharp adds `x-goog-user-project`

### GitHub Models

Primary variables:

- `CLAUDE_CODE_USE_GITHUB=1`
- `GITHUB_TOKEN`
- `GH_TOKEN`
- `OPENAI_MODEL`

Behavior:

- transport is OpenAI-compatible
- `github:copilot` normalizes to `openai/gpt-4.1`
- GitHub-specific headers are added automatically

### Bedrock

Primary variables:

- `CLAUDE_CODE_USE_BEDROCK=1`
- `BEDROCK_MODEL`
- `BEDROCK_BASE_URL`
- `BEDROCK_API_KEY`
- `AWS_BEARER_TOKEN_BEDROCK`
- `BEDROCK_AUTH_TOKEN`

Current state:

- ClawSharp resolves Bedrock as an Anthropic-shaped transport
- direct request signing parity with the upstream AWS SDK path is not fully implemented here yet
- if you have a reachable Bedrock-compatible proxy or bearer-token path, the runtime config layer can now pass that through

### Vertex

Primary variables:

- `CLAUDE_CODE_USE_VERTEX=1`
- `VERTEX_MODEL`
- `VERTEX_BASE_URL`
- `VERTEX_AUTH_TOKEN`
- `GOOGLE_ACCESS_TOKEN`
- `ANTHROPIC_VERTEX_PROJECT_ID`
- `GOOGLE_CLOUD_PROJECT`
- `GCLOUD_PROJECT`

Current state:

- ClawSharp resolves Vertex as an Anthropic-shaped transport
- if a project id is available, it adds `x-goog-user-project`
- full Google-auth-backed SDK parity is still incomplete

### Foundry

Primary variables:

- `CLAUDE_CODE_USE_FOUNDRY=1`
- `ANTHROPIC_FOUNDRY_MODEL`
- `ANTHROPIC_FOUNDRY_BASE_URL`
- `ANTHROPIC_FOUNDRY_RESOURCE`
- `ANTHROPIC_FOUNDRY_API_KEY`
- `ANTHROPIC_FOUNDRY_AUTH_TOKEN`

Current state:

- ClawSharp resolves Foundry as an Anthropic-shaped transport
- if `ANTHROPIC_FOUNDRY_RESOURCE` is set, ClawSharp infers `https://<resource>.services.ai.azure.com/anthropic`
- full Azure SDK credential parity is still incomplete

## Custom Model Connections With `agentModels`

ClawSharp now supports per-model connection entries in settings.

Example:

```json
{
  "runtime": {
    "model": "deepseek-chat"
  },
  "agentModels": {
    "deepseek-chat": {
      "provider": "openai",
      "baseUrl": "https://api.deepseek.com/v1",
      "apiKey": "sk-deepseek"
    },
    "github:copilot": {
      "provider": "github",
      "baseUrl": "https://models.github.ai/inference",
      "authToken": "ghp_xxx"
    },
    "codexplan": {
      "provider": "codex",
      "baseUrl": "https://chatgpt.com/backend-api/codex",
      "apiKey": "your-codex-token",
      "accountId": "your-chatgpt-account-id"
    }
  }
}
```

Supported fields on an `agentModels` entry:

- `baseUrl`
- `apiKey`
- `authToken`
- `accountId`
- `apiVersion`
- `provider`
- `headers`

Important behavior:

- if the selected model name matches an `agentModels` entry, that entry wins before the global provider env flags
- this is how routed subagents can use a different provider than the main thread

## Agent Routing With `agentRouting`

`agentRouting` maps agent names or subagent types to models defined in `agentModels`.

Example:

```json
{
  "agentModels": {
    "gpt-4o": {
      "provider": "openai",
      "baseUrl": "https://api.openai.com/v1",
      "apiKey": "sk-openai"
    },
    "deepseek-chat": {
      "provider": "openai",
      "baseUrl": "https://api.deepseek.com/v1",
      "apiKey": "sk-deepseek"
    }
  },
  "agentRouting": {
    "default": "gpt-4o",
    "Explore": "deepseek-chat"
  }
}
```

Resolution order:

1. explicit agent name
2. subagent type
3. `default`

Implementation sources:

- [AgentRoutingResolver.cs](/D:/Working/ClawSharp/src/ClawSharp.Core/AgentRoutingResolver.cs)
- [LocalAgentExecutionService.cs](/D:/Working/ClawSharp/src/ClawSharp.Infrastructure/LocalAgentExecutionService.cs)

## Codex Authentication

This is the most important provider-specific path to understand because Codex does not use the standard OpenAI chat-completions transport.

### What Transport Codex Uses

Codex uses:

- provider kind: `Codex`
- transport: `CodexResponses`
- endpoint: `/responses`

It is selected when either:

- `OPENAI_BASE_URL` points at `https://chatgpt.com/backend-api/codex`, or
- the model is a Codex alias such as `codexplan` and no custom OpenAI-compatible base URL overrides it

### Codex Model Aliases

Current aliases include:

- `codexplan` -> `gpt-5.4`
- `codexspark` -> `gpt-5.3-codex-spark`
- `gpt-5.3-codex`
- `gpt-5.3-codex-spark`
- `gpt-5.2-codex`
- `gpt-5.1-codex-max`
- `gpt-5.1-codex-mini`
- `gpt-5.4-mini`
- `gpt-5.2`

### Codex Credential Sources

ClawSharp resolves Codex credentials in this order:

1. `CODEX_API_KEY`
2. `CODEX_ACCOUNT_ID` or `CHATGPT_ACCOUNT_ID`
3. `auth.json` loaded from:
   - `CODEX_AUTH_JSON_PATH`, else
   - `CODEX_HOME\\auth.json`, else
   - `%USERPROFILE%\\.codex\\auth.json`

Implementation source:

- [ProviderRuntimeResolver.cs](/D:/Working/ClawSharp/src/ClawSharp.Core/ProviderRuntimeResolver.cs)

### Recommended Codex Setup: Environment Variables

If you already have a Codex token and account id:

```powershell
$env:CLAUDE_CODE_USE_OPENAI = "1"
$env:OPENAI_MODEL = "codexplan"
$env:CODEX_API_KEY = "your-codex-token"
$env:CHATGPT_ACCOUNT_ID = "your-chatgpt-account-id"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

Equivalent with provider flag:

```powershell
$env:CODEX_API_KEY = "your-codex-token"
$env:CHATGPT_ACCOUNT_ID = "your-chatgpt-account-id"
clawsharp --provider codex --model codexplan
```

### Recommended Codex Setup: `auth.json`

If you already use Codex tooling that writes `auth.json`, ClawSharp can reuse it.

Default location:

```text
%USERPROFILE%\.codex\auth.json
```

Custom location:

```powershell
$env:CODEX_AUTH_JSON_PATH = "D:\secrets\codex-auth.json"
clawsharp --provider codex --model codexplan
```

Or:

```powershell
$env:CODEX_HOME = "D:\codex-home"
clawsharp --provider codex --model codexplan
```

### What `auth.json` Must Contain

ClawSharp looks for an access token in several common fields, including:

- `access_token`
- `accessToken`
- `tokens.access_token`
- `tokens.accessToken`
- `auth.access_token`
- `auth.accessToken`
- `token.access_token`
- `token.accessToken`
- `tokens.id_token`
- `tokens.idToken`

If the token is a JWT and contains a ChatGPT account id claim, ClawSharp also tries to infer the account id from the token automatically.

Recognized account-id sources:

- `CODEX_ACCOUNT_ID`
- `CHATGPT_ACCOUNT_ID`
- JWT claims:
  - `https://api.openai.com/auth.chatgpt_account_id`
  - `chatgpt_account_id`

### How ClawSharp Sends Codex Auth

For Codex requests, ClawSharp sends:

- `Authorization: Bearer <token>`
- `chatgpt-account-id: <account-id>` when available
- `originator: clawsharp`

The request target is:

- `<base-url>/responses`

with the default base URL:

- `https://chatgpt.com/backend-api/codex`

### Codex Troubleshooting

#### Codex falls back to normal OpenAI transport

Check:

1. `CLAUDE_CODE_USE_OPENAI=1` is set, or you used `--provider codex`
2. `OPENAI_MODEL` is a Codex alias such as `codexplan`, or `OPENAI_BASE_URL` is the Codex base URL
3. you did not override `OPENAI_BASE_URL` to a normal OpenAI-compatible endpoint by mistake

#### Codex auth works in another tool but not in ClawSharp

Check:

1. `CODEX_API_KEY` is available, or
2. `auth.json` exists at `%USERPROFILE%\.codex\auth.json`, or
3. `CODEX_AUTH_JSON_PATH` points at the correct file

#### Codex account-specific requests fail

Set one of:

- `CODEX_ACCOUNT_ID`
- `CHATGPT_ACCOUNT_ID`

Even though ClawSharp can infer the account id from some JWTs, explicit env configuration is more reliable.

## Troubleshooting

### ClawSharp is using the wrong provider

Check:

1. whether a matching `agentModels` entry exists for the selected model
2. whether one of the `CLAUDE_CODE_USE_*` flags is already set in the environment
3. whether you launched with `--provider`
4. whether `/provider` changed the current app state

### Anthropic login token is ignored

If `ANTHROPIC_API_KEY` is set, API-key mode wins and OAuth-style token fallbacks are skipped.

### A child agent is using the wrong provider

Check:

1. `agentRouting`
2. matching `agentModels` entry for the routed model
3. whether the subagent requested an explicit model, which overrides routing

### Custom provider entry is not used

The selected model name must match the `agentModels` key. If the current model is `deepseek-chat`, the settings key must also be `deepseek-chat`.

## Relevant Implementation Files

- [ProviderRuntimeResolver.cs](/D:/Working/ClawSharp/src/ClawSharp.Core/ProviderRuntimeResolver.cs)
- [EnvironmentQueryModelHttpClientConfigProvider.cs](/D:/Working/ClawSharp/src/ClawSharp.Infrastructure/EnvironmentQueryModelHttpClientConfigProvider.cs)
- [QueryModelHttpRequestFactory.cs](/D:/Working/ClawSharp/src/ClawSharp.Query/QueryModelHttpRequestFactory.cs)
- [QueryModelSseStreamingClient.cs](/D:/Working/ClawSharp/src/ClawSharp.Query/QueryModelSseStreamingClient.cs)
- [ProviderCommandHandler.cs](/D:/Working/ClawSharp/src/ClawSharp.Infrastructure/ProviderCommandHandler.cs)
- [ProviderFlagUtilities.cs](/D:/Working/ClawSharp/src/ClawSharp.Infrastructure/ProviderFlagUtilities.cs)
- [AgentRoutingResolver.cs](/D:/Working/ClawSharp/src/ClawSharp.Core/AgentRoutingResolver.cs)
