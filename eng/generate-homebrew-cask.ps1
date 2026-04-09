[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$AssetsRoot,
    [string]$Output = "artifacts/desktop/homebrew/Casks/clawsharp.rb",
    [string]$Owner = "claw-sharp",
    [string]$Repo = "ClawSharp",
    [string]$Tag = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$args = @(
    (Join-Path $repoRoot "eng/desktop-release-tools.mjs"),
    "generate-homebrew-cask",
    "--version", $Version,
    "--assets-root", $AssetsRoot,
    "--output", $Output,
    "--owner", $Owner,
    "--repo", $Repo
)

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $args += @("--tag", $Tag)
}

& node @args
if ($LASTEXITCODE -ne 0) {
    throw "Failed to generate Homebrew cask."
}
