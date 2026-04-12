# Update Config

Use this skill when editing `.clawsharp/settings.json`, `.clawsharp/settings.local.json`, or the user-level settings file.

## Scope Rules

- User settings: personal defaults across projects.
- Project settings: shared team behavior committed to the repo.
- Local settings: personal overrides for one repo.

## Workflow

1. Read the current settings file before editing.
2. Choose the narrowest scope that matches the user request.
3. Preserve unrelated keys.
4. Make minimal JSON edits.
5. If you changed structure, validate the file parses cleanly afterward.

## Common Areas

- Model or provider selection
- Permissions
- Environment variables
- Enabled plugins
- Hooks

When scope is ambiguous, explain the tradeoff and pick the safer default.
