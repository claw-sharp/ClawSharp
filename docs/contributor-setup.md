# Contributor Setup

Welcome to the ClawSharp project! This document outlines how to set up your local development environment.

## Prerequisites

To build and run ClawSharp locally, you need the following dependencies installed on your machine:

1. **.NET 10.0 SDK**: Required to build the core C# projects and run tests.
   - [Download .NET 10.0](https://dotnet.microsoft.com/download/dotnet/10.0)
2. **Node.js (v22+)**: Required if you intend to test the NPM packaging and Javascript scripts.
   - [Download Node.js](https://nodejs.org/)
3. **PowerShell (pwsh)**: Required to run the release and packaging scripts in the `eng/` directory (`publish-cli.ps1`, `stage-npm-packages.ps1`).

## Getting the Source Code

Clone the repository to your local machine:

```bash
git clone https://github.com/claw-sharp/ClawSharp.git
cd ClawSharp
```

## Building and Running

You can build the entire solution using the standard `dotnet` CLI:

```bash
dotnet restore ClawSharp.sln
dotnet build ClawSharp.sln -c Debug
```

To run the ClawSharp CLI directly from the source code during development:

```bash
dotnet run --project src/ClawSharp.Cli -- <arguments>
```

For instance, to run the CLI locally using the Gemini provider with the default Gemini model:

```bash
export CLAUDE_CODE_USE_GEMINI=true
export GEMINI_API_KEY="your-api-key"
export GEMINI_MODEL="gemini-2.0-flash"

dotnet run --project src/ClawSharp.Cli -- 
```

## Running Tests

We use `xUnit` for testing. Ensure you run both the unit and integration tests to verify your changes.

```bash
dotnet test ClawSharp.sln -c Debug
```

*Note: Some integration tests may rely on specific environment variables (e.g., API keys) being present. Check the individual test requirements if you experience integration test failures.*

## Code Style and Conventions

- Please follow standard C# and .NET naming conventions.
- Make sure to add unit tests for any new features or bug fixes.
- The `ClawSharp.Ui.Terminal` project handles terminal interaction. Keep logic decoupled between the UI layers and orchestration (`ClawSharp.Query`).

## Submitting Pull Requests

1. Create a descriptive branch name (`feature/add-new-provider`, `fix/api-error-rendering`).
2. Include comprehensive test coverage for any new features.
3. Ensure CI completes successfully.
4. Keep PR summaries clear and reference any associated issues.
