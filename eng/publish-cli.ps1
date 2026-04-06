[CmdletBinding()]
param(
    [string]$ProjectPath = "src/ClawSharp.Cli/ClawSharp.Cli.csproj",
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string[]]$RuntimeIdentifiers = @(
        "win-x64",
        "win-arm64",
        "linux-x64",
        "linux-arm64",
        "osx-x64",
        "osx-arm64"
    ),
    [string]$OutputRoot = "artifacts/release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectFullPath = (Resolve-Path (Join-Path $repoRoot $ProjectPath)).Path
$resolvedOutputRoot = Join-Path $repoRoot $OutputRoot
$publishRoot = Join-Path $resolvedOutputRoot "publish"
$packagesRoot = Join-Path $resolvedOutputRoot "packages"
$stagingRoot = Join-Path $resolvedOutputRoot "staging"

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content -Raw (Join-Path $repoRoot "Directory.Build.props")
    $Version = $props.Project.PropertyGroup.VersionPrefix
}

foreach ($path in @($publishRoot, $packagesRoot, $stagingRoot)) {
    New-Item -ItemType Directory -Path $path -Force | Out-Null
}

$chmod = Get-Command chmod -ErrorAction SilentlyContinue
$licensePath = Join-Path $repoRoot "LICENSE"
$readmePath = Join-Path $repoRoot "README.md"

foreach ($rid in $RuntimeIdentifiers) {
    $publishDir = Join-Path $publishRoot $rid
    if (Test-Path $publishDir) {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

    $publishArgs = @(
        "publish",
        $projectFullPath,
        "-c", $Configuration,
        "-r", $rid,
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-p:Version=$Version",
        "-p:InformationalVersion=$Version",
        "-o", $publishDir,
        "/nologo"
    )

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for RID '$rid'."
    }

    $binaryName = if ($rid.StartsWith("win-", [System.StringComparison]::Ordinal)) { "clawsharp.exe" } else { "clawsharp" }
    $binaryPath = Join-Path $publishDir $binaryName
    if (-not (Test-Path $binaryPath)) {
        throw "Expected published binary was not found: $binaryPath"
    }

    if (-not $rid.StartsWith("win-", [System.StringComparison]::Ordinal) -and $null -ne $chmod) {
        & $chmod.Source "+x" $binaryPath
        if ($LASTEXITCODE -ne 0) {
            throw "chmod failed for $binaryPath"
        }
    }

    $packageBaseName = "clawsharp-$Version-$rid"
    $packageDir = Join-Path $stagingRoot $packageBaseName
    if (Test-Path $packageDir) {
        Remove-Item -LiteralPath $packageDir -Recurse -Force
    }

    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
    Copy-Item -LiteralPath $binaryPath -Destination (Join-Path $packageDir $binaryName)
    Copy-Item -LiteralPath $licensePath -Destination $packageDir
    Copy-Item -LiteralPath $readmePath -Destination $packageDir

    if (-not $rid.StartsWith("win-", [System.StringComparison]::Ordinal) -and $null -ne $chmod) {
        $stagedBinaryPath = Join-Path $packageDir $binaryName
        & $chmod.Source "+x" $stagedBinaryPath
        if ($LASTEXITCODE -ne 0) {
            throw "chmod failed for $stagedBinaryPath"
        }
    }

    if ($rid.StartsWith("win-", [System.StringComparison]::Ordinal)) {
        $archivePath = Join-Path $packagesRoot "$packageBaseName.zip"
        if (Test-Path $archivePath) {
            Remove-Item -LiteralPath $archivePath -Force
        }

        Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $archivePath -CompressionLevel Optimal
        Write-Host "Created $archivePath"
        continue
    }

    $archivePath = Join-Path $packagesRoot "$packageBaseName.tar.gz"
    if (Test-Path $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Push-Location $stagingRoot
    try {
        & tar -czf $archivePath $packageBaseName
        if ($LASTEXITCODE -ne 0) {
            throw "tar failed for RID '$rid'."
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Created $archivePath"
}
