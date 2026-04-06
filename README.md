# ClawSharp

ClawSharp is the C# rewrite workspace for the full-parity migration of the current terminal application.

This folder now contains:

- a `.NET 9` solution
- core projects for runtime, query, tools, tasks, terminal UI, bridge, extensions, and infrastructure
- initial unit, integration, and parity test projects
- the rewrite master checklist

Current status:

- solution scaffolding complete
- minimal runnable CLI/TUI foundation complete
- full feature parity not yet implemented

Documentation:

- architecture overview: [docs/architect.md](docs/architect.md)
- user chat flow: [docs/user-chat-flow.md](docs/user-chat-flow.md)
- authentication: [docs/authentication.md](docs/authentication.md)
- telemetry and diagnostics: [docs/telemetry-and-diagnostics.md](docs/telemetry-and-diagnostics.md)
- distribution and release process: [docs/distribution.md](docs/distribution.md)
- maintainer release process: [docs/release-process.md](docs/release-process.md)

## Install

```powershell
npm install -g clawsharp
```

```powershell
clawsharp --help
```

## Update

```powershell
npm update -g clawsharp
```

Explicitly refresh to the latest published version:

```powershell
npm install -g clawsharp@latest
```

ClawSharp does not ship a custom self-updater. Updates are owned by npm.

## Platform Support

Published self-contained binaries are produced for:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

Release artifacts are uploaded to GitHub Releases as:

- Windows: `.zip`
- Linux/macOS: `.tar.gz`
- shared checksum file: `SHA256SUMS`

## Verification

Release archives include SHA-256 checksums, and the release workflow generates GitHub artifact attestations for the archives and checksum manifest. Verification details are documented in [docs/distribution.md](docs/distribution.md).

## Developer Quick Start

Run the CLI from source:

```powershell
dotnet run --project .\src\ClawSharp.Cli -- repl
```

Show the current source-build version:

```powershell
dotnet run --project .\src\ClawSharp.Cli -- --version
```
