# Current Task — Spaarke Compose R6

> **Last Updated**: 2026-08-06 (post-014 close-out — PHASE 1 DEPLOYED + UAT PASSED; Phases 3/4/5/6 all unblocked)
> **Recovery**: Read "Quick Recovery" first; full state below.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | NONE ACTIVE — clean boundary. **014 COMPLETE**: render-on-save cutover LIVE on dev (BFF `spaarke-bff-dev` + `sprk_spaarkeai`, deployed SHA `d01007a38`, hash-verified, atomic window held). UAT on the operator's real Corteva signed NDA PASSED — no 422, redlines land, versions accumulate, prior-version-intact confirmed in Word (002's live gate closed). |
| **Next task** | Operator choice — all now unblocked: **030** (part-merge engine, opus/FULL), **040** (PDF intake, opus/FULL), **050** (OBO version-history endpoint, opus/FULL), **060** (CI fidelity harness, sonnet/FULL). ALSO on the table: UAT defect fix tasks D1-D7 (see notes/phase1-deploy-uat.md defect register) — operator may want D1 (duplicate sprk_document records) + D2 (quote→`2` mangling) prioritized before new phases. |
| **Status** | 014 closed; deploy + UAT record at notes/phase1-deploy-uat.md |
| **Next Action** | On "continue": ask/confirm which of 030/040/050/060 (or a D-fix task) to start, then task-execute it. Task creation for D1-D7 fixes = scope addition → operator approval first. |

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
