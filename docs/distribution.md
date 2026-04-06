# Distribution And Release

## User install and update flow

ClawSharp is distributed primarily through npm and ships prebuilt self-contained binaries, so end users do not need a local .NET runtime.

Install:

```powershell
npm install -g clawsharp
clawsharp --help
```

Update:

```powershell
npm update -g clawsharp
```

Explicit latest:

```powershell
npm install -g clawsharp@latest
```

The CLI intentionally does not implement an in-app self-updater. Package-manager-driven updates are simpler, safer, and easier to support across platforms.

## Published artifacts

The release workflow publishes self-contained single-file binaries for:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

Archive format by OS family:

- Windows release assets are published as `clawsharp-<version>-<rid>.zip`
- Linux release assets are published as `clawsharp-<version>-<rid>.tar.gz`
- macOS release assets are published as `clawsharp-<version>-<rid>.tar.gz`
- Every release also includes a `SHA256SUMS` manifest

Raw publish outputs are staged under `artifacts/release/publish/<rid>` and packaged release archives are staged under `artifacts/release/packages/`.

## npm package layout

The npm distribution is split into:

- `clawsharp`: thin root package that exposes the `clawsharp` command
- `@clawsharp/cli-win32-x64`
- `@clawsharp/cli-win32-arm64`
- `@clawsharp/cli-linux-x64`
- `@clawsharp/cli-linux-arm64`
- `@clawsharp/cli-darwin-x64`
- `@clawsharp/cli-darwin-arm64`

The root package selects the correct platform package with `optionalDependencies`, `os`, and `cpu` metadata. The launcher is a small Node script that resolves the installed binary package and executes the bundled native binary.

Templates for these packages live under `packaging/npm/`. Release staging is handled by `eng/stage-npm-packages.ps1`.

## Shell completion

The CLI now ships a built-in static completion generator:

- `clawsharp completion bash`
- `clawsharp completion zsh`
- `clawsharp completion powershell`

This keeps shell completion self-contained and avoids `dotnet-suggest` or any separate .NET tool dependency on end-user machines.

## Local validation

Publish binaries locally:

```powershell
pwsh .\eng\publish-cli.ps1 -Version 0.1.0
```

Stage npm packages from published binaries:

```powershell
pwsh .\eng\stage-npm-packages.ps1 -Version 0.1.0 -PublishedRoot artifacts\release\publish -OutputRoot artifacts\npm
```

Create a local npm smoke-test layout with file-based package references:

```powershell
pwsh .\eng\stage-npm-packages.ps1 -Version 0.1.0 -PublishedRoot artifacts\release\publish -OutputRoot artifacts\npm-local -RuntimeIdentifiers win-x64 -UseLocalDependencyReferences
npm pack .\artifacts\npm-local\@clawsharp\cli-win32-x64 --pack-destination .\artifacts\npm-packs
npm pack .\artifacts\npm-local\clawsharp --pack-destination .\artifacts\npm-packs
npm install --prefix artifacts\npm-smoke .\artifacts\npm-packs\clawsharp-cli-win32-x64-0.1.0.tgz .\artifacts\npm-packs\clawsharp-0.1.0.tgz
.\artifacts\npm-smoke\node_modules\.bin\clawsharp.cmd --help
```

Pack staged npm packages without publishing:

```powershell
npm pack .\artifacts\npm\@clawsharp\cli-win32-x64
npm pack .\artifacts\npm\clawsharp
```

## GitHub Actions

`ci.yml` does the following on pushes and pull requests:

- restores, builds, and tests the .NET solution
- publishes a Linux x64 self-contained binary
- stages a local npm installation layout
- installs the staged npm package globally and smoke-tests `clawsharp --help`

`release.yml` runs on `v*` tags and does the following:

- restores, builds, and tests the solution
- publishes release binaries for all supported RIDs
- creates archive assets in the expected formats
- generates `SHA256SUMS`
- creates GitHub artifact attestations for release archives and the checksum manifest
- creates or updates the corresponding GitHub Release
- stages the npm packages and publishes the platform packages first, then the root package

## npm publishing setup

The release workflow is ready for npm provenance publishing. Two supported setups are documented here:

1. Preferred: configure npm trusted publishing for `clawsharp` and every `@clawsharp/cli-*` package against this GitHub repository. The workflow already requests `id-token: write` and publishes with `npm publish --provenance`.
2. Fallback: add an `NPM_TOKEN` repository secret with publish rights for the same packages. The workflow will use that token through `NODE_AUTH_TOKEN`.

Because the distribution is split across seven npm packages, the trusted publisher or token must be authorized for all seven package names.

## Maintainer release steps

1. Update the version in `Directory.Build.props` when preparing a new release.
2. Ensure `dotnet test ClawSharp.sln -c Release` passes locally.
3. Push a matching `v<version>` tag.
4. Wait for `release.yml` to publish GitHub Release assets and npm packages.
5. Verify the published archives against `SHA256SUMS` and confirm the npm install path works on at least one target platform.

## Verification and provenance

Checksums:

```powershell
Get-FileHash .\clawsharp-0.1.0-win-x64.zip -Algorithm SHA256
```

GitHub artifact attestations can be verified with the GitHub CLI:

```powershell
gh attestation verify .\clawsharp-0.1.0-win-x64.zip -R <owner>/<repo>
```

The release workflow attests both the packaged archives and the `SHA256SUMS` manifest. That gives consumers a free provenance trail backed by GitHub-hosted signing infrastructure without requiring a paid signing service.
