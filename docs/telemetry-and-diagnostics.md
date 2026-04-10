# How To Use ClawSharp Telemetry And Diagnostics

This document explains how to use the current ClawSharp telemetry, tracing, debug logging, startup diagnostics, and crash capture features.

It describes the C# implementation that exists today.

## Summary

ClawSharp now writes local diagnostics artifacts for:

- debug logs
- event logs
- metrics
- crash and error capture
- Perfetto trace output
- startup profiling

These artifacts are filesystem-local. They are intended for development, parity validation, and runtime debugging.

## Default Storage Location

All telemetry output is rooted at `CLAWSHARP_CONFIG_DIR` when that variable is set.

If `CLAWSHARP_CONFIG_DIR` is not set, ClawSharp uses:

- Windows: `%USERPROFILE%\.clawsharp`
- macOS/Linux: `~/.clawsharp`

Within that directory, the current implementation writes:

- `debug/<run-id>.txt`
- `debug/latest`
- `telemetry/events.<run-id>.jsonl`
- `telemetry/metrics.<run-id>.jsonl`
- `telemetry/crash.<run-id>.jsonl`
- `traces/trace-<run-id>.json`
- `startup-perf/<session-id-or-run-id>.txt`

Notes:

- `run-id` is generated per process start.
- `session-id` replaces the run id for startup profiling once the REPL session is resolved.
- `debug/latest` is a best-effort symlink to the current debug log and may fail on systems where symlink creation is restricted.

## What Gets Recorded

### Event Logging

ClawSharp writes JSONL event records for:

- app event sink publications
- query turn completion and tool usage
- tool execution lifecycle
- application bootstrap completion
- extension bootstrap and refresh
- plugin enabled and plugin load failure telemetry
- skill loaded telemetry
- bridge connect, close, teardown, skip, and injected-fault telemetry
- explicit crash and error capture events
- sampled startup performance events

The event file is:

- `telemetry/events.<run-id>.jsonl`

### Metrics

ClawSharp writes JSONL metric records for:

- app event counts
- query turn counts and tool counts
- tool execution counts
- extension/bootstrap refresh durations
- plugin and skill load counts
- bridge connectivity counters
- crash and error counters
- startup checkpoint count

The metric file is:

- `telemetry/metrics.<run-id>.jsonl`

### Debug Logs

Debug logs are plain text with timestamps and levels.

The debug log file is:

- `debug/<run-id>.txt`

### Crash And Error Capture

Unhandled exceptions, unobserved task exceptions, and explicit runtime error captures are written to:

- `telemetry/crash.<run-id>.jsonl`

Each record includes:

- timestamp
- session id
- run id
- context
- fatal flag
- exception type
- exception message
- stack trace

### Perfetto Tracing

Perfetto-compatible JSON traces can be written for interaction and tool spans.

The trace file is:

- `traces/trace-<run-id>.json`

### Startup Profiling

Startup profiling supports two behaviors:

1. sampled startup performance event logging
2. detailed startup report generation

Detailed startup reports are written to:

- `startup-perf/<session-id>.txt`

## How To Enable Features

### 1. Basic Local Telemetry

No extra flag is required for the local event, metric, and crash files. They are created when the runtime emits telemetry.

Typical usage:

```powershell
dotnet run --project .\src\ClawSharp.Cli -- repl
```

After running a session, inspect:

```powershell
Get-ChildItem "$env:USERPROFILE\.clawsharp\telemetry"
```

Or if you want a temporary isolated location:

```powershell
$env:CLAWSHARP_CONFIG_DIR = "D:\temp\clawsharp"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

### 2. Debug Logging

Debug logging becomes active when any of these are true:

- `DEBUG=1`
- `DEBUG_SDK=1`
- `--debug`
- `-d`
- `--debug=<filter>`
- `--debug-to-stderr`
- `-d2e`
- `--debug-file <path>`
- `--debug-file=<path>`

Examples:

```powershell
dotnet run --project .\src\ClawSharp.Cli -- repl --debug
```

```powershell
dotnet run --project .\src\ClawSharp.Cli -- repl --debug=bridge
```

```powershell
dotnet run --project .\src\ClawSharp.Cli -- repl --debug-to-stderr
```

```powershell
dotnet run --project .\src\ClawSharp.Cli -- repl --debug-file D:\logs\clawsharp-debug.txt
```

You can also redirect the default debug directory:

```powershell
$env:CLAUDE_CODE_DEBUG_LOGS_DIR = "D:\logs\clawsharp"
dotnet run --project .\src\ClawSharp.Cli -- repl --debug
```

### 3. Debug Log Verbosity

The minimum debug log level defaults to `debug`.

Supported values:

- `verbose`
- `debug`
- `info`
- `warn`
- `error`

Example:

```powershell
$env:CLAUDE_CODE_DEBUG_LOG_LEVEL = "verbose"
dotnet run --project .\src\ClawSharp.Cli -- repl --debug
```

### 4. Startup Profiling

Detailed startup profiling is enabled with:

```powershell
$env:CLAUDE_CODE_PROFILE_STARTUP = "1"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

This does two things:

- records startup checkpoints in memory
- writes a human-readable startup report to `startup-perf/<session-id>.txt`

Without `CLAUDE_CODE_PROFILE_STARTUP=1`, ClawSharp can still emit sampled startup performance events for:

- `USER_TYPE=ant`
- a small random sample of non-ant sessions

### 5. User Prompt Logging In Traces

Interaction spans redact user prompts by default.

To include raw prompt text in span attributes:

```powershell
$env:OTEL_LOG_USER_PROMPTS = "1"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

If this variable is not set, prompts are stored as:

- `<REDACTED>`

### 6. Perfetto Trace Output

Enable Perfetto trace output with:

```powershell
$env:CLAUDE_CODE_PERFETTO_TRACE = "1"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

That writes the trace to the default trace path under `traces/`.

You can also provide an explicit output path:

```powershell
$env:CLAUDE_CODE_PERFETTO_TRACE = "D:\logs\clawsharp-trace.json"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

The resulting file can be opened in:

- `https://ui.perfetto.dev`

## Bridge And Remote Diagnostics

The bridge runtime emits telemetry for:

- bridge skips
- bridge fault injection
- remote bridge websocket connected
- remote bridge websocket closed
- remote bridge teardown

These show up in the local event and metric files.

The bridge debug helper also redacts common secret field names before logging structured bodies.

Redacted fields include:

- `session_ingress_token`
- `environment_secret`
- `access_token`
- `secret`
- `token`

## Plugin And Skill Telemetry

During extension bootstrap and plugin refresh, ClawSharp emits:

- `tengu_plugin_enabled_for_session`
- `tengu_plugin_load_failed`
- `tengu_skill_loaded`
- `tengu_extension_bootstrap`
- `tengu_plugin_refresh`

Current behavior:

- bundled or builtin plugins are treated as Anthropic-controlled
- non-bundled plugin names are redacted to `third-party`
- plugin identity hashing uses a fixed `sha256`-based hash truncated to 16 hex chars

## Practical Recipes

### Recipe 1: Isolated Local Debug Session

```powershell
$env:CLAWSHARP_CONFIG_DIR = "D:\temp\clawsharp-debug"
$env:CLAUDE_CODE_DEBUG_LOG_LEVEL = "verbose"
dotnet run --project .\src\ClawSharp.Cli -- repl --debug
```

Then inspect:

```powershell
Get-Content D:\temp\clawsharp-debug\debug\latest
Get-ChildItem D:\temp\clawsharp-debug\telemetry
```

### Recipe 2: Startup Investigation

```powershell
$env:CLAWSHARP_CONFIG_DIR = "D:\temp\clawsharp-startup"
$env:CLAUDE_CODE_PROFILE_STARTUP = "1"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

Then inspect:

```powershell
Get-ChildItem D:\temp\clawsharp-startup\startup-perf
Get-Content D:\temp\clawsharp-startup\startup-perf\*.txt
```

### Recipe 3: Trace A Query And Tool Run

```powershell
$env:CLAWSHARP_CONFIG_DIR = "D:\temp\clawsharp-trace"
$env:CLAUDE_CODE_PERFETTO_TRACE = "1"
$env:OTEL_LOG_USER_PROMPTS = "1"
dotnet run --project .\src\ClawSharp.Cli -- repl
```

Then open:

- `D:\temp\clawsharp-trace\traces\trace-<run-id>.json`

### Recipe 4: Debug Bridge Behavior

```powershell
$env:CLAWSHARP_CONFIG_DIR = "D:\temp\clawsharp-bridge"
dotnet run --project .\src\ClawSharp.Cli -- repl --debug=bridge
```

Then inspect:

- `debug/latest`
- `telemetry/events.<run-id>.jsonl`
- `telemetry/metrics.<run-id>.jsonl`

## Reading The Artifacts

### Event File

Each line is a JSON object with:

- `eventName`
- `timestamp`
- `sessionId`
- `runId`
- `sequence`
- `workspaceRoot`
- `metadata`

### Metric File

Each line is a JSON object with:

- `name`
- `value`
- `timestamp`
- `sessionId`
- `runId`
- `tags`

### Crash File

Each line is a JSON object with:

- `timestamp`
- `sessionId`
- `runId`
- `context`
- `fatal`
- `exceptionType`
- `message`
- `stackTrace`
- `metadata`

## Current Limitations

This is the current state of the C# rewrite, not the final parity target.

Known constraints:

- telemetry is local-file-backed, not a remote OTEL exporter pipeline
- debug logging can also activate for `USER_TYPE=ant` sessions without an explicit debug flag
- `debug/latest` symlink creation is best-effort
- Perfetto tracing currently covers interaction and tool spans only
- startup sampling is process-random for non-ant sessions
- the telemetry runtime uses process-global static state, so tests that mutate telemetry env vars must run serially

## Relevant Source Files

- `ClawSharp/src/ClawSharp.Core/ClawSharpTelemetry.cs`
- `ClawSharp/src/ClawSharp.Infrastructure/StartupProfiler.cs`
- `ClawSharp/src/ClawSharp.Infrastructure/ClawSharpApplicationFactory.cs`
- `ClawSharp/src/ClawSharp.Query/QueryEngine.cs`
- `ClawSharp/src/ClawSharp.Query/ToolOrchestrator.cs`
- `ClawSharp/src/ClawSharp.Infrastructure/PluginTelemetryUtilities.cs`
- `ClawSharp/src/ClawSharp.Bridge/BridgeDebugUtilities.cs`
- `ClawSharp/src/ClawSharp.Bridge/RemoteSessionManager.cs`
