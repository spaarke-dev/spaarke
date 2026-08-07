# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-07 (032 starting; agents 051+061+wave-review in flight)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NONE ACTIVE — wave-2 (032+051+061) CLOSED at a1f005033 (suite 1104/1104; the only reds all day = the known flaky ComposeServiceCreateOnSaveTests class). Step 9.5 review agent for 032+051 IN FLIGHT — triage on return (fix-commit if needed), record in notes §23. |
| **Next task** | **033 — part-merge provenance seam tests** (deps 030-032 ✅; MUST include an imported-carrier wire slice per §21) → then 052 (remaining Phase-5 client polish/picker if scoped), 040-042 (PDF intake), 090 wrap-up (needs 033/042/052/061✅). Also outstanding: D1-D7 fix-task scope approval; branch ~45 behind master (reconcile at next PR + /conflict-check; PR #690 fidelity-gate double-run dedup note). Phase 3 (030-033) is one task from done; Phases 5+6 DONE product-side. |
| **Status** | Pushing close-out; review triage pending |
| **Next Action** | On review return: triage + fix-commit + push. Then on "continue": task-execute 033 (Compose-serialized, main session or agent). |

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
