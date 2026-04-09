[CmdletBinding()]
param(
    [string]$ProjectPath = "src/ClawSharp.AgentHost/ClawSharp.AgentHost.csproj",
    [string]$Configuration = "Release",
    [string[]]$RuntimeIdentifiers = @(
        "win-x64",
        "win-arm64",
        "linux-x64",
        "linux-arm64",
        "osx-x64",
        "osx-arm64"
    ),
    [string]$OutputRoot = "artifacts/agenthost/publish"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFullPath = (Resolve-Path (Join-Path $repoRoot $ProjectPath)).Path
$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot

function Get-AgentHostExecutableName([string]$RuntimeIdentifier) {
    if ($RuntimeIdentifier.StartsWith("win-")) {
        return "clawsharp-agenthost.exe"
    }

    return "clawsharp-agenthost"
}

foreach ($rid in $RuntimeIdentifiers) {
    if ([string]::IsNullOrWhiteSpace($rid)) {
        continue
    }

    $publishDir = Join-Path $resolvedOutputRoot $rid
    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    & dotnet publish `
        $projectFullPath `
        -c $Configuration `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $publishDir `
        /nologo

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for RID '$rid'."
    }

    $expectedExecutable = Join-Path $publishDir (Get-AgentHostExecutableName -RuntimeIdentifier $rid)
    if (-not (Test-Path $expectedExecutable)) {
        throw "Expected AgentHost executable missing after publish: $expectedExecutable"
    }
}
