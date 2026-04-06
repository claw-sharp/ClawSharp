# Operator Runbooks

This document provides runbooks for common troubleshooting and support scenarios encountered while running or operating ClawSharp.

## 1. Diagnostics and File Locking Scenarios

**Symptom**: `JSONL` transcript store throws a locked file error or CLI hangs when writing new turns.
**Assessment**: Sometimes multiple CLI instances might attempt to access the same local session trace, or an abrupt crash left an orchestrator lock dangling.
**Resolution Steps**:
1. Check running ClawSharp processes: `ps -aux | grep clawsharp`
2. Investigate the `.clawsharp` or `.gemini` output directory inside the user's workspace to locate dangling locks.
3. Terminate runaway processes and safely truncate corrupt JSONL logs if necessary.

## 2. Model API Quota and Rate Limits (429s)

**Symptom**: The CLI prints a stream of `Model API Error` strings in the console UI such as "Retrying in 2s (Attempt 1 of 5)".
**Assessment**: The user is hitting provider-specific rate limits, especially common if using the default `gemini-2.0-flash` on a generative AI free tier.
**Resolution Steps**:
1. Verify the exact status payload from the diagnostic output.
2. Direct the user to configure a specific valid provider API key (e.g. `export GEMINI_API_KEY="..."`).
3. If the user relies on Anthropic or OpenAi, ensure their environment specifies the correct proxy or custom `BASE_URL`.

## 3. Remote/Bridge Session Disconnects

**Symptom**: The `ClawSharp.Bridge` connections continually time out when executing tasks remotely or interfacing with the IDE.
**Assessment**: Network disconnects between `ClawSharp` and the targeted bridge.
**Resolution Steps**:
1. Confirm that `ClawSharpApplicationFactory` is correctly loading the network proxies from standard system environment parameters.
2. Ask the user to verify firewall restrictions blocking loopback or external proxy hosts.

## 4. MCP Subprocess Failures

**Symptom**: Application fails to launch MCP extensions.
**Assessment**: A registered MCP server binary failed to start via standard standard I/O (e.g., node command not found).
**Resolution Steps**:
1. Ensure the required runtime for the MCP server (Python, Node.js) exists in the shell `PATH` before invoking `clawsharp`.
2. Evaluate `telemetry-and-diagnostics.md` output to trace the exact crash trace.
