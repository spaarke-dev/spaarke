# Lessons Learned -- SprkChat Interactive Collaboration R2

> **Project**: ai-spaarke-platform-enhancents-r2
> **Completed**: 2026-02-26
> **Scope**: 89 tasks across 9 packages (Packages A-I)

---

## Key Architectural Decisions and Rationale

### Code Page Migration (PCF to React 19)

**Decision**: Big-bang migration of AnalysisWorkspace from PCF (React 16, platform-provided) to Code Page (React 19, bundled).

**Rationale**: The incremental iframe approach was rejected in favor of a clean break. PCF controls are constrained to React 16/17 APIs provided by the Dataverse platform, which made streaming writes, concurrent rendering, and modern hooks impossible. Code Pages bundle their own React, enabling React 19 features like `createRoot()`, transitions, and suspense boundaries.

**Outcome**: The migration succeeded cleanly. Independent authentication per Code Page pane (ADR-008) prevented security shortcuts. The two-step build pipeline (webpack + inline HTML via `build-webresource.ps1`) required careful documentation but works reliably.

### SprkChatBridge Cross-Pane Communication

**Decision**: Use `BroadcastChannel` API with `window.postMessage` fallback for cross-pane communication between SprkChat side pane and AnalysisWorkspace.

**Rationale**: SprkChat needed to send streaming document events, selection changes, and context updates to the AnalysisWorkspace without sharing authentication tokens. BroadcastChannel provides synchronous same-origin messaging with a clean API. The `window.postMessage` fallback covers older browser versions.

**Outcome**: The channel name pattern (`sprk-workspace-{context}`) worked well for scoping. The strict rule that auth tokens are never transmitted via BroadcastChannel (independent auth per pane) was enforced successfully throughout the project.

### AI Tool Framework (Zero Additional DI Registrations)

**Decision**: All new AI tool classes (`WorkingDocumentTools`, `AnalysisExecutionTools`, `WebSearchTools`) are factory-instantiated via `AIFunctionFactory.Create` with zero additional DI registrations.

**Rationale**: ADR-010 limits DI registrations to 15 non-framework entries, with 12 already in use. Factory instantiation keeps the DI container lean while still providing full tool functionality through `SprkChatAgentFactory.ResolveTools()`.

**Outcome**: The project added zero DI registrations as planned. All tools are resolved through the factory pattern, keeping the container budget intact for future projects.

---

## Challenges Encountered

### React Version Differences (PCF vs Code Pages)

PCF controls use platform-provided React 16/17, while Code Pages bundle React 19. This created challenges for the shared component library (`@spaarke/ui-components`), which needed to work in both environments. Components that used React 18+ APIs (like `useId` or `useSyncExternalStore`) required compatibility shims or conditional code paths. The Fluent UI v9 dependency was consistent across both, which helped.

### BroadcastChannel Cross-Pane Communication

Debugging cross-pane communication was difficult because messages are asynchronous and panes have independent lifecycles. Issues included:
- Race conditions when the AnalysisWorkspace opened before SprkChat had finished initializing
- Message ordering was not guaranteed during rapid streaming events
- Testing required mock BroadcastChannel implementations since Jest does not provide one

The solution was to implement a handshake protocol during pane initialization and buffer events until both sides acknowledged readiness.

### Parallel Task Execution Coordination

The 3-track parallel execution model (Sprint 1: Packages A/B/D, Sprint 2: C/E/I, Sprint 3: F/G/H) required strict file ownership rules. Shared files like `SprkChat.tsx` and `ChatEndpoints.cs` could only be modified sequentially by one track at a time. This was managed through the POML dependency declarations and placeholder protocol, but required careful attention to avoid merge conflicts.

### Streaming SSE Testing

Integration tests for SSE streaming required an async `ReadableStream` polyfill with multi-chunk delivery to simulate realistic streaming behavior. A single-chunk delivery would not trigger the React render cycle needed for `isStreaming=true` state transitions. The two-chunk pattern (tokens first, metadata+done second) became the standard test approach across all streaming test files.

### Lexical Editor Integration

The `StreamingInsertPlugin` for Lexical required careful handling of the editor state update lifecycle. Direct DOM manipulation was not possible because Lexical manages its own reconciliation. All streaming token insertions had to go through Lexical's `$createTextNode` and `$insertNodes` APIs within an `editor.update()` callback, with proper selection management to maintain cursor position during streaming.

---

## What Worked Well

### Wave-Based Parallel Execution

The 3-track parallel execution model was effective. Each sprint had three independent tracks working on separate packages with no file conflicts. Phase gates between sprints ensured integration points were stable before downstream work began. The sprint structure (Foundation, Integration, Polish) naturally ordered work so that foundational APIs and components were built before they were consumed.

### Placeholder Protocol

The `// PLACEHOLDER: <description> -- Completed by task NNN` convention with typed stubs (`hardcoded-return`, `todo-comment`, `mock-data`, `no-op`) allowed tracks to proceed independently even when dependencies were not yet complete. The placeholder audit (Task 148) verified that all placeholders were resolved before project completion. Only two external placeholders remain (PH-015-A for icon and PH-088 for Bing API provisioning), both dependent on external actions.

### POML Task Format

The structured task format with explicit `<inputs>`, `<constraints>`, `<knowledge>`, `<placeholders>`, and `<steps>` sections provided clear, machine-readable task definitions. This made the `task-execute` skill reliable because each task carried its full context. The `<acceptance-criteria>` sections made task completion unambiguous.

### Shared Component Library

Building shared components in `@spaarke/ui-components` (ADR-012) paid significant dividends. Components like `DiffCompareView`, `SprkChatActionMenu`, `SprkChatSuggestions`, and `SprkChatCitationPopover` were built once and consumed by both the SprkChat side pane and the AnalysisWorkspace Code Page without duplication. The Fluent UI v9 design tokens (ADR-021) ensured consistent theming across all components.

### Proactive Checkpointing

The mandatory checkpointing protocol (every 3 steps, after 5+ file modifications, and at context thresholds) prevented work loss during long implementation sessions. The `current-task.md` file served as a reliable recovery point, and the Quick Recovery section made session resumption fast and accurate.

---

## What Could Be Improved

### Test Infrastructure Complexity

Setting up integration tests for SSE streaming, BroadcastChannel, and cross-pane communication required significant boilerplate. Each test file needed custom polyfills, mock implementations, and multi-chunk stream helpers. A shared test utilities package for streaming SSE assertions and BroadcastChannel mocking would reduce this overhead in future projects.

### Deployment Automation

The deployment phase (Tasks 143-146) involved manual coordination across four deployment targets (SprkChatPane Code Page, AnalysisWorkspace Code Page, BFF API, Dataverse schema). Each had its own deployment script and verification steps. A unified deployment pipeline that orchestrates all four targets with rollback capability would improve reliability and reduce deployment time.

### Dependency Resolution for Parallel Execution

While the placeholder protocol handled forward dependencies well, reverse dependency tracking was manual. When a task was completed that resolved placeholders in other tracks, updating the placeholder tracking table required checking each entry individually. An automated dependency resolution check (scanning for `// PLACEHOLDER:` comments and matching them against the tracking table) would catch stale placeholders earlier.

### Code Page Build Pipeline Documentation

The two-step build pipeline (webpack + inline HTML) is non-obvious and was a source of confusion early in the project. While it is now documented in the deployment checklists, it would benefit from a dedicated guide in `docs/guides/` explaining the full Code Page development and deployment lifecycle.

### Context Window Management

For very long task sequences, the context window filled up faster than expected despite proactive checkpointing. Tasks that modified many files (like the AW migration or the streaming E2E wiring) sometimes required `/compact` mid-task. Future projects could benefit from smaller, more focused tasks for high-file-count operations.

---

## Recommendations for R3 or Follow-Up Work

1. **PlaybookBuilder AI Assistant Convergence**: R3 should converge the PlaybookBuilder's existing AI assistant with SprkChat's capabilities. The `CommandPalette.tsx` and `SuggestionBar.tsx` reference implementations in PlaybookBuilder can inform this but should not be modified until the convergence design is finalized.

2. **Shared Test Utilities Package**: Extract the SSE streaming test helpers, BroadcastChannel mocks, and ReadableStream polyfills into a `@spaarke/test-utils` package for reuse across projects.

3. **Unified Deployment Pipeline**: Create a single deployment script that orchestrates Code Page, BFF API, and Dataverse schema deployments with pre/post verification and rollback.

4. **Real-Time Collaborative Editing**: While explicitly out of scope for R2, the streaming write infrastructure (StreamingInsertPlugin, document history, BroadcastChannel) provides a foundation for future multi-user editing if that requirement emerges.

5. **Office Add-In Integration**: SprkChat's side pane architecture and BroadcastChannel communication could be extended to Office Add-ins, enabling AI collaboration within Word and Excel documents.

6. **Performance Monitoring**: The performance benchmarks (Task 142) established baselines. R3 should add production telemetry to track streaming latency, diff rendering time, and side pane load time against these baselines.

7. **Mobile/Responsive Layout**: The current layout is optimized for desktop. If mobile Dataverse usage grows, the 2-panel layout and side pane will need responsive breakpoints.

8. **Bing API Provisioning**: Placeholder PH-088 (WebSearchTools) uses a hardcoded stub for Bing Search API calls. The Azure Bing Search API must be provisioned and the endpoint configured before web search is production-ready.

---

*This document captures lessons learned from the SprkChat Interactive Collaboration R2 project for reference by future projects.*
