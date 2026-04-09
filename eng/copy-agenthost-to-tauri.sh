#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
published_root="${PUBLISHED_ROOT:-$repo_root/artifacts/agenthost/publish}"
target_root="${TARGET_ROOT:-$repo_root/apps/desktop/src-tauri/binaries}"
runtime_ids_arg="${RUNTIME_IDENTIFIERS:-}"

if [[ ! -d "$published_root" ]]; then
  echo "Published AgentHost outputs not found: $published_root" >&2
  exit 1
fi

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

mkdir -p "$target_root"

if [[ -n "$runtime_ids_arg" ]]; then
  IFS=',' read -r -a runtime_identifiers <<< "$runtime_ids_arg"
else
  mapfile -t runtime_identifiers < <(find "$published_root" -mindepth 1 -maxdepth 1 -type d -exec basename {} \; | sort)
fi

for rid in "${runtime_identifiers[@]}"; do
  if [[ -z "$rid" ]]; then
    continue
  fi

  source_dir="$published_root/$rid"
  source_executable="$source_dir/$(expected_executable_name "$rid")"
  target_dir="$target_root/$rid"
  target_executable="$target_dir/$(expected_executable_name "$rid")"

  if [[ ! -f "$source_executable" ]]; then
    echo "Published AgentHost executable not found for $rid: $source_executable" >&2
    exit 1
  fi

  rm -rf "$target_dir"
  cp -R "$source_dir" "$target_dir"

  if [[ ! -f "$target_executable" ]]; then
    echo "Copied AgentHost executable not found for $rid: $target_executable" >&2
    exit 1
  fi
done
