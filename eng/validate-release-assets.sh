#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
published_root="${PUBLISHED_ROOT:-$repo_root/artifacts/agenthost/publish}"
tauri_binaries_root="${TAURI_BINARIES_ROOT:-$repo_root/apps/desktop/src-tauri/binaries}"
release_bundle_root="${RELEASE_BUNDLE_ROOT:-}"
runtime_ids_arg="${RUNTIME_IDENTIFIERS:-}"

if [[ $# -ge 1 ]]; then
  runtime_ids_arg="$*"
fi

expected_executable_name() {
  local rid="$1"
  if [[ "$rid" == win-* ]]; then
    printf '%s\n' "clawsharp-agenthost.exe"
  else
    printf '%s\n' "clawsharp-agenthost"
  fi
}

if [[ -n "$runtime_ids_arg" ]]; then
  IFS=',' read -r -a runtime_identifiers <<< "$runtime_ids_arg"
else
  mapfile -t runtime_identifiers < <(find "$published_root" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort)
fi

if [[ ${#runtime_identifiers[@]} -eq 0 ]]; then
  echo "No runtime identifiers found to validate." >&2
  exit 1
fi

for rid in "${runtime_identifiers[@]}"; do
  [[ -n "$rid" ]] || continue

  publish_executable="$published_root/$rid/$(expected_executable_name "$rid")"
  tauri_executable="$tauri_binaries_root/$rid/$(expected_executable_name "$rid")"

  [[ -f "$publish_executable" ]] || {
    echo "Missing published AgentHost executable for $rid: $publish_executable" >&2
    exit 1
  }

  [[ -f "$tauri_executable" ]] || {
    echo "Missing Tauri sidecar executable for $rid: $tauri_executable" >&2
    exit 1
  }
done

if [[ -n "$release_bundle_root" ]]; then
  [[ -d "$release_bundle_root" ]] || {
    echo "Release bundle root not found: $release_bundle_root" >&2
    exit 1
  }
fi

printf 'Validated AgentHost publish and Tauri sidecar layout for: %s\n' "${runtime_identifiers[*]}"
