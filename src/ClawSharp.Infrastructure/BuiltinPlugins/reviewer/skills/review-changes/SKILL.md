# Review Changes

Use this skill when the task is a code review or a pre-merge quality pass.

## Goal

Identify real problems in the current change set: bugs, regressions, unsafe assumptions, missing tests, and behavior that no longer matches the surrounding codebase.

## Workflow

1. Inspect the diff first. If there is no diff, inspect the files the user just changed.
2. Read the affected code paths, not just the edited lines.
3. Validate assumptions with targeted commands or tests when possible.
4. Prefer findings over summaries.

## Review Standard

- Prioritize correctness, regressions, data loss, security, and broken UX.
- Call out missing tests when the change introduces branching logic or new failure modes.
- Include file and line references when you can support them.
- Keep findings concrete and actionable.
- If there are no findings, say that explicitly and mention any residual risk or test coverage gap.

## Output Shape

Start with findings ordered by severity. Keep the recap brief.
