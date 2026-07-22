# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-07-22 (pipeline init)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | ✅ **Phases 0–2 COMPLETE** (020/021/022/024 done; **023 deferred→031**). Next: Phase 3 Patch Engine (030–035) |
| **Step** | — |
| **Status** | Phase 2 done (48/48 client tests green) — Phase 3 startable |
| **Next Action** | Run Phase 3: 030 (ComposeShadowPatchEngine core, build on the 005 spike's SpikeOpenXmlApplier) → 031 (structural ops — server + **client onStructuralStep wiring** per re-sequence) → 032 (retire writers, gated) · 033 born-in-editor · 034 seam+corpus · 035 deploy. Then 023 runs after 031. Autonomous. |

> **023 re-sequenced (Path A, 2026-07-22)**: its coverage check found `collectEditedParagraphs` handles whole-paragraph delete/merge (`{paraId,text:''}` sentinel — a real UAT-corruption guard) with no equivalent in the 020/022 interceptor (structural steps deferred). 023 now deps on **031**; 031's scope extended to wire the client `onStructuralStep`→structural-op emission. No regression window. Analysis: `notes/task-023-coverage-gap.md`.

> **Pre-existing tech debt flagged 2026-07-22** (NOT R4): ADR-007 GraphIsolation arch test fails on baseline — Graph* types leak in `Services/Communication/**` + `Api/Office/Errors` + `Infrastructure/Errors` (email/messaging subsystems). Zero Compose involvement. Out of R4 scope; flag to owner.

### Files Modified This Session (Wave W0a)
- `.claude/adr/ADR-049-compose-shadow-document.md` (new — task 001) + `.claude/adr/INDEX.md` (entry)
- `tests/fixtures/compose-corpus/` — 3 LFS sample docs + `corpus-manifest.md` (task 002)
- `src/server/api/Sprk.Bff.Api/Services/Compose/Operations/ComposeOperation.cs` (new — task 003)
- `src/client/shared/Spaarke.Compose.Components/src/types/compose-operations.ts` (new) + `compose-contracts.ts`/`index.ts` (re-export) + tests (task 003)
- `notes/task-002-corpus-deviations.md`, `notes/task-003-operation-schema-decisions.md`

### Critical Context
R4 is a MISSION-CRITICAL hard-replace of the Compose save layer with a Shadow Document Architecture. **Phase 0 (tasks 001–006) is a proof gate** that MUST be green before any old-path deletion (023/032/060). Wave W0a done: ADR-049 codified (invariants I-1…I-7, D1–D5, Path-B amendment of R3 paragraph-diff); operation schema built (10 ops, client+server, round-trips green, publish 46.11 MB); corpus staged as LFS. **⚠️ Corpus deviation finding (task 002): the 3 sample docs carry FEWER worst-offender features than the WBS assumed (CIPO doc is track-changes-clean as saved; only its footer page-number SDT is real SDT coverage). Owner worst-offenders needed in Phase 0 to fully exercise NFR-01 — see `notes/task-002-corpus-deviations.md`. This affects the 004 harness + 005 spike + 006 gate evidence bar.**

---

## Active Task (Full Details)

| Field | Value |
|-------|-------|
| **Task ID** | none |
| **Task File** | — (first task: `tasks/001-shadow-document-adr.poml`) |
| **Title** | — |
| **Phase** | Ready to start Phase 0 (Gate) |
| **Status** | none |
| **Started** | — |

---

## Progress

### Completed Steps
*No steps completed yet*

### Current Step
*No task started — project just initialized.*

### Files Modified (All Task)
*No files modified yet*

### Decisions Made
- 2026-07-22: Cutover = hard replace (owner-confirmed); Phase 0 proof gate is the pre-commit safety net.
- 2026-07-22: D1–D5 locked; anchor = `(paraId, runIndex, run-local-offset)`.

---

## Next Action

**Next Step**: Start task 001 (Shadow-Document ADR) via `task-execute`.

**Pre-conditions**:
- On branch `work/spaarkeai-compose-r4` (already checked out).
- Read `spec.md`, `design.md`, and `notes/as-built-inventory.md` before implementation tasks.

**Key Context**:
- Phase 0 gate (task 006) blocks all cutover/deletion tasks.
- BFF=Y — every BFF task: Placement Justification + publish-size + seam slice + `/conflict-check`.

**Expected Output**:
- Task 001 produces the R4 Shadow-Document ADR (`.claude/adr/`) + the ADR-Tension Path-B amendment of the R3 paragraph-diff decision.

---

## Blockers

**Status**: None

---

## Session Notes

### Current Session
- Started: 2026-07-22 (pipeline init)
- Focus: Project initialized via `/project-pipeline`.

### Key Learnings
- `Services/Compose/` overlaps 4 sibling projects — `/conflict-check` before every BFF PR.
- Consume `Services/Ai/PublicContracts/` seams; NO fork of `Services/Ai/`.

### Handoff Notes
*No handoff notes*

---

## Quick Reference

### Project Context
- **Project**: spaarkeai-compose-r4
- **Project CLAUDE.md**: [`CLAUDE.md`](./CLAUDE.md)
- **Task Index**: [`tasks/TASK-INDEX.md`](./tasks/TASK-INDEX.md)

### Applicable ADRs
- ADR-013: AI facade — no AI internals in `Services/Compose/`
- ADR-007: no `Microsoft.Graph` above `SpeFileStore`
- ADR-038: seam DoD; banned mock/DI/ctor tests
- ADR-039/040: engine frozen; no new AI dispatch

### Knowledge Files Loaded
*Loaded per-task by task-execute from each POML's `<knowledge>` section.*

---

## Recovery Instructions

1. **Quick Recovery**: Read the "Quick Recovery" section above.
2. **Load task file**: `tasks/{task-id}-*.poml`.
3. **Load knowledge files**: From the task's `<knowledge>` section.
4. **Resume**: From "Next Action".

**Commands**: `/project-continue` · `/context-handoff` · "where was I?"

---

*This file is the primary source of truth for active work state. Keep it updated.*
