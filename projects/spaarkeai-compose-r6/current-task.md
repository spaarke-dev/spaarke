# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-06 (by context-handoff, post-027 close-out — clean boundary; PHASE 1 + PHASE 2 FULLY COMPLETE; next stop is the 014 HUMAN GATE)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NONE ACTIVE — clean boundary. **027 COMPLETE** — and with it EVERY task on the render-on-save critical path except the deploy gate: 001/002/003/004 (Phase 0) · 020/011/021-026 (Phase 2) · 010/012/013/027 (Phase 1 + fidelity DoD) all ✅. |
| **Next task** | **014 — Deploy + UAT gate (render-on-save + fidelity) — BFF + sprk_spaarkeai together** (`tasks/014-*.poml`; deps 013 ✅ + 027 ✅ SATISFIED). ⚠️ **HUMAN-IN-LOOP** — deployment is not safe to run unattended (project Phase-1 gating note + root §6). DO NOT auto-execute on a bare "continue": present the deploy plan and get explicit operator go-ahead. |
| **Status** | 027 closed at `6b00765e9`; to be pushed |
| **Next Action** | Push, then STOP at the human gate: on "continue", read `tasks/014-*.poml`, present the deploy/UAT plan (incl. the pre-deploy master merge + /conflict-check), and WAIT for operator confirmation. |

### Critical Context (3 sentences)
027 commits: f7bd052f0 (ComposeFidelityRoundTripSeamTests — 11 wire slices, self-proving driver, goldens
from manifest §1.5, ZERO adapter gaps; NEW multi-author-redline-synthetic.docx corpus fixture §1.7 filling
025-F6; T-2 narrative refresh) + 6b00765e9 (review strengthenings F1-F6). Suites: full Compose 1020/1020
ZERO reds; publish 46.91 MB flat; zero production-code changes. The adr-check agent hit the session usage
limit mid-run — its axes were verified inline with evidence and recorded transparently in the POML note.

### 014 PRE-DEPLOY OBLIGATIONS (from the standing items + notes §20)
1. **Merge origin/master first** (~67 behind; Crypto.Xml HIGH patched there) + re-run /conflict-check
   (Services/Compose most-contested; last check clean 2026-08-06).
2. **BFF + `sprk_spaarkeai` deploy TOGETHER** (atomic window): an old client on the new server drops
   separate-comments LOUDLY (`comments-ignored`) — acceptable only within the window.
3. Ops notes (notes §20): dashboards should chart the `TRANSITIONAL op-log save shape` Warning decay
   (its hitting zero = the signal to delete the transitional path + engine/count-gate); watch save
   latency on very large docs (post-save re-projection) at UAT.
4. UAT focus: the NDA end-to-end (load → edit → save → no 422 → reopen), imported-doc redlines in real
   Word, clean-save byte-identity (FR-06a), comment round-trip, version history retrievability (002's
   live v3-after-v4 human gate is still open).

### Remaining project phases after 014
Phases 3-7: 030-033 (template part-merge) · 040-042 (PDF intake) · 050-052 (version history UX) ·
060/061 (CI fidelity harness — seeds from the corpus incl. the new §1.7 fixture) · 090 (wrap-up,
/test-diet gate). Check TASK-INDEX deps as each unblocks.

### Standing items
- Operator principle: best fidelity on common cases; rare shapes degrade LOUDLY, never silently.
- NEVER delete docxBridge.ts. Commit --no-verify + Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>.
- Execution shape per task: implement → slice → commit → Step 9.5 (two background agents on the SHA) +
  clean-worktree publish (46.91 MB baseline) → fix commit → close-out.
- Client baselines: tsc 28 pre-existing sibling-dist errors; full-jest 10 pre-existing
  stepOperationInterceptor reds (untouched since compose-r5).
- Sign-offs R4/R5 RESOLVED to warned baselines; fidelity-widener backlog notes §16.

## Steps completed this task
(none — no active task)

## Files modified this task
(none)

## Decisions
(none — see notes §20 for 027's record)
