# ClawSharp

**ClawSharp** is a C# port of Claude Code, Anthropic’s terminal-based AI assistant.

I built it because I love .NET, and there still are not many open-source code-agent projects in the .NET ecosystem. I also wanted to learn how a terminal-based coding agent is structured end to end — conversation loop, tool execution, session persistence, provider integration, and terminal UX — by building one myself.

The goal with ClawSharp is to stay close to the original Claude Code workflow while shipping as a single self-contained .NET binary that runs across Windows, Linux, and macOS without requiring Node.js at runtime.

This started as a vibe-coded side project, but I’m treating it as a real tool and a real learning project. Contributions, bug reports, and feedback are very welcome.

## Why use ClawSharp?

- **Single binary** — no Node.js runtime after install
- **Cross-platform** — Windows, Linux, and macOS on x64 and ARM64
- **Multi-provider** — Anthropic, Gemini, OpenAI, Codex, GitHub Models, Bedrock, Vertex, Foundry, and Ollama
- **Desktop App** — A beautiful, integrated GUI for those who prefer windows over terminals
- **Persistent sessions** — resume chats, keep transcripts, and retain file history
- **Extensible** — supports plugins, hooks, agents, skills, and MCP servers

## The Desktop Experience

ClawSharp now ships with a full **Desktop Application** built with Tauri. It gives you a rich, multi-window interface for your coding sessions while keeping the core AI engine native and fast.

You get the same power as the CLI, but with side-by-side file views, interactive tools, and a more comfortable workspace for long-form coding.

## Current status

ClawSharp is usable today, but some parts of Claude Code are still being aligned.

## Related projects

A few open projects in a similar space that I’ve looked at for reference and inspiration:

- [claw-code](https://github.com/ultraworkers/claw-code) — a public Rust implementation of the `claw` CLI agent harness
- [openclaude](https://github.com/Gitlawb/openclaude) — an open-source coding-agent CLI for cloud and local model providers

## Install

### Preferred: Package Managers

The easiest way to stay up to date is through your favorite package manager:

**macOS (Homebrew)**
```bash
brew tap claw-sharp/tap
brew install --cask clawsharp
```

**Windows (WinGet or Scoop)**
```powershell
# Using WinGet
winget install ClawSharp

# Using Scoop
scoop bucket add claw-sharp https://github.com/claw-sharp/scoop-bucket
scoop install clawsharp
```

### Standalone CLI
If you only want the terminal agent as a global command:
```bash
npm install -g clawsharp
clawsharp --help
```

### Direct Download
Don't like package managers? Snag the latest `.exe`, `.dmg`, or `.tar.gz` directly from our [GitHub Releases](https://github.com/claw-sharp/ClawSharp/releases).

---

## A Note on Security (Gatekeeper & SmartScreen)

Since ClawSharp is an independent open-source project, our releases are currently **unsigned**. This helps us stay at zero recurring platform cost, but it does mean your OS will be a bit suspicious at first:

*   **Windows**: You'll see a blue "SmartScreen" box. Click **"More info"** and then **"Run anyway"**.
*   **macOS**: When you first open the app, it might be blocked. **Right-click** the app icon in your Applications folder and choose **Open**, or go to System Settings > Privacy & Security and click **"Open Anyway"**.

We treat your code and privacy with the highest respect—the app is fully open-source, so you can always audit what’s happening under the hood.

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

ClawSharp supports the Codex provider through its native auth.json token file, designed to mirror the original Claude Code behavior.

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

| Provider      | Environment variable | Example                                                |
| ------------- | -------------------- | ------------------------------------------------------ |
| Anthropic     | `ANTHROPIC_API_KEY`  | `clawsharp --provider anthropic`                       |
| Gemini        | `GEMINI_API_KEY`     | `clawsharp --provider gemini --model gemini-2.0-flash` |
| OpenAI        | `OPENAI_API_KEY`     | `clawsharp --provider openai --model gpt-4o`           |
| GitHub Models | `GITHUB_TOKEN`       | `clawsharp --provider github`                          |
| Ollama        | *(none required)*    | `clawsharp --provider ollama --model llama3.2`         |

Full provider setup details: [docs/authentication.md](docs/authentication.md)

---

## Platform Support

Self-contained binaries are produced for all supported platforms:

| Platform            | RID           |
| ------------------- | ------------- |
| Windows 64-bit      | `win-x64`     |
| Windows ARM64       | `win-arm64`   |
| Linux 64-bit        | `linux-x64`   |
| Linux ARM64         | `linux-arm64` |
| macOS Intel         | `osx-x64`     |
| macOS Apple Silicon | `osx-arm64`   |

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

## Contributing

ClawSharp started as a personal learning project and is still evolving. If you're interested in code agents, terminal tooling, or building more AI-native tools in .NET, contributions, issues, and feedback are welcome.

---

## Documentation

| Document                                                               | Description                               |
| ---------------------------------------------------------------------- | ----------------------------------------- |
| [docs/architect.md](docs/architect.md)                                 | Architecture overview and layer model     |
| [docs/authentication.md](docs/authentication.md)                       | All provider setup and credential options |
| [docs/user-chat-flow.md](docs/user-chat-flow.md)                       | Turn-by-turn conversation flow            |
| [docs/contributor-setup.md](docs/contributor-setup.md)                 | Local development setup                   |
| [docs/extension-migration.md](docs/extension-migration.md)             | Migrating extensions and plugins          |
| [docs/release-process.md](docs/release-process.md)                     | Maintainer release process                |
| [docs/distribution.md](docs/distribution.md)                           | Distribution, install, and verification   |
| [docs/telemetry-and-diagnostics.md](docs/telemetry-and-diagnostics.md) | Debugging and diagnostics                 |
| [docs/runbooks.md](docs/runbooks.md)                                   | Operator support runbooks                 |
| [docs/known-gaps.md](docs/known-gaps.md)                               | Known gaps and deferred work              |
