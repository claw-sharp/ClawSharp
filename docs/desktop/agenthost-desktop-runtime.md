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

It does **not** scrape the terminal UI, recreate query logic in TypeScript, or move runtime orchestration into Rust.

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

### AgentHost only

```bash
dotnet build src/ClawSharp.AgentHost/ClawSharp.AgentHost.csproj
dotnet run --project src/ClawSharp.AgentHost/ClawSharp.AgentHost.csproj
```

### Desktop app

In development, the Tauri shell starts AgentHost via:

```bash
dotnet run --project src/ClawSharp.AgentHost/ClawSharp.AgentHost.csproj --
```

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
