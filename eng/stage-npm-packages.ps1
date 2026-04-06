[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$PublishedRoot = "artifacts/release/publish",
    [string]$OutputRoot = "artifacts/npm",
    [string[]]$RuntimeIdentifiers = @(
        "win-x64",
        "win-arm64",
        "linux-x64",
        "linux-arm64",
        "osx-x64",
        "osx-arm64"
    ),
    [switch]$UseLocalDependencyReferences
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$templatesRoot = Join-Path $repoRoot "packaging\npm"
$resolvedPublishedRoot = Join-Path $repoRoot $PublishedRoot
$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot
$readmePath = Join-Path $repoRoot "README.md"
$licensePath = Join-Path $repoRoot "LICENSE"
$chmod = Get-Command chmod -ErrorAction SilentlyContinue

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -Raw (Join-Path $repoRoot "Directory.Build.props")
    $Version = $props.Project.PropertyGroup.VersionPrefix
}

$packageMap = @{
    "win-x64" = @{
        PackageName = "@clawsharp/cli-win32-x64"
        BinaryName = "clawsharp.exe"
    }
    "win-arm64" = @{
        PackageName = "@clawsharp/cli-win32-arm64"
        BinaryName = "clawsharp.exe"
    }
    "linux-x64" = @{
        PackageName = "@clawsharp/cli-linux-x64"
        BinaryName = "clawsharp"
    }
    "linux-arm64" = @{
        PackageName = "@clawsharp/cli-linux-arm64"
        BinaryName = "clawsharp"
    }
    "osx-x64" = @{
        PackageName = "@clawsharp/cli-darwin-x64"
        BinaryName = "clawsharp"
    }
    "osx-arm64" = @{
        PackageName = "@clawsharp/cli-darwin-arm64"
        BinaryName = "clawsharp"
    }
}

if (Test-Path $resolvedOutputRoot) {
    Remove-Item -LiteralPath $resolvedOutputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
Copy-Item -Path (Join-Path $templatesRoot "*") -Destination $resolvedOutputRoot -Recurse -Force

function Update-PackageJson {
    param(
        [string]$Path,
        [hashtable]$OptionalDependencies = @{}
    )

    $packageJson = Get-Content -Raw $Path | ConvertFrom-Json -AsHashtable
    $packageJson["version"] = $Version

    if ($OptionalDependencies.Count -gt 0 -or $packageJson.ContainsKey("optionalDependencies")) {
        $packageJson["optionalDependencies"] = [ordered]@{}
        foreach ($name in $OptionalDependencies.Keys) {
            $packageJson["optionalDependencies"][$name] = $OptionalDependencies[$name]
        }
    }

    $packageJson | ConvertTo-Json -Depth 20 | Set-Content -NoNewline $Path
}

function Copy-CommonPackageFiles {
    param([string]$PackageDir)

    Copy-Item -LiteralPath $readmePath -Destination (Join-Path $PackageDir "README.md") -Force
    Copy-Item -LiteralPath $licensePath -Destination (Join-Path $PackageDir "LICENSE") -Force
}

$optionalDependencies = [ordered]@{}
$rootPackageDir = Join-Path $resolvedOutputRoot "clawsharp"

foreach ($rid in $RuntimeIdentifiers) {
    if (-not $packageMap.ContainsKey($rid)) {
        throw "Unsupported RID '$rid'."
    }

    $packageInfo = $packageMap[$rid]
    $packageDir = Join-Path $resolvedOutputRoot ($packageInfo.PackageName.Replace("/", "\"))
    $publishDir = Join-Path $resolvedPublishedRoot $rid
    $binaryPath = Join-Path $publishDir $packageInfo.BinaryName
    if (-not (Test-Path $binaryPath)) {
        throw "Published binary was not found for RID '$rid': $binaryPath"
    }

    $binDir = Join-Path $packageDir "bin"
    New-Item -ItemType Directory -Path $binDir -Force | Out-Null
    Copy-Item -LiteralPath $binaryPath -Destination (Join-Path $binDir $packageInfo.BinaryName) -Force

    if (-not $rid.StartsWith("win-", [System.StringComparison]::Ordinal) -and $null -ne $chmod) {
        & $chmod.Source "+x" (Join-Path $binDir $packageInfo.BinaryName)
        if ($LASTEXITCODE -ne 0) {
            throw "chmod failed for RID '$rid'."
        }
    }

    Copy-CommonPackageFiles -PackageDir $packageDir
    Update-PackageJson -Path (Join-Path $packageDir "package.json")

    $dependencyValue = $Version
    if ($UseLocalDependencyReferences) {
        $relativePath = [System.IO.Path]::GetRelativePath($rootPackageDir, $packageDir).Replace("\", "/")
        $dependencyValue = "file:$relativePath"
    }

    $optionalDependencies[$packageInfo.PackageName] = $dependencyValue
}

Copy-CommonPackageFiles -PackageDir $rootPackageDir
Update-PackageJson -Path (Join-Path $rootPackageDir "package.json") -OptionalDependencies $optionalDependencies
