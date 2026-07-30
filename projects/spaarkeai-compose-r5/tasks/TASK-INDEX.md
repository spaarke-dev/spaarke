# Spaarke Compose R5 — Task Index

> **Generated**: 2026-07-29 by `/project-pipeline` → `/task-create`
> **Source**: [`../plan.md`](../plan.md) (WBS) → [`../spec.md`](../spec.md) (FRs/NFRs) → [`../design.md`](../design.md)
> **Governing ADR**: [ADR-049 Compose Shadow Document](../../../.claude/adr/ADR-049-compose-shadow-document.md)
> **Portfolio**: [Project #695](https://github.com/spaarke-dev/spaarke/issues/695) · Epic #421
> **Total**: 22 tasks (6 gate · 5 edit-path ops · 3 lifecycle · 4 concurrency/UX · 3 hardening/cutover · 1 wrap-up)
> **Execution gate**: ⛔ **HELD** — two Phase-0 human gates must clear before implementation (see below).

## Human gates
1. **Task 002 — G1 Dataverse schema (`sprk_composeorigin`)** — ✅ **CLEARED 2026-07-29**: owner created the field. As-built: Authored=`100000000`, Imported=`100000001`, default Imported, null→Imported. See [`../notes/g1-origin-field-asbuilt.md`](../notes/g1-origin-field-asbuilt.md). Task 020 unblocked.
2. **Task 003 — G2 clean-apply spike (R5-D2)** — runs as a normal task (produces the decision note); its output gates task 021 only.

## Task table

| ID | Title | Phase | Status | Rigor | Tier/Effort | Deps | Parallel-safe |
|----|-------|-------|--------|-------|-------------|------|---------------|
| 001 | Confirm R4.5 merge + green baseline | 0 Gate | ✅ | STANDARD | sonnet/high | none | ✅ true |
| 002 | 🔔 G1 Dataverse origin field (`sprk_composeorigin`) — HUMAN GATE | 0 Gate | ✅ | FULL | sonnet/high | none | ✅ true |
| 003 | G2 clean-apply spike (R5-D2 decision) | 0 Gate | ✅ | FULL | opus/xhigh | none | ✅ true |
| 004 | Op-schema extension design (table/acceptRevision/rejectRevision) | 0 Gate | ✅ | FULL | opus/high | none | ✅ true |
| 005 | NumberingComputationEngine reuse decision (extract vs reference) | 0 Gate | ✅ | STANDARD | sonnet/high | none | ✅ true |
| 006 | Reciprocal R4.5 coordination note (optional courtesy) | 0 Gate | ✅ | MINIMAL | sonnet/high | none | ✅ true |
| 010 | G3 alignment applier (`Alignment`→`w:pPrChange`) | 1 Edit-path ops | ✅ | FULL | sonnet/high | 001 | ❌ shared engine |
| 011 | G3 heading/list applier (reuse numbering engine) | 1 Edit-path ops | ✅ | FULL | opus/high | 005,010 | ❌ shared engine |
| 012 | G12 accept/reject-revision single-by-id (ET-2) | 1 Edit-path ops | ✅ | FULL | opus/xhigh | 004 | ❌ shared engine+catalog |
| 013 | G12 accept-all/reject-all batch | 1 Edit-path ops | ✅ | FULL | opus/high | 012 | ❌ shared engine |
| 014 | G4 tables — full tracked structure (L long pole) | 1 Edit-path ops | ✅ | FULL | opus/xhigh | 004 | ❌ shared engine+catalog |
| 020 | G1 origin routing (LoadAsync/SaveAsync + client) | 2 Lifecycle | ✅ | FULL | sonnet/high | 002 | ❌ shared service+workspace |
| 021 | G2 clean-apply implementation | 2 Lifecycle | ✅ | FULL | opus/xhigh | 003 | ❌ shared engine/service |
| 022 | G7 Save-Version / Save-New split-button | 2 Lifecycle | ✅ | FULL | sonnet/high | 020 | ❌ shared service+workspace |
| 030 | G8 external-change refresh + remount banner | 3 Concurrency/UX | 🔲 | FULL | sonnet/high | 020 | ❌ ComposeEditor overlap (031) |
| 031 | G9 comment pane scroll-sync | 3 Concurrency/UX | 🔲 | STANDARD | sonnet/high | none | ❌ ComposeEditor overlap (030) |
| 032 | G11 track-changes-off keeps redlines visible | 3 Concurrency/UX | 🔲 | STANDARD | sonnet/high | none | ❌ shared TrackChangesExtension+toolbar |
| 033 | G5 hyperlinks (authored render + edit op, both paths) | 3 Concurrency/UX | 🔲 | FULL | opus/high | 004 | ❌ shared renderer+catalog+engine |
| 040 | G10 profile re-run (reload + manual button) | 4 Hardening | 🔲 | FULL | sonnet/high | 020 | ❌ shared service |
| 041 | No-regression + publish-size hardening gate | 4 Hardening | 🔲 | STANDARD | sonnet/high | 010,011,012,013,014,020,021,022,030,031,032,033,040 | ❌ aggregates all |
| 042 | Deploy (master-with-R4.5) + operator UAT | 4 Hardening | 🔲 | FULL | sonnet/high | 041 | ❌ deploy |
| 090 | Project wrap-up (/test-diet + review + close) | 4 Wrap-up | 🔲 | FULL | sonnet/high | 042 | ❌ terminal |

Status legend: 🔲 not-started · 🔄 in-progress/retry · ✅ completed · ⛔ blocked.

> **Baseline (task 001, 2026-07-29):** GREEN — 739/739 Compose unit + 208/208 seam + **24/24 byte-diff (8-doc corpus)**; publish **46.70 MB** excl PDBs (ceiling 60). The corpus is 8 docs → **24/24** is the correct no-regression figure (the earlier "28/28" was a stale headline; corrected across spec/plan/POMLs 2026-07-29). R4.5 outputs + docxBridge state confirmed. See `../notes/baseline-verification.md`.

## Dependency DAG (critical path)

```
Phase 0 (parallel — design/spike/gate; 002 & 003 are HUMAN-gated):
  001  002🔔  003🔔  004  005  006

Phase 1 op-schema/engine wave (serial — all share ComposeShadowPatchEngine.cs):
  004 → 012 → 013
  004 → 014                         (G4 tables — the L long pole, schedule LAST)
  001 → 010 → 011  (011 also needs 005)

Phase 2 authored-doc lifecycle (serial — share ComposeService.cs / ComposeWorkspace.tsx):
  002 → 020 → 022
  003 → 021

Phase 3 concurrency/UX:
  020 → 030          031  032        004 → 033
  (030 & 031 both touch ComposeEditor.tsx → run sequentially, not same wave)

Phase 4:
  {all impl} → 041 (no-regression + ≤60MB gate) → 042 (deploy + UAT) → 090 (wrap-up)
```

**Critical path (longest):** 004 → 014 (G4 full tracked tables, L) → 041 → 042 → 090.
**Parallel-lite reality:** this project is serialization-heavy — almost every implementation task mutates a shared Compose file (`ComposeShadowPatchEngine.cs`, `ComposeService.cs`, `ComposeWorkspace.tsx`, the op catalog), so `parallel-safe:false` dominates by design (consistent with `projects/INDEX.md`: "parallel-safe:false on ALL Compose tasks"). Run `/conflict-check` before every BFF PR.

## Parallel Execution Plan (waves)

| Wave | Tasks | Prereq | Concurrency | goal-eligible |
|------|-------|--------|-------------|---------------|
| W0 | 001, 003, 004, 005, 006 (+ **002 human-gated**) | none | up to 5 agents (separate note files; no shared source) | **NO** — spikes/design/human-gate, high ambiguity |
| W1a | 010 | 001 | 1 (shared engine) | NO |
| W1b | 011 | 005,010 | 1 (shared engine) | NO |
| W1c | 012 → 013 | 004 (012); 012 (013) | 1 serial (shared engine+catalog) | NO — opus/xhigh reconciliation judgment |
| W1d | 014 | 004 | 1 (shared engine+catalog) | NO — L long pole, novel tracked-table OOXML |
| W2 | 020 → 022 ; 021 | 002 (020); 020 (022); 003 (021) | serial per shared file | NO — human-gated + opus/xhigh |
| W3 | 030 ; 031 ; 032 ; 033 | 020 (030); 004 (033) | 030/031 serial (editor overlap); 032/033 separable | NO — shared client + downstream NFR-09 |
| W4 | 041 → 042 → 090 | see DAG | serial | NO — deploy/irreversible + wrap-up |

**`/goal` eligibility: NO for every wave.** Rationale: two human gates (002/003), opus/xhigh reconciliation-judgment tasks (012/014/021), shared-file serialization, a downstream behavioral consumer (NFR-09 analysis-hub-r1), and a deploy/irreversible tail. This project wants per-task operator checkpoints, not an auto-loop. Step 9.5 (code-review + adr-check) runs unconditionally on every BFF/test-modifying task regardless.

## Coordination (BINDING — see [`../notes/COORDINATION-with-r4.5.md`](../notes/COORDINATION-with-r4.5.md) + spec §Coordination)
- **R4.5 (merged):** rebase 020/022/040 onto post-R4.5 `ComposeService.cs`; rebase 020/022/030 onto post-R4.5 `ComposeWorkspace.tsx`. **NEVER delete `docxBridge.ts`.** Reuse (no fork): `NumberingComputationEngine` (011), `CitationResolver` (040), transient-mount identity (022). Deploy from master-with-R4.5.
- **analysis-hub-r1 (NFR-09):** 020/021/022/040 + G12 (012/013) must not regress its reopen-restore / retirement parity; shared `Spaarke.Compose.Components` + `ConversationPane` routing/e2e (030/031/032/033).
- Run `/conflict-check` before EVERY BFF PR (overlaps compose-r1/r2/r3 + ai-architecture-redesign-r2).

## How to execute
Per root CLAUDE.md §4, each task runs via **`task-execute`** (never read the POML and implement manually). Start: clear the two Phase-0 human gates (002 schema approval, 003 spike decision), then `task-execute 001`. Sequence by the DAG above.
