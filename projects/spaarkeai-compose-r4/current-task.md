# Current Task State — Spaarke Compose R4

> **Last Updated**: 2026-07-22 (by context-handoff, pre-compaction)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarkeai-compose-r4` @ `3e0abc7cc` (12 commits unpushed; all work committed locally).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project** | spaarkeai-compose-r4 (Shadow Document Architecture — hard-replace of Compose save layer) |
| **Progress** | **27 / 36 tasks done. Phases 0–5 COMPLETE.** |
| **Status** | ⏸ **Autonomous run paused — Phase 6 gated on OWNER DECISIONS** |
| **Next Action** | Get owner input on the 3 items below, then run Phase 6: `060 → 061 → 062 → 063 → 090`. |

### 🔔 OWNER DECISIONS NEEDED to unblock Phase 6 (nothing else is blocking)
1. **Task 036 — push-to-Word annotations** (`DocxAnnotationWriter`, text-anchored): **Path B** (migrate to op-log) **or Path C** (retire the feature — likely redundant now that R4 emits native Word tracked changes). See `notes/task-032-pushannotations-scope.md`.
2. **Task 037 — born-in-editor tables** (closed op-schema has no table op): **Path B** (extend the FR-11 op schema to author `w:tbl`, retire the renderer) **or Path C** (make tables import-only). See `notes/task-033-table-operation-gap.md`.
3. **Deploy authorization**: task **035** (dev deploy — verified, held) + task **062** (full R4 deploy + CIPO UAT). Outward actions, not taken autonomously.

Both 036 + 037 block **Success Criterion 7** ("one byte-author"). Interim: push-annotations on `DocxAnnotationWriter`, born-in-editor on `ComposeDocumentRenderer` — documented §6.5 exceptions, **zero regression**.

### Critical Context (1-3 sentences)
R4's save-path hard-replace is LIVE: all saves route through the single `ComposeShadowPatchEngine` (op-log → surgical `w:ins`/`w:del`/`w:comment`, zero write-path text-search). Two shipped constructs the closed 10-op schema can't express (push-annotations comments, born-in-editor tables) were kept working via documented §6.5 exceptions and deferred to owner Path-B/C decisions (036/037). Publish 46–47.5 MB compressed (≤60); 552/552 Compose tests green.

---

## Task Ledger (source of truth: `tasks/TASK-INDEX.md`)

**DONE (27):** 001–006 (Phase 0 gate 🟢), 010–013 (ingest), 020–024 (capture), 030/031/032 (engine + structural + save-path cutover), 023 (cleanup), 034 (seam proof), 040–042 (AI anchoring), 050–054 (concurrency + import).

**REMAINING (9), all gated:**
| Task | State | Gated on |
|---|---|---|
| 035 | verified (46.13 MB); Azure dev deploy held | owner deploy auth |
| 033 | deferred → folds into 037 | 037 |
| 036 | 🔔 deferred | owner Path B/C (push-annotations) |
| 037 | 🔔 deferred | owner Path B/C (born-in-editor tables) |
| 060 | blocked | 036, 037 (hard-replace completion / remove mammoth) |
| 061 | blocked | 060 (corpus proof + size + CVE + NetArch) |
| 062 | blocked | 060, 061 + deploy auth (full deploy + CIPO UAT) |
| 063 | blocked | 062 (flagship gate — needs Criterion 7 = 036+037) |
| 090 | blocked | 063 (wrap-up + /test-diet) |

## Key decisions made this run
- **Patch engine = `DocumentFormat.OpenXml`** (zero new package); Docxodus REJECTED (task 005 A/B; `notes/patch-engine-ab-decision.md`).
- **ADR-049** authored (invariants I-1…I-7, D1–D5, Path-B amendment of R3 paragraph-diff).
- **3 WBS gaps resolved**: 023 re-sequenced→031 (structural capture); 036 created (push-annotations, task-032 Path A); 037 created (born-in-editor tables, task-033).
- Offset space = **editor-visible run flatten** (task 011 → engine 030 consumes the same).

## Health
- Every phase build-verified + gates-clean + committed per wave. 552/552 Compose tests green; full BFF suite 8920/8920 baseline.
- Publish 46.13 MB compressed (Release; ↓ from 49.63 baseline via 032 deletion). Only HIGH CVE = pre-existing `System.Security.Cryptography.Xml` transitive (R4 added zero packages).
- Pre-existing ADR-007 GraphIsolation arch-test failure lives in `Services/Communication/**` + `Api/Office` — **NOT Compose, out of R4 scope** (flag for a separate ticket).

## How to resume after compaction
1. Read this Quick Recovery. 2. `tasks/TASK-INDEX.md` for the full status grid. 3. For Phase 6, first get the 3 owner decisions above. 4. Once 036/037 paths chosen: execute 036 + 037 (each POML has `<owner-decision-required>` + steps), then 060→061→062→063→090 via `task-execute`. 5. Deferred-decision analyses: `notes/task-032-pushannotations-scope.md`, `notes/task-033-table-operation-gap.md`.

## Portfolio
[Project #679](https://github.com/spaarke-dev/spaarke/issues/679) · Tasks Completed 27/36 · 12 commits unpushed on `work/spaarkeai-compose-r4` (push when ready).

---

## Recovery Instructions
`/project-continue` (full reload) · "where was I?" (quick). Full protocol: [docs/procedures/context-recovery.md](../../docs/procedures/context-recovery.md).

*Primary source of truth for active work state. All 27 completed tasks are committed; nothing uncommitted except this file.*
