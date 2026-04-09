# Desktop AgentHost Phase 0-1

This note records the first desktop integration slice for wiring `apps/desktop` to the real ClawSharp runtime.

## Goal

Phase 0 and Phase 1 establish a real backend boundary without duplicating runtime logic in TypeScript or Rust.

The desktop shell keeps its polished UI, while a dedicated C# sidecar process becomes the runtime-facing backend.

## Reuse Points

The implementation reuses the existing runtime layers directly:

- `ClawSharp.Infrastructure.ClawSharpApplicationFactory` remains the assembly root.
- `ClawSharp.Core.DefaultSessionFactory` remains the source of session creation and resume semantics.
- `ClawSharp.Infrastructure.DiskSessionLogStore` remains the source of thread/session discovery.
- `ClawSharp.Infrastructure.JsonlTranscriptStore` remains the source of persisted transcript history.
- `ClawSharp.Query.QueryEngine` stays the turn orchestration boundary for later streaming slices.

The terminal UI is intentionally not part of the desktop runtime contract.

## Transport Choice

AgentHost uses newline-delimited JSON over `stdio`.

Why this choice:

- it matches the repo’s existing process-oriented bridge patterns
- it supports request/response and streaming events on the same channel
- it keeps the Tauri host thin
- it avoids introducing a local HTTP server for a single local desktop shell

## Phase 1 Scope

Phase 1 adds:

- `src/ClawSharp.AgentHost`
- a typed request/response envelope over `stdio`
- startup/health handling
- open project
- recent project persistence for the desktop shell
- list threads
- create thread
- get thread with persisted transcript history
- publish/copy scripts for the sidecar

## Boundaries

Desktop-only state added in this slice is intentionally narrow:

- recent project tracking is stored under the local ClawSharp config directory for desktop convenience

Runtime behavior is still owned by the existing C# layers:

- transcript layout
- session ids
- resume behavior
- settings/bootstrap

## Deferred To Later Phases

This slice does not yet wire:

- real run execution and streaming into the React app
- diagnostics and provider/settings surfaces
- changed-file and diff review flows
- approval flows
- per-thread worktree lifecycle management

Those remain follow-up phases on top of the same AgentHost contract.
