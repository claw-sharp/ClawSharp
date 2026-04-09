# ClawSharp Desktop

A beautiful, integrated GUI for the ClawSharp coding agent. This app leverages the React frontend and builds a native shell using Tauri, providing a rich multi-window workspace for your AI-driven development.

## Getting Started

### Installation
The simplest way to install ClawSharp is through a package manager:

*   **macOS**: `brew tap claw-sharp/tap && brew install --cask clawsharp`
*   **Windows**: `winget install ClawSharp` or `scoop install clawsharp`
*   **Manual**: Snag the latest installer from the [GitHub Releases](https://github.com/claw-sharp/ClawSharp/releases) page.

### OS Security Warnings
Our releases are currently **unsigned** to keep the project open and free.
*   **macOS**: Right-click the app in your Applications folder and select **Open** to bypass Gatekeeper.
*   **Windows**: Click **"More info"** on the SmartScreen blue box and then **"Run anyway"**.

---

## Development

If you're looking to contribute or build the desktop app from source:

### Requirements
- **Node.js**: For the React frontend toolchain.
- **Rust**: To compile the native Tauri shell (`rustup`, `cargo`).
- **.NET 10**: To build the `AgentHost` sidecar.

### Commands
- `npm run dev`: Starts the Tauri desktop app in development mode with the real AgentHost runtime.
- `npm run dev:web`: Starts the Vite frontend only (mock data preview).
- `npm run build`: Compiles the full desktop bundle.

### Logs & Diagnostics
Logs are stored in your platform's standard app-data folder (e.g., `AppData/Local/com.clawsharp.desktop/logs` on Windows).
*   `webview.log`: Frontend React logs.
*   `rust.log`: Native shell and .NET AgentHost logs.

---

Detailed release engineering notes can be found in [docs/desktop/release.md](../../docs/desktop/release.md).
