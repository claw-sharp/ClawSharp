# Cleanup Diff

Use this skill after implementation when the code works but still looks heavier than necessary.

## Goal

Simplify the current change set without changing intended behavior.

## Checklist

- Remove duplicated logic when a nearby helper or existing abstraction already fits.
- Collapse unnecessary state, effects, wrappers, and branching.
- Delete dead code, stale comments, and task-specific scaffolding.
- Prefer small direct fixes over broad refactors.
- Re-run the narrowest useful verification after cleanup.

## Guardrails

- Do not chase style-only edits unless they reduce maintenance cost.
- Do not invent new abstractions for one call site.
- Preserve existing project conventions.

## Deliverable

Make the cleanup edits directly, then summarize the concrete simplifications you made.
