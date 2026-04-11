# Contributing to ClawSharp

Thank you for your interest in contributing to ClawSharp! This document provides guidelines and setup instructions for developers.

## Initial Setup

After cloning the repository, set up git hooks to ensure code quality:

### Automatic Setup (Recommended)

```bash
./setup-hooks.sh
```

Or manually:

```bash
git config core.hooksPath .githooks
```

### What Hooks Do

- **pre-commit**: Runs desktop tests (`npm run --prefix apps/desktop test`) before each commit
  - If tests fail, your commit will be blocked
  - This ensures only working code is committed

## Development Workflow

### 1. Create a Feature Branch

```bash
git checkout -b feature/your-feature-name
```

### 2. Make Your Changes

- Write your code
- Add tests as needed
- Keep commits focused and descriptive

### 3. Run Tests Locally

Before committing, ensure all tests pass:

```bash
# Desktop app tests
npm run --prefix apps/desktop test

# Or let the pre-commit hook catch it
git commit -m "Your commit message"
```

### 4. Commit with Message

The pre-commit hook will run automatically. If tests fail, fix the issues and try again.

```bash
git commit -m "Clear description of your changes"
```

### 5. Push and Create PR

```bash
git push origin feature/your-feature-name
```

Then open a pull request on GitHub.

## Git Hooks

### pre-commit

Automatically runs desktop tests before each commit to prevent broken code from being committed.

**Runs**: `npm run --prefix apps/desktop test`

**Why**: Ensures all tests pass before code is committed, maintaining code quality.

### post-checkout

Automatically ensures git hooks are configured after cloning or pulling the repository.

## Troubleshooting

### Hooks Not Running

If hooks aren't executing, ensure they're configured:

```bash
git config core.hooksPath
# Should output: .githooks
```

If not, run:

```bash
git config core.hooksPath .githooks
```

### Tests Fail on Commit

The pre-commit hook will prevent your commit if tests fail. To fix:

1. Review the test output
2. Fix the failing test(s)
3. Run `npm run --prefix apps/desktop test` locally to verify
4. Commit again

### Bypassing Hooks (Not Recommended)

If you absolutely need to bypass hooks (not recommended):

```bash
git commit --no-verify
```

However, please fix the issues before pushing to ensure CI passes.

## Code Style

- Follow existing code patterns in the repository
- Keep commits small and focused
- Write clear commit messages
- Include relevant tests for new features

## Questions?

If you have questions or need help, please open an issue on GitHub.
