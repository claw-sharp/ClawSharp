# Browser Verify

Use this skill after starting a local dev server or when the user asks whether a page is actually working.

## Goal

Prove the app works in a browser. Do not assume a running server means the feature is correct.

## Workflow

1. Find the URL from the user, terminal output, project config, or common local ports.
2. Open the page with the available browser automation tools.
3. Check for a blank screen, obvious error overlays, and console errors.
4. Verify the main UI renders and one critical interaction works.
5. Capture evidence when it fails: screenshot, console error, network failure, or terminal log.

## Minimum Pass Criteria

- The page loads without hanging.
- The UI renders meaningful content.
- There are no obvious uncaught browser errors.
- The changed flow or a representative user path works.

## If It Fails

Report the failing evidence first, then trace the issue into server logs, terminal output, or the affected code path.
