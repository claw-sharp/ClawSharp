#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
published_root="${PUBLISHED_ROOT:-$repo_root/artifacts/agenthost/publish}"
target_root="${TARGET_ROOT:-$repo_root/apps/desktop/src-tauri/binaries}"

if [[ ! -d "$published_root" ]]; then
  echo "Published AgentHost outputs not found: $published_root" >&2
  exit 1
fi

mkdir -p "$target_root"
find "$published_root" -mindepth 1 -maxdepth 1 -type d | while read -r dir; do
  name="$(basename "$dir")"
  rm -rf "$target_root/$name"
  cp -R "$dir" "$target_root/$name"
done
