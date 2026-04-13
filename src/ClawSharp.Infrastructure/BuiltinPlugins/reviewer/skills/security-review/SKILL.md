# Security Review

Use this skill when you need to scan the current change set for security problems before merge.

## Goal

Find concrete security issues introduced or exposed by the diff.

## Workflow

1. Inspect the diff first and focus on new trust boundaries, inputs, outputs, and privileged code paths.
2. Trace user-controlled data into sensitive sinks such as auth, filesystem, shell, network, templates, deserialization, and persistence.
3. Check whether the change weakens validation, authorization, secret handling, tenant isolation, or auditability.
4. Verify findings with the affected code path, tests, or a minimal reproduction when possible.

## Focus Areas

- Authentication and authorization bypasses.
- Missing permission checks or cross-tenant data exposure.
- Injection risks in SQL, shell, HTML, templates, paths, and deserialization.
- Secrets in code, logs, telemetry, configs, or client-visible responses.
- Unsafe file handling, SSRF, open redirects, and insecure external calls.
- Security regressions caused by fallback logic, debug flags, or weakened defaults.

## Output Shape

Start with concrete findings ordered by severity. Include file and line references when supported. If you find no issue, say that plainly and call out any residual risk or unverified area.
