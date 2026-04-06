#!/usr/bin/env node

const { spawnSync } = require("node:child_process");
const os = require("node:os");

const PLATFORM_MAP = {
  win32: "win32",
  linux: "linux",
  darwin: "darwin",
};

const ARCH_MAP = {
  x64: "x64",
  arm64: "arm64",
};

function resolveBinary() {
  const platform = PLATFORM_MAP[os.platform()];
  const arch = ARCH_MAP[os.arch()];

  if (!platform || !arch) {
    console.error(`Unsupported platform: ${os.platform()} ${os.arch()}`);
    process.exit(1);
  }

  const pkgName = `@clawsharp/cli-${platform}-${arch}`;
  const binaryName = os.platform() === "win32" ? "clawsharp.exe" : "clawsharp";

  try {
    return require.resolve(`${pkgName}/bin/${binaryName}`);
  } catch {
    console.error(
      `ClawSharp binary not found. Package "${pkgName}" may not be installed.\n` +
      `Try running: npm install`
    );
    process.exit(1);
  }
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