#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="${1:-$repo_root/src/ClawSharp.AgentHost/ClawSharp.AgentHost.csproj}"
configuration="${CONFIGURATION:-Release}"
output_root="${OUTPUT_ROOT:-$repo_root/artifacts/agenthost/publish}"

runtime_identifiers=(
  "win-x64"
  "win-arm64"
  "linux-x64"
  "linux-arm64"
  "osx-x64"
  "osx-arm64"
)

for rid in "${runtime_identifiers[@]}"; do
  publish_dir="$output_root/$rid"
  rm -rf "$publish_dir"
  mkdir -p "$publish_dir"

  dotnet publish "$project_path" \
    -c "$configuration" \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=None \
    -p:DebugSymbols=false \
    -o "$publish_dir" \
    /nologo
done
