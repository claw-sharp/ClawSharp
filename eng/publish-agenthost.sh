#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="${1:-$repo_root/src/ClawSharp.AgentHost/ClawSharp.AgentHost.csproj}"
configuration="${CONFIGURATION:-Release}"
output_root="${OUTPUT_ROOT:-$repo_root/artifacts/agenthost/publish}"
runtime_ids_arg="${RUNTIME_IDENTIFIERS:-}"

if [[ $# -ge 2 ]]; then
  shift
  runtime_ids_arg="${*:-$runtime_ids_arg}"
fi

if [[ ! -f "$project_path" ]]; then
  echo "AgentHost project not found: $project_path" >&2
  exit 1
fi

if [[ -n "$runtime_ids_arg" ]]; then
  IFS=',' read -r -a runtime_identifiers <<< "$runtime_ids_arg"
else
  runtime_identifiers=(
    "win-x64"
    "win-arm64"
    "linux-x64"
    "linux-arm64"
    "osx-x64"
    "osx-arm64"
  )
fi

expected_executable_name() {
  local rid="$1"
  if [[ "$rid" == win-* ]]; then
    printf '%s\n' "clawsharp-agenthost.exe"
  else
    printf '%s\n' "clawsharp-agenthost"
  fi
}

for rid in "${runtime_identifiers[@]}"; do
  if [[ -z "$rid" ]]; then
    continue
  fi

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

  executable_path="$publish_dir/$(expected_executable_name "$rid")"
  if [[ ! -f "$executable_path" ]]; then
    echo "Expected AgentHost executable missing after publish: $executable_path" >&2
    exit 1
  fi
done
