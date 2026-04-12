# Investigate Failure

Use this skill when something is broken, hanging, timing out, or behaving in a way that is not yet explained.

## Operating Rule

Do not guess. Move step by step and tell the user what you checked, what you found, and what you are checking next.

## Triage Order

1. Reproduce the problem or find the exact failing command.
2. Read the nearest evidence: terminal output, stack trace, failing test, browser console, or log file.
3. Narrow the failure to one subsystem: UI, API, background task, config, or dependency.
4. Inspect the code path responsible for that subsystem.
5. Only patch after you have a high-confidence cause.

## Reporting Contract

For each step:

- State what you are checking.
- Quote or summarize the relevant evidence.
- Explain the next step.

Stop once the root cause is supported by evidence.
