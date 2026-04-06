# Release Process

## Purpose

This document describes the maintainer-facing release flow for ClawSharp.

It complements `docs/distribution.md`:

- `distribution.md` explains how ClawSharp is packaged, installed, updated, and verified
- this document explains how to cut and validate a new release end to end

## Release Model

ClawSharp releases are built from git tags in the form `v<version>`.

Example:

- `v0.1.0`

A release tag triggers `.github/workflows/release.yml`, which:

- restores, builds, and tests the solution
- publishes self-contained single-file binaries for all supported RIDs
- archives release artifacts
- generates `SHA256SUMS`
- creates GitHub artifact attestations
- creates or updates the GitHub Release
- publishes the npm platform packages first, then the root `clawsharp` package

## Supported Release Artifacts

The current release process publishes binaries for:

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

Artifact formats:

- Windows: `clawsharp-<version>-<rid>.zip`
- Linux/macOS: `clawsharp-<version>-<rid>.tar.gz`
- Checksums: `SHA256SUMS`

## Prerequisites

Before cutting a release, confirm the following:

- the intended version is set in `Directory.Build.props`
- the release branch or commit is the exact code you want to ship
- GitHub Actions is enabled for the repository
- npm publishing is configured for all seven packages:
 - `clawsharp`
 - `@clawsharp/cli-win32-x64`
 - `@clawsharp/cli-win32-arm64`
 - `@clawsharp/cli-linux-x64`
 - `@clawsharp/cli-linux-arm64`
 - `@clawsharp/cli-darwin-x64`
 - `@clawsharp/cli-darwin-arm64`
- either npm trusted publishing is configured, or `NPM_TOKEN` is available to the workflow

## Pre-Release Checklist

Run the normal validation locally before creating the tag.

Recommended checks:

```powershell
dotnet restore .\ClawSharp.sln
dotnet build .\ClawSharp.sln -c Release --no-restore
dotnet test .\ClawSharp.sln -c Release
```

Recommended packaging smoke test:

```powershell
pwsh .\eng\publish-cli.ps1 -Version 0.1.0 -RuntimeIdentifiers win-x64 -OutputRoot artifacts\local-release

pwsh .\eng\stage-npm-packages.ps1 -Version 0.1.0 -PublishedRoot artifacts\local-release\publish -OutputRoot artifacts\npm-local -RuntimeIdentifiers win-x64 -UseLocalDependencyReferences

npm pack .\artifacts\npm-local\@clawsharp\cli-win32-x64 --pack-destination .\artifacts\npm-packs

npm pack .\artifacts\npm-local\clawsharp --pack-destination .\artifacts\npm-packs

npm install --prefix artifacts\npm-smoke .\artifacts\npm-packs\clawsharp-cli-win32-x64-0.1.0.tgz .\artifacts\npm-packs\clawsharp-0.1.0.tgz .\artifacts\npm-smoke\node_modules\.bin\clawsharp.cmd --help

```

## Cutting A Release

1. Update `Directory.Build.props` to the target version.
2. Commit the version change and any release notes or supporting documentation.
3. Create the release tag:

```powershell
git tag v0.1.0
```

4. Push the tag:

```powershell
git push origin v0.1.0
```

5. Monitor the `release` GitHub Actions workflow until all jobs complete successfully.

## What The Release Workflow Does

The release workflow is split into three jobs.

### `validate`

This job:

- resolves the tag and semantic version
- restores dependencies
- builds the solution in Release mode
- runs the full test suite
- performs a Linux x64 packaging smoke test through the staged npm tarballs

This is the gate that should fail fast if the release tag points at a bad revision.

### `build-release-assets`

This job runs as a matrix across:

- `windows-latest`
- `ubuntu-latest`
- `macos-14`

It publishes the RID-specific self-contained binaries and uploads the staged release output as workflow artifacts.

### `publish-release`

This job:

- downloads the staged release artifacts
- writes `SHA256SUMS`
- generates GitHub artifact attestations
- stages npm packages from the published binaries
- creates or updates the GitHub Release
- publishes the platform npm packages
- publishes the root `clawsharp` npm package

## Post-Release Verification

After the workflow finishes, verify the release from both GitHub Releases and npm.

### GitHub Release verification

Check that the release includes:

- all six RID archives
- `SHA256SUMS`
- successful provenance attestation output in the workflow

Optional local checksum verification:

```powershell
Get-FileHash .\clawsharp-0.1.0-win-x64.zip -Algorithm SHA256
```

Optional attestation verification:

```powershell
gh attestation verify .\clawsharp-0.1.0-win-x64.zip -R <owner>/<repo>
```

### npm verification

Verify a clean install path from npm:

```powershell
npm install -g clawsharp@latest
clawsharp --version
clawsharp --help
```

Also verify the update path:

```powershell
npm update -g clawsharp
```

## Rollback And Recovery

If a release is broken after publication, the safest response is usually a follow-up patch release.

Recommended order:

1. stop advertising the bad version
2. fix the issue on the release branch or main branch
3. cut a new patch tag, for example `v0.1.1`
4. publish the corrected release through the normal workflow

Avoid mutating an existing release tag unless there is an exceptional repository-level reason to do so.

If npm publication fails after GitHub Release assets are already published:

- inspect the failed workflow step
- fix the npm auth or package issue
- rerun the workflow if safe, or publish the affected package set manually with the same version only if the version has not already been partially consumed in a conflicting way
- if the published state is ambiguous, prefer shipping a new patch version instead of forcing manual repair

## Troubleshooting

### Release workflow fails before publishing

Check:

- `Directory.Build.props` version value
- test failures in the `validate` job
- smoke-test failures in the staged npm tarball install path
- RID-specific publish failures in the matrix job

### GitHub Release exists but is missing assets

Check:

- the `publish-release` job logs
- whether artifact download merged all matrix outputs
- whether the `gh release upload` step failed or only partially uploaded files

### npm publish fails

Check:

- npm trusted publishing configuration
- `NPM_TOKEN` permissions if token publishing is being used
- package ownership for all `@clawsharp/cli-*` packages and the root `clawsharp` package
- whether the target version already exists on npm

### User reports install works but binary does not start

Check:

- whether the correct platform package was installed
- whether the packaged binary exists under the platform package `bin/` folder
- whether Unix executable permissions were preserved or repaired by the launcher
- whether the issue is limited to one RID and points to a broken publish artifact
