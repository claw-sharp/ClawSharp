# Security Policy

## Supported Versions

Security fixes are applied to the latest released version only.

| Version | Supported |
|---------|-----------|
| Latest  | ✅ Yes    |
| Older   | ❌ No     |

## Reporting a Vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

ClawSharp executes shell commands, reads and writes files, and facilitates AI-driven tool use with broad system access. Security issues should be disclosed privately to give maintainers time to release a fix before the vulnerability is made public.

### How to report

Open a [GitHub Security Advisory](https://github.com/claw-sharp/ClawSharp/security/advisories/new) on the repository. This keeps the report private and allows maintainers to coordinate a fix and disclosure timeline with you.

Please include:

- A description of the vulnerability
- Steps to reproduce
- The potential impact (what an attacker could achieve)
- ClawSharp version and platform (`clawsharp --version`, OS, architecture)
- Any suggested mitigations if you have them

### What to expect

- You will receive an acknowledgement within **5 business days**.
- We will investigate and aim to release a patch within **30 days** for confirmed issues.
- We will credit you in the release notes unless you prefer to remain anonymous.

## Security Considerations

ClawSharp is a terminal-native agentic coding assistant. Users should be aware of the following:

- **Shell execution**: ClawSharp can execute arbitrary shell commands on behalf of the AI model. Only run it in environments and with permission modes you are comfortable with.
- **File access**: The tool has full read/write access to the filesystem within your workspace.
- **API keys**: Never hard-code API keys in source files. Use environment variables or the supported auth file paths documented in [docs/authentication.md](docs/authentication.md).
- **MCP servers**: Third-party MCP servers you configure run as local processes. Only connect to MCP servers you trust.
