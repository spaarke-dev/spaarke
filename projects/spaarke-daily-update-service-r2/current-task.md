# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-18 (Option D mid-flight)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Option D — replace module-mutation slot from R2 task 002 with registry-as-composition factory |
| **Step** | 8 of 12 — design + Phase 7b POMLs done; implementing the 5-file refactor + 3 tests now |
| **Status** | in-progress |
| **Next Action** | Execute refactor on 6 source files per `notes/option-d-registry-as-composition.md` §3; add 3 unit tests; commit + push to PR #396 |
| **Rigor Level** | FULL — touches LegalWorkspaceApp + SpaarkeAi/main.tsx + WorkspaceGrid + sectionRegistry; high-impact architectural change |
| **Branch** | `work/spaarke-daily-update-service-r2` |
| **Active PR** | [#396](https://github.com/spaarke-dev/spaarke/pull/396) — auto-merge armed; will re-fire when CI re-runs after Option D push |

### Files Modified Already This Session (Option D)

- `projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md` — Created — design rationale + alternatives + cookbook + speculative-design discipline
- `projects/spaarke-daily-update-service-r2/tasks/070-pattern-doc-workspace-section-registry-composition.poml` — Created
- `projects/spaarke-daily-update-service-r2/tasks/071-update-architecture-docs-for-registry-composition.poml` — Created
- `projects/spaarke-daily-update-service-r2/tasks/072-adr-workspace-section-registry-composition.poml` — Created
- `projects/spaarke-daily-update-service-r2/current-task.md` — Modified (this file)

### Pending implementation changes (next 6 files + tests)

| File | Change |
|---|---|
| `src/solutions/LegalWorkspace/src/sectionRegistry.ts` | Add `createLegalWorkspaceSectionRegistry(options)` factory + `LegalWorkspaceSectionRegistryOptions` interface; `SECTION_REGISTRY = createLegalWorkspaceSectionRegistry()`; extract dev-guards into `runRegistryDevGuards(registry)` helper |
| `src/solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts` | REMOVE `_globalNotificationLoader`, `setLegalWorkspaceDailyBriefingNotificationLoader`, `lateBoundNotificationLoader`; `dailyBriefingRegistration = createLegalWorkspaceDailyBriefingRegistration()` (no loader) |
| `src/solutions/LegalWorkspace/src/index.ts` | REMOVE setter re-export; ADD factory + options + SECTION_REGISTRY + SectionRegistration type re-exports |
| `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx` | Add `sections?: readonly SectionRegistration[]` to `ILegalWorkspaceAppProps`; pass to `WorkspaceGrid` |
| `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx` | Accept `sections` prop (default → imported SECTION_REGISTRY); replace direct usage |
| `src/solutions/SpaarkeAi/src/main.tsx` | REMOVE setter import + call; build `customSections = createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext: loadSpaarkeAiNotificationContext } })`; define `SpaarkeAiWorkspaceRenderer = (props) => <LegalWorkspaceApp {...props} sections={customSections} />`; register via existing `setDefaultWorkspaceRenderer(SpaarkeAiWorkspaceRenderer)` |
| **Tests** | 3 new tests: no-options equivalence; loader threading; legacy setter API removed |

### Critical Context

PR #396 is OPEN with auto-merge armed. Option D is being ADDED to this PR so the band-aid module-mutation pattern from R2 task 002 (Wave 8) is REPLACED, not layered. After Option D commits land, CI re-runs and auto-merge fires.

R6 PR #395 is open in parallel with ZERO file-level overlap (verified via `git -C r6-worktree log --oneline origin/master..HEAD -- <files>`). Either PR can merge first; both will apply cleanly.

3 Phase 7b documentation alignment tasks (070/071/072) are CREATED but NOT EXECUTED in this PR — they're queued for post-merge execution so the docs review can be holistic and not block the implementation merge.

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | Option D (no POML — emerged from PR #396 architectural review) |
| **Design rationale** | `notes/option-d-registry-as-composition.md` (canonical spec for the refactor) |
| **Title** | Replace module-mutation slot from R2 task 002 with registry-as-composition factory |
| **Phase** | P1 (architectural follow-up to task 002) |
| **Status** | in-progress (design done; implementation next) |
| **Started** | 2026-06-18 (mid-session, after user code review feedback) |
| **Rigor Level** | FULL |

---

## Progress

### Completed Steps

- [x] **Step 1**: Diagnose the band-aid pattern (R2 task 002 / Wave 8 Option B module-mutation)
- [x] **Step 2**: Evaluate alternatives (A: replace registry, B: shipped band-aid, C: React Context, D: registry-as-composition factory)
- [x] **Step 3**: User authorization for Option D after rejecting C as too narrow for the SpaarkeAi-as-critical-core-surface vision
- [x] **Step 4**: R6 review (zero file-level overlap confirmed via git diff against branches)
- [x] **Step 5**: Author comprehensive design rationale in `notes/option-d-registry-as-composition.md`
- [x] **Step 6**: Create Phase 7b POML tasks 070/071/072 for documentation alignment follow-ups
- [x] **Step 7**: Update this `current-task.md` with full Option D context

### Current Step

**Step 8**: Implement the 6-file refactor + 3 unit tests

**What this step involves**:
- Refactor `src/solutions/LegalWorkspace/src/sectionRegistry.ts` to expose `createLegalWorkspaceSectionRegistry(options)` factory + `LegalWorkspaceSectionRegistryOptions` interface. Default `SECTION_REGISTRY` becomes `createLegalWorkspaceSectionRegistry()` (no options). Dev-mode duplicate/drift guards extracted into a reusable `runRegistryDevGuards(registry)` helper that runs for every registry built.
- In `dailyBriefing.registration.ts`: REMOVE `_globalNotificationLoader`, `setLegalWorkspaceDailyBriefingNotificationLoader`, `lateBoundNotificationLoader`. Default `dailyBriefingRegistration` const becomes `createLegalWorkspaceDailyBriefingRegistration()` (no loader → empty contract preserved for standalone consumers).
- In `LegalWorkspace/src/index.ts`: REMOVE `setLegalWorkspaceDailyBriefingNotificationLoader` re-export. ADD `createLegalWorkspaceSectionRegistry` + `LegalWorkspaceSectionRegistryOptions` + (re-export of) `SECTION_REGISTRY` re-exports.
- In `LegalWorkspaceApp.tsx`: ADD optional `sections?: readonly SectionRegistration[]` prop to `ILegalWorkspaceAppProps`. Pass through to `WorkspaceGrid`.
- In `WorkspaceGrid.tsx`: accept `sections` prop (defaults to imported `SECTION_REGISTRY`). Replace direct usage with the prop variable.
- In `SpaarkeAi/main.tsx`: REPLACE setter import + call with `createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext: loadSpaarkeAiNotificationContext } })`. Define `SpaarkeAiWorkspaceRenderer: WorkspaceRenderer = (props) => <LegalWorkspaceApp {...props} sections={customSections} />`. Register via existing `setDefaultWorkspaceRenderer(SpaarkeAiWorkspaceRenderer)`.
- Add 3 tests (no-options equivalence; loader threading; legacy setter API removed).

### Subsequent Steps

- **Step 9**: Build verify (`tsc --noEmit` on LegalWorkspace + new package + SpaarkeAi surface gate)
- **Step 10**: Test verify (`npm test` in `Spaarke.DailyBriefing.Components` — 5 suites / 26 tests must still pass; new tests bring total to 5 suites / 29 tests)
- **Step 11**: Update TASK-INDEX.md noting Option D as Wave 11 (post-Wave-10/task-024); commit
- **Step 12**: Push to `work/spaarke-daily-update-service-r2`; auto-merge re-evaluates when CI re-runs

### Decisions Made

- **2026-06-18 (Option D session)**: Use Option D (registry-as-composition factory) instead of Option C (per-widget React Context). Reason: SpaarkeAi is a critical core surface that must support N widgets + two-way Assistant⇄Workspace⇄Context flows; Option C scales linearly with widget count, Option D scales O(1). Who: user (explicit authorization).
- **2026-06-18**: NO speculative options added to `LegalWorkspaceSectionRegistryOptions` (no `paneEventBus`/`agentClient`/`contextProvider` until first real consumer). Reason: per CLAUDE.md "Don't add features beyond what the task requires"; the interface is extensible additively when concrete needs arrive. Who: design rationale §5.
- **2026-06-18**: Documentation alignment (pattern doc + architecture docs + ADR) deferred to 3 separate Phase 7b POML tasks (070/071/072) executed post-merge. Reason: keep the implementation PR focused on the refactor; review the documentation holistically without it gating the merge. Who: user request ("include task(s) to review and update related documentation").
- **2026-06-18**: NO changes to `@spaarke/ui-components` public API (e.g., NOT adding `sections` to `WorkspaceRendererProps`). Reason: SpaarkeAi can register a custom RENDERER (a wrapper function over `LegalWorkspaceApp`) via the existing `setDefaultWorkspaceRenderer` slot, which already accepts arbitrary renderers. This preserves the shared-lib boundary. Who: design rationale §3.6.
- **2026-06-18 (R6 coordination)**: Proceed with Option D without coordinating with R6 team. Reason: R6 PR #395 makes ZERO commits to the files Option D touches; R6's Pillar 9 operates on a sibling `WorkspaceWidgetRegistry` at the AI-widgets layer, not `SECTION_REGISTRY` at the LegalWorkspace layer. Who: design rationale §6 + Explore-agent review.

---

## Next Action

**Next Step**: Step 8 — execute the 6-file implementation refactor + 3 unit tests

**Pre-conditions**:
- Design rationale documented ✓
- Phase 7b doc-alignment POMLs created ✓
- User authorization received ✓
- R6 zero-overlap confirmed ✓
- current-task.md updated (this file) ✓

**Key Context**:
- `notes/option-d-registry-as-composition.md` §3 has the exact target file shapes — implement to those shapes
- The implementation MUST preserve `SECTION_REGISTRY` as a top-level export (consumer compatibility) — it just becomes a default-options factory call instead of a literal const
- The legacy setter API (`setLegalWorkspaceDailyBriefingNotificationLoader`) MUST be REMOVED, not deprecated — the whole point is eliminating the band-aid
- After implementation, all 5 Jest 30 suites + 26 tests from R2 must continue to pass (the new tests bring total to ~29)
- After commit + push to `work/spaarke-daily-update-service-r2`, auto-merge on PR #396 re-fires when CI re-runs

**Expected Output**:
- 6 source files modified per notes/option-d-registry-as-composition.md §3
- 3 new unit tests added
- BFF build still green (no impact — only TypeScript files touched)
- Jest suites still green
- 1 commit pushed to PR #396

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session

- Started: 2026-06-18 (continuation from R2 31/36 ship state)
- Focus: Option D architectural refactor (replaces R2 task 002 band-aid)

### Key Learnings

- The user's code-review eye caught the module-mutation band-aid before merge. The orchestration agent's framing ("Option B works, file as debt later") was too narrow. The right answer at architectural-review time is "fix it now if the cost is small relative to the strategic blast radius."
- The pattern (one factory + typed options interface) is the right primitive for N widgets even when only 1 widget needs it today. Adding the pattern doesn't speculatively design future widgets; it just makes adding them cheap when they arrive.
- `notes/option-d-registry-as-composition.md` is the persistent record. Future maintainers don't have to re-derive the alternatives evaluation.
- **Speculative-design discipline**: do NOT add `paneEventBus`/`agentClient`/`contextProvider` to `LegalWorkspaceSectionRegistryOptions` today. Add them when the first concrete consumer needs them. The interface is extensible additively.

### Handoff Notes

If this session is interrupted before Option D implementation completes:

1. Read `notes/option-d-registry-as-composition.md` §3 for the exact target file shapes — those are the canonical implementation plan
2. Read this `current-task.md` "Pending implementation changes" list for the 6 files to modify
3. Read `notes/task-002-blocker.md` for the original architectural analysis that led here
4. Read the existing `src/solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts` to see Option B's current code (the band-aid being removed)
5. Read `src/solutions/SpaarkeAi/src/main.tsx` lines 60-75 + 229-241 to see Option B's setter call (being removed)
6. After implementing: run `npm test` in `src/client/shared/Spaarke.DailyBriefing.Components/` (existing 5 suites / 26 tests should still pass; new tests bring total to ~29)
7. Commit + push to `work/spaarke-daily-update-service-r2`; auto-merge on PR #396 will re-evaluate when CI runs

---

## Quick Reference

### Project Context

- **Project**: `spaarke-daily-update-service-r2`
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)
- **Active PR**: [#396](https://github.com/spaarke-dev/spaarke/pull/396)
- **Companion design rationale**: [`notes/option-d-registry-as-composition.md`](./notes/option-d-registry-as-composition.md)
- **Phase 7b POMLs (post-merge follow-ups)**: tasks 070 (pattern), 071 (architecture docs), 072 (ADR-033)

### Applicable ADRs (Option D specifically)

- **ADR-006** — UI Surface Architecture (Pattern D dual-use baseline; new ADR-033 will reference)
- **ADR-012** — Shared Component Library (per-widget factory pattern Option D builds on; new ADR-033 will reference)
- **ADR-022** — React 19 (the prop-drilling vs context decision; Option D prefers props at this layer)
- **ADR-026** — Code Page Build Standard (no changes; the shrunk standalone host shell still works)

(See `CLAUDE.md` "Applicable ADRs" for the full R2 ADR table; Option D adds ADR-033 as a follow-up via task 072.)

### Knowledge Files Loaded

- `projects/spaarke-daily-update-service-r2/notes/option-d-registry-as-composition.md` — comprehensive design
- `projects/spaarke-daily-update-service-r2/notes/task-002-blocker.md` — original architectural analysis
- R6 worktree at `C:/code_files/spaarke-wt-spaarke-ai-platform-unification-r6` — reviewed for overlap
- The 6 source files listed under "Pending implementation changes" — read in main session before implementation

---

## Recovery Instructions

**To recover context after compaction or new session:**

1. **Quick Recovery**: Read the "Quick Recovery" section above (< 30 seconds)
2. **If more context needed**: Read Active Task and Progress sections
3. **Load design rationale**: `notes/option-d-registry-as-composition.md` §3 (target file shapes) is the canonical implementation plan
4. **Knowledge**: the 6 implementation files are listed under "Pending implementation changes" — read them before editing
5. **Resume**: From the "Next Action" section

**Commands**:
- `/project-continue` — Full project context reload + master sync
- `/context-handoff` — Save current state before compaction
- "where was I?" — Quick context recovery

**For full protocol**: See [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md)

---

*This file is the primary source of truth for active work state. Keep it updated.*
