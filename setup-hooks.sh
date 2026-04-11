#!/bin/bash

# Setup git hooks for the project
# This script should be run after cloning the repository

echo "Setting up git hooks..."

git config core.hooksPath .githooks

if [ $? -eq 0 ]; then
  echo "✓ Git hooks configured successfully"
  echo "Pre-commit hook will run desktop tests before each commit"
else
  echo "✗ Failed to configure git hooks"
  exit 1
fi
