# Implementation Plan — Analysis Workspace + SprkChat Integration

> **Created**: 2026-03-26
> **Source**: [spec.md](spec.md)

---

## Executive Summary

**Purpose**: Merge AnalysisWorkspace and SprkChatPane into a single unified Code Page, replacing BroadcastChannel cross-pane communication with React context + callback props.

**Scope**: AnalysisWorkspace restructuring (3-panel layout, shared context), SprkChatPane removal, cross-pane infrastructure cleanup, unified auth/context resolution, layout polish.

**Estimated effort**: 8-12 days (5 phases, structured for parallel execution)

---

## Architecture Context

### Key Constraints
- ADR-006: Code Page for standalone dialogs — AnalysisWorkspace is a Code Page
- ADR-012: Shared component library — SprkChat stays in `@spaarke/ui-components`; callback-based props
- ADR-021: Fluent UI v9 exclusively — `makeStyles`, design tokens, dark mode
- ADR-022/026: React 19 for Code Pages — `createRoot`, bundled React, Vite + singlefile

### Technology Stack
- React 19 (bundled in Code Page)
- Fluent UI v9 (`@fluentui/react-components`)
- Vite + `vite-plugin-singlefile`
- MSAL for authentication
- Lexical editor (RichTextEditor from shared library)

### Discovered Resources

| Type | Resource | Purpose |
|------|----------|---------|
| ADR | ADR-006 | Code Page as default UI surface |
| ADR | ADR-012 | Shared component library (callback-based API) |
| ADR | ADR-021 | Fluent UI v9 design system |
| ADR | ADR-022 | React version by surface (PCF=16, CodePage=19) |
| ADR | ADR-026 | Vite + singlefile Code Page standard |
| Skill | code-page-deploy | Deploy Code Page web resources |
| Skill | code-review | Quality gate |
| Skill | adr-check | Architecture compliance |

---

## Implementation Approach

### Phase Structure

The project is organized into 5 phases. Phases 1-3 are sequential (each builds on the previous). Phase 4 tasks can partially overlap with Phase 3 completion. Phase 5 follows all implementation work.

### Critical Path

```
Phase 1 (Context + Integration) → Phase 2 (Remove Infrastructure) → Phase 3 (Unified Auth) → Phase 5 (Deploy)
                                                                     Phase 4 (Polish) ─────────────────────┘
```

### Parallel Execution Strategy

Within each phase, tasks are grouped for concurrent execution by independent Claude Code task agents. Each parallel group targets different files/modules to avoid conflicts.

---

## WBS (Work Breakdown Structure)

### Phase 1: AnalysisAiContext + ChatPanel Integration
**Objective**: Mount SprkChat inside AnalysisWorkspace via shared React context. Both approaches (embedded + legacy side pane) work simultaneously.

| # | Task | Tags | Estimate | Parallel Group |
|---|------|------|----------|----------------|
| 001 | Create AnalysisAiContext and AnalysisAiProvider | code-page, react, context | 2h | — (serial, foundation) |
| 002 | Create usePanelLayout hook for three-panel management | code-page, react, hooks | 2h | A |
| 003 | Create ChatPanel wrapper component | code-page, react, sprkchat | 2h | A |
| 004 | Restructure App.tsx to three-panel layout with AnalysisAiProvider | code-page, react, layout | 3h | — (serial, depends on 001-003) |
| 005 | Wire onInsertToEditor callback via context | code-page, react, editor | 2h | B |
| 006 | Wire editorSelection from editor state to SprkChat props | code-page, react, editor | 2h | B |
| 007 | Wire SSE streaming to editor via direct ref | code-page, react, streaming | 2h | — (depends on 005) |
| 008 | Add panel visibility toggles to toolbar | code-page, react, fluent-ui | 2h | C |
| 009 | Phase 1 integration verification | testing, code-page | 2h | — (serial, depends on all) |

**Parallel Groups**:
- Group A (002, 003): Independent new components — no shared files
- Group B (005, 006): Independent editor integration hooks — different callback paths
- Group C (008): Independent toolbar work

**Deliverables**: AnalysisAiContext, ChatPanel, three-panel layout, editor ↔ chat integration working

---

### Phase 2: Remove Cross-Pane Infrastructure
**Objective**: Eliminate all BroadcastChannel/side pane code. Clean removal of SprkChatPane.

| # | Task | Tags | Estimate | Parallel Group |
|---|------|------|----------|----------------|
| 010 | Remove DocumentStreamBridge and useSelectionBroadcast | code-page, cleanup | 1h | D |
| 011 | Remove SprkChat side pane launch code from App.tsx | code-page, cleanup | 1h | D |
| 012 | Remove SprkChatPane Code Page directory entirely | code-page, cleanup | 1h | E |
| 013 | Remove openSprkChatPane launcher and SidePaneManager | code-page, cleanup | 1h | E |
| 014 | Remove ribbon button XML and LegalWorkspace script injection | dataverse, cleanup | 1h | F |
| 015 | Remove web resources from Dataverse solution definition | dataverse, cleanup | 1h | F |
| 016 | Deprecate SprkChatBridge in shared library | shared-library, cleanup | 1h | G |
| 017 | Remove contextService.ts and related test files | code-page, cleanup | 1h | G |
| 018 | Phase 2 verification — grep for BroadcastChannel returns zero | testing, cleanup | 1h | — (serial) |

**Parallel Groups**:
- Group D (010, 011): AnalysisWorkspace internal cleanup — different files
- Group E (012, 013): SprkChatPane/SidePaneManager removal — different directories
- Group F (014, 015): Dataverse artifact removal — different solution files
- Group G (016, 017): Shared library deprecation + SprkChatPane service cleanup

**Deliverables**: All cross-pane infrastructure removed, SprkChatPane deleted, SprkChatBridge deprecated

---

### Phase 3: Unified Auth + Context Resolution
**Objective**: Single auth flow and single context resolution for the entire page.

| # | Task | Tags | Estimate | Parallel Group |
|---|------|------|----------|----------------|
| 020 | Consolidate to single MSAL initialization | code-page, auth | 2h | — (serial) |
| 021 | Unify useAnalysisLoader to serve both editor and chat | code-page, react, hooks | 2h | H |
| 022 | Single BFF base URL resolution and authenticated fetch | code-page, auth, api | 2h | H |
| 023 | Wire chat playbook resolution from AnalysisAiContext | code-page, react, sprkchat | 2h | — (depends on 021) |
| 024 | Auth verification — single token acquisition confirmed | testing, auth | 1h | — (serial) |

**Parallel Groups**:
- Group H (021, 022): Independent hook/service consolidation — different concerns

**Deliverables**: Single MSAL init, single context resolution, single BFF base URL

---

### Phase 4: Layout Polish + Persistence
**Objective**: Polished three-panel UX with persistence and accessibility.

| # | Task | Tags | Estimate | Parallel Group |
|---|------|------|----------|----------------|
| 030 | Enforce panel minimum widths (Editor 300px, Source 200px, Chat 280px) | code-page, layout | 1h | I |
| 031 | Add keyboard resize (Arrow keys on focused splitter) | code-page, a11y | 2h | I |
| 032 | Add double-click splitter to reset default ratios | code-page, layout | 1h | J |
| 033 | Add panel visibility keyboard shortcuts | code-page, a11y | 1h | J |
| 034 | Persist panel sizes and visibility to sessionStorage | code-page, layout | 2h | — (depends on 030) |
| 035 | Default Chat panel visible on M365 Copilot handoff URL | code-page, layout | 1h | K |
| 036 | Smooth collapse/expand animations | code-page, fluent-ui | 2h | K |
| 037 | Phase 4 layout testing and polish | testing, code-page | 2h | — (serial) |

**Parallel Groups**:
- Group I (030, 031): Independent layout constraints — different event handlers
- Group J (032, 033): Independent UX enhancements — different interaction types
- Group K (035, 036): Independent visual polish — different features

**Deliverables**: Min widths, keyboard resize, persistence, handoff support, animations

---

### Phase 5: Deployment + Cleanup
**Objective**: Clean deployment with removed artifacts, verified bundle size and performance.

| # | Task | Tags | Estimate | Parallel Group |
|---|------|------|----------|----------------|
| 040 | Update AnalysisWorkspace Vite config for combined bundle | code-page, build | 2h | — (serial) |
| 041 | Verify bundle size delta (measure before/after) | testing, performance | 1h | — (depends on 040) |
| 042 | Update deployment scripts for removed web resources | deploy, scripts | 2h | L |
| 043 | Update test mocks and remove obsolete test files | testing, cleanup | 2h | L |
| 044 | Full integration test — Analysis Workspace with embedded chat | testing, e2e | 3h | — (serial) |
| 045 | Update project documentation and CLAUDE.md references | docs | 1h | — (serial) |
| 090 | Project wrap-up | docs, cleanup | 1h | — (final) |

**Parallel Groups**:
- Group L (042, 043): Independent deployment/test cleanup — different file types

**Deliverables**: Updated build, verified bundle, clean deployment, documentation updated

---

## Parallel Execution Groups (Summary)

| Group | Tasks | Phase | Prerequisite | Notes |
|-------|-------|-------|--------------|-------|
| A | 002, 003 | 1 | 001 complete | New hook + component, no shared files |
| B | 005, 006 | 1 | 004 complete | Independent editor integration paths |
| C | 008 | 1 | 004 complete | Independent toolbar work |
| D | 010, 011 | 2 | Phase 1 complete | AnalysisWorkspace cleanup, different files |
| E | 012, 013 | 2 | Phase 1 complete | SprkChatPane/SidePaneManager removal |
| F | 014, 015 | 2 | Phase 1 complete | Dataverse artifact removal |
| G | 016, 017 | 2 | Phase 1 complete | Shared library + SprkChatPane services |
| H | 021, 022 | 3 | 020 complete | Independent consolidation concerns |
| I | 030, 031 | 4 | Phase 3 complete | Layout constraints + keyboard |
| J | 032, 033 | 4 | Phase 3 complete | UX enhancements |
| K | 035, 036 | 4 | Phase 3 complete | Visual polish |
| L | 042, 043 | 5 | 041 complete | Deployment + test cleanup |

---

## Dependencies

### External
- SprkChat shared library (`@spaarke/ui-components`) — used unchanged
- BFF API chat endpoints — used unchanged
- Dataverse solution tooling (PAC CLI)

### Internal
- AnalysisWorkspace Code Page (primary target)
- SprkChatPane Code Page (to be removed)
- LegalWorkspace solution (script injection removal)

---

## Testing Strategy

### Unit Testing
- Test AnalysisAiContext provider in isolation
- Test usePanelLayout hook behavior
- Test ChatPanel wrapper prop mapping
- Test panel visibility toggle logic

### Integration Testing
- Full AnalysisWorkspace flow with embedded chat
- Insert-to-editor round-trip
- SSE streaming to editor via ref
- Panel resize and collapse behavior

### Regression Testing
- Verify no BroadcastChannel references remain
- Verify single token acquisition
- Verify existing chat functionality works (sessions, playbooks, streaming)

---

## Acceptance Criteria

1. Single Code Page — one iframe in Dataverse, no side pane
2. Zero BroadcastChannel — all communication via React context/props
3. Single auth — one MSAL init, one token acquisition
4. Insert to editor works — AI "Insert" button writes to Lexical editor
5. Inline toolbar round-trip works end-to-end
6. SSE streaming writes directly to editor via ref
7. Three-panel layout with draggable splitters and min widths
8. Panels collapsible and toggleable with persistence
9. SprkChat components remain in shared library with no workspace-specific deps
10. SprkChatPane web resource removed from solution
11. Page load performance comparable to current

---

## Risk Register

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Bundle size increase | Slower initial load | Low | SprkChat already in shared library (tree-shaken). Measure delta; lazy-load ChatPanel if needed. |
| Render performance (editor + chat in one tree) | Jank during streaming | Low | React.memo on panels; streaming via editor ref bypasses React re-render |
| Removing SprkChatPane breaks other consumers | Broken functionality | Very Low | No other consumers exist — verified in codebase |
| Three-panel layout cramped on small screens | Poor UX | Medium | Responsive: collapse panels on narrow viewports |
| Migration breaks existing chat sessions | Lost history | Very Low | Sessions stored server-side; session ID format preserved |

---

## Next Steps

1. Run `task-create` to generate POML task files from this plan
2. Create feature branch and initial commit
3. Begin task execution with Phase 1 (foundation)

---

*Generated by Claude Code project-pipeline*
