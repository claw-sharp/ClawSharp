[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [Parameter(Mandatory=$true)]
    [string]$AssetsRoot,
    [string]$Owner,
    [string]$Repo,
    [string]$Tag,
    [string]$Output
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$args = @((Join-Path $repoRoot "eng/desktop-release-tools.mjs"), "generate-scoop-manifest", "--version", $Version, "--assets-root", $AssetsRoot)

if (-not [string]::IsNullOrWhiteSpace($Owner)) {
    $args += @("--owner", $Owner)
}
if (-not [string]::IsNullOrWhiteSpace($Repo)) {
    $args += @("--repo", $Repo)
}
if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $args += @("--tag", $Tag)
}
if (-not [string]::IsNullOrWhiteSpace($Output)) {
    $args += @("--output", $Output)
}

& node @args
