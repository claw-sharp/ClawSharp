#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
node "$repo_root/eng/desktop-release-tools.mjs" generate-winget-manifests "$@"
