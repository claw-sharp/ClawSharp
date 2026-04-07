[CmdletBinding()]
param(
    [string]$PublishedRoot = "artifacts/agenthost/publish",
    [string]$TargetRoot = "apps/desktop/src-tauri/binaries"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedPublishedRoot = Join-Path $repoRoot $PublishedRoot
$resolvedTargetRoot = Join-Path $repoRoot $TargetRoot

if (-not (Test-Path $resolvedPublishedRoot)) {
    throw "Published AgentHost outputs not found: $resolvedPublishedRoot"
}

New-Item -ItemType Directory -Path $resolvedTargetRoot -Force | Out-Null

Get-ChildItem -Path $resolvedPublishedRoot -Directory | ForEach-Object {
    $targetDir = Join-Path $resolvedTargetRoot $_.Name
    if (Test-Path $targetDir) {
        Remove-Item -LiteralPath $targetDir -Recurse -Force
    }

    Copy-Item -LiteralPath $_.FullName -Destination $targetDir -Recurse -Force
}
