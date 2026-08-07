# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-07 (parallel wave 031+050+060 closed; review triage pending; next 032)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NONE ACTIVE — clean boundary pending wave review triage. **PARALLEL WAVE 031+050+060 COMPLETE** (031 main-session 5c162e37b; 060 agent 4cb67b286; 050 agent cherry-picked at HEAD). Merged suite 1094/1094. Step 9.5 wave review agent IN FLIGHT — triage its findings on return, fix-commit if needed. Records: POML completion-notes + notes §22 + notes/050-obo-version-endpoint.md. |
| **Next task** | **032 — part-merge endpoint + client wiring** (deps 030 ✅ + 031 ✅) → 033 (provenance seam incl. imported-carrier slice). Also unblocked: 051 (version-history client entry, dep 050 ✅), 061 (CI gate wiring, dep 060 ✅ — consumes fidelity-gate-result.json; coordinate LFS with PR #690). Branch is 41 behind master (routine — merge at next PR). D1-D7 fix tasks still awaiting operator scope approval. |
| **Status** | Wave closed except review triage; pushing |
| **Next Action** | When the wave review agent reports: triage findings → fix commit if warranted → push. Then on "continue": task-execute 032 (Compose-serialized, main session; 051/061 are parallel-eligible agent candidates). |

### Critical Context (3 sentences)
Dev environment now runs render-on-save end-to-end; the assistant-enhancements-r2 session's deploys are
FROZEN per operator option A (r2 re-coordinates at its merge — "when it is ready to merge and deploy
we'll coordinate"). UAT defects D1-D7 are triaged in notes/phase1-deploy-uat.md — D1 (dup records per
save-session) is proven PRE-EXISTING (pre-deploy records exist; all 6 rows share one sprk_graphitemid);
D2 (curly quotes → digit 2 in AI-suggested text) lives in the suggestion pipeline NOT Services/Compose,
and D3 (placement failure) is its likely consequence. NONE were hot-patched (gate constraint honored).

### Standing items
- Operator principle: best fidelity on common cases; rare shapes degrade LOUDLY, never silently.
  UAT evidence: indentation ×84 + paragraph-style ×85 flatten warnings on a REAL NDA → these two
  move to the FRONT of the fidelity-widener backlog (notes §16).
- NEVER delete docxBridge.ts. Commit --no-verify + Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>.
- /conflict-check before every BFF PR. PR #743 overlap on ComposeWorkspace.tsx pending (second merger
  resolves). Publish baseline now **46.94 MB incl. PDBs** (post-master-merge, task 014).
- Corteva NDA (notes/, UNTRACKED — real signed agreement) = corpus row-4 candidate; needs operator
  confidentiality sign-off before committing (task 060 decision point).
- Execution shape per task: implement → slice → commit → Step 9.5 (two background agents on the SHA) +
  clean-worktree publish → fix commit → close-out.
- Client baselines: tsc 28 pre-existing sibling-dist errors; full-jest 10 pre-existing
  stepOperationInterceptor reds (untouched since compose-r5).

### Remaining project phases
Phases 3-7: 030-033 (template part-merge) · 040-042 (PDF intake) · 050-052 (version-history UX — UAT
finding 9 confirms users look for this in-app) · 060/061 (CI fidelity harness — seed candidates: §1.7
fixture + Corteva NDA pending sign-off) · 090 (wrap-up, /test-diet gate; deps 014 ✅ + 027 ✅ + 033/042/052/061).

## Steps completed this task
(none — no active task)

## Files modified this task
(none)

## Decisions
(none — see notes/phase1-deploy-uat.md for 014's record incl. the anti-clobber option-A decision)
