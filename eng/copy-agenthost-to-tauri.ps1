[CmdletBinding()]
param(
    [string]$PublishedRoot = "artifacts/agenthost/publish",
    [string]$TargetRoot = "apps/desktop/src-tauri/binaries",
    [string[]]$RuntimeIdentifiers = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedPublishedRoot = Join-Path $repoRoot $PublishedRoot
$resolvedTargetRoot = Join-Path $repoRoot $TargetRoot

if (-not (Test-Path $resolvedPublishedRoot)) {
    throw "Published AgentHost outputs not found: $resolvedPublishedRoot"
}

function Get-AgentHostExecutableName([string]$RuntimeIdentifier) {
    if ($RuntimeIdentifier.StartsWith("win-")) {
        return "clawsharp-agenthost.exe"
    }

    return "clawsharp-agenthost"
}

New-Item -ItemType Directory -Path $resolvedTargetRoot -Force | Out-Null

if ($RuntimeIdentifiers.Count -eq 0) {
    $RuntimeIdentifiers = @(Get-ChildItem -Path $resolvedPublishedRoot -Directory | Select-Object -ExpandProperty Name | Sort-Object)
}

foreach ($rid in $RuntimeIdentifiers) {
    if ([string]::IsNullOrWhiteSpace($rid)) {
        continue
    }

    $sourceDir = Join-Path $resolvedPublishedRoot $rid
    $sourceExecutable = Join-Path $sourceDir (Get-AgentHostExecutableName -RuntimeIdentifier $rid)
    if (-not (Test-Path $sourceExecutable)) {
        throw "Published AgentHost executable not found for RID '$rid': $sourceExecutable"
    }

    $targetDir = Join-Path $resolvedTargetRoot $rid
    if (Test-Path $targetDir) {
        Remove-Item -LiteralPath $targetDir -Recurse -Force
    }

    Copy-Item -LiteralPath $sourceDir -Destination $targetDir -Recurse -Force

    $targetExecutable = Join-Path $targetDir (Get-AgentHostExecutableName -RuntimeIdentifier $rid)
    if (-not (Test-Path $targetExecutable)) {
        throw "Copied AgentHost executable not found for RID '$rid': $targetExecutable"
    }
}
