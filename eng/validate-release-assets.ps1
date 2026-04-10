[CmdletBinding()]
param(
    [string]$PublishedRoot = "artifacts/agenthost/publish",
    [string]$TauriBinariesRoot = "apps/desktop/src-tauri/binaries",
    [string]$ReleaseBundleRoot = "",
    [string[]]$RuntimeIdentifiers = @()
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$resolvedPublishedRoot = Join-Path $repoRoot $PublishedRoot
$resolvedTauriBinariesRoot = Join-Path $repoRoot $TauriBinariesRoot
$resolvedTauriConfigPath = Join-Path $repoRoot "apps/desktop/src-tauri/tauri.conf.json"

function Get-AgentHostExecutableName([string]$RuntimeIdentifier) {
    if ($RuntimeIdentifier.StartsWith("win-")) {
        return "clawsharp-agenthost.exe"
    }

    return "clawsharp-agenthost"
}

function Test-TauriBundlesAgentHostResources {
    if (-not (Test-Path $resolvedTauriConfigPath)) {
        throw "Tauri config not found: $resolvedTauriConfigPath"
    }

    $config = Get-Content $resolvedTauriConfigPath -Raw | ConvertFrom-Json
    $resources = $config.bundle.resources
    if ($null -eq $resources) {
        return $false
    }

    if ($resources -is [System.Collections.IDictionary]) {
        foreach ($key in $resources.Keys) {
            if ($key -eq "binaries/") {
                return $true
            }
        }

        return $false
    }

    foreach ($entry in $resources) {
        if ($entry -eq "binaries/") {
            return $true
        }
    }

    return $false
}

if (-not (Test-TauriBundlesAgentHostResources)) {
    throw "Tauri bundle.resources must include 'binaries/' so the AgentHost sidecar is packaged."
}

if ($RuntimeIdentifiers.Count -eq 0) {
    if (-not (Test-Path $resolvedPublishedRoot)) {
        throw "Published AgentHost outputs not found: $resolvedPublishedRoot"
    }

    $RuntimeIdentifiers = @(Get-ChildItem -Path $resolvedPublishedRoot -Directory | Select-Object -ExpandProperty Name | Sort-Object)
}

if ($RuntimeIdentifiers.Count -eq 0) {
    throw "No runtime identifiers found to validate."
}

foreach ($rid in $RuntimeIdentifiers) {
    if ([string]::IsNullOrWhiteSpace($rid)) {
        continue
    }

    $expectedExecutable = Get-AgentHostExecutableName -RuntimeIdentifier $rid
    $publishedExecutable = Join-Path (Join-Path $resolvedPublishedRoot $rid) $expectedExecutable
    $tauriExecutable = Join-Path (Join-Path $resolvedTauriBinariesRoot $rid) $expectedExecutable

    if (-not (Test-Path $publishedExecutable)) {
        throw "Missing published AgentHost executable for RID '$rid': $publishedExecutable"
    }

    if (-not (Test-Path $tauriExecutable)) {
        throw "Missing Tauri sidecar executable for RID '$rid': $tauriExecutable"
    }
}

if (-not [string]::IsNullOrWhiteSpace($ReleaseBundleRoot)) {
    $resolvedReleaseBundleRoot = Join-Path $repoRoot $ReleaseBundleRoot
    if (-not (Test-Path $resolvedReleaseBundleRoot)) {
        throw "Release bundle root not found: $resolvedReleaseBundleRoot"
    }
}

Write-Host ("Validated AgentHost publish and Tauri sidecar layout for: {0}" -f ($RuntimeIdentifiers -join ", "))
