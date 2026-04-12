# Hook Author

Use this skill when creating or editing ClawSharp hooks in settings or plugin hook files.

## Principles

- Choose the right event first, then the matcher.
- Keep hook commands deterministic, fast, and safe to re-run.
- Prefer calling a checked-in script over embedding a long shell pipeline.
- Avoid hooks that silently mutate unrelated files.

## Workflow

1. Identify the trigger event and the exact tool or matcher.
2. Draft the smallest command that proves the behavior.
3. Check required environment variables and working directory assumptions.
4. Add or update the hook definition.
5. Verify the config shape and explain how to test the hook.
