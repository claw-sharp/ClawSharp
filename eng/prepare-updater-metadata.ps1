[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$AssetsRoot,
    [string]$Output = "artifacts/desktop/release/latest.json",
    [string]$Owner = "claw-sharp",
    [string]$Repo = "ClawSharp",
    [string]$Tag = "",
    [string]$Notes = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$args = @(
    (Join-Path $repoRoot "eng/desktop-release-tools.mjs"),
    "prepare-updater-metadata",
    "--version", $Version,
    "--assets-root", $AssetsRoot,
    "--output", $Output,
    "--owner", $Owner,
    "--repo", $Repo
)

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $args += @("--tag", $Tag)
}

if (-not [string]::IsNullOrWhiteSpace($Notes)) {
    $args += @("--notes", $Notes)
}

& node @args
if ($LASTEXITCODE -ne 0) {
    throw "Failed to prepare updater metadata."
}
