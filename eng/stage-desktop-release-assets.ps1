[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$BundleRoot,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [Parameter(Mandatory = $true)][string]$Platform,
    [Parameter(Mandatory = $true)][string]$Arch
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
& node (Join-Path $repoRoot "eng/desktop-release-tools.mjs") `
    "stage-desktop-release-assets" `
    "--version" $Version `
    "--bundle-root" $BundleRoot `
    "--output-root" $OutputRoot `
    "--platform" $Platform `
    "--arch" $Arch

if ($LASTEXITCODE -ne 0) {
    throw "Failed to stage desktop release assets."
}
