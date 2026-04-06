#!/usr/bin/env node

const { spawnSync } = require("node:child_process");
const os = require("node:os");
const path = require("node:path");
const fs = require("node:fs");

function resolveBinary() {
  const platform = os.platform();
  const arch = os.arch();

  let relativePath;

  if (platform === "win32" && arch === "x64") {
    relativePath = "../dist/win-x64/clawsharp.exe";
  } else if (platform === "win32" && arch === "arm64") {
    relativePath = "../dist/win-arm64/clawsharp.exe";
  } else if (platform === "linux" && arch === "x64") {
    relativePath = "../dist/linux-x64/clawsharp";
  } else if (platform === "linux" && arch === "arm64") {
    relativePath = "../dist/linux-arm64/clawsharp";
  } else if (platform === "darwin" && arch === "x64") {
    relativePath = "../dist/osx-x64/clawsharp";
  } else if (platform === "darwin" && arch === "arm64") {
    relativePath = "../dist/osx-arm64/clawsharp";
  } else {
    console.error(`Unsupported platform: ${platform} ${arch}`);
    process.exit(1);
  }

  const fullPath = path.join(__dirname, relativePath);

  if (!fs.existsSync(fullPath)) {
    console.error(`ClawSharp binary not found: ${fullPath}`);
    process.exit(1);
  }

  return fullPath;
}

const binary = resolveBinary();
const args = process.argv.slice(2);

const result = spawnSync(binary, args, {
  stdio: "inherit"
});

if (result.error) {
  console.error(result.error.message);
  process.exit(1);
}

process.exit(result.status ?? 0);