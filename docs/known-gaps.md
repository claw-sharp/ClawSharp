# Known Gaps and Deferred Work

This document tracks all features and architectural elements that remain incomplete or explicitly deferred during our port towards the full, terminal-native ClawSharp parity C# equivalent. 

## Incomplete Feature Parity

1. **Flexible Provider Selection Pipeline**:
   - The current `QueryModelHttpRequestFactory` and HTTP loop implementation is heavily intertwined with the Anthropic and OpenAI structural endpoints.
   - We need better, isolated streaming adapters for complex bespoke platforms without contaminating the central `QueryLoopRunner`.
2. **Context Compaction and Token Accounting**:
   - Advanced context memory compaction, history truncation, and long-session preservation when exceeding model token restrictions is minimal or missing. Current implementation only appends context. 
3. **Recovery Branches and Advanced Branching**:
   - Sophisticated tree-based continuation logic, fallback models when encountering critical schema rejection errors, and session revert branches.
4. **Rich Terminal Interactivity Elements**:
   - The UX is stable, but features like rich visual plotting representations or interactive fuzzy search inputs directly inside the shell might be missing from `ClawSharp.Ui.Terminal`.
5. **Secure Storage Extensions**:
   - Extending our `SecureStorageQueryAuthAccountStateProvider` to natively and cleanly support a wider cross-platform matrix of keychains/credentials without native unmanaged invocations.

## Technical Debt & Refactoring Plans

1. **Decouple the Composition Map**: 
   - `ClawSharpApplicationFactory` is currently acting as a monolithic assembly root. Once feature parity stabilizes, this should be factored into modular extension registrations.
2. **Native Telemetry Exporting:**
   - Migrate diagnostic logs strictly to robust OpenTelemetry pipelines out of the box instead of pure diagnostic traces.
   
*Note: Refer to closed issues or parity project boards before starting new work on features listed here, as they might be actively in progress!*
