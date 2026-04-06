[CmdletBinding()]
param(
    [string]$ArtifactsRoot = "artifacts/release/packages",
    [string]$OutputPath = "artifacts/release/SHA256SUMS"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedArtifactsRoot = Join-Path $repoRoot $ArtifactsRoot
$resolvedOutputPath = Join-Path $repoRoot $OutputPath
$outputDirectory = Split-Path -Parent $resolvedOutputPath

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$lines = Get-ChildItem -LiteralPath $resolvedArtifactsRoot -File |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $($_.Name)"
    }

Set-Content -LiteralPath $resolvedOutputPath -Value $lines
Write-Host "Wrote $resolvedOutputPath"
