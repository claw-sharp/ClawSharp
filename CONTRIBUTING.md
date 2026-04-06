# Contributing to ClawSharp

Thank you for your interest in contributing! ClawSharp is a community-driven C# port of Claude Code and welcomes contributions of all kinds.

## Ways to Contribute

- Reporting bugs
- Suggesting features or improvements
- Fixing issues
- Improving documentation
- Writing or improving tests

## Before You Start

- Search [existing issues](https://github.com/claw-sharp/ClawSharp/issues) before opening a new one.
- For large changes, open an issue first to discuss the approach before writing code.
- Review the [architecture overview](docs/architect.md) to understand how the codebase is structured.

## Development Setup

See [docs/contributor-setup.md](docs/contributor-setup.md) for full instructions on building and running ClawSharp locally.

Quick start:

```bash
git clone https://github.com/claw-sharp/ClawSharp.git
cd ClawSharp
dotnet restore ClawSharp.sln
dotnet test ClawSharp.sln
```

## Submitting Issues

When reporting a bug, please include:

- ClawSharp version (`clawsharp --version`)
- Operating system and architecture
- Provider being used (Anthropic, Gemini, Codex, etc.)
- Steps to reproduce
- Expected behavior vs. actual behavior
- Relevant terminal output or error messages

## Submitting Pull Requests

1. Fork the repository and create a branch from `main`.
2. Name your branch descriptively: `fix/gemini-streaming-error`, `feat/add-xyz-provider`.
3. Write or update tests to cover your changes.
4. Ensure the full test suite passes: `dotnet test ClawSharp.sln`.
5. Keep commits focused — one logical change per commit.
6. Open a pull request against `main` with a clear description.

## Commit Message Style

Use the [Conventional Commits](https://www.conventionalcommits.org/) format:

```
feat: add Bedrock provider streaming support
fix: surface Gemini 429 errors in terminal UI
docs: update authentication guide for Codex auth.json
test: add unit tests for QueryModelSseStreamingClient
chore: bump version to 0.0.6
```

## Code Style

- Follow standard C# / .NET naming conventions.
- Keep the interaction layer (`ClawSharp.Ui.Terminal`) thin — no model or orchestration logic there.
- Do not mix query, tool, and infrastructure responsibilities in the same class (see [architecture](docs/architect.md)).
- Add XML doc comments on public APIs.

## Parity Rule

ClawSharp aims for 1:1 behavioral parity with the original TypeScript Claude Code runtime. When porting behavior:

- Do not invent new runtime logic or alternate control flow.
- If C# has no direct TypeScript equivalent, stop and document the decision point clearly in a PR comment or issue.
- The TypeScript source is the behavioral specification.

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
