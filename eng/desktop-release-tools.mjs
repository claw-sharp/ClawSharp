#!/usr/bin/env node

import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function parseArgs(argv) {
  const positional = [];
  const options = {};

  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (!token.startsWith("--")) {
      positional.push(token);
      continue;
    }

    const key = token.slice(2);
    const next = argv[index + 1];
    if (!next || next.startsWith("--")) {
      options[key] = "true";
      continue;
    }

    options[key] = next;
    index += 1;
  }

  return { positional, options };
}

function requireOption(options, name) {
  const value = options[name];
  if (!value) {
    throw new Error(`Missing required option --${name}`);
  }

  return value;
}

function resolveRepoPath(relativeOrAbsolutePath) {
  if (path.isAbsolute(relativeOrAbsolutePath)) {
    return relativeOrAbsolutePath;
  }

  return path.join(repoRoot, relativeOrAbsolutePath);
}

function readJson(jsonPath) {
  return JSON.parse(fs.readFileSync(jsonPath, "utf8"));
}

function writeJson(jsonPath, value) {
  fs.mkdirSync(path.dirname(jsonPath), { recursive: true });
  fs.writeFileSync(jsonPath, `${JSON.stringify(value, null, 2)}\n`);
}

function sha256(filePath) {
  const hash = crypto.createHash("sha256");
  hash.update(fs.readFileSync(filePath));
  return hash.digest("hex");
}

function basenameWithoutSig(fileName) {
  return fileName.endsWith(".sig") ? fileName.slice(0, -4) : fileName;
}

function normalizeVersion(version) {
  if (version.startsWith("desktop-v")) {
    return version.slice("desktop-v".length);
  }

  if (version.startsWith("v")) {
    return version.slice(1);
  }

  return version;
}

function readDesktopVersions() {
  const tauriConfig = readJson(resolveRepoPath("apps/desktop/src-tauri/tauri.conf.json"));
  const cargoToml = fs.readFileSync(resolveRepoPath("apps/desktop/src-tauri/Cargo.toml"), "utf8");
  const packageJson = readJson(resolveRepoPath("apps/desktop/package.json"));
  const cargoVersionMatch = cargoToml.match(/^version = "([^"]+)"$/m);
  if (!cargoVersionMatch) {
    throw new Error("Could not resolve version from apps/desktop/src-tauri/Cargo.toml");
  }

  return {
    tauri: tauriConfig.version,
    cargo: cargoVersionMatch[1],
    packageJson: packageJson.version,
  };
}

function validateDesktopVersions(expectedTag) {
  const versions = readDesktopVersions();
  const unique = new Set(Object.values(versions));
  if (unique.size !== 1) {
    throw new Error(
      `Desktop version mismatch: tauri.conf.json=${versions.tauri}, Cargo.toml=${versions.cargo}, package.json=${versions.packageJson}`,
    );
  }

  const resolvedVersion = versions.tauri;
  if (expectedTag) {
    const normalizedExpectedVersion = normalizeVersion(expectedTag);
    if (resolvedVersion !== normalizedExpectedVersion) {
      throw new Error(`Desktop tag/version mismatch: tag=${expectedTag}, version=${resolvedVersion}`);
    }
  }

  return resolvedVersion;
}

function writeTauriReleaseConfig(options) {
  const basePath = resolveRepoPath(options.base ?? "apps/desktop/src-tauri/tauri.conf.json");
  const outputPath = resolveRepoPath(options.output ?? "apps/desktop/src-tauri/tauri.release.conf.json");
  const version = validateDesktopVersions(options.tag);
  const publicKey = options.pubkey ?? process.env.TAURI_UPDATER_PUBLIC_KEY;
  if (!publicKey) {
    throw new Error("TAURI_UPDATER_PUBLIC_KEY is required to prepare the release config.");
  }

  const owner = options.owner ?? "claw-sharp";
  const repo = options.repo ?? "ClawSharp";
  const endpoint =
    options.endpoint ?? `https://github.com/${owner}/${repo}/releases/latest/download/latest.json`;
  const baseConfig = readJson(basePath);
  const releaseConfig = {
    ...baseConfig,
    version,
    bundle: {
      ...(baseConfig.bundle ?? {}),
      active: true,
      createUpdaterArtifacts: true,
      targets: "all",
      windows: {
        ...(baseConfig.bundle?.windows ?? {}),
        nsis: {
          ...(baseConfig.bundle?.windows?.nsis ?? {}),
          installMode: "currentUser",
        },
      },
    },
    plugins: {
      ...(baseConfig.plugins ?? {}),
      updater: {
        pubkey: publicKey.trim(),
        endpoints: [endpoint],
      },
    },
  };

  writeJson(outputPath, releaseConfig);
}

function stageDesktopReleaseAssets(options) {
  const version = normalizeVersion(requireOption(options, "version"));
  const bundleRoot = resolveRepoPath(requireOption(options, "bundle-root"));
  const outputRoot = resolveRepoPath(requireOption(options, "output-root"));
  const platform = requireOption(options, "platform");
  const arch = requireOption(options, "arch");
  const files = fs.readdirSync(bundleRoot, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile())
    .map((entry) => path.join(entry.parentPath ?? entry.path ?? bundleRoot, entry.name));

  const mappings = [];
  if (platform === "windows") {
    const nsisPath = files.find((filePath) => filePath.endsWith(".exe") && !filePath.endsWith(".exe.sig"));
    const nsisSigPath = files.find((filePath) => filePath.endsWith(".exe.sig"));
    const msiPath = files.find((filePath) => filePath.endsWith(".msi"));
    const msiSigPath = files.find((filePath) => filePath.endsWith(".msi.sig"));
    if (!nsisPath || !nsisSigPath || !msiPath || !msiSigPath) {
      throw new Error(`Expected .exe/.msi updater artifacts under ${bundleRoot}`);
    }

    mappings.push(
      [nsisPath, `ClawSharp_${version}_windows_${arch}_setup.exe`],
      [nsisSigPath, `ClawSharp_${version}_windows_${arch}_setup.exe.sig`],
      [msiPath, `ClawSharp_${version}_windows_${arch}.msi`],
      [msiSigPath, `ClawSharp_${version}_windows_${arch}.msi.sig`],
    );
  } else if (platform === "darwin") {
    const dmgPath = files.find((filePath) => filePath.endsWith(".dmg"));
    const appTarballPath = files.find((filePath) => filePath.endsWith(".app.tar.gz"));
    const appTarballSigPath = files.find((filePath) => filePath.endsWith(".app.tar.gz.sig"));
    if (!dmgPath || !appTarballPath || !appTarballSigPath) {
      throw new Error(`Expected .dmg and .app.tar.gz updater artifacts under ${bundleRoot}`);
    }

    mappings.push(
      [dmgPath, `ClawSharp_${version}_darwin_${arch}.dmg`],
      [appTarballPath, `ClawSharp_${version}_darwin_${arch}.app.tar.gz`],
      [appTarballSigPath, `ClawSharp_${version}_darwin_${arch}.app.tar.gz.sig`],
    );
  } else {
    throw new Error(`Unsupported platform '${platform}'`);
  }

  fs.mkdirSync(outputRoot, { recursive: true });
  for (const [sourcePath, targetName] of mappings) {
    const targetPath = path.join(outputRoot, targetName);
    fs.copyFileSync(sourcePath, targetPath);
  }
}

function readSignatureFile(signatureFilePath) {
  return fs.readFileSync(signatureFilePath, "utf8").trim();
}

function buildReleaseUrl(owner, repo, tag, fileName) {
  return `https://github.com/${owner}/${repo}/releases/download/${tag}/${fileName}`;
}

function prepareUpdaterMetadata(options) {
  const version = normalizeVersion(requireOption(options, "version"));
  const tag = options.tag ?? `desktop-v${version}`;
  const owner = options.owner ?? "claw-sharp";
  const repo = options.repo ?? "ClawSharp";
  const assetsRoot = resolveRepoPath(requireOption(options, "assets-root"));
  const outputPath = resolveRepoPath(options.output ?? path.join(assetsRoot, "latest.json"));
  const notes = options.notes ?? `ClawSharp desktop ${version}`;

  const files = fs.readdirSync(assetsRoot);
  const platforms = {};

  const windowsSetup = files.find((fileName) => /_windows_x64_setup\.exe$/.test(fileName));
  if (windowsSetup) {
    const signature = readSignatureFile(path.join(assetsRoot, `${windowsSetup}.sig`));
    platforms["windows-x86_64"] = {
      signature,
      url: buildReleaseUrl(owner, repo, tag, windowsSetup),
    };
  }

  const macArm = files.find((fileName) => /_darwin_aarch64\.app\.tar\.gz$/.test(fileName));
  if (macArm) {
    const signature = readSignatureFile(path.join(assetsRoot, `${macArm}.sig`));
    platforms["darwin-aarch64"] = {
      signature,
      url: buildReleaseUrl(owner, repo, tag, macArm),
    };
  }

  const macIntel = files.find((fileName) => /_darwin_x86_64\.app\.tar\.gz$/.test(fileName));
  if (macIntel) {
    const signature = readSignatureFile(path.join(assetsRoot, `${macIntel}.sig`));
    platforms["darwin-x86_64"] = {
      signature,
      url: buildReleaseUrl(owner, repo, tag, macIntel),
    };
  }

  if (Object.keys(platforms).length === 0) {
    throw new Error(`No updater-compatible assets were found under ${assetsRoot}`);
  }

  writeJson(outputPath, {
    version,
    notes,
    pub_date: new Date().toISOString(),
    platforms,
  });
}

function generateWingetManifests(options) {
  const version = normalizeVersion(requireOption(options, "version"));
  const tag = options.tag ?? `desktop-v${version}`;
  const owner = options.owner ?? "claw-sharp";
  const repo = options.repo ?? "ClawSharp";
  const assetsRoot = resolveRepoPath(requireOption(options, "assets-root"));
  const outputRoot = resolveRepoPath(options.output ?? `artifacts/desktop/winget/ClawSharp.ClawSharp/${version}`);
  const packageIdentifier = options.packageIdentifier ?? "ClawSharp.ClawSharp";
  const publisher = options.publisher ?? "ClawSharp";
  const packageName = options.packageName ?? "ClawSharp";
  const manifestVersion = options.manifestVersion ?? "1.9.0";
  const installerFile = fs.readdirSync(assetsRoot).find((fileName) => /_windows_x64_setup\.exe$/.test(fileName));
  if (!installerFile) {
    throw new Error(`Could not find a Windows x64 NSIS installer under ${assetsRoot}`);
  }

  const installerPath = path.join(assetsRoot, installerFile);
  const installerUrl = buildReleaseUrl(owner, repo, tag, installerFile);
  const installerSha = sha256(installerPath).toUpperCase();
  fs.mkdirSync(outputRoot, { recursive: true });

  const sharedHeader = `# yaml-language-server: $schema=https://aka.ms/winget-manifest.`;
  const date = new Date().toISOString().slice(0, 10);
  fs.writeFileSync(
    path.join(outputRoot, `${packageIdentifier}.yaml`),
    `${sharedHeader}version.${manifestVersion}.schema.json
PackageIdentifier: ${packageIdentifier}
PackageVersion: ${version}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: ${manifestVersion}
`,
  );

  fs.writeFileSync(
    path.join(outputRoot, `${packageIdentifier}.installer.yaml`),
    `${sharedHeader}installer.${manifestVersion}.schema.json
PackageIdentifier: ${packageIdentifier}
PackageVersion: ${version}
MinimumOSVersion: 10.0.19041.0
InstallerType: nullsoft
Scope: user
UpgradeBehavior: install
ReleaseDate: ${date}
InstallModes:
  - interactive
  - silent
  - silentWithProgress
InstallerSwitches:
  Silent: /S
  SilentWithProgress: /S
Installers:
  - Architecture: x64
    InstallerUrl: ${installerUrl}
    InstallerSha256: ${installerSha}
ManifestType: installer
ManifestVersion: ${manifestVersion}
`,
  );

  fs.writeFileSync(
    path.join(outputRoot, `${packageIdentifier}.locale.en-US.yaml`),
    `${sharedHeader}defaultLocale.${manifestVersion}.schema.json
PackageIdentifier: ${packageIdentifier}
PackageVersion: ${version}
PackageLocale: en-US
Publisher: ${publisher}
PublisherUrl: https://github.com/${owner}/${repo}
PublisherSupportUrl: https://github.com/${owner}/${repo}/issues
Author: ${publisher}
PackageName: ${packageName}
PackageUrl: https://github.com/${owner}/${repo}
License: MIT
LicenseUrl: https://github.com/${owner}/${repo}/blob/main/LICENSE
ShortDescription: ClawSharp desktop app built with Tauri and a .NET AgentHost sidecar.
Description: Desktop distribution for ClawSharp using GitHub Releases, Tauri updater metadata, WinGet, and a Homebrew tap.
Moniker: clawsharp
Tags:
  - tauri
  - desktop
  - ai
ManifestType: defaultLocale
ManifestVersion: ${manifestVersion}
`,
  );
}

function generateHomebrewCask(options) {
  const version = normalizeVersion(requireOption(options, "version"));
  const tag = options.tag ?? `desktop-v${version}`;
  const owner = options.owner ?? "claw-sharp";
  const repo = options.repo ?? "ClawSharp";
  const assetsRoot = resolveRepoPath(requireOption(options, "assets-root"));
  const outputPath = resolveRepoPath(options.output ?? "artifacts/desktop/homebrew/Casks/clawsharp.rb");
  const armAsset = fs.readdirSync(assetsRoot).find((fileName) => /_darwin_aarch64\.dmg$/.test(fileName));
  const intelAsset = fs.readdirSync(assetsRoot).find((fileName) => /_darwin_x86_64\.dmg$/.test(fileName));
  if (!armAsset && !intelAsset) {
    throw new Error(`Could not find a macOS dmg asset under ${assetsRoot}`);
  }

  const sections = [];
  if (armAsset) {
    sections.push(`  on_arm do
    url "${buildReleaseUrl(owner, repo, tag, armAsset)}"
    sha256 "${sha256(path.join(assetsRoot, armAsset))}"
  end`);
  }

  if (intelAsset) {
    sections.push(`  on_intel do
    url "${buildReleaseUrl(owner, repo, tag, intelAsset)}"
    sha256 "${sha256(path.join(assetsRoot, intelAsset))}"
  end`);
  }

  fs.mkdirSync(path.dirname(outputPath), { recursive: true });
  fs.writeFileSync(
    outputPath,
    `cask "clawsharp" do
  version "${version}"
${sections.join("\n")}

  name "ClawSharp"
  desc "ClawSharp desktop app"
  homepage "https://github.com/${owner}/${repo}"
  auto_updates true

  app "ClawSharp.app"

  caveats do
    <<~EOS
      This release is unsigned and not notarized.
      macOS may show Gatekeeper warnings the first time you open ClawSharp.
      Future signed and notarized releases can replace this cask without changing the tap structure.
    EOS
  end
end
`,
  );
}

function main() {
  const { positional, options } = parseArgs(process.argv.slice(2));
  const command = positional[0];
  if (!command) {
    throw new Error("Missing command. Expected one of: get-version, validate-version, write-tauri-release-config, stage-desktop-release-assets, prepare-updater-metadata, generate-winget-manifests, generate-homebrew-cask");
  }

  switch (command) {
    case "get-version":
      process.stdout.write(`${validateDesktopVersions(options.tag)}\n`);
      break;
    case "validate-version":
      process.stdout.write(`${validateDesktopVersions(options.tag)}\n`);
      break;
    case "write-tauri-release-config":
      writeTauriReleaseConfig(options);
      break;
    case "stage-desktop-release-assets":
      stageDesktopReleaseAssets(options);
      break;
    case "prepare-updater-metadata":
      prepareUpdaterMetadata(options);
      break;
    case "generate-winget-manifests":
      generateWingetManifests(options);
      break;
    case "generate-homebrew-cask":
      generateHomebrewCask(options);
      break;
    default:
      throw new Error(`Unsupported command '${command}'`);
  }
}

main();
