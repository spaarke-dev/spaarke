# Task Index — Spaarke AI Platform Unification R3

> **Last Updated**: 2026-05-20
> **Total Tasks**: 36 (1 spike + 4 foundations + 7 Assistant + 6 Workspace + 6 Context + 2 backend conditional + 4 verify + 5 deploy/smoke + 1 wrap-up)
> **Project Status**: Not started
> **Status Legend**: 🔲 not-started · 🔄 in-progress · 🚫 blocked · ✅ completed · ⏭️ skipped

---

## Quick Status

| Phase | Tasks | Status |
|---|---|---|
| Phase 0 (Spike) | 1 (001) | ✅ |
| Phase A (Foundations) | 4 (010-013) | ✅ |
| Phase B (Assistant) | 7 (020-026) | 🔲 |
| Phase C (Workspace) | 6 (030-035) | 🔲 |
| Phase D (Context) | 6 (040-045) | 🔲 |
| Phase E (Backend, conditional) | 2 (050, 051) | ✅ — Phase E COMPLETE (per spike 001 decision; memo: notes/spikes/001-fr07-attachments-payload.md) |
| Phase F (Verification) | 4 (060-063) | 🔲 |
| Phase G (Deploy + Smoke) | 5 (070-074) | 🔲 |
| Phase H (Wrap-up) | 1 (090) | 🔲 |

---

## Full Task Listing

| ID | Title | Phase | Status | Dependencies | Blocks | Parallel | Rigor | Est. h |
|----|-------|-------|--------|--------------|--------|----------|-------|--------|
| 001 | Spike: FR-07 attachments payload verification | 0 (Spike) | ✅ | none | 010-013, 026, 050, 051 | — (serial) | STANDARD | 1 |
| 010 | Create `<PaneHeader>` shared component | A (Foundations) | ✅ | 001 | 021, 022, 030, 032, 040 | A | FULL | 2 |
| 011 | Configure `MAX_WORKSPACE_TABS = 8` + FIFO | A (Foundations) | ✅ | 001 | 032 | A | FULL | 2 |
| 012 | Lift `ActionCard` to `@spaarke/ui-components` (or verify shared) | A (Foundations) | ✅ | 001 | 041 | A | FULL | 3 |
| 013 | Error-only telemetry helpers | A (Foundations) | ✅ | 001 | 022, 035 | A | STANDARD | 2 |
| 020 | WelcomePanel chrome trim | B (Assistant) | ✅ | 001, 010-013 | — | B | FULL | 2 |
| 021 | ConversationPane → PaneHeader | B (Assistant) | ✅ | 010 | 022 | B (serial w/ 022 on ConversationPane.tsx) | FULL | 2 |
| 022 | HistoryOverlay component + wiring | B (Assistant) | ✅ | 010, 021 | — | B (serial w/ 021) | FULL | 3 |
| 023 | SprkChatInput editable on cold load | B (Assistant) | ✅ | 010 | 025, 026 | B (serial w/ 025 on SprkChat.tsx) | FULL | 2 |
| 024 | `useChatFileAttachment` hook + lazy extraction | B (Assistant) | ✅ | 010 | 025, 026 | B | FULL | 4 |
| 025 | SprkChat toolbar restructure (+, remove Word) | B (Assistant) | ✅ | 023, 024 | 026 | B (serial w/ 023) | FULL | 3 |
| 026 | Wire attachments into chat send payload | B (Assistant) | ✅ | 001, 024, 025 | — | — (serial — gated by spike) | FULL | 3 |
| 030 | WorkspacePane → PaneHeader + embed LegalWorkspace | C (Workspace) | ✅ | 010 | 031, 032, 034 | C (serial on WorkspacePane.tsx) | FULL | 4 |
| 031 | Delete `WorkspaceLandingWidget.tsx` | C (Workspace) | ✅ | 030 | — | C (serial w/ 030) | STANDARD | 1 |
| 032 | `WorkspacePaneMenu` Dropdown component | C (Workspace) | ✅ | 010, 011, 030 | — | C (serial on WorkspacePane.tsx) | FULL | 4 |
| 033 | `WorkspaceLayoutWizard.templateFilter` prop | C (Workspace) | ✅ | 001 | — | C | STANDARD | 2 |
| 034 | Daily Briefing section + `useDailyBriefing` hook | C (Workspace) | ✅ | 030 | 035 | C | FULL | 4 |
| 035 | Daily Briefing 429 + empty state | C (Workspace) | ✅ | 013, 034 | — | C (depends on 034) | FULL | 3 |
| 040 | ContextPaneController → PaneHeader + "Get Started" label | D (Context) | ✅ | 010 | 042 | D (serial on ContextPaneController.tsx) | FULL | 3 |
| 041 | `GetStartedCardsWidget` (7 cards, 2-col grid) | D (Context) | ✅ | 012 | 042 | D | FULL | 3 |
| 042 | Register widget + welcome-stage swap | D (Context) | ✅ | 040, 041 | — | D (serial on ContextPaneController.tsx) | FULL | 3 |
| 043 | Wizard widget wrappers (CreateProject, FindSimilar) | D (Context) | ✅ | 001 | — | D | FULL | 2 |
| 044 | Analysis Builder intents (email-compose, meeting-schedule) | D (Context) | ✅ | 001 | — | D | STANDARD | 2 |
| 045 | `AssignWorkWizardLauncher` (Xrm.Navigation) | D (Context) | ✅ | 001 | — | D | FULL | 2 |
| 050 | Extend ChatEndpoints with attachments[] (CONDITIONAL) | E (Backend) | ✅ | 001 | 051, 026 | E | FULL | 3 |
| 051 | BFF unit tests for attachments[] payload (CONDITIONAL) | E (Backend) | ✅ | 050 | — | E (serial w/ 050) | STANDARD | 2 |
| 060 | Auth audit (no token snapshots, all via `authenticatedFetch`) | F (Verification) | ✅ | All B/C/D | 070 | F | STANDARD | 2 |
| 061 | Bundle-size verification (<250 KB gzip delta vs R2) | F (Verification) | ✅ | All B/C/D | 070 | F | STANDARD | 2 |
| 062 | Dark-mode token audit (no hex/rgba in new code) | F (Verification) | ✅ | All B/C/D | 070 | F | STANDARD | 2 |
| 063 | Backwards-compat verification (standalone LW + persistence) | F (Verification) | ✅ | C tasks | 070 | F | STANDARD | 2 |
| 070 | Deploy SpaarkeAi via `Deploy-SpaarkeAi.ps1` | G (Deploy) | 🔲 | 060-063 | 071-074 | — | FULL | 1 |
| 071 | UI smoke — Assistant pane (FR-02..FR-09) | G (Smoke) | 🔲 | 070 | 090 | G | STANDARD | 2 |
| 072 | UI smoke — Workspace pane (FR-10..FR-16) | G (Smoke) | 🔲 | 070 | 090 | G | STANDARD | 2 |
| 073 | UI smoke — Context pane (FR-17..FR-22) | G (Smoke) | 🔲 | 070 | 090 | G | STANDARD | 2 |
| 074 | NFR verification (Lighthouse + History overlay timings) | G (Smoke) | 🔲 | 070 | 090 | G | STANDARD | 2 |
| 090 | Project wrap-up (code-review + adr-check + repo-cleanup + lessons-learned) | H (Wrap-up) | 🔲 | 070-074 (+050, 051 if Phase E) | — | — | STANDARD | 3 |

**Total estimated effort**: ~75 hours (excludes Phase E if skipped). With Phase E: ~80 hours.

---

## Parallel Execution Plan (Waves)

Tasks in the same wave can run simultaneously once prerequisites are met. Each task agent invokes `task-execute` independently. **Hard cap: 6 concurrent agents per wave.**

### Wave 0 — Spike (serial, blocking)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 0 | 001 | none | `notes/spikes/*` only | n/a (single task) |

### Wave 1 — Foundations (4-way parallel)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 1 | 010, 011, 012, 013 | 001 ✅ | Separate modules (Spaarke.UI.Components, WorkspaceTabManager, ActionCard lift, telemetry helpers) | ✅ Yes |

### Wave 2 — Independent Phase B/C/D tasks (≤6 parallel)

After Wave 1 completes, dispatch first-tier independent tasks from B/C/D in parallel (up to 6):

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 2a | 020, 021, 023, 024 | 010-013 ✅ | WelcomePanel, ConversationPane, SprkChat (023 reserves SprkChat lock), `useChatFileAttachment` hook (net-new) | ✅ Yes (23 + 24 don't overlap; 25 waits) |
| 2b | 030, 033, 034, 040, 041, 043 | 010-013 ✅ | WorkspacePane (030), LayoutWizard, DailyBriefing (net-new), ContextPaneController (040), GetStartedCardsWidget (net-new), wizard wrappers (net-new) | ✅ Yes (no file overlaps; max 6 concurrent) |
| 2c | 044, 045 | 010-013 ✅ | Analysis Builder intents, AssignWork launcher (both net-new) | ✅ Yes |

> Note: Waves 2a + 2b + 2c may need to be split further if you want strict 6-agent cap respected per wave. Sequence option: dispatch 2a (4 agents), then 2b (6 agents), then 2c (2 agents) on success.

### Wave 3 — Phase B/C/D dependent tasks (serial within phase, parallel across phases)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 3a | 022 | 021 ✅ | ConversationPane.tsx + new HistoryOverlay.tsx | serial w/ 021 |
| 3b | 025 | 023, 024 ✅ | SprkChat.tsx (serial w/ 023) | serial w/ 023 |
| 3c | 031, 032 | 030 ✅ | WorkspacePane.tsx (serial w/ 030) | serial w/ 030 |
| 3d | 035 | 013, 034 ✅ | DailyBriefingSection.tsx (extends 034) | serial w/ 034 |
| 3e | 042 | 040, 041 ✅ | ContextPaneController.tsx + ContextWidgetRegistry.ts | serial w/ 040 |

Across phases (3a + 3b + 3c + 3d + 3e), these can run in parallel since they touch different files (still respecting 6-agent cap).

### Wave 4 — Phase B final + Phase E (conditional)

| Wave | Tasks | Prerequisite | Notes |
|------|-------|--------------|-------|
| 4a | 026 | 001, 024, 025 ✅ | Frontend attachment-payload wiring — proceeds whether Phase E is in or out |
| 4b | 050 (if needed) | 001 (verdict: extension-needed) | Backend payload extension |
| 4c | 051 (if needed) | 050 ✅ | BFF unit tests |

If spike says NO extension needed: mark 050 + 051 as ⏭️ skipped; proceed directly to Wave 5.

### Wave 5 — Verification (4-way parallel, read-only audits)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 5 | 060, 061, 062, 063 | All Phases B/C/D (+E if executed) ✅ | Audit reports only (notes/*) | ✅ Yes |

### Wave 6 — Deploy (serial)

| Wave | Tasks | Prerequisite | Notes |
|------|-------|--------------|-------|
| 6 | 070 | 060-063 ✅ | Production deploy via `Deploy-SpaarkeAi.ps1` |

### Wave 7 — Smoke (4-way parallel, independent tests)

| Wave | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|------|-------|--------------|---------------|---------------------|
| 7 | 071, 072, 073, 074 | 070 ✅ | Smoke reports only (notes/*) | ✅ Yes |

### Wave 8 — Wrap-up (serial, final)

| Wave | Tasks | Prerequisite | Notes |
|------|-------|--------------|-------|
| 8 | 090 | 071-074 ✅ (+ 050, 051 if Phase E) | Quality gates + repo-cleanup + lessons-learned |

---

## How to Execute Parallel Waves

1. Confirm all prerequisites are ✅ in this index.
2. Invoke `task-execute` skill via the Skill tool with **multiple tool calls in ONE message** (one per task in the wave).
3. Each agent runs task-execute for its task with full context loading.
4. After all agents in the wave complete, run build verification (per [`CLAUDE.md` build-verification-between-waves](../../CLAUDE.md)):
   - If any `.cs` modified → `dotnet build src/server/api/Sprk.Bff.Api/`
   - If any `.ts`/`.tsx` modified → `npm run build` in the relevant package
5. Update this index (🔲 → ✅ for each completed task) before proceeding to next wave.
6. If any task fails → mark 🔄, do NOT skip wave verification, decide retry vs report.

**Respect**:
- 6-agent cap per wave (hard limit — API overload guard).
- Sub-agent permission boundary: tasks with `<parallel-safe>false</parallel-safe>` due to `.claude/` writes MUST run in main session sequentially (none here, but rule still applies).

---

## Critical Path

`001 → (010 ∨ 011 ∨ 012 ∨ 013) → (Phase B/C/D longest chain: 030 → 032 → ...) → 060-063 (verify) → 070 (deploy) → 071-074 (smoke) → 090 (wrap-up)`

Longest chain (Phase C): `001 → 010 → 030 → 032 → 060-063 → 070 → 071-074 → 090` ≈ 11 tasks deep. With parallel execution of other phases, total wall-clock is bounded by this path.

---

## High-Risk Items

| Risk ID | Task | Risk | Mitigation |
|---|---|---|---|
| R-1 | 001 → 026, 050, 051 | Spike result determines whether Phase E exists | Phase E tasks are gated; 026 has conditional behavior path |
| R-3 | 012 → 041 | ActionCard lift may surface coupling issues | Decision documented in `notes/drafts/012-actioncard-decision.md`; alternate path is direct import from LegalWorkspace |
| R-4 | 034 | Daily Briefing endpoint may lack per-user caching | `useDailyBriefing` adds frontend TTL cache (~5 min per ADR-014) |
| R-5 | 063 → potentially follow-up | NFR-09 persistence may need `SessionPersistenceService` extension | Verification surfaces gap; fix task opened mid-Phase F if needed |
| R-6 | 024, 061 | PDF.js + mammoth bundle size may exceed budget | Lazy-load both; verify in task 061 |

---

## File Modification Map (for parallel-safety analysis)

| File | Touched by tasks |
|---|---|
| `src/solutions/SpaarkeAi/src/components/WelcomePanel.tsx` | 020 |
| `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` | 021, 022 (serial) |
| `src/solutions/SpaarkeAi/src/components/conversation/HistoryOverlay.tsx` (new) | 022 |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx` | 023, 025, 026 (serial) |
| `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useChatFileAttachment.ts` (new) | 024 |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePane.tsx` | 030, 031, 032 (serial) |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceTabManager.ts` | 011 |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx` (new) | 032 |
| `src/solutions/SpaarkeAi/src/components/workspace/WorkspaceLandingWidget.tsx` (delete) | 031 |
| `src/solutions/WorkspaceLayoutWizard/src/App.tsx` or `steps/TemplateStep.tsx` | 033 |
| `src/solutions/LegalWorkspace/src/sections/dailyBriefing/*` (new) | 034, 035 (serial) |
| `src/solutions/LegalWorkspace/src/sectionRegistry.ts` | 034 |
| `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` | 040, 042 (serial) |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/context/GetStartedCardsWidget.tsx` (new) | 041 |
| `src/client/shared/Spaarke.AI.Widgets/src/registry/ContextWidgetRegistry.ts` | 042 |
| `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/*` (new wizard wrappers) | 043, 044, 045 |
| `src/client/shared/Spaarke.UI.Components/src/components/PaneHeader/*` (new) | 010 |
| `src/client/shared/Spaarke.UI.Components/src/components/ActionCard/*` (new or verified) | 012 |
| `src/solutions/SpaarkeAi/src/telemetry/errorTelemetry.ts` (new) | 013 |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | 050 (conditional) |
| Test files for above | 011, 010, 013, 024, 033, 050 → 051 |

**Conflict-free parallelization rule**: any two tasks listed against the same file MUST be serialized. This index encodes that via `parallel-safe: false` in the relevant POML files.

---

*This index is the single source of truth for task status. Update 🔲 → ✅ on each task completion. The `task-execute` skill updates this file automatically as part of its protocol.*
