[CmdletBinding()]
param(
    [string]$Tag = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$args = @((Join-Path $repoRoot "eng/desktop-release-tools.mjs"), "validate-version")

if (-not [string]::IsNullOrWhiteSpace($Tag)) {
    $args += @("--tag", $Tag)
}

& node @args
if ($LASTEXITCODE -ne 0) {
    throw "Failed to resolve the desktop version."
}
