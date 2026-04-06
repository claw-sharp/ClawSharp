# ClawSharp

**ClawSharp** is a full-parity C# port of Claude Code — Anthropic's terminal-native agentic coding assistant.

It targets behavioral parity with the original TypeScript implementation: same conversation loop, same tool surface, same session persistence, same provider support, and the same terminal UI — rebuilt from scratch in **.NET 9** as a self-contained cross-platform binary.



## Why ClawSharp?

- **Single binary** — no Node.js runtime required after install
- **Cross-platform** — native binaries for Windows x64/ARM64, Linux x64/ARM64, macOS x64/ARM64
- **Multi-provider** — Anthropic, Gemini, OpenAI, Codex, GitHub Models, Bedrock, Vertex, Foundry, Ollama
- **Full session model** — resume, continue, transcript persistence, file history
- **Extensible** — plugins, skills, hooks, agents, and MCP server integrations

## Install

```bash
npm install -g clawsharp
clawsharp --help
```

Or download a self-contained binary directly from [GitHub Releases](https://github.com/claw-sharp/ClawSharp/releases).

## Update

```bash
npm update -g clawsharp
# or pin to latest:
npm install -g clawsharp@latest
```

## Quick Start: Claude API Key

The simplest way to test ClawSharp is with a direct Anthropic API key.

### Using an environment variable

```bash
# macOS / Linux
export ANTHROPIC_API_KEY="sk-ant-..."
clawsharp

# Windows (PowerShell)
$env:ANTHROPIC_API_KEY = "sk-ant-..."
clawsharp
```

### Using CLI flags

```bash
clawsharp --provider anthropic --model claude-sonnet-4-5
```

### Running from source

```bash
git clone https://github.com/claw-sharp/ClawSharp.git
cd ClawSharp

export ANTHROPIC_API_KEY="sk-ant-..."
dotnet run --project src/ClawSharp.Cli
```

When the REPL starts, type any message and press Enter. Claude will respond in the terminal.

---

## Quick Start: Codex (auth.json)

ClawSharp supports the Codex provider through its native auth.json token file, matching the original Claude Code behavior exactly.

### Locate or create your auth.json

The Codex auth file is typically written by the official `codex` CLI (`~/.codex/auth.json` on macOS/Linux, `%USERPROFILE%\.codex\auth.json` on Windows). It contains an `access_token` and an `account_id`.

Example `auth.json` structure:

```json
{
  "access_token": "eyJ...",
  "account_id": "org-..."
}
```

ClawSharp reads this file automatically when you use `--provider codex` or set the Codex environment variables — no extra configuration needed.

### Launch with Codex

```bash
# Auto-discovers ~/.codex/auth.json
clawsharp --provider codex --model codexplan
```

```bash
# Windows (PowerShell)
clawsharp --provider codex --model codexplan
```

### Using environment variables instead

If you prefer to pass credentials explicitly rather than relying on the auth file:

```bash
export CLAUDE_CODE_USE_OPENAI=1
export OPENAI_MODEL=codexplan
export CODEX_API_KEY="eyJ..."         # access_token value
export CODEX_ACCOUNT_ID="org-..."     # account_id value

dotnet run --project src/ClawSharp.Cli
```

### Custom auth file path

```bash
export CODEX_AUTH_PATH="/path/to/your/auth.json"
clawsharp --provider codex
```

---

## Other Providers

| Provider | Environment variable | Example |
|---|---|---|
| Anthropic | `ANTHROPIC_API_KEY` | `clawsharp --provider anthropic` |
| Gemini | `GEMINI_API_KEY` | `clawsharp --provider gemini --model gemini-2.0-flash` |
| OpenAI | `OPENAI_API_KEY` | `clawsharp --provider openai --model gpt-4o` |
| GitHub Models | `GITHUB_TOKEN` | `clawsharp --provider github` |
| Ollama | _(none required)_ | `clawsharp --provider ollama --model llama3.2` |

Full provider setup details: [docs/authentication.md](docs/authentication.md)

---

## Platform Support

Self-contained binaries are produced for all supported platforms:

| Platform | RID |
|---|---|
| Windows 64-bit | `win-x64` |
| Windows ARM64 | `win-arm64` |
| Linux 64-bit | `linux-x64` |
| Linux ARM64 | `linux-arm64` |
| macOS Intel | `osx-x64` |
| macOS Apple Silicon | `osx-arm64` |

Release archives: `.zip` (Windows), `.tar.gz` (Linux/macOS), plus a shared `SHA256SUMS` checksum file.

## Verification

Release archives ship with SHA-256 checksums and GitHub artifact attestations. See [docs/distribution.md](docs/distribution.md) for verification steps.

---

## Developer Quick Start

```bash
git clone https://github.com/claw-sharp/ClawSharp.git
cd ClawSharp

dotnet restore ClawSharp.sln
dotnet build ClawSharp.sln -c Debug
dotnet run --project src/ClawSharp.Cli -- --version
```

Run all tests:

```bash
dotnet test ClawSharp.sln
```

See [docs/contributor-setup.md](docs/contributor-setup.md) for a full contributor guide.

---

## Documentation

| Document | Description |
|---|---|
| [docs/architect.md](docs/architect.md) | Architecture overview and layer model |
| [docs/authentication.md](docs/authentication.md) | All provider setup and credential options |
| [docs/user-chat-flow.md](docs/user-chat-flow.md) | Turn-by-turn conversation flow |
| [docs/contributor-setup.md](docs/contributor-setup.md) | Local development setup |
| [docs/extension-migration.md](docs/extension-migration.md) | Migrating extensions and plugins |
| [docs/release-process.md](docs/release-process.md) | Maintainer release process |
| [docs/distribution.md](docs/distribution.md) | Distribution, install, and verification |
| [docs/telemetry-and-diagnostics.md](docs/telemetry-and-diagnostics.md) | Debugging and diagnostics |
| [docs/runbooks.md](docs/runbooks.md) | Operator support runbooks |
| [docs/known-gaps.md](docs/known-gaps.md) | Known gaps and deferred work |
