# ClawSharp Desktop Release

This release pipeline is a production-shaped setup:

- GitHub Releases hosts the canonical desktop binaries.
- GitHub Actions builds Windows and macOS artifacts.
- Tauri updater signatures protect update integrity.
- WinGet and a Homebrew tap point at GitHub-hosted assets.
- No Microsoft Store, Mac App Store, Apple Developer Program, Windows code-signing certificate, or third-party updater service is required.

For the short version-bump checklist, see `docs/version-bump-checklist.md`.

## Phase 0 audit

Before these changes, the desktop app already had:

- a Tauri shell in `apps/desktop`
- a .NET AgentHost sidecar with the correct executable name (`clawsharp-agenthost`)
- helper scripts to publish and copy sidecars into `apps/desktop/src-tauri/binaries/<rid>/`

The release gaps were:

- `apps/desktop/src-tauri/tauri.conf.json` had bundling disabled
- no Tauri updater configuration or updater key flow existed
- no desktop-focused GitHub Actions release workflow existed
- no stable desktop asset naming convention existed
- no WinGet manifest generation path existed
- no Homebrew tap/cask generation path existed
- no honest operator docs existed for unsigned release friction

## Source of truth

Desktop versioning is intentionally simple:

- source of truth: `apps/desktop/src-tauri/tauri.conf.json`
- required sync targets:
  - `apps/desktop/src-tauri/Cargo.toml`
  - `apps/desktop/package.json`
- release tag format: `desktop-v<version>`

`eng/get-desktop-version.ps1` validates that those three files agree and, when a tag is supplied, that the tag matches the desktop version.

Examples:

```bash
./eng/get-desktop-version.sh
pwsh ./eng/get-desktop-version.ps1 -Tag desktop-v0.1.0
```

## Release architecture

For each release target:

1. publish `src/ClawSharp.AgentHost/ClawSharp.AgentHost.csproj`
2. copy the published sidecar into `apps/desktop/src-tauri/binaries/<rid>/`
3. generate `apps/desktop/src-tauri/tauri.release.conf.json`
4. run `tauri build`
5. stage normalized release assets under `artifacts/desktop/release/`
6. generate:
   - `latest.json`
   - `SHA256SUMS`
   - WinGet manifests
   - Homebrew cask
7. upload everything to the GitHub Release for the matching tag

## Supported targets

Phase 1 release targets:

- Windows x64
- macOS Apple Silicon
- macOS Intel

Not in the first release workflow:

- Windows ARM64
- Linux desktop bundles

The sidecar scripts still support more RIDs, but the public desktop release matrix stays intentionally small until the core path is stable.

## Required secrets

GitHub Actions requires:

- `TAURI_UPDATER_PUBLIC_KEY`
- `TAURI_SIGNING_PRIVATE_KEY`
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`

Only the private key and password are secrets in a security sense. The public key is safe to expose, but it is still stored as a secret here so the generated release config stays out of git.

## Updater key setup

Generate the Tauri updater key pair once and store it securely:

```bash
npx tauri signer generate -w ~/.config/clawsharp/tauri/updater.key
```

Store the results as:

- `TAURI_SIGNING_PRIVATE_KEY`: the generated private key contents
- `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`: the password you set during generation
- `TAURI_UPDATER_PUBLIC_KEY`: the generated public key contents

The release workflow writes a generated override config at:

- `apps/desktop/src-tauri/tauri.release.conf.json`

That generated config enables:

- `bundle.active`
- `bundle.createUpdaterArtifacts`
- updater endpoint `https://github.com/<owner>/<repo>/releases/latest/download/latest.json`

The file is ignored by git and recreated during local release testing or CI.

## Local release test flow

### 1. Publish the sidecar

macOS or Linux shell:

```bash
RUNTIME_IDENTIFIERS=osx-arm64 ./eng/publish-agenthost.sh
RUNTIME_IDENTIFIERS=osx-arm64 ./eng/copy-agenthost-to-tauri.sh
RUNTIME_IDENTIFIERS=osx-arm64 ./eng/validate-release-assets.sh
```

PowerShell:

```powershell
./eng/publish-agenthost.ps1 -RuntimeIdentifiers osx-arm64
./eng/copy-agenthost-to-tauri.ps1 -RuntimeIdentifiers osx-arm64
./eng/validate-release-assets.ps1 -RuntimeIdentifiers osx-arm64
```

### 2. Generate the release config

```bash
export TAURI_UPDATER_PUBLIC_KEY='...'
./eng/write-tauri-release-config.sh --owner claw-sharp --repo ClawSharp --tag desktop-v0.1.0
```

PowerShell:

```powershell
$env:TAURI_UPDATER_PUBLIC_KEY = "..."
./eng/write-tauri-release-config.ps1 -Owner claw-sharp -Repo ClawSharp -Tag desktop-v0.1.0
```

### 3. Build the desktop bundle

```bash
npm ci --prefix apps/desktop
npm run --prefix apps/desktop build -- --config src-tauri/tauri.release.conf.json --target aarch64-apple-darwin
```

### 4. Stage normalized release assets

```powershell
./eng/stage-desktop-release-assets.ps1 `
  -Version 0.1.0 `
  -BundleRoot apps/desktop/src-tauri/target/aarch64-apple-darwin/release/bundle `
  -OutputRoot artifacts/desktop/release `
  -Platform darwin `
  -Arch aarch64
```

### 5. Generate updater and package-manager metadata

```powershell
./eng/prepare-updater-metadata.ps1 -Version 0.1.0 -AssetsRoot artifacts/desktop/release
./eng/generate-winget-manifests.ps1 -Version 0.1.0 -AssetsRoot artifacts/desktop/release
./eng/generate-homebrew-cask.ps1 -Version 0.1.0 -AssetsRoot artifacts/desktop/release
```

## GitHub Actions flow

Workflow:

- `.github/workflows/desktop-release.yml`

Triggers:

- git tags matching `desktop-v*`
- manual `workflow_dispatch`

Jobs:

- `validate`
  - validates version sync
  - installs desktop dependencies
  - runs frontend build/tests
  - verifies sidecar publish/copy scripts
  - verifies updater release config generation
- `build-release-assets`
  - matrix builds Windows x64, macOS arm64, macOS x64
  - publishes the AgentHost sidecar for the matching RID
  - copies the sidecar into the Tauri bundle layout
  - runs `tauri build`
  - stages stable release filenames
- `publish-release`
  - merges staged assets
  - writes `latest.json`
  - writes `SHA256SUMS`
  - generates WinGet manifests
  - generates the Homebrew cask
  - creates or updates the GitHub Release

## Artifact naming

The staged end-user assets use stable names:

- `ClawSharp_<version>_windows_x64_setup.exe`
- `ClawSharp_<version>_windows_x64_setup.exe.sig`
- `ClawSharp_<version>_windows_x64.msi`
- `ClawSharp_<version>_windows_x64.msi.sig`
- `ClawSharp_<version>_darwin_aarch64.dmg`
- `ClawSharp_<version>_darwin_aarch64.app.tar.gz`
- `ClawSharp_<version>_darwin_aarch64.app.tar.gz.sig`
- `ClawSharp_<version>_darwin_x86_64.dmg`
- `ClawSharp_<version>_darwin_x86_64.app.tar.gz`
- `ClawSharp_<version>_darwin_x86_64.app.tar.gz.sig`
- `latest.json`
- `SHA256SUMS`

This keeps:

- end-user installers obvious
- updater payloads obvious
- signatures paired with their updater-compatible asset
- WinGet and Homebrew automation deterministic

## Tauri updater behavior

Updater details:

- endpoint: `releases/latest/download/latest.json`
- hosted on GitHub Releases
- signed with the Tauri updater key pair
- consumed by the desktop app through `@tauri-apps/plugin-updater` and `tauri-plugin-updater`

The desktop app checks for updates in production builds and offers an in-app install action. After installation, the user must restart the app.

## WinGet maintenance

Generated manifests are written to:

- `artifacts/desktop/winget/ClawSharp.ClawSharp/<version>/`

Current assumptions:

- package identifier: `ClawSharp.ClawSharp`
- installer: Windows x64 NSIS `.exe`
- silent switches: `/S`

Recommended update flow:

1. cut the GitHub desktop release
2. inspect `artifacts/desktop/winget/ClawSharp.ClawSharp/<version>/`
3. fork or branch `microsoft/winget-pkgs`
4. copy the generated files into the matching manifest path
5. open the PR to `winget-pkgs`

This repository intentionally does not try to automate third-party PR approval.

## Homebrew tap maintenance

Generated cask:

- `artifacts/desktop/homebrew/Casks/clawsharp.rb`

Recommended structure:

- separate tap repository, for example `claw-sharp/homebrew-tap`
- commit the generated `Casks/clawsharp.rb`

Recommended update flow:

1. cut the GitHub desktop release
2. copy `artifacts/desktop/homebrew/Casks/clawsharp.rb` into the tap repo
3. commit and push the tap update

The generated cask supports:

- Apple Silicon via `on_arm`
- Intel via `on_intel`
- `auto_updates true`

## Trust-warning limitations

These limitations are intentional in this phase:

- Windows installers are unsigned.
- Windows SmartScreen may warn or block on first launch.
- macOS builds are not signed with an Apple Developer ID.
- macOS builds are not notarized.
- Gatekeeper may warn users when opening the app for the first time.
- WinGet and Homebrew can distribute the installers, but they do not remove OS trust friction by themselves.

This is the tradeoff for staying at zero recurring platform cost.

## Recommended user guidance

For this phase, tell users:

- download from the GitHub Release page
- verify checksums when distributing in sensitive environments
- expect SmartScreen on Windows
- expect Gatekeeper friction on macOS
- use WinGet or the Homebrew tap only if they are comfortable with unsigned app distribution

## Future paid upgrades

The pipeline is ready for later upgrades without redesign:

- Windows code signing
  - add a signing step before uploading Windows assets
  - keep the same GitHub Releases, WinGet, and updater URLs
- Apple Developer Program + notarization
  - sign and notarize the macOS app/dmg before staging release assets
  - keep the same Homebrew tap structure and Tauri updater flow
- Store distribution
  - can be added later as extra channels
  - does not require replacing GitHub Releases as the canonical release channel

The architecture is designed to allow trust improvements to layer on top of it.
