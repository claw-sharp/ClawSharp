# CLAUDE.md

This file provides guidance to Claude Code when working in the ClawSharp repository.

## Project overview

ClawSharp is a C# port of Claude Code. The repository contains:

- a .NET 10 solution that implements the CLI, runtime, query loop, tools, AgentHost, and desktop-facing services
- a Tauri desktop app in `apps/desktop` with a React + TypeScript frontend and a Rust shell
- Playwright end-to-end coverage for the desktop app in `apps/desktop-e2e`
- packaging and release assets under `packaging/`, `eng/`, and `docs/`

## Primary stack

- C# / .NET 10 for the core product and tests
- xUnit for backend test coverage
- React 18 + TypeScript + Vite for the desktop frontend
- Tauri 2 + Rust for the native desktop shell
- Playwright for desktop end-to-end tests

## Repository map

- `src/ClawSharp.Core`: core models, settings, app state, provider/runtime foundations
- `src/ClawSharp.Query`: query loop, request building, compacting, hooks, tool orchestration
- `src/ClawSharp.Tools`: tool definitions and runtime integration
- `src/ClawSharp.Infrastructure`: filesystem, sessions, settings/bootstrap, provider wiring, AgentHost support services
- `src/ClawSharp.AgentHost`: desktop/IPC-facing host process and contracts
- `src/ClawSharp.Ui.Terminal`: REPL and terminal rendering
- `src/ClawSharp.Cli`: CLI entrypoint
- `apps/desktop`: React/Tauri desktop application
- `apps/desktop-e2e`: Playwright coverage for the desktop app
- `tests/ClawSharp.UnitTests`: primary unit and focused integration coverage for backend/runtime logic
- `tests/ClawSharp.IntegrationTests`, `tests/ClawSharp.ParityTests`, `tests/ClawSharp.Desktop.Tests`, `tests/ClawSharp.App.*.Tests`: broader validation surfaces

## Working guidelines

- Treat this as a .NET-first codebase. Most product behavior lives under `src/`, not the desktop frontend.
- Keep changes aligned with the existing layer split. Do not move infrastructure concerns into `Core`, and do not bypass query/runtime abstractions with ad hoc shortcuts.
- Many files include TypeScript parity notes. Preserve those notes and keep new work consistent with the stated parity goals instead of inventing unrelated architecture.
- Prefer minimal, local changes over broad refactors. This repo tracks Claude Code behavior closely, so unintended structural changes make parity work harder.
- When behavior changes, update the nearest tests in the corresponding surface instead of relying only on manual verification.
- Root `package.json` is intentionally empty. Node-based work happens inside `apps/desktop` and `apps/desktop-e2e`.
- The desktop app uses `package-lock.json`, so prefer `npm` there unless the user explicitly asks for another package manager.

## Verification

Use the smallest command set that actually covers the change.

### Backend / runtime changes

- Restore/build solution: `dotnet build ClawSharp.sln -c Debug`
- Full backend test pass: `dotnet test ClawSharp.sln -c Debug`
- Focused tests are preferred while iterating, especially under `tests/ClawSharp.UnitTests`

### CLI changes

- Run the CLI locally: `dotnet run --project src/ClawSharp.Cli -- --help`

### Desktop frontend changes

Run from `apps/desktop`:

- unit tests: `npm test`
- lint: `npm run lint`
- frontend build: `npm run build:web`
- full desktop dev shell: `npm run dev`
- frontend-only mock preview: `npm run dev:web`

### Desktop end-to-end changes

Run from `apps/desktop-e2e`:

- full e2e suite: `npm test`
- smoke flow: `npm run test:smoke`

## Change expectations

- If you touch both desktop UI and AgentHost/runtime contracts, verify both sides.
- If you change request construction, prompt assembly, tools, or session behavior, add or update backend tests.
- If you change visible desktop flows, prefer updating React tests and, when the flow is critical, Playwright coverage.
- If you cannot run a relevant verification step, say so explicitly in the final response.
