# Extension System Migration

As `ClawSharp` transitions into its C# terminal-native architecture, migrating from existing extensions (such as Javascript/TypeScript-based tools or tools from previous implementations) to the new `ClawSharp.Extensions` ecosystem is necessary.

This document outlines the migration strategies and concepts.

## Core Conceptual Changes

The new `ClawSharp` architecture strictly defines capabilities inside the layer logic. Specifically:
- **Agents:** Handled by `AgentBootstrapper` (Markdown frontmatter configurations).
- **Hooks & Skills:** Parsed and bootstrapped by `ExtensionBootstrapper`.
- **MCP Services:** Connected and orchestrated via `McpLifecycleManager` and standard capabilities.

If you are migrating standalone CLI scripts or TypeScript-based local tools:

1. **Use External MCP Servers:** The strongly recommended way to extend capabilities inside ClawSharp is to rely on **Model Context Protocol (MCP)**. Instead of rewriting your logic in C#, you can continue treating your existing node.js or python extensions as local MCP servers. ClawSharp acts as a fully compliant MCP client. 

2. **Rewrite as Built-in C# Tools:** If the extension is core to the application or too inherently tied to the system's memory constraints, it should be migrated into the `ClawSharp.Tools` project. Ensure it implements the strict schemas requested by the C# Tool Registry.

## Migrating Legacy Scripts to MCP

1. Initialize your existing scripts into standard `stdio`-based MCP servers.
2. Inside your `ClawSharp` runtime configuration, reference the new MCP server instances.
3. The `McpLifecycleManager` will handle spinning up the server and registering your tools alongside existing `ClawSharp.Tools`.

## Migrating Hooks & Skills

For skills previously defined as pure file prompts, ensure they are situated correctly so the `ExtensionBootstrapper` identifies them by their file extensions or schema declarations. Refer to `ClawSharpApplicationFactory` inside `ClawSharp.Infrastructure` to see the exact locations the app scans during load.

## Future Roadmap

Eventually, advanced extension mechanics like isolated DLL plugins and interprocess plugins without the full MCP overhead may be added depending on user demand. For now, MCP and baked-in C# tools are the golden paths.
