# Mobile + API Migration Plan

This document tracks the migration from the current desktop-only runtime topology to a shared multi-client architecture:

- mobile app connected to an ASP.NET API
- desktop app connected to the same ASP.NET API
- desktop app still able to run against a local `AgentHost`

The goal is to preserve the current local-first desktop workflow while adding a clean remote execution path that mobile can consume.

## Target End State

At the end of this migration, ClawSharp should support three runtime modes:

1. `Mobile -> API`
2. `Desktop -> API`
3. `Desktop -> local AgentHost`

That means the product splits into:

- UI clients in `apps/*`
- remote hosting in `services/api`
- shared runtime/domain logic in `src/*`

## Architecture Direction

The current desktop stack is:

1. React UI in `apps/desktop/src`
2. Tauri shell in `apps/desktop/src-tauri`
3. local C# `AgentHost` sidecar over `stdio`

That remains valid for desktop local mode.

The remote path adds:

1. shared contracts for projects, threads, runs, settings, approvals, and review
2. an ASP.NET Core API host
3. a transport for streaming run events over HTTP + WebSocket/SSE
4. a mobile Tauri app that consumes the remote API

## Product Scope

The first remote/mobile-capable surface should cover:

- sign-in and environment selection
- projects and threads
- run start/cancel/retry/archive
- live transcript streaming
- approvals
- changed files and diffs
- settings relevant to the remote runtime

Explicitly deferred from the first remote/mobile milestone:

- local project folder picker on mobile
- external editor launching on mobile
- local shell and filesystem-heavy actions on mobile
- full desktop-style workspace chrome on mobile

## Repository Layout

The target repository layout is:

```text
ClawSharp/
├─ apps/
│  ├─ desktop/
│  └─ mobile/
├─ services/
│  └─ api/
├─ src/
│  ├─ ClawSharp.Contracts/
│  ├─ ClawSharp.Application/
│  ├─ ClawSharp.Core/
│  ├─ ClawSharp.Query/
│  ├─ ClawSharp.Tools/
│  ├─ ClawSharp.Tasks/
│  ├─ ClawSharp.Bridge/
│  └─ ClawSharp.Infrastructure/
└─ docs/
   └─ mobile/
```

## Phase Plan

### Phase 1: Contracts And Composition Split

Create shared contracts and isolate desktop-only services from portable application services.

### Phase 2: ASP.NET API Host

Create an API host that exposes shared operations and run streaming.

### Phase 3: Desktop Remote Mode

Add a remote client path so the current desktop UI can talk to the API.

### Phase 4: Mobile Client

Create a Tauri Mobile app that consumes the same API and shared contract surface.

## Checklist

### Planning

- [x] Write the migration plan document in-repo.
- [x] Keep this checklist current as implementation lands.

### Shared Backend Shape

- [x] Add `src/ClawSharp.Contracts`.
- [x] Add `src/ClawSharp.Application`.
- [x] Define shared project/thread/run/settings/review contract groups.
- [ ] Move desktop-only integrations behind interfaces.
- [x] Add the new projects to `ClawSharp.sln`.

### API Host

- [ ] Add `services/api/ClawSharp.Api`.
- [ ] Add a minimal ASP.NET Core host bootstrap.
- [ ] Add health and capability discovery endpoints.
- [ ] Add project/thread query endpoints.
- [ ] Add run start/cancel/retry/archive endpoints.
- [ ] Add run event streaming over WebSocket or SSE.
- [ ] Add settings and approvals endpoints.

### Desktop Remote Mode

- [ ] Introduce a transport-agnostic desktop client abstraction.
- [ ] Keep the current local `AgentHost` path working.
- [ ] Add a remote API-backed desktop client.
- [ ] Add a runtime mode switch for desktop local vs remote.

### Mobile

- [ ] Add `apps/mobile`.
- [ ] Add a mobile-first navigation shell.
- [ ] Add authentication flow.
- [ ] Add project/thread browsing.
- [ ] Add transcript streaming.
- [ ] Add approvals and diff review.

### Validation

- [ ] Add contract tests for shared request/response/event models.
- [ ] Add API integration tests.
- [ ] Add desktop remote-mode smoke coverage.
- [ ] Add mobile smoke coverage.

## Implementation Sequence

The intended execution order is:

1. create the migration document
2. scaffold shared contracts and application projects
3. scaffold the ASP.NET API host
4. make the desktop app capable of using the API
5. scaffold the mobile app
6. fill in feature slices incrementally

## Notes

- Desktop local mode is a product feature and should not be removed.
- Mobile is remote-only unless a separate on-device runtime strategy is adopted later.
- The desktop UI should converge on a transport abstraction rather than binding directly to the current Tauri `invoke` path.
