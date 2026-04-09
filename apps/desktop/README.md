# ClawSharp Desktop

This app packages the React UI as a Tauri desktop application.

## Scripts

- `npm run dev:web` starts the Vite frontend only. This is a browser preview backed by mock AgentHost data, not the real desktop runtime.
- `npm run dev` starts the Tauri desktop app in development mode with the real AgentHost runtime.
- `npm run dev:codex` starts the Tauri desktop app in development mode and forces the Codex provider path.
- `npm run build:web` builds the frontend assets only.
- `npm run build` builds the desktop app bundle through Tauri.

Release engineering notes live in [docs/desktop/release.md](/Users/hadoan/Documents/GitHub/ClawSharp/docs/desktop/release.md).

## Requirements

- Node.js and npm for the frontend toolchain
- A Rust toolchain (`rustup`, `cargo`) plus the platform-specific Tauri system dependencies to compile the native shell

## Logs

The desktop app writes persistent logs through Tauri's log plugin. On Windows the log folder is:

- `C:\Users\<your-user>\AppData\Local\com.clawsharp.desktop\logs\`

Common files in that folder:

- `webview.log`: frontend/browser-side logs from the React app
- `rust.log`: current native-shell log file
- `rust_YYYY-MM-DD_HH-MM-SS.log`: rotated native-shell logs from previous runs

What goes where:

- Desktop shell startup, IPC, and Tauri-side AgentHost integration logs are written to `rust.log`
- Frontend chat/composer logs are written to `webview.log`
- AgentHost stderr is forwarded into the desktop native log, so AgentHost run/debug logs also show up in `rust.log`

Useful notes:

- In development, `npm run dev:web` is frontend-only, so it does not exercise the real Tauri + AgentHost logging path.
- `npm run dev` and `npm run dev:codex` use the real desktop runtime and are the right entry points when debugging logs.
- If you change C# or Rust logging code, restart the desktop dev session before collecting a new repro.
