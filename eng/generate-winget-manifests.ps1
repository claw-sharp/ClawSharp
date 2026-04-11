[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$AssetsRoot,
    [string]$Output = "",
    [string]$Owner = "claw-sharp",
    [string]$Repo = "ClawSharp",
    [string]$Tag = "",
    [string]$PackageIdentifier = "ClawSharp.ClawSharp",
    [string]$Publisher = "ClawSharp",
    [string]$PackageName = "ClawSharp",
    [string]$ManifestVersion = "1.12.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$args = @(
    (Join-Path $repoRoot "eng/desktop-release-tools.mjs"),
    "generate-winget-manifests",
    "--version", $Version,
    "--assets-root", $AssetsRoot,
    "--owner", $Owner,
    "--repo", $Repo,
    "--packageIdentifier", $PackageIdentifier,
    "--publisher", $Publisher,
    "--packageName", $PackageName,
    "--manifestVersion", $ManifestVersion
)

if (-not [string]::IsNullOrWhiteSpace($Output)) {
    $args += @("--output", $Output)
}

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $args += @("--tag", $Tag)
}

& node @args
if ($LASTEXITCODE -ne 0) {
    throw "Failed to generate WinGet manifests."
}
