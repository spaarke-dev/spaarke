# Task Index — Analysis Workspace + SprkChat Integration

> **Project**: ai-analysis-workspace-sprkchat-integration
> **Total Tasks**: 30
> **Last Updated**: 2026-03-26

---

## Task Registry

### Phase 1: AnalysisAiContext + ChatPanel Integration

| # | Title | Status | Tags | Group | Dependencies |
|---|-------|--------|------|-------|-------------|
| 001 | Create AnalysisAiContext and AnalysisAiProvider | ✅ | code-page, react, context | — | none |
| 002 | Create usePanelLayout hook | ✅ | code-page, react, hooks | A | 001 |
| 003 | Create ChatPanel wrapper component | ✅ | code-page, react, sprkchat | A | 001 |
| 004 | Restructure App.tsx to three-panel layout | ✅ | code-page, react, layout | — | 001, 002, 003 |
| 005 | Wire onInsertToEditor callback via context | ✅ | code-page, react, editor | B | 004 |
| 006 | Wire editorSelection to SprkChat props | ✅ | code-page, react, editor | B | 004 |
| 007 | Wire SSE streaming to editor via direct ref | ✅ | code-page, react, streaming | — | 005 |
| 008 | Add panel visibility toggles to toolbar | ✅ | code-page, react, fluent-ui | C | 004 |
| 009 | Phase 1 integration verification | ✅ | testing | — | all Phase 1 |

### Phase 2: Remove Cross-Pane Infrastructure

| # | Title | Status | Tags | Group | Dependencies |
|---|-------|--------|------|-------|-------------|
| 010 | Remove DocumentStreamBridge and useSelectionBroadcast | 🔲 | cleanup | D | 009 |
| 011 | Remove side pane launch code from App.tsx | 🔲 | cleanup | D | 009 |
| 012 | Remove SprkChatPane Code Page entirely | ✅ | cleanup | E | 009 |
| 013 | Remove openSprkChatPane launcher and SidePaneManager | ✅ | cleanup | E | 009 |
| 014 | Remove ribbon button XML and LegalWorkspace injection | ✅ | dataverse | F | 009 |
| 015 | Remove web resources from Dataverse solution | ✅ | dataverse | F | 009 |
| 016 | Deprecate SprkChatBridge in shared library | ✅ | shared-library | G | 009 |
| 017 | Remove contextService.ts and related tests | ✅ | cleanup | G | 012 |
| 018 | Phase 2 verification — grep BroadcastChannel = 0 | 🔲 | testing | — | all Phase 2 |

### Phase 3: Unified Auth + Context Resolution

| # | Title | Status | Tags | Group | Dependencies |
|---|-------|--------|------|-------|-------------|
| 020 | Consolidate to single MSAL initialization | 🔲 | auth | — | 018 |
| 021 | Unify useAnalysisLoader for editor and chat | 🔲 | react, hooks | H | 020 |
| 022 | Single BFF base URL and authenticated fetch | 🔲 | auth, api | H | 020 |
| 023 | Wire chat playbook from AnalysisAiContext | 🔲 | react, sprkchat | — | 021 |
| 024 | Auth verification — single token acquisition | 🔲 | testing | — | all Phase 3 |

### Phase 4: Layout Polish + Persistence

| # | Title | Status | Tags | Group | Dependencies |
|---|-------|--------|------|-------|-------------|
| 030 | Enforce panel minimum widths | 🔲 | layout | I | 024 |
| 031 | Keyboard resize (Arrow keys) | 🔲 | a11y | I | 024 |
| 032 | Double-click splitter to reset defaults | 🔲 | layout | J | 024 |
| 033 | Panel visibility keyboard shortcuts | 🔲 | a11y | J | 024 |
| 034 | Persist panel sizes and visibility | 🔲 | layout | — | 030 |
| 035 | M365 Copilot handoff default Chat visible | 🔲 | layout | K | 024 |
| 036 | Smooth collapse/expand animations | 🔲 | fluent-ui | K | 024 |
| 037 | Phase 4 layout testing and polish | 🔲 | testing | — | all Phase 4 |

### Phase 5: Deployment + Cleanup

| # | Title | Status | Tags | Group | Dependencies |
|---|-------|--------|------|-------|-------------|
| 040 | Update Vite config for combined bundle | 🔲 | build | — | 037 |
| 041 | Verify bundle size delta | 🔲 | performance | — | 040 |
| 042 | Update deployment scripts | 🔲 | deploy | L | 041 |
| 043 | Update test mocks and remove obsolete tests | 🔲 | testing | L | 041 |
| 044 | Full integration test (all 11 criteria) | 🔲 | e2e | — | 042, 043 |
| 045 | Update documentation | 🔲 | docs | — | 044 |
| 090 | Project wrap-up | 🔲 | cleanup | — | 045 |

---

## Parallel Execution Groups

| Group | Tasks | Phase | Prerequisite | Notes |
|-------|-------|-------|--------------|-------|
| A | 002, 003 | 1 | 001 complete | New hook + component, no shared files |
| B | 005, 006 | 1 | 004 complete | Independent editor integration paths |
| C | 008 | 1 | 004 complete | Independent toolbar work |
| D | 010, 011 | 2 | 009 complete | AnalysisWorkspace cleanup, different files |
| E | 012, 013 | 2 | 009 complete | SprkChatPane/SidePaneManager removal |
| F | 014, 015 | 2 | 009 complete | Dataverse artifact removal |
| G | 016, 017 | 2 | 009 complete | Shared library + SprkChatPane services |
| H | 021, 022 | 3 | 020 complete | Independent consolidation concerns |
| I | 030, 031 | 4 | 024 complete | Layout constraints + keyboard |
| J | 032, 033 | 4 | 024 complete | UX enhancements |
| K | 035, 036 | 4 | 024 complete | Visual polish |
| L | 042, 043 | 5 | 041 complete | Deployment + test cleanup |

---

## Critical Path

```
001 → 002+003 → 004 → 005+006+008 → 007 → 009 → 010-017 → 018 → 020 → 021+022 → 023 → 024 → 030-036 → 037 → 040 → 041 → 042+043 → 044 → 045 → 090
```

**Longest chain**: 001 → 004 → 005 → 007 → 009 → 018 → 020 → 023 → 024 → 037 → 040 → 041 → 044 → 045 → 090 (15 serial steps)

**Parallelism reduces effective duration**: 12 parallel groups allow concurrent execution, reducing wall-clock time significantly.

---

## Progress Summary

| Phase | Total | Complete | Remaining |
|-------|-------|----------|-----------|
| Phase 1 | 9 | 0 | 9 |
| Phase 2 | 9 | 0 | 9 |
| Phase 3 | 5 | 0 | 5 |
| Phase 4 | 8 | 0 | 8 |
| Phase 5 | 7 | 0 | 7 |
| **Total** | **30** | **0** | **30** |

---

*Generated by Claude Code project-pipeline*
