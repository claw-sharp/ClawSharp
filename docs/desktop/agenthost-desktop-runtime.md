# Desktop Runtime Integration

## Why `ClawSharp.AgentHost` exists

The Tauri desktop app in `apps/desktop` is now backed by a dedicated C# sidecar process:

- `src/ClawSharp.AgentHost`

AgentHost exists to keep the desktop shell thin while reusing the real ClawSharp runtime:

- `ClawSharp.Core`
- `ClawSharp.Infrastructure`
- `ClawSharp.Query`
- `ClawSharp.Tools`
- `ClawSharp.Tasks`

It does **not** scrape the terminal UI or move runtime orchestration into Rust.

If desktop needs behavior that already exists in `openclaude`, copy or port that runtime/query logic into the host-side implementation from `D:\Working\openclaude` instead of rebuilding it in the TypeScript UI layer.

## Desktop architecture

The desktop stack is split into three layers:

1. `apps/desktop/src`
   - React UI
   - Zustand store
   - typed client layer in `src/lib`
2. `apps/desktop/src-tauri`
   - launches AgentHost as a sidecar
   - relays `stdio`
   - exposes a very small Tauri command bridge
3. `src/ClawSharp.AgentHost`
   - boots the real ClawSharp runtime per workspace
   - maps runtime objects into desktop DTOs
   - streams run and tool events back to the UI

## Transport

Desktop talks to AgentHost over newline-delimited JSON on `stdio`.

- Requests are command envelopes with `requestId`, `command`, and typed payloads.
- Responses are typed success or error envelopes.
- Events are pushed asynchronously on the same stream.

This keeps startup simple, avoids a long-running network server, and works cleanly for local sidecar packaging.

## Implemented vertical slices

### Phase 1

- AgentHost bootstrap
- project open / recent project tracking
- thread listing, creation, and transcript-backed history loading

### Phase 2

- typed Tauri bridge and desktop client
- real project and thread loading in the desktop UI
- mock runtime path removed from the live store

### Phase 3

- real `QueryEngine` run execution from the desktop composer
- streaming text deltas
- tool progress and tool result events
- run completion, failure, and cancellation handling

### Phase 4

- runtime-backed settings and provider information
- provider validation feedback
- diagnostics/log path surfacing from existing telemetry helpers

### Phase 5

- workspace-level changed files
- unified diff retrieval for the review pane
- external editor launch through AgentHost

## Protocol overview

The current desktop client uses these AgentHost commands:

- `health`
- `openProject`
- `listRecentProjects`
- `listThreads`
- `createThread`
- `getThread`
- `renameThread`
- `archiveThread`
- `startRun`
- `cancelRun`
- `retryRun`
- `listChangedFiles`
- `getDiff`
- `openExternalEditor`
- `listDiagnostics`
- `getSettings`
- `updateSettings`
- `listProviders`
- `validateProviderConfig`
- `listPendingApprovals`
- `resolveApproval`

Key streamed events:

- `hostReady`
- `RunStarted`
- `RunTextDelta`
- `RunMessageCompleted`
- `RunToolProgress`
- `RunToolResult`
- `RunCompleted`
- `RunFailed`

## Reuse points

AgentHost intentionally reuses existing runtime seams instead of inventing new ones:

- `ClawSharpApplicationFactory.CreateForWorkspaceAsync`
- `DefaultSessionFactory`
- `DiskSessionLogStore`
- `JsonlTranscriptStore`
- `QueryEngine`
- `ProviderRuntimeResolver`
- `ProviderFlagUtilities`
- `ClawSharpTelemetry`
- `StartupProfiler`

## Local development flow

### Prerequisites

- install the desktop web dependencies in `apps/desktop`
- make sure `dotnet` is on `PATH`
- make sure the local Tauri toolchain is installed so `tauri dev` can start

For example:

```powershell
cd .\apps\desktop
npm install
```

### Desktop app with Codex provider

The simplest dev loop is to let Tauri start AgentHost for you.

From `apps/desktop`:

```powershell
$env:CODEX_API_KEY = "your-codex-token"
$env:CHATGPT_ACCOUNT_ID = "your-chatgpt-account-id"
npm run dev:codex
```

`dev:codex` is defined in `apps/desktop/package.json` and sets:

- `CLAUDE_CODE_USE_OPENAI=1`
- `OPENAI_MODEL=codexplan`
- `tauri dev`

If you already have Codex `auth.json`, you can use that instead of `CODEX_API_KEY`.

### Codex `auth.json` in desktop dev

ClawSharp resolves Codex credentials in this order:

- `CODEX_API_KEY`
- `CODEX_ACCOUNT_ID` or `CHATGPT_ACCOUNT_ID`
- `auth.json`

For `auth.json`, the lookup order is:

- `CODEX_AUTH_JSON_PATH`
- `CODEX_HOME\auth.json`
- `%USERPROFILE%\.codex\auth.json`

Default path:

```text
%USERPROFILE%\.codex\auth.json
```

Custom file path:

```powershell
$env:CODEX_AUTH_JSON_PATH = "D:\secrets\codex-auth.json"
npm run dev:codex
```

Custom Codex home:

```powershell
$env:CODEX_HOME = "D:\codex-home"
npm run dev:codex
```

`auth.json` can expose the token through several common fields, including:

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

If the token is a JWT that carries a ChatGPT account id claim, ClawSharp can infer the account id automatically. Explicitly setting `CODEX_ACCOUNT_ID` or `CHATGPT_ACCOUNT_ID` is still more reliable when account-scoped requests fail.

Minimal `auth.json`-based desktop run:

```powershell
cd .\apps\desktop
$env:CODEX_HOME = "$env:USERPROFILE\.codex"
npm run dev:codex
```

If desktop starts but Codex requests fail, check:

1. the shell you used to launch `npm run dev:codex` can see the same `CODEX_*` env vars
2. `auth.json` exists at one of the resolved paths
3. `OPENAI_MODEL` is still a Codex alias such as `codexplan`
4. `CHATGPT_ACCOUNT_ID` or `CODEX_ACCOUNT_ID` is set if the token does not carry the account id

If you want a different Codex model in dev, set the env vars yourself and use `npm run dev` instead:

```powershell
$env:CLAUDE_CODE_USE_OPENAI = "1"
$env:OPENAI_MODEL = "gpt-5.3-codex"
$env:CODEX_API_KEY = "your-codex-token"
$env:CHATGPT_ACCOUNT_ID = "your-chatgpt-account-id"
npm run dev
```

Under the hood, debug builds start AgentHost from the repo root with:

```powershell
dotnet run --project .\src\ClawSharp.AgentHost\ClawSharp.AgentHost.csproj --
```

That means the normal desktop dev flow only needs the Tauri command.

### AgentHost only

From the repo root, run this when you want to inspect host startup or protocol output without the desktop shell:

```powershell
dotnet build .\src\ClawSharp.AgentHost\ClawSharp.AgentHost.csproj
dotnet run --project .\src\ClawSharp.AgentHost\ClawSharp.AgentHost.csproj
```

### Split-terminal debugging

If you want both sets of logs visible:

1. In one terminal, run AgentHost directly.
2. In another terminal, run the desktop app with `npm run dev:codex`.

Current limitation: the desktop debug shell does not attach to an already-running AgentHost process. It always launches its own AgentHost instance in debug mode, so the manually started host is only useful for standalone inspection or protocol debugging.

The desktop web layer talks to the shell through:

- `apps/desktop/src/lib/protocol.ts`
- `apps/desktop/src/lib/agentHostClient.ts`
- `apps/desktop/src/lib/eventStream.ts`

## Packaging

Published sidecars are copied into:

- `apps/desktop/src-tauri/binaries/<rid>/`

Helper scripts:

- `eng/publish-agenthost.sh`
- `eng/publish-agenthost.ps1`
- `eng/copy-agenthost-to-tauri.sh`
- `eng/copy-agenthost-to-tauri.ps1`

## Mock mode

The live desktop runtime path no longer depends on the mock data layer.

The old mock assets can still be kept around as UI fixtures, but the shipped store now expects AgentHost.

## Worktree roadmap

The current review slice is intentionally workspace-safe:

- changed files are derived from the current repo state
- diffs are generated without destructive git operations
- thread DTOs already carry worktree metadata fields

Future work can layer one-thread-one-worktree behavior on top of the existing metadata and service boundaries without rewriting the desktop protocol.

## Remote and mobile reuse path

AgentHost is intentionally a standalone backend boundary. The desktop UI is not tied to terminal rendering, which keeps the protocol reusable for:

- future native shells
- remote desktop bridges
- mobile or tablet clients
- integration tests that drive the runtime without the terminal UI
