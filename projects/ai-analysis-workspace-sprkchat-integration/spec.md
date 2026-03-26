# Analysis Workspace + SprkChat Integration — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-26
> **Source**: design.md
> **Prerequisite for**: ai-sprk-chat-extensibility-r1

## Executive Summary

Merge the AnalysisWorkspace Code Page and SprkChatPane Code Page into a single unified Code Page. SprkChat becomes an embedded right panel within the Analysis Workspace (React context + callback props) instead of a separate Dataverse side pane communicating via BroadcastChannel. This eliminates cross-pane serialization, duplicate authentication, timing issues, and fixed layout constraints. The unified architecture is a prerequisite for the SprkChat extensibility project.

## Scope

### In Scope

- **AnalysisAiProvider** — shared React context for analysis state, chat state, editor refs, selection, auth
- **ChatPanel wrapper** — mounts `SprkChat` from `@spaarke/ui-components` with props from AnalysisAiContext
- **Three-panel layout** — Editor (left) + Source Document (center, collapsible) + Chat (right, collapsible) with two draggable splitters
- **Panel visibility toggles** — toolbar buttons to show/hide Source and Chat panels, with sessionStorage persistence
- **Direct editor integration** — `onInsertToEditor` callback wired to editor ref; `editorSelection` from editor state to SprkChat props; SSE streaming writes directly via ref
- **Remove SprkChatPane Code Page** — entire `src/client/code-pages/SprkChatPane/` directory
- **Remove cross-pane infrastructure** — DocumentStreamBridge, useSelectionBroadcast, contextService, openSprkChatPane launcher, SidePaneManager
- **Remove Dataverse artifacts** — web resources `sprk_SprkChatPane`, `sprk_openSprkChatPane`, `sprk_SidePaneManager`; global ribbon button XML; LegalWorkspace SidePaneManager script injection
- **Unified auth** — single MSAL initialization and token cache for both editor and chat
- **Unified context resolution** — single `useAnalysisLoader()` provides context to both editor and chat
- **Deprecate SprkChatBridge** — mark deprecated in shared library (do not delete — potential future cross-pane use)
- **M365 Copilot handoff support** — default Chat panel visible when page loads from handoff URL (contains `analysisId` params)

### Out of Scope

- **SprkChat component changes** — components in `@spaarke/ui-components` remain unchanged (callback-based API per ADR-012)
- **SprkChat extensibility features** — slash commands, compound actions, smart routing (separate project: `ai-sprk-chat-extensibility-r1`)
- **BFF API changes** — all existing endpoints unchanged; same API, different consumer
- **New features** — this is a structural refactor, not a feature addition
- **Mobile/responsive layout** — responsive behavior (< 800px) is Phase 4 polish, not structural

### Affected Areas

- `src/client/code-pages/AnalysisWorkspace/src/` — App.tsx restructured to 3-panel; new context, hook, and wrapper files
- `src/client/code-pages/SprkChatPane/` — removed entirely
- `src/client/side-pane-manager/` — removed entirely
- `src/client/shared/Spaarke.UI.Components/src/services/SprkChatBridge.ts` — deprecated (not deleted)
- `src/client/webresources/ribbon/` — remove SprkChat ribbon button XML
- `src/solutions/LegalWorkspace/` — remove SidePaneManager script injection
- Dataverse solution — remove 3 web resources

## Requirements

### Functional Requirements

1. **FR-01: Single Code Page** — AnalysisWorkspace renders both editor and chat in one React tree, one iframe. No side pane. Acceptance: single `sprk_AnalysisWorkspace` web resource serves complete experience.

2. **FR-02: AnalysisAiContext** — shared React context provides analysis state, chat state, editor refs, selection state, and auth context to all panels. Acceptance: both editor and chat read from same context; no duplicate API calls.

3. **FR-03: ChatPanel wrapper** — thin wrapper mounts `SprkChat` from `@spaarke/ui-components` with props sourced from AnalysisAiContext. Acceptance: SprkChat renders in right panel with full functionality.

4. **FR-04: Insert to editor** — AI response "Insert" button writes directly to Lexical editor via ref callback. Acceptance: click Insert → content appears in editor at cursor position, no serialization delay.

5. **FR-05: Editor selection → chat** — text selection in editor updates `editorSelection` prop on SprkChat via React context. Acceptance: select text → inline toolbar appears → action routes through chat → response inserts.

6. **FR-06: SSE streaming to editor** — chat streaming writes tokens directly to editor via ref. Acceptance: streaming content appears in editor in real-time without BroadcastChannel intermediary.

7. **FR-07: Three-panel layout** — Editor + Source Document + Chat with two draggable PanelSplitter components. Default ratios: ~45% / ~30% / ~25%. Acceptance: all three panels render; splitters are draggable.

8. **FR-08: Panel visibility toggles** — toolbar buttons toggle Source and Chat panels. State persisted to sessionStorage. Acceptance: toggle Chat off → Editor + Source fill space; toggle both off → Editor fills 100%.

9. **FR-09: Panel minimum widths** — Editor 300px, Source 200px, Chat 280px. Acceptance: splitter stops at minimum width; panels don't collapse below minimum.

10. **FR-10: Remove SprkChatPane** — entire SprkChatPane Code Page directory removed. Acceptance: `src/client/code-pages/SprkChatPane/` does not exist; no `sprk_SprkChatPane` web resource in solution.

11. **FR-11: Remove cross-pane infrastructure** — DocumentStreamBridge, useSelectionBroadcast, contextService, openSprkChatPane, SidePaneManager all removed. Acceptance: grep for BroadcastChannel in AnalysisWorkspace returns zero results.

12. **FR-12: Single auth** — one MSAL initialization, one token acquisition for both editor and chat operations. Acceptance: network tab shows single token request; no duplicate auth calls.

13. **FR-13: M365 Copilot handoff** — when page loads with handoff URL params (`analysisId`, `sourceFileId`), Chat panel defaults to visible. Acceptance: deep-link from M365 Copilot opens workspace with Chat panel showing.

### Non-Functional Requirements

- **NFR-01: Reusability preserved** — SprkChat components remain in `@spaarke/ui-components` with no AnalysisWorkspace-specific dependencies. No components moved into AnalysisWorkspace.
- **NFR-02: Performance** — page load time not significantly increased vs current (two Code Pages loaded separately). Measure and compare.
- **NFR-03: Bundle size** — SprkChat components already in shared library (tree-shaken). Verify minimal bundle increase. Lazy-load ChatPanel if needed.
- **NFR-04: Render performance** — use React.memo on panels; streaming updates bypass React re-render via editor ref. No jank during SSE streaming.
- **NFR-05: Rollback safety** — SprkChatBridge deprecated (not deleted). All removed code in git history. Revert to two-pane architecture possible by restoring files.

## Technical Constraints

### Applicable ADRs

- **ADR-006**: Code Page for standalone dialogs — AnalysisWorkspace is a Code Page (not PCF)
- **ADR-012**: Shared component library — SprkChat components stay in `@spaarke/ui-components`; callback-based props; runtime-context abstractions for platform-specific operations
- **ADR-021**: Fluent UI v9 exclusively — `makeStyles` for custom styling; design tokens for colors; light/dark/high-contrast support
- **ADR-022**: React 19 for Code Pages — `createRoot`, bundled React (not platform-provided)
- **ADR-026**: Code Page build standard — Vite + `vite-plugin-singlefile`; single HTML output; CSS reset for Dataverse iframe; Xrm frame-walk pattern

### MUST Rules

- ✅ MUST keep SprkChat components in `@spaarke/ui-components` (ADR-012)
- ✅ MUST use callback-based props on SprkChat — no direct service dependencies (ADR-012)
- ✅ MUST use Fluent UI v9 exclusively; `makeStyles` for styling (ADR-021)
- ✅ MUST use React 19 `createRoot` entry point (ADR-022, ADR-026)
- ✅ MUST wrap all UI in `FluentProvider` with theme (ADR-021)
- ✅ MUST use Fluent design tokens for colors/spacing (no hard-coded hex/rgb) (ADR-021)
- ✅ MUST support dark mode and high-contrast (ADR-021)
- ✅ MUST use Vite + `vite-plugin-singlefile` for build (ADR-026)
- ✅ MUST use Xrm frame-walk pattern for Dataverse API access (ADR-026)
- ❌ MUST NOT move SprkChat components into AnalysisWorkspace Code Page
- ❌ MUST NOT add AnalysisWorkspace-specific dependencies to shared library components
- ❌ MUST NOT use BroadcastChannel for editor ↔ chat communication in unified page
- ❌ MUST NOT use Fluent v8 or hard-coded colors

### Existing Patterns to Follow

- SprkChat callback API: `SprkChatProps` interface in `@spaarke/ui-components`
- PanelSplitter: `src/client/code-pages/AnalysisWorkspace/src/components/PanelSplitter.tsx`
- usePanelResize: `src/client/code-pages/AnalysisWorkspace/src/hooks/usePanelResize.ts`
- MSAL auth pattern: existing AnalysisWorkspace `initAuth()` entry point
- Code Page theme detection: 4-level priority (localStorage → URL param → navbar DOM → system preference)

## Success Criteria

1. [ ] **Single page** — AnalysisWorkspace renders editor + chat in one Code Page, no side pane — Verify: single iframe in Dataverse
2. [ ] **No BroadcastChannel** — zero cross-pane messaging; all communication via React context/props — Verify: grep for BroadcastChannel returns zero in AnalysisWorkspace
3. [ ] **Single auth** — one MSAL initialization, one token acquisition — Verify: network tab shows single token request
4. [ ] **Insert to editor** — AI response "Insert" button writes directly to Lexical editor — Verify: content appears at cursor
5. [ ] **Inline toolbar round-trip** — text selection → toolbar → action → chat → response inserts — Verify: full round-trip works
6. [ ] **SSE streaming** — chat streaming writes tokens directly to editor via ref — Verify: no serialization delay
7. [ ] **Resizable panels** — user can drag splitters to resize all three panels — Verify: drag works, min widths enforced
8. [ ] **Collapsible panels** — Source and Chat panels toggleable on/off — Verify: toggle works, layout adjusts
9. [ ] **Reusability preserved** — SprkChat components remain in shared library with no workspace-specific deps — Verify: no AnalysisWorkspace imports in `@spaarke/ui-components`
10. [ ] **SprkChatPane removed** — no `sprk_SprkChatPane` web resource in deployed solution — Verify: solution export
11. [ ] **Performance** — page load time comparable to current — Verify: measure before/after

## Phases

### Phase 1: AnalysisAiContext + ChatPanel Integration

- Create `AnalysisAiContext` with analysis state, editor refs, selection state, auth
- Create `ChatPanel` wrapper mounting `SprkChat` from shared library
- Update `App.tsx` to three-panel layout (Editor + Source + Chat)
- Create `usePanelLayout` hook for three panels with two splitters
- Add panel visibility toggles to toolbar
- Wire `onInsertToEditor` callback directly to editor ref
- Wire `editorSelection` from editor state to SprkChat props
- SSE streaming writes to editor via direct ref
- **Parallel operation**: both new (embedded) and legacy (side pane) approaches work simultaneously for A/B validation

### Phase 2: Remove Cross-Pane Infrastructure

- Remove DocumentStreamBridge, useSelectionBroadcast
- Remove SprkChat side pane launch code from App.tsx
- Remove SprkChatPane Code Page entirely
- Remove openSprkChatPane launcher, SidePaneManager
- Remove global ribbon button XML, LegalWorkspace script injection
- Remove web resources from Dataverse solution
- Deprecate SprkChatBridge in shared library
- Remove contextService.ts
- Consolidate duplicate auth: single MSAL init

### Phase 3: Unified Auth + Context Resolution

- Single `initAuth()` in AnalysisWorkspace entry point
- Single `useAnalysisLoader()` provides context to both editor and chat
- Single BFF base URL resolution
- Remove duplicate context mapping calls
- Chat playbook resolved from same analysis context via AnalysisAiContext
- Verify: token acquired once, context API called once

### Phase 4: Layout Polish + Persistence

- Panel minimum widths enforced (Editor 300px, Source 200px, Chat 280px)
- Keyboard resize: Arrow keys on focused splitter
- Double-click splitter to reset default ratios
- Panel visibility keyboard shortcuts
- Persist panel sizes and visibility to sessionStorage
- Default Chat panel visible on M365 Copilot handoff (URL contains `analysisId`)
- Responsive: narrow viewport (< 800px) → Chat as bottom sheet or overlay
- Smooth collapse/expand animations

### Phase 5: Deployment + Cleanup

- Update AnalysisWorkspace Vite config for combined bundle
- Verify bundle size delta is acceptable
- Update deployment scripts to remove deprecated web resources
- Update `code-page-deploy` skill if needed
- Integration test: full Analysis Workspace flow with embedded chat
- Update project documentation and CLAUDE.md references

## Dependencies

### Prerequisites

- All current SprkChat projects COMPLETE and merged to master:
  - Context Awareness
  - Workspace Companion
  - Platform Enhancement R2

### Existing Infrastructure (Reused Unchanged)

| Component | Location | Notes |
|---|---|---|
| SprkChat component suite | `@spaarke/ui-components/components/SprkChat/` | 17 components, 10 hooks — mounted via ChatPanel wrapper |
| InlineAiToolbar | `@spaarke/ui-components/components/InlineAiToolbar/` | Rewire from BroadcastChannel to context callback |
| SlashCommandMenu | `@spaarke/ui-components/components/SlashCommandMenu/` | Works unchanged |
| RichTextEditor | `@spaarke/ui-components/components/RichTextEditor/` | Already in AnalysisWorkspace; unchanged |
| PanelSplitter | `AnalysisWorkspace/components/PanelSplitter.tsx` | Extend for three-panel |
| BFF Chat endpoints | `Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | Unchanged |
| BFF Analysis context | `Sprk.Bff.Api/Api/Ai/AnalysisChatContextEndpoints.cs` | Unchanged |
| All playbook infrastructure | BFF services | Unchanged |

### What This Enables

After completion, `ai-sprk-chat-extensibility-r1` builds on the unified architecture:
- Slash commands operate within same React tree as editor
- Compound actions directly read/write editor content
- Smart routing has direct access to analysis context (no serialization)
- Plan preview can show editor diffs inline
- Email drafting can reference analysis content directly

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| M365 Copilot impact | Does M365 integration change this design? | No — M365 reinforces SprkChat as Analysis Workspace-only. Handoff from Copilot is simpler with unified page. | Added FR-13: Chat panel defaults visible on handoff URL |

## Assumptions

- **Bundle size**: SprkChat shared library components are tree-shaken; embedding in AnalysisWorkspace adds minimal bundle size. Will measure and lazy-load if needed.
- **Render performance**: React.memo on panels + editor ref for streaming bypasses React re-render cycle. No jank expected during SSE streaming.
- **No other SprkChatPane consumers**: SprkChatPane Code Page is only used by Analysis Workspace. Verified — safe to remove.
- **Session continuity**: Chat sessions stored server-side (BFF). Session ID persisted in sessionStorage. Migration preserves session key format.

## Unresolved Questions

- [ ] **PanelSplitter extension vs replacement** — extend existing `usePanelResize` for three panels, or create new `usePanelLayout`? Depends on current implementation complexity. — Blocks: Phase 1 layout work
- [ ] **Responsive breakpoint behavior** — on narrow viewports (< 800px), should Chat become a bottom sheet, overlay, or simply hidden? — Blocks: Phase 4 polish

---

*AI-optimized specification. Original: design.md*
