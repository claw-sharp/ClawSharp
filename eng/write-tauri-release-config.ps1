[CmdletBinding()]
param(
    [string]$Base = "apps/desktop/src-tauri/tauri.conf.json",
    [string]$Output = "apps/desktop/src-tauri/tauri.release.conf.json",
    [string]$Owner = "claw-sharp",
    [string]$Repo = "ClawSharp",
    [string]$Tag = "",
    [string]$Endpoint = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$args = @(
    (Join-Path $repoRoot "eng/desktop-release-tools.mjs"),
    "write-tauri-release-config",
    "--base", $Base,
    "--output", $Output,
    "--owner", $Owner,
    "--repo", $Repo
)

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $args += @("--tag", $Tag)
}

if (-not [string]::IsNullOrWhiteSpace($Endpoint)) {
    $args += @("--endpoint", $Endpoint)
}

& node @args
if ($LASTEXITCODE -ne 0) {
    throw "Failed to write Tauri release config."
}
