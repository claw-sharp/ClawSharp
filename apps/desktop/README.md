# ClawSharp Desktop

This app packages the React UI as a Tauri desktop application.

## Scripts

- `npm run dev:web` starts the Vite frontend by itself.
- `npm run dev` starts the Tauri desktop app in development mode.
- `npm run build:web` builds the frontend assets only.
- `npm run build` builds the desktop app bundle through Tauri.

## Requirements

- Node.js and npm for the frontend toolchain
- A Rust toolchain (`rustup`, `cargo`) plus the platform-specific Tauri system dependencies to compile the native shell
